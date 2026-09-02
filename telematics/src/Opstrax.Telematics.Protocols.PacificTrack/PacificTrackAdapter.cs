using Opstrax.Telematics.Contracts.Adapters;

namespace Opstrax.Telematics.Protocols.PacificTrack;

/// <summary>
/// <see cref="IProtocolAdapter"/> for Pacific Track hardware (PT40 / PT40-Q class). It is a thin,
/// stateless translator over <see cref="IPacificTrackParser"/> — the seam Pacific Track's official
/// parser is installed behind. This assembly contains <b>no</b> PT wire-format knowledge of its own.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read this before assuming PT40 works.</b> The adapter is only as real as the parser behind
/// it. With the default <see cref="UnavailablePacificTrackParser"/> installed, this adapter
/// deliberately matches nothing: a PT device connecting to the gateway is refused and counted,
/// which is the honest outcome when the decoder is absent. Everything else in the edge —
/// listener, IMEI allowlist, replay defence, HTTPS forwarding, outbox — is complete and
/// exercised; the vendor decode step is the one dependency that must be supplied.
/// </para>
/// <para>
/// <b>Why "PacificTrack" and not "PT40".</b> <see cref="AdapterMetadata.Name"/> is stamped into
/// provenance on every forwarded fix. It names the <em>protocol family the bytes were decoded
/// as</em>, never the model on the label — a model name is not protocol evidence
/// (<c>docs/telematics/pt40/pt40-fingerprint.md</c>).
/// </para>
/// </remarks>
public sealed class PacificTrackAdapter : IProtocolAdapter
{
    /// <summary>Stable protocol/adapter identifier, stamped into forwarded provenance.</summary>
    public const string ProtocolName = "PacificTrack";

    /// <summary>Version of this translation shim — NOT of the vendor parser behind it.</summary>
    public const string AdapterVersion = "1.0.0";

    private readonly IPacificTrackParser _parser;

    /// <summary>Creates the adapter over an installed parser.</summary>
    /// <param name="parser">
    /// The vendor parser. Pass <see cref="UnavailablePacificTrackParser.Instance"/> (the gateway's
    /// default) when none is installed; the adapter then refuses every stream.
    /// </param>
    public PacificTrackAdapter(IPacificTrackParser parser)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));

        Metadata = new AdapterMetadata(
            Name: ProtocolName,
            Version: AdapterVersion,
            SupportedModels: new[] { "PT40", "PT40-Q" },
            SupportedFirmware: Array.Empty<string>());
    }

    /// <inheritdoc />
    public AdapterMetadata Metadata { get; }

    /// <summary>
    /// Whether a real vendor parser is installed. The gateway logs this once at startup so an
    /// operator can tell "PT is wired up" from "PT is a fail-closed stub" without reading code.
    /// </summary>
    public bool IsParserInstalled => _parser.IsAvailable;

    /// <summary>Version of the installed vendor parser; empty when none is installed.</summary>
    public string ParserVersion => _parser.ParserVersion;

    /// <inheritdoc />
    public ProtocolMatch TryIdentify(ReadOnlySpan<byte> opening) =>
        // No parser => never claim a stream. Arbitration then has no PT candidate at all, which is
        // what makes an unhandled PT device a refusal rather than a mis-decode by another adapter.
        _parser.IsAvailable ? _parser.Identify(opening) : ProtocolMatch.NoMatch();

    /// <inheritdoc />
    public IReadOnlyList<DecodedMessage> Decode(ReadOnlySpan<byte> buffer, out int consumed) =>
        _parser.Decode(buffer, out consumed);

    /// <inheritdoc />
    public byte[] EncodeAck(DecodedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.RequiresAck ? _parser.EncodeAck(message) : Array.Empty<byte>();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Downlink is not routed through this seam. Returning <see langword="null"/> is the contract's
    /// "this protocol cannot express that command" answer, and is far safer than approximating a
    /// command to hardware that is on a vehicle.
    /// </remarks>
    public byte[]? EncodeCommand(DeviceCommand command) => null;
}
