namespace Opstrax.Tests;

public sealed class RoleSessionConsistencyTests
{
    [Fact]
    public void SessionMiddleware_UsesTheSameRoleDefaultsAsLogin()
    {
        var program = ReadSource("backend-dotnet", "Program.cs");

        Assert.Contains("EndpointMappings.RolePermissionDefaults.TryGetValue(roleName", program, StringComparison.Ordinal);
        Assert.Contains("permissions.UnionWith(defaultPermissions)", program, StringComparison.Ordinal);
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
