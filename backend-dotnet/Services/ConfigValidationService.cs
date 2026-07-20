using Microsoft.Extensions.Configuration;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// ConfigValidationService — Singleton
//
// Validates runtime configuration at startup and on demand.
// NEVER exposes secret values in output — only presence, length, and strength.
// Returns ConfigCheckResult with a list of issues (pass/warn/fail per check).
// ─────────────────────────────────────────────────────────────────────────────

public sealed class ConfigValidationService(IConfiguration config)
{
    public ConfigCheckResult Validate()
    {
        var issues = new List<ConfigIssue>();

        // JWT signing key
        var jwtKey = config["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(jwtKey))
            issues.Add(new("jwt_key", "fail",   "JWT signing key is not configured"));
        else if (jwtKey.Length < 32)
            issues.Add(new("jwt_key", "fail",   $"JWT signing key is too short ({jwtKey.Length} chars; minimum 32)"));
        else if (jwtKey.Length < 64)
            issues.Add(new("jwt_key", "warn",   "JWT signing key is adequate but recommended length is ≥64 chars"));
        else
            issues.Add(new("jwt_key", "pass",   "JWT signing key present and meets minimum length"));

        // Database connection
        var dbConn = config.GetConnectionString("DefaultConnection")
            ?? config["Database:ConnectionString"]
            ?? config["PG_CONNECTION_APP"]
            ?? config["PG_CONNECTION"]
            ?? Environment.GetEnvironmentVariable("PG_CONNECTION_APP")
            ?? Environment.GetEnvironmentVariable("PG_CONNECTION");
        if (string.IsNullOrWhiteSpace(dbConn))
            issues.Add(new("database_connection", "fail", "Database connection string is not configured"));
        else
            issues.Add(new("database_connection", "pass", "Database connection string is present"));

        // Device HMAC secret (telemetry ingest)
        var deviceSecret = config["Telemetry:DeviceSecret"] ?? config["DeviceSecret"];
        if (string.IsNullOrWhiteSpace(deviceSecret))
            issues.Add(new("device_hmac_secret", "warn", "Telemetry device HMAC secret not configured — device auth will be degraded"));
        else if (deviceSecret.Length < 32)
            issues.Add(new("device_hmac_secret", "warn", $"Device HMAC secret is short ({deviceSecret.Length} chars; ≥32 recommended)"));
        else
            issues.Add(new("device_hmac_secret", "pass", "Device HMAC secret present"));

        // Vendor/field trackers terminate at a trusted protocol gateway. This separate
        // secret authenticates that gateway; an IMEI is an identifier, not a credential.
        var gatewaySecret = config["Telemetry:GatewaySecret"];
        var gatewayIsProduction = string.Equals(
            config["ASPNETCORE_ENVIRONMENT"] ?? config["DOTNET_ENVIRONMENT"] ?? config["Environment"],
            "Production", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(gatewaySecret))
            issues.Add(new("telemetry_gateway_secret", gatewayIsProduction ? "fail" : "warn",
                "Trusted telemetry gateway secret is not configured — hardware tracker forwarding is unavailable"));
        else if (gatewaySecret.Length < 32)
            issues.Add(new("telemetry_gateway_secret", gatewayIsProduction ? "fail" : "warn",
                "Trusted telemetry gateway secret is too short; minimum 32 characters"));
        else
            issues.Add(new("telemetry_gateway_secret", "pass", "Trusted telemetry gateway secret is present"));

        // SSE ticket key
        var sseKey = config["Telemetry:SseTicketKey"] ?? config["Sse:TicketKey"] ?? config["SseTicketKey"];
        if (string.IsNullOrWhiteSpace(sseKey))
            issues.Add(new("sse_ticket_key", "warn", "SSE stream ticket key not configured — telemetry SSE will be unavailable"));
        else
            issues.Add(new("sse_ticket_key", "pass", "SSE ticket key present"));

        // Environment mode
        var env = config["ASPNETCORE_ENVIRONMENT"] ?? config["DOTNET_ENVIRONMENT"] ?? config["Environment"] ?? "Unknown";
        if (string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase))
            issues.Add(new("environment_mode", "warn", $"Environment is '{env}' — ensure production settings override demo/dev values before going live"));
        else if (string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase))
            issues.Add(new("environment_mode", "pass", "Environment is Production"));
        else
            issues.Add(new("environment_mode", "warn", $"Environment is '{env}'"));

