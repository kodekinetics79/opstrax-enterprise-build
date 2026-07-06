using Opstrax.Api;
using Opstrax.Api.Controllers;
using Opstrax.Api.Foundation;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Middleware;
using Opstrax.Api.Observability;
using Opstrax.Api.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ── Structured (JSON) logging ────────────────────────────────────────────────
// Production emits one JSON object per log line (trace_id/correlation_id/tenant
// enriched from the ambient TelemetryContext), which Render/Loki/Datadog ingest
// natively. Dev keeps the readable console formatter. Toggle with Logging:Json.
var useJsonLogs = builder.Configuration.GetValue("Logging:Json", builder.Environment.IsProduction());
if (useJsonLogs)
{
    builder.Logging.ClearProviders();
    builder.Logging.AddProvider(new JsonConsoleLoggerProvider());
}

// ── Graceful shutdown ────────────────────────────────────────────────────────
// On SIGTERM (Render deploy/restart) drain in-flight requests for up to 25s
// before the host force-stops. Combined with health-readiness flipping to 503,
// this prevents partial writes and dropped requests during a rolling deploy.
builder.Services.Configure<HostOptions>(o =>
{
    o.ShutdownTimeout = TimeSpan.FromSeconds(25);
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);

    static void AddNetwork(ForwardedHeadersOptions target, string cidr)
    {
        var parts = cidr.Split('/', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2 &&
            IPAddress.TryParse(parts[0], out var address) &&
            int.TryParse(parts[1], out var prefixLength))
        {
            target.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(address, prefixLength));
        }
    }

    foreach (var cidr in builder.Configuration["Proxy:KnownNetworks"]?
                 .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
             ?? [])
    {
        AddNetwork(options, cidr);
    }

    // Render terminates public traffic at a private-network proxy. Trust only the
    // immediate private peer; ForwardLimit prevents client-supplied XFF chains.
    if (string.Equals(builder.Configuration["RENDER"], "true", StringComparison.OrdinalIgnoreCase))
    {
        AddNetwork(options, "10.0.0.0/8");
        AddNetwork(options, "172.16.0.0/12");
        AddNetwork(options, "192.168.0.0/16");
    }
});
builder.Services.AddRateLimiter(options =>
{
    const int apiPermitLimit = 240;
    const int loginPermitLimit = 10;
    var window = TimeSpan.FromMinutes(1);

    static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    static bool IsHealthProbe(PathString path) =>
        path.Equals("/api/health", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/api/ready", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);

    static bool IsLogin(PathString path) =>
        path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/api/platform/auth/login", StringComparison.OrdinalIgnoreCase);

    var generalApiLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
            IsHealthProbe(context.Request.Path))
        {
            return RateLimitPartition.GetNoLimiter("unlimited");
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            $"api:{ClientKey(context)}",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = apiPermitLimit,
                QueueLimit = 0,
                Window = window
            });
    });

    var loginLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        if (!IsLogin(context.Request.Path))
            return RateLimitPartition.GetNoLimiter("not-login");

        return RateLimitPartition.GetFixedWindowLimiter(
            $"login:{ClientKey(context)}",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = loginPermitLimit,
                QueueLimit = 0,
                Window = window
            });
    });

    options.GlobalLimiter = PartitionedRateLimiter.CreateChained(generalApiLimiter, loginLimiter);
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var retryAfter = window;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var leaseRetryAfter))
            retryAfter = leaseRetryAfter;

        context.HttpContext.Response.Headers.RetryAfter =
            Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        await context.HttpContext.Response.WriteAsJsonAsync(
            ApiResponse<object>.Fail("Too many requests", "Rate limit exceeded"),
            cancellationToken);
    };
});
// Observability — in-process metrics collector (request/latency/error/DB), SLO
// evaluation, and reliability aggregation. All singletons; no external deps.
builder.Services.AddSingleton<ApiMetricsService>();
builder.Services.AddSingleton<SloService>();
builder.Services.AddScoped<ReliabilityService>();
// Data protection — application-layer PII encryption (AES-256-GCM envelope) with a
// KMS-swappable key provider, + S3-compatible object storage for uploaded files.
builder.Services.AddSingleton<Opstrax.Api.Security.IDataKeyProvider, Opstrax.Api.Security.EnvDataKeyProvider>();
builder.Services.AddSingleton<Opstrax.Api.Security.PiiProtectionService>();
builder.Services.AddSingleton<Opstrax.Api.Storage.IObjectStore>(sp =>
    Opstrax.Api.Storage.ObjectStoreFactory.Create(
        sp.GetRequiredService<IConfiguration>(),
        sp.GetRequiredService<ILoggerFactory>()));
