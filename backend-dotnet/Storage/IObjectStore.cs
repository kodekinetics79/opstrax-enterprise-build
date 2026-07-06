namespace Opstrax.Api.Storage;

// ─────────────────────────────────────────────────────────────────────────────
// IObjectStore — provider-agnostic object storage for uploaded files (PODs,
// signatures, driver documents, insurance evidence).
//
// Backed by any S3-compatible service (Cloudflare R2, AWS S3, Backblaze B2, MinIO,
// Wasabi) via one config switch, with a local-filesystem provider for dev/tests.
// This is the durable, access-controlled store that replaces the old placeholder
// upload endpoints — a compliance requirement (PDPL/PIPEDA: know where personal
// documents live, control access, support erasure + residency).
//
// Design:
//   • Keys are ALWAYS tenant-scoped ("tenant/{companyId}/...") so a signed URL or
//     listing can never cross tenants.
//   • Downloads are served via short-lived SIGNED URLs — objects are private; the
//     bucket is never public. This satisfies "no public PII" and time-boxes access.
//   • Server-side encryption (SSE) is requested on write where the provider
//     supports it; combined with app-layer PII encryption this is defence in depth.
// ─────────────────────────────────────────────────────────────────────────────

public interface IObjectStore
{
    /// <summary>Provider name for health/diagnostics (e.g. "s3", "local").</summary>
    string Provider { get; }

    /// <summary>True when a real backing store is configured (else dev-local).</summary>
    bool IsConfigured { get; }

    /// <summary>Uploads bytes under <paramref name="key"/>. Returns the stored key.</summary>
    Task<string> PutAsync(string key, Stream content, string contentType, CancellationToken ct = default);

    /// <summary>Opens a stored object for reading (used by the authenticated proxy).</summary>
    Task<Stream> GetAsync(string key, CancellationToken ct = default);

    /// <summary>Deletes an object (used by erasure + retention). Idempotent.</summary>
    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>Deletes every object under a tenant prefix (offboarding / erasure).</summary>
    Task<int> DeletePrefixAsync(string prefix, CancellationToken ct = default);

    /// <summary>Returns a short-lived signed GET URL, or null if the provider needs
    /// proxying instead (local dev). Callers fall back to the authenticated proxy.</summary>
    Task<string?> SignedUrlAsync(string key, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Health probe — cheap round-trip to confirm the store is reachable.</summary>
    Task<bool> HealthCheckAsync(CancellationToken ct = default);
}
