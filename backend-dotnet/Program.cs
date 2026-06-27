using Opstrax.Api;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Middleware;
using Opstrax.Api.Services;
using System.Collections.Concurrent;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<Database>();
builder.Services.AddHttpClient(); // POD asset proxy (token-scoped public POD delivery)
builder.Services.AddScoped<AuditService>();
builder.Services.AddSingleton<Batch1SchemaService>();
builder.Services.AddSingleton<Batch2SchemaService>();
builder.Services.AddSingleton<Batch3SchemaService>();
builder.Services.AddSingleton<Batch4SchemaService>();
builder.Services.AddSingleton<Batch5SchemaService>();
builder.Services.AddSingleton<Batch6SchemaService>();
builder.Services.AddSingleton<Batch7SchemaService>();
builder.Services.AddSingleton<TelemetrySchemaService>();
builder.Services.AddSingleton<SafetySchemaService>();
builder.Services.AddSingleton<TripSchemaService>();
builder.Services.AddSingleton<MaintenanceSchemaService>();
builder.Services.AddSingleton<DispatchSchemaService>();
builder.Services.AddSingleton<CustomerVisibilitySchemaService>();
builder.Services.AddSingleton<DriverSchemaService>();
builder.Services.AddSingleton<NotificationSchemaService>();
builder.Services.AddSingleton<AlertWorkflowSchemaService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddSingleton<ReportingSchemaService>();
builder.Services.AddSingleton<ObservabilitySchemaService>();
builder.Services.AddSingleton<ServiceRunTracker>();
builder.Services.AddSingleton<ConfigValidationService>();
builder.Services.AddScoped<IncidentService>();
builder.Services.AddScoped<OpsMetricsService>();
// P10 Security + Compliance
builder.Services.AddSingleton<SecuritySchemaService>();
// Platform Admin — global SaaS business control plane (separate from tenant admin)
builder.Services.AddSingleton<PlatformSchemaService>();
// Revenue foundation — module-package catalog, usage meters/events, pricing, overrides
builder.Services.AddSingleton<RevenueSchemaService>();
builder.Services.AddScoped<EntitlementService>();
// Market-pack engine (Canada/NA + Saudi/GCC) — regional capability + compliance
builder.Services.AddSingleton<MarketPackSchemaService>();
builder.Services.AddSingleton<Opstrax.Api.Seed.MarketPackSeeder>();
// Fleet TMS (PR1) — shipment lifecycle, POD workflow & public tracking (additive)
builder.Services.AddSingleton<FleetTmsSchemaService>();
builder.Services.AddSingleton<FleetTmsColdChainSchemaService>();
builder.Services.AddSingleton<FleetTmsLogisticsSchemaService>();
builder.Services.AddSingleton<Opstrax.Api.Seed.FleetTmsSeeder>();
builder.Services.AddScoped<SecuritySettingsService>();
builder.Services.AddScoped<SecurityEventService>();
builder.Services.AddScoped<SsoConnectionService>();
builder.Services.AddScoped<AccessReviewService>();
builder.Services.AddScoped<ComplianceService>();
builder.Services.AddScoped<BackupVerificationService>();
builder.Services.AddScoped<DataRetentionService>();
builder.Services.AddScoped<ExportGovernanceService>();
builder.Services.AddScoped<PasswordPolicyService>();
builder.Services.AddHostedService<TelemetryBackgroundService>();
builder.Services.AddHostedService<TelemetrySimulatorBackgroundService>();
builder.Services.AddHostedService<SafetyBackgroundService>();
builder.Services.AddHostedService<TripBackgroundService>();
builder.Services.AddHostedService<MaintenanceBackgroundService>();
builder.Services.AddHostedService<EscalationBackgroundService>();
builder.Services.AddHostedService<ScheduledReportBackgroundService>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("OpsTraxCors", policy =>
    {
        var origins = builder.Configuration["Cors:AllowedOrigins"]?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? ["http://localhost:10000"];
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    });
});

var app = builder.Build();
var rateWindows = new ConcurrentDictionary<string, (DateTimeOffset WindowStart, int Count)>();
var rateWindowSize = TimeSpan.FromMinutes(1);
const int apiRequestLimitPerWindow = 240;

