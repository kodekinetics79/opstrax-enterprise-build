namespace Opstrax.Tests;

public sealed class ExpenseWorkflowSourceTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    [Fact]
    public void ExpenseEndpoints_CannotReintroduceFabricatedPreviewOrClientApproval()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Controllers", "EndpointMappings.cs"));
        var start = source.IndexOf("// BATCH 5 HANDLERS — EXPENSES", StringComparison.Ordinal);
        var end = source.IndexOf("// BATCH 5 HANDLERS — CONTRACTS / RATES", start, StringComparison.Ordinal);
        var expenses = source[start..end];

        Assert.DoesNotContain("Expense Import Placeholder", expenses);
        Assert.DoesNotContain("detectedRows = 18", expenses);
        Assert.DoesNotContain("Get(body, \"approvalStatus\")", expenses);
        Assert.DoesNotContain("Get(body, \"riskScore\")", expenses);
        Assert.Contains("'Pending', 'Pending'", expenses);
        Assert.Contains("Status501NotImplemented", expenses);
        Assert.Contains("v.company_id=e.company_id", expenses);
        Assert.Contains("c.company_id=e.company_id", expenses);
    }

    [Fact]
    public void ExpenseUi_LabelsOriginAndDoesNotOfferApprovalAsAnEditableField()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "frontend", "src", "pages", "Batch5FinancePage.tsx"));

        Assert.Contains("recordOrigin", source);
        Assert.Contains("Demo Data", File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Controllers", "EndpointMappings.cs")));
        Assert.DoesNotContain("[\"approvalStatus\",\"Approval Status\"]", source);
        Assert.DoesNotContain("amount: 250", source);
        Assert.Contains("Currencies are displayed separately", source);
    }
}

