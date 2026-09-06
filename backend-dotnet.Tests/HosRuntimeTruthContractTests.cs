using System;
using System.IO;
using Xunit;
namespace Opstrax.Tests;
public sealed class HosRuntimeTruthContractTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    [Fact] public void RuntimeSchema_DoesNotRecreateLegalTimeDefaults()
    {
        var s = File.ReadAllText(Path.Combine(Root,"backend-dotnet","Services","Batch6SchemaService.cs"));
        Assert.DoesNotContain("drive_time_remaining_minutes INT NOT NULL DEFAULT 660", s, StringComparison.Ordinal);
        Assert.Contains("drive_time_remaining_minutes INT NULL", s, StringComparison.Ordinal);
        Assert.Contains("status VARCHAR(80) NOT NULL DEFAULT 'Unavailable'", s, StringComparison.Ordinal);
        Assert.Contains("source_authority", s, StringComparison.Ordinal);
    }
}
