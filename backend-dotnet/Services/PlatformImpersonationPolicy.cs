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

    // Surfaced to the Platform console so an operator — and the customer reading the
    // grant record — can see the exact scope a support session carries, rather than
    // being told to trust that it is "read only".
    public static IReadOnlyList<string> ReadOnlyScope => ReadOnlyPathPrefixes;

    // Reviewable mutation-denial matrix. Runtime policy still fails closed for
    // every method/path not explicitly allowed; this matrix makes the complete
    // supported read surface auditable and prevents a new read family from being
    // added without mutation coverage.
    public static IReadOnlyList<(string Method, string Path)> MutationDenialMatrix =>
        ReadOnlyPathPrefixes
            .SelectMany(prefix => new[] { HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete }
                .Select(method => (method, prefix)))
            .Append((HttpMethods.Post, "/api/auth/me"))
            .Append((HttpMethods.Put, "/api/auth/me"))
            .Append((HttpMethods.Patch, "/api/auth/me"))
            .Append((HttpMethods.Delete, "/api/auth/me"))
            .ToArray();

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
