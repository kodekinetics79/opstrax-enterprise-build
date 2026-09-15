namespace Opstrax.Tests;

public sealed class DeviceOpsExceptionQueueContractTests
{
    private static readonly string Endpoints = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
    private static readonly string Service = Read("frontend", "src", "services", "telematicsService.ts");
    private static readonly string Page = Read("frontend", "src", "pages", "IotDevicesPage.tsx");

    [Fact]
    public void ServerQueueUsesPersistedSoftwareFactsAndBoundedPaging()
    {
        Assert.Contains("\"readiness\" =>", Endpoints, StringComparison.Ordinal);
        Assert.Contains("deviceOpsGapExpression", Endpoints, StringComparison.Ordinal);
        Assert.Contains("exactDeviceTupleExpression", Endpoints, StringComparison.Ordinal);
        Assert.Contains("current_connectivity.id IS NULL", Endpoints, StringComparison.Ordinal);
        Assert.Contains("COALESCE(open_rma.open_case_count,0)>0", Endpoints, StringComparison.Ordinal);
        Assert.Contains("readiness_gaps", Endpoints, StringComparison.Ordinal);
        Assert.Contains("LIMIT @limit OFFSET @offset", Endpoints, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolvedRmaAndCrossBranchRecordsCannotRemainInTheQueue()
    {
        Assert.Contains("i.branch_id IS NOT DISTINCT FROM e.branch_id", Endpoints, StringComparison.Ordinal);
        Assert.Contains("p.branch_id IS NOT DISTINCT FROM e.branch_id", Endpoints, StringComparison.Ordinal);
        Assert.Contains("c.branch_id IS NOT DISTINCT FROM e.branch_id", Endpoints, StringComparison.Ordinal);
        Assert.Contains("),'Open')<>'Resolved'", Endpoints, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerSurfaceFailsClosedWhenAssessmentFieldsAreUnavailable()
    {
        Assert.Contains("deviceOpsAssessmentAvailable", Service, StringComparison.Ordinal);
        Assert.Contains("Assessment unavailable", Service, StringComparison.Ordinal);
        Assert.Contains("Reload after the DeviceOps assessment API is available", Page, StringComparison.Ordinal);
        Assert.Contains("Neither measure is certification evidence", Page, StringComparison.Ordinal);
        Assert.Contains("Hardware/provider certification holds tracked separately", Page, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../", Path.Combine(parts))));
}
