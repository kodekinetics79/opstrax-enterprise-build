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
        var dbConn = config.GetConnectionString("DefaultConnection") ?? config["Database:ConnectionString"];
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

        // SSE ticket key
        var sseKey = config["Telemetry:SseTicketKey"] ?? config["SseTicketKey"];
        if (string.IsNullOrWhiteSpace(sseKey))
            issues.Add(new("sse_ticket_key", "warn", "SSE stream ticket key not configured — telemetry SSE will be unavailable"));
        else
            issues.Add(new("sse_ticket_key", "pass", "SSE ticket key present"));

        // Environment mode
        var env = config["ASPNETCORE_ENVIRONMENT"] ?? config["Environment"] ?? "Unknown";
        if (string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase))
            issues.Add(new("environment_mode", "warn", $"Environment is '{env}' — ensure production settings override demo/dev values before going live"));
        else if (string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase))
            issues.Add(new("environment_mode", "pass", "Environment is Production"));
        else
            issues.Add(new("environment_mode", "warn", $"Environment is '{env}'"));

        // Demo / seed data guard
        var seedEnabled = config["Demo:SeedDataEnabled"] ?? config["SeedDataEnabled"];
        if (string.Equals(seedEnabled, "true", StringComparison.OrdinalIgnoreCase))
            issues.Add(new("demo_seed_data", "warn", "Demo seed data is enabled — disable in production"));
        else
            issues.Add(new("demo_seed_data", "pass", "Demo seed data flag is not 'true'"));

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
}

public sealed record ConfigCheckResult(
    string            Status,      // "valid" | "warnings" | "invalid"
    int               FailCount,
    int               WarnCount,
    List<ConfigIssue> Issues);

// Level: "pass" | "warn" | "fail" | "info"
public sealed record ConfigIssue(string Check, string Level, string Message);
