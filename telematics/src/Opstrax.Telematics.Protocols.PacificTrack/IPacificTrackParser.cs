using Opstrax.Telematics.Contracts.Adapters;

namespace Opstrax.Telematics.Protocols.PacificTrack;

/// <summary>
/// The seam Pacific Track's <b>official</b> parser is installed behind.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is an interface and not a decoder.</b> OpsTrax does not ship a Pacific Track
/// wire decoder and must not: the PT protocol specification is distributed by the vendor under
/// licence, this repository has never had a byte captured from the PT40-Q under test
/// (<c>docs/telematics/pt40/pt40-fingerprint.md</c>), and a guessed decoder is strictly worse
/// than no decoder — it produces plausible coordinates that are wrong, which is undetectable
/// downstream. So the decode step is a dependency the operator supplies, not something this
/// assembly infers.
/// </para>
/// <para>
/// <b>Installing the vendor parser.</b> Pacific Track publish the parser in C#, Python and Java.
/// Any of the three can satisfy this interface:
/// </para>
/// <list type="bullet">
///   <item><description><b>C#</b> — reference the vendor assembly and write a ~50-line class
///     implementing this interface directly, in-process. Fastest, and the only option that keeps
///     the <see cref="IProtocolAdapter"/> purity contract intact.</description></item>
///   <item><description><b>Python or Java</b> — run the vendor parser as a co-located child
///     process and use <see cref="StdioParserBridge"/>, which speaks a documented
///     newline-delimited JSON protocol over the child's stdin/stdout.</description></item>
/// </list>
/// <para>
/// <b>Fail closed.</b> Until a parser is installed the gateway registers
/// <see cref="UnavailablePacificTrackParser"/>, which never claims a stream. A PT device that
/// connects is refused and counted, never silently mis-decoded by a different vendor's adapter.
/// </para>
/// <para>
/// <b>Purity.</b> Implementations are shared as singletons across every connection and MUST be
/// thread-safe. An in-process C# implementation should also be pure (no I/O, no connection
/// state), matching <see cref="IProtocolAdapter"/>. <see cref="StdioParserBridge"/> deliberately
/// deviates — it does bounded, serialized I/O to a child process — and documents the cost.
/// </para>
/// </remarks>
public interface IPacificTrackParser
{
    /// <summary>
    /// Whether a working vendor parser is actually installed behind this seam. When
    /// <see langword="false"/> the adapter refuses every stream instead of guessing, and the
    /// gateway logs the refusal as a commissioning gap rather than a device fault.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>Version string of the installed vendor parser, for provenance and support. Empty when unavailable.</summary>
    string ParserVersion { get; }

    /// <summary>
    /// Decides whether <paramref name="opening"/> is the start of a Pacific Track stream.
    /// Must not consume the buffer. Returns <see cref="ProtocolMatch.Incomplete"/> when there
    /// are too few bytes to tell.
    /// </summary>
    ProtocolMatch Identify(ReadOnlySpan<byte> opening);

    /// <summary>
    /// Decodes every complete frame present in <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">The currently buffered, possibly partial, byte stream.</param>
    /// <param name="consumed">Leading bytes fully consumed into the returned frames; 0 when no frame completed.</param>
    /// <returns>Decoded frames in wire order. Never <see langword="null"/>.</returns>
    /// <exception cref="ProtocolException">The buffer is malformed beyond recovery.</exception>
    IReadOnlyList<DecodedMessage> Decode(ReadOnlySpan<byte> buffer, out int consumed);

    /// <summary>
    /// Builds the protocol acknowledgement <paramref name="message"/> requires, or an empty
    /// array when the protocol expects none.
    /// </summary>
    byte[] EncodeAck(DecodedMessage message);
}
