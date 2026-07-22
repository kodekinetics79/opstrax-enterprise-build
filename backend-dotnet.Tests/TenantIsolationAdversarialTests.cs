namespace Opstrax.Tests;

/// <summary>
/// Adversarial regression locks for the highest-risk tenant boundaries.
/// These deliberately inspect the SQL and guard ordering at the endpoint/service
/// boundary: a future refactor must keep tenant ownership in the database predicate,
/// rather than relying on a filtered UI or a caller-supplied company identifier.
/// </summary>
public sealed class TenantIsolationAdversarialTests
{
    [Fact]
    public void TenantAdminUsers_CannotEnableCrossTenantMode()
    {
        var source = EndpointSource();
        var allowAll = Block(source,
            "private static async Task<bool> AllowAllUsers(",
            "private static bool IsSuperAdmin(");
        var users = Block(source,
            "private static async Task<IResult> AdminUsers(",
            "private static async Task<IResult> AdminUserDetail(");
        var detail = Block(source,
            "private static async Task<Dictionary<string, object?>?> GetScopedUser(",
            "private static IResult? ValidateRolePermissions(");

        Assert.Contains("return false;", allowAll, StringComparison.Ordinal);
        Assert.DoesNotContain("IsSuperAdmin", allowAll, StringComparison.Ordinal);
        Assert.Contains("u.company_id=@companyId", users, StringComparison.Ordinal);
        Assert.Contains("u.id=@id AND (@allUsers=1 OR u.company_id=@companyId)", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void TenantRoles_ExposeOnlySystemTemplatesAndCurrentTenantRoles()
    {
        var source = EndpointSource();
        var list = Block(source,
            "private static async Task<IResult> AdminRoles(",
            "private static IResult AdminPermissions(");
        var update = Block(source,
            "private static async Task<IResult> UpdateAdminRole(",
            "private static async Task<IResult> CreateAdminAuditEvent(");
        var resolve = Block(source,
            "private static async Task<Dictionary<string, object?>?> ResolveRoleRecord(",
            "private static async Task<Dictionary<string, object?>?> GetScopedUser(");

        Assert.Contains("r.company_id IS NULL OR r.company_id=@companyId", list, StringComparison.Ordinal);
        Assert.Contains("u.company_id=@companyId", list, StringComparison.Ordinal);
        Assert.Contains("id=@id AND company_id=@companyId AND is_system=FALSE", update, StringComparison.Ordinal);
        Assert.Contains("company_id IS NULL OR company_id=@companyId", resolve, StringComparison.Ordinal);
    }

    [Fact]
    public void AccessReview_AllReadsAndStateTransitionsAreTenantScoped()
    {
        var source = AccessReviewSource();

        Assert.Contains("reviewer must be an active user in this tenant", source, StringComparison.Ordinal);
        Assert.Contains("WHERE company_id = @cid", source, StringComparison.Ordinal);
        Assert.Contains("FROM access_reviews WHERE id = @rid AND company_id = @cid", source, StringComparison.Ordinal);
        Assert.Contains("WHERE review_id = @rid AND company_id = @cid", source, StringComparison.Ordinal);
        Assert.Contains("WHERE id = @iid AND review_id = @rid AND company_id = @cid", source, StringComparison.Ordinal);
        Assert.Contains("r.id = @rid AND r.company_id = @cid", source, StringComparison.Ordinal);
        Assert.Contains("WHERE id = @rid AND company_id = @cid", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceAssignment_ValidatesRelatedEntitiesAndTargetDeviceInSameTenant()
    {
        var source = EndpointSource();
        var assign = Block(source,
            "private static async Task<IResult> DeviceAssign(",
            "// ═══════════════════════════════════════════════════════════════════════════");

        AssertOrdered(assign, "ValidateDeviceAssignmentAsync", "UPDATE eld_devices");
        Assert.Contains("WHERE id=@id AND company_id=@cid AND deleted_at IS NULL", assign, StringComparison.Ordinal);
        Assert.Contains("FROM vehicles WHERE id=@id AND company_id=@cid AND deleted_at IS NULL", assign, StringComparison.Ordinal);
        Assert.Contains("FROM drivers WHERE id=@id AND company_id=@cid AND deleted_at IS NULL", assign, StringComparison.Ordinal);
    }

    [Fact]
    public void SignedGpsIngest_AuthenticatesGatewayBeforeLookupAndUsesStoredTenantBinding()
    {
        var source = EndpointSource();
        var ingest = Block(source,
            "private static async Task<IResult> GpsTrackerIngest(",
            "internal static bool TryParseTrackerTimestamp(");

        AssertOrdered(ingest, "Telemetry:GatewaySecret", "FROM eld_devices");
        AssertOrdered(ingest, "FixedTimeEquals", "FROM eld_devices");
        // Replay defense is now the durable, cross-instance guard (TEL-P1-REPLAY-005), not the old
        // process-local in-memory cache. The reservation is scoped to the resolved device/tenant.
        Assert.Contains("GpsGatewayReplayGuard.TryReserveDurableAsync", ingest, StringComparison.Ordinal);
        Assert.Contains("var companyId = Convert.ToInt64(device[\"companyId\"]);", ingest, StringComparison.Ordinal);
        Assert.Contains("device[\"vehicleId\"]", ingest, StringComparison.Ordinal);
        Assert.DoesNotContain("Str(\"companyId\"", ingest, StringComparison.Ordinal);
        Assert.DoesNotContain("Str(\"vehicleId\"", ingest, StringComparison.Ordinal);
        Assert.Contains("(company_id, vehicle_id, device_id, driver_id", ingest, StringComparison.Ordinal);
    }

    [Fact]
    public void GenericModules_ScopeListDetailCreateAndUpdateToAuthenticatedTenant()
    {
        var source = EndpointSource();
        var load = Block(source,
            "private static async Task<IResult> LoadModule(",
            "private static async Task<IResult> LoadModuleDetail(");
        var detail = Block(source,
            "private static async Task<IResult> LoadModuleDetail(",
            "private static async Task<IResult> CreateModuleRecord(");
        var create = Block(source,
            "private static async Task<IResult> CreateGenericModuleRecord(",
            "private static async Task<IResult> UpdateGenericModuleRecord(");
        var update = Block(source,
            "private static async Task<IResult> UpdateGenericModuleRecord(",
            "private static async Task<IResult> CreateVehicle(");

        Assert.Contains("WHERE company_id=@companyId AND module_key=@key", load, StringComparison.Ordinal);
        Assert.Contains("WHERE {ownership}=@companyId", load, StringComparison.Ordinal);
        Assert.Contains("WHERE id=@id AND company_id=@companyId AND module_key=@key", detail, StringComparison.Ordinal);
        Assert.Contains("WHERE id=@id AND {ownership}=@companyId", detail, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO module_records (company_id, module_key", create, StringComparison.Ordinal);
        Assert.Contains("WHERE company_id=@companyId AND module_key=@key AND id=@id", update, StringComparison.Ordinal);
    }

    [Fact]
    public void ComplianceHosGeofenceAndProofReads_UseAuthenticatedTenantPredicates()
    {
        var source = EndpointSource();

        Assert.Contains("WHERE cv.company_id=@cid", source, StringComparison.Ordinal);
        Assert.Contains("WHERE cap.company_id=@cid", source, StringComparison.Ordinal);
        Assert.Contains("WHERE hc.company_id=@cid", source, StringComparison.Ordinal);
        Assert.Contains("WHERE hl.driver_id=@id AND hl.company_id=@cid", source, StringComparison.Ordinal);
        Assert.Contains("JOIN geofences g ON g.id=ge.geofence_id AND g.company_id=@cid", source, StringComparison.Ordinal);
        Assert.Contains("JOIN jobs j ON j.id=pod.job_id AND j.company_id=@cid", source, StringComparison.Ordinal);
        Assert.Contains("WHERE tenant_id=@cid LIMIT 1", source, StringComparison.Ordinal);
        Assert.Contains("WHERE user_id=@uid LIMIT 1", source, StringComparison.Ordinal);
    }

    private static void AssertOrdered(string source, string first, string second)
    {
        var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
        var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
        Assert.True(firstIndex >= 0, $"Missing marker: {first}");
        Assert.True(secondIndex >= 0, $"Missing marker: {second}");
        Assert.True(firstIndex < secondIndex, $"Expected '{first}' before '{second}'");
    }

    private static string Block(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Unable to locate source block beginning '{startMarker}'");
        return source[start..end];
    }

    private static string EndpointSource() => ReadRepoFile("backend-dotnet", "Controllers", "EndpointMappings.cs");
    private static string AccessReviewSource() => ReadRepoFile("backend-dotnet", "Services", "AccessReviewService.cs");

    private static string ReadRepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
    }
}
