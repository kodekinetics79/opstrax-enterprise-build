using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Opstrax.Api.Services.Connectors;

namespace Opstrax.Tests;

public sealed class SamsaraFeedArrayTests
{
    [Fact]
    public void CanonicalArraysKeepEveryGpsEventWithoutBorrowingSiblingTimeSeries()
    {
        var first = Vehicle("synthetic-A", Gps(0), Gps(1));
        first["engineStates"] = JsonNode.Parse("""[{"time":"2026-01-01T00:00:00Z","value":"On"}]""");
        first["obdOdometerMeters"] = JsonNode.Parse("""[{"time":"2026-01-01T00:00:00Z","value":1609344}]""");
        var parsed = Parse(Page(first, Vehicle("synthetic-B", Gps(2))));
        var readings = parsed.GetProperty("Readings");
        Assert.Equal(3, readings.GetArrayLength());
        Assert.Equal(0, parsed.GetProperty("Rejected").GetInt32());
        Assert.Equal(new[] { "synthetic-A", "synthetic-A", "synthetic-B" },
            readings.EnumerateArray().Select(r => r.GetProperty("VehicleId").GetString()));
        Assert.All(readings.EnumerateArray(), r =>
        {
            Assert.Equal(JsonValueKind.Null, r.GetProperty("EngineState").ValueKind);
            Assert.Equal(JsonValueKind.Null, r.GetProperty("OdometerMiles").ValueKind);
        });
    }

