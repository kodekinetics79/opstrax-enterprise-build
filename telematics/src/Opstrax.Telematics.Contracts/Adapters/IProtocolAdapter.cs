namespace Opstrax.Telematics.Contracts.Adapters;

/// <summary>
/// The versioned plugin contract every device-protocol decoder implements. An adapter
/// is a <b>pure, stateless</b> translator between one vendor's wire protocol and the
/// fabric's normalized message model: it identifies its protocol from opening bytes,
/// frames and decodes a byte stream into <see cref="DecodedMessage"/> instances, and
/// encodes the acknowledgements/commands that protocol expects.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purity &amp; safety.</b> Implementations must not perform I/O, hold connection
/// state, or resolve device ownership — identity resolution belongs to
/// <see cref="Identity.IDeviceRegistry"/>, and connection/session handling belongs to
/// the gateway. Adapters operate only on the bytes handed to them and must be safe to
/// share as singletons across concurrent connections.
/// </para>
/// <para>
/// <b>Partial frames.</b> <see cref="Decode"/> is explicitly partial-frame aware: it is
/// called with whatever bytes are currently buffered, decodes as many <em>complete</em>
/// frames as it can, and reports how many bytes it consumed so the gateway can retain the
/// unconsumed remainder and append the next read. It must never block waiting for more
/// bytes.
/// </para>
/// </remarks>
public interface IProtocolAdapter
{
    /// <summary>Immutable metadata describing this adapter and the hardware it supports.</summary>
    AdapterMetadata Metadata { get; }

    /// <summary>
    /// Inspects the opening bytes of a stream to decide whether it speaks this adapter's
    /// protocol. Must be side-effect free and must not consume the buffer. When the buffer
    /// is too short to decide, return <see cref="ProtocolMatch.Incomplete"/>.
    /// </summary>
    /// <param name="opening">The bytes buffered so far at the start of a connection.</param>
    /// <returns>A match verdict with confidence, used to arbitrate between adapters.</returns>
    ProtocolMatch TryIdentify(ReadOnlySpan<byte> opening);

    /// <summary>
    /// Decodes as many complete frames as are present in <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">The currently buffered, possibly partial, byte stream.</param>
    /// <param name="consumed">
    /// Set to the number of leading bytes that were fully consumed into returned messages.
    /// The caller retains <c>buffer[consumed..]</c> as the start of the next decode. When no
    /// complete frame is present this is 0 and the returned list is empty.
    /// </param>
    /// <returns>Zero or more decoded messages, in wire order. Never <see langword="null"/>.</returns>
    /// <exception cref="ProtocolException">The buffer is malformed beyond recovery (bad checksum, impossible framing).</exception>
    IReadOnlyList<DecodedMessage> Decode(ReadOnlySpan<byte> buffer, out int consumed);

    /// <summary>
    /// Builds the protocol-level acknowledgement the device expects for
    /// <paramref name="message"/>. Return an empty array when the protocol requires no
    /// acknowledgement for that frame.
    /// </summary>
    /// <param name="message">The decoded frame to acknowledge (carries any needed serial/message id).</param>
    /// <returns>The exact ack bytes to write back to the device; empty when no ack is due.</returns>
    byte[] EncodeAck(DecodedMessage message);

    /// <summary>
    /// Encodes a downlink <paramref name="command"/> into this protocol's wire bytes.
    /// </summary>
    /// <param name="command">The protocol-agnostic command intent.</param>
    /// <returns>
    /// The wire bytes to transmit, or <see langword="null"/> when this protocol cannot
    /// express the requested command. Returning <see langword="null"/> — rather than an
    /// approximation — keeps unsupported commands from being silently mis-sent.
    /// </returns>
    byte[]? EncodeCommand(DeviceCommand command);
}
