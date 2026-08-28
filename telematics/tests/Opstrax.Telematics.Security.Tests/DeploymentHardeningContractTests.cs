using System.Text.Json;
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

    // ── DEF-P1-15: the installer must be able to install what it references ────

    /// <summary>
    /// Every file <c>install.sh</c> installs must actually ship. The script runs under
    /// <c>set -euo pipefail</c>, so a missing artifact is not a warning — the run aborts at
    /// "Writing configuration", after it has already created the service account, the state
    /// directories and the outbox, leaving a half-provisioned box.
    /// </summary>
    [Fact]
    public void Installer_OnlyInstallsFilesThatShipAlongsideIt()
    {
        string script = Read("telematics", "deploy", "install.sh");
        string deployDir = Path.Combine(RepoRoot, "telematics", "deploy");

        var missing = new List<string>();
        foreach (string line in script.Split('\n'))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("install ", StringComparison.Ordinal)) continue;
            if (!trimmed.Contains("$HERE/", StringComparison.Ordinal)) continue;

            foreach (string token in trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!token.Contains("$HERE/", StringComparison.Ordinal)) continue;

                // Resolve "$HERE/<name>" (the deploy directory), stripping quotes and any
                // ${SERVICE} expansion the script performs.
                string name = token.Trim('"').Replace("$HERE/", string.Empty, StringComparison.Ordinal)
                    .Replace("${SERVICE}", "opstrax-telematics-gateway", StringComparison.Ordinal);
                if (name.Length == 0) continue;

                if (!File.Exists(Path.Combine(deployDir, name)))
                    missing.Add(name);
            }
        }

        Assert.True(missing.Count == 0,
            "install.sh installs files that are not present in telematics/deploy: " + string.Join(", ", missing));
    }

    /// <summary>
    /// The production config template must be COMMITTED, not merely present on the machine that
    /// happens to have built it. The repository ignores <c>appsettings.*.json</c> wholesale — a
    /// sensible default that would silently swallow this file, so that an operator cloning the
    /// repo gets an installer referencing a file that was never checked in. That is the original
    /// defect wearing a different hat, and existence-on-disk alone cannot detect it.
    /// </summary>
    [Fact]
    public void ProductionConfigTemplate_IsExemptedFromTheAppsettingsIgnoreRule()
    {
        string ignore = Read(".gitignore");
        string[] lines = ignore.Split('\n').Select(line => line.Trim()).ToArray();

        Assert.Contains("appsettings.*.json", lines);
        Assert.True(
            lines.Contains("!telematics/deploy/appsettings.Production.json"),
            ".gitignore ignores appsettings.*.json without exempting the telematics production " +
            "config template, so install.sh would reference a file that never ships.");

        // The exemption must come AFTER the rule it overrides, or git ignores it anyway.
        int ignoreRule = Array.IndexOf(lines, "appsettings.*.json");
        int exemption = Array.IndexOf(lines, "!telematics/deploy/appsettings.Production.json");
        Assert.True(exemption > ignoreRule,
            "the negation must follow the pattern it negates; git applies the last matching rule.");
    }

    /// <summary>
    /// The shipped production configuration is valid JSON, is the HTTPS (no-database) topology, and
    /// carries the exact placeholders the installer substitutes. A template whose placeholder text
    /// drifts from the installer's <c>sed</c> silently leaves REPLACE-ME in a running gateway.
    /// </summary>
    [Fact]
    public void ProductionConfigTemplate_IsValidAndCarriesTheInstallersPlaceholders()
    {
        string json = Read("telematics", "deploy", "appsettings.Production.json");
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        JsonElement gateway = document.RootElement.GetProperty("Gateway");
        JsonElement forward = gateway.GetProperty("Edge").GetProperty("Forward");

        // The public edge holds no database credentials: HTTPS forwarding, OpsTrax owns identity.
        Assert.Equal("Https", gateway.GetProperty("Edge").GetProperty("Egress").GetString());

        // Exactly the three values install.sh rewrites.
        Assert.Equal("REPLACE-ME", forward.GetProperty("GatewayId").GetString());
        Assert.Equal(5023, gateway.GetProperty("ListenPort").GetInt32());
        Assert.Contains("REPLACE-ME", forward.GetProperty("BaseUrl").GetString());

        string script = Read("telematics", "deploy", "install.sh");
        Assert.Contains("\\\"GatewayId\\\": \\\"REPLACE-ME\\\"", script, StringComparison.Ordinal);
        Assert.Contains("\\\"ListenPort\\\": 5023", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// No secret may live in the committed configuration. Both the HMAC secret and the outbox
    /// encryption key are supplied through the systemd EnvironmentFile; a value here would be
    /// committed to git, copied into backups and shipped to every edge box.
    /// </summary>
    [Fact]
    public void ProductionConfigTemplate_ContainsNoSecretValues()
    {
        string json = Read("telematics", "deploy", "appsettings.Production.json");
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        var offenders = new List<string>();
        CollectSecretKeys(document.RootElement, string.Empty, offenders);
        Assert.True(offenders.Count == 0,
            "secret-bearing keys must never appear in committed config: " + string.Join(", ", offenders));

        // And the environment-variable route is the one actually documented.
        string script = Read("telematics", "deploy", "install.sh");
        Assert.Contains("Gateway__Edge__Forward__Secret", script, StringComparison.Ordinal);
        Assert.Contains("Gateway__StoreForwardEncryptionKey", script, StringComparison.Ordinal);
    }

    private static void CollectSecretKeys(JsonElement element, string path, List<string> offenders)
    {
        string[] secretNames = { "secret", "password", "apikey", "token", "connectionstring", "storeforwardencryptionkey" };

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    // "//"-prefixed keys are documentation, not configuration.
                    if (property.Name.StartsWith("//", StringComparison.Ordinal)) continue;

                    if (secretNames.Contains(property.Name.ToLowerInvariant()))
                        offenders.Add($"{path}/{property.Name}");

                    CollectSecretKeys(property.Value, $"{path}/{property.Name}", offenders);
                }
                break;

            case JsonValueKind.Array:
                int index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                    CollectSecretKeys(item, $"{path}[{index++}]", offenders);
                break;
        }
    }
}
