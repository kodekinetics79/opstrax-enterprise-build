using System.Reflection;
using Microsoft.AspNetCore.Http;
using Opstrax.Api.Controllers;

namespace Opstrax.Tests;

public sealed class DeviceInstallationImportContractTests
{
    [Fact]
    public void RoutesAndEveryHandlerRequireDeviceManagePermission()
    {
        var mappings = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var source = Read("backend-dotnet", "Controllers", "DeviceInstallationImportEndpoints.cs");
        Assert.Contains("/api/telemetry/device-installations/import-template", mappings, StringComparison.Ordinal);
        Assert.Contains("/api/telemetry/device-installations/import-preview", mappings, StringComparison.Ordinal);
        Assert.Contains("/api/telemetry/device-installations/import-commit", mappings, StringComparison.Ordinal);
        foreach (var method in new[] { "DeviceInstallationsImportTemplate", "DeviceInstallationsImportPreview", "DeviceInstallationsImportCommit" })
            Assert.Contains("RequireAnyDirectPermission(http, \"telemetry.devices.manage\")", MethodBlock(source, method), StringComparison.Ordinal);
        Assert.Contains("GetArrayLength() > ImportMaxRows", source, StringComparison.Ordinal);
        Assert.Contains("InstallationImportExceedsLimit(body)", MethodBlock(source, "DeviceInstallationsImportPreview"), StringComparison.Ordinal);
        Assert.Contains("InstallationImportExceedsLimit(body)", MethodBlock(source, "DeviceInstallationsImportCommit"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FleetManageAliasCannotAccessTemplatePreviewOrCommit()
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthUserIdItemKey] = 9L;
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = 42L;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Dispatcher";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "fleet:manage" };

        var template = InvokeSync("DeviceInstallationsImportTemplate", http);
        var preview = await InvokeAsync("DeviceInstallationsImportPreview", http, new Dictionary<string, object?>(), null, CancellationToken.None);
        var commit = await InvokeAsync("DeviceInstallationsImportCommit", http, new Dictionary<string, object?>(), null, null, CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, Status(template));
        Assert.Equal(StatusCodes.Status403Forbidden, Status(preview));
        Assert.Equal(StatusCodes.Status403Forbidden, Status(commit));
    }

    [Fact]
    public void ValidationIsTenantBranchSetBasedAndDetectsDuplicatesAndConflicts()
    {
        var source = Read("backend-dotnet", "Controllers", "DeviceInstallationImportEndpoints.cs");
        var validation = MethodBlock(source, "ValidateInstallationImportAsync");
        Assert.Contains("company_id=@cid", validation, StringComparison.Ordinal);
        Assert.Contains("ResolveImportBranch", validation, StringComparison.Ordinal);
        Assert.Contains("ANY(@serials)", validation, StringComparison.Ordinal);
        Assert.Contains("ANY(@codes)", validation, StringComparison.Ordinal);
        Assert.True(Count(validation, "@caller_branch::BIGINT IS NULL") >= 3);
        Assert.Contains("BranchScopeResolved", validation, StringComparison.Ordinal);
        Assert.Contains("Submitted branch is outside the authorized branch.", validation, StringComparison.Ordinal);
        Assert.Contains("matchingBranches.GroupBy", validation, StringComparison.Ordinal);
        Assert.Contains("ambiguousBranchCodes", validation, StringComparison.Ordinal);
        Assert.Contains("Submitted branch identity is ambiguous", validation, StringComparison.Ordinal);
        Assert.Contains("if (!candidate.BranchScopeResolved)", validation, StringComparison.Ordinal);
        Assert.Contains("devices.GroupBy", validation, StringComparison.Ordinal);
        Assert.Contains("vehicles.GroupBy", validation, StringComparison.Ordinal);
        Assert.Contains("matchingDevices is { Length: > 1 }", validation, StringComparison.Ordinal);
        Assert.Contains("DeviceId = null, VehicleId = null", validation, StringComparison.Ordinal);
        Assert.Contains("Duplicate device", validation, StringComparison.Ordinal);
        Assert.Contains("Duplicate idempotencyKey", validation, StringComparison.Ordinal);
        Assert.Contains("Duplicate primary", validation, StringComparison.Ordinal);
        Assert.Contains("hasExplicitTimezone", validation, StringComparison.Ordinal);
        Assert.Contains("(?:[zZ]|[+-]\\\\d{2}:\\\\d{2})$", validation, StringComparison.Ordinal);
        Assert.Contains("outside the selected or authorized branch", validation, StringComparison.Ordinal);
        Assert.Contains("already has an active installation", validation, StringComparison.Ordinal);
        Assert.Contains("active primary", validation, StringComparison.Ordinal);
        Assert.Contains("i.status IN ('Installed','Verified','Removed')", validation, StringComparison.Ordinal);
        Assert.Contains("i.effective_to>@earliest", validation, StringComparison.Ordinal);
        Assert.Contains("overlaps closed installation history", validation, StringComparison.Ordinal);
        Assert.Contains("overlaps closed primary", validation, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayFingerprintCoversEveryNormalizedMutationFieldAndLegacyRowsFailClosed()
    {
        var source = Read("backend-dotnet", "Controllers", "DeviceInstallationImportEndpoints.cs");
        var fingerprint = MethodBlock(source, "InstallationImportRequestHash");
        foreach (var field in new[]
        {
            "candidate.BranchId", "candidate.DeviceId", "candidate.VehicleId", "candidate.Role",
            "candidate.IsPrimary", "candidate.EffectiveFrom", "candidate.Location", "candidate.Odometer",
            "candidate.Method", "candidate.Reason"
        }) Assert.Contains(field, fingerprint, StringComparison.Ordinal);
        Assert.Contains("SHA256.HashData", fingerprint, StringComparison.Ordinal);
        Assert.Contains("legacy installation without a verifiable request fingerprint", source, StringComparison.Ordinal);
        Assert.Contains("responseReference", source, StringComparison.Ordinal);
        var scopedKey = MethodBlock(source, "InstallationImportScopedIdempotencyKey");
        Assert.Contains("candidate.BranchId", scopedKey, StringComparison.Ordinal);
        Assert.Contains("SHA256.HashData", scopedKey, StringComparison.Ordinal);
        Assert.Contains("InstallationImportScopedIdempotencyKey(candidate)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CommitIsAtomicDeterministicallyLockedReplaySafeAndNeverCommissions()
    {
        var source = Read("backend-dotnet", "Controllers", "DeviceInstallationImportEndpoints.cs");
        var commit = MethodBlock(source, "DeviceInstallationsImportCommit");
        var persist = MethodBlock(source, "PersistInstallationImportCandidatesAsync");
        Assert.Contains("RunInTenantTransactionAsync", commit, StringComparison.Ordinal);
        Assert.Contains("Distinct(StringComparer.Ordinal).OrderBy", commit, StringComparison.Ordinal);
        Assert.Contains("pg_advisory_xact_lock", commit, StringComparison.Ordinal);
        Assert.Contains("LockInstallationImportResourcesAsync", commit, StringComparison.Ordinal);
        Assert.Contains("LockInstallationResourceIdentitiesAsync", commit, StringComparison.Ordinal);
        Assert.True(commit.IndexOf("LockInstallationResourceIdentitiesAsync", StringComparison.Ordinal) <
                    commit.IndexOf("LockInstallationImportResourcesAsync", StringComparison.Ordinal));
        Assert.True(commit.IndexOf("LockInstallationImportResourcesAsync", StringComparison.Ordinal) <
                    commit.LastIndexOf("ValidateInstallationImportAsync", StringComparison.Ordinal));
        Assert.Contains("MarkInstallationImportDevicesInstalledAsync", commit, StringComparison.Ordinal);
        Assert.Contains("before.DeviceId != row.DeviceId", commit, StringComparison.Ordinal);
        Assert.Contains("before.VehicleId != row.VehicleId", commit, StringComparison.Ordinal);
        Assert.Contains("before.BranchId != row.BranchId", commit, StringComparison.Ordinal);
        Assert.True(commit.IndexOf("resourceIdentityChanged", StringComparison.Ordinal) < commit.IndexOf("MarkInstallationImportDevicesInstalledAsync", StringComparison.Ordinal));
        Assert.True(commit.IndexOf("invalid.Count > 0", StringComparison.Ordinal) < commit.IndexOf("PersistInstallationImportCandidatesAsync", StringComparison.Ordinal));
        Assert.True(commit.IndexOf("MarkInstallationImportDevicesInstalledAsync", StringComparison.Ordinal) < commit.IndexOf("PersistInstallationImportCandidatesAsync", StringComparison.Ordinal));
        Assert.Contains("No rows changed", commit, StringComparison.Ordinal);
        Assert.Contains("candidate.Replay", commit, StringComparison.Ordinal);
        Assert.Contains("candidate.RequestHash", persist, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO idempotency_keys", persist, StringComparison.Ordinal);
        Assert.Contains("jsonb_to_recordset", persist, StringComparison.Ordinal);
        Assert.Contains("inserted_keys", persist, StringComparison.Ordinal);
        Assert.Contains("completed_keys", persist, StringComparison.Ordinal);
        Assert.Contains("invalid.Any(row => row.IdempotencyConflict)", commit, StringComparison.Ordinal);
        Assert.Contains("status,", persist, StringComparison.Ordinal);
        Assert.Contains("'Installed'", persist, StringComparison.Ordinal);
        Assert.DoesNotContain("commissioning_result", persist, StringComparison.Ordinal);
        Assert.DoesNotContain("activation_verified_at", persist, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO device_state_transitions", persist, StringComparison.Ordinal);
        Assert.Contains("LogBatchAsync", commit, StringComparison.Ordinal);
        var resultLoopStart = commit.IndexOf("foreach (var candidate", StringComparison.Ordinal);
        var resultLoopEnd = commit.IndexOf("await audit.LogBatchAsync", resultLoopStart, StringComparison.Ordinal);
        Assert.DoesNotContain("await db.", commit[resultLoopStart..resultLoopEnd], StringComparison.Ordinal);
        Assert.Contains("device.installation.created", commit, StringComparison.Ordinal);
        Assert.Contains("device.installations.imported", commit, StringComparison.Ordinal);
        Assert.Contains("IsInstallationImportPersistenceConflict", commit, StringComparison.Ordinal);
        Assert.Contains("PostgresErrorCodes.CheckViolation", source, StringComparison.Ordinal);
        Assert.Contains("PostgresErrorCodes.ForeignKeyViolation", source, StringComparison.Ordinal);
        Assert.Contains("PostgresErrorCodes.DeadlockDetected", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SingleBulkTransferAndRemoveUseOneDeterministicResourceLockNamespace()
    {
        var source = Read("backend-dotnet", "Controllers", "FleetIdentityEndpoints.cs");
        var declaration = source.IndexOf("private static Task LockInstallationResourceIdentitiesAsync", StringComparison.Ordinal);
        Assert.True(declaration >= 0);
        var shared = MethodBlock(source[declaration..], "LockInstallationResourceIdentitiesAsync");
        Assert.Contains("device-install-resource:{companyId}:device:{id}", shared, StringComparison.Ordinal);
        Assert.Contains("device-install-resource:{companyId}:vehicle:{id}", shared, StringComparison.Ordinal);
        Assert.Contains("Distinct(StringComparer.Ordinal).OrderBy", shared, StringComparison.Ordinal);
        Assert.Contains("pg_advisory_xact_lock(hashtextextended(identity,0))", shared, StringComparison.Ordinal);
        var single = MethodBlock(source, "DeviceInstallationCreate");
        var remove = MethodBlock(source, "DeviceInstallationRemove");
        var transfer = MethodBlock(source, "DeviceInstallationTransfer");
        Assert.Contains("LockInstallationIdentityAsync", single, StringComparison.Ordinal);
        Assert.Contains("LockInstallationIdentityAsync", remove, StringComparison.Ordinal);
        Assert.Contains("LockInstallationIdentityAsync", transfer, StringComparison.Ordinal);
        Assert.Contains("PostgresErrorCodes.DeadlockDetected", single, StringComparison.Ordinal);
        Assert.Contains("PostgresErrorCodes.DeadlockDetected", transfer, StringComparison.Ordinal);
        Assert.Contains("LoadAndLockInstallationResourcesAsync", single, StringComparison.Ordinal);
        Assert.True(single.IndexOf("LoadAndLockInstallationResourcesAsync", StringComparison.Ordinal) <
                    single.IndexOf("INSERT INTO device_installations", StringComparison.Ordinal));
        Assert.Contains("deviceUpdated != 1", single, StringComparison.Ordinal);
        var lockedLoadDeclaration = source.IndexOf("private static async Task<Dictionary<string, object?>?> LoadAndLockInstallationResourcesAsync", StringComparison.Ordinal);
        Assert.True(lockedLoadDeclaration >= 0);
        var lockedLoad = MethodBlock(source[lockedLoadDeclaration..], "LoadAndLockInstallationResourcesAsync");
        Assert.Equal(3, Count(lockedLoad, "FOR UPDATE"));
        Assert.True(lockedLoad.IndexOf("var branch = await", StringComparison.Ordinal) < lockedLoad.IndexOf("var device = await", StringComparison.Ordinal));
        Assert.True(lockedLoad.IndexOf("var device = await", StringComparison.Ordinal) < lockedLoad.IndexOf("var vehicle = await", StringComparison.Ordinal));
        Assert.Contains("branch[\"deletedAt\"]", lockedLoad, StringComparison.Ordinal);
        Assert.Contains("branch[\"status\"]", lockedLoad, StringComparison.Ordinal);
        Assert.Contains("device[\"deviceBranchId\"]", lockedLoad, StringComparison.Ordinal);
        Assert.Contains("vehicle[\"vehicleBranchId\"]", lockedLoad, StringComparison.Ordinal);
        Assert.Contains("expectedBranchId", lockedLoad, StringComparison.Ordinal);
        Assert.Contains("preserveTerminalLifecycle", remove, StringComparison.Ordinal);
        Assert.Contains("CASE WHEN @preserve THEN device_state ELSE 'Registered' END", remove, StringComparison.Ordinal);
        Assert.Contains("(@branch::BIGINT IS NULL OR branch_id=@branch)", remove, StringComparison.Ordinal);
        Assert.True(remove.IndexOf("visibleInstallation", StringComparison.Ordinal) <
                    remove.IndexOf("InstallationHasEventAtOrAfterAsync", StringComparison.Ordinal));

        var commission = MethodBlock(source, "DeviceInstallationCommission");
        Assert.Contains("LockInstallationIdentityAsync", commission, StringComparison.Ordinal);
        Assert.Contains("(@branch::BIGINT IS NULL OR (d.branch_id=@branch AND i.branch_id=@branch))", commission, StringComparison.Ordinal);
        Assert.Contains("IsDeviceInstallEligible(visibleInstallation)", commission, StringComparison.Ordinal);
        Assert.True(commission.IndexOf("IsDeviceInstallEligible(visibleInstallation)", StringComparison.Ordinal) <
                    commission.IndexOf("UPDATE device_installations", StringComparison.Ordinal));

        var lifecycle = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        foreach (var method in new[] { "DeviceRevoke", "DeviceSuspend", "DeviceActivate" })
        {
            var declarationStart = lifecycle.IndexOf($"private static async Task<IResult> {method}", StringComparison.Ordinal);
            Assert.True(declarationStart >= 0);
            var block = MethodBlock(lifecycle[declarationStart..], method);
            Assert.Contains("LockInstallationIdentityAsync", block, StringComparison.Ordinal);
            Assert.True(block.IndexOf("LockInstallationIdentityAsync", StringComparison.Ordinal) < block.IndexOf("FOR UPDATE", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ResourceLocksAreActualRowsDeterministicallyOrderedAndConditionalStateWriteIsAllOrNothing()
    {
        var source = Read("backend-dotnet", "Controllers", "DeviceInstallationImportEndpoints.cs");
        var locks = MethodBlock(source, "LockInstallationImportResourcesAsync");
        Assert.Contains("Distinct().OrderBy", locks, StringComparison.Ordinal);
        Assert.Contains("FROM branches", locks, StringComparison.Ordinal);
        Assert.Contains("FROM eld_devices", locks, StringComparison.Ordinal);
        Assert.Contains("FROM vehicles", locks, StringComparison.Ordinal);
        Assert.True(locks.IndexOf("FROM branches", StringComparison.Ordinal) < locks.IndexOf("FROM eld_devices", StringComparison.Ordinal));
        Assert.True(locks.IndexOf("FROM eld_devices", StringComparison.Ordinal) < locks.IndexOf("FROM vehicles", StringComparison.Ordinal));
        Assert.Contains("lockedBranches.Count != branchIds.Length", locks, StringComparison.Ordinal);
        Assert.Contains("\"Active\"", locks, StringComparison.Ordinal);
        Assert.Contains("UPPER(BTRIM(d.device_serial)) device_key", locks, StringComparison.Ordinal);
        Assert.Contains("UPPER(BTRIM(v.vehicle_code)) vehicle_key", locks, StringComparison.Ordinal);
        Assert.Contains("locked[\"deviceKey\"]", locks, StringComparison.Ordinal);
        Assert.Contains("locked[\"vehicleKey\"]", locks, StringComparison.Ordinal);
        Assert.Contains("locked[\"branchId\"]", locks, StringComparison.Ordinal);
        Assert.Contains("locked[\"deletedAt\"]", locks, StringComparison.Ordinal);
        Assert.Contains("IsDeviceInstallEligible(locked)", locks, StringComparison.Ordinal);
        Assert.Equal(2, Count(locks, "lockedDevices.Count != devices.Length") +
                        Count(locks, "lockedVehicles.Count != vehicles.Length"));
        Assert.Equal(3, Count(locks, "FOR UPDATE"));
        var update = MethodBlock(source, "MarkInstallationImportDevicesInstalledAsync");
        Assert.Contains("WITH eligible AS MATERIALIZED", update, StringComparison.Ordinal);
        Assert.Contains("revoked_at IS NULL", update, StringComparison.Ordinal);
        Assert.Contains("'suspended'", update, StringComparison.Ordinal);
        Assert.Contains("(SELECT COUNT(*) FROM eligible)=@expected", update, StringComparison.Ordinal);
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    private static IResult InvokeSync(string name, params object?[] args)
    {
        var method = typeof(EndpointMappings).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        return Assert.IsAssignableFrom<IResult>(method.Invoke(null, args));
    }
    private static async Task<IResult> InvokeAsync(string name, params object?[] args)
    {
        var method = typeof(EndpointMappings).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        return await Assert.IsAssignableFrom<Task<IResult>>(method.Invoke(null, args));
    }
    private static int? Status(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([RepoRoot, .. parts]));
    private static string MethodBlock(string source, string name)
    {
        var start = source.IndexOf($" {name}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing method {name}");
        var next = source.IndexOf("\n    private static ", start + name.Length, StringComparison.Ordinal);
        return next < 0 ? source[start..] : source[start..next];
    }
    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;
}
