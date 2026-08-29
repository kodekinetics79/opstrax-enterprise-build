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
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

static bool IsProtectedEnvironment(IHostEnvironment environment) =>
    environment.IsProduction() || environment.IsStaging();

// ── Structured (JSON) logging ────────────────────────────────────────────────
// Production emits one JSON object per log line (trace_id/correlation_id/tenant
// enriched from the ambient TelemetryContext), which Render/Loki/Datadog ingest
// natively. Dev keeps the readable console formatter. Toggle with Logging:Json.
var useJsonLogs = builder.Configuration.GetValue("Logging:Json", IsProtectedEnvironment(builder.Environment));
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
var apiRateLimitSettings = ApiRateLimitSettings.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(apiRateLimitSettings);
builder.Services.AddSingleton<PrincipalApiRateLimiter>();
builder.Services.AddRateLimiter(options =>
{
    var generalApiLimiter = ApiRateLimiterFactory.CreatePreAuthGeneral(apiRateLimitSettings);
    var abuseLimiter = ApiRateLimiterFactory.CreateAbuse(apiRateLimitSettings);

    options.GlobalLimiter = PartitionedRateLimiter.CreateChained(generalApiLimiter, abuseLimiter);
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var retryAfter = apiRateLimitSettings.Window;
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
builder.Services.AddSingleton<PostgresDataProtectionXmlRepository>();
builder.Services.AddSingleton<DataProtectionReadinessService>();
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("opstrax-api-v1");
if (IsProtectedEnvironment(builder.Environment))
{
    var certificates = Opstrax.Api.Security.DataProtectionCertificateLoader
        .LoadProductionCertificates(builder.Configuration);
    dataProtection.ProtectKeysWithCertificate(certificates.Current);
    if (certificates.Previous is not null)
        dataProtection.UnprotectKeysWithAnyCertificate(certificates.Current, certificates.Previous);
    builder.Services.AddOptions<KeyManagementOptions>()
        .Configure<PostgresDataProtectionXmlRepository>(
            (options, repository) => options.XmlRepository = repository);
}
builder.Services.AddSingleton<OidcLoginService>(); // OIDC SSO login (discovery + JWKS + code exchange)
builder.Services.AddScoped<AuditService>();

// ── Integration connector framework (real, testable third-party connectivity) ──
// Provider-specific connectors do a genuine API handshake; anything without a specific
// connector falls back to GenericHttpConnector (probes the configured URL). All are
// live-testable via POST /api/integrations/{id}/test-connection.
builder.Services.AddSingleton<Opstrax.Api.Services.Connectors.IConnector, Opstrax.Api.Services.Connectors.TwilioConnector>();
builder.Services.AddSingleton<Opstrax.Api.Services.Connectors.IConnector, Opstrax.Api.Services.Connectors.SlackConnector>();
builder.Services.AddSingleton<Opstrax.Api.Services.Connectors.IConnector, Opstrax.Api.Services.Connectors.SendGridConnector>();
builder.Services.AddSingleton<Opstrax.Api.Services.Connectors.IConnector, Opstrax.Api.Services.Connectors.GoogleMapsConnector>();
// Samsara — deep integration: real GPS/telemetry sync into latest_vehicle_positions.
builder.Services.AddSingleton<Opstrax.Api.Services.Connectors.IConnector, Opstrax.Api.Services.Connectors.SamsaraConnector>();
builder.Services.AddSingleton<Opstrax.Api.Services.Connectors.GenericHttpConnector>();
builder.Services.AddSingleton<Opstrax.Api.Services.Connectors.ConnectorRegistry>();
// Server-side Google Maps (geocoding/routing) using the tenant's stored Maps key.
// Map tiles stay on free Leaflet; Google is used only where it adds capability.
builder.Services.AddSingleton<Opstrax.Api.Services.Connectors.GoogleMapsService>();
builder.Services.AddSingleton<CoreSchemaService>();
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
builder.Services.AddSingleton<FleetProductionReadinessService>();
builder.Services.AddSingleton<TelemetryLiveStateService>();
// Agentic Brain — the model behind the AI foundation's empty reasoning slot.
builder.Services.AddSingleton<AgenticBrainService>();
builder.Services.AddScoped<IncidentService>();
builder.Services.AddScoped<CustomerPortalService>();
// Computes customers' SLA health / delivery experience / risk from real delivery history
// (jobs, POD, feedback, invoices). Replaces the hardcoded 94/92/18 scores.
builder.Services.AddScoped<CustomerHealthService>();
builder.Services.AddScoped<DemoTenantSeeder>();
builder.Services.AddScoped<OpsMetricsService>();
builder.Services.AddSingleton<FoundationSchemaService>();
builder.Services.AddSingleton<SafetyMaintenanceFoundationSchemaService>();
builder.Services.AddSingleton<SafetyMaintenanceFoundationService>();
builder.Services.AddSingleton<BusinessSpineSchemaService>();
builder.Services.AddSingleton<CommercialFoundationSchemaService>();
builder.Services.AddSingleton<RevenueReadinessSchemaService>();
builder.Services.AddSingleton<FinanceActivationSchemaService>();
builder.Services.AddSingleton<SettlementSchemaService>();
builder.Services.AddSingleton<TaxSchemaService>();
builder.Services.AddSingleton<BillingProfileSchemaService>();
builder.Services.AddSingleton<RevenueRecognitionSchemaService>();
builder.Services.AddSingleton<FinancialConfigSchemaService>();
builder.Services.AddSingleton<GeneralLedgerSchemaService>();
builder.Services.AddSingleton<GeneralLedgerService>();
builder.Services.AddSingleton<GeneralLedgerPeriodSchemaService>();
builder.Services.AddSingleton<GeneralLedgerPeriodService>();
builder.Services.AddSingleton<GeneralLedgerExportService>();
builder.Services.AddSingleton<DetentionSchemaService>();
builder.Services.AddSingleton<DetentionReviewService>();
builder.Services.AddSingleton<Stage9SchemaService>();
builder.Services.AddSingleton<BusinessSpineService>();
builder.Services.AddSingleton<RatingService>();
builder.Services.AddSingleton<SettlementService>();
builder.Services.AddSingleton<TaxService>();
builder.Services.AddSingleton<BillingConsolidationService>();
builder.Services.AddSingleton<RevenueRecognitionService>();
builder.Services.AddSingleton<IOutboxMessageHandler, InvoiceIssuedRecognitionHandler>();
// Fan-out sibling on the same invoice.issued event: auto-post the invoice to the general ledger.
builder.Services.AddSingleton<IOutboxMessageHandler, InvoiceIssuedGeneralLedgerHandler>();
// AP -> GL: accrue the payable on settlement approval, relieve it on payment.
builder.Services.AddSingleton<IOutboxMessageHandler, SettlementApprovedGlPostingHandler>();
builder.Services.AddSingleton<IOutboxMessageHandler, SettlementPaymentGlPostingHandler>();
// AR credit notes: maker-checker corrections against issued invoices; GL reversal on issue.
builder.Services.AddSingleton<CreditNoteService>();
builder.Services.AddSingleton<IOutboxMessageHandler, CreditNoteIssuedGeneralLedgerHandler>();
// Detention: real email delivery for the pre-expiry 'meter running' notice.
builder.Services.AddSingleton<IOutboxMessageHandler, DetentionWarningNotificationHandler>();
// Alert notifications: email/SMS fan-out per user_notification_prefs (Settings → Notifications).
builder.Services.AddSingleton<IOutboxMessageHandler, AlertNotificationDeliveryHandler>();
builder.Services.AddSingleton<FinancialConfigService>();
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
builder.Services.AddSingleton<IOutboxMessageHandler, JobDeliveredBillingHandler>();
builder.Services.AddSingleton<IOutboxMessageHandlerRegistry, OutboxMessageHandlerRegistry>();
builder.Services.AddSingleton<IOutboxDispatcher, PostgresOutboxDispatcher>();
if (outboxDispatcherOptions.Enabled && (!IsProtectedEnvironment(builder.Environment) || outboxDispatcherOptions.AllowProduction))
{
    builder.Services.AddHostedService<OutboxDispatcherBackgroundService>();
}
// P10 Security + Compliance
builder.Services.AddSingleton<SecuritySchemaService>();
// Tenant API access — per-company hashed API keys + webhook subscriptions (Settings → API & Webhooks)
builder.Services.AddSingleton<TenantApiSchemaService>();
// Platform Admin — global SaaS business control plane (separate from tenant admin)
builder.Services.AddSingleton<PlatformSchemaService>();
// Operator-editable platform configuration (SMTP today) — DB-first with an env fallback, so
// mail can be switched on from the console instead of requiring a redeploy. Singleton to match
// Database/PiiProtectionService, and because the outbox dispatcher (a singleton) resolves the
// mail service to deliver detention notices.
builder.Services.AddSingleton<PlatformSettingsService>();
builder.Services.AddSingleton<PlatformMailService>();
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
builder.Services.AddSingleton<PlatformBillingSchemaService>();
builder.Services.AddScoped<PlatformBillingService>();
builder.Services.AddScoped<EntitlementService>();
builder.Services.AddScoped<FeatureFlagService>();
builder.Services.AddSingleton<RolePermissionReconciler>();
builder.Services.AddSingleton<PlatformSuperAdminReconciler>();
// Market-pack engine (Canada/NA + Saudi/GCC) — regional capability + compliance
builder.Services.AddSingleton<MarketPackSchemaService>();
builder.Services.AddSingleton<Opstrax.Api.Seed.MarketPackSeeder>();
// Fleet TMS (PR1) — shipment lifecycle, POD workflow & public tracking (additive)
builder.Services.AddSingleton<FleetTmsSchemaService>();
builder.Services.AddSingleton<FleetTmsColdChainSchemaService>();
builder.Services.AddSingleton<FleetTmsColdChainFoundationSchemaService>();
builder.Services.AddSingleton<FleetTmsColdChainFoundationService>();
builder.Services.AddSingleton<FleetTmsLogisticsSchemaService>();
builder.Services.AddSingleton<FeatureFlagSchemaService>();
builder.Services.AddSingleton<RlsReconciliationSchemaService>();
builder.Services.AddSingleton<Opstrax.Api.Seed.FleetTmsSeeder>();
builder.Services.AddScoped<SecuritySettingsService>();
builder.Services.AddScoped<MfaChallengeConsumptionService>();
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
// Automatic third-party position sync (Samsara overlay -> continuous positions -> detention).
builder.Services.AddHostedService<ConnectorSyncBackgroundService>();
builder.Services.AddHostedService<TripBackgroundService>();
builder.Services.AddHostedService<MaintenanceBackgroundService>();
builder.Services.AddHostedService<EscalationBackgroundService>();
// Bridges telemetry_alerts into the notification spine: in-app fan-out per user prefs +
// outbox enqueue for the email/SMS delivery handler.
builder.Services.AddHostedService<AlertNotificationBridgeService>();
// Derives hos_violation / maintenance_due / sla_breach / fuel_anomaly / idling alerts
// from their source tables — the generators behind the notification-prefs matrix rows.
builder.Services.AddHostedService<OperationalAlertDetectionService>();
// Agentic Ops Copilot — reasons over open dispatch exceptions and proposes actions.
builder.Services.AddHostedService<AgenticOpsBackgroundService>();
builder.Services.AddHostedService<ScheduledReportBackgroundService>();
// Data-retention enforcement — executes stored operational-row policies while
// respecting legal hold. Protected-environment startup requires RetentionWorker:Enabled=true,
// and its heartbeat is part of the critical-worker readiness contract.
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
            .WithExposedHeaders(
                "X-Trace-Id",
                "X-Correlation-Id",
                "X-Deployment-Version",
                "X-CSRF-Token",
                // Paged fleet registers read this header in the browser. Without
                // exposing it through CORS, Axios only sees the current page length
                // and hides Next/Previous even when more branch records exist.
                "X-Total-Count");
    });
});

