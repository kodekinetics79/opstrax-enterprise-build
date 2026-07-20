namespace Opstrax.Tests;

public sealed class SecurityEventServiceTests
{
    [Fact]
    public void PostgreSqlBooleanColumnsUseBooleanParametersAndPredicates()
    {
        var source = ReadSource("backend-dotnet", "Services", "SecurityEventService.cs");

        Assert.Contains("AddWithValue(\"@ok\",   success)", source, StringComparison.Ordinal);
        Assert.Contains("AND success = false", source, StringComparison.Ordinal);
        Assert.Contains("@meta::jsonb", source, StringComparison.Ordinal);
        Assert.DoesNotContain("success ? 1 : 0", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AND success = 0", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }
}