builder.Services.AddScoped<Opstrax.Api.Storage.FileStorageService>();
builder.Services.AddSingleton<TenantScopeAccessor>();
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
builder.Services.AddSingleton<TelemetryLiveStateService>();
// Agentic Brain — the model behind the AI foundation's empty reasoning slot.
builder.Services.AddSingleton<AgenticBrainService>();
builder.Services.AddScoped<IncidentService>();
builder.Services.AddScoped<CustomerPortalService>();
builder.Services.AddScoped<DemoTenantSeeder>();
builder.Services.AddScoped<OpsMetricsService>();
builder.Services.AddSingleton<FoundationSchemaService>();
builder.Services.AddSingleton<SafetyMaintenanceFoundationSchemaService>();
builder.Services.AddSingleton<SafetyMaintenanceFoundationService>();
builder.Services.AddSingleton<BusinessSpineSchemaService>();
builder.Services.AddSingleton<CommercialFoundationSchemaService>();
builder.Services.AddSingleton<RevenueReadinessSchemaService>();
builder.Services.AddSingleton<FinanceActivationSchemaService>();
builder.Services.AddSingleton<Stage9SchemaService>();
builder.Services.AddSingleton<BusinessSpineService>();
builder.Services.AddSingleton<CommercialFoundationService>();
builder.Services.AddSingleton<RevenueReadinessService>();
builder.Services.AddSingleton<Stage9OperationalFoundationService>();
builder.Services.AddSingleton<IFeatureAccessService, PostgresFeatureAccessService>();
builder.Services.AddSingleton<IAuthorizationDecisionService, AuthorizationDecisionService>();
builder.Services.AddSingleton<IApprovalWorkflowService, PostgresApprovalWorkflowService>();
builder.Services.AddSingleton<IDomainEventPublisher, PostgresDomainEventPublisher>();
builder.Services.AddSingleton<IOutboxWriter, PostgresDomainEventPublisher>();
builder.Services.AddSingleton<IInboxProcessor, PostgresDomainEventPublisher>();
builder.Services.AddSingleton<IEventIdempotencyService, PostgresIdempotencyService>();
builder.Services.AddSingleton<IAuditLogService, PostgresAuditLogService>();
builder.Services.AddSingleton<AmbientCorrelationContext>();
builder.Services.AddSingleton<ICorrelationContext>(sp => sp.GetRequiredService<AmbientCorrelationContext>());
builder.Services.AddSingleton<PostgresAiFoundationService>();
var outboxDispatcherOptions = builder.Configuration.GetSection("OutboxDispatcher").Get<OutboxDispatcherOptions>() ?? new OutboxDispatcherOptions();
builder.Services.AddSingleton(outboxDispatcherOptions);
builder.Services.AddSingleton<IEventProcessingLogService, PostgresEventProcessingLogService>();
builder.Services.AddSingleton<IOutboxMessageHandler, FoundationSmokeRequestedHandler>();
builder.Services.AddSingleton<IOutboxMessageHandlerRegistry, OutboxMessageHandlerRegistry>();
builder.Services.AddSingleton<IOutboxDispatcher, PostgresOutboxDispatcher>();
if (outboxDispatcherOptions.Enabled && (!builder.Environment.IsProduction() || outboxDispatcherOptions.AllowProduction))
{
    builder.Services.AddHostedService<OutboxDispatcherBackgroundService>();
}
// P10 Security + Compliance
builder.Services.AddSingleton<SecuritySchemaService>();
// Platform Admin — global SaaS business control plane (separate from tenant admin)
builder.Services.AddSingleton<PlatformSchemaService>();
// Country profiles — platform-managed market/localization defaults + tenant cascade
builder.Services.AddSingleton<CountryProfileSchemaService>();
builder.Services.AddScoped<CountryProfileService>();
// Tenant offboarding — schema-driven cascade delete (pilot "delete on request")
builder.Services.AddScoped<TenantOffboardingService>();
// ZATCA Phase-2 e-invoicing foundation (Saudi). Crypto-stamp/clearance behind the
// gateway interface — PendingOnboardingZatcaGateway until ZATCA CSID onboarding.
builder.Services.AddSingleton<ZatcaSchemaService>();
builder.Services.AddSingleton<IZatcaComplianceGateway, PendingOnboardingZatcaGateway>();
builder.Services.AddScoped<ZatcaService>();
// Revenue foundation — module-package catalog, usage meters/events, pricing, overrides
builder.Services.AddSingleton<RevenueSchemaService>();
builder.Services.AddScoped<EntitlementService>();
// Market-pack engine (Canada/NA + Saudi/GCC) — regional capability + compliance
builder.Services.AddSingleton<MarketPackSchemaService>();
builder.Services.AddSingleton<Opstrax.Api.Seed.MarketPackSeeder>();
// Fleet TMS (PR1) — shipment lifecycle, POD workflow & public tracking (additive)
builder.Services.AddSingleton<FleetTmsSchemaService>();
builder.Services.AddSingleton<FleetTmsColdChainSchemaService>();
builder.Services.AddSingleton<FleetTmsColdChainFoundationSchemaService>();
builder.Services.AddSingleton<FleetTmsColdChainFoundationService>();
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
// Agentic Ops Copilot — reasons over open dispatch exceptions and proposes actions.
builder.Services.AddHostedService<AgenticOpsBackgroundService>();
builder.Services.AddHostedService<ScheduledReportBackgroundService>();
// Data-retention enforcement — executes the stored retention policies (purge of
// expired operational logs), respecting legal hold. Opt-in in Production via
// RetentionWorker:Enabled. Closes the "policy stored but never enforced" gap.
builder.Services.AddHostedService<RetentionEnforcementBackgroundService>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("OpsTraxCors", policy =>
    {
        var origins = builder.Configuration["Cors:AllowedOrigins"]?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? ["http://localhost:10000"];
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()
            // Expose trace headers so the browser can read them cross-origin and
            // surface a trace reference for a failed request (frontend→DB tracing).
            .WithExposedHeaders("X-Trace-Id", "X-Correlation-Id", "X-Deployment-Version", "X-CSRF-Token");
    });
});

