using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class TelemetryLaunchHardeningTests
{
    [Fact]
    public void FixFreshness_UsesDeviceTimeRatherThanReceiptConnectivity()
    {
        var now = new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(("healthy", "low"), TelemetryFixFreshness.Classify(now.AddMinutes(-2), now));
        Assert.Equal(("stale", "unknown"), TelemetryFixFreshness.Classify(now.AddHours(-2), now));
        Assert.Equal(("unknown", "unknown"), TelemetryFixFreshness.Classify(now.AddMinutes(6), now));
    }

    [Fact]
    public void EveryBackendIngress_AlertsOnlyAfterWinningMonotonicLatestProjection()
    {
        var endpoints = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var native = Block(endpoints, "private static async Task<IResult> TelemetryIngest", "// ── GET /api/telemetry/stream");
        var gateway = Block(endpoints, "private static async Task<IResult> GpsTrackerIngest", "internal static bool TryParseTrackerTimestamp");
        var samsara = Read("backend-dotnet", "Services", "Connectors", "SamsaraSync.cs");
        var raw = Read("telematics", "src", "Opstrax.Telematics.Gateway", "Projection", "PostgresPositionProjectionStore.cs");

        AssertOrdered(native, "var latestRows = await db.ExecuteAsync", "if (vehicleId.HasValue && latestAdvanced && body.SpeedMph");
        AssertOrdered(native, "latestAdvanced = latestRows > 0", "GeofenceEvaluator.ProjectPositionAsync");
        AssertOrdered(gateway, "UpsertGatewayLatestPositionAsync", "if (gatewayLatestAdvanced && harshType is not null)");
        Assert.Contains("if (vehicleId is not null && gatewayLatestAdvanced)", gateway, StringComparison.Ordinal);
        AssertOrdered(samsara, "if (projected > 0)", "await ProjectAlertsAsync");
        AssertOrdered(raw, "int upsertRows = await UpsertLatestPositionAsync", "if (upsertRows > 0)");
        AssertOrdered(raw, "if (upsertRows > 0)", "await ProjectAlertsAsync");
    }

    [Fact]
    public void DelayedTelemetryFromRemovedInstallationIsHistoryOnlyAcrossEveryIngress()
    {
        var endpoints = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var native = Block(endpoints, "private static async Task<IResult> TelemetryIngest", "// ── GET /api/telemetry/stream");
        var gateway = Block(endpoints, "private static async Task<IResult> GpsTrackerIngest", "internal static bool TryParseTrackerTimestamp");
        var raw = Read("telematics", "src", "Opstrax.Telematics.Gateway", "Projection", "PostgresPositionProjectionStore.cs");

        AssertOrdered(native, "isCurrentInstallation = identity.IsCurrentInstallation", "if (vehicleId.HasValue && isCurrentInstallation)");
        Assert.Contains("identity.IsCurrentInstallation && vehicleId is not null", gateway, StringComparison.Ordinal);
        AssertOrdered(gateway, "identity.IsCurrentInstallation && vehicleId is not null", "UpsertGatewayLatestPositionAsync");
        var rawHistoricalGate = Block(raw, "// Delayed evidence remains attached", "// ── (b) monotonic snapshot");
        Assert.Contains("if (!device.IsCurrentInstallation)", rawHistoricalGate, StringComparison.Ordinal);
        Assert.Contains("ProjectionOutcome.StaleIgnored", rawHistoricalGate, StringComparison.Ordinal);
    }

    [Fact]
    public void TrustedGatewayHarshAlertsUseCanonicalOpenStatusForLiveAndAcknowledgeContracts()
    {
        var endpoints = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var gateway = Block(endpoints, "private static async Task<IResult> GpsTrackerIngest", "internal static bool TryParseTrackerTimestamp");
        var alerts = Block(endpoints, "private static async Task<IResult> TelemetryAlerts", "// ── POST /api/telemetry/alerts/{id}/acknowledge");
        var acknowledge = Block(endpoints, "private static async Task<IResult> TelemetryAlertAcknowledge", "// ── POST /api/telemetry/alerts/{id}/resolve");
        var liveState = Read("backend-dotnet", "Services", "TelemetryLiveStateService.cs");

        Assert.Contains("@src, 'Open', 'trusted-gateway'", gateway, StringComparison.Ordinal);
        Assert.DoesNotContain("@src, 'open', 'trusted-gateway'", gateway, StringComparison.Ordinal);
        Assert.Contains("?? \"Open\"", alerts, StringComparison.Ordinal);
        Assert.Contains("ta.status=@status", alerts, StringComparison.Ordinal);
        Assert.Contains("status='Open'", acknowledge, StringComparison.Ordinal);
        Assert.Contains("ta.status='Open'", liveState, StringComparison.Ordinal);
        Assert.Contains("UPDATE latest_vehicle_positions", liveState, StringComparison.Ordinal);
        Assert.Contains("open_alert_count=@openAlertCount", liveState, StringComparison.Ordinal);
        Assert.Contains("Value(row, \"vehicleId\", \"vehicle_id\")", liveState, StringComparison.Ordinal);
    }

    [Fact]
    public void IngressUsesAuthorizedAreaSetAndTenantRuleInsideSystemTransaction()
    {
        var endpoints = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var native = Block(endpoints, "private static async Task<IResult> TelemetryIngest", "// ── GET /api/telemetry/stream");
        var geofence = Read("backend-dotnet", "Services", "GeofenceEvaluator.cs");

        AssertOrdered(native, "RunInSystemTransactionAsync", "SELECT threshold_value FROM telemetry_rules");
        Assert.Contains("e.company_id IS NOT NULL AND e.company_id>0", native, StringComparison.Ordinal);
        Assert.Contains("TryPositiveLong(device.GetValueOrDefault(\"companyId\"), out var companyId)", native, StringComparison.Ordinal);
        Assert.DoesNotContain(": 1L", native, StringComparison.Ordinal);
        Assert.Contains("inside any valid circle or polygon is authorized", geofence, StringComparison.Ordinal);
        Assert.Contains("if (isInside) return null", geofence, StringComparison.Ordinal);
        Assert.Equal(2, Count(endpoints, "GeofenceEvaluator.ProjectPositionAsync"));
        Assert.Contains("GeofenceEvaluator.ProjectPositionAsync", Read("backend-dotnet", "Services", "Connectors", "SamsaraSync.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void CustomIntegrationMutationsEncryptConfigAndCannotFakeConnectedStatus()
    {
        var endpoints = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var create = Block(endpoints, "private static async Task<IResult> CreateIntegration", "private static async Task<IResult> UpdateIntegration");
        var update = Block(endpoints, "private static async Task<IResult> UpdateIntegration", "private static async Task<IResult> RemoveIntegration");
        var sync = Block(endpoints, "private static async Task<IResult> IntegrationSync", "private static async Task<IResult> ConfigureIntegration");
        var configure = Block(endpoints, "private static async Task<IResult> ConfigureIntegration", "// ── POST /api/integrations/{id}/test-connection");
        var testConnection = Block(endpoints, "private static async Task<IResult> IntegrationTestConnection", "// ── POST /api/integrations/{id}/run-action");
        var runAction = Block(endpoints, "private static async Task<IResult> IntegrationRunAction", "// ── GET /api/maps/geocode");
        var operationLease = Read("backend-dotnet", "Services", "Connectors", "ConnectorOperationLease.cs");

        Assert.Contains("ProtectIntegrationConfig", create, StringComparison.Ordinal);
        Assert.Contains("'Disconnected'", create, StringComparison.Ordinal);
        Assert.DoesNotContain("@status", create, StringComparison.Ordinal);
        Assert.Contains("MergeConfigForStorage", endpoints, StringComparison.Ordinal);
        Assert.Contains("ProtectIntegrationConfig", update, StringComparison.Ordinal);
        Assert.DoesNotContain("status        =", update, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/api/integrations/{id:long}/connect\", IntegrationTestConnection)", endpoints, StringComparison.Ordinal);
        Assert.DoesNotContain("SetIntegrationStatus(http, id, \"Connected\"", sync, StringComparison.Ordinal);
        Assert.Contains("Status422UnprocessableEntity", sync, StringComparison.Ordinal);
        Assert.Contains("MergeConfigForStorage", configure, StringComparison.Ordinal);
        Assert.Contains("config_json = @config::jsonb", configure, StringComparison.Ordinal);
        Assert.DoesNotContain("config_json,'{}'::jsonb) ||", configure, StringComparison.Ordinal);
        Assert.Contains("ConnectorOperationLease.CompleteTestAsync", testConnection, StringComparison.Ordinal);
        Assert.Contains("last_tested_at=NOW()", operationLease, StringComparison.Ordinal);
        Assert.Contains("CASE WHEN @ok THEN 'Connected' ELSE 'Error' END", operationLease, StringComparison.Ordinal);
        Assert.DoesNotContain("last_sync_at", testConnection, StringComparison.Ordinal);
        Assert.DoesNotContain("sync_label", testConnection, StringComparison.Ordinal);
        Assert.Contains("action.Equals(\"sync\"", runAction, StringComparison.Ordinal);
        Assert.Contains("action.Equals(\"sync-telemetry\"", runAction, StringComparison.Ordinal);
        Assert.Contains("Status422UnprocessableEntity", runAction, StringComparison.Ordinal);
        Assert.Contains("Use the tenant-scoped integration sync endpoint", runAction, StringComparison.Ordinal);
        AssertOrdered(runAction, "action.Equals(\"sync\"", "connector.RunActionAsync");
    }

    [Fact]
    public void SamsaraCatalogStartsUnverifiedAndLegacyFixtureResetIsExact()
    {
        var dotnetCatalog = Block(Read("backend-dotnet", "Seed", "IntegrationCatalog.cs"),
            "new(\"samsara\"", "new(\"geotab\"");
        var nodeCatalog = Block(Read("backend", "src", "modules", "integrations", "integrations.registry.ts"),
            "key: \"samsara\"", "key: \"geotab\"");
        var initSeed = Read("database", "init", "002_seed.sql");
        var migration = Read("database", "migrations", "2026_09_02_stage94_samsara_provider_truth.sql");

        foreach (var catalog in new[] { dotnetCatalog, nodeCatalog })
        {
            Assert.Contains("Disconnected", catalog, StringComparison.Ordinal);
            Assert.Contains("Never", catalog, StringComparison.Ordinal);
            Assert.Contains("Odometer", catalog, StringComparison.Ordinal);
            Assert.DoesNotContain("Connected", catalog, StringComparison.Ordinal);
            Assert.DoesNotContain("Real-time", catalog, StringComparison.Ordinal);
            Assert.DoesNotContain("providerAccountId", catalog, StringComparison.Ordinal);
            Assert.DoesNotContain("Dashcam", catalog, StringComparison.Ordinal);
        }

        Assert.Contains("(1,'Samsara Import Adapter','Telematics','Disconnected')", initSeed, StringComparison.Ordinal);
        Assert.Contains("integration_key = 'samsara'", migration, StringComparison.Ordinal);
        Assert.Contains("config_json = '{\"providerAccountId\":\"sam-1001\"}'::jsonb", migration, StringComparison.Ordinal);
        Assert.Contains("last_sync_at = TIMESTAMPTZ '2026-06-24T14:14:00Z'", migration, StringComparison.Ordinal);
        Assert.Contains("last_test_ok IS NULL", migration, StringComparison.Ordinal);
        Assert.Contains("provider_name = 'Samsara Import Adapter'", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE integration_key = 'samsara';", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void SamsaraLifecycleIsFailClosedAcrossDisconnectSyncAndProviderMappingUi()
    {
        var endpoints = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var disconnect = Block(endpoints, "private static async Task<IResult> DisconnectIntegration", "private static async Task<IResult> IntegrationSync");
        var sync = Block(endpoints, "private static async Task<IResult> IntegrationSync", "private static async Task<IResult> ConfigureIntegration");
        var worker = Read("backend-dotnet", "Services", "ConnectorSyncBackgroundService.cs");
        var operationLease = Read("backend-dotnet", "Services", "Connectors", "ConnectorOperationLease.cs");
        var page = Read("frontend", "src", "pages", "IntegrationsPage.tsx");
        var api = Read("frontend", "src", "services", "integrationsApi.ts");
        var telemetry = Read("frontend", "src", "services", "telematicsService.ts");

        Assert.Contains("config_json='{}'::jsonb", disconnect, StringComparison.Ordinal);
        Assert.Contains("last_sync_at=NULL", disconnect, StringComparison.Ordinal);
        Assert.Contains("last_tested_at=NULL", disconnect, StringComparison.Ordinal);
        Assert.Contains("providerCredentialRevocationRequired = true", disconnect, StringComparison.Ordinal);
        Assert.Contains("TryAcquireAsync", sync, StringComparison.Ordinal);
        Assert.Contains("[\"Connected\"]", sync, StringComparison.Ordinal);
        Assert.Contains("operation_generation=operation_generation+1", disconnect, StringComparison.Ordinal);
        Assert.Contains("operation_lease_token=NULL", disconnect, StringComparison.Ordinal);
        Assert.Contains("operation_generation=@generation", operationLease, StringComparison.Ordinal);
        Assert.Contains("AssertCurrentForWriteAsync", operationLease, StringComparison.Ordinal);
        Assert.Contains("MaxDegreeOfParallelism = 4", worker, StringComparison.Ordinal);
        Assert.Contains("maxPages = 5", worker, StringComparison.Ordinal);

        Assert.Contains("key: \"apiToken\"", page, StringComparison.Ordinal);
        Assert.Contains("onConnect={() => setConfigTarget(integration)}", page, StringComparison.Ordinal);
        Assert.Contains("provider portal", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Discover → Map → Validate", page, StringComparison.Ordinal);
        Assert.Contains("to=\"/iot-devices\"", page, StringComparison.Ordinal);
        Assert.Contains("unwrap<IntegrationTestResult>(apiClient.post(`/api/integrations/${id}/sync`", api, StringComparison.Ordinal);
        Assert.DoesNotContain("isMatchedToDevice: true", telemetry, StringComparison.Ordinal);
        Assert.Contains("no persisted provider-device mapping has been verified", telemetry, StringComparison.Ordinal);
    }

    [Fact]
    public void GatewayProvisionRequiresTheSameAuthenticatedEncryptionEnvelopeAsRotation()
    {
        var endpoints = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var provision = Block(endpoints, "private static async Task<IResult> TelemetryGatewayProvision", "private static async Task<IResult> TelemetryGatewayList");

        Assert.Contains("DeviceHmacSecretProtection.EncryptForStorage", provision, StringComparison.Ordinal);
        Assert.DoesNotContain("pii.Encrypt(", provision, StringComparison.Ordinal);
        Assert.Contains("if (encrypted is null)", provision, StringComparison.Ordinal);
    }

    [Fact]
    public void CommittedIngressAcknowledgementDoesNotDependOnLiveStateCacheRefresh()
    {
        var endpoints = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var native = Block(endpoints, "private static async Task<IResult> TelemetryIngest", "// ── GET /api/telemetry/stream");
        var gateway = Block(endpoints, "private static async Task<IResult> GpsTrackerIngest", "internal static bool TryParseTrackerTimestamp");
        var refresh = Block(endpoints, "private static async Task RefreshTelemetryLiveStateBestEffortAsync", "private static Task<int> UpsertGatewayLatestPositionAsync");

        AssertOrdered(native, "if (!telemetryCommitted || duplicateNonce)", "RefreshTelemetryLiveStateBestEffortAsync");
        AssertOrdered(gateway, "if (!gatewayWriteCommitted || durableReplayDuplicate)", "RefreshTelemetryLiveStateBestEffortAsync");
        Assert.Contains("try", refresh, StringComparison.Ordinal);
        Assert.Contains("catch", refresh, StringComparison.Ordinal);
        Assert.Contains("durable event remains committed", refresh, StringComparison.Ordinal);
        Assert.DoesNotContain("LogWarning(exception", refresh, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LogWarning(ex", refresh, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RawGatewayAckRequiresIdempotentProjectionAndRestrictedSystemDatabaseIdentity()
    {
        var gateway = Read("telematics", "src", "Opstrax.Telematics.Gateway", "GatewayConnection.cs");
        var program = Read("telematics", "src", "Opstrax.Telematics.Gateway", "Program.cs");
        var readiness = Read("telematics", "src", "Opstrax.Telematics.Gateway", "Infrastructure", "ProductionStorageReadinessService.cs");
        var publishPump = Block(gateway, "private async Task PublishPumpAsync", "private readonly record struct PendingTelemetry");

        AssertOrdered(gateway, "_replayGuard.CheckAsync", "EventId = decision.EventId");
        Assert.Contains("owner.DeviceId, serial, contentHash", gateway, StringComparison.Ordinal);
        Assert.Contains("new PostgresReplayGuard(telematicsDb!, serialModulus: 65_536)", program, StringComparison.Ordinal);
        Assert.DoesNotContain("case ReplayOutcome.DuplicateReplay:",
            publishPump,
            StringComparison.Ordinal);
        Assert.Contains("pending.Persisted.TrySetException(ex)", publishPump, StringComparison.Ordinal);
        AssertOrdered(publishPump, ".ApplyAsync(evt", "_backbone");
        Assert.Contains("current_user<>'opstrax_system'", readiness, StringComparison.Ordinal);
        Assert.Contains("session_user<>'opstrax_system'", readiness, StringComparison.Ordinal);
        Assert.Contains("NOT role.rolsuper AND NOT role.rolbypassrls", readiness, StringComparison.Ordinal);
        Assert.Contains("database_owner", readiness, StringComparison.Ordinal);
        Assert.Contains("schema_owner", readiness, StringComparison.Ordinal);
        Assert.Contains("required_privileges(table_name,privilege)", readiness, StringComparison.Ordinal);
        Assert.Contains("has_table_privilege(current_user,to_regclass(table_name),privilege) IS NOT TRUE", readiness, StringComparison.Ordinal);
        Assert.Contains("has_sequence_privilege(current_user,to_regclass(name),'USAGE') IS NOT TRUE", readiness, StringComparison.Ordinal);
        Assert.Contains("('telemetry_replay_device_state')", readiness, StringComparison.Ordinal);
        Assert.Contains("('telemetry_replay_seen','unwrapped_serial')", readiness, StringComparison.Ordinal);
        Assert.Contains("('telemetry_replay_seen','event_id')", readiness, StringComparison.Ordinal);
        Assert.Contains("('telemetry_replay_device_state','SELECT')", readiness, StringComparison.Ordinal);
        Assert.Contains("('telemetry_replay_device_state','INSERT')", readiness, StringComparison.Ordinal);
        Assert.Contains("('telemetry_replay_device_state','UPDATE')", readiness, StringComparison.Ordinal);
        Assert.Contains("telemetry_replay_device_state:DELETE", readiness, StringComparison.Ordinal);
    }

    [Fact]
    public void TerminalTelemetryAclIsDefaultDenyAndCredentialColumnsAreSystemOnly()
    {
        var migration = Read("database", "migrations", "2026_08_11_stage76_telematics_security_hardening.sql");
        var replayMigration = Read("database", "migrations", "telematics", "005_replay_guard.sql");
        var readiness = Read("backend-dotnet", "Services", "FleetProductionReadinessService.cs");

        Assert.Contains("REVOKE ALL ON SCHEMA public FROM PUBLIC", migration, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT (%s) ON TABLE public.eld_devices", migration, StringComparison.Ordinal);
        Assert.Contains("GRANT UPDATE (%s) ON TABLE public.eld_devices", migration, StringComparison.Ordinal);
        Assert.Contains("'malfunction_resolved_at','malfunction_resolved_by','resolution_evidence'", migration, StringComparison.Ordinal);
        Assert.Contains("'row_version','updated_at'", migration, StringComparison.Ordinal);
        Assert.Contains("secret_encrypted','UPDATE", migration, StringComparison.Ordinal);
        Assert.Contains("telemetry_stream_ticket_nonces_id_seq", migration, StringComparison.Ordinal);
        Assert.Contains("Stage76 stream-ticket nonce sequence ACL is unsafe", migration, StringComparison.Ordinal);
        Assert.Contains("has_sequence_privilege('opstrax_system',sequence_record.oid,'USAGE')", migration, StringComparison.Ordinal);
        Assert.Contains("has_sequence_privilege('opstrax_system',sequence_record.oid,'SELECT')", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("NOT has_sequence_privilege('opstrax_system',sequence_record.oid,'USAGE,SELECT')", migration, StringComparison.Ordinal);
        Assert.Contains("defaults.defaclnamespace=0 OR schema_ns.nspname='public'", migration, StringComparison.Ordinal);
        Assert.Contains("$stage76_replay_generation_repair$", migration, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE telemetry_replay_seen ADD COLUMN IF NOT EXISTS unwrapped_serial", migration, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE telemetry_replay_seen ADD COLUMN IF NOT EXISTS event_id", migration, StringComparison.Ordinal);
        Assert.Contains("Deliberately do not infer state from legacy rows", migration, StringComparison.Ordinal);
        Assert.Contains("telemetry_replay_device_state", migration, StringComparison.Ordinal);
        Assert.Contains("uq_telemetry_replay_seen_unwrapped", migration, StringComparison.Ordinal);
        Assert.Contains("Stage76 durable replay generation ACL/schema is unsafe", migration, StringComparison.Ordinal);
        Assert.Contains("tenant_system_only", readiness, StringComparison.Ordinal);
        Assert.Contains("system_no_update", readiness, StringComparison.Ordinal);
        Assert.Contains("('canonical_telemetry_events',          FALSE, FALSE,FALSE,FALSE,FALSE, TRUE,TRUE,FALSE,TRUE)", migration, StringComparison.Ordinal);
        Assert.Contains("('canonical_telemetry_events')", readiness, StringComparison.Ordinal);
        Assert.Contains("default_acl.grantee=0", readiness, StringComparison.Ordinal);
        Assert.Contains("defaults.defaclnamespace=0 OR default_ns.nspname='public'", readiness, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM telemetry_replay_seen WHERE seen_at", replayMigration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No blanket age-based DELETE is safe", replayMigration, StringComparison.Ordinal);
        Assert.Contains("every row for devices without a durable", replayMigration, StringComparison.Ordinal);
    }

    private static int Count(string source, string marker) =>
        source.Split(marker, StringSplitOptions.None).Length - 1;

    private static string Block(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start < 0 ? 0 : start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Unable to locate source block {startMarker}");
        return source[start..end];
    }

    private static void AssertOrdered(string source, string first, string second)
    {
        var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
        var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
        Assert.True(firstIndex >= 0, $"Missing marker: {first}");
        Assert.True(secondIndex > firstIndex, $"Expected '{first}' before '{second}'");
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([RepoRoot(), .. parts]));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "backend-dotnet")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
