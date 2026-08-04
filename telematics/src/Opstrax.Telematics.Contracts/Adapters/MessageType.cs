namespace Opstrax.Telematics.Contracts.Adapters;

/// <summary>
/// The coarse category of a decoded protocol message, normalized across vendor
/// protocols so the gateway can route by intent without knowing the wire dialect.
/// </summary>
public enum MessageType
{
    /// <summary>Device login / registration handshake (typically carries the identity claim).</summary>
    Login = 0,

    /// <summary>Keep-alive / heartbeat with no positional payload.</summary>
    Heartbeat = 1,

    /// <summary>A GNSS position report.</summary>
    Location = 2,

    /// <summary>An alarm/event (SOS, tow, low battery, geofence, tamper).</summary>
    Alarm = 3,

    /// <summary>A status/diagnostics report without a primary position fix.</summary>
    Status = 4,

    /// <summary>A protocol-level acknowledgement.</summary>
    Ack = 5,

    /// <summary>A well-formed frame whose semantics this adapter does not (yet) map.</summary>
    Unknown = 6,
}
