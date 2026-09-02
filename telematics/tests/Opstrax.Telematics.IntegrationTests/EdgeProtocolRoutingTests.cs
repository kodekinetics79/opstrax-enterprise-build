using Opstrax.Telematics.Contracts.Adapters;
using Opstrax.Telematics.Gateway.Edge;
using Opstrax.Telematics.Protocols.Gt06;
using Opstrax.Telematics.Protocols.PacificTrack;

namespace Opstrax.Telematics.IntegrationTests;

/// <summary>
/// Covers protocol arbitration on a shared public port, and the Pacific Track seam's fail-closed
/// behaviour when the vendor parser has not been installed.
/// </summary>
public sealed class EdgeProtocolRoutingTests
{
    private static readonly byte[] Gt06Login = Convert.FromHexString("78780D0108681203033379760001618E0D0A");

    [Fact]
    public void Gt06Stream_IsRoutedToTheGt06Adapter()
    {
        var router = new ProtocolRouter(new IProtocolAdapter[]
        {
            new Gt06Adapter(2048),
            new PacificTrackAdapter(UnavailablePacificTrackParser.Instance),
        });

        ProtocolSelection selection = router.Select(Gt06Login);

        Assert.True(selection.IsMatch);
        Assert.Equal(Gt06Adapter.ProtocolName, selection.Adapter!.Metadata.Name);
    }

    [Fact]
    public void UnrecognisedBytes_AreADefinitiveNonMatch_NotAnInvitationToKeepBuffering()
    {
        var router = new ProtocolRouter(new IProtocolAdapter[] { new Gt06Adapter(2048) });

        ProtocolSelection selection = router.Select(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

        Assert.False(selection.IsMatch);
        Assert.False(selection.NeedMoreData);
    }

    [Fact]
    public void TooFewBytesToDecide_AsksForMore()
    {
        var router = new ProtocolRouter(new IProtocolAdapter[] { new Gt06Adapter(2048) });

        ProtocolSelection selection = router.Select(new byte[] { 0x78 });

        Assert.False(selection.IsMatch);
        Assert.True(selection.NeedMoreData);
    }

    [Fact]
    public void HighestConfidenceWins_RegardlessOfRegistrationOrder()
    {
        var weak = new StubAdapter("Weak", ProtocolMatch.Match(0.4));
        var strong = new StubAdapter("Strong", ProtocolMatch.Match(0.9));

        Assert.Equal("Strong", new ProtocolRouter(new IProtocolAdapter[] { weak, strong })
            .Select(new byte[] { 1 }).Adapter!.Metadata.Name);

        Assert.Equal("Strong", new ProtocolRouter(new IProtocolAdapter[] { strong, weak })
            .Select(new byte[] { 1 }).Adapter!.Metadata.Name);
    }

    [Fact]
    public void AmbiguousFingerprint_IsRefused_RatherThanDecodedWithTheWrongFieldLayout()
    {
        // Two adapters equally sure means the fingerprint is genuinely ambiguous. Picking either
        // yields coordinates that are wrong but entirely plausible — the worst outcome available.
        var router = new ProtocolRouter(new IProtocolAdapter[]
        {
            new StubAdapter("VendorA", ProtocolMatch.Match(0.8)),
            new StubAdapter("VendorB", ProtocolMatch.Match(0.8)),
        });

        ProtocolSelection selection = router.Select(new byte[] { 1, 2, 3 });

        Assert.False(selection.IsMatch);
        Assert.False(selection.NeedMoreData);
    }

    [Fact]
    public void AnEdgeWithNoAdapters_IsRefusedAtConstruction()
    {
        Assert.Throws<ArgumentException>(() => new ProtocolRouter(Array.Empty<IProtocolAdapter>()));
    }

    // ── The Pacific Track seam ─────────────────────────────────────────────────

    [Fact]
    public void WithNoVendorParser_ThePacificTrackAdapterClaimsNothing()
    {
        // This is what keeps an unhandled PT device a visible refusal instead of being mis-decoded
        // by whichever other adapter happens to be least strict.
        var adapter = new PacificTrackAdapter(UnavailablePacificTrackParser.Instance);

        Assert.False(adapter.IsParserInstalled);
        Assert.False(adapter.TryIdentify(new byte[] { 0x24, 0x24, 0x01 }).IsMatch);
        Assert.False(adapter.TryIdentify(Gt06Login).IsMatch);
    }

    [Fact]
    public void WithNoVendorParser_DecodingThrowsRatherThanReturningSilence()
    {
        // Returning "no frames" would look like a quiet device; throwing surfaces the misconfiguration.
        var adapter = new PacificTrackAdapter(UnavailablePacificTrackParser.Instance);

        ProtocolException error = Assert.Throws<ProtocolException>(() =>
        {
            adapter.Decode(new byte[] { 1, 2, 3 }, out _);
        });

        Assert.Contains("No Pacific Track parser is installed", error.Message);
    }

    [Fact]
    public void PacificTrackAdapter_DoesNotFabricateDownlinkCommands()
    {
        var adapter = new PacificTrackAdapter(UnavailablePacificTrackParser.Instance);

        Assert.Null(adapter.EncodeCommand(new DeviceCommand("reboot", new Dictionary<string, string>())));
    }

    [Fact]
    public void PacificTrackAdapter_NamesTheProtocolFamilyNotTheModelOnTheLabel()
    {
        AdapterMetadata metadata = new PacificTrackAdapter(UnavailablePacificTrackParser.Instance).Metadata;

        Assert.Equal("PacificTrack", metadata.Name);
        Assert.Contains("PT40-Q", metadata.SupportedModels);
    }

    /// <summary>An adapter that returns a scripted identification verdict, for arbitration tests.</summary>
    private sealed class StubAdapter : IProtocolAdapter
    {
        private readonly ProtocolMatch _match;

        public StubAdapter(string name, ProtocolMatch match)
        {
            _match = match;
            Metadata = new AdapterMetadata(name, "1.0.0", Array.Empty<string>(), Array.Empty<string>());
        }

        public AdapterMetadata Metadata { get; }

        public ProtocolMatch TryIdentify(ReadOnlySpan<byte> opening) => _match;

        public IReadOnlyList<DecodedMessage> Decode(ReadOnlySpan<byte> buffer, out int consumed)
        {
            consumed = buffer.Length;
            return Array.Empty<DecodedMessage>();
        }

        public byte[] EncodeAck(DecodedMessage message) => Array.Empty<byte>();

        public byte[]? EncodeCommand(DeviceCommand command) => null;
    }
}
