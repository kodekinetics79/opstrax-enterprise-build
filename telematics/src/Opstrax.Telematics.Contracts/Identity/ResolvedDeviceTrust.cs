namespace Opstrax.Telematics.Contracts.Identity;

/// <summary>
/// Composes the three registry-sourced trust facts for a device into one record:
/// <b>who it belongs to</b> (<see cref="Owner"/>), <b>what checks apply</b>
/// (<see cref="Policy"/>) and <b>where its credential lives</b> (<see cref="Credential"/>).
/// This is the input an authenticator needs, assembled without rewriting the existing
/// <see cref="ResolvedDeviceOwner"/> — it composes it rather than replacing it.
/// </summary>
/// <remarks>
/// <para>
/// Like <see cref="ResolvedDeviceOwner"/>, every field here is sourced from the registry, never
/// from the packet. Holding a <see cref="ResolvedDeviceTrust"/> means "the registry recognises
/// and describes this device"; it does <b>not</b> mean the device has authenticated — that is
/// the authenticator's job, and for <see cref="DeviceAuthMode.ImeiAllowlistOnly"/> the identity
/// is never cryptographically proven at all (see <see cref="DeviceTrustPolicy"/>).
/// </para>
/// </remarks>
/// <param name="Owner">The tenant/company/vehicle binding and lifecycle state.</param>
/// <param name="Policy">The per-device trust policy (auth mode, pins, replay requirement).</param>
/// <param name="Credential">
/// The opaque credential handle. Never the raw secret. For
/// <see cref="DeviceAuthMode.ImeiAllowlistOnly"/> this is typically
/// <see cref="CredentialMaterial.None"/>.
/// </param>
public readonly record struct ResolvedDeviceTrust(
    ResolvedDeviceOwner Owner,
    DeviceTrustPolicy Policy,
    CredentialMaterial Credential)
{
    /// <summary>Convenience: the coarse trust tier implied by the policy's auth mode.</summary>
    public DeviceTrustTier TrustTier => Policy.TrustTier;

    /// <summary>
    /// Convenience: <see langword="true"/> only when the device is cryptographically
    /// authenticated by its auth mode. <see langword="false"/> for the spoofable IMEI baseline.
    /// </summary>
    public bool IsCryptographicallyIdentified => Policy.IsCryptographicDeviceAuth;
}