        // Platform superadmin bootstrap credential. PlatformSchemaService falls back to
        // a well-known demo password when the env var is unset — acceptable ONLY for
        // local/dev. In production the env var MUST be set and MUST NOT be the default.
        var isProduction = string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase);

        // Tenant RLS is a production invariant. Missing and explicit false are both
        // treated as disabled so a deployment cannot silently lose its DB backstop.
        var tenantRlsEnabled = config.GetValue<bool?>("Rls:EnforceTenantContext") == true;
        if (tenantRlsEnabled)
            issues.Add(new("tenant_rls_enforcement", "pass", "Tenant RLS context enforcement is enabled"));
        else
            issues.Add(new("tenant_rls_enforcement", isProduction ? "fail" : "warn",
                "Rls:EnforceTenantContext must be explicitly true before running in Production"));

        var platformPwd = Environment.GetEnvironmentVariable("PLATFORM_SUPERADMIN_PASSWORD") ?? config["Platform:SuperAdminPassword"];
        if (string.IsNullOrWhiteSpace(platformPwd))
            issues.Add(new("platform_superadmin_password", isProduction ? "fail" : "warn",
                "PLATFORM_SUPERADMIN_PASSWORD is not set — the bootstrap platform admin uses a well-known default password"));
        else if (string.Equals(platformPwd, "Platform@12345", StringComparison.Ordinal))
            issues.Add(new("platform_superadmin_password", isProduction ? "fail" : "warn",
                "PLATFORM_SUPERADMIN_PASSWORD is set to the well-known default — rotate it"));
        else if (platformPwd.Length < 12)
            issues.Add(new("platform_superadmin_password", "warn", $"Platform superadmin password is short ({platformPwd.Length} chars; ≥12 recommended)"));
        else
            issues.Add(new("platform_superadmin_password", "pass", "Platform superadmin password is configured (value redacted)"));

        // Demo / seed data guard
        var seedEnabled = config["DemoSeed:Enabled"]
            ?? config["Demo:SeedDataEnabled"]
            ?? config["SeedDataEnabled"];
        var fleetSeedEnabled = Environment.GetEnvironmentVariable("ENABLE_FLEET_DEMO_SEED")
            ?? config["Fleet:EnableDemoSeed"]
            ?? config["ENABLE_FLEET_DEMO_SEED"];
        if (string.Equals(seedEnabled, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fleetSeedEnabled, "true", StringComparison.OrdinalIgnoreCase))
            issues.Add(new("demo_seed_data", isProduction ? "fail" : "warn",
                "Demo seed data is enabled — disable DemoSeed:Enabled and Fleet:EnableDemoSeed in production"));
        else
            issues.Add(new("demo_seed_data", "pass", "Demo seed data flags are disabled"));

        // External email provider
        var smtpHost = config["Email:SmtpHost"] ?? config["Smtp:Host"];
        if (string.IsNullOrWhiteSpace(smtpHost))
            issues.Add(new("email_provider", "info", "External email provider (SMTP) not configured — scheduled report delivery will use in_app only"));
        else
            issues.Add(new("email_provider", "pass", $"SMTP host is configured (value redacted)"));

        // CORS origins
        var corsOrigins = config["Cors:AllowedOrigins"];
        if (string.IsNullOrWhiteSpace(corsOrigins) ||
            corsOrigins.Contains("*", StringComparison.Ordinal))
            issues.Add(new("cors_origins", "warn", "CORS wildcard (*) or empty — restrict to known frontend origins in production"));
        else
            issues.Add(new("cors_origins", "pass", "CORS origins are explicitly configured"));

        // Report scheduler toggle
        var schedulerEnabled = config["ReportScheduler:Enabled"];
        if (string.Equals(schedulerEnabled, "false", StringComparison.OrdinalIgnoreCase))
            issues.Add(new("report_scheduler", "warn", "Report scheduler explicitly disabled via config"));
        else
            issues.Add(new("report_scheduler", "pass", "Report scheduler is enabled (default)"));

        var failCount = issues.Count(i => i.Level == "fail");
        var warnCount = issues.Count(i => i.Level == "warn");
        var overallStatus = failCount > 0 ? "invalid" : warnCount > 0 ? "warnings" : "valid";

        return new ConfigCheckResult(overallStatus, failCount, warnCount, issues);
    }

    public static void EnsureStartupAllowed(ConfigCheckResult result, bool isProduction)
    {
        if (isProduction && result.FailCount > 0)
            throw new InvalidOperationException(
                $"Refusing to start with {result.FailCount} critical configuration failure(s). See logs (values redacted).");
    }
}

public sealed record ConfigCheckResult(
    string            Status,      // "valid" | "warnings" | "invalid"
    int               FailCount,
    int               WarnCount,
    List<ConfigIssue> Issues);

// Level: "pass" | "warn" | "fail" | "info"
public sealed record ConfigIssue(string Check, string Level, string Message);
