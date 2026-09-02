using System.Diagnostics;
using System.Reflection;

namespace Opstrax.Api.Observability;

// ─────────────────────────────────────────────────────────────────────────────
// BuildInfo — deploy identity + process uptime, resolved once at startup.
//
// Version resolution order (first non-empty wins):
//   RENDER_GIT_COMMIT (exact) → OPSTRAX_DEPLOY_VERSION → assembly informational
//   version → "unknown". Render injects the commit automatically; it must win
//   over a stale manually configured label so readiness cannot misidentify a deploy.
//
// Nothing here is secret; all values are safe to surface in /health responses,
// logs, spans, and the Reliability Center.
// ─────────────────────────────────────────────────────────────────────────────

public static class BuildInfo
{
    private static readonly DateTime StartUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime();

    public static string Version { get; } = ResolveVersion();

    public static string Environment { get; } =
        System.Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

    public static string Service => "opstrax-api";

    /// <summary>Wall-clock seconds since the process started.</summary>
    public static long UptimeSeconds => (long)(DateTime.UtcNow - StartUtc).TotalSeconds;

    public static DateTime StartedAtUtc => StartUtc;

    private static string ResolveVersion()
    {
        var renderCommit = System.Environment.GetEnvironmentVariable("RENDER_GIT_COMMIT");
        if (!string.IsNullOrWhiteSpace(renderCommit)) return renderCommit.Trim();

        var explicitVersion = System.Environment.GetEnvironmentVariable("OPSTRAX_DEPLOY_VERSION");
        if (!string.IsNullOrWhiteSpace(explicitVersion)) return explicitVersion.Trim();

        var info = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info)) return info.Trim();

        return "unknown";
    }
}
