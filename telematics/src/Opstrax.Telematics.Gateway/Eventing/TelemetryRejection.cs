namespace Opstrax.Telematics.Gateway.Eventing;

/// <summary>
/// The payload published on <c>telemetry.rejected</c> when the gateway decoded a frame but
/// refused to admit it into the fabric — overwhelmingly because the identity claim did not
/// resolve to a known, ingestable device.
/// </summary>
/// <remarks>
/// <para>
/// <b>A rejection is deliberately not tenant-bound.</b> Its envelope carries
/// <see cref="Guid.Empty"/> / company <c>0</c>, because the whole point of the rejection is
/// that we do <em>not</em> know who owns this device — and inventing an owner to satisfy a
/// required field is exactly the cross-tenant leak this contract exists to prevent. Consumers
/// of this topic are platform/security consumers, never tenant-scoped ones.
/// </para>
/// <para>
/// The claimed identifier is stored <b>masked</b>. This topic is retained and fanned out to
/// security tooling; the un-redacted IMEI of an unknown (possibly spoofed) device buys nothing
/// that the masked form does not, and carrying it would spread personal data into a lane whose
/// consumers were never scoped for it.
/// </para>
/// </remarks>
internal sealed record TelemetryRejection
{
    /// <summary>Machine-readable cause, for example <c>unknown-device</c>.</summary>
    public required string Reason { get; init; }

    /// <summary>The identity the frame claimed, masked for logging/retention safety. Never the full IMEI.</summary>
    public required string ClaimedIdentifierMasked { get; init; }

    /// <summary>The wire protocol the rejected frame was decoded with, for example <c>GT06</c>.</summary>
    public required string ProtocolName { get; init; }

    /// <summary>The category of frame that was rejected, for example <c>Login</c>.</summary>
    public required string MessageType { get; init; }

    /// <summary>When the gateway received the rejected frame.</summary>
    public required DateTimeOffset ReceivedAtGatewayUtc { get; init; }

    /// <summary>Size of the rejected frame in bytes. The bytes themselves are not retained here.</summary>
    public int RawFrameBytes { get; init; }

    /// <summary>The remote peer, for correlating a probe campaign across connections.</summary>
    public string RemoteEndpoint { get; init; } = string.Empty;
}

/// <summary>Well-known <see cref="TelemetryRejection.Reason"/> values.</summary>
internal static class RejectionReasons
{
    /// <summary>The registry did not recognise the claimed identity. Fail closed: no tenant is invented.</summary>
    public const string UnknownDevice = "unknown-device";

    /// <summary>The registry resolved the device, but its lifecycle state bars it from ingesting (suspended/retired).</summary>
    public const string DeviceNotIngestable = "device-not-ingestable";

    /// <summary>A telemetry frame arrived on a connection that never completed a successful login.</summary>
    public const string UnidentifiedSession = "unidentified-session";
}
