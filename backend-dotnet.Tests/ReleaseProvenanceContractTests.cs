namespace Opstrax.Tests;

public sealed class ReleaseProvenanceContractTests
{
    [Fact]
    public void ProductionApiDockerfilesPackageTerminalPilotMigrations()
    {
        foreach (var dockerfile in new[] { Read("Dockerfile"), Read("backend-dotnet", "Dockerfile") })
        foreach (var migration in new[]
        {
            "2026_07_16_stage42_telemetry_gateways.sql",
            "2026_07_22_stage47_detention_recovery.sql",
            "2026_08_02_stage68_entitlement_policy_mode.sql",
            "2026_08_02_stage69_market_pack_control_hardening.sql",
            "2026_08_02_stage70_hos_pilot_schema_reconciliation.sql",
            "2026_08_02_stage71_coaching_evidence_reconciliation.sql",
            "2026_08_02_stage72_hos_offboarding_immutability_reconciliation.sql",
            "2026_08_02_stage73_hos_offboarding_null_fail_closed.sql",
            "2026_08_02_stage74_retention_policy_production_contract.sql",
            "2026_08_02_stage75_bounded_support_access.sql",
            "2026_08_11_stage76_telematics_security_hardening.sql",
            "2026_08_12_stage77_protected_role_bootstrap.sql",
        }) Assert.Contains(migration, dockerfile, StringComparison.Ordinal);

        var immutableEvidenceReconciliation = Read(
            "database", "migrations", "2026_08_02_stage72_hos_offboarding_immutability_reconciliation.sql");
        Assert.Contains("stage65_guard_hos_certification_snapshot", immutableEvidenceReconciliation, StringComparison.Ordinal);
        Assert.Contains("detention_evidence_immutable", immutableEvidenceReconciliation, StringComparison.Ordinal);
        Assert.Contains("COALESCE(current_setting('opstrax.offboarding', true) = 'on', FALSE)", immutableEvidenceReconciliation, StringComparison.Ordinal);
        Assert.Contains("pg_has_role(current_user, 'opstrax_system', 'MEMBER')", immutableEvidenceReconciliation, StringComparison.Ordinal);
        var nullSafeReconciliation = Read(
            "database", "migrations", "2026_08_02_stage73_hos_offboarding_null_fail_closed.sql");
        Assert.Contains("COALESCE(current_setting('opstrax.offboarding', true) = 'on', FALSE)", nullSafeReconciliation, StringComparison.Ordinal);

        var detentionRuntimeSchema = Read("backend-dotnet", "Services", "DetentionSchemaService.cs");
        Assert.Contains("COALESCE(current_setting('opstrax.offboarding', true) = 'on', FALSE)", detentionRuntimeSchema, StringComparison.Ordinal);
        Assert.Contains("pg_has_role(current_user, 'opstrax_system', 'MEMBER')", detentionRuntimeSchema, StringComparison.Ordinal);
    }

