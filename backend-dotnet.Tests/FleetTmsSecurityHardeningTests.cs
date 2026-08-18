using Opstrax.Api.Controllers;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class FleetTmsSecurityHardeningTests
{
    [Fact]
    public void PublicTrackingOnlySelectsVerifiedProofsAndAssets()
    {
        var source = ReadSource("backend-dotnet", "Controllers", "FleetTmsEndpoints.cs");
        var publicSection = source[source.IndexOf("// ── Public tracking", StringComparison.Ordinal)..];

        Assert.Equal(3, Count(publicSection, "status='Verified'"));
        Assert.DoesNotContain("status <> 'Rejected'", publicSection, StringComparison.Ordinal);
    }

    [Fact]
    public void ColdChainReportGetIsReadOnlyOnCacheMiss()
    {
        var source = ReadSource("backend-dotnet", "Controllers", "FleetTmsColdChainEndpoints.cs");
        var start = source.IndexOf("private static async Task<IResult> ColdChainReport(", StringComparison.Ordinal);
        var end = source.IndexOf("// ── Assets", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];

        Assert.DoesNotContain("InsertAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO", method, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id = 0L", method, StringComparison.Ordinal);
        Assert.Contains("summaryJson", method, StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceValidationRejectsMalformedJsonAndUnsafeMeasurements()
    {
        var malformed = Device(metadataJson: "{not-json}");
        var unsafeBattery = Device(battery: 101m);
        var unsafeTemperature = Device(temperature: -101m);

        Assert.Contains("JSON", FleetTmsColdChainEndpoints.ValidateDeviceRequest(malformed), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Battery", FleetTmsColdChainEndpoints.ValidateDeviceRequest(unsafeBattery), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Temperature", FleetTmsColdChainEndpoints.ValidateDeviceRequest(unsafeTemperature), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadingValidationRejectsOutOfRangeTelemetry()
    {
        var latitude = Reading(latitude: 90.01m);
        var longitude = Reading(longitude: -180.01m);
        var humidity = Reading(humidity: -1m);

        Assert.Contains("Latitude", FleetTmsColdChainEndpoints.ValidateReadingRequest(latitude), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Longitude", FleetTmsColdChainEndpoints.ValidateReadingRequest(longitude), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Humidity", FleetTmsColdChainEndpoints.ValidateReadingRequest(humidity), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source", FleetTmsColdChainEndpoints.ValidateReadingRequest(Reading() with { Source = "Invented" }), StringComparison.OrdinalIgnoreCase);
        Assert.Null(FleetTmsColdChainEndpoints.ValidateReadingRequest(Reading() with { Source = "Gateway" }));
    }

    [Fact]
    public void ReadingSourceIsCanonicalAndUnknownSourcesFailClosed()
    {
        Assert.Equal("Sensor", FleetTmsColdChainFoundationService.NormalizeReadingSource(null));
        Assert.Equal("Gateway", FleetTmsColdChainFoundationService.NormalizeReadingSource(" gateway "));
        Assert.Throws<InvalidOperationException>(() => FleetTmsColdChainFoundationService.NormalizeReadingSource("estimated"));
    }

    [Fact]
    public void ColdChainRuntimeDoesNotTrustCallerStatusOrManufactureBatteryEvidence()
    {
        var source = ReadSource("backend-dotnet", "Services", "FleetTmsColdChainFoundationService.cs");
        Assert.Contains("var status = isBreach ? \"Breach\" : \"Normal\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("string.IsNullOrWhiteSpace(req.Status)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("THEN 98", source, StringComparison.Ordinal);
        Assert.DoesNotContain("battery_percent=", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceRegistrationKeepsUnobservedTelemetryNull()
    {
        var endpoints = ReadSource("backend-dotnet", "Controllers", "FleetTmsColdChainEndpoints.cs");
        Assert.Contains("(object?)req.LastReportedTemperatureCelsius ?? DBNull.Value", endpoints, StringComparison.Ordinal);
        Assert.Contains("(object?)req.BatteryPercent ?? DBNull.Value", endpoints, StringComparison.Ordinal);
        Assert.Contains("(object?)req.LastPingAtUtc ?? DBNull.Value", endpoints, StringComparison.Ordinal);
        Assert.DoesNotContain("req.BatteryPercent ?? 0m", endpoints, StringComparison.Ordinal);
    }

    [Fact]
    public void PolicyValidationRejectsImpossibleTemperatureHumidityAndStateContracts()
    {
        static Dictionary<string, object?> Policy(params (string Key, object? Value)[] values)
            => values.ToDictionary(v => v.Key, v => v.Value);

        Assert.Contains("lower than", FleetTmsColdChainEndpoints.ValidatePolicyRequest(
            Policy(("minCelsius", 8m), ("maxCelsius", 2m)))!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("humidity", FleetTmsColdChainEndpoints.ValidatePolicyRequest(
            Policy(("humidityMinPercent", -1m), ("humidityMaxPercent", 80m)))!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot exceed", FleetTmsColdChainEndpoints.ValidatePolicyRequest(
            Policy(("humidityMinPercent", 90m), ("humidityMaxPercent", 80m)))!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scope key", FleetTmsColdChainEndpoints.ValidatePolicyRequest(
            Policy(("scopeType", "zone")))!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("severity", FleetTmsColdChainEndpoints.ValidatePolicyRequest(
            Policy(("severity", "Catastrophic")))!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("JSON", FleetTmsColdChainEndpoints.ValidatePolicyRequest(
            Policy(("metadataJson", "[1,2,3]")))!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(FleetTmsColdChainEndpoints.ValidatePolicyRequest(Policy(
            ("policyCode", "CHILLED"), ("scopeType", "zone"), ("scopeKey", "CHILL"),
            ("minCelsius", 2m), ("maxCelsius", 8m),
            ("humidityMinPercent", 30m), ("humidityMaxPercent", 80m),
            ("severity", "Critical"), ("status", "Active"), ("metadataJson", "{}"))));
    }

    [Fact]
    public void AssetValidationRejectsCorruptQuantityAndState()
    {
        var negative = new AssetRequest(1, "TAG", "Asset", "Available", "Dock", "Good", true, -1, "Each", null, null);
        var unknownState = negative with { Quantity = 1, Status = "MadeUpState" };

        Assert.Contains("quantity", FleetTmsColdChainEndpoints.ValidateAssetRequest(negative, true), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("status", FleetTmsColdChainEndpoints.ValidateAssetRequest(unknownState, true), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScanValidationRejectsBlankIdentifiersAndInvalidReferences()
    {
        var blankBarcode = new AssetScanRequest("Barcode", null, null, " ", null, null, null, null, null, null);
        var blankRfid = blankBarcode with { Kind = "RFID" };
        var invalidAsset = blankBarcode with { ScannedValue = "ABC-1", AssetId = -1 };

        Assert.Contains("required", FleetTmsColdChainEndpoints.ValidateAssetScanRequest(blankBarcode), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("required", FleetTmsColdChainEndpoints.ValidateAssetScanRequest(blankRfid), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("positive", FleetTmsColdChainEndpoints.ValidateAssetScanRequest(invalidAsset), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RelationshipWritesVerifyTenantOwnedReferencesBeforeInsert()
    {
        var source = ReadSource("backend-dotnet", "Controllers", "FleetTmsColdChainEndpoints.cs");

        Assert.Contains("fleet_tms_temperature_zones WHERE company_id=@companyId AND id=@id", source, StringComparison.Ordinal);
        Assert.Contains("SharedConfigScope(http)", source, StringComparison.Ordinal);
        Assert.Contains("Row(db, \"fleet_tms_shipments\", companyId, req.ShipmentId.Value", source, StringComparison.Ordinal);
        Assert.Contains("Row(db, \"carriers\", companyId, req.CarrierId.Value", source, StringComparison.Ordinal);
        Assert.Contains("OwnedRow(db, http, \"fleet_tms_assets\", req.AssetId.Value", source, StringComparison.Ordinal);
        Assert.Contains("lower(asset_tag)=lower(@tag)", source, StringComparison.Ordinal);
        Assert.Contains("Unknown asset scan identifier for this tenant", source, StringComparison.Ordinal);
    }

    [Fact]
    public void IntegrityMigrationsRepairDriftAndVerifyExactIndexesBeforeLedgering()
    {
        var stage54 = ReadSource("database", "migrations", "2026_07_30_stage54_cold_chain_device_integrity.sql");
        var stage56 = ReadSource("database", "migrations", "2026_07_30_stage56_asset_type_integrity.sql");

        Assert.Contains("DROP INDEX IF EXISTS uq_ftms_tdev_tenant_code_norm", stage54, StringComparison.Ordinal);
        Assert.Contains("DROP INDEX IF EXISTS uq_ftms_tdev_branch_idem", stage54, StringComparison.Ordinal);
        Assert.Contains("idx.indnkeyatts <> 2 OR idx.indnatts <> 2", stage54, StringComparison.Ordinal);
        Assert.Contains("idx.indnkeyatts <> 3 OR idx.indnatts <> 3", stage54, StringComparison.Ordinal);
        Assert.True(stage54.IndexOf("$cold_chain_device_verify$", StringComparison.Ordinal)
                    < stage54.IndexOf("INSERT INTO schema_migrations", StringComparison.Ordinal));

        Assert.Contains("DROP INDEX IF EXISTS uq_ftms_atype_tenant_code_norm", stage56, StringComparison.Ordinal);
        Assert.Contains("idx.indnkeyatts <> 2 OR idx.indnatts <> 2", stage56, StringComparison.Ordinal);
        Assert.True(stage56.IndexOf("$asset_type_verify$", StringComparison.Ordinal)
                    < stage56.IndexOf("INSERT INTO schema_migrations", StringComparison.Ordinal));
    }

    private static TemperatureDeviceRequest Device(decimal? temperature = 0, decimal? battery = 50, string? metadataJson = "{}")
        => new("DEV-1", "Trailer sensor", null, null, "TRK-1", "Active", temperature, battery, null, null,
            null, null, null, null, null, metadataJson);

    private static TemperatureReadingRequest Reading(decimal? humidity = 50, decimal? latitude = 0, decimal? longitude = 0)
        => new(1, null, null, 4, humidity, latitude, longitude, "Sensor", "Normal", null,
            null, null, null, null, null, "{}");

    private static int Count(string source, string value)
    {
        var count = 0;
        for (var index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length)
            count++;
        return count;
    }

    private static string ReadSource(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }
}
