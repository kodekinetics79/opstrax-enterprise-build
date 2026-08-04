namespace Opstrax.Telematics.Contracts.Provenance;

/// <summary>
/// Identifies the origin category of a telemetry event as it entered the fabric.
/// Provenance is authoritative for trust decisions: a fix that originated from
/// <see cref="Simulator"/> or <see cref="Seed"/> must never be treated with the
/// same trust as one decoded from a <see cref="DirectDevice"/> frame.
/// </summary>
public enum TelemetrySource
{
    /// <summary>A physical field device that spoke its native protocol directly to an Opstrax gateway.</summary>
    DirectDevice = 0,

    /// <summary>Relayed from a third-party telematics cloud (e.g. a vendor webhook or poll aggregator).</summary>
    VendorCloud = 1,

    /// <summary>Produced by a driver/operator mobile application acting as a soft device.</summary>
    MobileApp = 2,

    /// <summary>Synthetic data emitted by a load/soak/route simulator. Never billable, never map-authoritative.</summary>
    Simulator = 3,

    /// <summary>Deterministic development/demo seed data. Excluded from trust and anomaly baselines.</summary>
    Seed = 4,

    /// <summary>Batch-imported historical data (backfill, migration, CSV/telematics export).</summary>
    Import = 5,

    /// <summary>Manually entered or corrected by an operator through an authenticated console.</summary>
    Manual = 6,
}
