using System.Collections;
using System.Net;
using System.Reflection;
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
using Microsoft.AspNetCore.Routing.Patterns;
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

// Actual production registration, opaque sessions and signed tenant RLS over
// PostgreSQL. Acknowledgements here are persisted synthetic metadata, not proof
// of real-driver, camera, provider, browser, field or certification acceptance.
[Collection("fleet-identity-schema")]
[Trait("Category", "Integration")]
public sealed class CoachingCompletionAcknowledgementHttpPostgresTests
{
    private const string Route = "/api/coaching/tasks/{id:long}/complete";
    private const string MissingAcknowledgement = "A recorded driver acknowledgement is required before completion.";

    [Theory]
    [InlineData("Escalated", false, false)]
    [InlineData("Escalated", false, true)]
    [InlineData("Escalated", true, false)]
    [InlineData("Driver Acknowledged", false, false)]
    [InlineData("Driver Acknowledged", false, true)]
    [InlineData("Driver Acknowledged", true, false)]
    public async Task MissingStoredAcknowledgement_RejectsPayloadSubstitutesWithoutDomainWrites(string status, bool acknowledged, bool timestamp)
    {
        await using var f = await Fixture.Create();
        await f.SetAcknowledgement(status, acknowledged, timestamp);
        var before = await f.Snapshot();
        var response = await f.Complete(f.Own, new { rowVersion = 0, completionNote = "Synthetic outcome", afterSafetyScore = 0,
            driverAcknowledged = true, acknowledgedAt = "2026-09-01T00:00:00Z", status = "Driver Acknowledged" });
        Assert.Equal(HttpStatusCode.Conflict, response.Status);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(MissingAcknowledgement, json.RootElement.GetProperty("message").GetString());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("data").ValueKind);
        Assert.Equal(before, await f.Snapshot());
    }

    [Theory]
    [InlineData("Escalated")]
    [InlineData("Driver Acknowledged")]
    public async Task RecordedAcknowledgement_CompletesOnceAndPreservesEvidenceAndObservedZero(string status)
    {
        await using var f = await Fixture.Create();
        await f.SetAcknowledgement(status, true, true);
        var acknowledgement = await f.Acknowledgement();
        Assert.Equal(HttpStatusCode.OK, (await f.Complete(f.Own)).Status);
        Assert.Equal(acknowledgement, await f.Acknowledgement());
        var state = await f.TaskState();
        Assert.Equal("Completed", state.GetProperty("status").GetString());
        Assert.Equal(1, state.GetProperty("row_version").GetInt64());
        Assert.Equal(0, state.GetProperty("after_safety_score").GetDecimal());
        Assert.Equal(JsonValueKind.Null, state.GetProperty("effectiveness_score").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, state.GetProperty("completed_at").ValueKind);
        Assert.Contains("not a causal measure", state.GetProperty("effectiveness_observation_json").GetRawText());
        Assert.Equal(1, await f.Count("coaching_notes"));
        Assert.Equal(1, await f.Count("audit_logs"));
        var completed = await f.Snapshot();
        Assert.Equal(HttpStatusCode.Conflict, (await f.Complete(f.Own)).Status);
        Assert.Equal(completed, await f.Snapshot());
    }

    [Fact]
    public async Task StaleVersionAndConcurrentCompletion_PreserveSingleAtomicOutcome()
    {
        await using var f = await Fixture.Create();
        await f.SetAcknowledgement("Escalated", true, true);
        var before = await f.Snapshot();
        Assert.Equal(HttpStatusCode.Conflict, (await f.Complete(f.Own, new { rowVersion = 9, completionNote = "Synthetic outcome", afterSafetyScore = 0 })).Status);
        Assert.Equal(before, await f.Snapshot());
        var responses = await Task.WhenAll(f.Complete(f.Own), f.Complete(f.Own));
        Assert.Single(responses, r => r.Status == HttpStatusCode.OK);
        Assert.Single(responses, r => r.Status == HttpStatusCode.Conflict);
        Assert.Equal(1, await f.Count("coaching_notes"));
        Assert.Equal(1, await f.Count("audit_logs"));
        Assert.Equal(1, (await f.TaskState()).GetProperty("row_version").GetInt64());
    }

    [Theory]
    [InlineData("Assigned")]
    [InlineData("Draft")]
    [InlineData("Cancelled")]
    [InlineData("Completed")]
    public async Task ExistingStateRefusalsRemainUnchanged(string status)
    {
        await using var f = await Fixture.Create();
        await f.SetAcknowledgement(status, true, true);
        var before = await f.Snapshot();
        Assert.Equal(HttpStatusCode.Conflict, (await f.Complete(f.Own)).Status);
        Assert.Equal(before, await f.Snapshot());
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("missing")]
    [InlineData("deleted")]
    [InlineData("other-branch")]
    public async Task ExistingTenantBranchAndArchiveRefusalsRemainOpaque(string target)
    {
        await using var f = await Fixture.Create();
        var id = target switch { "foreign" => f.Foreign, "deleted" => f.Deleted, "other-branch" => f.OtherBranch, _ => long.MaxValue - 1 };
        var before = await f.Snapshot();
        var response = await f.Complete(id);
        Assert.Equal(HttpStatusCode.NotFound, response.Status);
        Assert.DoesNotContain(MissingAcknowledgement, response.Body);
        Assert.DoesNotContain(f.Prefix, response.Body);
        Assert.Equal(before, await f.Snapshot());
    }

    [Theory]
    [InlineData("no-session", 401)]
    [InlineData("revoked-session", 401)]
    [InlineData("customer", 403)]
    [InlineData("no-permission", 403)]
    [InlineData("safety-module", 403)]
    [InlineData("driver-safety-module", 403)]
    public async Task ExistingSessionCustomerPermissionAndModuleBoundariesRemain(string state, int expected)
    {
        await using var f = await Fixture.Create();
        await f.ChangePrincipal(state);
        var before = await f.Snapshot();
        var response = await f.Complete(f.Own, authenticated: state != "no-session");
        Assert.Equal(expected, (int)response.Status);
        Assert.DoesNotContain(MissingAcknowledgement, response.Body);
        Assert.Equal(before, await f.Snapshot());
    }

    [Fact]
    public async Task CompanyWideAndDirectManageRemainAllowedWithRecordedAcknowledgement()
    {
        await using var f = await Fixture.Create();
        await f.Execute("UPDATE users SET branch_id=NULL WHERE id=@id", ("id", f.User));
        await f.SetPermission("safety:manage");
        Assert.Equal(HttpStatusCode.OK, (await f.Complete(f.OtherBranch)).Status);
        Assert.Equal(1, await f.Count("coaching_notes"));
        Assert.Equal(1, await f.Count("audit_logs"));
    }

    [Theory]
    [InlineData("audit_logs")]
    [InlineData("coaching_notes")]
    public async Task CompletionPersistenceFailure_RollsBackTaskAndEvidence(string table)
    {
        await using var f = await Fixture.Create();
        await f.SetAcknowledgement("Escalated", true, true);
        await f.FailInsert(table);
        var before = await f.Snapshot();
        var response = await f.Complete(f.Own);
        Assert.Equal(HttpStatusCode.InternalServerError, response.Status);
        Assert.DoesNotContain(f.Prefix, response.Body);
        Assert.Equal(before, await f.Snapshot());
    }

    [Fact]
    public async Task CompletionStillRequiresOutcomeAndInRangeObservedScore()
    {
        await using var f = await Fixture.Create();
        await f.SetAcknowledgement("Escalated", true, true);
        var before = await f.Snapshot();
        foreach (var body in new object[] { new { rowVersion = 0 }, new { rowVersion = 0, completionNote = "", afterSafetyScore = 0 },
            new { rowVersion = 0, completionNote = "Synthetic", afterSafetyScore = 101 } })
            Assert.Equal(HttpStatusCode.BadRequest, (await f.Complete(f.Own, body)).Status);
        Assert.Equal(before, await f.Snapshot());
    }

    private static Delegate ProductionCompletion()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Staging, Args = [] });
        builder.Configuration.Sources.Clear();
        using var app = builder.Build();
        app.MapOpsTraxEndpoints();
        var matches = new List<Delegate>();
        foreach (var source in ((IEndpointRouteBuilder)app).DataSources)
        {
            if (source.GetType().GetField("_routeEntries", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(source) is not IEnumerable entries) continue;
            foreach (var entry in entries)
            {
                if (entry is null || Member(entry, "RoutePattern") is not RoutePattern pattern || pattern.RawText != Route || Member(entry, "RouteHandler") is not Delegate handler) continue;
                if (Member(entry, "HttpMethods") is IEnumerable<string> methods && methods.Contains(HttpMethods.Post)) matches.Add(handler);
            }
        }
        return Assert.Single(matches);
    }

    private static object? Member(object value, string name) => value.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(value)
        ?? value.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(value);
    private sealed record Response(HttpStatusCode Status, string Body);

    // Package-free, fixture-owned parity host for the normal authenticated slice
    // of current Program.cs. It intentionally does not claim literal middleware
    // reuse or coverage of public, SSO, SSE, support, proxy, CSRF or rate-limit paths.
    private sealed class KestrelParityHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public static async Task<KestrelParityHost> StartAsync(
            string appConnection,
            string systemConnection,
            Delegate completion)
        {
            var runtime = GuardConnection(appConnection, "opstrax_app");
            var system = GuardConnection(systemConnection, "opstrax_system");
            Assert.True(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PG_CONNECTION_REPLICA")),
                "No ambient read-replica fallback is permitted in the isolated HTTP fixture.");
            Assert.True(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES")),
                "Ambient hosting-startup injection is not permitted in the isolated HTTP fixture.");

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Staging,
                Args = []
            });
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = Environments.Staging,
                ["Rls:EnforceTenantContext"] = "true",
                ["ConnectionStrings:DefaultConnection"] = runtime,
                ["ConnectionStrings:SystemConnection"] = system
            });
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton<TenantScopeAccessor>();
            builder.Services.AddSingleton<Database>();
            builder.Services.AddSingleton<AmbientCorrelationContext>();
            builder.Services.AddSingleton<ICorrelationContext>(services => services.GetRequiredService<AmbientCorrelationContext>());
            builder.Services.AddSingleton<IFeatureAccessService, PostgresFeatureAccessService>();
            builder.Services.AddSingleton<IAuthorizationDecisionService, AuthorizationDecisionService>();
            builder.Services.AddSingleton<IAuditLogService, PostgresAuditLogService>();
            builder.Services.AddScoped<AuditService>();
            builder.Services.AddSingleton<SecurityEventService>();

            var servingApp = builder.Build();
            try
            {
                using (var startup = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                    await servingApp.Services.GetRequiredService<Database>().ValidateProductionIdentitiesAsync(startup.Token);

                // Error handling is deliberately outside session/tenant ownership so
                // persistence exceptions first escape and roll back the tenant scope.
                servingApp.UseMiddleware<ErrorHandlingMiddleware>();
                servingApp.Use(async (http, next) =>
                {
                    // Narrow parity with current Program.cs normal bearer-session stages.
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
                          AND s.impersonation_grant_id IS NULL
                        LIMIT 1";
                    var session = await db.QuerySingleInSystemScopeAsync(
                        sessionSql, command => command.Parameters.AddWithValue("@token", token), http.RequestAborted);
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

                    // This route's current Program.cs path-level package gate. The
                    // handler independently enforces fleet.driver_safety in-scope.
                    const string entitlementSql = @"SELECT COUNT(*)
                        FROM companies c
                        LEFT JOIN tenant_entitlements e ON e.company_id=c.id AND e.module_key='safety'
                        WHERE c.id=@cid AND (
                          c.entitlement_policy_mode NOT IN ('legacy_allow','package_allowlist') OR
                          (c.entitlement_policy_mode='package_allowlist' AND COALESCE(e.enabled,false)=false) OR
                          (c.entitlement_policy_mode='legacy_allow' AND e.enabled=false))";
                    var blocked = await db.ScalarLongInSystemScopeAsync(
                        entitlementSql, command => command.Parameters.AddWithValue("@cid", companyId), http.RequestAborted);
                    if (blocked > 0)
                    {
                        http.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await http.Response.WriteAsJsonAsync(ApiResponse<object>.Fail(
                            "Module disabled", "The 'safety' module is not enabled for your account. Contact your account owner."));
                        return;
                    }

                    var scopes = http.RequestServices.GetRequiredService<TenantScopeAccessor>();
                    await using var tenant = await db.BeginTenantScopeAsync(companyId, http.RequestAborted);
                    scopes.Current = tenant;
                    try
                    {
                        await next();
                        await tenant.CompleteAsync(http.RequestAborted);
                    }
                    finally
                    {
                        scopes.Current = null;
                    }
                });
                servingApp.Use(async (http, next) =>
                {
                    var companyId = Convert.ToInt64(http.Items[EndpointMappings.AuthCompanyIdItemKey]);
                    var identity = await http.RequestServices.GetRequiredService<Database>().QuerySingleAsync(
                        @"SELECT current_user AS role,
                                 opstrax_security.current_tenant_id() AS tenant,
                                 row_security_active('public.coaching_tasks'::regclass) AS rls_active",
                        ct: http.RequestAborted);
                    Assert.NotNull(identity);
                    Assert.Equal("opstrax_app", identity["role"]?.ToString());
                    Assert.Equal(companyId, Convert.ToInt64(identity["tenant"]));
                    Assert.Equal(true, identity["rlsActive"]);
                    await next();
                });
                servingApp.MapPost(Route, completion);

                using (var startup = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                    await servingApp.StartAsync(startup.Token);
                var addresses = servingApp.Services.GetRequiredService<IServer>().Features
                    .Get<IServerAddressesFeature>()!.Addresses;
                var address = Assert.Single(addresses);
                Assert.True(Uri.TryCreate(address, UriKind.Absolute, out var uri));
                Assert.Equal(Uri.UriSchemeHttp, uri!.Scheme);
                Assert.Equal("127.0.0.1", uri.Host);
                Assert.True(uri.Port > 0);
                var client = new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(15) };
                return new KestrelParityHost(servingApp, client);
            }
            catch (Exception startupFailure)
            {
                try
                {
                    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await servingApp.StopAsync(stop.Token);
                }
                catch (Exception cleanupFailure)
                {
                    startupFailure.Data["KestrelStopFailure"] = cleanupFailure.ToString();
                }
                try { await servingApp.DisposeAsync(); }
                catch (Exception cleanupFailure)
                {
                    startupFailure.Data["KestrelDisposeFailure"] = cleanupFailure.ToString();
                }
                ExceptionDispatchInfo.Capture(startupFailure).Throw();
                throw;
            }
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
            try { Client.Dispose(); }
            catch (Exception error) { first = error; }
            try
            {
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await app.StopAsync(stop.Token);
            }
            catch (Exception error) { first ??= error; }
            try { await app.DisposeAsync(); }
            catch (Exception error) { first ??= error; }
            if (first is not null) ExceptionDispatchInfo.Capture(first).Throw();
        }
    }

    private sealed class Fixture(string owner) : IAsyncDisposable
    {
        public string Prefix { get; } = "W4ACK-" + Guid.NewGuid().ToString("N")[..12];
        private string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        private readonly List<long> companies = [];
        private readonly List<(string Table, string Name)> failures = [];
        private readonly CancellationTokenSource deadline = new(TimeSpan.FromMinutes(3));
        private KestrelParityHost? host;
        private long company, role, branch, customer;
        public long Own, OtherBranch, Foreign, Deleted, User;

        public static async Task<Fixture> Create()
        {
            static string Local(string key, string role)
            {
                var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(key) ?? "")
                {
                    Pooling = false,
                    ApplicationName = "opstrax-w4-ack-tests"
                };
                return KestrelParityHost.GuardConnection(connection.ConnectionString, role);
            }
            var f = new Fixture(Local("OPSTRAX_TEST_DB", "zayra"));
            try
            {
                var handler = ProductionCompletion();
                f.host = await KestrelParityHost.StartAsync(
                    Local("OPSTRAX_TEST_DB_APP", "opstrax_app"),
                    Local("OPSTRAX_TEST_DB_SYSTEM", "opstrax_system"),
                    handler);
                await f.Initialize();
                return f;
            }
            catch (Exception setupFailure)
            {
                try { await f.DisposeAsync(); }
                catch (Exception cleanupFailure)
                {
                    setupFailure.Data["FixtureCleanupFailure"] = cleanupFailure.ToString();
                }
                ExceptionDispatchInfo.Capture(setupFailure).Throw();
                throw;
            }
        }

        public async Task<Response> Complete(long id, object? body = null, bool authenticated = true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/coaching/tasks/{id}/complete");
            if (authenticated) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + Token);
            request.Content = new StringContent(JsonSerializer.Serialize(body ?? new { rowVersion = 0, completionNote = "Synthetic recorded outcome", afterSafetyScore = 0 }), Encoding.UTF8, "application/json");
            using var response = await host!.Client.SendAsync(request, deadline.Token);
            return new(response.StatusCode, await response.Content.ReadAsStringAsync(deadline.Token));
        }

        public Task SetAcknowledgement(string status, bool acknowledged, bool timestamp) => Execute(
            "UPDATE coaching_tasks SET status=@status,driver_acknowledged=@ack,acknowledged_at=CASE WHEN @time THEN TIMESTAMPTZ '2026-09-01 00:00:00Z' ELSE NULL END WHERE id=@id",
            ("status", status), ("ack", acknowledged), ("time", timestamp), ("id", Own));
        public Task<string> Acknowledgement() => Text("SELECT jsonb_build_array(driver_acknowledged,acknowledged_at,acknowledged_note)::text FROM coaching_tasks WHERE id=@id", ("id", Own));
        public async Task<JsonElement> TaskState()
        {
            using var json = JsonDocument.Parse(await Text("SELECT to_jsonb(t)::text FROM coaching_tasks t WHERE id=@id", ("id", Own)));
            return json.RootElement.Clone();
        }
        public Task<string> Snapshot() => Text(@"SELECT jsonb_build_object(
            'tasks',(SELECT COALESCE(jsonb_agg(to_jsonb(t) ORDER BY id),'[]') FROM coaching_tasks t WHERE company_id=ANY(@ids)),
            'notes',(SELECT COALESCE(jsonb_agg(to_jsonb(t) ORDER BY id),'[]') FROM coaching_notes t WHERE company_id=ANY(@ids)),
            'audit',(SELECT COALESCE(jsonb_agg(to_jsonb(t) ORDER BY id),'[]') FROM audit_logs t WHERE company_id=ANY(@ids)))::text", ("ids", companies.ToArray()));
        public Task<long> Count(string table) => Scalar($"SELECT COUNT(*) FROM {table} WHERE company_id=ANY(@ids)", ("ids", companies.ToArray()));

        public async Task SetPermission(string value)
        {
            await Execute("DELETE FROM role_permissions WHERE role_id=@id", ("id", role));
            await Execute("UPDATE roles SET permissions_json=@json::jsonb WHERE id=@id", ("id", role), ("json", JsonSerializer.Serialize(new[] { value })));
            await Execute("INSERT INTO role_permissions(role_id,permission_key) VALUES(@id,@permission)", ("id", role), ("permission", value));
        }
        public async Task ChangePrincipal(string state)
        {
            if (state == "revoked-session") await Execute("DELETE FROM user_sessions WHERE company_id=@id", ("id", company));
            if (state == "customer") await Execute("UPDATE users SET customer_id=@customer WHERE id=@id", ("customer", customer), ("id", User));
            if (state == "no-permission") await SetPermission("safety:view");
            if (state is "safety-module" or "driver-safety-module") await Execute("UPDATE tenant_entitlements SET enabled=FALSE WHERE company_id=@id AND module_key=@module",
                ("id", company), ("module", state == "safety-module" ? "safety" : "fleet.driver_safety"));
        }
        public async Task FailInsert(string table)
        {
            Assert.Contains(table, new[] { "audit_logs", "coaching_notes" });
            var name = "w4ack_fail_" + Guid.NewGuid().ToString("N")[..12];
            failures.Add((table, name));
            await Execute($"CREATE FUNCTION public.{name}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.company_id={company} THEN RAISE EXCEPTION 'synthetic completion persistence failure'; END IF; RETURN NEW; END $$");
            await Execute($"CREATE TRIGGER {name} BEFORE INSERT ON public.{table} FOR EACH ROW EXECUTE FUNCTION public.{name}()");
        }

        private async Task Initialize()
        {
            async Task<long> Company(string suffix)
            {
                var id = await Scalar("INSERT INTO companies(company_code,name,industry,entitlement_policy_mode) VALUES(@code,'Synthetic coaching completion','Transportation','package_allowlist') RETURNING id", ("code", Prefix + suffix));
                companies.Add(id); return id;
            }
            async Task<long> Branch(long cid, string suffix) => await Scalar("INSERT INTO branches(company_id,branch_code,name,status) VALUES(@cid,@code,'Synthetic branch','Active') RETURNING id", ("cid", cid), ("code", Prefix + suffix));
            async Task<long> Task(long cid, long bid, string suffix, bool deleted = false)
            {
                var driver = await Scalar("INSERT INTO drivers(company_id,branch_id,driver_code,full_name,status) VALUES(@cid,@branch,@code,'Synthetic driver','Available') RETURNING id", ("cid", cid), ("branch", bid), ("code", Prefix + suffix));
                return await Scalar(@"INSERT INTO coaching_tasks(company_id,branch_id,task_number,driver_id,coaching_type,title,status,driver_acknowledged,acknowledged_at,acknowledged_note,before_safety_score,deleted_at)
                    VALUES(@cid,@branch,@number,@driver,'Synthetic coaching','Synthetic task','Driver Acknowledged',TRUE,TIMESTAMPTZ '2026-09-01 00:00:00Z','Synthetic persisted acknowledgement',NULL,CASE WHEN @deleted THEN NOW() ELSE NULL END) RETURNING id",
                    ("cid", cid), ("branch", bid), ("number", Prefix + suffix), ("driver", driver), ("deleted", deleted));
            }
            company = await Company("A"); var foreignCompany = await Company("B");
            branch = await Branch(company, "A"); var otherBranch = await Branch(company, "B"); var foreignBranch = await Branch(foreignCompany, "F");
            Own = await Task(company, branch, "OWN"); OtherBranch = await Task(company, otherBranch, "OTHER");
            Foreign = await Task(foreignCompany, foreignBranch, "FOREIGN"); Deleted = await Task(company, branch, "DELETED", true);
            customer = await Scalar("INSERT INTO customers(company_id,customer_code,name,status) VALUES(@cid,@code,'Synthetic customer','Active') RETURNING id", ("cid", company), ("code", Prefix));
            role = await Scalar("INSERT INTO roles(company_id,name,permissions_json,is_system) VALUES(@cid,@name,'[\"safety:update\"]'::jsonb,FALSE) RETURNING id", ("cid", company), ("name", Prefix));
            await Execute("INSERT INTO role_permissions(role_id,permission_key) VALUES(@role,'safety:update')", ("role", role));
            User = await Scalar(@"INSERT INTO users(company_id,branch_id,role_id,role_name,full_name,email,status,permissions_json)
                VALUES(@cid,@branch,@role,@name,'Synthetic supervisor',@email,'Active','[]'::jsonb) RETURNING id",
                ("cid", company), ("branch", branch), ("role", role), ("name", Prefix), ("email", Prefix + "@example.invalid"));
            await Execute("INSERT INTO user_sessions(company_id,user_id,session_token,expires_at) VALUES(@cid,@user,@token,NOW()+INTERVAL '20 minutes')", ("cid", company), ("user", User), ("token", Token));
            foreach (var module in new[] { "safety", "fleet.driver_safety" }) await Execute("INSERT INTO tenant_entitlements(company_id,module_key,enabled) VALUES(@cid,@module,TRUE)", ("cid", company), ("module", module));
        }
        public Task<int> Execute(string sql, params (string Key, object? Value)[] values) => Execute(sql, deadline.Token, values);
        private Task<int> Execute(string sql, CancellationToken token, params (string Key, object? Value)[] values) =>
            Owner(sql, token, command => command.ExecuteNonQueryAsync(token), values);
        private Task<long> Scalar(string sql, params (string Key, object? Value)[] values) => Scalar(sql, deadline.Token, values);
        private Task<long> Scalar(string sql, CancellationToken token, params (string Key, object? Value)[] values) =>
            Owner(sql, token, async command => Convert.ToInt64(await command.ExecuteScalarAsync(token)), values);
        private Task<string> Text(string sql, params (string Key, object? Value)[] values) =>
            Owner(sql, deadline.Token, async command => (string)(await command.ExecuteScalarAsync(deadline.Token))!, values);
        private Task<long> Count(string table, CancellationToken token) =>
            Scalar($"SELECT COUNT(*) FROM {table} WHERE company_id=ANY(@ids)", token, ("ids", companies.ToArray()));
        private async Task<T> Owner<T>(string sql, CancellationToken token, Func<NpgsqlCommand, Task<T>> run, params (string Key, object? Value)[] values)
        {
            await using var connection = new NpgsqlConnection(owner); await connection.OpenAsync(token);
            await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 10 };
            foreach (var (key, value) in values) command.Parameters.AddWithValue(key, value ?? DBNull.Value);
            return await run(command);
        }
        public async ValueTask DisposeAsync()
        {
            Exception? first = null;
            async Task Attempt(Func<Task> operation)
            {
                try { await operation(); }
                catch (Exception error) { first ??= error; }
            }

            await Attempt(async () => { if (host is not null) await host.DisposeAsync(); });
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            foreach (var (table, name) in failures)
            {
                await Attempt(() => Execute($"DROP TRIGGER IF EXISTS {name} ON public.{table}", cleanup.Token));
                await Attempt(() => Execute($"DROP FUNCTION IF EXISTS public.{name}()", cleanup.Token));
            }
            if (companies.Count > 0)
            {
                foreach (var table in new[] { "authorization_decision_logs", "security_events", "audit_logs", "coaching_notes", "coaching_tasks", "user_sessions", "users", "role_permissions", "roles", "tenant_entitlements", "customers", "drivers", "branches" })
                {
                    var predicate = table == "authorization_decision_logs" ? "tenant_id=ANY(@ids)" : table == "role_permissions" ? "role_id IN(SELECT id FROM roles WHERE company_id=ANY(@ids))" : "company_id=ANY(@ids)";
                    await Attempt(() => Execute($"DELETE FROM {table} WHERE {predicate}", cleanup.Token, ("ids", companies.ToArray())));
                }
                await Attempt(async () => Assert.Equal(companies.Count, await Execute(
                    "DELETE FROM companies WHERE id=ANY(@ids) AND company_code LIKE @prefix", cleanup.Token,
                    ("ids", companies.ToArray()), ("prefix", Prefix + "%"))));
                await Attempt(async () => Assert.Equal(0, await Scalar(
                    "SELECT COUNT(*) FROM companies WHERE company_code LIKE @prefix", cleanup.Token, ("prefix", Prefix + "%"))));
                foreach (var table in new[] { "coaching_tasks", "coaching_notes", "audit_logs", "security_events", "user_sessions", "users", "roles", "drivers", "branches", "customers", "tenant_entitlements" })
                    await Attempt(async () => Assert.Equal(0, await Count(table, cleanup.Token)));
                await Attempt(async () => Assert.Equal(0, await Scalar(
                    "SELECT COUNT(*) FROM authorization_decision_logs WHERE tenant_id=ANY(@ids)", cleanup.Token,
                    ("ids", companies.ToArray()))));
            }
            await Attempt(async () => Assert.Equal(0, await Scalar(
                "SELECT COUNT(*) FROM pg_stat_activity WHERE application_name='opstrax-w4-ack-tests' AND state='idle in transaction'", cleanup.Token)));
            await Attempt(async () => Assert.Equal(0, await Scalar(
                "SELECT COUNT(*) FROM pg_locks l JOIN pg_stat_activity a ON a.pid=l.pid WHERE a.application_name='opstrax-w4-ack-tests' AND NOT l.granted", cleanup.Token)));
            deadline.Dispose();
            if (first is not null) ExceptionDispatchInfo.Capture(first).Throw();
        }
    }
}
