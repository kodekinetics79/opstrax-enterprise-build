using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// Deploy-safe capability guard for the provenance / trust columns on
// latest_vehicle_positions (source, provider, protocol, adapter_version,
// device_fix_time, gateway_received_at, normalized_at, confidence, trust_score,
// quality_flags).
//
// Those columns are created idempotently by TelemetrySchemaService in
// owner-capable environments AND by migration
// database/migrations/telematics/001_latest_position_provenance.sql. In
// PRODUCTION the app runs as a restricted role that SKIPS startup schema init,
// so the columns may NOT exist until the owner applies migration 001 out-of-band.
//
// Every read/write that references a provenance column MUST gate on this probe:
//   - reads SELECT the columns only when present, else return null provenance;
//   - writes stamp the columns only when present, else omit them entirely.
// This keeps a query from ever hard-failing with "column ... does not exist".
//
// The probe runs ONCE against information_schema, is thread-safe, and caches its
// result for the process lifetime. When the owner applies migration 001 to a
// running production instance, provenance activates on the next app restart —
// the paired-with-deploy migration rollout the honest-map plan already assumes.
public static class TelemetryProvenance
{
    private static volatile Task<bool>? _probe;
    private static readonly object _gate = new();

    // Canonical logical-provenance source vocabulary (kept here so every writer
    // stamps a consistent value).
    public const string SourceNativeEld  = "native_eld";  // native ELD HMAC ingest
    public const string SourceGateway    = "gateway";     // trusted GT06/Concox gateway forwarder
    public const string SourceSimulator  = "simulator";   // demo/dev simulator
    public const string SourcePartnerApi = "partner_api"; // partner/vendor API pull (Samsara)
    public const string SourceLegacy     = "legacy";      // pre-provenance / backfill
    public const string SourceSeed       = "seed";        // seeded fixtures

    // True when latest_vehicle_positions.source exists. Probed once against
    // information_schema, then cached (thread-safe) for the process lifetime.
    public static Task<bool> ColumnsAvailableAsync(Database db, CancellationToken ct = default)
    {
        var existing = _probe;
        if (existing is not null) return existing;
        lock (_gate)
        {
            return _probe ??= ProbeAsync(db);
        }
    }

    private static async Task<bool> ProbeAsync(Database db)
    {
        try
        {
            var n = await db.ScalarLongAsync(
                @"SELECT COUNT(*) FROM information_schema.columns
                  WHERE table_schema = current_schema()
                    AND table_name  = 'latest_vehicle_positions'
                    AND column_name = 'source'");
            return n > 0;
        }
        catch
        {
            // The probe must never throw into a hot read/write path. A clean
            // count of 0 (columns genuinely absent) is cached as a real negative,
            // but a TRANSIENT failure here must NOT be frozen as a permanent
            // negative — clear the cache so the next call re-probes. Report absent
            // for THIS call (the safe default: provenance is simply skipped).
            _probe = null;
            return false;
        }
    }
}
