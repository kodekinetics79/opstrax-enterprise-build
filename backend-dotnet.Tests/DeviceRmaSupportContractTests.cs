namespace Opstrax.Tests;

public sealed class DeviceRmaSupportContractTests
{
    private static readonly string Migration = Read("database", "migrations", "2026_09_07_stage124_rma_support_ownership.sql");
    private static readonly string Endpoint = Read("backend-dotnet", "Controllers", "DeviceRmaSupportEndpoints.cs");
    private static readonly string Routes = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
    private static readonly string Service = Read("frontend", "src", "services", "telematicsService.ts");
    private static readonly string Page = Read("frontend", "src", "pages", "IotDevicesPage.tsx");

    [Fact]
    public void DatabaseHistoryIsAppendOnlyScopedAndCannotPromoteOutcomes()
    {
        Assert.Contains("RMA support actions are append-only", Migration, StringComparison.Ordinal);
        Assert.Contains("First RMA support action must claim ownership for the actor", Migration, StringComparison.Ordinal);
        Assert.Contains("Resolved RMA case cannot accept support actions", Migration, StringComparison.Ordinal);
        Assert.Contains("ck_stage124_actor_scope", Migration, StringComparison.Ordinal);
        Assert.Contains("support_response_claim=FALSE", Migration, StringComparison.Ordinal);
        Assert.Contains("physical_outcome_claim=FALSE", Migration, StringComparison.Ordinal);
        Assert.Contains("warranty_acceptance_claim=FALSE", Migration, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT ON TABLE device_rma_support_actions TO opstrax_app", Migration, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT,INSERT ON TABLE device_rma_support_actions TO opstrax_system", Migration, StringComparison.Ordinal);
    }

    [Fact]
    public void EndpointSerializesEachCaseAndReturnsExplicitNonClaims()
    {
        Assert.Contains("/api/telemetry/rma-cases/{caseId:long}/support-actions", Routes, StringComparison.Ordinal);
        Assert.Contains("RequirePermission(http, \"telematics:devices:rma\")", Endpoint, StringComparison.Ordinal);
        Assert.Contains("device-rma-support:{companyId}:{caseId}", Endpoint, StringComparison.Ordinal);
        Assert.Contains("OwnershipReassigned", Endpoint, StringComparison.Ordinal);
        Assert.Contains("rma_support_case_resolved", Endpoint, StringComparison.Ordinal);
        Assert.Contains("supportResponseClaim = false", Endpoint, StringComparison.Ordinal);
        Assert.Contains("physicalOutcomeClaim = false", Endpoint, StringComparison.Ordinal);
        Assert.Contains("warrantyAcceptanceClaim = false", Endpoint, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerSurfaceFailsClosedAndLabelsTheRecordHonestly()
    {
        Assert.Contains("recordDeviceRmaSupportAction", Service, StringComparison.Ordinal);
        Assert.Contains("payload.support_response_claim !== false", Service, StringComparison.Ordinal);
        Assert.Contains("RMA support data crossed the operator-recorded no-outcome-claim boundary", Service, StringComparison.Ordinal);
        Assert.Contains("Take ownership", Page, StringComparison.Ordinal);
        Assert.Contains("Escalate support case", Page, StringComparison.Ordinal);
        Assert.Contains("It does not claim a support response, warranty acceptance, or physical outcome", Page, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../", Path.Combine(parts))));
}