using (var scope = app.Services.CreateScope())
{
    await RunSchemaStep(app, "Batch1", () => scope.ServiceProvider.GetRequiredService<Batch1SchemaService>().EnsureAsync());
    await RunSchemaStep(app, "Batch2", () => scope.ServiceProvider.GetRequiredService<Batch2SchemaService>().EnsureAsync());
    await RunSchemaStep(app, "Batch3", () => scope.ServiceProvider.GetRequiredService<Batch3SchemaService>().EnsureAsync());
    await RunSchemaStep(app, "Batch4", () => scope.ServiceProvider.GetRequiredService<Batch4SchemaService>().EnsureAsync());
    await RunSchemaStep(app, "Batch5", () => scope.ServiceProvider.GetRequiredService<Batch5SchemaService>().EnsureAsync());
    await RunSchemaStep(app, "Batch6", () => scope.ServiceProvider.GetRequiredService<Batch6SchemaService>().EnsureAsync());
    await RunSchemaStep(app, "Batch7", () => scope.ServiceProvider.GetRequiredService<Batch7SchemaService>().EnsureAsync());
    await RunSchemaStep(app, "Telemetry", () => scope.ServiceProvider.GetRequiredService<TelemetrySchemaService>().EnsureAsync());
    await RunSchemaStep(app, "Safety",    () => scope.ServiceProvider.GetRequiredService<SafetySchemaService>().EnsureAsync());
    await RunSchemaStep(app, "Trips",       () => scope.ServiceProvider.GetRequiredService<TripSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "Maintenance", () => scope.ServiceProvider.GetRequiredService<MaintenanceSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "Dispatch",    () => scope.ServiceProvider.GetRequiredService<DispatchSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "CustomerVisibility", () => scope.ServiceProvider.GetRequiredService<CustomerVisibilitySchemaService>().EnsureAsync());
    await RunSchemaStep(app, "Driver",            () => scope.ServiceProvider.GetRequiredService<DriverSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "Notification",      () => scope.ServiceProvider.GetRequiredService<NotificationSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "Alerts",            () => scope.ServiceProvider.GetRequiredService<AlertWorkflowSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "Reporting",         () => scope.ServiceProvider.GetRequiredService<ReportingSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "Observability",     () => scope.ServiceProvider.GetRequiredService<ObservabilitySchemaService>().EnsureAsync());
    await RunSchemaStep(app, "Security",          () => scope.ServiceProvider.GetRequiredService<SecuritySchemaService>().EnsureAsync());
    await RunSchemaStep(app, "Platform",          () => scope.ServiceProvider.GetRequiredService<PlatformSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "Revenue",           () => scope.ServiceProvider.GetRequiredService<RevenueSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "MarketPacks",        () => scope.ServiceProvider.GetRequiredService<MarketPackSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "FleetTms",           () => scope.ServiceProvider.GetRequiredService<FleetTmsSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "FleetTmsColdChain",  () => scope.ServiceProvider.GetRequiredService<FleetTmsColdChainSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "FleetTmsLogistics",  () => scope.ServiceProvider.GetRequiredService<FleetTmsLogisticsSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "FleetTmsSeed",        () => scope.ServiceProvider.GetRequiredService<Opstrax.Api.Seed.FleetTmsSeeder>().EnsureAsync());
    await RunSchemaStep(app, "MarketPackSeed",      () => scope.ServiceProvider.GetRequiredService<Opstrax.Api.Seed.MarketPackSeeder>().EnsureAsync());
}