    [Fact]
    public void CiAggregatesMandatoryExactShaGatesAndCycloneDxProvenance()
    {
        var workflow = Read(".github", "workflows", "ci.yml");
        Assert.Contains("exact-sha-release-evidence:", workflow, StringComparison.Ordinal);
        Assert.Contains("CANDIDATE_SHA: ${{ github.event.pull_request.head.sha || github.sha }}", workflow, StringComparison.Ordinal);
        Assert.Contains("ref: ${{ env.CANDIDATE_SHA }}", workflow, StringComparison.Ordinal);
        Assert.Contains("opstrax-release-candidate-${{ env.CANDIDATE_SHA }}", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet-integration-tests", workflow, StringComparison.Ordinal);
        Assert.Contains("production-shaped-release-rehearsal", workflow, StringComparison.Ordinal);
        Assert.Contains("release-container-builds", workflow, StringComparison.Ordinal);
        Assert.Contains("artifact-digest", workflow, StringComparison.Ordinal);
        Assert.Contains("--image api=opstrax-api:ci", workflow, StringComparison.Ordinal);
        Assert.Contains("--image frontend=opstrax-frontend:ci", workflow, StringComparison.Ordinal);
        Assert.Contains("--image gateway=opstrax-telematics-gateway:ci", workflow, StringComparison.Ordinal);
        Assert.Contains("2026_08_02_stage71_coaching_evidence_reconciliation.sql", workflow, StringComparison.Ordinal);
        Assert.Contains("2026_07_22_stage47_detention_recovery.sql", workflow, StringComparison.Ordinal);
        Assert.Contains("2026_08_02_stage72_hos_offboarding_immutability_reconciliation.sql", workflow, StringComparison.Ordinal);
        Assert.Contains("2026_08_02_stage73_hos_offboarding_null_fail_closed.sql", workflow, StringComparison.Ordinal);
        Assert.Contains("2026_08_02_stage74_retention_policy_production_contract.sql", workflow, StringComparison.Ordinal);
        Assert.Contains("2026_08_02_stage75_bounded_support_access.sql", workflow, StringComparison.Ordinal);
        Assert.Contains("2026_08_12_stage77_protected_role_bootstrap.sql", workflow, StringComparison.Ordinal);
        Assert.Contains("if: ${{ always() }}", workflow, StringComparison.Ordinal);
        Assert.Contains("opstrax-mandatory-gates-${{ env.CANDIDATE_SHA }}-${{ github.run_attempt }}", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("opstrax-release-candidate-${{ github.sha }}", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("git rev-parse HEAD)\" = \"$GITHUB_SHA\"", workflow, StringComparison.Ordinal);
        Assert.Contains("steps.gates.outputs.all_success == 'true'", workflow, StringComparison.Ordinal);
        Assert.Contains("validate-mandatory-ci-gates.sh --require-success", workflow, StringComparison.Ordinal);
        Assert.Contains("Enforce release evidence outcome", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void CiPinsThirdPartyExecutionInputsAndRatchetingWarningDebt()
    {
        var workflow = Read(".github", "workflows", "ci.yml");
        Assert.DoesNotContain("actions/checkout@v4", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("actions/setup-node@v4", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("actions/setup-dotnet@v4", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("actions/upload-artifact@v4", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("actions/download-artifact@v4", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("image: postgres:17\n", workflow, StringComparison.Ordinal);
        Assert.Contains("verify-dotnet-warning-baseline.sh", workflow, StringComparison.Ordinal);

        var pinValidator = Read("tools", "validate-ci-supply-chain-pins.sh");
        Assert.Contains("@[0-9a-f]{40}", pinValidator, StringComparison.Ordinal);
        Assert.Contains("@sha256:[0-9a-f]{64}", pinValidator, StringComparison.Ordinal);

        var warningGate = Read("tools", "verify-dotnet-warning-baseline.sh");
        Assert.Contains("Warning debt increased", warningGate, StringComparison.Ordinal);
        Assert.Contains("New warning code is not baselined", warningGate, StringComparison.Ordinal);
        Assert.Contains("--target:Rebuild", warningGate, StringComparison.Ordinal);
    }

    [Fact]
    public void ProvenanceCollectorSeparatesLocalIdentityFromPublishedDigestEvidence()
    {
        var collector = Read("tools", "collect-release-candidate-provenance.sh");
        Assert.Contains("git archive --format=tar HEAD", collector, StringComparison.Ordinal);
        Assert.Contains("migrations.sha256", collector, StringComparison.Ordinal);
        Assert.Contains("dependencies.sha256", collector, StringComparison.Ordinal);
        Assert.Contains("published_registry_digest", collector, StringComparison.Ordinal);
        Assert.Contains("NOT_EVIDENCED", collector, StringComparison.Ordinal);
        Assert.Contains("--require-registry-digest", collector, StringComparison.Ordinal);
        Assert.Contains("--format cyclonedx", collector, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "backend-dotnet")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray()));
    }
}
