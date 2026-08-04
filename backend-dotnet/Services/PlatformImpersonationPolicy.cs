namespace Opstrax.Api.Services;

/// <summary>
/// Fail-closed policy shared by the tenant authentication edge and the Platform
/// support-access endpoint. An impersonated principal can inspect tenant state,
/// but cannot invoke a state-changing tenant route. Ending the exact grant remains
/// a Platform-plane operation; logout is the only tenant-plane mutation allowed.
/// </summary>
public static class PlatformImpersonationPolicy
{
    public const string GrantIdItemKey = "auth.impersonation_grant_id";
    public const string GrantRefItemKey = "auth.impersonation_grant_ref";
    public const string GrantExpiresAtItemKey = "auth.impersonation_grant_expires_at";

    // Initial release allowlist: audited Safety investigation and tenant audit
    // visibility only. Method safety alone is insufficient because legacy GET
    // handlers elsewhere may hydrate catalogs or have other write side effects.
    private static readonly string[] ReadOnlyPathPrefixes =
    [
        "/api/safety",
        "/api/incidents",
        "/api/coaching",
        "/api/dvir",
        "/api/hos",
        "/api/audit/logs",
    ];

    public static bool IsEnabled(IConfiguration configuration) =>
        configuration.GetValue("PlatformImpersonation:Enabled", false);

    public static bool IsReadOnlyRequestAllowed(string method, string path)
    {
        if (HttpMethods.IsOptions(method)) return true;

        if ((HttpMethods.IsGet(method) || HttpMethods.IsHead(method))
            && (string.Equals(path, "/api/auth/me", StringComparison.OrdinalIgnoreCase)
                || ReadOnlyPathPrefixes.Any(prefix =>
                    string.Equals(path, prefix, StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))))
            return true;

        // A support user must be able to discard the exact bearer locally. This
        // endpoint deletes only the caller's session and cannot mutate tenant data.
        return HttpMethods.IsPost(method)
            && string.Equals(path, "/api/auth/logout", StringComparison.OrdinalIgnoreCase);
    }
}