app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
    context.Response.Headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    await next();
});

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseMiddleware<CsrfMiddleware>();
app.UseCors("OpsTraxCors");
app.UseSwagger();
app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase),
    branch =>
    {
        branch.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? string.Empty;
            if (string.Equals(path, "/api/auth/login", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, "/api/health", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, "/api/ready", StringComparison.OrdinalIgnoreCase) ||
                // Platform Admin — self-authenticates against platform_sessions (separate
                // identity from tenant users); must bypass the tenant session middleware
                // so a platform bearer token is never validated as a tenant user token.
                path.StartsWith("/api/platform", StringComparison.OrdinalIgnoreCase) ||
                // P9 health probes — must be unauthenticated for k8s / load-balancer probes
                path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
                // Telemetry ingest — device-authenticated via X-Device-Key header, not user session
                path.StartsWith("/api/telemetry/ingest", StringComparison.OrdinalIgnoreCase) ||
                (context.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                 path.StartsWith("/api/customer-eta/track/", StringComparison.OrdinalIgnoreCase)) ||
                // Customer-facing public tracking — token-scoped, expiring, revocable; no user session
                (context.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                 path.StartsWith("/api/customer-visibility/tracking/", StringComparison.OrdinalIgnoreCase)) ||
                // Fleet TMS public shipment tracking — token-scoped, expiring, revocable; no user session
                (context.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                 path.StartsWith("/api/public/shipments/track/", StringComparison.OrdinalIgnoreCase)))
            {
                await next();
                return;
            }

            var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var now = DateTimeOffset.UtcNow;
            var window = rateWindows.AddOrUpdate(
                remoteIp,
                _ => (now, 1),
                (_, current) => now - current.WindowStart > rateWindowSize
                    ? (now, 1)
                    : (current.WindowStart, current.Count + 1));

            if (now - window.WindowStart <= rateWindowSize && window.Count > apiRequestLimitPerWindow)
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail("Too many requests", "Rate limit exceeded"));
                return;
            }

            // SSE stream path: authenticate exclusively via short-lived stream ticket (?sst=).
            // This avoids long-lived session tokens appearing in query strings (logs, proxies).
            // The SST encodes {userId:companyId:exp} signed with HMAC-SHA256.
            var authHeader = context.Request.Headers.Authorization.ToString();
            if (path.StartsWith("/api/telemetry/stream", StringComparison.OrdinalIgnoreCase))
            {
                var sst = context.Request.Query["sst"].FirstOrDefault() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(sst))
                {
                    var (sstOk, sstUserId, sstCompanyId) = TelemetryTicketHelper.Validate(TelemetryKeyStore.SseTicketKey, sst);
                    if (!sstOk)
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail("Invalid or expired stream ticket"));
                        return;
                    }
                    context.Items[EndpointMappings.AuthUserIdItemKey]      = sstUserId;
                    context.Items[EndpointMappings.AuthCompanyIdItemKey]   = sstCompanyId;
                    context.Items[EndpointMappings.AuthRoleItemKey]        = "sst-client";
                    context.Items[EndpointMappings.AuthPermissionsItemKey] = Array.Empty<string>();
                    await next();
                    return;
                }
                // No ?sst= present — reject; session tokens are no longer accepted in query string for SSE.
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail("Stream ticket required — call POST /api/telemetry/stream-ticket first"));
                return;
            }

            if (string.IsNullOrWhiteSpace(authHeader) ||
                !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail("Unauthorized", "Missing bearer token"));
                return;
            }

            var token = authHeader["Bearer ".Length..].Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail("Unauthorized", "Invalid bearer token"));
                return;
            }

            var db = context.RequestServices.GetRequiredService<Database>();
            var session = await db.QuerySingleAsync(
                @"SELECT s.user_id, s.company_id, u.role_name, u.role_id, u.permissions_json, r.permissions_json role_permissions_json
                  FROM user_sessions s
                  JOIN users u ON u.id = s.user_id
                  LEFT JOIN roles r ON r.id = u.role_id
                  WHERE s.session_token=@token
                    AND s.expires_at > NOW()
                    AND u.status='Active'
                  LIMIT 1",
                c => c.Parameters.AddWithValue("@token", token));

            if (session is null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail("Unauthorized", "Session expired or invalid"));
                return;
            }

            var userId = Convert.ToInt64(session["userId"]);
            var companyId = Convert.ToInt64(session["companyId"]);
            var roleName = session["roleName"]?.ToString() ?? string.Empty;
            var roleId = session.TryGetValue("roleId", out var rid) && rid is not null && rid is not DBNull ? Convert.ToInt64(rid) : 0;

            var permissions = ParsePermissions(session.GetValueOrDefault("permissionsJson"))
                .Concat(ParsePermissions(session.GetValueOrDefault("rolePermissionsJson")))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (roleId > 0)
            {
                var rows = await db.QueryAsync(
                    "SELECT permission_key FROM role_permissions WHERE role_id=@roleId",
                    c => c.Parameters.AddWithValue("@roleId", roleId));
                foreach (var row in rows)
                {
                    var key = row.GetValueOrDefault("permissionKey")?.ToString();
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        permissions.Add(key.Trim());
                    }
                }
            }

            if (permissions.Count == 0 && string.Equals(roleName, "Super Admin", StringComparison.OrdinalIgnoreCase))
            {
                permissions.Add("*");
            }

            context.Items[EndpointMappings.AuthUserIdItemKey] = userId;
            context.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
            context.Items[EndpointMappings.AuthRoleItemKey] = roleName;
            context.Items[EndpointMappings.AuthPermissionsItemKey] = permissions.ToArray();

            // ── Feature entitlement enforcement (server-side, tenant-isolated) ──────
            // Platform Admin controls which modules a tenant may access. If a tenant has
            // an entitlement row explicitly disabling the module this path belongs to,
            // block it here — even if the client calls the API/URL directly. Default is
            // allow (no row = inherit), preserving behaviour for un-gated modules.
            var moduleKey = ModuleKeyForPath(path);
            if (moduleKey is not null)
            {
                var blocked = await db.ScalarLongAsync(
                    "SELECT COUNT(*) FROM tenant_entitlements WHERE company_id=@cid AND module_key=@mk AND enabled=false",
                    c =>
                    {
                        c.Parameters.AddWithValue("@cid", companyId);
                        c.Parameters.AddWithValue("@mk", moduleKey);
                    });
                if (blocked > 0)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail("Module disabled", $"The '{moduleKey}' module is not enabled for your account. Contact your account owner."));
                    return;
                }
            }

            await next();
        });
    });
