using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// BackupVerificationService — Scoped
//
// Records backup verification results. Does NOT fake success.
//
// Status meanings:
//   not_configured — no backup integration has been set up yet
//   passed         — backup completed and restore was verified
//   warning        — backup completed but restore was NOT tested
//   failed         — backup process reported failure
//
// Evidence hash: SHA-256 of the verification record content at insert time.
// storage_location_label stores a human-readable label (e.g. "AWS S3 eu-west-1")
// NOT the actual bucket path or credentials.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class BackupVerificationService(Database db, AuditService audit)
{
    public Task<List<Dictionary<string, object?>>> GetForTenantAsync(
        long? companyId, int limit = 50, CancellationToken ct = default)
    {
        if (companyId.HasValue)
        {
            return db.QueryAsync(
                @"SELECT id, company_id, backup_type, status, verified_at, restore_tested,
                         duration_ms, storage_location_label, safe_error, evidence_hash
                  FROM backup_verifications
                  WHERE company_id = @cid OR company_id IS NULL
                  ORDER BY verified_at DESC
                  LIMIT @lim",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId.Value);
                    c.Parameters.AddWithValue("@lim", limit);
                }, ct);
        }

        return db.QueryAsync(
            @"SELECT id, company_id, backup_type, status, verified_at, restore_tested,
                     duration_ms, storage_location_label, safe_error, evidence_hash
              FROM backup_verifications
              ORDER BY verified_at DESC
              LIMIT @lim",
            c => c.Parameters.AddWithValue("@lim", limit), ct);
    }

    public async Task<long> RecordAsync(
        long? companyId,
        string backupType,
        string status,
        bool restoreTested,
        int? durationMs,
        string? storageLocationLabel,
        string? safeError,
        Microsoft.AspNetCore.Http.HttpContext http,
        CancellationToken ct = default)
    {
        if (!new[] { "passed", "failed", "warning", "not_configured" }.Contains(status))
            throw new ArgumentException($"Invalid status '{status}'");

        // Evidence hash proves the record was created at this time with this content
        var hash = ComputeHash(backupType, status, restoreTested, durationMs, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm"));

        var id = await db.InsertAsync(
            @"INSERT INTO backup_verifications
                (company_id, backup_type, status, verified_at, restore_tested,
                 duration_ms, storage_location_label, safe_error, evidence_hash)
              VALUES
                (@cid, @type, @status, NOW(), @restore,
                 @dur, @loc, @err, @hash)",
            c =>
            {
                c.Parameters.AddWithValue("@cid",     (object?)companyId ?? DBNull.Value);
                c.Parameters.AddWithValue("@type",    backupType);
                c.Parameters.AddWithValue("@status",  status);
                c.Parameters.AddWithValue("@restore", restoreTested ? 1 : 0);
                c.Parameters.AddWithValue("@dur",     (object?)durationMs ?? DBNull.Value);
                c.Parameters.AddWithValue("@loc",     (object?)storageLocationLabel ?? DBNull.Value);
                c.Parameters.AddWithValue("@err",     (object?)safeError  ?? DBNull.Value);
                c.Parameters.AddWithValue("@hash",    hash);
            }, ct);

        await audit.LogAsync(http, $"backup.verification.{status}", "backup_verifications", id,
            JsonSerializer.Serialize(new { backupType, status, restoreTested }), ct);

        return id;
    }

    internal static string ComputeHash(string backupType, string status, bool restoreTested, int? durationMs, string timestamp)
    {
        var data = $"{backupType}:{status}:{restoreTested}:{durationMs}:{timestamp}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
