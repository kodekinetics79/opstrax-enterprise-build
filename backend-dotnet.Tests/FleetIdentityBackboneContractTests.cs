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
        Assert.Contains("ex_stage80_vehicle_primary_role_period", sql, StringComparison.Ordinal);
        Assert.Contains("(LOWER(BTRIM(device_role))) WITH =", sql, StringComparison.Ordinal);
        Assert.Contains("status IN ('Installed','Verified','Removed')", sql, StringComparison.Ordinal);
        Assert.Contains("stage80_preserve_installation_history", sql, StringComparison.Ordinal);
        Assert.Contains("stage80_preserve_installation_evidence", sql, StringComparison.Ordinal);
        Assert.Contains("fk_stage80_evidence_installation", sql, StringComparison.Ordinal);
        Assert.Contains("installation_id BIGINT", sql, StringComparison.Ordinal);
        Assert.Contains("assignment_id BIGINT", sql, StringComparison.Ordinal);
        Assert.Contains("trip_id BIGINT", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE eld_devices ADD COLUMN IF NOT EXISTS notes TEXT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE location_events ADD COLUMN IF NOT EXISTS battery_voltage NUMERIC(10, 3) NULL", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE location_events ADD COLUMN IF NOT EXISTS engine_status VARCHAR(40) NULL", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE latest_vehicle_positions ADD COLUMN IF NOT EXISTS address TEXT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE latest_vehicle_positions ADD COLUMN IF NOT EXISTS battery_voltage NUMERIC(6, 2) NULL", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER COLUMN last_reported_temperature_celsius DROP NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS measured_humidity NUMERIC(6, 2) NULL", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE customers ADD COLUMN IF NOT EXISTS health_computed_at TIMESTAMPTZ NULL", sql, StringComparison.Ordinal);
        Assert.Contains("vehicle_confirmed_at", sql, StringComparison.Ordinal);
        Assert.Contains("pretrip_dvir_id", sql, StringComparison.Ordinal);
        Assert.Contains("assigned_by_user_id", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE dvir_reports ADD COLUMN IF NOT EXISTS trip_id", sql, StringComparison.Ordinal);
        Assert.Contains("device_installation_quarantine", sql, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT,INSERT,UPDATE ON TABLE device_installation_quarantine TO opstrax_system", sql, StringComparison.Ordinal);
        Assert.Contains("GRANT USAGE,SELECT ON SEQUENCE device_installation_quarantine_id_seq TO opstrax_system", sql, StringComparison.Ordinal);
        Assert.Contains("q.reason_code='ambiguous_device_identifier'", sql, StringComparison.Ordinal);
        Assert.Contains("stage80_hold_ambiguous_device_identifier", sql, StringComparison.Ordinal);
        Assert.Contains("NEW.device_state:='Quarantined'", sql, StringComparison.Ordinal);
        Assert.Contains("MAX(history.effective_to)", sql, StringComparison.Ordinal);
        Assert.Contains("i.effective_to IS NULL AND i.status IN ('Installed','Verified')", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void TerminalAclKeepsCompatibilityProjectionDatabaseOwned()
    {
        var sql = Read("database", "migrations", "2026_08_11_stage76_telematics_security_hardening.sql");
        Assert.DoesNotContain("'provider','vehicle_id','driver_id'", sql, StringComparison.Ordinal);
        Assert.Contains("has_column_privilege('opstrax_app','eld_devices','vehicle_id','UPDATE')", sql, StringComparison.Ordinal);
        Assert.Contains("has_column_privilege('opstrax_app','eld_devices','driver_id','UPDATE')", sql, StringComparison.Ordinal);
        Assert.Contains("SECURITY DEFINER", Read("database", "migrations", "2026_08_14_stage80_fleet_identity_backbone.sql"), StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceProvisionColumnsAreOwnedByTheProtectedMigrationChain()
    {
        var endpoint = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var provision = Block(endpoint, "private static async Task<IResult> DeviceProvision(", "private static async Task<IResult> DeviceRotateSecret(");
        Assert.Contains("firmware_version, notes, api_key_hash", provision, StringComparison.Ordinal);
        Assert.Contains("deviceCategory is required", provision, StringComparison.Ordinal);
        Assert.Contains("@category", provision, StringComparison.Ordinal);

        var runner = Read("tools", "apply-neon-predeploy-migrations.sh");
        Assert.Contains("('eld_devices','notes')", runner, StringComparison.Ordinal);
        Assert.Contains("('location_events','battery_voltage')", runner, StringComparison.Ordinal);
        Assert.Contains("('location_events','engine_status')", runner, StringComparison.Ordinal);
        Assert.Contains("('latest_vehicle_positions','address')", runner, StringComparison.Ordinal);
        Assert.Contains("('latest_vehicle_positions','battery_voltage')", runner, StringComparison.Ordinal);

        var rotation = Block(endpoint, "private static async Task<IResult> DeviceRotateSecret(", "private static async Task<IResult> DeviceRevoke(");
        Assert.Contains("revoked_at IS NULL", rotation, StringComparison.Ordinal);
        Assert.Contains("row_version=row_version+1", rotation, StringComparison.Ordinal);
        Assert.Contains("CacheControl = \"no-store\"", rotation, StringComparison.Ordinal);
        Assert.DoesNotContain("api_key_hash = newApiKey", rotation, StringComparison.Ordinal);
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
        Assert.Contains("s != \"accepted\" || currentStatus == \"exception\"", current, StringComparison.Ordinal);
        Assert.Contains("new[] { \"assigned\",\"accepted\"", current, StringComparison.Ordinal);
        var confirm = Block(source, "private static async Task<IResult> DriverConfirmVehicle(", "private static async Task<IResult> DriverUpdateStatus(");
        Assert.Contains("FixedTimeEquals", confirm, StringComparison.Ordinal);
        Assert.Contains("vehicle_confirmed_by_driver_id", confirm, StringComparison.Ordinal);
        var status = Block(source, "private static async Task<IResult> DriverUpdateStatus(", "private static async Task<IResult> DriverReportException(");
        Assert.Contains("Confirm the exact assigned vehicle before departure", status, StringComparison.Ordinal);
        Assert.Contains("vehicle_out_of_service", status, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE OF da,v", status, StringComparison.Ordinal);
        Assert.Contains("AcquireDvirDepartureSafetyLockAsync", status, StringComparison.Ordinal);
        Assert.Contains("assigned vehicle is out of service", status, StringComparison.Ordinal);
        Assert.Contains("SELECT id,safe_to_operate,driver_signature_status", status, StringComparison.Ordinal);
        Assert.DoesNotContain("AND safe_to_operate=TRUE", status, StringComparison.Ordinal);
        Assert.Contains("latestPretripIsSafe", status, StringComparison.Ordinal);
        Assert.Contains("latestPretripIsSigned", status, StringComparison.Ordinal);
        Assert.Contains("latest pre-trip DVIR must be safe to operate and signed", status, StringComparison.Ordinal);
        Assert.Contains("pretrip_dvir_id", status, StringComparison.Ordinal);
        Assert.Contains("{ \"assigned\",\"accepted\"", status, StringComparison.Ordinal);
        AssertOrdered(status, "if (from == \"exception\")", "driverAllowedTargets");
        var driverMe = Block(source, "private static async Task<IResult> DriverMe(", "private static async Task<IResult> DriverAssignments(");
        Assert.Contains("latest_pretrip_safe_to_operate", driverMe, StringComparison.Ordinal);
        Assert.Contains("latest_pretrip_driver_signature_status", driverMe, StringComparison.Ordinal);
        Assert.Contains("vehicle_confirmed_at", driverMe, StringComparison.Ordinal);
        Assert.Contains("vehicle_confirmed_by_driver_id", driverMe, StringComparison.Ordinal);
        Assert.Contains("da.vehicle_confirmed_by_driver_id", current, StringComparison.Ordinal);
        Assert.Contains("assignment = row, driverId, driverNextStatuses", current, StringComparison.Ordinal);
        Assert.Contains("Verify the exact assigned vehicle", source, StringComparison.Ordinal);
        Assert.Contains("signed pre-trip DVIR are ready", source, StringComparison.Ordinal);
        var inspection = Block(source, "private static async Task<IResult> MaintInspectionCreate(", "private static async Task<IResult> MaintInspectionCreateInTransaction(");
        Assert.Contains("RunInTenantTransactionAsync", inspection, StringComparison.Ordinal);
        var inspectionTransaction = Block(source, "private static async Task<IResult> MaintInspectionCreateInTransaction(", "// GET /api/maintenance/inspections/{id}");
        Assert.Contains("AcquireDvirDepartureSafetyLockAsync", inspectionTransaction, StringComparison.Ordinal);
        Assert.Contains("isDriverSubmission = authenticatedDriverSubmission", inspectionTransaction, StringComparison.Ordinal);
        Assert.Contains("template_id", inspectionTransaction, StringComparison.Ordinal);
        Assert.Contains("checklist_item_id", inspectionTransaction, StringComparison.Ordinal);
        var driverSubmit = Block(source, "private static async Task<IResult> DriverSubmitDvir(", "private static async Task<IResult> DriverCoachingTasks(");
        Assert.Contains("Every required DVIR checklist item must be answered", driverSubmit, StringComparison.Ordinal);
        Assert.Contains("Required DVIR checklist items must be answered pass or fail", driverSubmit, StringComparison.Ordinal);
        Assert.Contains("DVIR contains an unknown or inactive checklist item", driverSubmit, StringComparison.Ordinal);
        Assert.Contains("DuplicateItem", driverSubmit, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("vehicle_unit_suffix_length", current, StringComparison.Ordinal);
        Assert.Contains("supplied.Length is < 1 or > 16", source, StringComparison.Ordinal);
        var dvirPilot = Read("backend-dotnet", "Controllers", "DvirHosEndpoints.cs");
        Assert.Contains("AcquireDvirDepartureSafetyLockAsync", dvirPilot, StringComparison.Ordinal);
        Assert.Contains("fleet-departure-safety", dvirPilot, StringComparison.Ordinal);
        foreach (var mutation in new[] { "CreateDvirReportPilot", "DvirCertifyRepairPilot", "DvirDriverSignPilot", "DvirDriverAcknowledgeRepairPilot" })
            Assert.Contains("AcquireDvirDepartureSafetyLockAsync", Block(dvirPilot, $"private static async Task<IResult> {mutation}(", "\n    private static"), StringComparison.Ordinal);
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
        Assert.Contains("s !== \"accepted\" || status === \"exception\"", driverPage, StringComparison.Ordinal);
        Assert.Contains("s === \"assigned\" || s === \"accepted\"", driverPage, StringComparison.Ordinal);
        Assert.Contains("Resume Assignment", driverPage, StringComparison.Ordinal);
        Assert.Contains("/api/telemetry/installation-quarantine", devices, StringComparison.Ordinal);
        Assert.Contains("Identity Quarantine", Read("frontend", "src", "pages", "IotDevicesPage.tsx"), StringComparison.Ordinal);
    }

    [Fact]
    public void QuarantineAndHistoricalAttributionFailClosedAcrossIngressAndLifecycle()
    {
        var routes = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var lifecycle = Read("backend-dotnet", "Controllers", "FleetIdentityEndpoints.cs");
        var registry = Read("telematics", "src", "Opstrax.Telematics.Gateway", "Identity", "PostgresDeviceRegistry.cs");
        Assert.Contains("/api/telemetry/installation-quarantine/{id:long}/resolve", routes, StringComparison.Ordinal);
        Assert.Contains("/api/telemetry/devices/{id:long}/installations/{installationId:long}/evidence", routes, StringComparison.Ordinal);
        Assert.Contains("has_unresolved_quarantine", routes, StringComparison.Ordinal);
        Assert.Contains("Device identity is quarantined", routes, StringComparison.Ordinal);
        Assert.Contains("InstallationHasEventAtOrAfterAsync", lifecycle, StringComparison.Ordinal);
        Assert.Contains("cannot predate telemetry already attributed", lifecycle, StringComparison.Ordinal);
        Assert.Contains("device_installation_quarantine", registry, StringComparison.Ordinal);
        Assert.Contains("resolution_notes", lifecycle, StringComparison.Ordinal);
        Assert.Contains("Duplicate serial quarantine requires a corrected serial", lifecycle, StringComparison.Ordinal);
        Assert.Contains("Corrected IMEI must contain exactly 15 digits", lifecycle, StringComparison.Ordinal);
        Assert.Contains("device.installation.evidence.created", lifecycle, StringComparison.Ordinal);
        Assert.Contains("objectKey must be a relative governed-storage key", lifecycle, StringComparison.Ordinal);
        Assert.Contains("Commissioning requires a persisted authenticated telemetry event", lifecycle, StringComparison.Ordinal);
        Assert.Contains("'location-event:'||id::TEXT", lifecycle, StringComparison.Ordinal);
        Assert.Contains("'canonical-telemetry:'||id::TEXT", lifecycle, StringComparison.Ordinal);
    }

    [Fact]
    public void DvirTemplateCreationPersistsTenantScopedChecklistItemsAtomically()
    {
        var source = Read("backend-dotnet", "Controllers", "DvirHosEndpoints.cs");
        var create = Block(source, "private static async Task<IResult> CreateDvirTemplatePilot(", "private static async Task<IResult> UpdateDvirTemplatePilot(");
        Assert.Contains("One to 50 checklist items are required", create, StringComparison.Ordinal);
        Assert.Contains("RunInTenantTransactionAsync", create, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO inspection_checklist_items", create, StringComparison.Ordinal);
        Assert.Contains("checklistItemCount", create, StringComparison.Ordinal);
        var ui = Read("frontend", "src", "pages", "DvirInspectionsPage.tsx");
        Assert.Contains("New checklist template", ui, StringComparison.Ordinal);
        Assert.Contains("dvirApi.createTemplate", ui, StringComparison.Ordinal);
        Assert.Contains("checklistItems:", ui, StringComparison.Ordinal);
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
