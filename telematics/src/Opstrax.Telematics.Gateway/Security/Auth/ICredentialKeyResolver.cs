using Opstrax.Telematics.Contracts.Identity;

namespace Opstrax.Telematics.Gateway.Security.Auth;

/// <summary>
/// Dereferences an opaque <see cref="CredentialMaterial"/> (or raw handle string) into the
/// actual key bytes needed to verify a proof — for example fetching a per-device HMAC secret
/// from a vault/KMS. This is the <b>only</b> place a raw secret enters memory, and only for the
/// instant of verification; it is never placed on a contract, log line or config value.
/// </summary>
/// <remarks>
/// <para><b>Fail-closed:</b> implementations MUST return <see langword="null"/> — never an empty
/// or guessed key — when the handle is unknown, the material is <see cref="CredentialKind.None"/>,
/// or the backing store is unavailable. The authenticator treats a null key as
/// <see cref="AuthReasonCode.CredentialUnavailable"/> and rejects.</para>
/// <para>Returned key bytes are the caller's to zero after use where practical.</para>
/// </remarks>
public interface ICredentialKeyResolver
{
    /// <summary>
    /// Resolves the symmetric key bytes for <paramref name="credential"/>, or
    /// <see langword="null"/> if it cannot be safely resolved.
    /// </summary>
    /// <param name="credential">The opaque handle to dereference. Never carries the secret itself.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    ValueTask<byte[]?> ResolveHmacKeyAsync(CredentialMaterial credential, CancellationToken cancellationToken = default);
}
