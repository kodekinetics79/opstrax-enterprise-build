namespace Opstrax.Tests;

public sealed class DeviceRmaEndpointContractTests
{
    private static readonly string Source = File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../backend-dotnet/Controllers/EndpointMappings.cs")));

    [Fact]
    public void EveryRmaMutationUsesDedicatedPermissionAndBranchScope()
    {
        var handlers = Handlers();
        Assert.Equal(3, Count(handlers, "RequirePermission(http, \"telematics:devices:rma\")"));
        Assert.Contains("@branchId::BIGINT IS NULL OR c.branch_id=@branchId", handlers, StringComparison.Ordinal);
        Assert.Contains("@branchId::BIGINT IS NULL OR branch_id=@branchId", handlers, StringComparison.Ordinal);
    }

    [Fact]
    public void CaseAndEventWritesAreIdempotentOrderedAndAppendOnly()
    {
        var handlers = Handlers();
        Assert.Contains("RmaCaseReplayMatches", handlers, StringComparison.Ordinal);
        Assert.Contains("RmaEventReplayMatches", handlers, StringComparison.Ordinal);
        Assert.Contains("rma_event_out_of_order", handlers, StringComparison.Ordinal);
        Assert.Contains("nextSequence", handlers, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE device_rma_", handlers, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM device_rma_", handlers, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReplacementResolvesExactInventoryAndNeverClaimsPhysicalSwap()
    {
        var handlers = Handlers();
        Assert.Contains("UPPER(BTRIM(device_serial))=@serial", handlers, StringComparison.Ordinal);
        Assert.Contains("NOT EXISTS (", handlers, StringComparison.Ordinal);
        Assert.Contains("active_install.status IN ('Installed','Verified')", handlers, StringComparison.Ordinal);
        Assert.Contains("rma_replacement_same_device", handlers, StringComparison.Ordinal);
        Assert.Contains("'Planned','ExternalHold',FALSE", handlers, StringComparison.Ordinal);
        Assert.Contains("physicalSwapClaim = false", handlers, StringComparison.Ordinal);
        Assert.Contains("No physical swap or installation is claimed", handlers, StringComparison.Ordinal);
    }

    private static string Handlers()
    {
        var start = Source.IndexOf("private static async Task<IResult> DeviceRmaCaseCreate", StringComparison.Ordinal);
        var end = Source.IndexOf("private static object DbNullableText", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "RMA handlers could not be isolated.");
        return Source[start..end];
    }

    private static int Count(string text, string value) =>
        (text.Length - text.Replace(value, "", StringComparison.Ordinal).Length) / value.Length;
}