var app = builder.Build();

// Validate before any database initialization or hosted workload starts. Staging and Production
// must explicitly enable tenant RLS context enforcement; missing/false is fatal.
{
    var validator = app.Services.GetRequiredService<ConfigValidationService>();
    var result = validator.Validate();
    foreach (var issue in result.Issues.Where(i => i.Level is "fail" or "warn"))
        app.Logger.Log(issue.Level == "fail" ? LogLevel.Error : LogLevel.Warning,
            new EventId(0, $"config_{issue.Check}"), "Config check '{Check}': {Message}", issue.Check, issue.Message);

    try
    {
        ConfigValidationService.EnsureStartupAllowed(result, IsProtectedEnvironment(app.Environment));
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

// A connection string that merely claims a restricted username is not evidence.
// Protected environments open both pools and validate the server-reported roles before schema
// checks, background services, or request middleware can touch customer data.
if (IsProtectedEnvironment(app.Environment) && app.Configuration.GetValue<bool>("Rls:EnforceTenantContext"))
{
    try
    {
        await app.Services.GetRequiredService<Database>().ValidateProductionIdentitiesAsync();
        app.Logger.LogInformation("Database identity separation verified (opstrax_app + opstrax_system).");
    }
    catch (Exception ex)
    {
        app.Logger.LogCritical(ex, "Protected-environment startup refused: dual database identities could not be proven.");
        throw new InvalidOperationException("Staging and Production require exact isolated opstrax_app and opstrax_system database identities.", ex);
    }
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
    // ── MIGRATIONS ARE THE ONLY SCHEMA AUTHORITY (stage88) ────────────────────
    // Boot-time runtime DDL is RETIRED. It was never a schema authority: this
    // process skipped every *SchemaService whenever it connected as the restricted
    // opstrax_app role under RLS enforcement — always true in staging and
    // production — so 1,006 columns and 51 tables that only those services declared
    // could never exist there, and the endpoints selecting them returned 42703 /
    // 42P01 while /health/ready stayed green. A boot path that runs in development
    // and silently does not run in production is a split-brain, not a fallback.
    // database/migrations/2026_08_22_stage88_runtime_schema_service_contract.sql
    // materializes every one of those declarations as migration-owned schema, so
    // the migration chain is now a strict superset of what this block ever built.
    //
    // The *SchemaService classes and their declaration lists are DELIBERATELY kept:
    // they are the generator input for stage88 and the subject of the runtime/
    // migration parity test. Only their EXECUTION at boot is retired.
    //
    // DEV / LOCAL ONBOARDING is the same command production uses:
    //   NEON_PG_URI=postgresql://…/opstrax_local ./tools/apply-neon-predeploy-migrations.sh
    //
    // The decision is now explicit configuration, never inferred from the connected
    // role (see ResolveRuntimeSchemaDdlAsync): once a database carries the stage88
    // ledger row, boot performs NO DDL on it — which is every protected environment
    // and every correctly-onboarded dev box. The one surviving exception is a
    // database the chain has never touched at all, where the legacy path still
    // bootstraps rather than leaving a developer with an empty database and no
    // explanation. `SchemaInit:RunRuntimeDdl` overrides both directions.
    var runtimeIdentity = scope.ServiceProvider.GetRequiredService<Database>();
    await AssertRuntimeDatabaseIdentityAsync(app, runtimeIdentity);
    var runSchemaInit = await ResolveRuntimeSchemaDdlAsync(app, builder.Configuration, runtimeIdentity);
    if (runSchemaInit)
    {
    await RunSchemaStep(app, "Core", () => scope.ServiceProvider.GetRequiredService<CoreSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "Batch1", () => scope.ServiceProvider.GetRequiredService<Batch1SchemaService>().EnsureAsync());
    await RunSchemaStep(app, "Batch2", () => scope.ServiceProvider.GetRequiredService<Batch2SchemaService>().EnsureAsync());
    // Secure customer-ETA tracking token (breach-class P0 fix): the public /api/customer-eta/track
    // endpoint must key on an unguessable 256-bit secret, never the enumerable jobs.tracking_code.
    // Add the column, enforce uniqueness, and disable every legacy link that has no secure token so
    // the old 'ETA-JOB-xxxx' / 'B2ETA-xxxx' codes stop resolving. Idempotent; safe to re-run.
    await RunSchemaStep(app, "CustomerEtaSecureToken", async () =>
    {
        var etaDb = scope.ServiceProvider.GetRequiredService<Database>();
        await etaDb.ExecuteAsync("ALTER TABLE customer_eta_links ADD COLUMN IF NOT EXISTS secure_token VARCHAR(80) NULL");
        await etaDb.ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS ux_customer_eta_links_secure_token ON customer_eta_links (secure_token) WHERE secure_token IS NOT NULL");
        await etaDb.ExecuteAsync("UPDATE customer_eta_links SET public_status='Disabled' WHERE secure_token IS NULL AND public_status <> 'Disabled'");
    });
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
    var revenueReadinessSchemaEnabled = builder.Configuration.GetValue("RevenueReadinessSchema:Enabled", !IsProtectedEnvironment(app.Environment));
    if (revenueReadinessSchemaEnabled)
    {
        await RunSchemaStep(app, "RevenueReadiness", () => scope.ServiceProvider.GetRequiredService<RevenueReadinessSchemaService>().EnsureAsync());
    }
    var financeActivationSchemaEnabled = builder.Configuration.GetValue("FinanceActivationSchema:Enabled", !IsProtectedEnvironment(app.Environment));
    if (financeActivationSchemaEnabled)
    {
        await RunSchemaStep(app, "FinanceActivation", () => scope.ServiceProvider.GetRequiredService<FinanceActivationSchemaService>().EnsureAsync());
    }
    var settlementSchemaEnabled = builder.Configuration.GetValue("SettlementSchema:Enabled", !IsProtectedEnvironment(app.Environment));
    if (settlementSchemaEnabled)
    {
        await RunSchemaStep(app, "Settlement", () => scope.ServiceProvider.GetRequiredService<SettlementSchemaService>().EnsureAsync());
    }
    var taxSchemaEnabled = builder.Configuration.GetValue("TaxSchema:Enabled", !IsProtectedEnvironment(app.Environment));
    if (taxSchemaEnabled)
    {
        await RunSchemaStep(app, "Tax", () => scope.ServiceProvider.GetRequiredService<TaxSchemaService>().EnsureAsync());
    }
    var billingSchemaEnabled = builder.Configuration.GetValue("BillingSchema:Enabled", !IsProtectedEnvironment(app.Environment));
    if (billingSchemaEnabled)
    {
        await RunSchemaStep(app, "Billing", () => scope.ServiceProvider.GetRequiredService<BillingProfileSchemaService>().EnsureAsync());
    }
    var revrecSchemaEnabled = builder.Configuration.GetValue("RevRecSchema:Enabled", !IsProtectedEnvironment(app.Environment));
    if (revrecSchemaEnabled)
    {
        await RunSchemaStep(app, "RevRec", () => scope.ServiceProvider.GetRequiredService<RevenueRecognitionSchemaService>().EnsureAsync());
    }
    var finConfigSchemaEnabled = builder.Configuration.GetValue("FinConfigSchema:Enabled", !IsProtectedEnvironment(app.Environment));
    if (finConfigSchemaEnabled)
    {
        await RunSchemaStep(app, "FinConfig", () => scope.ServiceProvider.GetRequiredService<FinancialConfigSchemaService>().EnsureAsync());
    }
    var glSchemaEnabled = builder.Configuration.GetValue("GeneralLedgerSchema:Enabled", !IsProtectedEnvironment(app.Environment));
    if (glSchemaEnabled)
    {
        await RunSchemaStep(app, "GeneralLedger", () => scope.ServiceProvider.GetRequiredService<GeneralLedgerSchemaService>().EnsureAsync());
        await RunSchemaStep(app, "GeneralLedgerPeriods", () => scope.ServiceProvider.GetRequiredService<GeneralLedgerPeriodSchemaService>().EnsureAsync());
    }
    var stage9SchemaEnabled = builder.Configuration.GetValue("Stage9Schema:Enabled", !IsProtectedEnvironment(app.Environment));
    if (stage9SchemaEnabled)
    {
        await RunSchemaStep(app, "Stage9", () => scope.ServiceProvider.GetRequiredService<Stage9SchemaService>().EnsureAsync());
    }
    await RunSchemaStep(app, "FeatureFlags",       () => scope.ServiceProvider.GetRequiredService<FeatureFlagSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "Security",          () => scope.ServiceProvider.GetRequiredService<SecuritySchemaService>().EnsureAsync());
    await RunSchemaStep(app, "TenantApi",         () => scope.ServiceProvider.GetRequiredService<TenantApiSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "Platform",          () => scope.ServiceProvider.GetRequiredService<PlatformSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "CountryProfiles",    () => scope.ServiceProvider.GetRequiredService<CountryProfileSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "Zatca",              () => scope.ServiceProvider.GetRequiredService<ZatcaSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "Revenue",           () => scope.ServiceProvider.GetRequiredService<RevenueSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "MarketPacks",        () => scope.ServiceProvider.GetRequiredService<MarketPackSchemaService>().EnsureAsync());
    // After Revenue + MarketPacks: itemization references their meters and packs.
    await RunSchemaStep(app, "PlatformBilling",    () => scope.ServiceProvider.GetRequiredService<PlatformBillingSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "FleetTms",           () => scope.ServiceProvider.GetRequiredService<FleetTmsSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "FleetTmsColdChain",  () => scope.ServiceProvider.GetRequiredService<FleetTmsColdChainSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "FleetTmsColdChainFoundation", () => scope.ServiceProvider.GetRequiredService<FleetTmsColdChainFoundationSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "FleetTmsLogistics",  () => scope.ServiceProvider.GetRequiredService<FleetTmsLogisticsSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "FleetTmsSeed",        () => scope.ServiceProvider.GetRequiredService<Opstrax.Api.Seed.FleetTmsSeeder>().EnsureAsync());
    await RunSchemaStep(app, "MarketPackSeed",      () => scope.ServiceProvider.GetRequiredService<Opstrax.Api.Seed.MarketPackSeeder>().EnsureAsync());
    // MUST run LAST: enrolls every tenant-scoped table created above (feature_flags and
    // any table added by a later schema service) into RLS + FORCE, closing the coverage
    // gap the point-in-time Stage 19/22 migrations cannot cover for boot-created tables.
    await RunSchemaStep(app, "Detention",           () => scope.ServiceProvider.GetRequiredService<DetentionSchemaService>().EnsureAsync());
    await RunSchemaStep(app, "RlsReconciliation",   () => scope.ServiceProvider.GetRequiredService<RlsReconciliationSchemaService>().EnsureAsync());
    }
    else
    {
        app.Logger.LogInformation(
            "Boot-time schema DDL is retired — migrations are the only schema authority. " +
            "Apply database/migrations via tools/apply-neon-predeploy-migrations.sh (through stage88) " +
            "before starting the API; /health/ready reports any object the contract still misses.");
    }
}

// Built-in role permissions are reconciled from RolePermissionDefaults on EVERY boot,
// deliberately OUTSIDE the schema-init gate above. This is DML, not DDL, so the restricted
// `opstrax_app` role can run it — which matters because production is exactly the
// environment where schema init is skipped, and exactly where the drift this repairs
// (the Driver role missing `driver:self`, locking every driver out of the driver portal)
// was fatal. Additive and idempotent; see RolePermissionReconciler for the full rationale.
using (var scope = app.Services.CreateScope())
{
    var reconciliationDb = scope.ServiceProvider.GetRequiredService<Database>();
    var reconciler = scope.ServiceProvider.GetRequiredService<RolePermissionReconciler>();
    await reconciliationDb.RunInSystemScopeAsync(() => reconciler.ReconcileAsync());
}

// Break-glass reconcile of the bootstrap Platform Super Admin password from env. Also DML,
// also OUTSIDE the schema-init gate (and self-scoping to the opstrax_system identity), for
// the same reason: the one-time seed never re-reads PLATFORM_SUPERADMIN_PASSWORD, and in
// production the seed is skipped entirely — so a rotated env credential could never reach the
// control plane, leaving the operator locked out with "Invalid credentials" against the exact
// password they set in Render. Inert unless PLATFORM_SUPERADMIN_RESET is explicitly armed.
using (var scope = app.Services.CreateScope())
{
    var platformAdminReconciler = scope.ServiceProvider.GetRequiredService<PlatformSuperAdminReconciler>();
    await platformAdminReconciler.ReconcileAsync();
}

// platform_settings (operator-editable SMTP + app URLs) must exist even where the
// schema-init gate is skipped (production: restricted role, RLS enforced, owner applies
// migrations out-of-band). CREATE TABLE IF NOT EXISTS is DML-adjacent enough to attempt
// under the system identity: where that identity may create tables this self-heals; where
// it may not, the failure is swallowed after a loud log and the console degrades to
// env-only configuration with save disabled — the operator applies stage83 out-of-band.
// (Skipping this entirely was the 2026-08-21 incident: the Email & SMTP page loaded from
// env fallback but every save 500'd against the missing table.)
using (var scope = app.Services.CreateScope())
{
    var settingsDb = scope.ServiceProvider.GetRequiredService<Database>();
    var platformSettings = scope.ServiceProvider.GetRequiredService<PlatformSettingsService>();
    try
    {
        await settingsDb.RunInSystemScopeAsync(() => platformSettings.EnsureSchemaAsync());
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex,
            "platform_settings bootstrap failed — the system identity cannot create the table. " +
            "Apply database/migrations/2026_08_21_stage83_platform_settings.sql as the owner; " +
            "until then Email & SMTP settings are environment-only and console saves are refused cleanly.");
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
    if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
    {
        // Authenticated and control-plane API payloads can contain tenant data.
        // Prevent browser/proxy caches from resurrecting them after logout or
        // serving them to a later identity in the same browser profile.
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Expires = "0";
    }
    await next();
});

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseCors("OpsTraxCors");
app.UseRateLimiter();
app.UseMiddleware<CsrfMiddleware>();
app.UseSwagger();

// RLS enforcement (Option A1). Protected-environment startup requires this to be explicitly
// true. Non-production may leave it off for local/test compatibility. Enable it
// only when PG_CONNECTION_APP uses the restricted `opstrax_app` role
// (see 2026_06_30_stage20_rls_force_and_app_role.sql). When true, each authenticated
// request runs inside an app transaction carrying a DB-signed ticket bound to that
// backend PID + transaction id. Pre-tenant auth, public/platform and background paths
// use the separately authenticated opstrax_system pool; no client-set bypass GUC exists.
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

            // Wraps next() under the isolated system identity for no-tenant-context
            // paths (public / platform / device-auth).
            async Task InvokeUnderSystemAsync()
            {
                if (!rlsEnforceTenantContext) { await next(); return; }
                await using var sys = await scopedDb.BeginSystemScopeAsync(context.RequestAborted);
                scopes.Current = sys;
                try { await next(); await sys.CompleteAsync(context.RequestAborted); }
                finally { scopes.Current = null; }
            }
            if (string.Equals(path, "/api/auth/login", StringComparison.OrdinalIgnoreCase) ||
                // Second-factor login completion is pre-session too: it validates a challenge, reads
                // users/user_mfa_status and mints a session with no prior tenant context, like login.
                string.Equals(path, "/api/auth/mfa/login-verify", StringComparison.OrdinalIgnoreCase) ||
                // Pre-login SSO discovery: no tenant context exists at email-entry
                // time, so it reads the RLS-forced sso_connections table under the
                // isolated system scope, exactly like /api/auth/login.
                string.Equals(path, "/api/auth/sso/discover", StringComparison.OrdinalIgnoreCase) ||
                // OIDC login start + callback are pre-session too: they read the
                // sso_connections + users tables and mint a session with no prior
                // tenant context, so they run under the same system scope as login.
                path.StartsWith("/api/auth/sso/start", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, "/api/auth/sso/callback", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, "/api/auth/forgot-password", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, "/api/auth/reset-password", StringComparison.OrdinalIgnoreCase) ||
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
                // GT06/PT40 GPS-tracker ingest — IMEI-authenticated hardware webhook, no bearer.
                path.StartsWith("/api/telemetry/gps-ingest", StringComparison.OrdinalIgnoreCase) ||
                // OBD/J1939 fault-code ingest — same device (X-Device-Key + HMAC) auth as
                // telemetry ingest; a physical diagnostics device has no user bearer token.
                path.StartsWith("/api/maintenance/fault-codes/ingest", StringComparison.OrdinalIgnoreCase) ||
                (context.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                 path.StartsWith("/api/customer-eta/track/", StringComparison.OrdinalIgnoreCase)) ||
                // Customer-facing public tracking — token-scoped, expiring, revocable; no user session
                (context.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                 path.StartsWith("/api/customer-visibility/tracking/", StringComparison.OrdinalIgnoreCase)) ||
                // Fleet TMS public shipment tracking — token-scoped, expiring, revocable; no user session
                (context.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                 path.StartsWith("/api/public/shipments/track/", StringComparison.OrdinalIgnoreCase)) ||
                // Detention evidence page — the no-login artifact an AP clerk verifies; token-scoped, expiring, revocable
                (context.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                 path.StartsWith("/api/public/detention/evidence/", StringComparison.OrdinalIgnoreCase)))
            {
                await InvokeUnderSystemAsync();
                return;
            }

            // SSE stream path: authenticate exclusively via short-lived stream ticket (?sst=).
            // This avoids long-lived session tokens appearing in query strings (logs, proxies).
            // The SST is a one-shot, short-lived capability carrying tenant, branch and
            // the precise live-location permission snapshot, signed with HMAC-SHA256.
            var authHeader = context.Request.Headers.Authorization.ToString();
            if (string.Equals(path, "/api/telemetry/stream", StringComparison.OrdinalIgnoreCase))
            {
                var sst = context.Request.Query["sst"].FirstOrDefault() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(sst))
                {
                    var claims = TelemetryTicketHelper.ValidateScoped(TelemetryKeyStore.SseTicketKey, sst);
                    if (!claims.Ok)
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail("Invalid or expired stream ticket"));
                        return;
                    }
                    // Durable atomic consume: the same signed capability cannot open a
                    // second stream on another instance or after a process restart.
                    // This happens before tenant context exists, under the isolated
                    // system identity; the row is still claim-bound and short-lived.
                    var consumed = await scopedDb.RunInSystemScopeAsync(() => scopedDb.ExecuteAsync(
                        @"UPDATE telemetry_stream_ticket_nonces
                          SET consumed_at=NOW()
                          WHERE nonce_hash=@nonce AND user_id=@uid AND audit_company_id=@cid
                            AND branch_id IS NOT DISTINCT FROM @branchId
                            AND consumed_at IS NULL AND expires_at>NOW()",
                        c =>
                        {
                            c.Parameters.AddWithValue("@nonce", TelemetryTicketHelper.HashNonce(claims.Nonce));
                            c.Parameters.AddWithValue("@uid", claims.UserId); c.Parameters.AddWithValue("@cid", claims.CompanyId);
                            c.Parameters.AddWithValue("@branchId", (object?)claims.BranchId ?? DBNull.Value);
                        }, context.RequestAborted), context.RequestAborted);
                    if (consumed != 1)
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail("Stream ticket was already used, expired, or revoked"));
                        return;
                    }
                    context.Items[EndpointMappings.AuthUserIdItemKey]      = claims.UserId;
                    context.Items[EndpointMappings.AuthCompanyIdItemKey]   = claims.CompanyId;
                    context.Items[EndpointMappings.AuthRoleItemKey]        = "sst-client";
                    context.Items[EndpointMappings.AuthPermissionsItemKey] = claims.Permissions;
                    if (claims.BranchId is { } sstBranchId)
                        context.Items[EndpointMappings.AuthBranchIdItemKey] = sstBranchId;
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
            // system identity so it succeeds before a tenant ticket exists.
            var sessionSql =
                @"SELECT s.user_id, s.company_id, u.role_name, u.role_id, u.customer_id, u.branch_id,
                         u.permissions_json, r.permissions_json role_permissions_json,
                         s.impersonation_grant_id, pis.grant_ref impersonation_grant_ref,
                         pis.expires_at impersonation_grant_expires_at,
                         pis.admin_id impersonation_admin_id
                  FROM user_sessions s
                  JOIN users u ON u.id = s.user_id AND u.company_id = s.company_id
                  LEFT JOIN roles r ON r.id = u.role_id AND (r.company_id IS NULL OR r.company_id=u.company_id)
                  LEFT JOIN platform_impersonation_sessions pis ON pis.id=s.impersonation_grant_id
                  WHERE s.session_token=@token
                    AND s.expires_at > NOW()
                    AND u.status='Active'
                    AND (
                      s.impersonation_grant_id IS NULL
                      OR (
                        pis.id IS NOT NULL AND pis.ended_at IS NULL AND pis.expires_at > NOW()
                        AND pis.company_id=s.company_id AND pis.target_user_id=s.user_id
                      )
                    )
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

            // Role membership is authoritative. Legacy user-level JSON is consulted only for
            // accounts without a role, so removed role grants cannot linger.
            //
            // This calls the SAME resolver as the login endpoint. It previously duplicated the
            // logic, and the two copies had drifted into opposite precedence — login answered
            // from users.permissions_json, this answered from the role — so the SPA could be
            // told it had permissions the API would then deny (and vice versa). One resolver,
            // one answer. Do not re-inline this.
            Task<string[]> ResolvePermissions() => EndpointMappings.ResolveEffectivePermissionsAsync(
                roleId, roleName,
                session.GetValueOrDefault("rolePermissionsJson"),
                session.GetValueOrDefault("permissionsJson"),
                db, context.RequestAborted);
            var permissionSet = rlsEnforceTenantContext
                ? await db.RunInSystemScopeAsync(ResolvePermissions, context.RequestAborted)
                : await ResolvePermissions();
            var permissions = permissionSet.ToHashSet(StringComparer.OrdinalIgnoreCase);
            (long PlatformAdminId, long GrantId, Guid GrantRef, string Method, string Path)? supportAccessAudit = null;

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

            // Bounded Platform support access is a distinct principal mode. The
            // grant is revalidated by the session query on every request, then the
            // edge denies mutation before any tenant handler or transaction runs.
            if (session.TryGetValue("impersonationGrantId", out var grantIdValue)
                && grantIdValue is not null and not DBNull)
            {
                var grantId = Convert.ToInt64(grantIdValue);
                var grantRef = Guid.Parse(session["impersonationGrantRef"]!.ToString()!);
                var grantExpiresAt = Convert.ToDateTime(session["impersonationGrantExpiresAt"]);
                var platformAdminId = Convert.ToInt64(session["impersonationAdminId"]);
                context.Items[PlatformImpersonationPolicy.GrantIdItemKey] = grantId;
                context.Items[PlatformImpersonationPolicy.GrantRefItemKey] = grantRef.ToString("D");
                context.Items[PlatformImpersonationPolicy.GrantExpiresAtItemKey] = grantExpiresAt;

                // A dedicated distributed ceiling prevents an authenticated support
                // token from turning the mandatory dual-audit trail into a write-amplification
                // vector. Global/IP and principal limits remain additional layers.
                var withinAuditBudget = await SupportAccessWithinAuditBudgetAsync(
                    scopedDb, grantId, context.RequestAborted);
                if (!withinAuditBudget)
                {
                    context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.Response.Headers.RetryAfter = "60";
                    await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail(
                        "Support access rate limit exceeded", "Retry after 60 seconds."),
                        context.RequestAborted);
                    return;
                }

                var readOnlyAllowed = PlatformImpersonationPolicy.IsReadOnlyRequestAllowed(context.Request.Method, path);
                if (!readOnlyAllowed)
                {
                    await AuditSupportAccessRequestAsync(scopedDb, companyId, platformAdminId, grantId,
                        grantRef, context.Request.Method, path, allowed: false,
                        StatusCodes.Status403Forbidden, context.RequestAborted);
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail(
                        "Read-only support access", "This support session cannot change tenant data."),
                        context.RequestAborted);
                    return;
                }
                supportAccessAudit = (platformAdminId, grantId, grantRef, context.Request.Method, path);
            }

            // ── Feature entitlement enforcement (server-side, tenant-isolated) ──────
            // Platform Admin controls which modules a tenant may access. If the
            // tenant's policy denies the module this path belongs to, block it here
            // even if the client calls the API directly. Existing tenants use
            // legacy_allow (missing row = allow); new customers use package_allowlist
            // (missing row = deny).
            var moduleKey = ModuleKeyForPath(path);
            if (moduleKey is not null)
            {
                const string entitlementSql = """
                    SELECT COUNT(*)
                    FROM companies c
                    LEFT JOIN tenant_entitlements e
                      ON e.company_id=c.id AND e.module_key=@mk
                    WHERE c.id=@cid
                      AND (
                        c.entitlement_policy_mode NOT IN ('legacy_allow','package_allowlist')
                        OR
                        (c.entitlement_policy_mode='package_allowlist' AND COALESCE(e.enabled,false)=false)
                        OR
                        (c.entitlement_policy_mode='legacy_allow' AND e.enabled=false)
                      )
                    """;
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

            // ── Feature-flag route kill-switch (server-side, tenant-isolated) ──────
            // A REAL flag gate: turning a flag off here stops the traffic at the edge,
            // even if the API is called directly. Runs BEFORE the tenant scope opens, so
            // (like the entitlement check above) it must read in system scope under RLS.
            //   defaultOn:true → a tenant with no row yet keeps working. Kill switches
            //   over EXISTING behaviour must never fail closed on a missing row.
            var flagGate = FlagGateForPath(path);
            if (flagGate is not null)
            {
                var (flagKey, defaultOn) = flagGate.Value;
                const string flagSql = "SELECT enabled, rollout_pct FROM feature_flags WHERE company_id=@cid AND flag_key=@fk";
                void BindFlag(Npgsql.NpgsqlCommand c)
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@fk", flagKey);
                }
                var flagRow = rlsEnforceTenantContext
                    ? await db.QuerySingleInSystemScopeAsync(flagSql, BindFlag, context.RequestAborted)
                    : await db.QuerySingleAsync(flagSql, BindFlag, context.RequestAborted);

                if (!FeatureFlagService.Resolve(flagRow, flagKey, userId, defaultOn))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail("Feature turned off",
                        $"The '{flagKey}' feature is currently switched off for your account."));
                    return;
                }
            }

            // Authenticated handlers share one app transaction. RLS derives the tenant
            // only from its DB-signed, PID+txid-bound ticket.
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

            // Log the completed outcome, not an optimistic pre-handler "read". A
            // 403/404/429 therefore cannot be represented as successful access.
            if (supportAccessAudit is { } supportAudit)
            {
                await AuditSupportAccessRequestAsync(scopedDb, companyId, supportAudit.PlatformAdminId,
                    supportAudit.GrantId, supportAudit.GrantRef, supportAudit.Method, supportAudit.Path,
                    allowed: true, context.Response.StatusCode, context.RequestAborted);
            }
        });
        // The authoritative company/user identifiers now exist. Apply the regular
        // API quota here, not at the public edge, so users sharing a corporate NAT
        // cannot exhaust one another's allowance. Sensitive anonymous/device/public
        // paths were already handled by the early IP-based abuse limiter.
        branch.UseMiddleware<PrincipalRateLimitingMiddleware>();
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
static async Task<IResult> ReadinessAsync(
    Database db,
    ConfigValidationService cfg,
    FleetProductionReadinessService fleetContract,
    DataProtectionReadinessService dataProtectionReadiness,
    IWebHostEnvironment environment,
    CancellationToken ct)
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

    DataProtectionReadinessResult? dataProtectionResult = null;
    if (IsProtectedEnvironment(environment) && dbOk && cfgResult.FailCount == 0)
    {
        dataProtectionResult = await dataProtectionReadiness.CheckAsync(ct);
        checks["data_protection_key_ring"] = new
        {
            status = dataProtectionResult.Ready ? "ready" : "unavailable",
            key_count = dataProtectionResult.KeyCount,
            failure_code = dataProtectionResult.FailureCode,
        };
        if (!dataProtectionResult.Ready) failure ??= "data_protection_key_ring_unavailable";
    }

    // Protected-environment Fleet readiness is a real database-contract proof, not SELECT 1.
    // It verifies the restricted runtime identity, the complete Stage-50 schema,
    // FORCE RLS/policies/grants, market reference data and correctness indexes.
    // Details remain in structured logs; the public envelope exposes counts/booleans
    // only and never SQL, role credentials, table data or connection values.
    FleetProductionContractResult? fleetResult = null;
    if (IsProtectedEnvironment(environment) && dbOk && cfgResult.FailCount == 0)
    {
        fleetResult = await fleetContract.CheckAsync(ct);
        checks["fleet_production_contract"] = new
        {
            status = fleetResult.Ready ? "ready" : "invalid",
            role_restricted = fleetResult.RoleRestricted,
            missing_tables = fleetResult.MissingTables,
            rls_violations = fleetResult.RlsViolations,
            grant_violations = fleetResult.GrantViolations,
            tenant_coverage_violations = fleetResult.TenantCoverageViolations,
            tenant_grant_violations = fleetResult.TenantGrantViolations,
            default_privilege_violations = fleetResult.DefaultPrivilegeViolations,
            runtime_route_column_violations = fleetResult.RuntimeRouteColumnViolations,
            runtime_route_object_violations = fleetResult.RuntimeRouteObjectViolations,
            fleet_integrity_object_violations = fleetResult.FleetIntegrityObjectViolations,
            workforce_contract_violations = fleetResult.WorkforceContractViolations,
            migration_applied = fleetResult.MigrationApplied,
            runtime_support_migration_applied = fleetResult.RuntimeSupportMigrationApplied,
            tenant_coverage_migration_applied = fleetResult.TenantCoverageMigrationApplied,
            cold_chain_integrity_migration_applied = fleetResult.ColdChainIntegrityMigrationApplied,
            runtime_route_migration_applied = fleetResult.RuntimeRouteMigrationApplied,
            asset_type_integrity_migration_applied = fleetResult.AssetTypeIntegrityMigrationApplied,
            workforce_schedule_integrity_migration_applied = fleetResult.WorkforceScheduleIntegrityMigrationApplied,
            tenant_ticket_migration_applied = fleetResult.TenantTicketMigrationApplied,
            data_protection_key_ring_migration_applied = fleetResult.DataProtectionKeyRingMigrationApplied,
            market_catalog_ready = fleetResult.MarketCatalogReady,
            tenant_provisioning_ready = fleetResult.TenantProvisioningReady,
            indexes_ready = fleetResult.IndexesReady,
            critical_worker_violations = fleetResult.CriticalWorkerViolations,
            raw_critical_worker_violations = fleetResult.RawCriticalWorkerViolations,
            missing_critical_workers = fleetResult.MissingCriticalWorkers,
            stale_critical_workers = fleetResult.StaleCriticalWorkers,
            failed_critical_workers = fleetResult.FailedCriticalWorkers,
            critical_worker_startup_grace_active = fleetResult.CriticalWorkerStartupGraceActive,
            critical_worker_startup_grace_remaining_seconds = fleetResult.CriticalWorkerStartupGraceRemainingSeconds,
            failure_code = fleetResult.FailureCode
        };
        if (!fleetResult.Ready) failure ??= "fleet_production_contract_invalid";
    }

    var ready = dbOk && cfgResult.FailCount == 0 &&
                (dataProtectionResult?.Ready ?? !IsProtectedEnvironment(environment)) &&
                (fleetResult?.Ready ?? !IsProtectedEnvironment(environment));
    var envelope = HealthEnvelope(ready ? "ready" : "not_ready", checks, ready ? null : failure);
    return Results.Json(envelope, statusCode: ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
}

