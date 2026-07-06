namespace Opstrax.Tests;

public sealed class LiveWorkflowRegressionTests
{
    [Fact]
    public void BusinessSpine_HandlesJsonElementsAndPostgreSqlNativeTypes()
    {
        var source = ReadSource("backend-dotnet", "Controllers", "BusinessSpineEndpoints.cs");
        var endpoints = ReadSource("backend-dotnet", "Controllers", "EndpointMappings.cs");

        Assert.Contains("value is JsonElement element ? element.ToString()", source, StringComparison.Ordinal);
        Assert.Contains("element.TryGetInt64(out var parsed)", source, StringComparison.Ordinal);
        Assert.Contains("element.TryGetDecimal(out var parsed)", source, StringComparison.Ordinal);
        Assert.Contains("element.ValueKind is JsonValueKind.True or JsonValueKind.False", source, StringComparison.Ordinal);
        Assert.Contains("@elig::jsonb", endpoints, StringComparison.Ordinal);
        Assert.DoesNotContain("safetyOverridden ? 1 : 0", endpoints, StringComparison.Ordinal);
        Assert.DoesNotContain("hosOverridden ? 1 : 0", endpoints, StringComparison.Ordinal);
    }

    [Fact]
    public void Notifications_PersistRequiredBodyColumn()
    {
        var source = ReadSource("backend-dotnet", "Services", "NotificationService.cs");

        Assert.Contains("title, message, body", source, StringComparison.Ordinal);
        Assert.Contains("@title, @msg, @msg", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InitialDatabaseSeed_HasNoSharedPlaintextPassword()
    {
        var schema = ReadSource("database", "init", "001_schema.sql");
        var seed = ReadSource("database", "init", "002_seed.sql");

        Assert.DoesNotContain("DEFAULT 'Admin@12345'", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("demo123", seed, StringComparison.OrdinalIgnoreCase);
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