var app = builder.Build();

// Validate before any database initialization or hosted workload starts. Production
// must explicitly enable tenant RLS context enforcement; missing/false is fatal.
{
    var validator = app.Services.GetRequiredService<ConfigValidationService>();
    var result = validator.Validate();
    foreach (var issue in result.Issues.Where(i => i.Level is "fail" or "warn"))
        app.Logger.Log(issue.Level == "fail" ? LogLevel.Error : LogLevel.Warning,
            new EventId(0, $"config_{issue.Check}"), "Config check '{Check}': {Message}", issue.Check, issue.Message);

    try
    {
        ConfigValidationService.EnsureStartupAllowed(result, app.Environment.IsProduction());
    }
    catch (InvalidOperationException)
    {
        app.Logger.LogCritical(new EventId(1, "startup_config_invalid"),
            "Startup aborted: {FailCount} critical configuration failure(s). Fix config and redeploy.", result.FailCount);
        throw;
    }

    app.Logger.LogInformation(new EventId(0, "startup_config_ok"),
        "Config validation: {Status} ({Fail} failures, {Warn} warnings) · version {Version} · env {Env}",
        result.Status, result.FailCount, result.WarnCount, Opstrax.Api.Observability.BuildInfo.Version, app.Environment.EnvironmentName);
}

