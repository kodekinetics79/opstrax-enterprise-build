namespace Opstrax.Telematics.Contracts.Adapters;

/// <summary>
/// Raised by an <see cref="IProtocolAdapter"/> when a buffer is malformed in a way the
/// adapter cannot recover from — a bad checksum, an impossible length header, or a
/// framing violation. This signals a <em>protocol</em> fault (attacker-controlled or
/// corrupt input), distinct from an infrastructure fault.
/// </summary>
/// <remarks>
/// The gateway treats <see cref="ProtocolException"/> as a fail-closed, drop-the-frame
/// (and typically drop-the-connection) condition; it must never be caught and turned
/// into a fabricated event. Adapters should throw this rather than guessing at
/// corrupt data.
/// </remarks>
public class ProtocolException : Exception
{
    /// <summary>The adapter that raised the fault, for diagnostics. May be <see langword="null"/>.</summary>
    public string? AdapterName { get; }

    /// <summary>Byte offset within the buffer at which decoding failed, when known.</summary>
    public int? Offset { get; }

    /// <summary>Creates a protocol fault with a human-readable message.</summary>
    public ProtocolException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a protocol fault with adapter context and an optional failure offset.</summary>
    public ProtocolException(string message, string? adapterName, int? offset = null, Exception? innerException = null)
        : base(message, innerException)
    {
        AdapterName = adapterName;
        Offset = offset;
    }
}
