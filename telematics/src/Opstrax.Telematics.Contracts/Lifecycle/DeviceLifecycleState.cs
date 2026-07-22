namespace Opstrax.Telematics.Contracts.Lifecycle;

/// <summary>
/// The full lifecycle of a telematics device inside the fabric, from catalogue
/// entry through decommissioning. The ordering here is narrative, not a guarantee
/// that transitions are linear — see <see cref="LifecycleTransitions"/> for the
/// authoritative allowed-transition map.
/// </summary>
/// <remarks>
/// <para>
/// A critical invariant of this model: <b>provisioning never implies connectivity.</b>
/// A device can be fully <see cref="Provisioned"/> and configured yet has never sent
/// a byte; it only reaches <see cref="Online"/> after it authenticates and its data
/// passes validation. Any code that infers "the device is live" from a provisioning
/// record is wrong by construction.
/// </para>
/// </remarks>
public enum DeviceLifecycleState
{
    /// <summary>Catalogued but not yet committed for deployment. No credentials issued.</summary>
    Draft = 0,

    /// <summary>Registered in the device registry with identity and credentials issued. Not yet connected.</summary>
    Provisioned = 1,

    /// <summary>Provisioned but not yet bound to a tenant/vehicle owner.</summary>
    AwaitingAssignment = 2,

    /// <summary>Assigned but still missing required configuration (APN, server URL, reporting profile).</summary>
    AwaitingConfiguration = 3,

    /// <summary>Fully configured and waiting for the device to make first contact.</summary>
    AwaitingFirstConnection = 4,

    /// <summary>The device has presented an identity claim (IMEI/serial) but is not yet authenticated.</summary>
    Identified = 5,

    /// <summary>Identity has been authenticated against the registry credential handle.</summary>
    Authenticated = 6,

    /// <summary>Authenticated; its first stream of data is being validated for plausibility.</summary>
    Validating = 7,

    /// <summary>Actively connected and reporting fresh, validated telemetry.</summary>
    Online = 8,

    /// <summary>Connected but reporting later than its expected cadence (soft lateness).</summary>
    Delayed = 9,

    /// <summary>No fresh fix within the freshness budget; last-known position is aging.</summary>
    Stale = 10,

    /// <summary>Confirmed disconnected — no transport session and past the stale horizon.</summary>
    Offline = 11,

    /// <summary>Connected but emitting partially invalid/implausible data (sensor fault, GPS drift).</summary>
    Degraded = 12,

    /// <summary>Isolated by policy pending investigation (suspected spoofing, replay, tamper). Data retained, not trusted.</summary>
    Quarantined = 13,

    /// <summary>Administratively disabled (billing, security hold). Rejected at ingest until lifted.</summary>
    Suspended = 14,

    /// <summary>Permanently decommissioned. Terminal; identity retired and not reusable.</summary>
    Retired = 15,
}