// Route every DB query's latency + success/failure into the metrics collector so
// DB-latency and DB-connection-failure metrics/alerts have live data. Static hook
// keeps the many `new Database(config)` call sites (tests, schema services) clean.
{
    var apiMetrics = app.Services.GetRequiredService<ApiMetricsService>();
    Database.MetricsSink = (ms, failed) => apiMetrics.RecordDbQuery(ms, failed);
}

using (var scope = app.Services.CreateScope())
{
    // Schema init does DDL + seeding and MUST run as the DB owner, never the
    // restricted runtime role (opstrax_app is NOSUPERUSER/NOBYPASSRLS with no DDL
    // grants). Decide up front whether to run it:
    //   • owner-capable role (super/bypassrls)  -> run schema init (normal path).
    //   • restricted role + RLS enforced        -> SKIP with a clear log; the owner
    //     applies migrations/seeders out-of-band (documented production flow), so the
    //     single runtime process can boot as opstrax_app without failing on DDL.
    //   • restricted role + RLS off (misconfig) -> warn but still attempt (legacy behaviour).
    var runSchemaInit = await ShouldRunSchemaInitAsync(app, scope.ServiceProvider.GetRequiredService<Database>());
    if (runSchemaInit)
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
    await RunSchemaStep(app, "Foundation",        () => scope.ServiceProvider.GetRequiredService<FoundationSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "SafetyMaintenanceFoundation", () => scope.ServiceProvider.GetRequiredService<SafetyMaintenanceFoundationSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "BusinessSpine",     () => scope.ServiceProvider.GetRequiredService<BusinessSpineSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "CommercialFoundation", () => scope.ServiceProvider.GetRequiredService<CommercialFoundationSchemaService>().EnsureAsync());
    var revenueReadinessSchemaEnabled = builder.Configuration.GetValue("RevenueReadinessSchema:Enabled", !app.Environment.IsProduction());
    if (revenueReadinessSchemaEnabled)
    {
        await RunSchemaStep(app, "RevenueReadiness", () => scope.ServiceProvider.GetRequiredService<RevenueReadinessSchemaService>().EnsureAsync());
    }
    var financeActivationSchemaEnabled = builder.Configuration.GetValue("FinanceActivationSchema:Enabled", !app.Environment.IsProduction());
    if (financeActivationSchemaEnabled)
    {
        await RunSchemaStep(app, "FinanceActivation", () => scope.ServiceProvider.GetRequiredService<FinanceActivationSchemaService>().EnsureAsync());
    }
    var stage9SchemaEnabled = builder.Configuration.GetValue("Stage9Schema:Enabled", !app.Environment.IsProduction());
    if (stage9SchemaEnabled)
    {
        await RunSchemaStep(app, "Stage9", () => scope.ServiceProvider.GetRequiredService<Stage9SchemaService>().EnsureAsync());
    }
    await RunSchemaStep(app, "Security",          () => scope.ServiceProvider.GetRequiredService<SecuritySchemaService>().EnsureAsync());
    await RunSchemaStep(app, "Platform",          () => scope.ServiceProvider.GetRequiredService<PlatformSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "CountryProfiles",    () => scope.ServiceProvider.GetRequiredService<CountryProfileSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "Zatca",              () => scope.ServiceProvider.GetRequiredService<ZatcaSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "Revenue",           () => scope.ServiceProvider.GetRequiredService<RevenueSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "MarketPacks",        () => scope.ServiceProvider.GetRequiredService<MarketPackSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "FleetTms",           () => scope.ServiceProvider.GetRequiredService<FleetTmsSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "FleetTmsColdChain",  () => scope.ServiceProvider.GetRequiredService<FleetTmsColdChainSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "FleetTmsColdChainFoundation", () => scope.ServiceProvider.GetRequiredService<FleetTmsColdChainFoundationSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "FleetTmsLogistics",  () => scope.ServiceProvider.GetRequiredService<FleetTmsLogisticsSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "FleetTmsSeed",        () => scope.ServiceProvider.GetRequiredService<Opstrax.Api.Seed.FleetTmsSeeder>().EnsureAsync());
    await RunSchemaStep(app, "MarketPackSeed",      () => scope.ServiceProvider.GetRequiredService<Opstrax.Api.Seed.MarketPackSeeder>().EnsureAsync());
    }
    else
    {
        app.Logger.LogWarning("Schema init SKIPPED — runtime is connected as the restricted role under RLS enforcement. " +
            "Ensure migrations/seeders have been applied out-of-band by the DB owner.");
    }
}

