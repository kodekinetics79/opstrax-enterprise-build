namespace Opstrax.Telematics.Contracts.Identity;

/// <summary>
/// How a device proves — or fails to prove — that it is the device it claims to be,
/// at the point it presents an identity to the fabric.
/// </summary>
/// <remarks>
/// <para>
/// <b>Honesty note (read this).</b> Only three of these four modes provide
/// <em>cryptographic</em> device authentication. <see cref="ImeiAllowlistOnly"/> does
/// <b>not</b>: raw GT06/Concox (and most cheap trackers) carry no cryptographic proof at
/// the protocol level — the IMEI in the login frame is the sole identifier and it is
/// trivially spoofable. For those devices the "trust" that remains is <em>not</em> proof
/// of identity; it is a defence-in-depth stack: explicit provisioning + IMEI allowlist +
/// optional source-IP / SIM pinning + durable replay/sequence defence + behavioural
/// trust-scoring + quarantine on anomaly. Use <see cref="ProvidesCryptographicDeviceAuth"/>
/// to branch on this rather than hard-coding assumptions.
/// </para>
/// </remarks>
public enum DeviceAuthMode
{
    /// <summary>
    /// No cryptographic device proof. The device is trusted only because its self-asserted
    /// IMEI/serial matches an explicitly provisioned allowlist entry. <b>Spoofable</b>: any
    /// party that learns a valid IMEI can impersonate the device at the protocol level. This
    /// is the honest baseline for raw-TCP no-auth protocols (GT06, most Concox variants);
    /// residual risk is mitigated — never eliminated — by IP/SIM pinning, replay defence and
    /// trust-scoring. Always the lowest trust tier.
    /// </summary>
    ImeiAllowlistOnly = 0,

    /// <summary>
    /// Per-device symmetric proof: the device signs a canonical challenge/message with a
    /// unique per-device HMAC secret (never a fleet-wide secret). Cryptographic, revocable
    /// per device. Applicable where the transport can carry a per-message signature
    /// (HTTP ingest, capable firmware, mobile SDK).
    /// </summary>
    PerDeviceHmac = 1,

    /// <summary>
    /// Mutual TLS: the device (or its gateway) presents an X.509 client certificate whose
    /// fingerprint is pinned server-side. Authenticates the transport connection
    /// cryptographically. TLS is terminated at the edge; the verified fingerprint is handed
    /// to the authenticator out-of-band, not asserted by the device payload.
    /// </summary>
    MutualTls = 2,

    /// <summary>
    /// Asymmetric per-device client certificate / key pair where the server stores only the
    /// public key or cert fingerprint. Strongest tier: a database read no longer discloses
    /// the ability to forge (unlike a stored HMAC secret).
    /// </summary>
    ClientCertificate = 3,
}

/// <summary>
/// The trust tier a resolved device is granted, driven by its <see cref="DeviceAuthMode"/>.
/// This is a coarse, honest label for how much a downstream consumer should rely on the
/// identity being genuine.
/// </summary>
public enum DeviceTrustTier
{
    /// <summary>
    /// Spoofable identity (IMEI allowlist only, no cryptographic proof). Accept the data for
    /// operational use but treat the identity as unproven; keep behavioural scoring and
    /// quarantine armed. This is the correct tier for raw GT06/Concox trackers.
    /// </summary>
    LowSpoofable = 0,

    /// <summary>Cryptographic per-device symmetric proof (HMAC). Identity is proven per message.</summary>
    Cryptographic = 1,

    /// <summary>Cryptographic proof plus asymmetric key custody (client cert / mTLS). Highest assurance.</summary>
    StrongCryptographic = 2,
}

/// <summary>Helpers that classify an <see cref="DeviceAuthMode"/> honestly.</summary>
public static class DeviceAuthModeExtensions
{
    /// <summary>
    /// <see langword="true"/> only for modes that cryptographically authenticate the device.
    /// <see cref="DeviceAuthMode.ImeiAllowlistOnly"/> returns <see langword="false"/> — it is a
    /// spoofable identifier match, not proof of identity.
    /// </summary>
    public static bool ProvidesCryptographicDeviceAuth(this DeviceAuthMode mode) => mode switch
    {
        DeviceAuthMode.PerDeviceHmac => true,
        DeviceAuthMode.MutualTls => true,
        DeviceAuthMode.ClientCertificate => true,
        _ => false, // ImeiAllowlistOnly and anything unknown: fail closed to "not cryptographic".
    };

    /// <summary>Maps a mode to the coarse <see cref="DeviceTrustTier"/> a successful auth confers.</summary>
    public static DeviceTrustTier TrustTier(this DeviceAuthMode mode) => mode switch
    {
        DeviceAuthMode.ImeiAllowlistOnly => DeviceTrustTier.LowSpoofable,
        DeviceAuthMode.PerDeviceHmac => DeviceTrustTier.Cryptographic,
        DeviceAuthMode.MutualTls => DeviceTrustTier.StrongCryptographic,
        DeviceAuthMode.ClientCertificate => DeviceTrustTier.StrongCryptographic,
        _ => DeviceTrustTier.LowSpoofable,
    };
}
