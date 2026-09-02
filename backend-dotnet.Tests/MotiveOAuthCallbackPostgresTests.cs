using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Security;
using Opstrax.Api.Services;
using Opstrax.Api.Services.Connectors;

namespace Opstrax.Tests;

// These tests exercise the real callback, row locks, transactions, audit writes and
// encrypted persistence against PostgreSQL. Provider HTTP is a controlled double:
// a pass proves callback behavior, NOT Motive connectivity, G2A, or certification.
// Every run owns one random schema; no existing application tables are modified.
[Trait("Category", "Integration")]
public sealed class MotiveOAuthCallbackPostgresTests
{
    private const string AccessToken = "motive-sdet-access-token";
    private const string RefreshToken = "motive-sdet-refresh-token";

    [Theory]
    [InlineData("valid", StatusCodes.Status200OK)]
    [InlineData("missing_cookie", StatusCodes.Status409Conflict)]
    [InlineData("mismatched_cookie", StatusCodes.Status409Conflict)]
    [InlineData("other_tenant", StatusCodes.Status409Conflict)]
    [InlineData("other_actor", StatusCodes.Status409Conflict)]
    [InlineData("other_integration", StatusCodes.Status409Conflict)]
    [InlineData("unprivileged", StatusCodes.Status403Forbidden)]
    public async Task BrowserPreflight_IsPrincipalBoundAndNonConsuming(string scenario, int expectedStatus)
    {
        await using var fixture = await Fixture.CreateAsync();
        var runtime = fixture.Runtime((_, _, _) => throw new InvalidOperationException("Preflight must not contact the provider."));
        var state = await fixture.SeedPendingAsync(runtime);
        var before = (await fixture.IntegrationAsync())["configJson"]?.ToString();

        var result = await runtime.InvokePreflightAsync(state,
            companyId: scenario == "other_tenant" ? 8 : 7,
            actorId: scenario == "other_actor" ? 14 : 13,
            integrationId: scenario == "other_integration" ? 12 : 11,
            browserCookie: scenario == "missing_cookie" ? ""
                : scenario == "mismatched_cookie" ? "another-browser" : null,
            canManage: scenario != "unprivileged");

        Assert.Equal(expectedStatus, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        if (expectedStatus == StatusCodes.Status200OK)
        {
            var envelope = Assert.IsType<ApiResponse<object>>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
            Assert.True(envelope.Success);
            var payload = JsonSerializer.SerializeToElement(envelope.Data);
            Assert.Single(payload.EnumerateObject());
            Assert.True(payload.GetProperty("ready").GetBoolean());
        }
        Assert.Empty(runtime.HttpObservations);
        Assert.Equal(before, (await fixture.IntegrationAsync())["configJson"]?.ToString());
        Assert.Equal(0, await fixture.AuditCountAsync("integration.oauth.callback.claimed"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonce-from-a-different-browser")]
    public async Task MissingOrMismatchedBrowserCookie_RejectsBeforeClaimOrProviderExchange(string cookie)
    {
        await using var fixture = await Fixture.CreateAsync();
        var runtime = fixture.Runtime((_, _, _) => throw new InvalidOperationException("Provider must not be called."));
        var state = await fixture.SeedPendingAsync(runtime);

        AssertRedirect(await runtime.InvokeAsync(state, browserCookie: cookie), "browser_mismatch");

        Assert.Empty(runtime.HttpObservations);
        Assert.Equal(0, await fixture.AuditCountAsync("integration.oauth.callback.claimed"));
        var config = runtime.Registry.DecryptConfig((await fixture.IntegrationAsync())["configJson"]);
        Assert.Equal("authorization_pending", config["oauthStatus"]);
        Assert.NotNull(config["oauthStateHash"]);
    }

    [Fact]
    public async Task SuccessfulCallback_PerformsProviderHttpOutsideTransactions_AndPersistsEncryptedAccessOnly()
    {
        await using var fixture = await Fixture.CreateAsync();
        var runtime = fixture.Runtime((_, request, _) => Task.FromResult(ProviderOk(request)));
        var state = await fixture.SeedPendingAsync(runtime);

        var result = await runtime.InvokeAsync(state);

        AssertRedirect(result, "connected");
        Assert.Equal(10, runtime.HttpObservations.Count); // one token exchange + nine scope reads
        Assert.All(runtime.HttpObservations, observation => Assert.False(observation.HasAmbientTransaction));
        var row = await fixture.IntegrationAsync();
        Assert.Equal("Connected", row["status"]);
        Assert.Equal(true, row["lastTestOk"]);
        var stored = row["configJson"]!.ToString()!;
        Assert.DoesNotContain(AccessToken, stored, StringComparison.Ordinal);
        Assert.DoesNotContain(RefreshToken, stored, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(stored);
        Assert.StartsWith("enc:", document.RootElement.GetProperty("accessToken").GetString(), StringComparison.Ordinal);
        var config = runtime.Registry.DecryptConfig(stored);
        Assert.Equal(AccessToken, config["accessToken"]);
        Assert.Null(config["refreshToken"]);
        Assert.Null(config["oauthStateHash"]);
        Assert.Equal("verified", config["oauthStatus"]);
        Assert.Equal(1, await fixture.AuditCountAsync("integration.oauth.verified"));
    }

    [Fact]
    public async Task ConcurrentDuplicateCallback_ClaimsStateOnce_AndExchangesExactlyOneCode()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tokenEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseToken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokenExchanges = 0;
        async Task<HttpResponseMessage> Response(Database _, HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath == "/oauth/token")
            {
                Interlocked.Increment(ref tokenExchanges);
                tokenEntered.TrySetResult();
                await releaseToken.Task.WaitAsync(ct);
            }
            return ProviderOk(request);
        }
        var firstRuntime = fixture.Runtime(Response);
        var secondRuntime = fixture.Runtime(Response);
        var state = await fixture.SeedPendingAsync(firstRuntime);
        var first = firstRuntime.InvokeAsync(state);
        try
        {
            await tokenEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // The first callback has committed its claim but is waiting on the
            // provider. Another instance must reject the same state immediately.
            var duplicate = await secondRuntime.InvokeAsync(state).WaitAsync(TimeSpan.FromSeconds(10));
            AssertRedirect(duplicate, "invalid_state");
            Assert.Equal(1, Volatile.Read(ref tokenExchanges));
        }
        finally
        {
            releaseToken.TrySetResult();
        }
        AssertRedirect(await first.WaitAsync(TimeSpan.FromSeconds(10)), "connected");
        Assert.Equal(1, tokenExchanges);
        Assert.Empty(secondRuntime.HttpObservations);
        Assert.Equal(1, await fixture.AuditCountAsync("integration.oauth.callback.claimed"));
        Assert.Equal(1, await fixture.AuditCountAsync("integration.oauth.verified"));
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("permission_removed")]
    [InlineData("entitlement_removed")]
    public async Task RevokedInitiatorAuthority_FailsBeforeAnyProviderRequest(string revocation)
    {
        await using var fixture = await Fixture.CreateAsync();
        var runtime = fixture.Runtime((_, _, _) => throw new InvalidOperationException("Provider must not be called."));
        var state = await fixture.SeedPendingAsync(runtime);
        var statement = revocation switch
        {
            "inactive" => "UPDATE users SET status='Inactive' WHERE id=13",
            "permission_removed" => "UPDATE users SET permissions_json='[]'::jsonb WHERE id=13",
            _ => "UPDATE tenant_entitlements SET enabled=false WHERE company_id=7",
        };
        await fixture.Db.ExecuteAsync(statement);

        AssertRedirect(await runtime.InvokeAsync(state), "authorization_revoked");

        Assert.Empty(runtime.HttpObservations);
        var config = runtime.Registry.DecryptConfig((await fixture.IntegrationAsync())["configJson"]);
        Assert.Equal("authorization_revoked", config["oauthStatus"]);
        Assert.Null(config["oauthStateHash"]);
        Assert.False(config.TryGetValue("accessToken", out var token) && !string.IsNullOrEmpty(token));
        Assert.Equal(1, await fixture.AuditCountAsync("integration.oauth.authorization_revoked"));
    }

    [Fact]
    public async Task DisconnectDuringExchange_InvalidatesGeneration_AndCannotResurrectCredentials()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tokenEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseToken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = fixture.Runtime(async (_, request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath == "/oauth/token")
            {
                tokenEntered.TrySetResult();
                await releaseToken.Task.WaitAsync(ct);
            }
            return ProviderOk(request);
        });
        var state = await fixture.SeedPendingAsync(runtime);
        var callback = runtime.InvokeAsync(state);
        try
        {
            await tokenEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // This is the same generation/credential invalidation performed by
            // disconnect; an independent DB connection must not wait on HTTP.
            await fixture.Db.ExecuteAsync(
                "UPDATE integrations SET operation_generation=operation_generation+1,status='Disconnected',config_json='{}'::jsonb WHERE id=11")
                .WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            releaseToken.TrySetResult();
        }

        AssertRedirect(await callback.WaitAsync(TimeSpan.FromSeconds(10)), "invalidated");
        var row = await fixture.IntegrationAsync();
        Assert.Equal("Disconnected", row["status"]);
        Assert.Equal("{}", row["configJson"]?.ToString());
        Assert.Equal(2L, Convert.ToInt64(row["operationGeneration"]));
        Assert.Equal(0, await fixture.AuditCountAsync("integration.oauth.verified"));
    }

    [Theory]
    [InlineData("/oauth/token")]
    [InlineData("/v1/eld_devices")]
    public async Task ActorRevokedDuringProviderHttp_CannotPersistAnOtherwiseSuccessfulAuthorization(string pausePath)
    {
        await using var fixture = await Fixture.CreateAsync();
        var requestEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = fixture.Runtime(async (_, request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath == pausePath)
            {
                requestEntered.TrySetResult();
                await releaseRequest.Task.WaitAsync(ct);
            }
            return ProviderOk(request);
        });
        var state = await fixture.SeedPendingAsync(runtime);
        var callback = runtime.InvokeAsync(state);
        try
        {
            await requestEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await fixture.Db.ExecuteAsync("UPDATE users SET status='Inactive' WHERE id=13")
                .WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            releaseRequest.TrySetResult();
        }

        AssertRedirect(await callback.WaitAsync(TimeSpan.FromSeconds(10)), "authorization_revoked");
        var row = await fixture.IntegrationAsync();
        Assert.Equal("Error", row["status"]);
        Assert.Equal(false, row["lastTestOk"]);
        var config = runtime.Registry.DecryptConfig(row["configJson"]);
        Assert.Equal("authorization_revoked", config["oauthStatus"]);
        foreach (var credential in new[] { "accessToken", "refreshToken", "tokenType", "tokenExpiresAt", "oauthStateHash", "verifiedScopes" })
            Assert.Null(config[credential]);
        Assert.All(runtime.HttpObservations, observation => Assert.False(observation.HasAmbientTransaction));
        Assert.Equal(1, await fixture.AuditCountAsync("integration.oauth.authorization_revoked"));
        Assert.Equal(0, await fixture.AuditCountAsync("integration.oauth.verified"));
    }

