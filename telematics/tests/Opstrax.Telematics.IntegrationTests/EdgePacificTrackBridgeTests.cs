using Opstrax.Telematics.Contracts.Adapters;
using Opstrax.Telematics.Protocols.PacificTrack;

namespace Opstrax.Telematics.IntegrationTests;

/// <summary>
/// Covers the line protocol that lets Pacific Track's official Python, Java or C# parser run as a
/// child process behind <see cref="IPacificTrackParser"/>.
/// </summary>
/// <remarks>
/// The bridge is exercised over in-memory streams rather than a real subprocess: the contract under
/// test is the wire protocol and its failure handling, and stream injection makes the
/// desynchronization cases (a child that hangs, dies, or answers with garbage) deterministic
/// instead of timing-dependent.
/// </remarks>
public sealed class EdgePacificTrackBridgeTests
{
    private static (StdioParserBridge Bridge, StringWriter Requests, List<string> Faults) Build(
        params string[] responses)
    {
        var requests = new StringWriter();
        var faults = new List<string>();

        var bridge = new StdioParserBridge(
            requests,
            new StringReader(string.Join('\n', responses) + (responses.Length > 0 ? "\n" : string.Empty)),
            parserVersion: "vendor-3.2.1",
            requestTimeout: TimeSpan.FromSeconds(5),
            onFault: faults.Add);

        return (bridge, requests, faults);
    }

    [Fact]
    public void Identify_MapsTheVendorVerdict()
    {
        (StdioParserBridge bridge, StringWriter requests, _) =
            Build("""{"ok":true,"match":true,"confidence":0.97}""");

        ProtocolMatch match = bridge.Identify(new byte[] { 0x24, 0x24, 0x01 });

        Assert.True(match.IsMatch);
        Assert.Equal(0.97, match.Confidence, precision: 6);
        Assert.Contains("\"op\":\"identify\"", requests.ToString());
        Assert.Contains("\"hex\":\"242401\"", requests.ToString());
    }

    [Fact]
    public void Identify_HonoursNeedMoreData()
    {
        (StdioParserBridge bridge, _, _) = Build("""{"ok":true,"needMoreData":true}""");

        Assert.True(bridge.Identify(new byte[] { 0x24 }).NeedMoreData);
    }

