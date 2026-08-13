namespace Opstrax.Tests;

public sealed class FleetIdentityBackboneContractTests
{
    [Fact]
    public void Stage80OwnsTemporalInstallationIdentifiersAndDepartureEvidence()
    {
        var sql = Read("database", "migrations", "2026_08_14_stage80_fleet_identity_backbone.sql");
        Assert.Contains("stage80_vin_is_valid", sql, StringComparison.Ordinal);
        Assert.Contains("duplicate_normalized_device_serial", sql, StringComparison.Ordinal);
        Assert.Contains("duplicate_normalized_imei", sql, StringComparison.Ordinal);
        Assert.Contains("ex_stage80_device_installation_period", sql, StringComparison.Ordinal);
        Assert.Contains("installation_id BIGINT", sql, StringComparison.Ordinal);
        Assert.Contains("assignment_id BIGINT", sql, StringComparison.Ordinal);
        Assert.Contains("trip_id BIGINT", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS notes TEXT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE location_events ADD COLUMN IF NOT EXISTS battery_voltage NUMERIC(10, 3) NULL", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE latest_vehicle_positions ADD COLUMN IF NOT EXISTS address TEXT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("vehicle_confirmed_at", sql, StringComparison.Ordinal);
        Assert.Contains("pretrip_dvir_id", sql, StringComparison.Ordinal);
        Assert.Contains("assigned_by_user_id", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE dvir_reports ADD COLUMN IF NOT EXISTS trip_id", sql, StringComparison.Ordinal);
        Assert.Contains("device_installation_quarantine", sql, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT,INSERT,UPDATE ON TABLE device_installation_quarantine TO opstrax_system", sql, StringComparison.Ordinal);
        Assert.Contains("GRANT USAGE,SELECT ON SEQUENCE device_installation_quarantine_id_seq TO opstrax_system", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceProvisionColumnsAreOwnedByTheProtectedMigrationChain()
    {
        var endpoint = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var provision = Block(endpoint, "private static async Task<IResult> DeviceProvision(", "private static async Task<IResult> DeviceRotateSecret(");
        Assert.Contains("firmware_version, notes, api_key_hash", provision, StringComparison.Ordinal);

        var runner = Read("tools", "apply-neon-predeploy-migrations.sh");
        Assert.Contains("('eld_devices','notes')", runner, StringComparison.Ordinal);
        Assert.Contains("('location_events','battery_voltage')", runner, StringComparison.Ordinal);
        Assert.Contains("('latest_vehicle_positions','address')", runner, StringComparison.Ordinal);
    }

    [Fact]
    public void SmartAssignmentAcceptanceCreatesCanonicalDispatchBeforeConfirmation()
    {
        var source = Read("backend-dotnet", "Services", "Stage9OperationalFoundationService.cs");
        var accept = Block(source, "AcceptSmartAssignmentAsync", "RejectSmartAssignmentAsync");
        AssertOrdered(accept, "INSERT INTO dispatch_assignments", "INSERT INTO assignment_confirmations");
        Assert.Contains("activeConflict", accept, StringComparison.Ordinal);
        Assert.Contains("dispatch.assignment.created", accept, StringComparison.Ordinal);
        Assert.Contains("dispatch_assignment_id", accept, StringComparison.Ordinal);
    }

    [Fact]
    public void DriverDepartureRequiresVehicleConfirmationAndSafePretripDvir()
    {
        var source = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        Assert.Contains("/api/driver/assignments/{id:long}/confirm-vehicle", source, StringComparison.Ordinal);
        var current = Block(source, "private static async Task<IResult> DriverCurrentAssignment(", "private static async Task<IResult> DriverAcceptAssignment(");
        Assert.Contains("customer.name customer_name", current, StringComparison.Ordinal);
        Assert.Contains("customer.company_id=da.company_id", current, StringComparison.Ordinal);
        Assert.DoesNotContain("j.customer_name", current, StringComparison.Ordinal);
        var confirm = Block(source, "private static async Task<IResult> DriverConfirmVehicle(", "private static async Task<IResult> DriverUpdateStatus(");
        Assert.Contains("FixedTimeEquals", confirm, StringComparison.Ordinal);
        Assert.Contains("vehicle_confirmed_by_driver_id", confirm, StringComparison.Ordinal);
        var status = Block(source, "private static async Task<IResult> DriverUpdateStatus(", "private static async Task<IResult> DriverReportException(");
        Assert.Contains("Confirm the exact assigned vehicle before departure", status, StringComparison.Ordinal);
        Assert.Contains("safe_to_operate=TRUE", status, StringComparison.Ordinal);
        Assert.Contains("driver_signature_status", status, StringComparison.Ordinal);
        Assert.Contains("pretrip_dvir_id", status, StringComparison.Ordinal);
    }

    [Fact]
    public void OperatorUiUsesGovernedInstallationAndDriverConfirmationApis()
    {
        var devices = Read("frontend", "src", "services", "telematicsService.ts");
        var driver = Read("frontend", "src", "services", "driverApi.ts");
        var driverPage = Read("frontend", "src", "pages", "driver", "DriverAssignmentPage.tsx");
        Assert.Contains("/installations/transfer", devices, StringComparison.Ordinal);
        Assert.Contains("/confirm-vehicle", driver, StringComparison.Ordinal);
        Assert.Contains("Complete pre-trip DVIR", driverPage, StringComparison.Ordinal);
        Assert.Contains("latestPretripDriverSignatureStatus", driverPage, StringComparison.Ordinal);
        Assert.Contains("Driver signature required before departure", driverPage, StringComparison.Ordinal);
        Assert.Contains("vehicleConfirmed", driverPage, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }

    private static string Block(string source, string start, string end)
    {
        var begin = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(begin >= 0, $"Missing marker: {start}");
        var finish = source.IndexOf(end, begin + start.Length, StringComparison.Ordinal);
        Assert.True(finish > begin, $"Missing marker: {end}");
        return source[begin..finish];
    }

    private static void AssertOrdered(string source, string first, string second)
    {
        var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
        var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
        Assert.True(firstIndex >= 0, $"Missing marker: {first}");
        Assert.True(secondIndex > firstIndex, $"Expected '{first}' before '{second}'");
    }
}