app.MapGet("/swagger", () => Results.Content(SwaggerHtml(), "text/html"));
app.MapGet("/swagger/index.html", () => Results.Content(SwaggerHtml(), "text/html"));

// ── Health probes ──────────────────────────────────────────────────────────────
// /health/live  — always 200 if process is alive (kubernetes liveness probe)
// /health/ready — DB connectivity (kubernetes readiness probe)
// /health/deep  — comprehensive check; never exposes secrets
// Legacy aliases kept for backward compatibility:
//   /health  → same as /health/live
//   /ready   → same as /health/ready

app.MapGet("/health",       () => Results.Ok(new { status = "alive", service = "opstrax-api", utc = DateTime.UtcNow }));
app.MapGet("/health/live",  () => Results.Ok(new { status = "alive", service = "opstrax-api", utc = DateTime.UtcNow }));

app.MapGet("/ready", async (Database db, CancellationToken ct) =>
{
    try
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText      = "SELECT 1";
        await cmd.ExecuteScalarAsync(ct);
        return Results.Ok(new { status = "ready", service = "opstrax-api", database = "connected", utc = DateTime.UtcNow });
    }
    catch
    {
        return Results.Json(
            new { status = "not_ready", service = "opstrax-api", database = "unavailable", utc = DateTime.UtcNow },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/health/ready", async (Database db, CancellationToken ct) =>
{
    try
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText      = "SELECT 1";
        await cmd.ExecuteScalarAsync(ct);
        return Results.Ok(new { status = "ready", service = "opstrax-api", database = "connected", utc = DateTime.UtcNow });
    }
    catch
    {
        return Results.Json(
            new { status = "not_ready", service = "opstrax-api", database = "unavailable", utc = DateTime.UtcNow },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/health/deep", async (Database db, ConfigValidationService configValidator, CancellationToken ct) =>
{
    var checks   = new Dictionary<string, object>();
    var dbOk     = false;
    var dbLatMs  = -1;

    // DB check
    var dbSw = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText      = "SELECT 1";
        await cmd.ExecuteScalarAsync(ct);
        dbSw.Stop();
        dbOk    = true;
        dbLatMs = (int)dbSw.ElapsedMilliseconds;
    }
    catch
    {
        dbSw.Stop();
    }
    checks["database"] = new { status = dbOk ? "connected" : "unavailable", latency_ms = dbLatMs };

    // Background service heartbeats (read from DB if available)
    var serviceStatuses = new List<object>();
    if (dbOk)
    {
        try
        {
            var heartbeatRows = await db.QueryAsync(
                @"SELECT service_name, last_heartbeat_at, last_run_status, consecutive_failures
                  FROM service_heartbeats ORDER BY service_name", ct: ct);

            foreach (var row in heartbeatRows)
            {
                var name    = row["serviceName"]?.ToString() ?? "";
                var lastBeat = row["lastHeartbeatAt"] as DateTime?;
                var consec   = row["consecutiveFailures"] is { } cf ? Convert.ToInt32(cf) : 0;
                var svcStatus = consec >= 3 ? "degraded" : consec > 0 ? "warning" : "healthy";

                serviceStatuses.Add(new
                {
                    name,
                    status              = svcStatus,
                    last_heartbeat_utc  = lastBeat?.ToString("o"),
                    consecutive_failures = consec,
                });
            }
        }
        catch { /* DB readable but heartbeats table not yet migrated — non-fatal */ }
    }
    checks["services"] = serviceStatuses;

    // Config validation — no values exposed
    var cfgResult = configValidator.Validate();
    checks["config"] = new
    {
        status   = cfgResult.Status,
        warnings = cfgResult.WarnCount,
        failures = cfgResult.FailCount,
        // Expose issue check names and levels but NOT values
        issues   = cfgResult.Issues.Select(i => new { i.Check, i.Level, i.Message }).ToList()
    };

    // Determine overall status
    var overallStatus =
        !dbOk                                          ? "unhealthy" :
        serviceStatuses.Any(s => s.GetType().GetProperty("status")?.GetValue(s)?.ToString() == "degraded")
                                                       ? "degraded" :
        cfgResult.FailCount > 0                       ? "degraded" :
                                                         "healthy";

    var statusCode = overallStatus == "unhealthy" ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status200OK;

    return Results.Json(new
    {
        status  = overallStatus,
        service = "opstrax-api",
        utc     = DateTime.UtcNow,
        checks
    }, statusCode: statusCode);
});
app.MapOpsTraxEndpoints();
app.MapPlatformEndpoints();
EndpointMappings.MapP9OpsEndpoints(app);
EndpointMappings.MapP10SecurityEndpoints(app);
EndpointMappings.MapFleetHealthEndpoints(app);
app.MapFleetTmsEndpoints();
app.MapFleetTmsColdChainEndpoints();
app.MapFleetTmsLogisticsEndpoints();
app.MapRevenueEndpoints();
app.MapMarketPackEndpoints();

app.Run();

static IEnumerable<string> ParsePermissions(object? source)
{
    if (source is null or DBNull) yield break;

    if (source is byte[] bytes)
    {
        source = System.Text.Encoding.UTF8.GetString(bytes);
    }

    if (source is JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Array) yield break;
        foreach (var item in json.EnumerateArray())
        {
            var key = item.GetString();
            if (!string.IsNullOrWhiteSpace(key)) yield return key.Trim();
        }
        yield break;
    }

    if (source is string str && !string.IsNullOrWhiteSpace(str))
    {
        str = str.Trim();
        if (str.StartsWith("[", StringComparison.Ordinal))
        {
            List<string>? values = null;
            try
            {
                values = JsonSerializer.Deserialize<List<string>>(str);
            }
            catch
            {
                yield break;
            }

            if (values is null) yield break;
            foreach (var value in values.Where(v => !string.IsNullOrWhiteSpace(v)))
            {
                yield return value.Trim();
            }
            yield break;
        }

        yield return str;
    }
}

// Maps an /api/* request path to the entitlement module_key that gates it.
// Returns null for paths that are not entitlement-gated (always allowed).
static string? ModuleKeyForPath(string path)
{
    if (string.IsNullOrEmpty(path)) return null;
    // Order matters: most specific prefixes first.
    (string Prefix, string Module)[] map =
    [
        ("/api/safety",              "safety"),
        ("/api/dashcam",             "safety"),
        ("/api/maintenance",         "maintenance"),
        ("/api/work-orders",         "maintenance"),
        ("/api/dispatch",            "dispatch"),
        ("/api/trips",               "dispatch"),
        ("/api/telemetry",           "telematics"),
        ("/api/devices",             "telematics"),
        ("/api/customers",           "crm"),
        ("/api/contracts",           "crm"),
        ("/api/customer-eta",        "customer_portal"),
        ("/api/customer-visibility", "customer_portal"),
        ("/api/reporting",           "reports"),
        ("/api/compliance",          "compliance"),
    ];
    foreach (var (prefix, module) in map)
    {
        if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return module;
    }
    return null;
}

static async Task RunSchemaStep(WebApplication app, string name, Func<Task> step)
{
    try
    {
        await step();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "{SchemaStep} schema bootstrap failed; continuing startup", name);
    }
}

static string SwaggerHtml() => """
<!doctype html>
<html lang="en">
<head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>OpsTrax API Swagger</title>
<style>body{margin:0;background:#f3f6fb;color:#0f172a;font-family:Inter,system-ui,sans-serif}.wrap{max-width:980px;margin:0 auto;padding:48px 24px}.card{border:1px solid #dbe5f0;background:#fff;border-radius:18px;padding:28px;box-shadow:0 1px 2px rgba(15,23,42,.04),0 16px 42px rgba(15,23,42,.08)}a{color:#1d4ed8;font-weight:700}code{background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;padding:3px 6px;color:#0f766e}</style></head>
<body><main class="wrap"><div class="card"><p style="color:#0f766e;font-weight:800;letter-spacing:.18em;text-transform:uppercase">OpsTrax Transport Management Solution</p><h1>OpsTrax API Swagger</h1><p>OpenAPI specification is available at <a href="/swagger/v1/swagger.json">/swagger/v1/swagger.json</a>.</p><p>Core endpoints include <code>/api/command-center/summary</code>, <code>/api/control-tower/summary</code>, <code>/api/vehicles</code>, <code>/api/drivers</code>, <code>/api/jobs</code>, <code>/api/dispatch/board</code>, and <code>/api/ai/ask</code>.</p></div></main></body></html>
""";
