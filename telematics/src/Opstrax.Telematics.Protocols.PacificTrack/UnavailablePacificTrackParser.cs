using Opstrax.Telematics.Contracts.Adapters;

namespace Opstrax.Telematics.Protocols.PacificTrack;

/// <summary>
/// The fail-closed default that is registered when no Pacific Track parser has been installed.
/// It claims nothing and decodes nothing.
/// </summary>
/// <remarks>
/// <para>
/// This type exists so that "the vendor parser is missing" is a <b>visible, counted refusal</b>
/// rather than a silent behaviour change. Without it the gateway's adapter arbitration would
/// simply find no PT match and fall through to whichever other adapter was least strict —
/// exactly the failure mode <c>docs/telematics/pt40/pt40-fingerprint.md</c> forbids
/// ("never fall through to a lower branch after a signature already matched").
/// </para>
/// <para>
/// <see cref="Decode"/> throwing rather than returning empty is deliberate: reaching decode means
/// something already routed a stream here, which can only happen through a configuration mistake.
/// Returning "no frames" would look like a quiet, slow device; throwing surfaces the mistake.
/// </para>
/// </remarks>
public sealed class UnavailablePacificTrackParser : IPacificTrackParser
{
    /// <summary>A shared instance; the type holds no state.</summary>
    public static readonly UnavailablePacificTrackParser Instance = new();

    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public string ParserVersion => string.Empty;

    /// <inheritdoc />
    /// <remarks>Always a definitive non-match — never <c>Incomplete</c>, which would ask the
    /// gateway to keep buffering bytes for a decision that can never arrive.</remarks>
    public ProtocolMatch Identify(ReadOnlySpan<byte> opening) => ProtocolMatch.NoMatch();

    /// <inheritdoc />
    public IReadOnlyList<DecodedMessage> Decode(ReadOnlySpan<byte> buffer, out int consumed) =>
        throw new ProtocolException(
            "No Pacific Track parser is installed. Install the vendor parser behind IPacificTrackParser " +
            "(in-process C#, or Python/Java via StdioParserBridge) before routing PT devices here.",
            PacificTrackAdapter.ProtocolName);

    /// <inheritdoc />
    public byte[] EncodeAck(DecodedMessage message) => Array.Empty<byte>();
}
