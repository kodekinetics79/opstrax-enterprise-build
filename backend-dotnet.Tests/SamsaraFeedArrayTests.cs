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
    [InlineData("speedMilesPerHour")]
    [InlineData("headingDegrees")]
    public void ValidButUnrepresentableOptionalMeasurementPausesPageInsteadOfInventingZero(string field)
    {
        var gps = Gps(1);
        gps.Remove(field);
        var error = Assert.Throws<InvalidDataException>(() => Parse(Page(Vehicle("synthetic-A", Gps(0), gps))));
        Assert.Contains(field, error.Message, StringComparison.Ordinal);
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
