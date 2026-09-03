using Opstrax.Api.Services;
using Tier = Opstrax.Api.Services.DeviceCompatibilityTruth.Tier;

namespace Opstrax.Tests;

public sealed class DeviceCompatibilityTruthTests
{
    private static DeviceCompatibilityTruth.Evidence Evidence(
        bool tuple = false, bool protocol = false, bool bench = false,
        bool route = false, bool recovery = false, bool soak24 = false,
        bool security = false, bool soak72 = false, bool install = false,
        bool procurement = false, bool rma = false)
        => new(tuple, protocol, bench, route, recovery, soak24, security, soak72, install, procurement, rma);

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
        Assert.Contains("exact-hardware-tuple", decision.MissingEvidence);
    }

    [Fact]
    public void Tuple_protocol_and_bench_can_reach_pilot_but_not_certified()
    {
        var decision = DeviceCompatibilityTruth.Evaluate(Evidence(tuple: true, protocol: true, bench: true));
        Assert.Equal(Tier.Pilot, decision.MaximumTier);
        Assert.Contains("vehicle-route", decision.MissingEvidence);
        Assert.Contains("24h-soak", decision.MissingEvidence);
    }

    [Fact]
    public void Certified_compatible_requires_all_physical_and_security_evidence()
    {
        var decision = DeviceCompatibilityTruth.Evaluate(Evidence(
            tuple: true, protocol: true, bench: true, route: true,
            recovery: true, soak24: true, security: true));
        Assert.Equal(Tier.CertifiedCompatible, decision.MaximumTier);
        Assert.Contains("72h-soak", decision.MissingEvidence);
        Assert.Contains("rma-support", decision.MissingEvidence);
    }

    [Fact]
    public void Production_supported_requires_72h_install_procurement_and_rma()
    {
        var decision = DeviceCompatibilityTruth.Evaluate(Evidence(
            tuple: true, protocol: true, bench: true, route: true,
            recovery: true, soak24: true, security: true,
            soak72: true, install: true, procurement: true, rma: true));
        Assert.Equal(Tier.ProductionSupported, decision.MaximumTier);
        Assert.Empty(decision.MissingEvidence);
    }
}