// Request telemetry runs FIRST: it establishes the trace_id / correlation_id for
// the whole request (continuing an inbound W3C traceparent if present), binds it
// as ambient so every log line + DB call carries the same trace, records metrics
// on completion, and echoes the ids back on the response for frontend→DB tracing.
app.UseForwardedHeaders();
app.UseMiddleware<RequestTelemetryMiddleware>();

app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
    context.Response.Headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    await next();
});

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseRateLimiter();
app.UseMiddleware<CsrfMiddleware>();
app.UseCors("OpsTraxCors");
app.UseSwagger();

// RLS enforcement (Option A1). Production startup requires this to be explicitly
// true. Non-production may leave it off for local/test compatibility. Enable it
// only when PG_CONNECTION uses the restricted `opstrax_app` role
// (see 2026_06_30_stage20_rls_force_and_app_role.sql). When true, each authenticated
// request runs inside a tenant-scoped transaction (set_config('app.current_tenant_id',
// …, true)); the pre-tenant auth bootstrap and public/platform paths run under the
// separate platform_admin_bypass GUC so they are never silently blocked by RLS.
var rlsEnforceTenantContext = app.Configuration.GetValue<bool>("Rls:EnforceTenantContext");

app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase),
    branch =>
    {
        branch.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? string.Empty;

            // Ambient tenant-scope plumbing (no-ops entirely when RLS is off).
            var scopes = context.RequestServices.GetRequiredService<TenantScopeAccessor>();
            var scopedDb = context.RequestServices.GetRequiredService<Database>();

            // Wraps next() under a bypass scope for no-tenant-context paths (public /
            // platform / device-auth), so their handlers can reach RLS tables.
            async Task InvokeUnderBypassAsync()
            {
                if (!rlsEnforceTenantContext) { await next(); return; }
                await using var sys = await scopedDb.BeginSystemScopeAsync(context.RequestAborted);
                scopes.Current = sys;
                try { await next(); await sys.CompleteAsync(context.RequestAborted); }
                finally { scopes.Current = null; }
            }
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
                await InvokeUnderBypassAsync();
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
            // Pre-tenant bootstrap read of RLS-protected auth tables — runs under the
            // platform bypass so it succeeds under the restricted role (no tenant yet).
            var sessionSql =
                @"SELECT s.user_id, s.company_id, u.role_name, u.role_id, u.customer_id, u.branch_id, u.permissions_json, r.permissions_json role_permissions_json
                  FROM user_sessions s
                  JOIN users u ON u.id = s.user_id
                  LEFT JOIN roles r ON r.id = u.role_id
                  WHERE s.session_token=@token
                    AND s.expires_at > NOW()
                    AND u.status='Active'
                  LIMIT 1";
            var session = rlsEnforceTenantContext
                ? await db.QuerySingleInSystemScopeAsync(
                    sessionSql, c => c.Parameters.AddWithValue("@token", token), context.RequestAborted)
                : await db.QuerySingleAsync(
                    sessionSql, c => c.Parameters.AddWithValue("@token", token), context.RequestAborted);

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

            if (permissions.Count == 0 &&
                EndpointMappings.RolePermissionDefaults.TryGetValue(roleName, out var defaultPermissions))
            {
                permissions.UnionWith(defaultPermissions);
            }

            context.Items[EndpointMappings.AuthUserIdItemKey] = userId;
            context.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
            context.Items[EndpointMappings.AuthRoleItemKey] = roleName;
            context.Items[EndpointMappings.AuthPermissionsItemKey] = permissions.ToArray();
            // Branch scoping: non-null when the user is bound to a branch; NULL = tenant-wide.
            if (session.TryGetValue("branchId", out var bid) && bid is not null && bid is not DBNull)
                context.Items[EndpointMappings.AuthBranchIdItemKey] = Convert.ToInt64(bid);
            // Customer-portal binding: non-null when the user is a customer_portal user.
            // Internal endpoints reject any principal carrying this (see RequirePermission
            // / RequireInternalUser) — a stricter boundary than tenant RBAC.
            if (session.TryGetValue("customerId", out var custId) && custId is not null && custId is not DBNull)
            {
                context.Items[EndpointMappings.AuthCustomerIdItemKey] = Convert.ToInt64(custId);
            }

            // ── Feature entitlement enforcement (server-side, tenant-isolated) ──────
            // Platform Admin controls which modules a tenant may access. If a tenant has
            // an entitlement row explicitly disabling the module this path belongs to,
            // block it here — even if the client calls the API/URL directly. Default is
            // allow (no row = inherit), preserving behaviour for un-gated modules.
            var moduleKey = ModuleKeyForPath(path);
            if (moduleKey is not null)
            {
                const string entitlementSql =
                    "SELECT COUNT(*) FROM tenant_entitlements WHERE company_id=@cid AND module_key=@mk AND enabled=false";
                void BindEntitlement(Npgsql.NpgsqlCommand c)
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@mk", moduleKey);
                }
                var blocked = rlsEnforceTenantContext
                    ? await db.ScalarLongInSystemScopeAsync(entitlementSql, BindEntitlement, context.RequestAborted)
                    : await db.ScalarLongAsync(entitlementSql, BindEntitlement, context.RequestAborted);
                if (blocked > 0)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail("Module disabled", $"The '{moduleKey}' module is not enabled for your account. Contact your account owner."));
                    return;
                }
            }

            // Authenticated handler runs inside a tenant-scoped transaction so every
            // query is filtered by RLS on app.current_tenant_id (no-op when RLS is off).
            if (rlsEnforceTenantContext)
            {
                await using var reqScope = await scopedDb.BeginTenantScopeAsync(companyId, context.RequestAborted);
                scopes.Current = reqScope;
                try
                {
                    await next();
                    await reqScope.CompleteAsync(context.RequestAborted);
                }
                finally { scopes.Current = null; }
            }
            else
            {
                await next();
            }
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

