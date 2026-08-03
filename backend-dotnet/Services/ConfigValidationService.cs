using Microsoft.Extensions.Configuration;
using Npgsql;

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
        var env = config["ASPNETCORE_ENVIRONMENT"] ?? config["DOTNET_ENVIRONMENT"] ?? config["Environment"] ?? "Unknown";
        var isProduction = string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase);
        var tenantRlsEnabled = config.GetValue<bool?>("Rls:EnforceTenantContext") == true;

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
        var explicitAppConn = FirstConfigured(
            config.GetConnectionString("DefaultConnection"),
            config["PG_CONNECTION_APP"],
            Environment.GetEnvironmentVariable("PG_CONNECTION_APP"));
        var legacyConn = FirstConfigured(config["PG_CONNECTION"], Environment.GetEnvironmentVariable("PG_CONNECTION"));
        var dbConn = isProduction && tenantRlsEnabled ? explicitAppConn : FirstConfigured(explicitAppConn, legacyConn);
        if (string.IsNullOrWhiteSpace(dbConn))
            issues.Add(new("database_application_connection", "fail",
                "Application database connection is not configured; set ConnectionStrings:DefaultConnection or PG_CONNECTION_APP"));
        else
            issues.Add(new("database_application_connection", "pass", "Application database connection is present"));

        var systemConn = FirstConfigured(
            config.GetConnectionString("SystemConnection"),
            config["PG_CONNECTION_SYSTEM"],
            Environment.GetEnvironmentVariable("PG_CONNECTION_SYSTEM"));
        if (string.IsNullOrWhiteSpace(systemConn))
            issues.Add(new("database_system_connection", isProduction && tenantRlsEnabled ? "fail" : "warn",
                "System database connection is not configured; set ConnectionStrings:SystemConnection or PG_CONNECTION_SYSTEM"));
        else
            issues.Add(new("database_system_connection", "pass", "System database connection is present"));

        if (!string.IsNullOrWhiteSpace(dbConn) && !string.IsNullOrWhiteSpace(systemConn))
        {
            var appUser = ConnectionUsername(dbConn);
            var systemUser = ConnectionUsername(systemConn);
            var appPassword = ConnectionPassword(dbConn);
            var systemPassword = ConnectionPassword(systemConn);
            if (isProduction && tenantRlsEnabled && !string.Equals(appUser, "opstrax_app", StringComparison.Ordinal))
                issues.Add(new("database_application_identity", "fail", "Application database connection must declare the exact opstrax_app identity"));
            else
                issues.Add(new("database_application_identity", "pass", "Application database identity is separately configured"));

            if (isProduction && tenantRlsEnabled && !string.Equals(systemUser, "opstrax_system", StringComparison.Ordinal))
                issues.Add(new("database_system_identity", "fail", "System database connection must declare the exact opstrax_system identity"));
            else
                issues.Add(new("database_system_identity", "pass", "System database identity is separately configured"));

            if (string.Equals(appUser, systemUser, StringComparison.Ordinal) ||
                string.Equals(dbConn.Trim(), systemConn.Trim(), StringComparison.Ordinal) ||
                (!string.IsNullOrEmpty(appPassword) &&
                 string.Equals(appPassword, systemPassword, StringComparison.Ordinal)))
                issues.Add(new("database_identity_separation", isProduction && tenantRlsEnabled ? "fail" : "warn",
                    "Application and system database connections alias an identity or credential"));
            else
                issues.Add(new("database_identity_separation", "pass", "Application and system database identities are distinct"));
        }

        var replicaConn = config.GetConnectionString("ReadReplica")
            ?? config["PG_CONNECTION_REPLICA"]
            ?? Environment.GetEnvironmentVariable("PG_CONNECTION_REPLICA");
        if (!string.IsNullOrWhiteSpace(replicaConn))
        {
            var replicaUser = ConnectionUsername(replicaConn);
            issues.Add(isProduction && tenantRlsEnabled &&
                       !string.Equals(replicaUser, "opstrax_app", StringComparison.Ordinal)
                ? new ConfigIssue("database_replica_identity", "fail",
                    "Read-replica connection must declare the exact opstrax_app identity")
                : new ConfigIssue("database_replica_identity", "pass",
                    "Read-replica database identity is restricted to opstrax_app"));
        }

        var ticketTtl = config.GetValue<int?>("Rls:TenantTicketTtlSeconds") ?? 120;
        issues.Add(ticketTtl is >= 5 and <= 300
            ? new ConfigIssue("tenant_ticket_ttl", "pass", "Tenant transaction ticket TTL is within the enforced 5–300 second range")
            : new ConfigIssue("tenant_ticket_ttl", isProduction && tenantRlsEnabled ? "fail" : "warn",
                "Rls:TenantTicketTtlSeconds must be between 5 and 300 seconds"));

        var dpCertificate = FirstConfigured(config["DataProtection:CertificateBase64"],
            config["DATA_PROTECTION_CERTIFICATE_BASE64"]);
        var dpPassword = FirstConfigured(config["DataProtection:CertificatePassword"],
            config["DATA_PROTECTION_CERTIFICATE_PASSWORD"]);
        if (string.IsNullOrWhiteSpace(dpCertificate) || string.IsNullOrWhiteSpace(dpPassword))
            issues.Add(new("data_protection_key_ring", isProduction ? "fail" : "warn",
                "Shared Data Protection certificate configuration is incomplete"));
        else if (dpPassword.Length < 16)
            issues.Add(new("data_protection_key_ring", isProduction ? "fail" : "warn",
                "Data Protection certificate password must be at least 16 characters"));
        else
            issues.Add(new("data_protection_key_ring", "pass",
                "Shared certificate-encrypted Data Protection key ring is configured"));

        var dpPreviousCertificate = FirstConfigured(config["DataProtection:PreviousCertificateBase64"],
            config["DATA_PROTECTION_PREVIOUS_CERTIFICATE_BASE64"]);
        var dpPreviousPassword = FirstConfigured(config["DataProtection:PreviousCertificatePassword"],
            config["DATA_PROTECTION_PREVIOUS_CERTIFICATE_PASSWORD"]);
        if (string.IsNullOrWhiteSpace(dpPreviousCertificate) != string.IsNullOrWhiteSpace(dpPreviousPassword))
            issues.Add(new("data_protection_certificate_rotation", isProduction ? "fail" : "warn",
                "Previous Data Protection certificate and password must be configured together"));
        else
            issues.Add(new("data_protection_certificate_rotation", "pass",
                string.IsNullOrWhiteSpace(dpPreviousCertificate)
                    ? "No previous Data Protection certificate is configured"
                    : "Previous Data Protection certificate is configured for rotation"));

        // Device credentials are unique per device and envelope-encrypted. The old global
        // Telemetry:DeviceSecret was never consumed by ingest, so validating its presence gave a
        // false sense of readiness. Validate the key that actually protects credential envelopes.
        var dataEncryptionKey = FirstConfigured(
            config["Pii:DataKey"], config["DATA_ENCRYPTION_KEY"],
            Environment.GetEnvironmentVariable("DATA_ENCRYPTION_KEY"));
        if (!IsBase64Key32(dataEncryptionKey))
            issues.Add(new("device_hmac_encryption", isProduction ? "fail" : "warn",
                "A valid 32-byte DATA_ENCRYPTION_KEY is required for encrypted per-device HMAC credentials"));
        else
            issues.Add(new("device_hmac_encryption", "pass",
                "Per-device HMAC envelope encryption is configured"));

        var allowLegacyDeviceSecrets = config.GetValue(DeviceHmacSecretProtection.LegacyReadSetting, false);
        if (allowLegacyDeviceSecrets)
            issues.Add(new("legacy_device_hmac_read", isProduction ? "fail" : "warn",
                "Legacy plaintext device-secret compatibility is enabled; rotate development devices and disable it"));
        else
            issues.Add(new("legacy_device_hmac_read", "pass", "Legacy plaintext device-secret reads are disabled"));

        // Per-gateway encrypted credentials in telemetry_gateways are mandatory. Presence of the
        // former fleet-wide secret is now a release blocker because a headerless fallback would be
        // a cross-tenant skeleton key.
        var legacyGatewaySecret = config["Telemetry:GatewaySecret"];
        if (!string.IsNullOrWhiteSpace(legacyGatewaySecret))
            issues.Add(new("legacy_telemetry_gateway_secret", isProduction ? "fail" : "warn",
                "Remove Telemetry:GatewaySecret; gateway ingest requires a tenant-bound X-Gateway-Id credential"));
        else
            issues.Add(new("legacy_telemetry_gateway_secret", "pass",
                "Legacy fleet-wide telemetry gateway secret is absent"));

        // SSE ticket key
        var sseKey = config["Telemetry:SseTicketKey"] ?? config["Sse:TicketKey"] ?? config["SseTicketKey"];
        if (string.IsNullOrWhiteSpace(sseKey))
            issues.Add(new("sse_ticket_key", "warn", "SSE stream ticket key not configured — telemetry SSE will be unavailable"));
        else
            issues.Add(new("sse_ticket_key", "pass", "SSE ticket key present"));

        // Environment mode
        if (string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase))
            issues.Add(new("environment_mode", "warn", $"Environment is '{env}' — ensure production settings override demo/dev values before going live"));
        else if (string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase))
            issues.Add(new("environment_mode", "pass", "Environment is Production"));
        else
            issues.Add(new("environment_mode", "warn", $"Environment is '{env}'"));

        // Platform superadmin bootstrap credential. PlatformSchemaService falls back to
        // a well-known demo password when the env var is unset — acceptable ONLY for
        // local/dev. In production the env var MUST be set and MUST NOT be the default.
        // Tenant RLS is a production invariant. Missing and explicit false are both
        // treated as disabled so a deployment cannot silently lose its DB backstop.
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

        var simulatorEnabled = config.GetValue("Telemetry:Simulator:Enabled", false);
        if (simulatorEnabled)
            issues.Add(new("telemetry_simulator", isProduction ? "fail" : "warn",
                "Telemetry simulator is enabled — production must use authenticated device/provider fixes only"));
        else
            issues.Add(new("telemetry_simulator", "pass", "Telemetry simulator is disabled"));

        // Retention is a compliance control, not an optional convenience in Production.
        // Require an explicit true value so a missing environment variable, typo, or
        // inherited false default cannot leave published policies unenforced.
        var retentionWorkerSetting = config["RetentionWorker:Enabled"];
        var retentionWorkerExplicitlyEnabled = bool.TryParse(retentionWorkerSetting, out var retentionWorkerEnabled)
                                               && retentionWorkerEnabled;
        if (isProduction && !retentionWorkerExplicitlyEnabled)
            issues.Add(new("retention_worker", "fail",
                "RetentionWorker:Enabled must be explicitly true in Production"));
        else if (retentionWorkerExplicitlyEnabled)
            issues.Add(new("retention_worker", "pass", "Retention enforcement worker is explicitly enabled"));
        else
            issues.Add(new("retention_worker", "warn",
                "Retention enforcement worker is not explicitly enabled; operational-row policies are not enforced"));

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

    private static string? ConnectionUsername(string connectionString)
    {
        try { return new NpgsqlConnectionStringBuilder(connectionString).Username; }
        catch { return null; }
    }

    private static string? FirstConfigured(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static bool IsBase64Key32(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { return Convert.FromBase64String(value.Trim()).Length == 32; }
        catch { return false; }
    }

    private static string? ConnectionPassword(string connectionString)
    {
        try { return new NpgsqlConnectionStringBuilder(connectionString).Password; }
        catch { return null; }
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
