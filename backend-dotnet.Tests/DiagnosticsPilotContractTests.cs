using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Security;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class DiagnosticsPilotContractTests
{
    [Fact]
    public void Fault_ingest_is_body_bound_replay_safe_and_encrypted_at_rest()
    {
        var source = HandlerSource();

        Assert.Contains("TelemetryHmacHelper.Sha256Hex(rawBody)", source, StringComparison.Ordinal);
        Assert.Contains("TelemetryHmacHelper.ComputeSignature", source, StringComparison.Ordinal);
        Assert.Contains("telemetry_nonces(device_id,nonce)", source, StringComparison.Ordinal);
        Assert.Contains("hmac_secret_encrypted", source, StringComparison.Ordinal);
        Assert.Contains("DeviceHmacSecretProtection.ResolveForVerification", source, StringComparison.Ordinal);
        Assert.Contains("allowLegacyPlaintext: false", source, StringComparison.Ordinal);
        Assert.DoesNotContain("d.hmac_secret\n", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Fault_ingest_uses_immutable_occurrence_and_atomic_safety_projection()
    {
        var source = HandlerSource();

        Assert.Contains("db.WithTransactionAsync", source, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO fault_occurrences", source, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (company_id,device_id,source_event_id,dtc_ordinal,canonical_dtc) DO NOTHING", source, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (company_id,device_id,protocol,canonical_identity)", source, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO diagnostic_holds", source, StringComparison.Ordinal);
        Assert.Contains("UPDATE vehicles SET out_of_service=true", source, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO diagnostic_holds", source, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO dvir_reports", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Diagnostic_holds_have_scoped_acknowledge_resolve_and_safe_vehicle_release_workflow()
    {
        var source = File.ReadAllText(Path.Combine(FindRoot(), "backend-dotnet", "Controllers", "EndpointMappings.cs"));
        Assert.Contains("/api/maintenance/diagnostic-holds", source, StringComparison.Ordinal);
        Assert.Contains("DiagnosticHoldAcknowledge", source, StringComparison.Ordinal);
        Assert.Contains("DiagnosticHoldResolve", source, StringComparison.Ordinal);
        Assert.Contains("resolutionNote is required", source, StringComparison.Ordinal);
        Assert.Contains("verificationType (technician_scan, provider_diagnostic, or service_record)", source, StringComparison.Ordinal);
        Assert.Contains("resolution_evidence_reference=@evidenceReference", source, StringComparison.Ordinal);
        Assert.Contains("status='resolved',cleared_at=NOW()", source, StringComparison.Ordinal);
        Assert.Contains("NOT EXISTS (SELECT 1 FROM diagnostic_holds", source, StringComparison.Ordinal);
        Assert.Contains("NOT EXISTS (SELECT 1 FROM dvir_defects", source, StringComparison.Ordinal);
        Assert.Contains("Resolved by authenticated device clear", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Fault_projection_is_canonical_multi_dtc_and_monotonic()
    {
        var source = HandlerSource();
        Assert.Contains("DiagnosticFaultNormalizer.TryNormalize", source, StringComparison.Ordinal);
        Assert.Contains("foreach (var dtc in normalized!.Dtcs)", source, StringComparison.Ordinal);
        Assert.Contains("last_observed_at < EXCLUDED.last_observed_at", source, StringComparison.Ordinal);
        Assert.Contains("last_source_event_id < EXCLUDED.last_source_event_id", source, StringComparison.Ordinal);
        Assert.Contains("DM2 is previously-active evidence", source, StringComparison.Ordinal);
        Assert.DoesNotContain("body.Severity", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Fault_state_reads_and_writes_derive_branch_from_authoritative_ownership()
    {
        var source = HandlerSource();

        Assert.Contains("v.branch_id vehicle_branch_id", source, StringComparison.Ordinal);
        Assert.Contains("Device and vehicle branch assignments conflict", source, StringComparison.Ordinal);
        Assert.Contains("(@branchId::BIGINT IS NULL OR fc.branch_id=@branchId)", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("enc:not-base64")]
    [InlineData("enc:AQ==")]
    [InlineData("plaintext-must-never-be-accepted")]
    public void Corrupt_or_plaintext_diagnostic_secret_fails_closed_without_throwing(string stored)
    {
        var protection = new PiiProtectionService(new TestKeyProvider(), NullLogger<PiiProtectionService>.Instance);

        using var keys = DeviceHmacSecretProtection.ResolveForVerification(
            protection, stored, previousEncrypted: null, previousValidUntil: null,
            legacyPlaintext: null, allowLegacyPlaintext: false, DateTimeOffset.UtcNow,
            NullLogger.Instance);

        Assert.Null(keys);
    }

    private static string HandlerSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var all = File.ReadAllText(Path.Combine(dir!.FullName, "backend-dotnet", "Controllers", "EndpointMappings.cs"));
        var start = all.IndexOf("private static async Task<IResult> MaintFaultCodeIngest", StringComparison.Ordinal);
        var end = all.IndexOf("// ── Maintenance insight builder", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return all[start..end];
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException();
    }
}