app.MapGet("/ready",        (Database db, ConfigValidationService cfg, FleetProductionReadinessService fleet, DataProtectionReadinessService dp, IWebHostEnvironment env, CancellationToken ct) => ReadinessAsync(db, cfg, fleet, dp, env, ct));
app.MapGet("/health/ready", (Database db, ConfigValidationService cfg, FleetProductionReadinessService fleet, DataProtectionReadinessService dp, IWebHostEnvironment env, CancellationToken ct) => ReadinessAsync(db, cfg, fleet, dp, env, ct));

// ── Diagnostics gate ─────────────────────────────────────────────────────────
// /health/deep and /metrics live OUTSIDE the /api session middleware (probes must
// stay unauthenticated), which historically left them fully public. Security
// review: /health/deep discloses the worker roster, migration state and RLS
// violation lists — an architecture map — and /metrics allows tenant-activity
// inference. Both now require either a valid session bearer (the SPA already
// sends one) or an X-Diagnostics-Key matching the DIAGNOSTICS_KEY env var (for
// monitoring agents and rehearsal scripts). /health, /health/live and
// /health/ready remain public for load-balancer probes.
async Task<bool> DiagnosticsAuthorizedAsync(HttpContext http, Database db, CancellationToken ct)
{
    var configuredKey = Environment.GetEnvironmentVariable("DIAGNOSTICS_KEY");
    var presentedKey = http.Request.Headers["X-Diagnostics-Key"].ToString();
    if (!string.IsNullOrWhiteSpace(configuredKey) && !string.IsNullOrWhiteSpace(presentedKey)
        && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(configuredKey), System.Text.Encoding.UTF8.GetBytes(presentedKey)))
        return true;

    var auth = http.Request.Headers.Authorization.ToString();
    if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
    var token = auth["Bearer ".Length..].Trim();
    if (string.IsNullOrWhiteSpace(token)) return false;

    const string sessionSql =
        @"SELECT s.user_id FROM user_sessions s
          JOIN users u ON u.id = s.user_id AND u.company_id = s.company_id
          WHERE s.session_token=@token AND s.expires_at > NOW() AND u.status='Active' LIMIT 1";
    var session = rlsEnforceTenantContext
        ? await db.QuerySingleInSystemScopeAsync(sessionSql, c => c.Parameters.AddWithValue("@token", token), ct)
        : await db.QuerySingleAsync(sessionSql, c => c.Parameters.AddWithValue("@token", token), ct);
    return session is not null;
}

