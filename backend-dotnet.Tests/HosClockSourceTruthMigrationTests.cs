using System;
using System.IO;
using Xunit;
namespace Opstrax.Tests;
public sealed class HosClockSourceTruthMigrationTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    private static string Sql => File.ReadAllText(Path.Combine(Root,"database","migrations","2026_09_03_stage99_hos_clock_source_truth.sql"));
    [Fact] public void Stage99_RemovesFabricatedDefaultsAndAddsAuthority()
    {
        Assert.Contains("source_authority VARCHAR(32)", Sql, StringComparison.Ordinal);
        Assert.Contains("DROP DEFAULT", Sql, StringComparison.Ordinal);
        Assert.Contains("drive_time_remaining_minutes = NULL", Sql, StringComparison.Ordinal);
        Assert.Contains("source_authority = 'LegacyUnverified'", Sql, StringComparison.Ordinal);
        Assert.Contains("BEFORE INSERT OR UPDATE ON hos_clocks", Sql, StringComparison.Ordinal);
        Assert.Contains("VALIDATE CONSTRAINT ck_hos_clocks_source_authority", Sql, StringComparison.Ordinal);
    }
    [Fact] public void CurrentHosUiRendersMissingClockAsUnavailable()
    {
        var page = File.ReadAllText(Path.Combine(Root,"frontend","src","pages","HosEldPage.tsx"));
        Assert.Contains("if (value == null) return", page, StringComparison.Ordinal);
        Assert.Contains("Clock value unavailable", page, StringComparison.Ordinal);
    }
}
