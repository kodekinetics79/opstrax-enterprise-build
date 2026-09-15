using Opstrax.Api.Services;
using Tier = Opstrax.Api.Services.DeviceCompatibilityTruth.Tier;

namespace Opstrax.Tests;

public sealed class DeviceCompatibilityTruthTests
{
    private const string Sha = "294a20d294a20d294a20d294a20d294a20d294a2";

    private static readonly DeviceCompatibilityTruth.HardwareTuple ExactTuple =
        new("Pacific Track", "PT40", "rev-a", "1.2.3");

    private static DeviceCompatibilityTruth.IndependentAcceptance Acceptance(string reviewer, string discipline,
        string candidateSha = Sha, DeviceCompatibilityTruth.HardwareTuple? hardware = null) =>
        new(reviewer, discipline, candidateSha, hardware ?? ExactTuple);

    private static DeviceCompatibilityTruth.Evidence Evidence(
        string? sha = null,
        DeviceCompatibilityTruth.HardwareTuple? hardware = null,
        bool protocol = false, bool bench = false, bool route = false,
        bool recovery = false, bool soak24 = false, bool security = false,
        bool soak72 = false, bool install = false, bool procurement = false,
        bool rma = false,
        IReadOnlyList<DeviceCompatibilityTruth.IndependentAcceptance>? acceptances = null)
        => new(
            sha,
            hardware,
            protocol ? "evidence://protocol" : null,
            bench ? "evidence://bench" : null,
            route ? "evidence://route" : null,
            recovery ? "evidence://recovery" : null,
            soak24 ? "evidence://soak-24h" : null,
            security ? "evidence://security" : null,
            soak72 ? "evidence://soak-72h" : null,
            install ? "evidence://repeatable-install" : null,
            procurement ? "evidence://procurement" : null,
            rma ? "evidence://rma" : null,
            acceptances);

    [Theory]
    [InlineData("Registered")]
    [InlineData("Installed")]
    [InlineData("Verified")]
    [InlineData("Activated")]
    [InlineData("Online")]
    public void Registry_or_lifecycle_state_never_certifies_hardware(string state)
        => Assert.False(DeviceCompatibilityTruth.RegistryOrLifecycleStateCanCertifyHardware(state));

    [Fact]
    public void Registry_presence_without_physical_evidence_is_unverified()
    {
        var decision = DeviceCompatibilityTruth.Evaluate(Evidence());
        Assert.Equal(Tier.Unverified, decision.MaximumTier);
        Assert.Contains("exact-candidate-sha", decision.MissingEvidence);
        Assert.Contains("exact-hardware-tuple", decision.MissingEvidence);
    }

    [Theory]
    [InlineData("294a20d")]
    [InlineData("294A20D294A20D294A20D294A20D294A20D294A2")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggg")]
    public void Candidate_requires_exact_lowercase_forty_character_sha(string sha)
    {
        var decision = DeviceCompatibilityTruth.Evaluate(Evidence(
            sha: sha, hardware: ExactTuple, protocol: true, bench: true));
        Assert.Equal(Tier.Unverified, decision.MaximumTier);
        Assert.Contains("exact-candidate-sha", decision.MissingEvidence);
    }

    [Fact]
    public void Exact_candidate_protocol_and_bench_can_reach_pilot_but_not_certified()
    {
        var decision = DeviceCompatibilityTruth.Evaluate(Evidence(
            sha: Sha, hardware: ExactTuple, protocol: true, bench: true));
        Assert.Equal(Tier.Pilot, decision.MaximumTier);
        Assert.Contains("vehicle-route", decision.MissingEvidence);
        Assert.Contains("24h-soak", decision.MissingEvidence);
        Assert.Contains("independent-hardware-acceptance", decision.MissingEvidence);
        Assert.Contains("independent-sdet-acceptance", decision.MissingEvidence);
    }

    [Fact]
    public void Complete_physical_checks_without_independent_acceptance_stay_pilot()
    {
        var decision = DeviceCompatibilityTruth.Evaluate(Evidence(
            sha: Sha, hardware: ExactTuple, protocol: true, bench: true, route: true,
            recovery: true, soak24: true, security: true));
        Assert.Equal(Tier.Pilot, decision.MaximumTier);
        Assert.Contains("independent-hardware-acceptance", decision.MissingEvidence);
        Assert.Contains("independent-sdet-acceptance", decision.MissingEvidence);
        Assert.Contains("two-independent-perspectives", decision.MissingEvidence);
    }

    [Fact]
    public void Acceptances_for_other_sha_or_hardware_tuple_do_not_count()
    {
        var wrongFirmware = ExactTuple with { FirmwareVersion = "9.9.9" };
        var decision = DeviceCompatibilityTruth.Evaluate(Evidence(
            sha: Sha, hardware: ExactTuple, protocol: true, bench: true, route: true,
            recovery: true, soak24: true, security: true,
            acceptances:
            [
                Acceptance("hardware-reviewer", "hardware-certification", candidateSha: new string('a', 40)),
                Acceptance("sdet-reviewer", "independent-sdet", hardware: wrongFirmware),
            ]));
        Assert.Equal(Tier.Pilot, decision.MaximumTier);
        Assert.Contains("independent-hardware-acceptance", decision.MissingEvidence);
        Assert.Contains("independent-sdet-acceptance", decision.MissingEvidence);
    }

    [Fact]
    public void Same_reviewer_cannot_supply_two_independent_perspectives()
    {
        var decision = DeviceCompatibilityTruth.Evaluate(Evidence(
            sha: Sha, hardware: ExactTuple, protocol: true, bench: true, route: true,
            recovery: true, soak24: true, security: true,
            acceptances:
            [
                Acceptance("same-reviewer", "hardware-certification"),
                Acceptance("same-reviewer", "independent-sdet"),
            ]));
        Assert.Equal(Tier.Pilot, decision.MaximumTier);
        Assert.Contains("two-independent-perspectives", decision.MissingEvidence);
    }

    [Fact]
    public void Certified_compatible_requires_physical_security_and_two_bound_acceptances()
    {
        var decision = DeviceCompatibilityTruth.Evaluate(Evidence(
            sha: Sha, hardware: ExactTuple, protocol: true, bench: true, route: true,
            recovery: true, soak24: true, security: true,
            acceptances:
            [
                Acceptance("hardware-reviewer", "hardware-certification"),
                Acceptance("sdet-reviewer", "independent-sdet"),
            ]));
        Assert.Equal(Tier.CertifiedCompatible, decision.MaximumTier);
        Assert.Contains("72h-soak", decision.MissingEvidence);
        Assert.Contains("rma-support", decision.MissingEvidence);
    }

    [Fact]
    public void Production_supported_requires_72h_install_procurement_and_rma()
    {
        var decision = DeviceCompatibilityTruth.Evaluate(Evidence(
            sha: Sha, hardware: ExactTuple, protocol: true, bench: true, route: true,
            recovery: true, soak24: true, security: true,
            soak72: true, install: true, procurement: true, rma: true,
            acceptances:
            [
                Acceptance("hardware-reviewer", "hardware-certification"),
                Acceptance("sdet-reviewer", "independent-sdet"),
            ]));
        Assert.Equal(Tier.ProductionSupported, decision.MaximumTier);
        Assert.Empty(decision.MissingEvidence);
    }
}
