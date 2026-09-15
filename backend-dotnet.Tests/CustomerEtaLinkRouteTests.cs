namespace Opstrax.Tests;

// P1 fix guard — the customer-ETA "Send update" flow mints a token in customer_eta_links, which is
// resolved ONLY by /api/customer-eta/track/{token} whose public page is /eta/:trackingCode. Handing out
// /track/{token} routed to the fleet_tms tracking page (a different table) and always 404'd. This locks the
// returned link to the /eta/ prefix so the customer link the operator copies actually resolves.
public sealed class CustomerEtaLinkRouteTests
{
    [Fact]
    public void SendEta_Returns_Eta_Prefixed_Link_Not_Track()
    {
        var source = ReadSource("backend-dotnet", "Controllers", "EndpointMappings.cs");

        // The customer_eta_links secure token must be surfaced as an /eta/ link.
        Assert.Contains("trackingUrl = $\"/eta/{secureToken}\"", source, StringComparison.Ordinal);
        // And must NOT be surfaced as the fleet_tms /track/ link that cannot resolve it.
        Assert.DoesNotContain("trackingUrl = $\"/track/{secureToken}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EtaSurfacesUsePersistedEvidenceAndBoundProviderClaims()
    {
        var endpoints = ReadSource("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var program = ReadSource("backend-dotnet", "Program.cs");

        Assert.DoesNotContain("customer_experience_score", endpoints, StringComparison.Ordinal);
        Assert.Contains("average_feedback_rating", endpoints, StringComparison.Ordinal);
        Assert.Contains("NULLIF(eu.confidence_level,'Unspecified') eta_confidence_level", endpoints, StringComparison.Ordinal);
        Assert.Contains("var (branchClause, branchId) = StrictBranchFilter(http, \"j\")", endpoints, StringComparison.Ordinal);
        Assert.Contains("providerDeliveryClaim = false", endpoints, StringComparison.Ordinal);
        Assert.Contains("Bulk ETA processing completed:", endpoints, StringComparison.Ordinal);
        Assert.DoesNotContain("dispatch.eta.bulk.sent", endpoints, StringComparison.Ordinal);

        Assert.Contains("app.MapPost(\"/api/customer-eta/track/{trackingCode}/feedback\", CustomerEtaPublicFeedback)", endpoints, StringComparison.Ordinal);
        Assert.Contains("path.EndsWith(\"/feedback\"", program, StringComparison.Ordinal);
        Assert.Contains("'eta_tracking','open'", endpoints, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO customer_feedback (company_id,customer_id,job_id,tracking_code,rating,sentiment,comments)", endpoints, StringComparison.Ordinal);
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
