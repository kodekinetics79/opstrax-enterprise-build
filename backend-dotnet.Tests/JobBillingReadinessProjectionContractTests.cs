namespace Opstrax.Tests;

public sealed class JobBillingReadinessProjectionContractTests
{
    [Fact]
    public void JobListExportAndDetailUsePersistedBillingEvidence()
    {
        var source = Source("backend-dotnet", "Controllers", "EndpointMappings.cs");
        Assert.True(Count(source, "THEN 'Ready to bill'") >= 3);
        Assert.True(Count(source, "THEN 'Invoiced'") >= 3);
        Assert.True(Count(source, "THEN 'Blocked: Invoice Missing'") >= 3);
        Assert.True(Count(source, "FROM issued_invoices ii WHERE ii.company_id=j.company_id AND ii.job_id=j.id") >= 3);
    }

    private static int Count(string text, string value)
    {
        var count = 0; var offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0) { count++; offset += value.Length; }
        return count;
    }

    private static string Source(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }
}