    [Theory]
    [InlineData("Off")]
    [InlineData("On")]
    [InlineData("Idle")]
    public void OnlyGpsEventBoundDecorationsSupplyEngineAndOdometer(string state)
    {
        var gps = Gps(0);
        gps["decorations"] = new JsonObject
        {
            ["engineStates"] = new JsonObject { ["value"] = state },
            ["obdOdometerMeters"] = new JsonObject { ["value"] = 1609344 },
        };
        var readings = Parse(Page(Vehicle("synthetic-A", gps, Gps(1)))).GetProperty("Readings");
        Assert.Equal(2, readings.GetArrayLength());
        Assert.Equal(state, readings[0].GetProperty("EngineState").GetString());
        Assert.Equal(1000d, readings[0].GetProperty("OdometerMiles").GetDouble(), 6);
        Assert.Equal(JsonValueKind.Null, readings[1].GetProperty("EngineState").ValueKind);
        Assert.Equal(JsonValueKind.Null, readings[1].GetProperty("OdometerMiles").ValueKind);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("\"wrong\"")]
    [InlineData("true")]
    public void WrongGpsContainerFailsClosedInsteadOfConsumingCursor(string gpsJson)
    {
        var vehicle = new JsonObject { ["id"] = "synthetic-A", ["gps"] = JsonNode.Parse(gpsJson) };
        Assert.Contains("gps", Assert.Throws<InvalidDataException>(() => Parse(Page(vehicle))).Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("speedMilesPerHour", "SpeedMph", false)]
    [InlineData("speedMilesPerHour", "SpeedMph", true)]
    [InlineData("headingDegrees", "Heading", false)]
    [InlineData("headingDegrees", "Heading", true)]
    public void MissingOptionalMeasurementRemainsUnknownWithoutDroppingNeighbours(string field, string output, bool explicitNull)
    {
        var gps = Gps(1);
        if (explicitNull) gps[field] = null; else gps.Remove(field);
        var parsed = Parse(Page(Vehicle("synthetic-A", Gps(0), gps)));
        Assert.Equal(0, parsed.GetProperty("Rejected").GetInt32());
        var readings = parsed.GetProperty("Readings");
        Assert.Equal(2, readings.GetArrayLength());
        Assert.Equal(JsonValueKind.Number, readings[0].GetProperty(output).ValueKind);
        Assert.Equal(JsonValueKind.Null, readings[1].GetProperty(output).ValueKind);
    }

    [Fact]
    public void ExplicitZeroMeasurementsRemainKnownZero()
    {
        var gps = Gps(0);
        gps["speedMilesPerHour"] = 0; gps["headingDegrees"] = 0;
        var reading = Parse(Page(Vehicle("synthetic-A", gps))).GetProperty("Readings")[0];
        Assert.Equal(0d, reading.GetProperty("SpeedMph").GetDouble());
        Assert.Equal(0, reading.GetProperty("Heading").GetInt32());
    }

    [Theory]
    [InlineData("speedMilesPerHour", "\"40\"")]
    [InlineData("speedMilesPerHour", "false")]
    [InlineData("headingDegrees", "{}")]
    [InlineData("headingDegrees", "[]")]
    public void MalformedOptionalMeasurementIsNotSilentlyConvertedToUnknown(string field, string value)
    {
        var gps = Gps(0); gps[field] = JsonNode.Parse(value);
        Assert.Contains(field, Assert.Throws<InvalidDataException>(() => Parse(Page(Vehicle("synthetic-A", gps)))).Message);
    }

    [Fact]
    public void EmptyArraysAndLegitimateAuxiliaryOnlyUpdatesAreNotRejectedGps()
    {
        var engineOnly = new JsonObject { ["id"] = "synthetic-A", ["engineStates"] = new JsonArray() };
        var parsed = Parse(Page(engineOnly, Vehicle("synthetic-B")));
        Assert.Equal(0, parsed.GetProperty("Readings").GetArrayLength());
        Assert.Equal(0, parsed.GetProperty("Rejected").GetInt32());
    }

    [Fact]
    public void InvalidGpsReadingsAreCountedIndividuallyWithoutDroppingValidNeighbours()
    {
        var badLocation = Gps(1); badLocation["latitude"] = 999;
        var badTime = Gps(2); badTime["time"] = "not-a-time";
        var badSpeed = Gps(3); badSpeed["speedMilesPerHour"] = -1;
        var parsed = Parse(Page(Vehicle("synthetic-A", Gps(0), badLocation, badTime, badSpeed, Gps(4))));
        Assert.Equal(2, parsed.GetProperty("Readings").GetArrayLength());
        Assert.Equal(3, parsed.GetProperty("Rejected").GetInt32());
    }

    [Fact]
    public void NumericHeadingIsNormalizedToExistingWholeDegreeStorage()
    {
        var fractional = Gps(0); fractional["headingDegrees"] = 359.9;
        var north = Gps(1); north["headingDegrees"] = 360;
        var readings = Parse(Page(Vehicle("synthetic-A", fractional, north))).GetProperty("Readings");
        Assert.Equal(2, readings.GetArrayLength());
        Assert.Equal(359, readings[0].GetProperty("Heading").GetInt32());
        Assert.Equal(0, readings[1].GetProperty("Heading").GetInt32());
    }

    [Fact]
    public async Task PartialGpsWriterIsDefaultOffAndDoesNotConsumeAPageBeforeReaderApproval()
    {
        var gps = Gps(0); gps.Remove("speedMilesPerHour");
        // No Database is registered: reaching a DB scope would fail this specific
        // deployment-guard assertion. Parsing remains supported independently.
        var connector = SamsaraResponseBoundsTests.Connector(_ =>
            Task.FromResult(SamsaraResponseBoundsTests.Json(Page(Vehicle("synthetic-A", gps)).ToJsonString())));
        using var body = SamsaraResponseBoundsTests.OperationBody("retained-before-opt-in");
        var result = await connector.RunActionAsync("sync", new Dictionary<string, string?> { ["apiToken"] = "synthetic-token" }, body.RootElement, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("Samsara:AllowPartialGpsMeasurements", result.Message);
        Assert.Equal(0, result.Details!["pagesCommitted"]);
        Assert.Equal(0, result.Details["positionsWritten"]);
        Assert.Null(result.Details["nextCursor"]);
    }

    [Fact]
    public async Task SyncKeepsExistingQueryProfilePairedWithEncodedResumeCursor()
    {
        string? query = null;
        var connector = SamsaraResponseBoundsTests.Connector(request =>
        {
            query = request.RequestUri!.Query;
            return Task.FromResult(SamsaraResponseBoundsTests.Json(Page().ToJsonString()));
        });
        using var body = SamsaraResponseBoundsTests.OperationBody("retained/&cursor");
        var result = await connector.RunActionAsync("sync", new Dictionary<string, string?> { ["apiToken"] = "synthetic-token" },
            body.RootElement, CancellationToken.None);
        Assert.True(result.Success, result.Message);
        Assert.Equal("?types=gps,engineStates,obdOdometerMeters&after=retained%2F%26cursor", query);
    }

    internal static JsonObject Gps(int seconds) => new()
    {
        ["time"] = DateTimeOffset.UtcNow.AddMinutes(-2).AddSeconds(seconds).ToString("O"),
        ["latitude"] = 34.05, ["longitude"] = -118.24,
        ["speedMilesPerHour"] = 40, ["headingDegrees"] = 90,
    };

    internal static JsonObject Vehicle(string id, params JsonObject[] gps) => new()
    {
        ["id"] = id, ["gps"] = new JsonArray(gps.Select(g => (JsonNode)g).ToArray()),
    };

    internal static JsonObject Page(params JsonObject[] vehicles) => new()
    {
        ["data"] = new JsonArray(vehicles.Select(v => (JsonNode)v).ToArray()),
        ["pagination"] = new JsonObject { ["endCursor"] = "retained-cursor", ["hasNextPage"] = false },
    };

    private static JsonElement Parse(JsonObject page)
    {
        using var doc = JsonDocument.Parse(page.ToJsonString());
        try
        {
            // Keep the production parser private while exercising its unchanged pre-fix
            // behavior and all normalized fields, alongside public-entry PostgreSQL tests.
            var result = typeof(SamsaraSync).GetMethod("ParseFeed", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [doc.RootElement]);
            return JsonSerializer.SerializeToElement(result, result!.GetType());
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}
