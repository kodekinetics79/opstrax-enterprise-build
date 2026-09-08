using System;
using System.IO;
using Xunit;
namespace Opstrax.Tests;
public sealed class HosOperationalAlertSourceTruthTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    [Fact] public void HosAlerts_RequireFreshAuthoritativeClock()
    {
        var s = File.ReadAllText(Path.Combine(Root,"backend-dotnet","Services","OperationalAlertDetectionService.cs"));
        Assert.DoesNotContain("HosRecordsSql", s, StringComparison.Ordinal);
        Assert.Contains("c.source_authority = 'Authoritative'", s, StringComparison.Ordinal);
        Assert.Contains("c.source_observed_at > NOW() - INTERVAL '24 hours'", s, StringComparison.Ordinal);
    }
}
