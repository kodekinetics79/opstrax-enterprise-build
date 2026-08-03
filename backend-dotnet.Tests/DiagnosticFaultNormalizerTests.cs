using System.Text.Json;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class DiagnosticFaultNormalizerTests
{
    [Fact]
    public void RawDm1_DecodesEveryDtc_DerivesCritical_AndIgnoresSubmittedSeverity()
    {
        var payload = Convert.ToBase64String([0b00_00_01_00, 0, 1, 0, 0, 1, 0, 1, 0x1F, 2]);
        var request = new DiagnosticFaultIngestRequest(
            Severity: "Info", SourceEventId: "dm1-frame-1", Protocol: "J1939",
            Controller: "engine", SourceAddress: 7, Pgn: 65226, RawPayloadBase64: payload);

        Assert.True(DiagnosticFaultNormalizer.TryNormalize(request, out var batch, out var error), error);
        Assert.NotNull(batch);
        Assert.True(batch!.MutatesProjection);
        Assert.Collection(batch.Dtcs,
            first =>
            {
                Assert.Equal("Critical", first.Severity);
                Assert.Equal("SPN-1-FMI-0", first.Code);
                Assert.Equal("J1939:ENGINE:SPN:1:FMI:0", first.CanonicalIdentity);
                Assert.Equal(0, first.Ordinal);
            },
            second =>
            {
                Assert.Equal("Critical", second.Severity);
                Assert.Equal("J1939:ENGINE:SPN:256:FMI:31", second.CanonicalIdentity);
                Assert.Equal(1, second.Ordinal);
            });
    }

    [Fact]
    public void Dm2_IsHistoricalEvidence_NotClearOrReactivation()
    {
        var request = new DiagnosticFaultIngestRequest(
            SourceEventId: "dm2-frame-1", Protocol: "J1939", SourceAddress: 3,
            Pgn: 65227, RawPayloadBase64: Convert.ToBase64String([0, 0, 1, 0, 0, 1]));

        Assert.True(DiagnosticFaultNormalizer.TryNormalize(request, out var batch, out var error), error);
        Assert.False(batch!.MutatesProjection);
        Assert.False(batch.ClearsProjection);
        Assert.Equal("J1939:SA:03:SPN:1:FMI:0", Assert.Single(batch.Dtcs).CanonicalIdentity);
    }

    [Fact]
    public void NormalizedDm1_RequiresCompleteLampState_AndRejectsClientClear()
    {
        var incomplete = JsonDocument.Parse("{\"redStop\":\"On\"}").RootElement.Clone();
        var request = new DiagnosticFaultIngestRequest(
            SourceEventId: "normalized-1", Protocol: "J1939", Pgn: 65226,
            LampStatus: incomplete, Dtcs: [new DiagnosticDtcInput(100, 4)]);
        Assert.False(DiagnosticFaultNormalizer.TryNormalize(request, out _, out var lampError));
        Assert.Contains("all eight", lampError);

        request = request with { Cleared = true };
        Assert.False(DiagnosticFaultNormalizer.TryNormalize(request, out _, out var clearError));
        Assert.Contains("not a clear command", clearError);
    }
}
