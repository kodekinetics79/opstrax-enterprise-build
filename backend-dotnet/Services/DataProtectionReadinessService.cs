using Microsoft.AspNetCore.DataProtection;

namespace Opstrax.Api.Services;

public sealed record DataProtectionReadinessResult(bool Ready, long KeyCount, string? FailureCode);

public sealed class DataProtectionReadinessService(
    PostgresDataProtectionXmlRepository repository,
    IDataProtectionProvider provider)
{
    public async Task<DataProtectionReadinessResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            if (!await repository.HasExpectedSchemaContractAsync(ct))
                return new(false, 0, "data_protection_key_ring_schema_drift");
            _ = await repository.ProbeAsync(ct);
            var protector = provider.CreateProtector("opstrax.readiness.data-protection.v1");
            var marker = Guid.NewGuid().ToString("N");
            var protectedMarker = protector.Protect(marker);
            if (!string.Equals(marker, protector.Unprotect(protectedMarker), StringComparison.Ordinal))
                return new(false, 0, "data_protection_canary_mismatch");
            var keyCount = await repository.ProbeAsync(ct);
            if (keyCount < 1)
                return new(false, 0, "data_protection_key_ring_empty");
            return new(true, keyCount, null);
        }
        catch
        {
            return new(false, 0, "data_protection_key_ring_unavailable");
        }
    }
}