// Liveness — process is up. Cheap, no dependencies. Every response carries the
// version/environment/uptime block so probes double as deploy verification.
static object HealthEnvelope(string status, object? checks = null, string? failureReason = null) => new
{
    status,
    service     = Opstrax.Api.Observability.BuildInfo.Service,
    version     = Opstrax.Api.Observability.BuildInfo.Version,
    environment = Opstrax.Api.Observability.BuildInfo.Environment,
    uptime_seconds = Opstrax.Api.Observability.BuildInfo.UptimeSeconds,
    timestamp   = DateTime.UtcNow.ToString("o"),
    checks,
    failure_reason = failureReason,
};

app.MapGet("/health",       () => Results.Ok(HealthEnvelope("alive")));
app.MapGet("/health/live",  () => Results.Ok(HealthEnvelope("alive")));

// Readiness — validates the app can actually serve traffic: DB connectivity +
// critical config (env vars, JWT key, etc.). A failing readiness pulls the
// instance out of the load balancer without killing the process (unlike liveness).
static async Task<IResult> ReadinessAsync(Database db, ConfigValidationService cfg, CancellationToken ct)
{
    var checks = new Dictionary<string, object>();
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var dbOk = false;
    string? failure = null;
    try
    {
        await db.ScalarLongAsync("SELECT 1", ct: ct);
        sw.Stop();
        dbOk = true;
        checks["database"] = new { status = "connected", latency_ms = (int)sw.ElapsedMilliseconds };
    }
    catch (Exception ex)
    {
        sw.Stop();
        failure = "database_unavailable";
        checks["database"] = new { status = "unavailable", latency_ms = -1, error_code = ex.GetType().Name };
    }

    // Critical config gate — a 'fail'-level config issue (e.g. missing JWT key or
    // DB string, default superadmin password in prod) means we are NOT ready.
    var cfgResult = cfg.Validate();
    checks["config"] = new { status = cfgResult.Status, failures = cfgResult.FailCount, warnings = cfgResult.WarnCount };
    if (cfgResult.FailCount > 0) failure ??= "critical_config_invalid";

    var ready = dbOk && cfgResult.FailCount == 0;
    var envelope = HealthEnvelope(ready ? "ready" : "not_ready", checks, ready ? null : failure);
    return Results.Json(envelope, statusCode: ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
}

app.MapGet("/ready",        (Database db, ConfigValidationService cfg, CancellationToken ct) => ReadinessAsync(db, cfg, ct));
app.MapGet("/health/ready", (Database db, ConfigValidationService cfg, CancellationToken ct) => ReadinessAsync(db, cfg, ct));

// Prometheus scrape target — any external monitor (Grafana Agent, Datadog,
// UptimeRobot-with-metrics) can alert on 5xx rate / p95 / DB failures within 60s.
app.MapGet("/metrics", (Opstrax.Api.Observability.ApiMetricsService m) =>
    Results.Text(m.ToPrometheus(), "text/plain; version=0.0.4"));

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

    var failureReason =
        !dbOk                    ? "database_unavailable" :
        cfgResult.FailCount > 0  ? "critical_config_invalid" :
        serviceStatuses.Any(s => s.GetType().GetProperty("status")?.GetValue(s)?.ToString() == "degraded")
                                 ? "background_service_degraded" :
                                   null;

    return Results.Json(new
    {
        status         = overallStatus,
        service        = Opstrax.Api.Observability.BuildInfo.Service,
        version        = Opstrax.Api.Observability.BuildInfo.Version,
        environment    = Opstrax.Api.Observability.BuildInfo.Environment,
        uptime_seconds = Opstrax.Api.Observability.BuildInfo.UptimeSeconds,
        timestamp      = DateTime.UtcNow.ToString("o"),
        db_latency_ms  = dbLatMs,
        failure_reason = failureReason,
        checks
    }, statusCode: statusCode);
});
app.MapOpsTraxEndpoints();
app.MapBusinessSpineEndpoints();
app.MapPlatformEndpoints();
EndpointMappings.MapP9OpsEndpoints(app);
EndpointMappings.MapStage9OperationsEndpoints(app);
EndpointMappings.MapP10SecurityEndpoints(app);
EndpointMappings.MapFleetHealthEndpoints(app);
app.MapFleetTmsEndpoints();
app.MapFleetTmsColdChainEndpoints();
app.MapFleetTmsLogisticsEndpoints();
app.MapRevenueEndpoints();
app.MapRevenueReadinessEndpoints();
app.MapCustomerPortalEndpoints();
app.MapDevSeedEndpoints();
app.MapMarketPackEndpoints();
app.MapSafetyMaintenanceFoundationEndpoints();

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
        ("/api/foundation/safety-maintenance", "dashboard"),
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
        ("/api/reports",             "reports"),
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