// Prometheus scrape target — any external monitor (Grafana Agent, Datadog,
// UptimeRobot-with-metrics) can alert on 5xx rate / p95 / DB failures within 60s.
// Scrapers authenticate with the X-Diagnostics-Key header.
app.MapGet("/metrics", async (HttpContext http, Database db, Opstrax.Api.Observability.ApiMetricsService m, CancellationToken ct) =>
    await DiagnosticsAuthorizedAsync(http, db, ct)
        ? Results.Text(m.ToPrometheus(), "text/plain; version=0.0.4")
        : Results.Json(ApiResponse<object>.Fail("Unauthorized", "Metrics require an authenticated session or diagnostics key"),
            statusCode: StatusCodes.Status401Unauthorized));

app.MapGet("/health/deep", async (
    HttpContext http,
    Database db,
    ConfigValidationService configValidator,
    FleetProductionReadinessService fleetContract,
    DataProtectionReadinessService dataProtectionReadiness,
    IWebHostEnvironment environment,
    CancellationToken ct) =>
{
    if (!await DiagnosticsAuthorizedAsync(http, db, ct))
        return Results.Json(ApiResponse<object>.Fail("Unauthorized", "Deep diagnostics require an authenticated session or diagnostics key"),
            statusCode: StatusCodes.Status401Unauthorized);

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
    var servicesDegraded = false;
    if (dbOk)
    {
        var expectedWorkers = fleetContract.ExpectedCriticalWorkerNames.ToHashSet(StringComparer.Ordinal);
        var observedExpectedWorkers = new HashSet<string>(StringComparer.Ordinal);
        var startupGraceActive = fleetContract.CriticalWorkerStartupGraceActive;
        var staleBefore = DateTime.UtcNow - FleetProductionReadinessService.CriticalWorkerFreshness;
        if (Opstrax.Api.Observability.BuildInfo.StartedAtUtc > staleBefore)
            staleBefore = Opstrax.Api.Observability.BuildInfo.StartedAtUtc;
        var heartbeatLedgerReadable = true;
        try
        {
            // Health probes are outside the /api middleware branch, so no ambient
            // tenant/system scope exists here. Read the cross-tenant worker ledger
            // explicitly through the restricted control-plane identity.
            var heartbeatRows = await db.RunInSystemScopeAsync(() => db.QueryAsync(
                @"SELECT service_name, last_heartbeat_at, last_run_status, consecutive_failures
                  FROM service_heartbeats ORDER BY service_name", ct: ct), ct);

            foreach (var row in heartbeatRows)
            {
                var name    = row["serviceName"]?.ToString() ?? "";
                var lastBeat = row["lastHeartbeatAt"] as DateTime?;
                var consec   = row["consecutiveFailures"] is { } cf ? Convert.ToInt32(cf) : 0;
                var critical = expectedWorkers.Contains(name);
                if (critical) observedExpectedWorkers.Add(name);
                var stale = lastBeat is null || lastBeat.Value < staleBefore;
                var criticalViolation = critical &&
                    (stale || consec >= FleetProductionReadinessService.CriticalWorkerFailureThreshold(name));
                var svcStatus = criticalViolation && startupGraceActive ? "starting"
                    : criticalViolation ? "degraded"
                    : consec > 0 ? "warning"
                    : "healthy";
                if (svcStatus == "degraded") servicesDegraded = true;

                serviceStatuses.Add(new
                {
                    name,
                    status              = svcStatus,
                    expected_critical   = critical,
                    reason              = criticalViolation ? (stale ? "stale" : "repeated_failures") : null,
                    last_heartbeat_utc  = lastBeat?.ToString("o"),
                    consecutive_failures = consec,
                });
            }

        }
        catch { heartbeatLedgerReadable = false; }

        foreach (var missing in fleetContract.ExpectedCriticalWorkerNames
                     .Where(name => !observedExpectedWorkers.Contains(name)))
        {
            var status = startupGraceActive ? "starting" : "degraded";
            if (status == "degraded") servicesDegraded = true;
            serviceStatuses.Add(new
            {
                name = missing,
                status,
                expected_critical = true,
                reason = heartbeatLedgerReadable ? "missing" : "heartbeat_ledger_unavailable",
                last_heartbeat_utc = (string?)null,
                consecutive_failures = 0,
            });
        }

        checks["critical_worker_contract"] = new
        {
            status = servicesDegraded ? "invalid" : startupGraceActive ? "starting" : "healthy",
            expected_count = fleetContract.ExpectedCriticalWorkerNames.Count,
            observed_count = observedExpectedWorkers.Count,
            heartbeat_ledger_readable = heartbeatLedgerReadable,
            startup_grace_active = startupGraceActive,
            startup_grace_remaining_seconds = fleetContract.CriticalWorkerStartupGraceRemainingSeconds,
            freshness_seconds = (int)FleetProductionReadinessService.CriticalWorkerFreshness.TotalSeconds,
        };
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

    DataProtectionReadinessResult? dataProtectionResult = null;
    if (IsProtectedEnvironment(environment) && dbOk && cfgResult.FailCount == 0)
    {
        dataProtectionResult = await dataProtectionReadiness.CheckAsync(ct);
        checks["data_protection_key_ring"] = new
        {
            status = dataProtectionResult.Ready ? "ready" : "unavailable",
            key_count = dataProtectionResult.KeyCount,
            failure_code = dataProtectionResult.FailureCode,
        };
    }

    FleetProductionContractResult? fleetResult = null;
    if (IsProtectedEnvironment(environment) && dbOk && cfgResult.FailCount == 0)
    {
        fleetResult = await fleetContract.CheckAsync(ct);
        checks["fleet_production_contract"] = new
        {
            status = fleetResult.Ready ? "ready" : "invalid",
            role_restricted = fleetResult.RoleRestricted,
            missing_tables = fleetResult.MissingTables,
            rls_violations = fleetResult.RlsViolations,
            grant_violations = fleetResult.GrantViolations,
            tenant_coverage_violations = fleetResult.TenantCoverageViolations,
            tenant_grant_violations = fleetResult.TenantGrantViolations,
            default_privilege_violations = fleetResult.DefaultPrivilegeViolations,
            runtime_route_column_violations = fleetResult.RuntimeRouteColumnViolations,
            runtime_route_object_violations = fleetResult.RuntimeRouteObjectViolations,
            fleet_integrity_object_violations = fleetResult.FleetIntegrityObjectViolations,
            workforce_contract_violations = fleetResult.WorkforceContractViolations,
            migration_applied = fleetResult.MigrationApplied,
            runtime_support_migration_applied = fleetResult.RuntimeSupportMigrationApplied,
            tenant_coverage_migration_applied = fleetResult.TenantCoverageMigrationApplied,
            cold_chain_integrity_migration_applied = fleetResult.ColdChainIntegrityMigrationApplied,
            runtime_route_migration_applied = fleetResult.RuntimeRouteMigrationApplied,
            asset_type_integrity_migration_applied = fleetResult.AssetTypeIntegrityMigrationApplied,
            workforce_schedule_integrity_migration_applied = fleetResult.WorkforceScheduleIntegrityMigrationApplied,
            tenant_ticket_migration_applied = fleetResult.TenantTicketMigrationApplied,
            data_protection_key_ring_migration_applied = fleetResult.DataProtectionKeyRingMigrationApplied,
            market_catalog_ready = fleetResult.MarketCatalogReady,
            tenant_provisioning_ready = fleetResult.TenantProvisioningReady,
            indexes_ready = fleetResult.IndexesReady,
            critical_worker_violations = fleetResult.CriticalWorkerViolations,
            raw_critical_worker_violations = fleetResult.RawCriticalWorkerViolations,
            missing_critical_workers = fleetResult.MissingCriticalWorkers,
            stale_critical_workers = fleetResult.StaleCriticalWorkers,
            failed_critical_workers = fleetResult.FailedCriticalWorkers,
            critical_worker_startup_grace_active = fleetResult.CriticalWorkerStartupGraceActive,
            critical_worker_startup_grace_remaining_seconds = fleetResult.CriticalWorkerStartupGraceRemainingSeconds,
            failure_code = fleetResult.FailureCode
        };
    }

    // Determine overall status
    var overallStatus =
        !dbOk                                          ? "unhealthy" :
        dataProtectionResult is { Ready: false }       ? "unhealthy" :
        fleetResult is { Ready: false }                ? "unhealthy" :
        servicesDegraded
                                                       ? "degraded" :
        cfgResult.FailCount > 0                       ? "degraded" :
                                                         "healthy";

    var statusCode = overallStatus == "healthy" ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;

    var failureReason =
        !dbOk                    ? "database_unavailable" :
        dataProtectionResult is { Ready: false } ? "data_protection_key_ring_unavailable" :
        fleetResult is { Ready: false } ? "fleet_production_contract_invalid" :
        cfgResult.FailCount > 0  ? "critical_config_invalid" :
        servicesDegraded        ? "background_service_degraded" :
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
app.MapActiveShipmentsEndpoints();
app.MapRevenueEndpoints();
app.MapPlatformBillingEndpoints();
app.MapRevenueReadinessEndpoints();
app.MapRatingEndpoints();
app.MapSettlementEndpoints();
app.MapTaxEndpoints();
app.MapBillingEndpoints();
app.MapRevenueRecognitionEndpoints();
app.MapFinancialConfigEndpoints();
app.MapCustomerPortalEndpoints();
app.MapDevSeedEndpoints();
app.MapMarketPackEndpoints();
app.MapSafetyMaintenanceFoundationEndpoints();

app.Run();

// Maps an /api/* request path to the entitlement module_key that gates it.
// Returns null for paths that are not entitlement-gated (always allowed).
// Route-level feature-flag gates. This is only ONE way to consume a flag — any code
// path can call FeatureFlagService.IsEnabledAsync(...) directly, and the UI resolves
// its own via GET /api/feature-flags/evaluate.
//
// defaultOn:true means "no flag row → allowed". Use it for kill switches over EXISTING
// behaviour (a tenant with no row must not break). Use defaultOn:false for genuinely
// new features, which should be off until explicitly turned on.
static (string Flag, bool DefaultOn)? FlagGateForPath(string path)
{
    if (string.IsNullOrEmpty(path)) return null;
    // AI kill switch: lets an operator stop every AI call tenant-wide during an incident
    // (cost spike, bad output, provider outage) without a deploy.
    if (path.StartsWith("/api/ai", StringComparison.OrdinalIgnoreCase)) return ("ai_copilot", true);
    return null;
}

static string? ModuleKeyForPath(string path)
{
    if (string.IsNullOrEmpty(path)) return null;
    // The legacy generic module-record surface uses /api/modules/{ui-module-key}.
    // It must not bypass the same commercial envelope as the canonical endpoint:
    // e.g. /api/modules/traffic-violations is still Safety. Resolve only catalogued
    // keys; unknown buckets are not Platform-governed product modules.
    const string genericModulePrefix = "/api/modules/";
    if (path.StartsWith(genericModulePrefix, StringComparison.OrdinalIgnoreCase))
    {
        var remainder = path[genericModulePrefix.Length..];
        var separator = remainder.IndexOf('/');
        var moduleKey = separator < 0 ? remainder : remainder[..separator];
        var catalogEntry = PlatformTenantModuleCatalog.Modules.FirstOrDefault(
            module => string.Equals(module.Key, moduleKey, StringComparison.OrdinalIgnoreCase));
        if (catalogEntry?.RequiredEntitlement is { Length: > 0 } entitlement)
            return entitlement;
    }
    // Order matters: most specific prefixes first.
    // Every route surface a gated module actually owns. Previously this map was
    // incomplete, so disabling e.g. `dispatch` still left /api/jobs open and
    // disabling `crm` left /api/leads open — the toggle looked enforced but wasn't.
    // Keep the most specific prefixes first.
    (string Prefix, string Module)[] map =
    [
        // Safety
        ("/api/safety",              "safety"),
        ("/api/dashcam",             "safety"),
        ("/api/incidents",           "safety"),
        ("/api/coaching",            "safety"),
        ("/api/traffic-violations",  "safety"),
        ("/api/evidence-packages",   "safety"),

        // Driver portal safety surfaces. These live below the shared /api/driver
        // namespace, so they must be listed before any future broad driver gate.
        // Otherwise a Platform Admin can disable the tenant module while the
        // corresponding driver workflow remains callable.
        ("/api/driver/coaching",     "safety"),

        // Maintenance
        ("/api/preventive-maintenance", "maintenance"),
        ("/api/maintenance",         "maintenance"),
        ("/api/work-orders",         "maintenance"),
        ("/api/workorders",          "maintenance"),
        ("/api/service-history",     "maintenance"),
        ("/api/downtime",            "maintenance"),
        ("/api/dvir",                "maintenance"),
        ("/api/driver/dvir",         "maintenance"),

        // Dispatch
        // Driver portal assignment surface lives under the shared /api/driver
        // namespace, so it must be listed before any future broad driver gate.
        // Otherwise a Platform Admin can disable the tenant `dispatch` module
        // while the driver-app assignment workflow remains callable.
        ("/api/driver/assignments",  "dispatch"),
        ("/api/dispatch",            "dispatch"),
        ("/api/jobs",                "dispatch"),
        ("/api/trips",               "dispatch"),
        ("/api/routes",              "dispatch"),
        ("/api/smart-assign",        "dispatch"),
        ("/api/last-mile",           "dispatch"),
        ("/api/proof-of-delivery",   "dispatch"),
        // Legacy dedicated compatibility root retained for old clients. It must
        // remain inside the same commercial boundary as canonical route APIs.
        ("/api/route-planning",      "dispatch"),

        // Telematics
        ("/api/telemetry",           "telematics"),
        ("/api/devices",             "telematics"),
        ("/api/eld",                 "telematics"),
        ("/api/geofences",           "telematics"),

        // Tenant-owned third-party connectors can cause external side effects and
        // incur provider cost. Keep an independent Platform Admin entitlement rather
        // than coupling the full connector marketplace to telematics alone.
        ("/api/integrations",        "integrations"),

        // CRM  (customer-* prefixes below belong to the portal, not CRM)
        ("/api/customers",           "crm"),
        ("/api/contracts",           "crm"),
        ("/api/leads",               "crm"),
        ("/api/opportunities",       "crm"),
        ("/api/campaigns",           "crm"),
        ("/api/quotations",          "crm"),
        ("/api/rate-cards",          "crm"),
        // Compatibility aggregate for contracts/rates; direct calls must not
        // outlive a disabled CRM entitlement.
        ("/api/contracts-rates",     "crm"),

        // Customer portal
        ("/api/portal",              "customer_portal"),
        ("/api/customer-eta",        "customer_portal"),
        ("/api/customer-visibility", "customer_portal"),
        ("/api/customer-portal",     "customer_portal"),

        // Reports
        ("/api/reports",             "reports"),
        ("/api/analytics",           "reports"),
        ("/api/reports-analytics",   "reports"),

        // Compliance
        ("/api/fleet-compliance",    "compliance"),
        ("/api/compliance",          "compliance"),
        ("/api/hos",                 "compliance"),
        ("/api/driver/hos",          "compliance"),
        ("/api/hos-eld",             "compliance"),
    ];
    foreach (var (prefix, module) in map)
    {
        if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return module;
    }
    return null;
}

static async Task<bool> SupportAccessWithinAuditBudgetAsync(Database db, long grantId, CancellationToken ct)
{
    const string sql = """
        SELECT COUNT(*)
        FROM platform_audit_log
        WHERE entity_type='SupportAccessGrant' AND entity_id=@grantId
          AND action IN (
            'platform.impersonation.read_completed', 'platform.impersonation.read_denied',
            'platform.impersonation.read_failed', 'platform.impersonation.write_denied',
            'platform.impersonation.session_logout')
          AND created_at > NOW() - INTERVAL '1 minute'
        """;
    var count = await db.RunInSystemScopeAsync(() => db.ScalarLongAsync(
        sql, c => c.Parameters.AddWithValue("@grantId", grantId), ct), ct);
    return count < 120;
}

static async Task AuditSupportAccessRequestAsync(Database db, long companyId, long platformAdminId,
    long grantId, Guid grantRef, string method, string path, bool allowed, int responseStatus, CancellationToken ct)
{
    await db.RunInSystemTransactionAsync(async () =>
    {
        var selfLogout = allowed && HttpMethods.IsPost(method)
            && string.Equals(path, "/api/auth/logout", StringComparison.OrdinalIgnoreCase);
        var outcome = responseStatus < 400 ? "completed" : responseStatus is 401 or 403 ? "denied" : "failed";
        var platformAction = !allowed ? "platform.impersonation.write_denied"
            : selfLogout ? "platform.impersonation.session_logout" : $"platform.impersonation.read_{outcome}";
        var tenantAction = !allowed ? "platform.support_access.write_denied"
            : selfLogout ? "platform.support_access.session_logout" : $"platform.support_access.read_{outcome}";
        var details = JsonSerializer.Serialize(new { grantRef, mode = "read_only", method, path, responseStatus });
        await AuditLogSequenceRepair.ExecuteWithSequenceRepairAsync(
            db, "platform_audit_log", "id",
            @"INSERT INTO platform_audit_log
                (actor_admin_id, actor_role, action, entity_type, entity_id, target_company_id, details_json)
              VALUES (@adminId, 'support_access', @action, 'SupportAccessGrant', @grantId, @companyId, @details::jsonb)",
            c =>
            {
                c.Parameters.AddWithValue("@adminId", platformAdminId);
                c.Parameters.AddWithValue("@action", platformAction);
                c.Parameters.AddWithValue("@grantId", grantId);
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@details", details);
            }, ct);
        await AuditLogSequenceRepair.ExecuteWithSequenceRepairAsync(
            db, "audit_logs", "id",
            @"INSERT INTO audit_logs
                (company_id, actor_user_id, actor_name, action_name, entity_name, entity_id, details_json)
              VALUES (@companyId, NULL, @actor, @action, 'SupportAccessGrant', NULL, @details::jsonb)",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@actor", $"platform-support:{grantRef:N}");
                c.Parameters.AddWithValue("@action", tenantAction);
                c.Parameters.AddWithValue("@details", details);
            }, ct);
        return true;
    }, ct);
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

// Decide whether boot runs the retired *SchemaService DDL.
//
// Stage88 made migrations the only schema authority, so this answers "no" for every
// database the migration chain has touched. It is deliberately NOT inferred from the
// connected role any more: role inference is exactly what produced the split-brain
// (owner-capable dev boot built 1,006 columns that the restricted staging/production
// process could never build, so the deployed code queried columns the deployed
// database could not hold).
//
//   protected environment               -> false, ALWAYS. Owners apply the chain. This is
//                                          an absolute floor and is evaluated FIRST: a
//                                          `true` in SchemaInit:RunRuntimeDdl is refused
//                                          (and logged) rather than honoured, because a
//                                          config flag must not be able to re-enable
//                                          retired boot DDL in production. `false` there
//                                          is redundant but harmless.
//   SchemaInit:RunRuntimeDdl set        -> otherwise honour it, both directions.
//   stage88 ledger row present          -> false. Migrations own this database.
//   otherwise (chain never applied here) -> true + a loud warning naming the chain,
//                                          so a first boot against a genuinely empty
//                                          local database still bootstraps.
static async Task<bool> ResolveRuntimeSchemaDdlAsync(WebApplication app, IConfiguration configuration, Database db)
{
    const string Stage88 = "2026_08_22_stage88_runtime_schema_service_contract";
    var configured = configuration.GetValue<bool?>("SchemaInit:RunRuntimeDdl");

    // The protected-environment floor is checked BEFORE the config flag, not after it.
    // Evaluating the flag first made the header's "protected environment -> false, always"
    // untrue: anything that can set SchemaInit:RunRuntimeDdl=true (an env var, a stray
    // appsettings override, a copied Render blueprint) could re-enable the retired boot DDL
    // against production and rebuild the split-brain that stage88 exists to end. Refuse it
    // loudly instead of silently obeying.
    if (configured is true && IsProtectedEnvironment(app.Environment))
    {
        app.Logger.LogWarning(
            "SchemaInit:RunRuntimeDdl=true was REFUSED — {Environment} is a protected environment and boot DDL is " +
            "permanently disabled there. Schema is migration-owned; apply the migration chain with an owner role.",
            app.Environment.EnvironmentName);
    }
    if (IsProtectedEnvironment(app.Environment))
    {
        app.Logger.LogInformation("Boot schema DDL disabled — protected environment. Schema is migration-owned.");
        return false;
    }

    if (configured is not null)
    {
        app.Logger.LogInformation("Boot schema DDL is explicitly configured: SchemaInit:RunRuntimeDdl={Configured}.", configured);
        return configured.Value;
    }
    try
    {
        // Two steps on purpose: PostgreSQL parses the whole statement before it runs,
        // so a CASE guarding a SELECT on a missing relation still raises 42P01 — which
        // would send a genuinely empty database down the "cannot tell" branch.
        var ledgerExists = await db.ScalarLongAsync(
            "SELECT CASE WHEN to_regclass('public.schema_migrations') IS NULL THEN 0 ELSE 1 END");
        var ledgered = ledgerExists == 0
            ? 0
            : await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM schema_migrations WHERE version=@version",
                c => c.Parameters.AddWithValue("@version", Stage88));
        if (ledgered > 0)
        {
            app.Logger.LogInformation(
                "Boot schema DDL disabled — {Version} is ledgered, so migrations own this database.", Stage88);
            return false;
        }
        app.Logger.LogWarning(
            "This database has no {Version} ledger row, so the migration chain has never been applied to it. " +
            "Running the RETIRED boot-time schema services once so a first local boot is not left with an empty " +
            "database. Apply tools/apply-neon-predeploy-migrations.sh — it is the only schema authority and a " +
            "strict superset of what this path builds.", Stage88);
        return true;
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Could not read the migration ledger; leaving boot schema DDL disabled.");
        return false;
    }
}

// Fail-closed proof of the runtime database identity.
//
// This USED to be ShouldRunSchemaInitAsync: it decided whether boot ran the runtime
// *SchemaService DDL, and answering "no" for every restricted-role/RLS-enforced
// process is exactly what made staging and production structurally unable to hold
// the 1,006 columns those services declared. Stage88 moved every one of those
// declarations into database/migrations, so the DDL decision no longer exists —
// migrations are the only schema authority and boot never runs DDL.
//
// What survives is the half that was always load-bearing: a protected environment
// must be connected as the EXACT restricted opstrax_app identity with no role
// memberships and no CREATE rights, and startup is refused otherwise.
static async Task AssertRuntimeDatabaseIdentityAsync(WebApplication app, Database db)
{
    try
    {
        var row = await db.QuerySingleAsync(
            @"SELECT current_user AS role_name,
                     role.rolcanlogin AS can_login,
                     role.rolsuper AS is_super,
                     role.rolbypassrls AS bypass_rls,
                     role.rolcreatedb AS can_create_db,
                     role.rolcreaterole AS can_create_role,
                     role.rolinherit AS inherits_roles,
                     role.rolreplication AS can_replicate,
                     (SELECT COUNT(*) FROM pg_auth_members membership
                      WHERE membership.member=role.oid)::int AS membership_count,
                     has_database_privilege(current_user,current_database(),'CONNECT') AS db_connect,
                     has_database_privilege(current_user,current_database(),'CREATE') AS db_create,
                     has_database_privilege(current_user,current_database(),'TEMPORARY') AS db_temporary,
                     has_schema_privilege(current_user,'public','USAGE') AS schema_usage,
                     has_schema_privilege(current_user,'public','CREATE') AS schema_create
              FROM pg_roles role WHERE role.rolname=current_user");
        var roleName = row?["roleName"]?.ToString() ?? "unknown";
        var canLogin = row?["canLogin"] is bool l && l;
        var isSuper = row?["isSuper"] is bool s && s;
        var bypassRls = row?["bypassRls"] is bool b && b;
        var canCreateDb = row?["canCreateDb"] is bool d && d;
        var canCreateRole = row?["canCreateRole"] is bool c && c;
        var inheritsRoles = row?["inheritsRoles"] is bool i && i;
        var canReplicate = row?["canReplicate"] is bool r && r;
        var membershipCount = Convert.ToInt32(row?.GetValueOrDefault("membershipCount") ?? -1);
        var dbConnect = row?["dbConnect"] is bool dbc && dbc;
        var dbCreate = row?["dbCreate"] is bool dbcr && dbcr;
        var dbTemporary = row?["dbTemporary"] is bool dbt && dbt;
        var schemaUsage = row?["schemaUsage"] is bool su && su;
        var schemaCreate = row?["schemaCreate"] is bool sc && sc;
        var roleRestricted = canLogin && !isSuper && !bypassRls && !canCreateDb && !canCreateRole
                             && !inheritsRoles && !canReplicate && membershipCount == 0
                             && dbConnect && !dbCreate && !dbTemporary && schemaUsage && !schemaCreate;

        // The owner is either a superuser or has BYPASSRLS (the app role has neither).
        var looksLikeOwner = isSuper || bypassRls;
        var rlsEnforced = app.Configuration.GetValue<bool>("Rls:EnforceTenantContext");

        if (IsProtectedEnvironment(app.Environment) && rlsEnforced &&
            (!string.Equals(roleName, "opstrax_app", StringComparison.Ordinal) || !roleRestricted))
        {
            app.Logger.LogCritical(
                "Protected-environment startup refused: database role '{Role}' is not the required restricted opstrax_app identity " +
                "(login={Login}, super={Super}, bypassrls={Bypass}, createdb={CreateDb}, createrole={CreateRole}, inherit={Inherit}, replication={Replication}, memberships={Memberships}, " +
                "db_connect={DbConnect}, db_create={DbCreate}, db_temp={DbTemp}, schema_usage={SchemaUsage}, schema_create={SchemaCreate}). " +
                "Run owner migrations out-of-band, then connect the API as opstrax_app.",
                roleName, canLogin, isSuper, bypassRls, canCreateDb, canCreateRole, inheritsRoles, canReplicate, membershipCount,
                dbConnect, dbCreate, dbTemporary, schemaUsage, schemaCreate);
            throw new InvalidOperationException(
                "Protected-environment runtime database role must be exact restricted opstrax_app with no role memberships.");
        }

        app.Logger.LogInformation(
            "Runtime database identity proven — role '{Role}' (super={Super}, bypassrls={Bypass}, rlsEnforced={Rls}). " +
            "Schema is migration-owned; boot performs no DDL.",
            roleName, isSuper, bypassRls, rlsEnforced);
    }
    catch (Exception ex)
    {
        if (IsProtectedEnvironment(app.Environment) && app.Configuration.GetValue<bool>("Rls:EnforceTenantContext"))
        {
            app.Logger.LogCritical(ex,
                "Protected-environment startup refused: the restricted runtime database identity could not be proven.");
            throw new InvalidOperationException(
                "Protected-environment runtime database role must be provably restricted opstrax_app.", ex);
        }
        // Never block a non-protected startup on the check itself failing (e.g. a
        // restricted pg_roles view). No DDL depends on the answer any more.
        app.Logger.LogWarning(ex, "Runtime database identity check could not be evaluated; continuing.");
    }
}

static string SwaggerHtml() => """
<!doctype html>
<html lang="en">
<head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>OpsTrax API Swagger</title>
<style>body{margin:0;background:#f3f6fb;color:#0f172a;font-family:Inter,system-ui,sans-serif}.wrap{max-width:980px;margin:0 auto;padding:48px 24px}.card{border:1px solid #dbe5f0;background:#fff;border-radius:18px;padding:28px;box-shadow:0 1px 2px rgba(15,23,42,.04),0 16px 42px rgba(15,23,42,.08)}a{color:#1d4ed8;font-weight:700}code{background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;padding:3px 6px;color:#0f766e}</style></head>
<body><main class="wrap"><div class="card"><p style="color:#0f766e;font-weight:800;letter-spacing:.18em;text-transform:uppercase">OpsTrax Transport Management Solution</p><h1>OpsTrax API Swagger</h1><p>OpenAPI specification is available at <a href="/swagger/v1/swagger.json">/swagger/v1/swagger.json</a>.</p><p>Core endpoints include <code>/api/command-center/summary</code>, <code>/api/control-tower/summary</code>, <code>/api/vehicles</code>, <code>/api/drivers</code>, <code>/api/jobs</code>, <code>/api/dispatch/board</code>, and <code>/api/ai/ask</code>.</p></div></main></body></html>
""";
