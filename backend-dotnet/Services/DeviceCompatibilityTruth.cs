namespace Opstrax.Api.Services;

/// <summary>
/// Canonical product-truth model for hardware compatibility/support status.
/// Device registry/lifecycle state (Provisioned, Installed, Online, etc.) is
/// deliberately orthogonal to certification/support status.
/// </summary>
public static class DeviceCompatibilityTruth
{
    public enum Tier
    {
        Unverified = 0,
        Pilot = 1,
        CertifiedCompatible = 2,
        ProductionSupported = 3,
        Deprecated = 4,
        Retired = 5,
    }

    public sealed record Evidence(
        bool ExactHardwareTupleFrozen,
        bool RealProtocolIdentified,
        bool BenchPassed,
        bool VehicleRoutePassed,
        bool FailureRecoveryPassed,
        bool Soak24HoursPassed,
        bool SecurityReviewPassed,
        bool Soak72HoursPassed,
        bool RepeatableInstallPassed,
        bool ProcurementReady,
        bool RmaSupportReady);

    public sealed record Decision(Tier MaximumTier, IReadOnlyList<string> MissingEvidence);

    public static Decision Evaluate(Evidence evidence)
    {
        var certifiedMissing = MissingCertifiedCompatibleEvidence(evidence);
        if (certifiedMissing.Count > 0)
        {
            // A real protocol + bench can justify a controlled Pilot classification,
            // but registry presence or connection alone never does.
            var pilot = evidence.ExactHardwareTupleFrozen && evidence.RealProtocolIdentified && evidence.BenchPassed;
            return new Decision(pilot ? Tier.Pilot : Tier.Unverified, certifiedMissing);
        }

        var productionMissing = MissingProductionSupportEvidence(evidence);
        return productionMissing.Count == 0
            ? new Decision(Tier.ProductionSupported, Array.Empty<string>())
            : new Decision(Tier.CertifiedCompatible, productionMissing);
    }

    public static bool RegistryOrLifecycleStateCanCertifyHardware(string? _)
        => false;

    private static List<string> MissingCertifiedCompatibleEvidence(Evidence e)
    {
        var missing = new List<string>();
        if (!e.ExactHardwareTupleFrozen) missing.Add("exact-hardware-tuple");
        if (!e.RealProtocolIdentified) missing.Add("real-protocol-identification");
        if (!e.BenchPassed) missing.Add("bench");
        if (!e.VehicleRoutePassed) missing.Add("vehicle-route");
        if (!e.FailureRecoveryPassed) missing.Add("failure-recovery");
        if (!e.Soak24HoursPassed) missing.Add("24h-soak");
        if (!e.SecurityReviewPassed) missing.Add("security-review");
        return missing;
    }

    private static List<string> MissingProductionSupportEvidence(Evidence e)
    {
        var missing = new List<string>();
        if (!e.Soak72HoursPassed) missing.Add("72h-soak");
        if (!e.RepeatableInstallPassed) missing.Add("repeatable-install");
        if (!e.ProcurementReady) missing.Add("procurement");
        if (!e.RmaSupportReady) missing.Add("rma-support");
        return missing;
    }
}
