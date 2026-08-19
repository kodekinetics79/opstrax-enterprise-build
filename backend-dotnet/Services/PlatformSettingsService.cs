using Opstrax.Api.Data;
using Opstrax.Api.Security;

namespace Opstrax.Api.Services;

/// <summary>
/// Durable, operator-editable platform configuration — the settings a platform admin must be
/// able to change from the console without a redeploy.
///
/// WHY THIS EXISTS: outbound email was configured exclusively through <c>SMTP_*</c> environment
/// variables. That made mail delivery a deploy-time concern on a product whose onboarding flow
/// depends on it: a platform admin inviting a tenant administrator had no way to switch mail on,
/// so every invite silently fell back to "no email sent" and the invited user stayed Pending.
/// Settings written here take effect on the next send — no restart, no redeploy.
///
/// Storage notes:
///  • <c>platform_settings</c> is a small key/value table in the control plane. Values marked
///    secret are encrypted at rest with <see cref="PiiProtectionService"/> (the same AES-GCM
///    envelope used for tenant PII) and are never returned to a client in plaintext.
///  • Environment variables remain the FALLBACK for every key, so an existing deployment that
///    configures SMTP through Render keeps working untouched, and a database that has never
///    been written to behaves exactly as before.
///  • Reads are uncached on purpose. These are low-frequency (one lookup per outbound mail),
///    and caching would reintroduce the "changed the setting, still sending with the old one"
///    confusion this service exists to remove.
/// </summary>
public sealed class PlatformSettingsService(Database db, PiiProtectionService pii)
{
    /// <summary>
    /// Creates the settings table if absent. DML-safe to call repeatedly; invoked from the
    /// platform schema step alongside the other control-plane tables.
    /// </summary>
    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS platform_settings (
                setting_key   VARCHAR(120)  NOT NULL PRIMARY KEY,
                setting_value TEXT          NULL,
                is_secret     BOOLEAN       NOT NULL DEFAULT false,
                updated_by    VARCHAR(220)  NULL,
                updated_at    TIMESTAMPTZ   NOT NULL DEFAULT NOW()
            )
            """, ct: ct);
    }

    /// <summary>
    /// The stored value for a key, decrypted when it is a secret, falling back to
    /// <paramref name="envVar"/> when nothing is stored. Returns null when neither is set.
    /// </summary>
    public async Task<string?> GetAsync(string key, string? envVar = null, CancellationToken ct = default)
    {
        string? stored = null;
        try
        {
            var row = await db.QuerySingleAsync(
                "SELECT setting_value, is_secret FROM platform_settings WHERE setting_key=@k",
                c => c.Parameters.AddWithValue("@k", key), ct);
            if (row is not null)
            {
                var raw = row["settingValue"]?.ToString();
                stored = row["isSecret"] is true ? pii.Decrypt(raw) : raw;
            }
        }
        catch
        {
            // A missing table (schema step skipped under a restricted role) must degrade to the
            // environment rather than break the caller — mail config is not worth a 500.
        }

        if (!string.IsNullOrWhiteSpace(stored)) return stored;
        var fromEnv = envVar is null ? null : Environment.GetEnvironmentVariable(envVar);
        return string.IsNullOrWhiteSpace(fromEnv) ? null : fromEnv;
    }

    /// <summary>True when the key has a value stored in the database (as opposed to env).</summary>
    public async Task<bool> HasStoredAsync(string key, CancellationToken ct = default)
    {
        try
        {
            return await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM platform_settings WHERE setting_key=@k AND COALESCE(setting_value,'') <> ''",
                c => c.Parameters.AddWithValue("@k", key), ct) > 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// Whether stored secrets can actually be encrypted. <see cref="PiiProtectionService"/> passes
    /// plaintext through when no data key is configured (a deliberate dev convenience), which is
    /// acceptable for a value that is merely sensitive but NOT for a live credential — so callers
    /// check this before offering to store one.
    /// </summary>
    public bool EncryptionAvailable => pii.Enabled;

    /// <summary>
    /// Upserts a setting. A null or empty <paramref name="value"/> DELETES the row, which is how
    /// an operator reverts a key back to its environment default. Secrets are encrypted at rest.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A secret was supplied while no encryption key is configured. Fail closed rather than write
    /// a credential to the database in the clear — the same posture the telemetry gateway secrets
    /// take. The environment fallback (e.g. SMTP_PASS) remains available in that case.
    /// </exception>
    public async Task SetAsync(string key, string? value, bool isSecret, string? updatedBy, CancellationToken ct = default)
    {
        if (isSecret && !string.IsNullOrWhiteSpace(value) && !EncryptionAvailable)
            throw new InvalidOperationException(
                $"Refusing to store '{key}' unencrypted: no PII data key is configured.");

        if (string.IsNullOrWhiteSpace(value))
        {
            await db.ExecuteAsync("DELETE FROM platform_settings WHERE setting_key=@k",
                c => c.Parameters.AddWithValue("@k", key), ct);
            return;
        }

        var toStore = isSecret ? pii.Encrypt(value) : value;
        await db.ExecuteAsync(
            @"INSERT INTO platform_settings (setting_key, setting_value, is_secret, updated_by, updated_at)
              VALUES (@k, @v, @s, @by, NOW())
              ON CONFLICT (setting_key) DO UPDATE
                SET setting_value = EXCLUDED.setting_value,
                    is_secret     = EXCLUDED.is_secret,
                    updated_by    = EXCLUDED.updated_by,
                    updated_at    = NOW()",
            c =>
            {
                c.Parameters.AddWithValue("@k", key);
                c.Parameters.AddWithValue("@v", (object?)toStore ?? DBNull.Value);
                c.Parameters.AddWithValue("@s", isSecret);
                c.Parameters.AddWithValue("@by", (object?)updatedBy ?? DBNull.Value);
            }, ct);
    }

    // ── Public application URLs ─────────────────────────────────────────────────
    // Invite emails and the copyable activation links both need to know where the apps
    // live. These were env-only too, which meant an unset FRONTEND_PUBLIC_URL produced an
    // invite that could neither be emailed NOR handed over — the same dead end as unset SMTP.

    public const string TenantAppUrlKey = "app.tenant_url";
    public const string PlatformAppUrlKey = "app.platform_url";

    /// <summary>Base URL of the tenant-facing SPA, where an invited admin sets their password.</summary>
    public async Task<string?> GetTenantAppUrlAsync(CancellationToken ct = default)
        => Trim(await GetAsync(TenantAppUrlKey, "FRONTEND_PUBLIC_URL", ct)
                ?? Environment.GetEnvironmentVariable("PUBLIC_APP_URL"));

    /// <summary>Base URL of the Platform Admin SPA, where an invited operator accepts.</summary>
    public async Task<string?> GetPlatformAppUrlAsync(CancellationToken ct = default)
        => Trim(await GetAsync(PlatformAppUrlKey, "PLATFORM_PUBLIC_URL", ct));

    private static string? Trim(string? url)
    {
        var trimmed = url?.Trim().TrimEnd('/');
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    /// <summary>When the key was last written in-app, for "who changed this" in the console.</summary>
    public async Task<(string? By, DateTimeOffset? At)> LastUpdatedAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var row = await db.QuerySingleAsync(
                "SELECT updated_by, updated_at FROM platform_settings WHERE setting_key=@k",
                c => c.Parameters.AddWithValue("@k", key), ct);
            if (row is null) return (null, null);
            var at = row["updatedAt"] is DateTime dt ? new DateTimeOffset(dt, TimeSpan.Zero) : (DateTimeOffset?)null;
            return (row["updatedBy"]?.ToString(), at);
        }
        catch { return (null, null); }
    }
}
