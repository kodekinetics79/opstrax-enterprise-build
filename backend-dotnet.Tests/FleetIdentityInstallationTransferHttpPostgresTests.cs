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
using Xunit.Abstractions;

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
    private readonly ITestOutputHelper output;

    public FleetIdentityInstallationTransferHttpPostgresTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public async Task RegisteredTransfer_ConcurrentSameKeyProducesOneFreshOneReplayAndRefreshesPersistedHistory()
    {
        await WithFixtureAsync(async fixture =>
        {
        var tenantBBefore = await fixture.TenantBProjectionSnapshotAsync();
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
        Assert.Equal("Installed", replayData.GetProperty("status").GetString());
        Assert.True(replayData.GetProperty("rowVersion").GetInt64() >= 0);
        Assert.Equal(TransferAt, freshData.GetProperty("effectiveFrom").GetDateTimeOffset());
        Assert.Equal(TransferAt, replayData.GetProperty("effectiveFrom").GetDateTimeOffset());

        var afterConcurrent = await fixture.BusinessSnapshotAsync();
        var sequentialTrace = Guid.NewGuid().ToString("D");
        Assert.DoesNotContain(sequentialTrace, new[] { fresh.Trace, replay.Trace });
        var sequential = await fixture.TransferAsync(sequentialTrace, currentInstallationId, currentRowVersion);
        Assert.Equal(HttpStatusCode.OK, sequential.Status);
        Assert.Equal("Device transfer already recorded", Message(sequential));
        using (var sequentialJson = JsonDocument.Parse(sequential.Body))
        {
            var sequentialData = Data(sequentialJson.RootElement);
            AssertExactProperties(sequentialData, "id", "replacedInstallationId", "deviceId", "vehicleId", "status", "effectiveFrom", "rowVersion");
            Assert.Equal(successorId, sequentialData.GetProperty("id").GetInt64());
            Assert.Equal(fixture.PriorInstallation, sequentialData.GetProperty("replacedInstallationId").GetInt64());
            Assert.Equal(fixture.DeviceA, sequentialData.GetProperty("deviceId").GetInt64());
            Assert.Equal(fixture.VehicleB, sequentialData.GetProperty("vehicleId").GetInt64());
            Assert.Equal("Installed", sequentialData.GetProperty("status").GetString());
            Assert.Equal(TransferAt, sequentialData.GetProperty("effectiveFrom").GetDateTimeOffset());
            Assert.True(sequentialData.GetProperty("rowVersion").GetInt64() >= 0);
        }
        Assert.Equal(afterConcurrent, await fixture.BusinessSnapshotAsync());

        var changed = await fixture.TransferAsync(Guid.NewGuid().ToString("D"), currentInstallationId,
            currentRowVersion, assignmentReason: "Different material assignment");
        AssertFailure(changed, HttpStatusCode.Conflict, fixture.Prefix, fixture.PositiveLeakageIdentifiers);
        Assert.Equal(afterConcurrent, await fixture.BusinessSnapshotAsync());

        var refresh = await fixture.GetDeviceAsync(fixture.DeviceA);
        Assert.Equal(HttpStatusCode.OK, refresh.Status);
        using var refreshJson = JsonDocument.Parse(refresh.Body);
        var refreshData = Data(refreshJson.RootElement);
        var refreshedCurrent = refreshData.GetProperty("currentInstallation");
        Assert.Equal(successorId, refreshedCurrent.GetProperty("id").GetInt64());
        Assert.Equal(fixture.VehicleB, refreshedCurrent.GetProperty("vehicleId").GetInt64());
        Assert.Equal("Installed", refreshedCurrent.GetProperty("status").GetString());
        Assert.Equal(TransferAt, refreshedCurrent.GetProperty("effectiveFrom").GetDateTimeOffset());
        var history = refreshData.GetProperty("installationHistory");
        Assert.Equal(2, history.GetArrayLength());
        var replacement = history[0];
        var prior = history[1];
        Assert.Equal(successorId, replacement.GetProperty("id").GetInt64());
        Assert.Equal(fixture.VehicleB, replacement.GetProperty("vehicleId").GetInt64());
        Assert.Equal("Installed", replacement.GetProperty("status").GetString());
        Assert.Equal(TransferAt, replacement.GetProperty("effectiveFrom").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, replacement.GetProperty("effectiveTo").ValueKind);
        Assert.Equal(fixture.PriorInstallation, prior.GetProperty("id").GetInt64());
        Assert.Equal(fixture.VehicleA, prior.GetProperty("vehicleId").GetInt64());
        Assert.Equal("Removed", prior.GetProperty("status").GetString());
        Assert.Equal(TransferAt, prior.GetProperty("effectiveTo").GetDateTimeOffset());

        await fixture.AssertPersistedOutcomeAsync(successorId, fresh.Trace);
        Assert.Equal(tenantBBefore, await fixture.TenantBProjectionSnapshotAsync());
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
        AssertFailure(response, (HttpStatusCode)expectedStatus, fixture.Prefix, fixture.FailureLeakageIdentifiers(state));
        Assert.Equal(before, await fixture.BusinessSnapshotAsync());
        });
    }

    [Fact]
    public async Task RegisteredTransfer_LateAuditFailureReturnsSafe500AndRollsBackEverything()
    {
        await WithFixtureAsync(async fixture =>
        {
        await fixture.InstallLateAuditFailureAsync();
        var expectedSourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fixture.LateFailureSource))).ToLowerInvariant();
        Assert.Equal(expectedSourceSha256, fixture.LateFailureSourceSha256);
        Assert.Matches("^[a-f0-9]{64}$", fixture.LateFailureSourceSha256);
        output.WriteLine($"Late-audit failure source SHA-256: {fixture.LateFailureSourceSha256}");
        var before = await fixture.BusinessSnapshotAsync();
        var response = await fixture.TransferAsync(Guid.NewGuid().ToString("D"), fixture.PriorInstallation, 1,
            key: fixture.LateFailureKey);
        AssertFailure(response, HttpStatusCode.InternalServerError, fixture.Prefix, fixture.PositiveLeakageIdentifiers);
        Assert.DoesNotContain("synthetic_late_audit_failure", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await fixture.BusinessSnapshotAsync());
        });
    }

    private static async Task WithFixtureAsync(Func<Fixture, Task> body)
    {
        var fixture = await Fixture.CreateAsync();
        var failures = new List<Exception>();
        try { await body(fixture); }
        catch (Exception error) { failures.Add(error); }
        try { await fixture.DisposeAsync(); }
        catch (Exception cleanupFailure) { failures.Add(cleanupFailure); }
        ThrowFailures("Registered transfer journey and cleanup failures.", failures);
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

    private static void AssertFailure(Response response, HttpStatusCode expected, string secret, params long[] numericSecrets)
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
        foreach (var identifier in numericSecrets.Distinct())
            Assert.DoesNotMatch($@"(?<!\d){identifier}(?!\d)", response.Body);
    }

    private static void ThrowFailures(string message, IReadOnlyCollection<Exception> failures)
    {
        if (failures.Count == 0) return;
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures.Single()).Throw();
        throw new AggregateException(message, failures);
    }

    private static bool TenantBound(string? expression)
        => expression?.Contains("current_tenant_id", StringComparison.OrdinalIgnoreCase) == true;

    private static DateTimeOffset UtcOffset(object value) => value switch
    {
        DateTimeOffset offset => offset.ToUniversalTime(),
        DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
        _ => DateTimeOffset.Parse(value.ToString()!).ToUniversalTime()
    };

    private sealed record Response(HttpStatusCode Status, string Body, string Trace);
    private sealed record PolicyPosture(string Name, string Command, string Roles, string? UsingExpression,
        string? WithCheckExpression, bool Applicable);
    private sealed record RlsPosture(string Table, bool Enabled, bool Forced, bool Active,
        IReadOnlyList<PolicyPosture> Policies, long Tenant, string Role);
    private sealed record BackendIdentity(int Pid, string DatabaseUser, string ApplicationName, DateTimeOffset BackendStart);
    private sealed record AdvisoryLockIdentity(long Database, long ClassId, long ObjectId, int ObjectSubId, string Mode);
    private sealed record OwnerLockEvidence(BackendIdentity Backend, string ResourceIdentity, long ResourceHash,
        AdvisoryLockIdentity Lock);

    private sealed class RequestEvidence
    {
        public ConcurrentDictionary<string, BackendIdentity> BackendByTrace { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, IReadOnlyList<RlsPosture>> RlsByTrace { get; } = new(StringComparer.Ordinal);
    }

    private sealed class KestrelParityHost(WebApplication app, HttpClient client, RequestEvidence evidence) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;
        public RequestEvidence Evidence { get; } = evidence;

        public static async Task<KestrelParityHost> StartAsync(string appConnection, string systemConnection,
            string appApplicationName, string systemApplicationName)
        {
            var runtime = GuardConnection(appConnection, "opstrax_app", appApplicationName);
            var system = GuardConnection(systemConnection, "opstrax_system", systemApplicationName);
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
                                   p.polname policy_name,p.polcmd::text policy_command,
                                   COALESCE((SELECT string_agg(CASE WHEN policy_role=0 THEN 'public'
                                                                      ELSE pg_get_userbyid(policy_role) END,',' ORDER BY policy_role)
                                               FROM unnest(p.polroles) policy_role),'') policy_roles,
                                   pg_get_expr(p.polqual,p.polrelid) using_expression,
                                   pg_get_expr(p.polwithcheck,p.polrelid) with_check_expression,
                                   COALESCE(EXISTS(SELECT 1 FROM unnest(p.polroles) policy_role
                                                   WHERE CASE WHEN policy_role=0 THEN TRUE
                                                              ELSE pg_has_role(current_user,policy_role,'member') END),FALSE) applicable
                              FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                              LEFT JOIN pg_policy p ON p.polrelid=c.oid
                             WHERE n.nspname='public' AND c.relname=ANY(@tables)
                             ORDER BY c.relname,p.polname",
                            command => command.Parameters.AddWithValue("@tables", new[]
                            {
                                "device_installations", "eld_devices", "idempotency_keys", "device_state_transitions", "audit_logs"
                            }), http.RequestAborted);
                        var posture = rows.GroupBy(row => row["tableName"]!.ToString()!, StringComparer.Ordinal)
                            .Select(group =>
                            {
                                var first = group.First();
                                var policies = group.Where(row => row["policyName"] is not null)
                                    .Select(row => new PolicyPosture(
                                        row["policyName"]!.ToString()!, row["policyCommand"]?.ToString() ?? string.Empty,
                                        row["policyRoles"]?.ToString() ?? string.Empty,
                                        row["usingExpression"]?.ToString(), row["withCheckExpression"]?.ToString(),
                                        Convert.ToBoolean(row["applicable"]))).ToArray();
                                return new RlsPosture(group.Key, Convert.ToBoolean(first["enabled"]), Convert.ToBoolean(first["forced"]),
                                    Convert.ToBoolean(first["active"]), policies, Convert.ToInt64(first["tenant"]),
                                    first["role"]?.ToString() ?? string.Empty);
                            }).OrderBy(item => item.Table, StringComparer.Ordinal).ToArray();
                        Assert.Equal(5, posture.Length);
                        Assert.All(posture, item =>
                        {
                            Assert.Equal("opstrax_app", item.Role);
                            Assert.Equal(companyId, item.Tenant);
                            if (item.Enabled)
                            {
                                Assert.True(item.Active, $"RLS was enabled but inactive for {item.Table}.");
                                var applicablePolicies = item.Policies.Where(policy => policy.Applicable).ToArray();
                                Assert.NotEmpty(applicablePolicies);
                                Assert.Contains(applicablePolicies, policy =>
                                    TenantBound(policy.UsingExpression) || TenantBound(policy.WithCheckExpression));
                            }
                            else Assert.False(item.Active, $"RLS was disabled but reported active for {item.Table}.");
                        });
                        var evidence = http.RequestServices.GetRequiredService<RequestEvidence>();
                        var backendRow = Assert.Single(await db.QueryAsync(@"SELECT a.pid,a.usename database_user,
                                   a.application_name,a.backend_start
                              FROM pg_stat_activity a WHERE a.pid=pg_backend_pid()", ct: http.RequestAborted));
                        var backend = new BackendIdentity(Convert.ToInt32(backendRow["pid"]),
                            backendRow["databaseUser"]?.ToString() ?? string.Empty,
                            backendRow["applicationName"]?.ToString() ?? string.Empty,
                            UtcOffset(backendRow["backendStart"]!));
                        Assert.Equal("opstrax_app", backend.DatabaseUser);
                        Assert.Equal(appApplicationName, backend.ApplicationName);
                        evidence.BackendByTrace[http.TraceIdentifier] = backend;
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
                var failures = new List<Exception> { startupFailure };
                try { using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await servingApp.StopAsync(stop.Token); }
                catch (Exception cleanupFailure) { failures.Add(new InvalidOperationException("Kestrel startup stop failed.", cleanupFailure)); }
                try { await servingApp.DisposeAsync(); }
                catch (Exception cleanupFailure) { failures.Add(new InvalidOperationException("Kestrel startup application disposal failed.", cleanupFailure)); }
                ThrowFailures("Kestrel startup and cleanup failures.", failures);
                throw;
            }
        }

        private static void AssertRouteUnique(IEnumerable<RouteEndpoint> endpoints, string pattern, string method)
        {
            var matches = endpoints.Where(endpoint => endpoint.RoutePattern.RawText == pattern &&
                endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) == true).ToArray();
            Assert.Single(matches);
        }

        internal static string GuardConnection(string connectionString, string expectedRole, string expectedApplicationName)
        {
            Assert.False(string.IsNullOrWhiteSpace(connectionString), "Explicit local database identity required; no fallback.");
            var connection = new NpgsqlConnectionStringBuilder(connectionString);
            Assert.Equal("127.0.0.1", connection.Host);
            Assert.Equal(5433, connection.Port);
            Assert.Equal("opstrax_local", connection.Database);
            Assert.Equal(expectedRole, connection.Username);
            Assert.Equal(expectedApplicationName, connection.ApplicationName);
            Assert.False(connection.Pooling, "The isolated HTTP fixture requires pooling disabled.");
            connection.Timeout = 5;
            connection.CommandTimeout = 10;
            return connection.ConnectionString;
        }

        public async ValueTask DisposeAsync()
        {
            var failures = new List<Exception>();
            try { Client.Dispose(); } catch (Exception error) { failures.Add(new InvalidOperationException("HTTP client disposal failed.", error)); }
            try { using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await app.StopAsync(stop.Token); }
            catch (Exception error) { failures.Add(new InvalidOperationException("Kestrel stop failed.", error)); }
            try { await app.DisposeAsync(); }
            catch (Exception error) { failures.Add(new InvalidOperationException("Kestrel application disposal failed.", error)); }
            ThrowFailures("HTTP client and Kestrel cleanup failures.", failures);
        }
    }

    private sealed class Fixture(string ownerConnection, string nonce, string ownerApplicationName,
        string appApplicationName, string systemApplicationName) : IAsyncDisposable
    {
        public string Prefix { get; } = "G5HTTP-" + nonce;
        private string OwnerApplicationName { get; } = ownerApplicationName;
        private string AppApplicationName { get; } = appApplicationName;
        private string SystemApplicationName { get; } = systemApplicationName;
        private string[] FixtureApplicationNames => [OwnerApplicationName, AppApplicationName, SystemApplicationName];
        public string LateFailureKey { get; } = "late-" + Guid.NewGuid().ToString("N");
        private string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        private string TransferKey { get; } = "transfer-" + Guid.NewGuid().ToString("N");
        private readonly CancellationTokenSource deadline = new(TimeSpan.FromMinutes(4));
        private readonly List<long> companies = [];
        private KestrelParityHost? host;
        private string? failureTrigger;
        public string LateFailureSourceSha256 { get; private set; } = string.Empty;
        public string LateFailureSource { get; private set; } = string.Empty;
        private long companyA, companyB, branchA, branchOther, branchB, role, user;
        public long DeviceA, DeviceB, VehicleA, VehicleB, ForeignVehicle, OtherBranchVehicle, PriorInstallation;
        public long[] PositiveLeakageIdentifiers => [companyA, companyB, DeviceA, DeviceB, VehicleA, VehicleB, ForeignVehicle, OtherBranchVehicle];

        public static async Task<Fixture> CreateAsync()
        {
            var nonce = Guid.NewGuid().ToString("N")[..12];
            var ownerApplicationName = "opstrax-g5-xfer-owner-" + nonce;
            var appApplicationName = "opstrax-g5-xfer-app-" + nonce;
            var systemApplicationName = "opstrax-g5-xfer-system-" + nonce;
            static string Local(string key, string role, string applicationName)
            {
                var value = Environment.GetEnvironmentVariable(key) ?? string.Empty;
                var connection = new NpgsqlConnectionStringBuilder(value)
                {
                    Pooling = false,
                    ApplicationName = applicationName
                };
                return KestrelParityHost.GuardConnection(connection.ConnectionString, role, applicationName);
            }
            var fixture = new Fixture(Local("OPSTRAX_TEST_DB", "zayra", ownerApplicationName), nonce,
                ownerApplicationName, appApplicationName, systemApplicationName);
            try
            {
                fixture.host = await KestrelParityHost.StartAsync(
                    Local("OPSTRAX_TEST_DB_APP", "opstrax_app", appApplicationName),
                    Local("OPSTRAX_TEST_DB_SYSTEM", "opstrax_system", systemApplicationName),
                    appApplicationName, systemApplicationName);
                await fixture.InitializeAsync();
                return fixture;
            }
            catch (Exception setupFailure)
            {
                var failures = new List<Exception> { setupFailure };
                try { await fixture.DisposeAsync(); }
                catch (Exception cleanupFailure) { failures.Add(cleanupFailure); }
                ThrowFailures("Fixture setup and cleanup failures.", failures);
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
            var failures = new List<Exception>();
            var responses = Array.Empty<Response>();
            var traceA = Guid.NewGuid().ToString("D");
            var traceB = Guid.NewGuid().ToString("D");
            Assert.NotEqual(traceA, traceB);
            try
            {
                owner = new NpgsqlConnection(ownerConnection);
                await owner.OpenAsync(deadline.Token);
                transaction = await owner.BeginTransactionAsync(deadline.Token);
                var resourceIdentity = $"device-install-resource:{companyA}:device:{DeviceA}";
                await using (var lockCommand = new NpgsqlCommand(
                    "SELECT pg_advisory_xact_lock(hashtextextended(@identity,0)),pg_backend_pid()", owner, transaction))
                {
                    lockCommand.Parameters.AddWithValue("@identity", resourceIdentity);
                    await lockCommand.ExecuteNonQueryAsync(deadline.Token);
                }
                var ownerEvidence = await ReadOwnerLockEvidenceAsync(owner, transaction, resourceIdentity);
                requests =
                [
                    TransferAsync(traceA, currentInstallation, currentVersion),
                    TransferAsync(traceB, currentInstallation, currentVersion)
                ];
                await WaitForBothBlockedAsync(ownerEvidence, traceA, traceB, requests);
            }
            catch (Exception error) { failures.Add(error); }
            finally
            {
                async Task Cleanup(string step, Func<Task> action)
                {
                    try { await action(); }
                    catch (Exception error) { failures.Add(new InvalidOperationException(step + " failed.", error)); }
                }
                // Owner release is mandatory before request drain so the two exact
                // registered transfers can settle without a cleanup deadlock.
                if (transaction is not null) await Cleanup("Owner rollback", async () => await transaction.RollbackAsync(CancellationToken.None));
                if (transaction is not null) await Cleanup("Owner transaction disposal", async () => await transaction.DisposeAsync());
                if (owner is not null) await Cleanup("Owner connection disposal", async () => await owner.DisposeAsync());
                if (requests.Length > 0)
                    await Cleanup("Concurrent request drain", async () =>
                    {
                        responses = await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(30));
                    });
            }
            ThrowFailures("Concurrent Owner/request failures.", failures);
            return responses;
        }

        private async Task<OwnerLockEvidence> ReadOwnerLockEvidenceAsync(NpgsqlConnection owner,
            NpgsqlTransaction transaction, string resourceIdentity)
        {
            await using var command = new NpgsqlCommand(@"SELECT a.pid,a.usename database_user,a.application_name,a.backend_start,
                       hashtextextended(@identity,0) resource_hash,l.database::bigint lock_database,
                       l.classid::bigint lock_classid,l.objid::bigint lock_objid,l.objsubid,l.mode
                  FROM pg_stat_activity a JOIN pg_locks l ON l.pid=a.pid
                 WHERE a.pid=pg_backend_pid() AND l.locktype='advisory' AND l.granted", owner, transaction);
            command.Parameters.AddWithValue("@identity", resourceIdentity);
            await using var reader = await command.ExecuteReaderAsync(deadline.Token);
            Assert.True(await reader.ReadAsync(deadline.Token), "Owner advisory lock evidence was absent.");
            var resourceHash = reader.GetInt64(reader.GetOrdinal("resource_hash"));
            var expectedClassId = (long)unchecked((uint)(resourceHash >> 32));
            var expectedObjectId = (long)unchecked((uint)resourceHash);
            var backend = new BackendIdentity(reader.GetInt32(reader.GetOrdinal("pid")),
                reader.GetString(reader.GetOrdinal("database_user")), reader.GetString(reader.GetOrdinal("application_name")),
                UtcOffset(reader.GetValue(reader.GetOrdinal("backend_start"))));
            var lockIdentity = new AdvisoryLockIdentity(reader.GetInt64(reader.GetOrdinal("lock_database")),
                reader.GetInt64(reader.GetOrdinal("lock_classid")), reader.GetInt64(reader.GetOrdinal("lock_objid")),
                reader.GetInt32(reader.GetOrdinal("objsubid")), reader.GetString(reader.GetOrdinal("mode")));
            Assert.False(await reader.ReadAsync(deadline.Token), "Owner held more than the exact fixture advisory lock.");
            Assert.Equal(owner.ProcessID, backend.Pid);
            Assert.Equal("zayra", backend.DatabaseUser);
            Assert.Equal(OwnerApplicationName, backend.ApplicationName);
            Assert.Equal(expectedClassId, lockIdentity.ClassId);
            Assert.Equal(expectedObjectId, lockIdentity.ObjectId);
            Assert.Equal(1, lockIdentity.ObjectSubId);
            Assert.Equal("ExclusiveLock", lockIdentity.Mode);
            Assert.True(lockIdentity.Database > 0);
            return new OwnerLockEvidence(backend, resourceIdentity, resourceHash, lockIdentity);
        }

        private async Task WaitForBothBlockedAsync(OwnerLockEvidence owner, string traceA, string traceB,
            Task<Response>[] requests)
        {
            var stop = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < stop)
            {
                Assert.DoesNotContain(requests, request => request.IsFaulted || request.IsCanceled || request.IsCompletedSuccessfully);
                if (host!.Evidence.BackendByTrace.TryGetValue(traceA, out var backendA) &&
                    host.Evidence.BackendByTrace.TryGetValue(traceB, out var backendB))
                {
                    Assert.NotEqual(backendA.Pid, backendB.Pid);
                    foreach (var backend in new[] { backendA, backendB })
                    {
                        Assert.Equal("opstrax_app", backend.DatabaseUser);
                        Assert.Equal(AppApplicationName, backend.ApplicationName);
                        Assert.NotEqual(owner.Backend.Pid, backend.Pid);
                    }
                    var pids = new[] { backendA.Pid, backendB.Pid };
                    var rows = await QueryAsync(@"SELECT a.pid,a.usename database_user,a.application_name,a.backend_start,
                               a.wait_event_type,a.query,l.database::bigint lock_database,
                               l.classid::bigint lock_classid,l.objid::bigint lock_objid,l.objsubid,l.mode,l.granted,
                               pg_blocking_pids(a.pid) blockers
                          FROM pg_stat_activity a JOIN pg_locks l ON l.pid=a.pid
                         WHERE a.pid=ANY(@pids) AND a.wait_event_type='Lock'
                           AND l.locktype='advisory' AND NOT l.granted
                           AND l.database::bigint=@database AND l.classid::bigint=@classid
                           AND l.objid::bigint=@objid AND l.objsubid=@objsubid AND l.mode=@mode",
                        ("pids", pids), ("database", owner.Lock.Database), ("classid", owner.Lock.ClassId),
                        ("objid", owner.Lock.ObjectId), ("objsubid", owner.Lock.ObjectSubId), ("mode", owner.Lock.Mode));
                    if (rows.Count == 2)
                    {
                        Assert.Equal(pids.Order(), rows.Select(row => Convert.ToInt32(row["pid"])).Order());
                        foreach (var backend in new[] { backendA, backendB })
                        {
                            var row = Assert.Single(rows, candidate => Convert.ToInt32(candidate["pid"]) == backend.Pid);
                            Assert.Equal(backend.DatabaseUser, row["databaseUser"]?.ToString());
                            Assert.Equal(backend.ApplicationName, row["applicationName"]?.ToString());
                            Assert.Equal(backend.BackendStart, UtcOffset(row["backendStart"]!));
                            Assert.Equal("Lock", row["waitEventType"]?.ToString());
                            Assert.Contains("pg_advisory_xact_lock", row["query"]?.ToString(), StringComparison.Ordinal);
                            Assert.Equal(owner.Lock.Database, Convert.ToInt64(row["lockDatabase"]));
                            Assert.Equal(owner.Lock.ClassId, Convert.ToInt64(row["lockClassid"]));
                            Assert.Equal(owner.Lock.ObjectId, Convert.ToInt64(row["lockObjid"]));
                            Assert.Equal(owner.Lock.ObjectSubId, Convert.ToInt32(row["objsubid"]));
                            Assert.Equal(owner.Lock.Mode, row["mode"]?.ToString());
                            Assert.False(Convert.ToBoolean(row["granted"]));
                            Assert.Equal(new[] { owner.Backend.Pid }, Assert.IsType<int[]>(row["blockers"]));
                        }
                        Assert.Equal($"device-install-resource:{companyA}:device:{DeviceA}", owner.ResourceIdentity);
                        Assert.Equal((long)unchecked((uint)(owner.ResourceHash >> 32)), owner.Lock.ClassId);
                        Assert.Equal((long)unchecked((uint)owner.ResourceHash), owner.Lock.ObjectId);
                        return;
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

        public long[] FailureLeakageIdentifiers(string state) => state switch
        {
            "foreign-device" => [companyB, DeviceB, VehicleB],
            "foreign-target" => [companyA, companyB, DeviceA, ForeignVehicle],
            "out-of-branch-target" => [companyA, DeviceA, OtherBranchVehicle],
            _ => [companyA, DeviceA, VehicleB]
        };

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
            LateFailureSource = $@"CREATE FUNCTION public.{failureTrigger}() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN IF NEW.company_id={companyA} THEN RAISE EXCEPTION 'synthetic_late_audit_failure'; END IF; RETURN NEW; END $$";
            LateFailureSourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(LateFailureSource))).ToLowerInvariant();
            await ExecuteAsync(LateFailureSource);
            await ExecuteAsync($"CREATE TRIGGER {failureTrigger} BEFORE INSERT ON public.audit_logs FOR EACH ROW EXECUTE FUNCTION public.{failureTrigger}()");
        }

        public Task<string> BusinessSnapshotAsync() => TextAsync(@"SELECT jsonb_build_object(
            'installations',(SELECT COALESCE(jsonb_agg(to_jsonb(x) ORDER BY x.id),'[]') FROM device_installations x WHERE x.company_id=ANY(@ids)),
            'devices',(SELECT COALESCE(jsonb_agg(to_jsonb(x) ORDER BY x.id),'[]') FROM eld_devices x WHERE x.company_id=ANY(@ids)),
            'ledger',(SELECT COALESCE(jsonb_agg(to_jsonb(x) ORDER BY x.id),'[]') FROM idempotency_keys x WHERE x.tenant_id=ANY(@ids)),
            'transitions',(SELECT COALESCE(jsonb_agg(to_jsonb(x) ORDER BY x.id),'[]') FROM device_state_transitions x WHERE x.company_id=ANY(@ids)),
            'audit',(SELECT COALESCE(jsonb_agg(to_jsonb(x) ORDER BY x.id),'[]') FROM audit_logs x WHERE x.company_id=ANY(@ids)))::text",
            ("ids", companies.ToArray()));

        public Task<string> TenantBProjectionSnapshotAsync() => TextAsync(@"SELECT jsonb_build_object(
            'tenant',@cid,
            'device',(SELECT to_jsonb(d) FROM eld_devices d WHERE d.company_id=@cid AND d.id=@device),
            'vehicle',(SELECT to_jsonb(v) FROM vehicles v WHERE v.company_id=@cid AND v.id=@vehicle))::text",
            ("cid", companyB), ("device", DeviceB), ("vehicle", ForeignVehicle));

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
            Assert.Equal(TransferAt, UtcOffset(rows[1]["effectiveFrom"]!));
            Assert.Null(rows[1]["effectiveTo"]);
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
            IReadOnlyList<RlsPosture>? baseline = null;
            Assert.All(traces, trace =>
            {
                Assert.True(host!.Evidence.RlsByTrace.TryGetValue(trace, out var posture), $"Missing in-request RLS posture for {trace}.");
                Assert.Equal(5, posture!.Count);
                Assert.Equal(new[] { "audit_logs", "device_installations", "device_state_transitions", "eld_devices", "idempotency_keys" },
                    posture.Select(item => item.Table).Order(StringComparer.Ordinal));
                Assert.All(posture, item =>
                {
                    Assert.Equal("opstrax_app", item.Role);
                    Assert.Equal(companyA, item.Tenant);
                    Assert.All(item.Policies, policy =>
                    {
                        Assert.False(string.IsNullOrWhiteSpace(policy.Name));
                        Assert.Contains(policy.Command, new[] { "r", "a", "w", "d", "*" });
                        Assert.False(string.IsNullOrWhiteSpace(policy.Roles));
                    });
                    if (item.Enabled)
                    {
                        Assert.True(item.Active);
                        var applicable = item.Policies.Where(policy => policy.Applicable).ToArray();
                        Assert.NotEmpty(applicable);
                        Assert.Contains(applicable, policy =>
                            TenantBound(policy.UsingExpression) || TenantBound(policy.WithCheckExpression));
                    }
                    else Assert.False(item.Active);
                });
                if (baseline is null) baseline = posture;
                else Assert.Equal(baseline.Select(PostureIdentity), posture.Select(PostureIdentity));
            });
        }

        private static string PostureIdentity(RlsPosture posture) => JsonSerializer.Serialize(new
        {
            posture.Table,
            posture.Enabled,
            posture.Forced,
            posture.Active,
            posture.Tenant,
            posture.Role,
            Policies = posture.Policies.Select(policy => new
            {
                policy.Name,
                policy.Command,
                policy.Roles,
                policy.UsingExpression,
                policy.WithCheckExpression,
                policy.Applicable
            }).ToArray()
        });

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
            var failures = new List<Exception>();
            async Task Attempt(string step, Func<Task> action)
            {
                try { await action(); }
                catch (Exception error) { failures.Add(new InvalidOperationException(step + " failed.", error)); }
            }

            // Every request is awaited by its caller. The host is stopped before
            // database artifacts are removed, so no request can race cleanup.
            if (host is not null) await Attempt("HTTP client and Kestrel cleanup", async () => await host.DisposeAsync());
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            if (failureTrigger is not null)
            {
                await Attempt("Late-audit trigger cleanup", () => ExecuteAsync($"DROP TRIGGER IF EXISTS {failureTrigger} ON public.audit_logs", cleanup.Token));
                await Attempt("Late-audit function cleanup", () => ExecuteAsync($"DROP FUNCTION IF EXISTS public.{failureTrigger}()", cleanup.Token));
                await Attempt("Late-audit catalog postflight", async () => Assert.Equal(0, await ScalarAsync(@"SELECT
                    (SELECT COUNT(*) FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid
                        JOIN pg_namespace n ON n.oid=c.relnamespace
                       WHERE n.nspname='public' AND c.relname='audit_logs' AND t.tgname=@name AND NOT t.tgisinternal)+
                    (SELECT COUNT(*) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
                       WHERE n.nspname='public' AND p.proname=@name AND p.pronargs=0)", cleanup.Token, ("name", failureTrigger))));
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
                    await Attempt(table + " row cleanup", () => ExecuteAsync($"DELETE FROM {table} WHERE {predicate}", cleanup.Token,
                        ("ids", companies.ToArray())));
                }
                await Attempt("Company row cleanup", async () => Assert.Equal(companies.Count, await ExecuteAsync(
                    "DELETE FROM companies WHERE id=ANY(@ids) AND company_code LIKE @prefix", cleanup.Token,
                    ("ids", companies.ToArray()), ("prefix", Prefix + "%"))));
                await Attempt("Company residue postflight", async () => Assert.Equal(0, await ScalarAsync(
                    "SELECT COUNT(*) FROM companies WHERE company_code LIKE @prefix", cleanup.Token, ("prefix", Prefix + "%"))));
                await Attempt("Tenant residue postflight", async () => Assert.Equal(0, await ScalarAsync(@"SELECT
                    (SELECT COUNT(*) FROM device_installations WHERE company_id=ANY(@ids))+
                    (SELECT COUNT(*) FROM eld_devices WHERE company_id=ANY(@ids))+
                    (SELECT COUNT(*) FROM idempotency_keys WHERE tenant_id=ANY(@ids))+
                    (SELECT COUNT(*) FROM device_state_transitions WHERE company_id=ANY(@ids))+
                    (SELECT COUNT(*) FROM audit_logs WHERE company_id=ANY(@ids))+
                    (SELECT COUNT(*) FROM user_sessions WHERE company_id=ANY(@ids))", cleanup.Token, ("ids", companies.ToArray()))));
            }
            await Attempt("Exact fixture session residue postflight", async () => Assert.Equal(0, await ScalarAsync(
                "SELECT COUNT(*) FROM pg_stat_activity WHERE application_name=ANY(@names) AND pid<>pg_backend_pid()", cleanup.Token,
                ("names", FixtureApplicationNames))));
            await Attempt("Exact fixture idle-transaction residue postflight", async () => Assert.Equal(0, await ScalarAsync(
                "SELECT COUNT(*) FROM pg_stat_activity WHERE application_name=ANY(@names) AND pid<>pg_backend_pid() AND state='idle in transaction'", cleanup.Token,
                ("names", FixtureApplicationNames))));
            await Attempt("Exact fixture wait residue postflight", async () => Assert.Equal(0, await ScalarAsync(@"SELECT COUNT(*) FROM pg_locks l
                JOIN pg_stat_activity a ON a.pid=l.pid WHERE a.application_name=ANY(@names)
                  AND a.pid<>pg_backend_pid() AND NOT l.granted", cleanup.Token, ("names", FixtureApplicationNames))));
            await Attempt("Fixture deadline disposal", () =>
            {
                deadline.Dispose();
                return Task.CompletedTask;
            });
            ThrowFailures("Fixture cleanup failures.", failures);
        }
    }
}
