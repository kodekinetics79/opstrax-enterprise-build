using Opstrax.Telematics.Contracts.Identity;

namespace Opstrax.Telematics.Contracts.Adapters;

/// <summary>
/// One fully-framed protocol message produced by <see cref="IProtocolAdapter.Decode"/>.
/// A decoded message is the bridge between raw wire bytes and the normalized
/// <c>CanonicalTelemetryEvent</c>: it exposes the adapter's structured interpretation
/// while <b>retaining the exact raw frame</b> so the pipeline can acknowledge, audit,
/// replay and forensically re-decode without re-reading the socket.
/// </summary>
public sealed class DecodedMessage
{
    /// <summary>Creates a decoded message.</summary>
    /// <param name="messageType">The normalized category of this frame.</param>
    /// <param name="rawFrame">The exact bytes of this single frame, defensively copied by the constructor.</param>
    /// <param name="fields">Adapter-decoded fields keyed by adapter-local names. Never <see langword="null"/>.</param>
    /// <param name="identity">The identity claim carried by the frame, when present (typically on <see cref="MessageType.Login"/>).</param>
    /// <param name="protocolMessageId">
    /// The protocol's own message/serial number when the frame carries one, used to build
    /// a correct protocol-level acknowledgement. <see langword="null"/> when not applicable.
    /// </param>
    /// <param name="requiresAck"><see langword="true"/> when the protocol expects the server to answer this frame.</param>
    public DecodedMessage(
        MessageType messageType,
        ReadOnlySpan<byte> rawFrame,
        IReadOnlyDictionary<string, object?> fields,
        DeviceIdentityRef? identity = null,
        int? protocolMessageId = null,
        bool requiresAck = false)
    {
        MessageType = messageType;
        RawFrame = rawFrame.ToArray();
        Fields = fields ?? throw new ArgumentNullException(nameof(fields));
        Identity = identity;
        ProtocolMessageId = protocolMessageId;
        RequiresAck = requiresAck;
    }

    /// <summary>The normalized category of the frame.</summary>
    public MessageType MessageType { get; }

    /// <summary>
    /// The exact, immutable bytes of the single frame this message was decoded from.
    /// Preserved verbatim for acknowledgement construction, audit and replay.
    /// </summary>
    public IReadOnlyList<byte> RawFrame { get; }

    /// <summary>
    /// The adapter's decoded fields, keyed by adapter-local names (for example
    /// <c>"latitude"</c>, <c>"speedKph"</c>, <c>"alarmType"</c>). A later normalization
    /// stage maps these into the canonical typed fields and VSS signal bag.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Fields { get; }

    /// <summary>
    /// The identity claim carried by this frame, if any. Untrusted — resolve via
    /// <see cref="IDeviceRegistry"/>. Usually populated on <see cref="MessageType.Login"/>.
    /// </summary>
    public DeviceIdentityRef? Identity { get; }

    /// <summary>The protocol's own serial/message id for this frame, when present.</summary>
    public int? ProtocolMessageId { get; }

    /// <summary>Whether the protocol requires the server to acknowledge this frame.</summary>
    public bool RequiresAck { get; }
}