// Guard: schema init must connect as the DB owner, not the restricted `opstrax_app`
// role. A NOBYPASSRLS non-superuser role has no DDL grants and would fail every
// CREATE/ALTER — so we detect it up front and throw, halting startup with a clear
// message instead of a cascade of permission errors. Only enforced when RLS is on
// (the only scenario in which a restricted role is even in play); otherwise a warning.
// Decide whether startup should run schema DDL/seeding, based on the connected role.
//   owner-capable (super/bypassrls)        -> true  (normal single-process path)
//   restricted role + RLS enforced         -> false (owner applies schema out-of-band;
//                                                     runtime boots as opstrax_app safely)
//   restricted role + RLS off (misconfig)  -> true + warn (legacy behaviour; DDL will
//                                                     likely fail, surfaced loudly)
static async Task<bool> ShouldRunSchemaInitAsync(WebApplication app, Database db)
{
    try
    {
        var row = await db.QuerySingleAsync(
            @"SELECT current_user AS role_name,
                     (SELECT rolsuper     FROM pg_roles WHERE rolname = current_user) AS is_super,
                     (SELECT rolbypassrls FROM pg_roles WHERE rolname = current_user) AS bypass_rls");
        var roleName = row?["roleName"]?.ToString() ?? "unknown";
        var isSuper = row?["isSuper"] is bool s && s;
        var bypassRls = row?["bypassRls"] is bool b && b;

        // The owner is either a superuser or has BYPASSRLS (the app role has neither).
        var looksLikeOwner = isSuper || bypassRls;
        var rlsEnforced = app.Configuration.GetValue<bool>("Rls:EnforceTenantContext");

        if (looksLikeOwner)
        {
            app.Logger.LogInformation("Schema init will run — owner-capable role '{Role}' (super={Super}, bypassrls={Bypass}).",
                roleName, isSuper, bypassRls);
            return true;
        }

        if (rlsEnforced)
        {
            app.Logger.LogWarning("Skipping schema init — connected as restricted role '{Role}' under RLS enforcement. " +
                "Migrations/seeders must be applied out-of-band by the DB owner.", roleName);
            return false;
        }

        app.Logger.LogWarning("Connected as restricted role '{Role}' but RLS is OFF — attempting schema init anyway; " +
            "DDL may fail. Point PG_CONNECTION at the owner for migrations/init.", roleName);
        return true;
    }
    catch (Exception ex)
    {
        // Never block startup on the check itself failing (e.g. restricted pg_roles view).
        app.Logger.LogWarning(ex, "Schema init role check could not be evaluated; proceeding with schema init.");
        return true;
    }
}

static string SwaggerHtml() => """
<!doctype html>
<html lang="en">
<head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>OpsTrax API Swagger</title>
<style>body{margin:0;background:#f3f6fb;color:#0f172a;font-family:Inter,system-ui,sans-serif}.wrap{max-width:980px;margin:0 auto;padding:48px 24px}.card{border:1px solid #dbe5f0;background:#fff;border-radius:18px;padding:28px;box-shadow:0 1px 2px rgba(15,23,42,.04),0 16px 42px rgba(15,23,42,.08)}a{color:#1d4ed8;font-weight:700}code{background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;padding:3px 6px;color:#0f766e}</style></head>
<body><main class="wrap"><div class="card"><p style="color:#0f766e;font-weight:800;letter-spacing:.18em;text-transform:uppercase">OpsTrax Transport Management Solution</p><h1>OpsTrax API Swagger</h1><p>OpenAPI specification is available at <a href="/swagger/v1/swagger.json">/swagger/v1/swagger.json</a>.</p><p>Core endpoints include <code>/api/command-center/summary</code>, <code>/api/control-tower/summary</code>, <code>/api/vehicles</code>, <code>/api/drivers</code>, <code>/api/jobs</code>, <code>/api/dispatch/board</code>, and <code>/api/ai/ask</code>.</p></div></main></body></html>
""";