    [Theory]
    [InlineData("denied", "denied", "integration.oauth.denied")]
    [InlineData("exchange_failed", "token_exchange_failed", "integration.oauth.token_exchange_failed")]
    [InlineData("probe_failed", "scope_verification_failed", "integration.oauth.scope_verification_failed")]
    [InlineData("exchange_oversized", "token_exchange_failed", "integration.oauth.token_exchange_failed")]
    [InlineData("probe_oversized", "scope_verification_failed", "integration.oauth.scope_verification_failed")]
    public async Task UnsuccessfulCallback_ClearsOldAndNewCredentials_AndAuditsTheOutcome(
        string failure, string expectedOutcome, string expectedAudit)
    {
        await using var fixture = await Fixture.CreateAsync();
        var runtime = fixture.Runtime((_, request, _) => Task.FromResult(
            failure == "exchange_oversized" && request.RequestUri!.AbsolutePath == "/oauth/token"
                || failure == "probe_oversized" && request.RequestUri!.AbsolutePath == "/v1/eld_devices"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new MotiveStreamingFixture(new MotiveReadFixture([], unending: true)),
                }
                : failure == "exchange_failed" && request.RequestUri!.AbsolutePath == "/oauth/token"
                ? Json(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}")
                : failure == "probe_failed" && request.RequestUri!.AbsolutePath == "/v1/eld_devices"
                    ? Json(HttpStatusCode.Forbidden, "{\"error\":\"insufficient_scope\"}")
                    : ProviderOk(request)));
        var state = await fixture.SeedPendingAsync(runtime, retainOldTokens: true);

