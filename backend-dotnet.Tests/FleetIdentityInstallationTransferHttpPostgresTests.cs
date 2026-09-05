using System.Collections.Concurrent;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Foundation;
using Opstrax.Api.Middleware;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// Controlled local registered-route evidence only. The data and acknowledgements
// in this fixture are synthetic and establish no provider, device, field, browser,
// customer, deployment, certification, or commercial-readiness claim.
[Collection("fleet-identity-schema")]
[Trait("Category", "Integration")]
public sealed class FleetIdentityInstallationTransferHttpPostgresTests
{
    private const string DetailPattern = "/api/telemetry/devices/{id:long}";
    private const string TransferPattern = "/api/telemetry/devices/{id:long}/installations/transfer";
    private static readonly DateTimeOffset TransferAt = DateTimeOffset.Parse("2026-09-05T04:00:00Z");

    [Fact]
    public async Task RegisteredTransfer_ConcurrentSameKeyProducesOneFreshOneReplayAndRefreshesPersistedHistory()
    {
        await WithFixtureAsync(async fixture =>
        {
        var preflight = await fixture.GetDeviceAsync(fixture.DeviceA);
        Assert.Equal(HttpStatusCode.OK, preflight.Status);
        using var preflightJson = JsonDocument.Parse(preflight.Body);
        var preflightData = Data(preflightJson.RootElement);
        var current = preflightData.GetProperty("currentInstallation");
        var currentInstallationId = current.GetProperty("id").GetInt64();
        var currentRowVersion = current.GetProperty("rowVersion").GetInt64();
        Assert.Equal(fixture.PriorInstallation, currentInstallationId);
        Assert.Equal(fixture.VehicleA, current.GetProperty("vehicleId").GetInt64());
        Assert.True(currentRowVersion > 0);

        var concurrent = await fixture.ConcurrentTransferAsync(currentInstallationId, currentRowVersion);
        Assert.All(concurrent, response => Assert.Equal(HttpStatusCode.OK, response.Status));
        var fresh = Assert.Single(concurrent, response => Message(response) == "Device transferred");
        var replay = Assert.Single(concurrent, response => Message(response) == "Device transfer already recorded");
        using var freshJson = JsonDocument.Parse(fresh.Body);
        using var replayJson = JsonDocument.Parse(replay.Body);
        var freshData = Data(freshJson.RootElement);
        var replayData = Data(replayJson.RootElement);
        AssertExactProperties(freshData, "priorInstallationId", "id", "vehicleId", "effectiveFrom", "status");
        AssertExactProperties(replayData, "id", "replacedInstallationId", "deviceId", "vehicleId", "status", "effectiveFrom", "rowVersion");
        var successorId = freshData.GetProperty("id").GetInt64();
        Assert.Equal(fixture.PriorInstallation, freshData.GetProperty("priorInstallationId").GetInt64());
        Assert.Equal(fixture.VehicleB, freshData.GetProperty("vehicleId").GetInt64());
        Assert.Equal("Installed", freshData.GetProperty("status").GetString());
        Assert.Equal(successorId, replayData.GetProperty("id").GetInt64());
        Assert.Equal(fixture.PriorInstallation, replayData.GetProperty("replacedInstallationId").GetInt64());
        Assert.Equal(fixture.DeviceA, replayData.GetProperty("deviceId").GetInt64());
        Assert.Equal(fixture.VehicleB, replayData.GetProperty("vehicleId").GetInt64());
        Assert.True(replayData.GetProperty("rowVersion").GetInt64() >= 0);
        Assert.Equal(TransferAt, freshData.GetProperty("effectiveFrom").GetDateTimeOffset());
        Assert.Equal(TransferAt, replayData.GetProperty("effectiveFrom").GetDateTimeOffset());

        var afterConcurrent = await fixture.BusinessSnapshotAsync();
        var sequential = await fixture.TransferAsync(fresh.Trace, currentInstallationId, currentRowVersion);
        Assert.Equal(HttpStatusCode.OK, sequential.Status);
        Assert.Equal("Device transfer already recorded", Message(sequential));
        using (var sequentialJson = JsonDocument.Parse(sequential.Body))
            Assert.Equal(successorId, Data(sequentialJson.RootElement).GetProperty("id").GetInt64());
        Assert.Equal(afterConcurrent, await fixture.BusinessSnapshotAsync());

        var changed = await fixture.TransferAsync(Guid.NewGuid().ToString("D"), currentInstallationId,
            currentRowVersion, assignmentReason: "Different material assignment");
        AssertFailure(changed, HttpStatusCode.Conflict, fixture.Prefix);
        Assert.Equal(afterConcurrent, await fixture.BusinessSnapshotAsync());

        var refresh = await fixture.GetDeviceAsync(fixture.DeviceA);
        Assert.Equal(HttpStatusCode.OK, refresh.Status);
        using var refreshJson = JsonDocument.Parse(refresh.Body);
        var refreshData = Data(refreshJson.RootElement);
        var refreshedCurrent = refreshData.GetProperty("currentInstallation");
        Assert.Equal(successorId, refreshedCurrent.GetProperty("id").GetInt64());
        Assert.Equal(fixture.VehicleB, refreshedCurrent.GetProperty("vehicleId").GetInt64());
        var history = refreshData.GetProperty("installationHistory");
        Assert.Equal(2, history.GetArrayLength());
        var replacement = history[0];
        var prior = history[1];
        Assert.Equal(successorId, replacement.GetProperty("id").GetInt64());
        Assert.Equal(fixture.VehicleB, replacement.GetProperty("vehicleId").GetInt64());
        Assert.Equal(JsonValueKind.Null, replacement.GetProperty("effectiveTo").ValueKind);
        Assert.Equal(fixture.PriorInstallation, prior.GetProperty("id").GetInt64());
        Assert.Equal(fixture.VehicleA, prior.GetProperty("vehicleId").GetInt64());
        Assert.Equal(TransferAt, prior.GetProperty("effectiveTo").GetDateTimeOffset());

        await fixture.AssertPersistedOutcomeAsync(successorId, fresh.Trace);
        fixture.AssertObservedRlsPosture(preflight.Trace, fresh.Trace, replay.Trace, sequential.Trace, changed.Trace, refresh.Trace);
        });
    }

