using System.IO;
using System.Linq;
using Xunit;

namespace Opstrax.Tests;

public class Stage16ASourceRegressionTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    private static string ReadSource(params string[] parts)
    {
        var path = Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray());
        return File.ReadAllText(path);
    }

    [Fact]
    public void CustomerPortal_Offers_Live_Feedback_Intake_Without_Internal_Data()
    {
        var page = ReadSource("frontend", "src", "pages", "CustomerVisibilityPage.tsx");

        Assert.Contains("Customer Feedback & Complaint Intake", page);
        Assert.Contains("customerEtaApi.feedback", page);
        Assert.Contains("Feedback", page);
        Assert.DoesNotContain("margin/cost/internal risk", page, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("seed", page, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CrmAndFinance_Surfaces_Are_Live_Only_No_Seed_Fallback_Masking()
    {
        var leads = ReadSource("frontend", "src", "pages", "LeadsPage.tsx");
        var opps = ReadSource("frontend", "src", "pages", "OpportunitiesPage.tsx");
        var quotes = ReadSource("frontend", "src", "pages", "QuotationsPage.tsx");
        var finance = ReadSource("frontend", "src", "pages", "FinancialAnalyticsPage.tsx");

        Assert.DoesNotContain("withFallback", leads);
        Assert.DoesNotContain("mockOperatingData", leads);
        Assert.Contains("/api/leads", leads);

        Assert.DoesNotContain("withFallback", opps);
        Assert.DoesNotContain("mockOperatingData", opps);
        Assert.Contains("/api/opportunities", opps);

        Assert.DoesNotContain("withFallback", quotes);
        Assert.DoesNotContain("mockOperatingData", quotes);
        Assert.Contains("/api/quotations", quotes);

        Assert.DoesNotContain("withFallback", finance);
        Assert.DoesNotContain("seedInvoices", finance);
        Assert.DoesNotContain("seedCustomers", finance);
        // The Invoices/AR-aging surfaces are wired to the real revenue spine (issued_invoices)
        // and the tested AR-aging endpoint — NOT the generic module_records feed.
        Assert.Contains("/api/issued-invoices", finance);
        Assert.Contains("/api/finance/ar-aging", finance);
        Assert.DoesNotContain("/api/invoices", finance);
        Assert.Contains("Sourced from the live revenue spine (issued_invoices).", finance);
    }

    [Fact]
    public void FleetDriversDispatchAndReports_Surfaces_Remain_Live_And_Recommendation_Only()
    {
        var vehicles = ReadSource("frontend", "src", "pages", "VehiclesModulePage.tsx");
        var drivers = ReadSource("frontend", "src", "pages", "DriversModulePage.tsx");
        var dispatch = ReadSource("frontend", "src", "pages", "DispatchCommandPage.tsx");
        var reports = ReadSource("frontend", "src", "pages", "ReportsPage.tsx");
        var platform = ReadSource("frontend", "src", "pages", "platform", "PlatformCommandCenterPage.tsx");
        var compliance = ReadSource("frontend", "src", "pages", "CompliancePage.tsx");

        Assert.Contains("vehiclesApi.list", vehicles);
        Assert.Contains("vehiclesApi.summary", vehicles);
        Assert.Contains("vehiclesApi.planningInsights", vehicles);
        Assert.DoesNotContain("withFallback", vehicles);

        Assert.Contains("driversApi.list", drivers);
        Assert.Contains("driversApi.summary", drivers);
        Assert.DoesNotContain("withFallback", drivers);

        Assert.Contains("dispatchApi.board", dispatch);
        Assert.Contains("dispatchApi.eligibility", dispatch);
        Assert.Contains("System Dispatch Insights", dispatch);
        Assert.DoesNotContain("success: true", dispatch);

        Assert.Contains("useDatasets", reports);
        Assert.Contains("useSavedReports", reports);
        Assert.DoesNotContain("mockOperatingData", reports);
        Assert.DoesNotContain("withFallback", reports);

        Assert.Contains("Platform Dashboard", platform);
        Assert.Contains("platformApi.commandCenter", platform);
        Assert.Contains("tenant", platform, System.StringComparison.OrdinalIgnoreCase);

        Assert.Contains("useComplianceSummary", compliance);
        Assert.DoesNotContain("withFallback", compliance);
    }
}