    [Fact]
    public void Decode_MapsFramesFieldsAndIdentity()
    {
        (StdioParserBridge bridge, _, _) = Build(
            """
            {"ok":true,"consumed":6,"frames":[{"type":"Location","hex":"AABBCC","messageId":7,
             "requiresAck":true,"imei":"862464068456321",
             "fields":{"latitude":38.9072,"longitude":-77.0369,"speedKph":52,"ignitionOn":true,
                       "fixTimeUtc":"2026-08-21T12:00:00Z"}}]}
            """.ReplaceLineEndings(string.Empty));

        IReadOnlyList<DecodedMessage> frames = bridge.Decode(new byte[] { 1, 2, 3, 4, 5, 6 }, out int consumed);

        Assert.Equal(6, consumed);
        DecodedMessage frame = Assert.Single(frames);
        Assert.Equal(MessageType.Location, frame.MessageType);
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC }, frame.RawFrame);
        Assert.Equal(7, frame.ProtocolMessageId);
        Assert.True(frame.RequiresAck);
        Assert.Equal("862464068456321", frame.Identity?.Imei);
        Assert.Equal(38.9072, Assert.IsType<double>(frame.Fields["latitude"]));
        Assert.Equal(52L, Assert.IsType<long>(frame.Fields["speedKph"]));
        Assert.True(Assert.IsType<bool>(frame.Fields["ignitionOn"]));
    }

    [Fact]
    public void Decode_ClampsAnOverReportedConsumedCount()
    {
        // A child that over-reports would make the gateway discard bytes it never decoded, silently
        // swallowing the next frame.
        (StdioParserBridge bridge, _, _) = Build("""{"ok":true,"consumed":9999,"frames":[]}""");

        bridge.Decode(new byte[] { 1, 2, 3, 4 }, out int consumed);

        Assert.Equal(4, consumed);
    }

    [Fact]
    public void MalformedBytes_SurfaceAsAProtocolFault_NotAsBridgeDeath()
    {
        // "Your bytes are bad" is a per-connection outcome; the gateway drops that connection and
        // keeps serving every other tracker.
        (StdioParserBridge bridge, _, List<string> faults) =
            Build("""{"ok":false,"error":"bad crc","offset":12}""");

        ProtocolException error = Assert.Throws<ProtocolException>(() =>
        {
            bridge.Decode(new byte[] { 1, 2, 3 }, out _);
        });

        Assert.Contains("bad crc", error.Message);
        Assert.Equal(12, error.Offset);
        Assert.True(bridge.IsAvailable);
        Assert.Empty(faults);
    }

    [Fact]
    public void AChildThatDies_LatchesTheBridgeUnavailable()
    {
        (StdioParserBridge bridge, _, List<string> faults) = Build(); // stdout at EOF immediately

        Assert.False(bridge.Identify(new byte[] { 1, 2 }).IsMatch);
        Assert.False(bridge.IsAvailable);
        Assert.Single(faults);
    }

    [Fact]
    public void AChildThatAnswersWithGarbage_LatchesTheBridgeUnavailable()
    {
        // The stream position is no longer knowable, so continuing would pair the next request with
        // a stale response — one truck's frame answered with another truck's decode.
        (StdioParserBridge bridge, _, List<string> faults) = Build("this is not json");

        Assert.False(bridge.Identify(new byte[] { 1, 2 }).IsMatch);
        Assert.False(bridge.IsAvailable);
        Assert.Single(faults);
    }

    [Fact]
    public void OnceUnavailable_TheAdapterAboveItStopsClaimingStreams()
    {
        (StdioParserBridge bridge, _, _) = Build("garbage");
        var adapter = new PacificTrackAdapter(bridge);

        Assert.True(adapter.IsParserInstalled);
        adapter.TryIdentify(new byte[] { 1, 2 });          // trips the fault

        Assert.False(adapter.IsParserInstalled);
        Assert.False(adapter.TryIdentify(new byte[] { 1, 2 }).IsMatch);
    }

    [Fact]
    public void AHungChild_DoesNotHoldATrackerConnectionOpenForever()
    {
        var requests = new StringWriter();
        var faults = new List<string>();
        using var blocked = new BlockingReader();

        var bridge = new StdioParserBridge(
            requests, blocked, "vendor-3.2.1", TimeSpan.FromMilliseconds(150), faults.Add);

        Assert.False(bridge.Identify(new byte[] { 1, 2 }).IsMatch);
        Assert.False(bridge.IsAvailable);
        Assert.Contains(faults, f => f.Contains("did not respond"));
    }

    [Fact]
    public void Ack_RoundTripsTheVendorsBytes()
    {
        (StdioParserBridge bridge, StringWriter requests, _) =
            Build("""{"ok":true,"hex":"787805010001D9DC0D0A"}""");

        var frame = new DecodedMessage(
            MessageType.Login, new byte[] { 0xAA }, new Dictionary<string, object?>(),
            protocolMessageId: 1, requiresAck: true);

        byte[] ack = bridge.EncodeAck(frame);

        Assert.Equal(Convert.FromHexString("787805010001D9DC0D0A"), ack);
        Assert.Contains("\"op\":\"ack\"", requests.ToString());
        Assert.Contains("\"messageId\":1", requests.ToString());
    }

    /// <summary>A reader that never returns a line, standing in for a wedged child process.</summary>
    private sealed class BlockingReader : TextReader
    {
        private readonly ManualResetEventSlim _never = new(false);

        public override string? ReadLine()
        {
            _never.Wait();
            return null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _never.Set();
            base.Dispose(disposing);
        }
    }
}
