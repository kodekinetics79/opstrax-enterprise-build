using System.Text;
using System.Text.Json;
using Opstrax.Api.Services.Connectors;

namespace Opstrax.Tests;

public sealed class SamsaraCameraSafetyParserTests
{
    private static readonly DateTimeOffset RetrievedAt =
        new(2026, 9, 7, 15, 30, 0, TimeSpan.Zero);

    [Fact]
    public void ParsePage_RecordsExternalIdentitiesLabelsAndExactEventPayload()
    {
        const string eventJson = """{"id":"evt-1","asset":{"id":"asset-7"},"driverId":"driver-3","startMs":1788793200000,"updatedAtTime":"2026-09-07T15:10:00Z","behaviorLabels":[{"name":"Braking"},"Crash"],"mediaUrls":["https://signed.example/never-store"]}""";
        using var document = JsonDocument.Parse(
            $$$"""{"data":[{{{eventJson}}}],"pagination":{"endCursor":"next-1","hasNextPage":true}}""");

        var page = SamsaraCameraSafetySync.ParsePage(
            document.RootElement, "opstrax-integration:23", RetrievedAt);

        var parsed = Assert.Single(page.Events);
        Assert.Equal("next-1", page.EndCursor);
        Assert.True(page.HasNextPage);
        Assert.Equal("samsara", parsed.Envelope.ProviderKey);
        Assert.Equal("opstrax-integration:23", parsed.Envelope.ProviderAccountReference);
        Assert.Equal("evt-1", parsed.Envelope.ProviderEventId);
        Assert.Equal("asset-7", parsed.Envelope.VehicleExternalId);
        Assert.Equal("driver-3", parsed.Envelope.DriverExternalId);
        Assert.Equal("Samsara:Braking|Crash", parsed.Envelope.EventType);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1788793200000), parsed.Envelope.OccurredAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 15, 10, 0, TimeSpan.Zero), parsed.Envelope.ProviderReceivedAtUtc);
        Assert.Empty(parsed.Envelope.MediaReferences);
        Assert.Equal(eventJson, Encoding.UTF8.GetString(parsed.AuthenticatedPayload.Span));
        Assert.DoesNotContain("signed.example", parsed.Envelope.MediaReferences.Select(m => m.ProviderMediaId));
    }

    [Fact]
    public void ParsePage_AcceptsEquivalentModernAndCompatibilityAssetIdentities()
    {
        using var document = JsonDocument.Parse("""
            {"data":[{
              "id":"evt-1",
              "assetId":"asset-7",
              "asset":{"id":"asset-7"},
              "vehicleId":"asset-7",
              "vehicle":{"id":"asset-7"},
              "startMs":1788793200000,
              "updatedAtTime":"2026-09-07T15:10:00Z"
            }],"pagination":{"endCursor":"end-1","hasNextPage":false}}
            """);

        var page = SamsaraCameraSafetySync.ParsePage(
            document.RootElement, "opstrax-integration:23", RetrievedAt);

        Assert.Equal("asset-7", Assert.Single(page.Events).Envelope.VehicleExternalId);
        Assert.Equal("SamsaraSafetyEvent", page.Events[0].Envelope.EventType);
    }

    [Fact]
    public void ParsePage_RejectsConflictingAssetIdentitiesWithoutAdvancingCursor()
    {
        using var document = JsonDocument.Parse("""
            {"data":[{
              "id":"evt-1","assetId":"asset-7","vehicle":{"id":"asset-8"},
              "startMs":1788793200000,"updatedAtTime":"2026-09-07T15:10:00Z"
            }],"pagination":{"endCursor":"next-1","hasNextPage":true}}
            """);

        var failure = Assert.Throws<InvalidDataException>(() =>
            SamsaraCameraSafetySync.ParsePage(
                document.RootElement, "opstrax-integration:23", RetrievedAt));

        Assert.Contains("identities conflict", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"pagination\":{\"endCursor\":\"\",\"hasNextPage\":false}}", "data array")]
    [InlineData("{\"data\":[]}", "pagination")]
    [InlineData("{\"data\":[],\"pagination\":{\"endCursor\":\"\",\"hasNextPage\":true}}", "cannot be empty")]
    [InlineData("{\"data\":[],\"pagination\":{\"endCursor\":\"\",\"hasNextPage\":false}}", "unending safety-events stream")]
    public void ParsePage_RejectsIncompleteProviderEnvelope(string json, string expected)
    {
        using var document = JsonDocument.Parse(json);

        var failure = Assert.Throws<InvalidDataException>(() =>
            SamsaraCameraSafetySync.ParsePage(
                document.RootElement, "opstrax-integration:23", RetrievedAt));

        Assert.Contains(expected, failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("\"1788793200000\"")]
    [InlineData("1788793200000.5")]
    [InlineData("null")]
    public void ParsePage_RejectsNonIntegerEventTime(string startMs)
    {
        using var document = JsonDocument.Parse(
            $$$"""{"data":[{"id":"evt-1","assetId":"asset-7","startMs":{{{startMs}}},"updatedAtTime":"2026-09-07T15:10:00Z"}],"pagination":{"endCursor":"end-1","hasNextPage":false}}""");

        var failure = Assert.Throws<InvalidDataException>(() =>
            SamsaraCameraSafetySync.ParsePage(
                document.RootElement, "opstrax-integration:23", RetrievedAt));

        Assert.Contains("Unix millisecond", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsePage_RejectsDuplicateEventIdentityWithinPage()
    {
        using var document = JsonDocument.Parse("""
            {"data":[
              {"id":"evt-1","assetId":"asset-7","startMs":1788793200000,"updatedAtTime":"2026-09-07T15:10:00Z"},
              {"id":"evt-1","assetId":"asset-7","startMs":1788793200000,"updatedAtTime":"2026-09-07T15:10:00Z"}
            ],"pagination":{"endCursor":"end-1","hasNextPage":false}}
            """);

        var failure = Assert.Throws<InvalidDataException>(() =>
            SamsaraCameraSafetySync.ParsePage(
                document.RootElement, "opstrax-integration:23", RetrievedAt));

        Assert.Contains("duplicate", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParsePage_RejectsEventTimeAfterReceiptTolerance()
    {
        using var document = JsonDocument.Parse("""
            {"data":[{"id":"evt-1","assetId":"asset-7","startMs":1788797100000,"updatedAtTime":"2026-09-07T15:31:00Z"}],
             "pagination":{"endCursor":"end-1","hasNextPage":false}}
            """);

        var failure = Assert.Throws<InvalidDataException>(() =>
            SamsaraCameraSafetySync.ParsePage(
                document.RootElement, "opstrax-integration:23", RetrievedAt));

        Assert.Contains("after the provider receipt", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2026-09-07T11:10:00-04:00")]
    [InlineData("not-a-time")]
    public void ParsePage_RejectsUnstableOrNonUtcProviderUpdateTime(string updatedAtTime)
    {
        using var document = JsonDocument.Parse(
            $$$"""{"data":[{"id":"evt-1","assetId":"asset-7","startMs":1788793200000,"updatedAtTime":"{{{updatedAtTime}}}"}],"pagination":{"endCursor":"end-1","hasNextPage":false}}""");

        var failure = Assert.Throws<InvalidDataException>(() =>
            SamsaraCameraSafetySync.ParsePage(
                document.RootElement, "opstrax-integration:23", RetrievedAt));

        Assert.Contains("RFC 3339 UTC", failure.Message, StringComparison.Ordinal);
    }
}
