namespace Opstrax.Telematics.Contracts.Identity;

/// <summary>
/// Resolves a self-asserted <see cref="DeviceIdentityRef"/> into the trusted
/// <see cref="ResolvedDeviceOwner"/> record held by the fabric's device registry.
/// This is the single choke point where ownership is established, and it is the
/// only source of truth for "who does this device belong to".
/// </summary>
/// <remarks>
/// <para><b>Fail-closed contract.</b> Implementations MUST:</para>
/// <list type="bullet">
///   <item><description>Resolve ownership <b>only</b> from the registry, never from
///     fields carried in the packet. The <see cref="DeviceIdentityRef"/> is an
///     untrusted lookup key.</description></item>
///   <item><description>Return <see langword="null"/> — not a fabricated or default
///     owner — when the identity is unknown, ambiguous (for example a duplicate IMEI
///     that no operator has disambiguated), or when the registry is unavailable. A
///     null result MUST cause the caller to reject the event, never to invent a
///     tenant.</description></item>
///   <item><description>Never throw to signal "not found"; reserve exceptions for
///     genuine infrastructure faults. Treat a thrown exception as fail-closed at the
///     call site.</description></item>
/// </list>
/// </remarks>
public interface IDeviceRegistry
{
    /// <summary>
    /// Attempts to resolve the owner of the device that presented
    /// <paramref name="identity"/>.
    /// </summary>
    /// <param name="identity">The untrusted identity claim extracted from the frame.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>
    /// The resolved owner, or <see langword="null"/> when the identity cannot be
    /// unambiguously and safely resolved. Callers MUST treat <see langword="null"/> as
    /// "reject" (fail closed).
    /// </returns>
    ValueTask<ResolvedDeviceOwner?> ResolveAsync(DeviceIdentityRef identity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the full per-device TRUST record — owner + trust policy + credential handle — for
    /// an identity claim. The trust policy (auth mode, source-IP/SIM pins, replay requirement) is
    /// sourced from the REGISTRY <em>per device</em>; the enforcement stage MUST use this rather
    /// than a global default, so a device provisioned with pins or a cryptographic auth mode is
    /// actually held to it. Same fail-closed contract as <see cref="ResolveAsync"/>: an unknown,
    /// ambiguous, or unavailable identity resolves to <see langword="null"/> and MUST be rejected.
    /// </summary>
    /// <param name="identity">The untrusted identity claim extracted from the frame.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The resolved trust record, or <see langword="null"/> (fail closed).</returns>
    ValueTask<ResolvedDeviceTrust?> ResolveTrustAsync(DeviceIdentityRef identity, CancellationToken cancellationToken = default);
}
