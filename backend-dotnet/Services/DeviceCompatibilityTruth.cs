using System.Text.RegularExpressions;

namespace Opstrax.Api.Services;

/// <summary>
/// Canonical product-truth model for hardware compatibility/support status.
/// Device registry/lifecycle state (Provisioned, Installed, Online, etc.) is
/// deliberately orthogonal to certification/support status.
/// </summary>
public static partial class DeviceCompatibilityTruth
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

    public sealed record HardwareTuple(
        string? Manufacturer,
        string? Model,
        string? HardwareRevision,
        string? FirmwareVersion);

    public sealed record IndependentAcceptance(
        string? ReviewerId,
        string? Discipline,
        string? CandidateSha,
        HardwareTuple? Hardware);

    public sealed record Evidence(
        string? CandidateSha,
        HardwareTuple? Hardware,
        string? ProtocolEvidenceReference,
        string? BenchEvidenceReference,
        string? VehicleRouteEvidenceReference,
        string? FailureRecoveryEvidenceReference,
        string? Soak24HoursEvidenceReference,
        string? SecurityReviewReference,
        string? Soak72HoursEvidenceReference,
        string? RepeatableInstallEvidenceReference,
        string? ProcurementEvidenceReference,
        string? RmaSupportEvidenceReference,
        IReadOnlyList<IndependentAcceptance>? IndependentAcceptances);

    public sealed record Decision(Tier MaximumTier, IReadOnlyList<string> MissingEvidence);

    public static Decision Evaluate(Evidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var certifiedMissing = MissingCertifiedCompatibleEvidence(evidence);
        if (certifiedMissing.Count > 0)
        {
            // Exact frozen identity plus real protocol and bench references can justify
            // a controlled Pilot classification. Registry presence or device traffic
            // still cannot do so, and Pilot is never a certification claim.
            var pilot = HasExactCandidate(evidence)
                && Present(evidence.ProtocolEvidenceReference)
                && Present(evidence.BenchEvidenceReference);
            return new Decision(pilot ? Tier.Pilot : Tier.Unverified, certifiedMissing);
        }

        var productionMissing = MissingProductionSupportEvidence(evidence);
        return productionMissing.Count == 0
            ? new Decision(Tier.ProductionSupported, Array.Empty<string>())
            : new Decision(Tier.CertifiedCompatible, productionMissing);
    }

    public static bool RegistryOrLifecycleStateCanCertifyHardware(string? _)
        => false;

    public static string? CanonicalHardwareTuple(HardwareTuple? hardware)
    {
        if (!HasExactTuple(hardware)) return null;
        return string.Join('|', new[]
        {
            hardware!.Manufacturer,
            hardware.Model,
            hardware.HardwareRevision,
            hardware.FirmwareVersion,
        }.Select(value => value!.Trim().ToUpperInvariant()));
    }

    private static List<string> MissingCertifiedCompatibleEvidence(Evidence evidence)
    {
        var missing = new List<string>();
        if (!ExactSha().IsMatch(evidence.CandidateSha ?? "")) missing.Add("exact-candidate-sha");
        if (!HasExactTuple(evidence.Hardware)) missing.Add("exact-hardware-tuple");
        if (!Present(evidence.ProtocolEvidenceReference)) missing.Add("real-protocol-identification");
        if (!Present(evidence.BenchEvidenceReference)) missing.Add("bench");
        if (!Present(evidence.VehicleRouteEvidenceReference)) missing.Add("vehicle-route");
        if (!Present(evidence.FailureRecoveryEvidenceReference)) missing.Add("failure-recovery");
        if (!Present(evidence.Soak24HoursEvidenceReference)) missing.Add("24h-soak");
        if (!Present(evidence.SecurityReviewReference)) missing.Add("security-review");
        if (!HasIndependentAcceptance(evidence, "hardware-certification")) missing.Add("independent-hardware-acceptance");
        if (!HasIndependentAcceptance(evidence, "independent-sdet")) missing.Add("independent-sdet-acceptance");
        if (!HasTwoIndependentPerspectives(evidence)) missing.Add("two-independent-perspectives");
        return missing;
    }

    private static List<string> MissingProductionSupportEvidence(Evidence evidence)
    {
        var missing = new List<string>();
        if (!Present(evidence.Soak72HoursEvidenceReference)) missing.Add("72h-soak");
        if (!Present(evidence.RepeatableInstallEvidenceReference)) missing.Add("repeatable-install");
        if (!Present(evidence.ProcurementEvidenceReference)) missing.Add("procurement");
        if (!Present(evidence.RmaSupportEvidenceReference)) missing.Add("rma-support");
        return missing;
    }

    private static bool HasExactCandidate(Evidence evidence) =>
        ExactSha().IsMatch(evidence.CandidateSha ?? "") && HasExactTuple(evidence.Hardware);

    private static bool HasExactTuple(HardwareTuple? hardware) =>
        hardware is not null
        && Present(hardware.Manufacturer)
        && Present(hardware.Model)
        && Present(hardware.HardwareRevision)
        && Present(hardware.FirmwareVersion);

    private static bool HasIndependentAcceptance(Evidence evidence, string discipline) =>
        ValidAcceptances(evidence).Any(acceptance =>
            string.Equals(acceptance.Discipline?.Trim(), discipline, StringComparison.OrdinalIgnoreCase));

    private static bool HasTwoIndependentPerspectives(Evidence evidence)
    {
        var acceptances = ValidAcceptances(evidence).ToArray();
        return acceptances.Select(acceptance => acceptance.ReviewerId!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2
            && acceptances.Select(acceptance => acceptance.Discipline!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2;
    }

    private static IEnumerable<IndependentAcceptance> ValidAcceptances(Evidence evidence)
    {
        var candidateTuple = CanonicalHardwareTuple(evidence.Hardware);
        if (!ExactSha().IsMatch(evidence.CandidateSha ?? "") || candidateTuple is null)
            return [];

        return (evidence.IndependentAcceptances ?? []).Where(acceptance =>
            Present(acceptance.ReviewerId)
            && Present(acceptance.Discipline)
            && string.Equals(acceptance.CandidateSha, evidence.CandidateSha, StringComparison.Ordinal)
            && string.Equals(CanonicalHardwareTuple(acceptance.Hardware), candidateTuple, StringComparison.Ordinal));
    }

    private static bool Present(string? value) => !string.IsNullOrWhiteSpace(value);

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex ExactSha();
}
