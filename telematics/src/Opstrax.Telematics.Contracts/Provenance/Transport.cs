namespace Opstrax.Telematics.Contracts.Provenance;

/// <summary>
/// The wire transport over which the originating frame or message reached the
/// fabric. Transport is descriptive metadata for observability and replay; it is
/// intentionally decoupled from <see cref="TelemetrySource"/> so that, for
/// example, a <see cref="TelemetrySource.VendorCloud"/> event can be carried over
/// either <see cref="Http"/> or <see cref="VendorWebhook"/>.
/// </summary>
public enum Transport
{
    /// <summary>Raw TCP stream (typical for GT06/Concox-class hardware trackers).</summary>
    Tcp = 0,

    /// <summary>Connectionless UDP datagrams.</summary>
    Udp = 1,

    /// <summary>HTTP(S) request/response.</summary>
    Http = 2,

    /// <summary>MQTT publish/subscribe.</summary>
    Mqtt = 3,

    /// <summary>WebSocket full-duplex channel.</summary>
    WebSocket = 4,

    /// <summary>Inbound webhook push from a vendor cloud.</summary>
    VendorWebhook = 5,

    /// <summary>Outbound poll of a vendor cloud API on a schedule.</summary>
    VendorPoll = 6,
}