    [Theory]
    [InlineData("missing-bearer", 401)]
    [InlineData("revoked-session", 401)]
    [InlineData("expired-session", 401)]
    [InlineData("read-only", 403)]
    [InlineData("no-manage", 403)]
    [InlineData("disabled-telematics", 403)]
    [InlineData("foreign-device", 400)]
    [InlineData("foreign-target", 400)]
    [InlineData("out-of-branch-target", 400)]
    [InlineData("stale-installation", 409)]
    [InlineData("stale-version", 409)]
    public async Task RegisteredTransfer_FailsClosedWithoutBusinessMutation(string state, int expectedStatus)
    {
        await WithFixtureAsync(async fixture =>
        {
        await fixture.ArrangeStateAsync(state);
        var before = await fixture.BusinessSnapshotAsync();
        var response = await fixture.TransferForStateAsync(state);
        AssertFailure(response, (HttpStatusCode)expectedStatus, fixture.Prefix);
        Assert.Equal(before, await fixture.BusinessSnapshotAsync());
        });
    }

    [Fact]
    public async Task RegisteredTransfer_LateAuditFailureReturnsSafe500AndRollsBackEverything()
    {
        await WithFixtureAsync(async fixture =>
        {
        await fixture.InstallLateAuditFailureAsync();
        Assert.Equal(64, fixture.LateFailureSourceSha256.Length);
        var before = await fixture.BusinessSnapshotAsync();
        var response = await fixture.TransferAsync(Guid.NewGuid().ToString("D"), fixture.PriorInstallation, 1,
            key: fixture.LateFailureKey);
        AssertFailure(response, HttpStatusCode.InternalServerError, fixture.Prefix);
        Assert.DoesNotContain("synthetic_late_audit_failure", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await fixture.BusinessSnapshotAsync());
        });
    }

    private static async Task WithFixtureAsync(Func<Fixture, Task> body)
    {
        var fixture = await Fixture.CreateAsync();
        Exception? primary = null;
        try { await body(fixture); }
        catch (Exception error) { primary = error; }
        try { await fixture.DisposeAsync(); }
        catch (Exception cleanupFailure)
        {
            if (primary is null) primary = cleanupFailure;
            else primary.Data["FixtureCleanupFailure"] = cleanupFailure.ToString();
        }
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private static JsonElement Data(JsonElement root)
    {
        Assert.True(root.GetProperty("success").GetBoolean());
        return root.GetProperty("data");
    }

    private static string? Message(Response response)
    {
        using var json = JsonDocument.Parse(response.Body);
        return json.RootElement.GetProperty("message").GetString();
    }

    private static void AssertExactProperties(JsonElement value, params string[] expected)
        => Assert.Equal(expected.Order(StringComparer.Ordinal), value.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));

