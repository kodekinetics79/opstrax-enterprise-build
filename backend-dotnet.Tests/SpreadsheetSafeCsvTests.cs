using Opstrax.Api.Controllers;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class SpreadsheetSafeCsvTests
{
    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData(" +SUM(A1:A2)", "' +SUM(A1:A2)")]
    [InlineData("\t@cmd", "'\t@cmd")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("ordinary", "ordinary")]
    [InlineData("comma,value", "\"comma,value\"")]
    [InlineData("quoted \"value\"", "\"quoted \"\"value\"\"\"")]
    [InlineData("line\nbreak", "\"line\nbreak\"")]
    public void Cell_IsSpreadsheetSafeAndRfc4180Compatible(string input, string expected) =>
        Assert.Equal(expected, SpreadsheetSafeCsv.Cell(input));

    [Fact]
    public void ExistingExportEntryPoints_DelegateToTheCentralEncoder()
    {
        const string formula = "=HYPERLINK(\"https://invalid.example\")";
        Assert.Equal(SpreadsheetSafeCsv.Cell(formula), EndpointMappings.CsvCell(formula));
        Assert.Equal(SpreadsheetSafeCsv.Cell(formula, quoteAlways: true), ActiveShipmentsEndpoints.CsvCell(formula));
        Assert.Equal(SpreadsheetSafeCsv.Cell(formula, quoteAlways: true), FleetTmsLogisticsEndpoints.LastMileCsvCell(formula));
    }

    [Fact]
    public void RevenueAndPlatformAuditExports_UseTheCentralEncoder()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        var revenue = File.ReadAllText(Path.Combine(root, "backend-dotnet", "Controllers", "RevenueReadinessEndpoints.cs"));
        var platform = File.ReadAllText(Path.Combine(root, "backend-dotnet", "Controllers", "PlatformEndpoints.cs"));

        Assert.Contains("SpreadsheetSafeCsv.Row", revenue, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string Csv(", revenue, StringComparison.Ordinal);
        Assert.Contains("SpreadsheetSafeCsv.Cell(value, quoteAlways: true)", platform, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string CsvCell(", platform, StringComparison.Ordinal);
    }

    [Fact]
    public void RevenueExports_NeutralizePersistedCustomerNames()
    {
        var aging = new ArAgingRecord(
            1, "SAR", 0, 0, 0, 0, 0, 0,
            [new ArAgingCustomerRecord(1, "=WEBSERVICE(\"https://invalid.example\")", 0, 0, 0, 0, 0, 0)]);
        var summary = new PaymentSummaryRecord(
            1, "SAR", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 0, 0, null, 0, 0,
            [new PaymentSummaryCustomerRecord(1, "+cmd", 0, 0, null, 0)]);

        Assert.Contains("\"'=WEBSERVICE(\"\"https://invalid.example\"\")\"", RevenueReadinessEndpoints.BuildArAgingCsv(aging));
        Assert.Contains("'+cmd", RevenueReadinessEndpoints.BuildPaymentSummaryCsv(summary));
    }
}
