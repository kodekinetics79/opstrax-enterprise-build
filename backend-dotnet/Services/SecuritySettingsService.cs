using System.Text.Json;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// SecuritySettingsService — Scoped
//
// Manages tenant-level security policy settings.
// All writes are audit-logged via AuditService.
// Never exposes internal config values outside tenant scope.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class SecuritySettingsService(Database db, AuditService audit)
{
    // Default policy returned when no settings row exists yet
    public static SecuritySettings Defaults(long companyId) => new()
    {
        CompanyId                     = companyId,
        MfaRequired                   = false,
        MfaRequiredRoles              = [],
        PasswordMinLength             = 8,
        PasswordRequiresUppercase     = false,
        PasswordRequiresNumber        = false,
        PasswordRequiresSymbol        = false,
        PasswordExpiryDays            = 0,
        SessionIdleTimeoutMinutes     = 60,
        SessionAbsoluteTimeoutMinutes = 480,
        MaxFailedLoginAttempts        = 5,
        LockoutDurationMinutes        = 30,
        AllowedSsoProviders           = [],
        ExportApprovalRequired        = false,
        AuditRetentionDays            = 90,
        DataRetentionDays             = 365,
    };

    public async Task<SecuritySettings> GetAsync(long companyId, CancellationToken ct = default)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT * FROM company_security_settings WHERE company_id = @cid LIMIT 1",
            c => c.Parameters.AddWithValue("@cid", companyId), ct);

        if (row is null) return Defaults(companyId);

        return new SecuritySettings
        {
            CompanyId                     = companyId,
            MfaRequired                   = Convert.ToBoolean(row.GetValueOrDefault("mfaRequired")),
            MfaRequiredRoles              = ParseJsonArray(row.GetValueOrDefault("mfaRequiredRoles")),
            PasswordMinLength             = Convert.ToInt32(row.GetValueOrDefault("passwordMinLength") ?? 8),
            PasswordRequiresUppercase     = Convert.ToBoolean(row.GetValueOrDefault("passwordRequiresUppercase")),
            PasswordRequiresNumber        = Convert.ToBoolean(row.GetValueOrDefault("passwordRequiresNumber")),
            PasswordRequiresSymbol        = Convert.ToBoolean(row.GetValueOrDefault("passwordRequiresSymbol")),
            PasswordExpiryDays            = Convert.ToInt32(row.GetValueOrDefault("passwordExpiryDays") ?? 0),
            SessionIdleTimeoutMinutes     = Convert.ToInt32(row.GetValueOrDefault("sessionIdleTimeoutMinutes") ?? 60),
            SessionAbsoluteTimeoutMinutes = Convert.ToInt32(row.GetValueOrDefault("sessionAbsoluteTimeoutMinutes") ?? 480),
            MaxFailedLoginAttempts        = Convert.ToInt32(row.GetValueOrDefault("maxFailedLoginAttempts") ?? 5),
            LockoutDurationMinutes        = Convert.ToInt32(row.GetValueOrDefault("lockoutDurationMinutes") ?? 30),
            AllowedSsoProviders           = ParseJsonArray(row.GetValueOrDefault("allowedSsoProviders")),
            ExportApprovalRequired        = Convert.ToBoolean(row.GetValueOrDefault("exportApprovalRequired")),
            AuditRetentionDays            = Convert.ToInt32(row.GetValueOrDefault("auditRetentionDays") ?? 90),
            DataRetentionDays             = Convert.ToInt32(row.GetValueOrDefault("dataRetentionDays") ?? 365),
            UpdatedAt                     = row.GetValueOrDefault("updatedAt") is DateTime dt ? dt : null,
            UpdatedBy                     = row.GetValueOrDefault("updatedBy")?.ToString(),
        };
    }

    public async Task UpsertAsync(
        long companyId,
        SecuritySettings settings,
        Microsoft.AspNetCore.Http.HttpContext http,
        CancellationToken ct = default)
    {
        var updatedBy = http.Items.TryGetValue("opstrax.auth.user_id", out var uid)
            ? $"user:{uid}"
            : "system";

        await db.ExecuteAsync(
            @"INSERT INTO company_security_settings
                (company_id, mfa_required, mfa_required_roles, password_min_length,
                 password_requires_uppercase, password_requires_number, password_requires_symbol,
                 password_expiry_days, session_idle_timeout_minutes, session_absolute_timeout_minutes,
                 max_failed_login_attempts, lockout_duration_minutes, allowed_sso_providers,
                 export_approval_required, audit_retention_days, data_retention_days,
                 created_at, updated_at, updated_by)
              VALUES
                (@cid, @mfa, @mfaRoles, @minLen,
                 @upper, @num, @sym,
                 @expiry, @idle, @absolute,
                 @maxFail, @lockout, @sso,
                 @exportApproval, @auditDays, @dataDays,
                 NOW(), NOW(), @updatedBy)
              ON DUPLICATE KEY UPDATE
                mfa_required                   = VALUES(mfa_required),
                mfa_required_roles             = VALUES(mfa_required_roles),
                password_min_length            = VALUES(password_min_length),
                password_requires_uppercase    = VALUES(password_requires_uppercase),
                password_requires_number       = VALUES(password_requires_number),
                password_requires_symbol       = VALUES(password_requires_symbol),
                password_expiry_days           = VALUES(password_expiry_days),
                session_idle_timeout_minutes   = VALUES(session_idle_timeout_minutes),
                session_absolute_timeout_minutes = VALUES(session_absolute_timeout_minutes),
                max_failed_login_attempts      = VALUES(max_failed_login_attempts),
                lockout_duration_minutes       = VALUES(lockout_duration_minutes),
                allowed_sso_providers          = VALUES(allowed_sso_providers),
                export_approval_required       = VALUES(export_approval_required),
                audit_retention_days           = VALUES(audit_retention_days),
                data_retention_days            = VALUES(data_retention_days),
                updated_at                     = NOW(),
                updated_by                     = VALUES(updated_by)",
            c =>
            {
                c.Parameters.AddWithValue("@cid",            companyId);
                c.Parameters.AddWithValue("@mfa",            settings.MfaRequired ? 1 : 0);
                c.Parameters.AddWithValue("@mfaRoles",       JsonSerializer.Serialize(settings.MfaRequiredRoles));
                c.Parameters.AddWithValue("@minLen",         Math.Max(6, settings.PasswordMinLength));
                c.Parameters.AddWithValue("@upper",          settings.PasswordRequiresUppercase ? 1 : 0);
                c.Parameters.AddWithValue("@num",            settings.PasswordRequiresNumber ? 1 : 0);
                c.Parameters.AddWithValue("@sym",            settings.PasswordRequiresSymbol ? 1 : 0);
                c.Parameters.AddWithValue("@expiry",         settings.PasswordExpiryDays);
                c.Parameters.AddWithValue("@idle",           Math.Max(5, settings.SessionIdleTimeoutMinutes));
                c.Parameters.AddWithValue("@absolute",       Math.Max(30, settings.SessionAbsoluteTimeoutMinutes));
                c.Parameters.AddWithValue("@maxFail",        Math.Max(1, settings.MaxFailedLoginAttempts));
                c.Parameters.AddWithValue("@lockout",        Math.Max(1, settings.LockoutDurationMinutes));
                c.Parameters.AddWithValue("@sso",            JsonSerializer.Serialize(settings.AllowedSsoProviders));
                c.Parameters.AddWithValue("@exportApproval", settings.ExportApprovalRequired ? 1 : 0);
                c.Parameters.AddWithValue("@auditDays",      Math.Max(30, settings.AuditRetentionDays));
                c.Parameters.AddWithValue("@dataDays",       Math.Max(30, settings.DataRetentionDays));
                c.Parameters.AddWithValue("@updatedBy",      updatedBy);
            }, ct);

        await audit.LogAsync(http, "security.settings.updated", "company_security_settings",
            companyId, null, ct);
    }

    // Pure static validation — no DB required
    public static (bool valid, string[] failures) ValidatePassword(string password, SecuritySettings settings)
    {
        var failures = new List<string>();

        if (password.Length < settings.PasswordMinLength)
            failures.Add($"Password must be at least {settings.PasswordMinLength} characters");

        if (settings.PasswordRequiresUppercase && !password.Any(char.IsUpper))
            failures.Add("Password must contain at least one uppercase letter");

        if (settings.PasswordRequiresNumber && !password.Any(char.IsDigit))
            failures.Add("Password must contain at least one number");

        if (settings.PasswordRequiresSymbol && !password.Any(c => !char.IsLetterOrDigit(c)))
            failures.Add("Password must contain at least one special character");

        return (failures.Count == 0, [.. failures]);
    }

    private static string[] ParseJsonArray(object? value)
    {
        if (value is null) return [];
        var raw = value.ToString();
        if (string.IsNullOrWhiteSpace(raw) || raw == "null") return [];
        try { return JsonSerializer.Deserialize<string[]>(raw) ?? []; }
        catch { return []; }
    }
}

// ─────────────────────────────────────────────────────────────────────────────

public sealed class SecuritySettings
{
    public long CompanyId { get; init; }
    public bool MfaRequired { get; init; }
    public string[] MfaRequiredRoles { get; init; } = [];
    public int PasswordMinLength { get; init; } = 8;
    public bool PasswordRequiresUppercase { get; init; }
    public bool PasswordRequiresNumber { get; init; }
    public bool PasswordRequiresSymbol { get; init; }
    public int PasswordExpiryDays { get; init; }
    public int SessionIdleTimeoutMinutes { get; init; } = 60;
    public int SessionAbsoluteTimeoutMinutes { get; init; } = 480;
    public int MaxFailedLoginAttempts { get; init; } = 5;
    public int LockoutDurationMinutes { get; init; } = 30;
    public string[] AllowedSsoProviders { get; init; } = [];
    public bool ExportApprovalRequired { get; init; }
    public int AuditRetentionDays { get; init; } = 90;
    public int DataRetentionDays { get; init; } = 365;
    public DateTime? UpdatedAt { get; init; }
    public string? UpdatedBy { get; init; }
}
