using System.Collections.Concurrent;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// Durable, cross-instance replay defense for POST /api/telemetry/gps-ingest (TEL-P1-REPLAY-005).
//
// The gps-ingest forward is authenticated by HMAC-SHA256 over "<timestamp>.<rawPayload>" under
// the shared Telemetry:GatewaySecret. That signature is a per-message identity: the SAME signed
// request replayed produces the SAME signature bytes. This guard makes "have I already accepted
// this exact signed message?" a durable, atomic question so a captured valid packet cannot be
// re-accepted after a restart, on another instance, on retry, or under concurrent submission.
//
// CANONICAL IDENTITY: callers must pass the LOWERCASE HEX of the verified HMAC BYTES as the nonce
// (Convert.ToHexString(bytes).ToLowerInvariant()), NOT the raw request header. HMAC verification
// decodes hex case-insensitively and compares bytes, so "AB..." and "ab..." authenticate as the
// same message; keying the durable row on the canonical bytes closes the case-variance replay
// bypass a raw-string key would leave open.
//
// Durable path: atomic INSERT ... ON CONFLICT (gateway_id, signature) DO NOTHING. rows==1 -> first
// time (accept); rows==0 -> already seen (replay). Same DB-UNIQUE idiom as telemetry_nonces.
//
// Deploy-safety with a HARD fail-closed default: production runs as the restricted opstrax_app
// role and SKIPS startup schema init, so the table may be absent until the owner applies the
// migration. The presence probe distinguishes THREE states, and only a DEFINITIVE absent falls
// back — a probe ERROR fails closed so a DB blip during a cold cache can never silently disable
// durable protection:
//   - Present    : durable INSERT (accept/duplicate); insert throwing -> caller tx aborts (fail closed)
//   - Absent     : table genuinely does not exist (COUNT==0) -> in-memory fallback (pre-migration)
//   - ProbeError : probe threw (transient) -> FailClosed, never fall back
public static class GpsGatewayReplayGuard
{
    public enum Availability { Present, Absent, ProbeError }

    /// <summary>Single global gateway scope until a per-gateway credential identity exists.</summary>
    public const string DefaultGatewayId = "default";

    // Definitive presence is cached once known; a transient probe error is NOT cached (re-probed).
    private static volatile bool _known;
    private static volatile bool _present;
    private static readonly SemaphoreSlim _probeLock = new(1, 1);

    // Fallback replay set, used ONLY when the durable table is DEFINITIVELY absent (pre-migration).
    // Same 300s window + lazy prune as the auth freshness gate, so it stays bounded.
    private static readonly ConcurrentDictionary<string, long> Fallback = new();
    private const long WindowSeconds = 300;

    /// <summary>
    /// Determine whether the durable table exists. Present/Absent are cached; a transient probe
    /// error returns <see cref="Availability.ProbeError"/> and is NOT cached (next call re-probes).
    /// </summary>
    public static async Task<Availability> DetermineAvailabilityAsync(Database db, CancellationToken ct = default)
    {
        if (_known) return _present ? Availability.Present : Availability.Absent;
        await _probeLock.WaitAsync(ct);
        try
        {
            if (_known) return _present ? Availability.Present : Availability.Absent;
            long n;
            try
            {
                n = await db.ScalarLongAsync(
                    @"SELECT COUNT(*) FROM information_schema.tables
                      WHERE table_schema = current_schema() AND table_name = 'gps_gateway_replay'");
            }
            catch
            {
                // Transient failure — do NOT freeze a negative; fail closed for THIS call.
                return Availability.ProbeError;
            }
            _present = n > 0;
            _known = true;
            return _present ? Availability.Present : Availability.Absent;
        }
        finally
        {
            _probeLock.Release();
        }
    }

    /// <summary>
    /// Durable reservation — call INSIDE the same transaction/scope as the ingest writes so a write
    /// failure rolls the reservation back too (no burned nonce). Returns true when this signature was
    /// newly reserved (accept), false when it was already present (replay). Throws on DB error, which
    /// aborts the caller's transaction and fails the request closed.
    /// </summary>
    public static async Task<bool> TryReserveDurableAsync(
        Database db, string gatewayId, string canonicalSignature, long signedAtUnix,
        long? deviceId, long? companyId, CancellationToken ct = default)
    {
        var gw = string.IsNullOrWhiteSpace(gatewayId) ? DefaultGatewayId : gatewayId;
        var signedAt = DateTimeOffset.FromUnixTimeSeconds(signedAtUnix).UtcDateTime;
        var rows = await db.ExecuteAsync(
            @"INSERT INTO gps_gateway_replay (gateway_id, signature, signed_at, device_id, company_id)
              VALUES (@gw, @sig, @signedAt, @did, @cid)
              ON CONFLICT (gateway_id, signature) DO NOTHING",
            c =>
            {
                c.Parameters.AddWithValue("@gw", gw);
                c.Parameters.AddWithValue("@sig", canonicalSignature);
                c.Parameters.AddWithValue("@signedAt", signedAt);
                c.Parameters.AddWithValue("@did", (object?)deviceId ?? DBNull.Value);
                c.Parameters.AddWithValue("@cid", (object?)companyId ?? DBNull.Value);
            }, ct);
        return rows == 1;
    }

    /// <summary>
    /// In-memory fallback, used ONLY when the durable table is definitively absent (pre-migration).
    /// Returns true when newly reserved, false when already seen. Bounded by a 300s prune.
    /// </summary>
    public static bool TryReserveInMemory(string gatewayId, string canonicalSignature, long signedAtUnix)
    {
        var gw = string.IsNullOrWhiteSpace(gatewayId) ? DefaultGatewayId : gatewayId;
        var key = $"{gw}:{canonicalSignature}";
        var added = Fallback.TryAdd(key, signedAtUnix);
        var cutoff = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - WindowSeconds;
        foreach (var stale in Fallback.Where(p => p.Value < cutoff).Select(p => p.Key))
            Fallback.TryRemove(stale, out _);
        return added;
    }

    /// <summary>
    /// Compensating release for the in-memory fallback: the durable path rolls its reservation back
    /// with the write transaction automatically, but an in-memory reservation cannot join that tx,
    /// so if the writes fail after a fallback reservation the caller must release it — otherwise a
    /// legitimate retry of the same packet is wrongly rejected until the entry ages out. Only used
    /// on the (pre-migration) Absent path.
    /// </summary>
    public static void ReleaseInMemory(string gatewayId, string canonicalSignature)
    {
        var gw = string.IsNullOrWhiteSpace(gatewayId) ? DefaultGatewayId : gatewayId;
        Fallback.TryRemove($"{gw}:{canonicalSignature}", out _);
    }
}
