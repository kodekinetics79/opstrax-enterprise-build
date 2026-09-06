using System;
using System.IO;
using Xunit;
namespace Opstrax.Tests;
public sealed class DispatchHosAuthorityContractTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    private static string Source => File.ReadAllText(Path.Combine(Root, "backend-dotnet", "Controllers", "EndpointMappings.cs"));
    private static string Between(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal); Assert.True(start >= 0);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal); Assert.True(end > start);
        return source[start..end];
    }
    [Fact] public void AvailableDrivers_UsesOnlyFreshAuthoritativeClocks()
    {
        var s = Between(Source, "private static async Task<IResult> AvailableDrivers", "private static async Task<IResult> AvailableVehicles");
        Assert.DoesNotContain("FROM hos_records", s, StringComparison.Ordinal);
        Assert.Contains("FROM hos_clocks hc", s, StringComparison.Ordinal);
        Assert.Contains("hc.source_authority='Authoritative'", s, StringComparison.Ordinal);
        Assert.Contains("hc.source_observed_at >= NOW() - INTERVAL '24 hours'", s, StringComparison.Ordinal);
        Assert.Contains("hos.drive_time_remaining_minutes >= 60", s, StringComparison.Ordinal);
    }
    [Fact] public void DispatchEligibility_FailsClosedWithoutFreshAuthority()
    {
        var s = Between(Source, "internal static async Task<DispatchEligibilityResult> CheckDispatchEligibilityAsync", "// Safety events — critical unresolved flags.");
        Assert.DoesNotContain("FROM hos_records", s, StringComparison.Ordinal);
        Assert.Contains("source_authority='Authoritative'", s, StringComparison.Ordinal);
        Assert.Contains("Authoritative HOS clock unavailable or stale — cannot dispatch", s, StringComparison.Ordinal);
        Assert.Contains("blocking.Add", s, StringComparison.Ordinal);
    }
}