    private static void AssertFailure(Response response, HttpStatusCode expected, string secret)
    {
        Assert.Equal(expected, response.Status);
        using var json = JsonDocument.Parse(response.Body);
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("data").ValueKind);
        var message = json.RootElement.GetProperty("message").GetString() ?? string.Empty;
        Assert.InRange(message.Length, 1, 240);
        Assert.DoesNotContain(secret, response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connection", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT ", response.Body, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record Response(HttpStatusCode Status, string Body, string Trace);
    private sealed record RlsPosture(string Table, bool Enabled, bool Forced, bool Active, string Policies, long Tenant, string Role);

    private sealed class RequestEvidence
    {
        public ConcurrentDictionary<string, int> BackendByTrace { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, IReadOnlyList<RlsPosture>> RlsByTrace { get; } = new(StringComparer.Ordinal);
    }

    private sealed class KestrelParityHost(WebApplication app, HttpClient client, RequestEvidence evidence) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;
        public RequestEvidence Evidence { get; } = evidence;

        public static async Task<KestrelParityHost> StartAsync(string appConnection, string systemConnection)
        {
            var runtime = GuardConnection(appConnection, "opstrax_app");
            var system = GuardConnection(systemConnection, "opstrax_system");
            Assert.True(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PG_CONNECTION_REPLICA")),
                "No ambient read-replica fallback is permitted in this isolated HTTP fixture.");
            Assert.True(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES")),
                "Ambient hosting-startup injection is not permitted in this isolated HTTP fixture.");

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Staging, Args = [] });
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = Environments.Staging,
                ["Rls:EnforceTenantContext"] = "true",
                ["Rls:TenantTicketTtlSeconds"] = "120",
                ["ConnectionStrings:DefaultConnection"] = runtime,
                ["ConnectionStrings:SystemConnection"] = system
            });
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");

            // MapOpsTraxEndpoints contains the authoritative registrations. Register
            // inert fallbacks so RequestDelegateFactory classifies the other route
            // service parameters without allowing an unrelated route to execute.
            foreach (var serviceType in typeof(EndpointMappings).Assembly.GetTypes().Where(type =>
                         !type.IsGenericTypeDefinition &&
                         (type.Name.EndsWith("Service", StringComparison.Ordinal) ||
                          type.Name.EndsWith("Registry", StringComparison.Ordinal) ||
                          (type.IsInterface && type.Namespace?.StartsWith("Opstrax.Api", StringComparison.Ordinal) == true))))
            {
                builder.Services.AddSingleton(serviceType, _ =>
                    throw new InvalidOperationException($"Unrelated service {serviceType.Name} was resolved by the transfer fixture."));
            }
            builder.Services.AddSingleton<TenantScopeAccessor>();
            builder.Services.AddSingleton<Database>();
            builder.Services.AddSingleton<AmbientCorrelationContext>();
            builder.Services.AddSingleton<ICorrelationContext>(services => services.GetRequiredService<AmbientCorrelationContext>());
            builder.Services.AddSingleton<IFeatureAccessService, PostgresFeatureAccessService>();
            builder.Services.AddSingleton<IAuthorizationDecisionService, AuthorizationDecisionService>();
            builder.Services.AddSingleton<IAuditLogService, PostgresAuditLogService>();
            builder.Services.AddScoped<AuditService>();
            builder.Services.AddSingleton<SecurityEventService>();
            builder.Services.AddSingleton<RequestEvidence>();

            var servingApp = builder.Build();
            try
            {
                using (var startup = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                    await servingApp.Services.GetRequiredService<Database>().ValidateProductionIdentitiesAsync(startup.Token);

                // The error boundary is deliberately outside session and tenant
                // ownership: downstream failure unwinds/rolls back before safe 500.
                servingApp.UseMiddleware<ErrorHandlingMiddleware>();
                servingApp.Use(async (http, next) =>
                {
                    var trace = http.Request.Headers["X-OpsTrax-Test-Trace"].ToString();
                    http.TraceIdentifier = Guid.TryParse(trace, out var traceId) ? traceId.ToString("D") : Guid.NewGuid().ToString("D");
                    var authorization = http.Request.Headers.Authorization.ToString();
                    if (string.IsNullOrWhiteSpace(authorization) ||
                        !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await http.Response.WriteAsJsonAsync(ApiResponse<object>.Fail("Unauthorized", "Missing bearer token"));
                        return;
                    }
                    var token = authorization["Bearer ".Length..].Trim();
                    if (string.IsNullOrWhiteSpace(token))
                    {
                        http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await http.Response.WriteAsJsonAsync(ApiResponse<object>.Fail("Unauthorized", "Invalid bearer token"));
                        return;
                    }

                    var db = http.RequestServices.GetRequiredService<Database>();
                    const string sessionSql = @"SELECT s.user_id,s.company_id,u.role_name,u.role_id,u.customer_id,u.branch_id,
                               u.permissions_json,r.permissions_json role_permissions_json
                        FROM user_sessions s
                        JOIN users u ON u.id=s.user_id AND u.company_id=s.company_id
                        LEFT JOIN roles r ON r.id=u.role_id AND (r.company_id IS NULL OR r.company_id=u.company_id)
                        WHERE s.session_token=@token AND s.expires_at>NOW() AND u.status='Active'
                          AND s.impersonation_grant_id IS NULL LIMIT 1";
                    var session = await db.QuerySingleInSystemScopeAsync(sessionSql,
                        command => command.Parameters.AddWithValue("@token", token), http.RequestAborted);
                    if (session is null)
                    {
                        http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await http.Response.WriteAsJsonAsync(ApiResponse<object>.Fail("Unauthorized", "Session expired or invalid"));
                        return;
                    }

                    var userId = Convert.ToInt64(session["userId"]);
                    var companyId = Convert.ToInt64(session["companyId"]);
                    var roleName = session["roleName"]?.ToString() ?? string.Empty;
                    var roleId = session.TryGetValue("roleId", out var roleValue) && roleValue is not null and not DBNull
                        ? Convert.ToInt64(roleValue) : 0;
                    Task<string[]> ResolvePermissions() => EndpointMappings.ResolveEffectivePermissionsAsync(
                        roleId, roleName, session.GetValueOrDefault("rolePermissionsJson"),
                        session.GetValueOrDefault("permissionsJson"), db, http.RequestAborted);
                    var permissions = await db.RunInSystemScopeAsync(ResolvePermissions, http.RequestAborted);
                    http.Items[EndpointMappings.AuthUserIdItemKey] = userId;
                    http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
                    http.Items[EndpointMappings.AuthRoleItemKey] = roleName;
                    http.Items[EndpointMappings.AuthPermissionsItemKey] = permissions;
                    if (session.TryGetValue("branchId", out var branch) && branch is not null and not DBNull)
                        http.Items[EndpointMappings.AuthBranchIdItemKey] = Convert.ToInt64(branch);
                    if (session.TryGetValue("customerId", out var customer) && customer is not null and not DBNull)
                        http.Items[EndpointMappings.AuthCustomerIdItemKey] = Convert.ToInt64(customer);

                    const string entitlementSql = @"SELECT COUNT(*) FROM companies c
                        LEFT JOIN tenant_entitlements e ON e.company_id=c.id AND e.module_key='telematics'
                        WHERE c.id=@cid AND (
                          c.entitlement_policy_mode NOT IN ('legacy_allow','package_allowlist') OR
                          (c.entitlement_policy_mode='package_allowlist' AND COALESCE(e.enabled,false)=false) OR
                          (c.entitlement_policy_mode='legacy_allow' AND e.enabled=false))";
                    var blocked = await db.ScalarLongInSystemScopeAsync(entitlementSql,
                        command => command.Parameters.AddWithValue("@cid", companyId), http.RequestAborted);
                    if (blocked > 0)
                    {
                        http.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await http.Response.WriteAsJsonAsync(ApiResponse<object>.Fail(
                            "Module disabled", "The 'telematics' module is not enabled for your account. Contact your account owner."));
                        return;
                    }

                    var scopes = http.RequestServices.GetRequiredService<TenantScopeAccessor>();
                    await using var tenant = await db.BeginTenantScopeAsync(companyId, http.RequestAborted);
                    scopes.Current = tenant;
                    try
                    {
                        var rows = await db.QueryAsync(@"SELECT c.relname table_name,c.relrowsecurity enabled,c.relforcerowsecurity forced,
                                   row_security_active(c.oid) active,current_user role,
                                   opstrax_security.current_tenant_id() tenant,
                                   COALESCE(string_agg(DISTINCT p.polname,',' ORDER BY p.polname),'') policies
                              FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                              LEFT JOIN pg_policy p ON p.polrelid=c.oid
                             WHERE n.nspname='public' AND c.relname=ANY(@tables)
                             GROUP BY c.oid,c.relname,c.relrowsecurity,c.relforcerowsecurity ORDER BY c.relname",
                            command => command.Parameters.AddWithValue("@tables", new[]
                            {
                                "device_installations", "eld_devices", "idempotency_keys", "device_state_transitions", "audit_logs"
                            }), http.RequestAborted);
                        var posture = rows.Select(row => new RlsPosture(
                            row["tableName"]!.ToString()!, Convert.ToBoolean(row["enabled"]), Convert.ToBoolean(row["forced"]),
                            Convert.ToBoolean(row["active"]), row["policies"]?.ToString() ?? string.Empty,
                            Convert.ToInt64(row["tenant"]), row["role"]?.ToString() ?? string.Empty)).ToArray();
                        Assert.Equal(5, posture.Length);
                        Assert.All(posture, item =>
                        {
                            Assert.Equal("opstrax_app", item.Role);
                            Assert.Equal(companyId, item.Tenant);
                            if (item.Enabled)
                            {
                                Assert.True(item.Active, $"RLS was enabled but inactive for {item.Table}.");
                                Assert.False(string.IsNullOrWhiteSpace(item.Policies), $"RLS policy list was empty for {item.Table}.");
                            }
                        });
                        var evidence = http.RequestServices.GetRequiredService<RequestEvidence>();
                        evidence.BackendByTrace[http.TraceIdentifier] = Convert.ToInt32(await db.ScalarLongAsync("SELECT pg_backend_pid()", ct: http.RequestAborted));
                        evidence.RlsByTrace[http.TraceIdentifier] = posture;
                        await next();
                        await tenant.CompleteAsync(http.RequestAborted);
                    }
                    finally { scopes.Current = null; }
                });

                // These are the production registrations. No private handler/body
                // reflection and no remapped extracted delegates are used.
                servingApp.MapOpsTraxEndpoints();
                var endpoints = ((IEndpointRouteBuilder)servingApp).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
                AssertRouteUnique(endpoints, DetailPattern, HttpMethods.Get);
                AssertRouteUnique(endpoints, TransferPattern, HttpMethods.Post);

                using (var startup = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                    await servingApp.StartAsync(startup.Token);
                var address = Assert.Single(servingApp.Services.GetRequiredService<IServer>().Features
                    .Get<IServerAddressesFeature>()!.Addresses);
                Assert.True(Uri.TryCreate(address, UriKind.Absolute, out var uri));
                Assert.Equal(Uri.UriSchemeHttp, uri!.Scheme);
                Assert.Equal("127.0.0.1", uri.Host);
                Assert.True(uri.Port > 0);
                var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
                    { BaseAddress = uri, Timeout = Timeout.InfiniteTimeSpan };
                return new KestrelParityHost(servingApp, client, servingApp.Services.GetRequiredService<RequestEvidence>());
            }
            catch (Exception startupFailure)
            {
                try { using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await servingApp.StopAsync(stop.Token); }
                catch (Exception cleanupFailure) { startupFailure.Data["KestrelStopFailure"] = cleanupFailure.ToString(); }
                try { await servingApp.DisposeAsync(); }
                catch (Exception cleanupFailure) { startupFailure.Data["KestrelDisposeFailure"] = cleanupFailure.ToString(); }
                ExceptionDispatchInfo.Capture(startupFailure).Throw();
                throw;
            }
        }

        private static void AssertRouteUnique(IEnumerable<RouteEndpoint> endpoints, string pattern, string method)
        {
            var matches = endpoints.Where(endpoint => endpoint.RoutePattern.RawText == pattern &&
                endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) == true).ToArray();
            Assert.Single(matches);
        }

        internal static string GuardConnection(string connectionString, string expectedRole)
        {
            Assert.False(string.IsNullOrWhiteSpace(connectionString), "Explicit local database identity required; no fallback.");
            var connection = new NpgsqlConnectionStringBuilder(connectionString);
            Assert.Equal("127.0.0.1", connection.Host);
            Assert.Equal(5433, connection.Port);
            Assert.Equal("opstrax_local", connection.Database);
            Assert.Equal(expectedRole, connection.Username);
            Assert.False(connection.Pooling, "The isolated HTTP fixture requires pooling disabled.");
            connection.Timeout = 5;
            connection.CommandTimeout = 10;
            return connection.ConnectionString;
        }

        public async ValueTask DisposeAsync()
        {
            Exception? first = null;
            try { Client.Dispose(); } catch (Exception error) { first = error; }
            try { using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await app.StopAsync(stop.Token); }
            catch (Exception error) { first ??= error; }
            try { await app.DisposeAsync(); } catch (Exception error) { first ??= error; }
            if (first is not null) ExceptionDispatchInfo.Capture(first).Throw();
        }
    }

    private sealed class Fixture(string ownerConnection) : IAsyncDisposable
    {
        public string Prefix { get; } = "G5HTTP-" + Guid.NewGuid().ToString("N")[..12];
        public string LateFailureKey { get; } = "late-" + Guid.NewGuid().ToString("N");
        private string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        private string TransferKey { get; } = "transfer-" + Guid.NewGuid().ToString("N");
        private readonly CancellationTokenSource deadline = new(TimeSpan.FromMinutes(4));
        private readonly List<long> companies = [];
        private KestrelParityHost? host;
        private string? failureTrigger;
        public string LateFailureSourceSha256 { get; private set; } = string.Empty;
        private long companyA, companyB, branchA, branchOther, branchB, role, user;
        public long DeviceA, DeviceB, VehicleA, VehicleB, ForeignVehicle, OtherBranchVehicle, PriorInstallation;

        public static async Task<Fixture> CreateAsync()
        {
            static string Local(string key, string role)
            {
                var value = Environment.GetEnvironmentVariable(key) ?? string.Empty;
                var connection = new NpgsqlConnectionStringBuilder(value)
                {
                    Pooling = false,
                    ApplicationName = role == "zayra"
                        ? "opstrax-g5-http-transfer-owner"
                        : "opstrax-g5-http-transfer-tests"
                };
                return KestrelParityHost.GuardConnection(connection.ConnectionString, role);
            }
            var fixture = new Fixture(Local("OPSTRAX_TEST_DB", "zayra"));
            try
            {
                fixture.host = await KestrelParityHost.StartAsync(
                    Local("OPSTRAX_TEST_DB_APP", "opstrax_app"),
                    Local("OPSTRAX_TEST_DB_SYSTEM", "opstrax_system"));
                await fixture.InitializeAsync();
                return fixture;
            }
            catch (Exception setupFailure)
            {
                try { await fixture.DisposeAsync(); }
                catch (Exception cleanupFailure) { setupFailure.Data["FixtureCleanupFailure"] = cleanupFailure.ToString(); }
                ExceptionDispatchInfo.Capture(setupFailure).Throw();
                throw;
            }
        }

        public Task<Response> GetDeviceAsync(long device, bool authenticated = true)
            => SendAsync(HttpMethod.Get, $"/api/telemetry/devices/{device}", Guid.NewGuid().ToString("D"), null, authenticated);

        public Task<Response> TransferAsync(string trace, long currentInstallation, long currentVersion,
            long? device = null, long? target = null, string? key = null, string assignmentReason = "Assigned to replacement vehicle",
            bool authenticated = true)
            => SendAsync(HttpMethod.Post, $"/api/telemetry/devices/{device ?? DeviceA}/installations/transfer", trace,
                new
                {
                    vehicleId = target ?? VehicleB,
                    currentInstallationId = currentInstallation,
                    removalReason = "Scheduled fleet reassignment",
                    assignmentReason,
                    deviceRole = "GPS",
                    isPrimary = true,
                    effectiveAt = TransferAt,
                    installationLocation = "dashboard",
                    odometerAtInstallation = 12345.67m,
                    commissioningMethod = "controlled-http-evidence",
                    expectedRowVersion = currentVersion,
                    idempotencyKey = key ?? TransferKey
                }, authenticated);

        public async Task<Response[]> ConcurrentTransferAsync(long currentInstallation, long currentVersion)
        {
            NpgsqlConnection? owner = null;
            NpgsqlTransaction? transaction = null;
            Task<Response>[] requests = [];
            Exception? primary = null;
            var responses = Array.Empty<Response>();
            try
            {
                owner = new NpgsqlConnection(ownerConnection);
                await owner.OpenAsync(deadline.Token);
                transaction = await owner.BeginTransactionAsync(deadline.Token);
                await using (var lockCommand = new NpgsqlCommand(
                    "SELECT pg_advisory_xact_lock(hashtextextended(@identity,0)),pg_backend_pid()", owner, transaction))
                {
                    lockCommand.Parameters.AddWithValue("@identity", $"device-install-resource:{companyA}:device:{DeviceA}");
                    await lockCommand.ExecuteNonQueryAsync(deadline.Token);
                }
                var ownerPid = owner.ProcessID;
                requests =
                [
                    TransferAsync(Guid.NewGuid().ToString("D"), currentInstallation, currentVersion),
                    TransferAsync(Guid.NewGuid().ToString("D"), currentInstallation, currentVersion)
                ];
                await WaitForBothBlockedAsync(ownerPid, requests);
                await transaction.RollbackAsync(deadline.Token);
                await transaction.DisposeAsync();
                transaction = null;
                await owner.DisposeAsync();
                owner = null;
                responses = await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(30), deadline.Token);
            }
            catch (Exception error) { primary = error; }
            finally
            {
                async Task Cleanup(string key, Func<Task> action)
                {
                    try { await action(); }
                    catch (Exception error) { if (primary is null) primary = error; else primary.Data[key] = error.ToString(); }
                }
                if (transaction is not null) await Cleanup("OwnerRollbackFailure", async () => await transaction.RollbackAsync(CancellationToken.None));
                if (transaction is not null) await Cleanup("OwnerTransactionDisposeFailure", async () => await transaction.DisposeAsync());
                if (owner is not null) await Cleanup("OwnerConnectionDisposeFailure", async () => await owner.DisposeAsync());
                if (requests.Length > 0)
                    await Cleanup("RequestDrainFailure", async () => { await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(30)); });
            }
            if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
            return responses;
        }

        private async Task WaitForBothBlockedAsync(int ownerPid, Task<Response>[] requests)
        {
            var stop = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < stop)
            {
                Assert.DoesNotContain(requests, request => request.IsFaulted || request.IsCanceled || request.IsCompletedSuccessfully);
                if (host!.Evidence.BackendByTrace.Count >= 2)
                {
                    var pids = host.Evidence.BackendByTrace.Values.Distinct().ToArray();
                    if (pids.Length >= 2)
                    {
                        var blocked = await ScalarAsync(@"SELECT COUNT(*) FROM pg_stat_activity a
                            WHERE a.pid=ANY(@pids) AND a.wait_event_type='Lock'
                              AND a.query LIKE '%pg_advisory_xact_lock%'
                              AND @owner=ANY(pg_blocking_pids(a.pid))",
                            ("pids", pids), ("owner", ownerPid));
                        if (blocked == 2) return;
                    }
                }
                await Task.Delay(25, deadline.Token);
            }
            throw new TimeoutException("Both registered HTTP requests did not prove concurrent waiting on the fixture-owned advisory lock.");
        }

        public async Task ArrangeStateAsync(string state)
        {
            switch (state)
            {
                case "revoked-session": await ExecuteAsync("DELETE FROM user_sessions WHERE company_id=@cid", ("cid", companyA)); break;
                case "expired-session": await ExecuteAsync("UPDATE user_sessions SET expires_at=NOW()-INTERVAL '1 minute' WHERE company_id=@cid", ("cid", companyA)); break;
                case "read-only": await SetPermissionsAsync("telemetry.devices.read"); break;
                case "no-manage": await SetPermissionsAsync("telematics:gps:view"); break;
                case "disabled-telematics": await ExecuteAsync("UPDATE tenant_entitlements SET enabled=FALSE WHERE company_id=@cid AND module_key='telematics'", ("cid", companyA)); break;
            }
        }

        public Task<Response> TransferForStateAsync(string state)
        {
            var device = state == "foreign-device" ? DeviceB : DeviceA;
            var target = state switch
            {
                "foreign-target" => ForeignVehicle,
                "out-of-branch-target" => OtherBranchVehicle,
                _ => VehicleB
            };
            var installation = state == "stale-installation" ? PriorInstallation + 9_999_999 : PriorInstallation;
            var version = state == "stale-version" ? 999 : 1;
            return TransferAsync(Guid.NewGuid().ToString("D"), installation, version, device, target,
                authenticated: state != "missing-bearer");
        }

        private async Task<Response> SendAsync(HttpMethod method, string uri, string trace, object? body, bool authenticated)
        {
            using var request = new HttpRequestMessage(method, uri);
            request.Headers.TryAddWithoutValidation("X-OpsTrax-Test-Trace", trace);
            if (authenticated) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + Token);
            if (body is not null)
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await host!.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            var payload = await response.Content.ReadAsStringAsync(deadline.Token);
            return new Response(response.StatusCode, payload, trace);
        }

        public async Task InstallLateAuditFailureAsync()
        {
            failureTrigger = "g5_http_audit_fail_" + Guid.NewGuid().ToString("N")[..12];
            var source = $@"CREATE FUNCTION public.{failureTrigger}() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN IF NEW.company_id={companyA} THEN RAISE EXCEPTION 'synthetic_late_audit_failure'; END IF; RETURN NEW; END $$";
            LateFailureSourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
            await ExecuteAsync(source);
            await ExecuteAsync($"CREATE TRIGGER {failureTrigger} BEFORE INSERT ON public.audit_logs FOR EACH ROW EXECUTE FUNCTION public.{failureTrigger}()");
        }

        public Task<string> BusinessSnapshotAsync() => TextAsync(@"SELECT jsonb_build_object(
            'installations',(SELECT COALESCE(jsonb_agg(to_jsonb(x) ORDER BY x.id),'[]') FROM device_installations x WHERE x.company_id=ANY(@ids)),
            'devices',(SELECT COALESCE(jsonb_agg(to_jsonb(x) ORDER BY x.id),'[]') FROM eld_devices x WHERE x.company_id=ANY(@ids)),
            'ledger',(SELECT COALESCE(jsonb_agg(to_jsonb(x) ORDER BY x.id),'[]') FROM idempotency_keys x WHERE x.tenant_id=ANY(@ids)),
            'transitions',(SELECT COALESCE(jsonb_agg(to_jsonb(x) ORDER BY x.id),'[]') FROM device_state_transitions x WHERE x.company_id=ANY(@ids)),
            'audit',(SELECT COALESCE(jsonb_agg(to_jsonb(x) ORDER BY x.id),'[]') FROM audit_logs x WHERE x.company_id=ANY(@ids)))::text",
            ("ids", companies.ToArray()));

        public async Task AssertPersistedOutcomeAsync(long successorId, string freshTrace)
        {
            var rows = await QueryAsync(@"SELECT i.id,i.vehicle_id,i.status,i.effective_from,i.effective_to,i.replaced_installation_id,i.correlation_id,
                       d.vehicle_id projected_vehicle_id,d.device_state,i.company_id
                  FROM device_installations i JOIN eld_devices d ON d.id=i.device_id AND d.company_id=i.company_id
                 WHERE i.company_id=@cid AND i.device_id=@did ORDER BY i.effective_from,i.id",
                ("cid", companyA), ("did", DeviceA));
            Assert.Equal(2, rows.Count);
            Assert.Equal(PriorInstallation, Convert.ToInt64(rows[0]["id"]));
            Assert.Equal("Removed", rows[0]["status"]);
            Assert.Equal(TransferAt, new DateTimeOffset(Convert.ToDateTime(rows[0]["effectiveTo"]).ToUniversalTime()));
            Assert.Equal(successorId, Convert.ToInt64(rows[1]["id"]));
            Assert.Equal("Installed", rows[1]["status"]);
            Assert.Equal(PriorInstallation, Convert.ToInt64(rows[1]["replacedInstallationId"]));
            Assert.Equal(VehicleB, Convert.ToInt64(rows[1]["vehicleId"]));
            Assert.Equal(VehicleB, Convert.ToInt64(rows[1]["projectedVehicleId"]));
            Assert.Equal("Installed", rows[1]["deviceState"]);
            Assert.Equal(Guid.Parse(freshTrace), Guid.Parse(rows[1]["correlationId"]!.ToString()!));

            var ledger = await QuerySingleAsync(@"SELECT status,response_reference FROM idempotency_keys
                WHERE tenant_id=@cid AND operation='device.installation.transfer' AND idempotency_key=@key",
                ("cid", companyA), ("key", TransferKey));
            Assert.NotNull(ledger);
            Assert.Equal("completed", ledger!["status"]);
            Assert.Equal(successorId.ToString(), ledger["responseReference"]?.ToString());

            var transition = await QuerySingleAsync(@"SELECT company_id,branch_id,actor_user_id,correlation_id,reason_code,from_state,to_state
                FROM device_state_transitions WHERE company_id=@cid AND device_id=@did AND reason_code='installation_transferred'",
                ("cid", companyA), ("did", DeviceA));
            Assert.NotNull(transition);
            Assert.Equal(companyA, Convert.ToInt64(transition!["companyId"]));
            Assert.Equal(branchA, Convert.ToInt64(transition["branchId"]));
            Assert.Equal(user, Convert.ToInt64(transition["actorUserId"]));
            Assert.Equal(Guid.Parse(freshTrace), Guid.Parse(transition["correlationId"]!.ToString()!));
            Assert.Equal("Installed", transition["fromState"]);
            Assert.Equal("Installed", transition["toState"]);

            var audit = await QuerySingleAsync(@"SELECT entity_id,details_json FROM audit_logs
                WHERE company_id=@cid AND action_name='device.installation.transferred'",
                ("cid", companyA));
            Assert.NotNull(audit);
            Assert.Equal(successorId, Convert.ToInt64(audit!["entityId"]));
            using (var details = JsonDocument.Parse(audit["detailsJson"]!.ToString()!))
            {
                Assert.Equal(DeviceA, details.RootElement.GetProperty("deviceId").GetInt64());
                Assert.Equal(PriorInstallation, details.RootElement.GetProperty("priorInstallationId").GetInt64());
                Assert.Equal(VehicleB, details.RootElement.GetProperty("vehicleId").GetInt64());
                Assert.Equal(TransferAt, details.RootElement.GetProperty("effectiveAt").GetDateTimeOffset());
            }
            Assert.Equal(0, await ScalarAsync(@"SELECT
                (SELECT COUNT(*) FROM device_installations WHERE company_id=@foreign)+
                (SELECT COUNT(*) FROM idempotency_keys WHERE tenant_id=@foreign)+
                (SELECT COUNT(*) FROM device_state_transitions WHERE company_id=@foreign)+
                (SELECT COUNT(*) FROM audit_logs WHERE company_id=@foreign)", ("foreign", companyB)));
        }

        public void AssertObservedRlsPosture(params string[] traces)
        {
            Assert.All(traces, trace =>
            {
                Assert.True(host!.Evidence.RlsByTrace.TryGetValue(trace, out var posture), $"Missing in-request RLS posture for {trace}.");
                Assert.Equal(5, posture!.Count);
                Assert.Equal(new[] { "audit_logs", "device_installations", "device_state_transitions", "eld_devices", "idempotency_keys" },
                    posture.Select(item => item.Table).Order(StringComparer.Ordinal));
            });
        }

        private async Task InitializeAsync()
        {
            async Task<long> CompanyAsync(string suffix)
            {
                var id = await ScalarAsync(@"INSERT INTO companies(company_code,name,industry,entitlement_policy_mode)
                    VALUES(@code,'Synthetic G5 HTTP transfer','Transportation','package_allowlist') RETURNING id", ("code", Prefix + suffix));
                companies.Add(id);
                return id;
            }
            async Task<long> BranchAsync(long cid, string suffix) => await ScalarAsync(
                "INSERT INTO branches(company_id,branch_code,name,status) VALUES(@cid,@code,'Synthetic branch','Active') RETURNING id",
                ("cid", cid), ("code", Prefix + suffix));
            async Task<long> VehicleAsync(long cid, long bid, string suffix) => await ScalarAsync(@"INSERT INTO vehicles
                (company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status,availability_status,out_of_service)
                VALUES(@cid,@bid,@code,'Truck','manufacturer-serial-number',@alternate,'Available','available',FALSE) RETURNING id",
                ("cid", cid), ("bid", bid), ("code", Prefix + suffix), ("alternate", Prefix + "ALT-" + suffix));
            async Task<long> DeviceAsync(long cid, long bid, string suffix) => await ScalarAsync(@"INSERT INTO eld_devices
                (company_id,branch_id,device_serial,status,device_state,api_key_hash,hmac_secret_encrypted,hmac_key_version,created_at)
                VALUES(@cid,@bid,@serial,'Active','Registered',encode(sha256(@serial::bytea),'hex'),repeat('b',32),1,NOW()) RETURNING id",
                ("cid", cid), ("bid", bid), ("serial", Prefix + suffix));

            companyA = await CompanyAsync("-A");
            companyB = await CompanyAsync("-B");
            branchA = await BranchAsync(companyA, "-A");
            branchOther = await BranchAsync(companyA, "-OTHER");
            branchB = await BranchAsync(companyB, "-B");
            VehicleA = await VehicleAsync(companyA, branchA, "-VEH-A");
            VehicleB = await VehicleAsync(companyA, branchA, "-VEH-B");
            OtherBranchVehicle = await VehicleAsync(companyA, branchOther, "-VEH-OTHER");
            ForeignVehicle = await VehicleAsync(companyB, branchB, "-VEH-FOREIGN");
            DeviceA = await DeviceAsync(companyA, branchA, "-DEVICE-A");
            DeviceB = await DeviceAsync(companyB, branchB, "-DEVICE-B");
            PriorInstallation = await ScalarAsync(@"INSERT INTO device_installations
                (company_id,branch_id,device_id,vehicle_id,status,device_role,is_primary,effective_from,installed_at,
                 installation_location,assignment_reason,source,row_version)
                VALUES(@cid,@branch,@device,@vehicle,'Installed','GPS',TRUE,@at,@at,'dashboard','Initial controlled fixture','test',1)
                RETURNING id", ("cid", companyA), ("branch", branchA), ("device", DeviceA), ("vehicle", VehicleA),
                ("at", TransferAt.AddHours(-1)));
            await ExecuteAsync("UPDATE eld_devices SET device_state='Installed',vehicle_id=@vehicle WHERE id=@device AND company_id=@cid",
                ("vehicle", VehicleA), ("device", DeviceA), ("cid", companyA));

            role = await ScalarAsync(@"INSERT INTO roles(company_id,name,permissions_json,is_system)
                VALUES(@cid,@name,'[""telemetry.devices.read"",""telemetry.devices.manage""]'::jsonb,FALSE) RETURNING id",
                ("cid", companyA), ("name", Prefix + "-ROLE"));
            await ExecuteAsync(@"INSERT INTO role_permissions(role_id,permission_key) VALUES
                (@role,'telemetry.devices.read'),(@role,'telemetry.devices.manage')", ("role", role));
            user = await ScalarAsync(@"INSERT INTO users(company_id,branch_id,role_id,role_name,full_name,email,status,permissions_json)
                VALUES(@cid,@branch,@role,@name,'Synthetic fleet operator',@email,'Active','[]'::jsonb) RETURNING id",
                ("cid", companyA), ("branch", branchA), ("role", role), ("name", Prefix + "-ROLE"),
                ("email", Prefix + "@example.invalid"));
            await ExecuteAsync("INSERT INTO user_sessions(company_id,user_id,session_token,expires_at) VALUES(@cid,@user,@token,NOW()+INTERVAL '30 minutes')",
                ("cid", companyA), ("user", user), ("token", Token));
            await ExecuteAsync("INSERT INTO tenant_entitlements(company_id,module_key,enabled) VALUES(@cid,'telematics',TRUE)", ("cid", companyA));
        }

        private async Task SetPermissionsAsync(params string[] permissions)
        {
            await ExecuteAsync("DELETE FROM role_permissions WHERE role_id=@role", ("role", role));
            await ExecuteAsync("UPDATE roles SET permissions_json=@json::jsonb WHERE id=@role", ("role", role),
                ("json", JsonSerializer.Serialize(permissions)));
            foreach (var permission in permissions)
                await ExecuteAsync("INSERT INTO role_permissions(role_id,permission_key) VALUES(@role,@permission)",
                    ("role", role), ("permission", permission));
        }

        private Task<int> ExecuteAsync(string sql, params (string Key, object? Value)[] values)
            => OwnerAsync(sql, deadline.Token, command => command.ExecuteNonQueryAsync(deadline.Token), values);
        private Task<int> ExecuteAsync(string sql, CancellationToken token, params (string Key, object? Value)[] values)
            => OwnerAsync(sql, token, command => command.ExecuteNonQueryAsync(token), values);
        private Task<long> ScalarAsync(string sql, params (string Key, object? Value)[] values)
            => OwnerAsync(sql, deadline.Token, async command => Convert.ToInt64(await command.ExecuteScalarAsync(deadline.Token)), values);
        private Task<long> ScalarAsync(string sql, CancellationToken token, params (string Key, object? Value)[] values)
            => OwnerAsync(sql, token, async command => Convert.ToInt64(await command.ExecuteScalarAsync(token)), values);
        private Task<string> TextAsync(string sql, params (string Key, object? Value)[] values)
            => OwnerAsync(sql, deadline.Token, async command => (string)(await command.ExecuteScalarAsync(deadline.Token))!, values);
        private Task<List<Dictionary<string, object?>>> QueryAsync(string sql, params (string Key, object? Value)[] values)
            => OwnerAsync(sql, deadline.Token, async command =>
            {
                var rows = new List<Dictionary<string, object?>>();
                await using var reader = await command.ExecuteReaderAsync(deadline.Token);
                while (await reader.ReadAsync(deadline.Token))
                {
                    var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    for (var i = 0; i < reader.FieldCount; i++) row[ToCamel(reader.GetName(i))] = await reader.IsDBNullAsync(i, deadline.Token) ? null : reader.GetValue(i);
                    rows.Add(row);
                }
                return rows;
            }, values);
        private async Task<Dictionary<string, object?>?> QuerySingleAsync(string sql, params (string Key, object? Value)[] values)
            => (await QueryAsync(sql, values)).SingleOrDefault();

        private async Task<T> OwnerAsync<T>(string sql, CancellationToken token, Func<NpgsqlCommand, Task<T>> run,
            params (string Key, object? Value)[] values)
        {
            await using var connection = new NpgsqlConnection(ownerConnection);
            await connection.OpenAsync(token);
            await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 10 };
            foreach (var (key, value) in values) command.Parameters.AddWithValue(key, value ?? DBNull.Value);
            return await run(command);
        }

        private static string ToCamel(string name)
        {
            var pieces = name.Split('_');
            return pieces[0] + string.Concat(pieces.Skip(1).Select(piece => char.ToUpperInvariant(piece[0]) + piece[1..]));
        }

        public async ValueTask DisposeAsync()
        {
            Exception? first = null;
            async Task Attempt(string key, Func<Task> action)
            {
                try { await action(); }
                catch (Exception error) { if (first is null) first = error; else first.Data[key] = error.ToString(); }
            }

            // Every request is awaited by its caller. The host is stopped before
            // database artifacts are removed, so no request can race cleanup.
            if (host is not null) await Attempt("KestrelCleanupFailure", async () => await host.DisposeAsync());
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            if (failureTrigger is not null)
            {
                await Attempt("TriggerCleanupFailure", () => ExecuteAsync($"DROP TRIGGER IF EXISTS {failureTrigger} ON public.audit_logs", cleanup.Token));
                await Attempt("FunctionCleanupFailure", () => ExecuteAsync($"DROP FUNCTION IF EXISTS public.{failureTrigger}()", cleanup.Token));
            }
            if (companies.Count > 0)
            {
                foreach (var table in new[]
                {
                    "authorization_decision_logs", "security_events", "audit_logs", "device_state_transitions",
                    "device_installation_evidence", "device_installations", "idempotency_keys", "user_sessions",
                    "role_permissions", "users", "roles", "tenant_entitlements", "eld_devices", "vehicles", "branches"
                })
                {
                    var predicate = table switch
                    {
                        "authorization_decision_logs" => "tenant_id=ANY(@ids)",
                        "idempotency_keys" => "tenant_id=ANY(@ids)",
                        "role_permissions" => "role_id IN(SELECT id FROM roles WHERE company_id=ANY(@ids))",
                        _ => "company_id=ANY(@ids)"
                    };
                    await Attempt(table + "CleanupFailure", () => ExecuteAsync($"DELETE FROM {table} WHERE {predicate}", cleanup.Token,
                        ("ids", companies.ToArray())));
                }
                await Attempt("CompanyCleanupFailure", async () => Assert.Equal(companies.Count, await ExecuteAsync(
                    "DELETE FROM companies WHERE id=ANY(@ids) AND company_code LIKE @prefix", cleanup.Token,
                    ("ids", companies.ToArray()), ("prefix", Prefix + "%"))));
                await Attempt("CompanyResidueFailure", async () => Assert.Equal(0, await ScalarAsync(
                    "SELECT COUNT(*) FROM companies WHERE company_code LIKE @prefix", cleanup.Token, ("prefix", Prefix + "%"))));
                await Attempt("TenantResidueFailure", async () => Assert.Equal(0, await ScalarAsync(@"SELECT
                    (SELECT COUNT(*) FROM device_installations WHERE company_id=ANY(@ids))+
                    (SELECT COUNT(*) FROM eld_devices WHERE company_id=ANY(@ids))+
                    (SELECT COUNT(*) FROM idempotency_keys WHERE tenant_id=ANY(@ids))+
                    (SELECT COUNT(*) FROM device_state_transitions WHERE company_id=ANY(@ids))+
                    (SELECT COUNT(*) FROM audit_logs WHERE company_id=ANY(@ids))+
                    (SELECT COUNT(*) FROM user_sessions WHERE company_id=ANY(@ids))", cleanup.Token, ("ids", companies.ToArray()))));
            }
            await Attempt("SessionResidueFailure", async () => Assert.Equal(0, await ScalarAsync(
                "SELECT COUNT(*) FROM pg_stat_activity WHERE application_name LIKE 'opstrax-g5-http-transfer-%' AND pid<>pg_backend_pid()", cleanup.Token)));
            await Attempt("IdleTransactionResidueFailure", async () => Assert.Equal(0, await ScalarAsync(
                "SELECT COUNT(*) FROM pg_stat_activity WHERE application_name LIKE 'opstrax-g5-http-transfer-%' AND pid<>pg_backend_pid() AND state='idle in transaction'", cleanup.Token)));
            await Attempt("WaitResidueFailure", async () => Assert.Equal(0, await ScalarAsync(@"SELECT COUNT(*) FROM pg_locks l
                JOIN pg_stat_activity a ON a.pid=l.pid WHERE a.application_name LIKE 'opstrax-g5-http-transfer-%'
                  AND a.pid<>pg_backend_pid() AND NOT l.granted", cleanup.Token)));
            deadline.Dispose();
            if (first is not null) ExceptionDispatchInfo.Capture(first).Throw();
        }
    }
}
