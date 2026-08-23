using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Telematics.Gateway.Edge;

namespace Opstrax.Telematics.Security.Tests;

/// <summary>
/// Pins the deployment-surface hardening contracts for the public device edge: the artifacts an
/// operator actually runs (installer, example allowlist, service manifests) must never be able to
/// reintroduce the defects they were remediated for — a seeded allowlist (DEF-003), a secret on a
/// world-readable command line (DEF-004a), or the retired fleet-wide gateway secret whose mere
/// presence bricks the API in protected environments (DEF-004b).
/// </summary>
/// <remarks>
/// The repo files are read directly (read-only) so the tests break the moment a manifest or the
/// installer regresses, not the first time an operator follows it.
/// </remarks>
public sealed class DeploymentHardeningContractTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../"));

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    // ── DEF-003: the installer must never invent an allowlist ─────────────────

    [Fact]
    public void Installer_NeverSeedsTheAllowlistFromTheExampleFile()
    {
        string script = Read("telematics", "deploy", "install.sh");

        // The example file may be NAMED in guidance text, but never copied into service: no
        // install/cp of it, under any quoting.
        foreach (string line in script.Split('\n'))
        {
            if (!line.Contains("imei-allowlist.example", StringComparison.Ordinal)) continue;

            string trimmed = line.TrimStart();
            Assert.False(
                trimmed.StartsWith("install", StringComparison.Ordinal) ||
                trimmed.StartsWith("cp", StringComparison.Ordinal) ||
                trimmed.Contains("install ", StringComparison.Ordinal) && trimmed.Contains("$CONF_DIR", StringComparison.Ordinal),
                $"install.sh copies the example allowlist into service: {line.Trim()}");
        }

        // And the replacement contract is present: an explicit allowlist source is required.
        Assert.Contains("--allowlist", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ExampleAllowlist_ParsesToZeroAdmissibleEntries_ThroughTheRealParser()
    {
        string path = Path.Combine(RepoRoot, "telematics", "deploy", "imei-allowlist.example.txt");
        Assert.True(File.Exists(path), $"example allowlist missing: {path}");

        // The REAL admission parser, not a re-implementation: if the example ever gains a line the
        // gateway would admit, this fails — even a hand-copied example must admit nothing.
        var allowlist = new ImeiAllowlist(
            new AllowlistOptions { Path = path },
            NullLogger.Instance);

        Assert.False(allowlist.IsFileFaulted); // readable file, deliberately empty of admissible entries
        Assert.Equal(0, allowlist.Count);

        foreach (string line in File.ReadAllLines(path))
        {
            Assert.False(
                allowlist.IsAllowed(line),
                $"example allowlist line would be admitted if hand-copied: '{line}'");
        }
    }

    [Fact]
    public void ExampleAllowlist_NoLongerShipsThePilotImei()
    {
        // The original example carried a realistic pilot-device IMEI, which the installer then
        // copied into service — a spoofable identifier pre-admitted on a public port.
        string text = Read("telematics", "deploy", "imei-allowlist.example.txt");
        Assert.DoesNotContain("862464068456321", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DeployCompose_RefusesToStartWithoutAnExplicitAllowlistFile()
    {
        string compose = Read("telematics", "deploy", "docker-compose.yml");

        // The old bind-mount pointed at a repo path that does not exist, so Docker silently
        // created a DIRECTORY and the gateway failed closed with a confusing cause.
        Assert.DoesNotContain("./telematics/deploy/imei-allowlist.txt", compose, StringComparison.Ordinal);
        Assert.Contains("${OPSTRAX_IMEI_ALLOWLIST:?", compose, StringComparison.Ordinal);
    }

    // ── DEF-004a: no secret may travel through argv ───────────────────────────

    [Fact]
    public void Installer_AcceptsNoSecretOnTheCommandLine()
    {
        string script = Read("telematics", "deploy", "install.sh");

        // No case arm may consume a secret VALUE from argv. The sanctioned channels are the
        // environment variable and a root-readable file.
        Assert.DoesNotContain("--secret)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET=\"$2\"", script, StringComparison.Ordinal);
        Assert.Contains("OPSTRAX_GATEWAY_SECRET", script, StringComparison.Ordinal);
        Assert.Contains("--secret-file", script, StringComparison.Ordinal);
    }

    [Fact]
    public void GatewayCompositionRoot_HasNoCommandLineConfigurationSource()
    {
        string program = Read("telematics", "src", "Opstrax.Telematics.Gateway", "Program.cs");

        // Host.CreateApplicationBuilder(args) registers AddCommandLine, which would let
        // Gateway:Edge:Forward:Secret (or the outbox key) arrive via world-readable argv.
        Assert.DoesNotContain("CreateApplicationBuilder(args)", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AddCommandLine", program, StringComparison.Ordinal);
        Assert.Contains("Host.CreateApplicationBuilder()", program, StringComparison.Ordinal);
    }

    // ── DEF-004b: the retired fleet-wide telemetry secrets must stay retired ──

    [Theory]
    [InlineData("render.yaml")]
    [InlineData("docker-compose.yml")]
    public void ServiceManifests_DoNotDeclareTheRetiredTelemetrySecretKeys(string manifest)
    {
        // ConfigValidationService fails closed on any non-blank Telemetry:GatewaySecret in
        // Staging/Production, so a manifest declaring the key instructs operators to brick the
        // API; the device-secret sibling was never consumed by ingest at all.
        string text = Read(manifest);

        Assert.DoesNotContain("Telemetry__GatewaySecret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Telemetry__DeviceSecret", text, StringComparison.Ordinal);
    }
}
