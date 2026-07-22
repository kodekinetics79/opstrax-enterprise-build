namespace Opstrax.Telematics.Contracts.Identity;

/// <summary>The kind of credential a <see cref="CredentialMaterial"/> handle points at.</summary>
public enum CredentialKind
{
    /// <summary>No credential material — the device authenticates by allowlisted identifier only (spoofable).</summary>
    None = 0,

    /// <summary>A per-device symmetric HMAC key (dereferenced from a vault/KMS at verify time).</summary>
    PreSharedHmacKey = 1,

    /// <summary>An X.509 client-certificate fingerprint (SHA-256 of the DER). Comparison value, not a secret.</summary>
    ClientCertFingerprint = 2,

    /// <summary>An asymmetric public key reference. Public by nature; never enables forgery if disclosed.</summary>
    PublicKey = 3,
}

/// <summary>
/// An <b>opaque handle</b> to a device's authentication material. It carries a
/// <see cref="Kind"/> and a dereferenceable <see cref="Handle"/> (for example a
/// <c>vault://…</c> URI, a KMS key id, or a cert-fingerprint lookup key) — <b>never the raw
/// secret</b>. The secret (if any) is resolved out-of-band by the authentication stage,
/// immediately before use, and never lives on the contract surface, in logs, or in config.
/// </summary>
/// <remarks>
/// <para>
/// This exists so that ownership/trust records can be passed around, cached and logged without
/// ever risking secret exposure (threat I1). <see cref="ToString"/> is deliberately redacted.
/// A <see cref="Kind"/> of <see cref="CredentialKind.None"/> denotes the honest raw-tracker
/// case: there is no credential, only a spoofable identifier.
/// </para>
/// </remarks>
/// <param name="Kind">What the handle refers to.</param>
/// <param name="Handle">
/// An opaque, dereferenceable reference (vault URI, KMS key id, fingerprint lookup key). NOT a
/// secret. For <see cref="CredentialKind.ClientCertFingerprint"/> this may itself be the public
/// fingerprint (a comparison value, safe to hold). For symmetric/private material it is only a
/// pointer to where the secret is custodied.
/// </param>
public readonly record struct CredentialMaterial(CredentialKind Kind, string? Handle)
{
    /// <summary>The "no credential" handle for allowlist-only (spoofable) devices.</summary>
    public static readonly CredentialMaterial None = new(CredentialKind.None, null);

    /// <summary><see langword="true"/> when there is a non-empty handle to dereference.</summary>
    public bool HasHandle => Kind != CredentialKind.None && !string.IsNullOrWhiteSpace(Handle);

    /// <summary>
    /// Wraps an existing opaque credential handle string (as already carried on
    /// <see cref="ResolvedDeviceOwner.CredentialHandle"/>) as pre-shared HMAC key material.
    /// </summary>
    public static CredentialMaterial Hmac(string handle) => new(CredentialKind.PreSharedHmacKey, handle);

    /// <summary>Redacted on purpose: the handle is a pointer, but we still never print it verbatim.</summary>
    public override string ToString() =>
        Kind == CredentialKind.None ? "CredentialMaterial(None)" : $"CredentialMaterial({Kind}, handle=<redacted>)";
}
