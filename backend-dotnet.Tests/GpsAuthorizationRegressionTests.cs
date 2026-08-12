namespace Opstrax.Tests;

public sealed class GpsAuthorizationRegressionTests
{
    private static readonly string Endpoints = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "../../../../backend-dotnet/Controllers/EndpointMappings.cs"));
    private static readonly string ProgramSource = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "../../../../backend-dotnet/Program.cs"));
    private static readonly string SamsaraConnector = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "../../../../backend-dotnet/Services/Connectors/SamsaraConnector.cs"));
    private static readonly string SamsaraSync = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "../../../../backend-dotnet/Services/Connectors/SamsaraSync.cs"));

    [Fact]
    public void LiveLocationSurfaces_RequireCapabilityAndBranchScope()
    {
        var ticket = Slice("private static async Task<IResult> TelemetryStreamTicket", "// ── POST /api/telemetry/ingest");
        var stream = Slice("private static async Task TelemetryStream", "// ── GET /api/telemetry/positions");
        var positions = Slice("private static async Task<IResult> TelemetryPositions", "// ── GET /api/telemetry/breadcrumbs");
        var breadcrumbs = Slice("private static async Task<IResult> TelemetryBreadcrumbs", "// ── POST /api/telemetry/gps-ingest");

        Assert.Contains("RequirePermission(http, \"telemetry.live_state.read\")", ticket);
        Assert.Contains("IssueStreamTicket(userId, companyId, GetBranchId(http))", ticket);
        Assert.Contains("@branchId", stream);
        Assert.Contains("v.branch_id=@branchId", stream);
        Assert.Contains("RequirePermission(http, \"telemetry.live_state.read\")", positions);
        Assert.Contains("v.branch_id=@branchId", positions);
        Assert.Contains("scoped_vehicle.branch_id=@branchId", breadcrumbs);
        Assert.Contains("ValidateScoped", ProgramSource);
        Assert.Contains("UPDATE telemetry_stream_ticket_nonces", ProgramSource);
        Assert.Contains("consumed_at IS NULL", ProgramSource);
        Assert.Contains("consumed != 1", ProgramSource);
        Assert.Contains("string.Equals(path, \"/api/telemetry/stream\"", ProgramSource);
        Assert.DoesNotContain("path.StartsWith(\"/api/telemetry/stream\"", ProgramSource);
        Assert.Contains("claims.BranchId", ProgramSource);
        Assert.Contains("claims.Permissions", ProgramSource);
    }

    [Fact]
    public void GeofenceCrud_IsPermissionedValidatedAndBranchScoped()
    {
        var list = Slice("private static Task<IResult> GeofenceList", "private static async Task<IResult> GeofenceSummary");
        var create = Slice("private static async Task<IResult> GeofenceCreate", "private static async Task<IResult> GeofenceUpdate");
        var update = Slice("private static async Task<IResult> GeofenceUpdate", "private static async Task<IResult> GeofenceDelete");
        var delete = Slice("private static async Task<IResult> GeofenceDelete", "private sealed record GeofenceValue");

        Assert.Contains("telemetry.live_state.read", list);
        Assert.Contains("g.branch_id=@branchId", list);
        Assert.Contains("fleet:manage", create);
        Assert.Contains("TryValidateGeofence", create);
        Assert.Contains("@branchId", create);
        Assert.Contains("affected == 0", update);
        Assert.Contains("branch_id=@branchId", update);
        Assert.Contains("affected == 0", delete);
        Assert.Contains("branch_id=@branchId", delete);
    }

    [Fact]
    public void NativeIngest_PreservesObservedTimeInsteadOfReceiptTime()
    {
        var ingest = Slice("private static async Task<IResult> TelemetryIngest", "private static async Task TelemetryStream");
        Assert.Contains("TryParseObservedAt(body.EventTime, out var observedAt)", ingest);
        Assert.Contains("client_generated_id, idempotency_key, observed_at, normalized_at, event_time, received_at", ingest);
        Assert.Contains("@observedAt, NOW(), @observedAt, NOW()", ingest);
        Assert.Contains("@batt, @observedAt, NOW()", ingest);
        Assert.DoesNotContain("@idempotencyKey, NOW(), NOW()", ingest);
    }

    [Fact]
    public void SamsaraSync_DrainsCursorAndRejectsFabricatedFixes()
    {
        Assert.Contains("for (var page = 0; page < maxPages; page++)", SamsaraConnector);
        Assert.Contains("Samsara:MaxPagesPerSync", SamsaraConnector);
        Assert.Contains("if (!hasNextPage)", SamsaraConnector);
        Assert.Contains("completed = true", SamsaraConnector);
        Assert.Contains("pagination did not advance its cursor", SamsaraConnector);
        Assert.Contains("GetWithRetryAsync", SamsaraSync);
        Assert.Contains("existing.idempotency_key=@idem", SamsaraSync);
        Assert.Contains("if (!gps.TryGetProperty(\"time\"", SamsaraSync);
        Assert.DoesNotContain("? t.ToUniversalTime() : DateTime.UtcNow", SamsaraSync);
    }

    private static string Slice(string start, string end)
    {
        var from = Endpoints.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"Missing source marker: {start}");
        var to = Endpoints.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"Missing source marker: {end}");
        return Endpoints[from..to];
    }
}
