using System.Diagnostics;
using System.Reflection;

namespace Opstrax.Api.Observability;

// ─────────────────────────────────────────────────────────────────────────────
// BuildInfo — deploy identity + process uptime, resolved once at startup.
//
// Version resolution order (first non-empty wins):
//   OPSTRAX_DEPLOY_VERSION → RENDER_GIT_COMMIT (short) → assembly informational
//   version → "unknown". Render injects RENDER_GIT_COMMIT automatically, so a
//   deploy is traceable to a commit with no extra wiring.
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
        var explicitVersion = System.Environment.GetEnvironmentVariable("OPSTRAX_DEPLOY_VERSION");
        if (!string.IsNullOrWhiteSpace(explicitVersion)) return explicitVersion.Trim();

        var renderCommit = System.Environment.GetEnvironmentVariable("RENDER_GIT_COMMIT");
        if (!string.IsNullOrWhiteSpace(renderCommit))
            return renderCommit.Trim().Length > 12 ? renderCommit.Trim()[..12] : renderCommit.Trim();

        var info = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info)) return info.Trim();

        return "unknown";
    }
}
