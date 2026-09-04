using System;
using System.IO;
using Xunit;

namespace Opstrax.Tests;

public sealed class DispatchHosAuthorityContractTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    private static string Source => File.ReadAllText(
        Path.Combine(Root, "backend-dotnet", "Controllers", "EndpointMappings.cs"));

    private static string Between(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"start marker not found: {startMarker}");
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"end marker not found: {endMarker}");
        return source[start..end];
    }

    [Fact]
    public void AvailableDrivers_DoesNotUseLegacyHosRecordsAsLegalAuthority()
    {
        var slice = Between(
            Source,
            "private static async Task<IResult> AvailableDrivers",
            "private static async Task<IResult> AvailableVehicles");

        Assert.DoesNotContain("FROM hos_records", slice, StringComparison.Ordinal);
        Assert.DoesNotContain("to_regclass('public.hos_records')", slice, StringComparison.Ordinal);
        Assert.Contains("FROM hos_clocks hc", slice, StringComparison.Ordinal);
        Assert.Contains("hc.source_authority='Authoritative'", slice, StringComparison.Ordinal);
        Assert.Contains("hc.source_observed_at >= NOW() - INTERVAL '24 hours'", slice, StringComparison.Ordinal);
        Assert.Contains("available_hos_hours", slice, StringComparison.Ordinal);
        Assert.Contains("COALESCE(hos.status,'Unavailable')", slice, StringComparison.Ordinal);
    }

    [Fact]
    public void DispatchEligibility_UsesOnlyFreshAuthoritativeHosClock()
    {
        var slice = Between(
            Source,
            "internal static async Task<DispatchEligibilityResult> CheckDispatchEligibilityAsync",
            "// Safety events — critical unresolved flags.");

        Assert.DoesNotContain("FROM hos_records", slice, StringComparison.Ordinal);
        Assert.DoesNotContain("to_regclass('public.hos_records')", slice, StringComparison.Ordinal);
        Assert.DoesNotContain("IsOperableHosStatus", slice, StringComparison.Ordinal);
        Assert.Contains("FROM hos_clocks", slice, StringComparison.Ordinal);
        Assert.Contains("source_authority='Authoritative'", slice, StringComparison.Ordinal);
        Assert.Contains("source_observed_at >= NOW() - INTERVAL '24 hours'", slice, StringComparison.Ordinal);
        Assert.Contains("Authoritative HOS clock unavailable or stale", slice, StringComparison.Ordinal);
    }
}
