namespace Opstrax.Tests;

/// <summary>
/// The invariant: the permission set a user is GRANTED at login must be the same set they are
/// AUTHORIZED against on every subsequent request. If those two disagree, the SPA renders menus
/// the API then 403s (and hides ones it would allow).
///
/// This used to be enforced by grepping Program.cs for an inlined
/// `RolePermissionDefaults.TryGetValue(roleName…)` — asserting one particular *implementation*
/// of the invariant rather than the invariant itself. It passed while the two paths had in fact
/// drifted into OPPOSITE precedence: login answered from users.permissions_json first, the
/// middleware ignored that column entirely whenever the user had a role. Same user, two answers,
/// green test.
///
/// The duplication is now gone — both callers delegate to
/// EndpointMappings.ResolveEffectivePermissionsAsync — so the assertion is that the duplication
/// STAYS gone. That is the thing that actually protects us.
/// </summary>
public sealed class RoleSessionConsistencyTests
{
    [Fact]
    public void SessionMiddleware_AndLogin_ResolvePermissionsThroughTheSameResolver()
    {
        var program = ReadSource("backend-dotnet", "Program.cs");
        var endpoints = ReadSource("backend-dotnet", "Controllers", "EndpointMappings.cs");

        // The request-authorization middleware must not compute permissions itself.
        Assert.Contains("EndpointMappings.ResolveEffectivePermissionsAsync(", program, StringComparison.Ordinal);

        // The login path must go through the very same resolver.
        Assert.Contains("ResolveEffectivePermissionsAsync(", endpoints, StringComparison.Ordinal);
        Assert.Contains("public static async Task<string[]> ResolvePermissionsAsync(", endpoints, StringComparison.Ordinal);
    }

    /// <summary>
    /// Guards the specific regression: the middleware must not grow its own private copy of the
    /// resolution logic again. A second implementation is how login and enforcement drifted apart
    /// the first time.
    /// </summary>
    [Fact]
    public void SessionMiddleware_DoesNotReimplementPermissionResolution()
    {
        var program = ReadSource("backend-dotnet", "Program.cs");

        Assert.DoesNotContain("SELECT permission_key FROM role_permissions", program, StringComparison.Ordinal);
        Assert.DoesNotContain("RolePermissionDefaults.TryGetValue", program, StringComparison.Ordinal);
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