        AssertRedirect(await runtime.InvokeAsync(state, failure == "denied" ? "access_denied" : null), expectedOutcome);

        var config = runtime.Registry.DecryptConfig((await fixture.IntegrationAsync())["configJson"]);
        foreach (var credential in new[] { "accessToken", "refreshToken", "tokenType", "tokenExpiresAt", "oauthStateHash" })
            Assert.Null(config[credential]);
        Assert.Equal(1, await fixture.AuditCountAsync(expectedAudit));
        Assert.Equal(0, await fixture.AuditCountAsync("integration.oauth.verified"));
        if (failure == "denied") Assert.Empty(runtime.HttpObservations);
        Assert.All(runtime.HttpObservations, observation => Assert.False(observation.HasAmbientTransaction));
    }

    private static void AssertRedirect(IResult result, string outcome) =>
        Assert.Equal($"https://frontend.example.test/integrations?motiveOAuth={outcome}",
            Assert.IsType<RedirectHttpResult>(result).Url);

    private static HttpResponseMessage ProviderOk(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath == "/oauth/token"
            ? Json(HttpStatusCode.OK,
                $$"""{"access_token":"{{AccessToken}}","refresh_token":"{{RefreshToken}}","token_type":"Bearer","expires_in":3600}""")
            : Json(HttpStatusCode.OK, "{\"data\":[]}");

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _schema = $"motive_callback_{Guid.NewGuid():N}";
        private readonly IDataProtectionProvider _protection = new EphemeralDataProtectionProvider();
        private readonly IDataKeyProvider _keys = new TestKeyProvider();
        private readonly Database _owner;
        private readonly IConfiguration _configuration;
        public Database Db { get; }

        private Fixture(string connectionString)
        {
            var ownerConfiguration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = connectionString,
                    ["ConnectionStrings:SystemConnection"] = connectionString,
                }).Build();
            _owner = new Database(ownerConfiguration);
            var scoped = new NpgsqlConnectionStringBuilder(connectionString)
            {
                SearchPath = _schema,
                Pooling = false,
            };
            _configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = scoped.ConnectionString,
                    ["ConnectionStrings:SystemConnection"] = scoped.ConnectionString,
                    ["Rls:EnforceTenantContext"] = "false",
                    ["Motive:ClientId"] = "sdet-client-id",
                    ["Motive:ClientSecret"] = "sdet-client-secret",
                    ["PUBLIC_API_URL"] = "https://api.example.test",
                    ["PUBLIC_APP_URL"] = "https://frontend.example.test",
                }).Build();
            Db = new Database(_configuration);
        }

        public static async Task<Fixture> CreateAsync()
        {
            // Never silently use a developer's historical fallback or a remote DB.
            // The caller explicitly selects a disposable local PostgreSQL database.
            var configured = Environment.GetEnvironmentVariable("OPSTRAX_TEST_DB");
            if (string.IsNullOrWhiteSpace(configured))
                throw new InvalidOperationException("Set OPSTRAX_TEST_DB explicitly to a disposable local PostgreSQL database for Motive callback tests.");
            var connection = new NpgsqlConnectionStringBuilder(configured);
            if (connection.Host is not ("127.0.0.1" or "localhost" or "::1"))
                throw new InvalidOperationException("Motive callback tests refuse remote PostgreSQL hosts.");
            var fixture = new Fixture(connection.ConnectionString);
            await fixture._owner.ExecuteAsync($"CREATE SCHEMA \"{fixture._schema}\"");
            try
            {
                await fixture.Db.ExecuteAsync(
                    """
                    CREATE TABLE companies(id BIGINT PRIMARY KEY, entitlement_policy_mode TEXT NOT NULL);
                    CREATE TABLE roles(id BIGINT PRIMARY KEY, company_id BIGINT NULL, permissions_json JSONB NULL);
                    CREATE TABLE role_permissions(role_id BIGINT NOT NULL, permission_key TEXT NOT NULL);
                    CREATE TABLE users(id BIGINT PRIMARY KEY, company_id BIGINT NOT NULL, role_id BIGINT NULL,
                        role_name TEXT NOT NULL, permissions_json JSONB NOT NULL, status TEXT NOT NULL);
                    CREATE TABLE tenant_entitlements(company_id BIGINT NOT NULL, module_key TEXT NOT NULL, enabled BOOLEAN NOT NULL);
                    CREATE TABLE integrations(id BIGINT PRIMARY KEY, company_id BIGINT NOT NULL, integration_key TEXT NOT NULL,
                        config_json JSONB NOT NULL, operation_generation BIGINT NOT NULL, status TEXT NOT NULL,
                        last_tested_at TIMESTAMPTZ NULL, last_test_ok BOOLEAN NULL, last_test_message TEXT NULL,
                        updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW());
                    CREATE TABLE audit_logs(id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                        company_id BIGINT NOT NULL, actor_user_id BIGINT NULL, actor_name TEXT NOT NULL,
                        action_name TEXT NOT NULL, entity_name TEXT NOT NULL, entity_id BIGINT NULL, details_json JSONB NOT NULL);
                    INSERT INTO companies VALUES(7,'package_allowlist'),(8,'package_allowlist');
                    INSERT INTO users VALUES(13,7,NULL,'Motive test role','["integrations:manage"]'::jsonb,'Active');
                    INSERT INTO tenant_entitlements VALUES(7,'fleet.integrations',true),(8,'fleet.integrations',true);
                    """);
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public Runtime Runtime(Func<Database, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response)
            => new(_configuration, _protection, _keys, response);

        public async Task<string> SeedPendingAsync(Runtime runtime, bool retainOldTokens = false)
        {
            var state = runtime.OAuth.CreateState(7, 11, 13, 1);
            using var config = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                oauthStateHash = state.StateHash,
                oauthStateExpiresAt = state.Payload.ExpiresAt.ToString("O"),
                oauthStatus = "authorization_pending",
                accessToken = retainOldTokens ? "old-access-token" : null,
                refreshToken = retainOldTokens ? "old-refresh-token" : null,
                tokenType = retainOldTokens ? "Bearer" : null,
                tokenExpiresAt = retainOldTokens ? DateTimeOffset.UtcNow.AddHours(1).ToString("O") : null,
            }));
            await Db.ExecuteAsync(
                "INSERT INTO integrations(id,company_id,integration_key,config_json,operation_generation,status) VALUES(11,7,'motive',@config::jsonb,1,'Pending')",
                command => command.Parameters.AddWithValue("@config", runtime.Registry.EncryptConfigForStorage(config.RootElement)));
            return state.State;
        }

        public async Task<Dictionary<string, object?>> IntegrationAsync() =>
            (await Db.QuerySingleAsync("SELECT status,config_json,last_test_ok,operation_generation FROM integrations WHERE id=11"))!;

        public Task<long> AuditCountAsync(string action) => Db.ScalarLongAsync(
            "SELECT COUNT(*) FROM audit_logs WHERE company_id=7 AND entity_id=11 AND action_name=@action",
            command => command.Parameters.AddWithValue("@action", action));

        public async ValueTask DisposeAsync() =>
            await _owner.ExecuteAsync($"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE");
    }

    private sealed class Runtime
    {
        private readonly Database _db;
        public MotiveOAuthService OAuth { get; }
        public ConnectorRegistry Registry { get; }
        public ConcurrentBag<(string Path, bool HasAmbientTransaction)> HttpObservations { get; } = [];

        public Runtime(IConfiguration configuration, IDataProtectionProvider protection, IDataKeyProvider keys,
            Func<Database, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response)
        {
            _db = new Database(configuration, new TenantScopeAccessor());
            var factory = new Factory(new Handler((request, ct) =>
            {
                HttpObservations.Add((request.RequestUri!.AbsolutePath, _db.HasAmbientTransaction));
                return response(_db, request, ct);
            }));
            var environment = new TestEnvironment();
            OAuth = new MotiveOAuthService(factory, configuration, protection, environment, NullLogger<MotiveOAuthService>.Instance);
            Registry = new ConnectorRegistry(
                [new MotiveConnector(factory, NullLogger<MotiveConnector>.Instance)],
                new GenericHttpConnector(factory, NullLogger<GenericHttpConnector>.Instance),
                new PiiProtectionService(keys, NullLogger<PiiProtectionService>.Instance), environment);
        }

        public Task<IResult> InvokeAsync(string state, string? error = null, string? browserCookie = null)
        {
            var http = new DefaultHttpContext();
            http.Request.Scheme = "https";
            http.Request.Host = new HostString("api.example.test");
            http.Request.Path = MotiveOAuthService.CallbackPath;
            http.Request.QueryString = new QueryString("?state=" + Uri.EscapeDataString(state)
                + (error is null ? "&code=sdet-code" : "&error=" + Uri.EscapeDataString(error)));
            Assert.True(OAuth.TryReadState(state, out var payload));
            var cookie = browserCookie ?? payload!.Nonce;
            if (!string.IsNullOrEmpty(cookie))
                http.Request.Headers.Cookie = MotiveOAuthService.FlowCookieName + "=" + cookie;
            var method = typeof(EndpointMappings).GetMethod("MotiveOAuthCallback", BindingFlags.Static | BindingFlags.NonPublic)!;
            return (Task<IResult>)method.Invoke(null, [http, _db, new AuditService(_db), Registry, OAuth, CancellationToken.None])!;
        }

        public async Task<IResult> InvokePreflightAsync(string state, long companyId = 7, long actorId = 13,
            long integrationId = 11, string? browserCookie = null, bool canManage = true)
        {
            var http = new DefaultHttpContext();
            http.Request.Scheme = "https";
            http.Request.Host = new HostString("api.example.test");
            http.Request.Path = $"/api/integrations/{integrationId}/oauth/motive/preflight";
            http.Request.Method = HttpMethods.Post;
            http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
            http.Items[EndpointMappings.AuthUserIdItemKey] = actorId;
            http.Items[EndpointMappings.AuthRoleItemKey] = "Motive test role";
            http.Items[EndpointMappings.AuthPermissionsItemKey] = canManage ? new[] { "integrations:manage" } : [];
            Assert.True(OAuth.TryReadState(state, out var payload));
            var cookie = browserCookie ?? payload!.Nonce;
            if (!string.IsNullOrEmpty(cookie))
                http.Request.Headers.Cookie = MotiveOAuthService.FlowCookieName + "=" + cookie;
            using var body = JsonDocument.Parse(JsonSerializer.Serialize(new { state }));
            var method = typeof(EndpointMappings).GetMethod("MotiveOAuthPreflight", BindingFlags.Static | BindingFlags.NonPublic)!;
            return await (Task<IResult>)method.Invoke(null, [http, integrationId, body.RootElement, _db, OAuth, CancellationToken.None])!;
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => response(request, cancellationToken);
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Staging;
        public string ApplicationName { get; set; } = "motive-callback-tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
