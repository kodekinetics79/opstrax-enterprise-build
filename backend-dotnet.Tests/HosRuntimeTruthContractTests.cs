using System;
using System.IO;
using Xunit;

namespace Opstrax.Tests;

public sealed class HosRuntimeTruthContractTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    [Fact]
    public void RuntimeSchema_DoesNotRecreateLegalTimeOrOkDefaults()
    {
        var source = File.ReadAllText(Path.Combine(Root, "backend-dotnet", "Services", "Batch6SchemaService.cs"));

        Assert.DoesNotContain("drive_time_remaining_minutes INT NOT NULL DEFAULT 660", source, StringComparison.Ordinal);
        Assert.DoesNotContain("shift_time_remaining_minutes INT NOT NULL DEFAULT 840", source, StringComparison.Ordinal);
        Assert.DoesNotContain("cycle_time_remaining_minutes INT NOT NULL DEFAULT 4200", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "status VARCHAR(80) NOT NULL DEFAULT 'OK',\n            hos_warning",
            source,
            StringComparison.Ordinal);

        Assert.Contains("drive_time_remaining_minutes INT NULL", source, StringComparison.Ordinal);
        Assert.Contains("shift_time_remaining_minutes INT NULL", source, StringComparison.Ordinal);
        Assert.Contains("cycle_time_remaining_minutes INT NULL", source, StringComparison.Ordinal);
        Assert.Contains(
            "status VARCHAR(80) NOT NULL DEFAULT 'Unavailable',\n            hos_warning",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeRepair_ContainsStage99SourceAuthorityColumns()
    {
        var source = File.ReadAllText(Path.Combine(Root, "backend-dotnet", "Services", "Batch6SchemaService.cs"));

        Assert.Contains("new(\"hos_clocks\",               \"clock_source\",", source, StringComparison.Ordinal);
        Assert.Contains("new(\"hos_clocks\",               \"source_event_id\",", source, StringComparison.Ordinal);
        Assert.Contains("new(\"hos_clocks\",               \"source_observed_at\",", source, StringComparison.Ordinal);
        Assert.Contains("new(\"hos_clocks\",               \"source_authority\",", source, StringComparison.Ordinal);
        Assert.Contains("new(\"hos_clocks\",               \"source_quality\",", source, StringComparison.Ordinal);
    }
}
