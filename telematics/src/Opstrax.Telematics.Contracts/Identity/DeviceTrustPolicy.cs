namespace Opstrax.Telematics.Contracts.Identity;

/// <summary>
/// The per-device trust policy: the set of controls the fabric is willing to enforce for a
/// specific device, resolved from the registry alongside its owner. It is <b>configuration</b>,
/// not a secret and not a proof — it declares <em>which</em> checks apply, and the
/// authenticator applies them against an incoming login.
/// </summary>
/// <remarks>
/// <para>
/// <b>Trust model, stated plainly.</b> The strength of this policy is capped by
/// <see cref="AuthMode"/>. When <see cref="AuthMode"/> is
/// <see cref="DeviceAuthMode.ImeiAllowlistOnly"/> there is <b>no cryptographic device
/// authentication</b> — the IMEI is a spoofable bearer identifier. The remaining knobs
/// (<see cref="PinnedSourceCidrs"/>, <see cref="PinnedSimIccid"/>, <see cref="PinnedImsi"/>,
/// <see cref="RequireReplayDefense"/>) are defence-in-depth that <em>reduce</em> the spoofing
/// window; they do not close it. Residual risk: an attacker who learns a provisioned IMEI and
/// can source traffic from the pinned network/SIM can still impersonate the device. Treat such
/// devices as <see cref="DeviceTrustTier.LowSpoofable"/> and keep quarantine armed.
/// </para>
/// <para>This type has zero external dependencies by design — it is pure policy data.</para>
/// </remarks>
/// <param name="AuthMode">
/// The authentication mechanism the device is provisioned for. Determines the trust tier and
/// whether a cryptographic proof is required/verified.
/// </param>
/// <param name="PinnedSourceCidrs">
/// Optional source-IP allowlist in CIDR notation (e.g. <c>"203.0.113.0/24"</c>,
/// <c>"2001:db8::/32"</c>). When non-empty, the login's remote address MUST fall within one
/// of these ranges. Pinning a mobile-carrier NAT range narrows, but does not eliminate,
/// impersonation from that carrier. Null/empty means "no source-IP pin".
/// </param>
/// <param name="PinnedSimIccid">
/// Optional pinned SIM ICCID. When set, a login that reports a different ICCID is treated as a
/// SIM-swap anomaly. Null means "not pinned".
/// </param>
/// <param name="PinnedImsi">
/// Optional pinned IMSI. When set, a login that reports a different IMSI is treated as a
/// SIM-swap anomaly. Null means "not pinned".
/// </param>
/// <param name="RequireReplayDefense">
/// When <see langword="true"/> (the default), the ingest path MUST enforce durable, shared
/// replay/sequence defence for this device. The authenticator surfaces this requirement; the
/// durable nonce/sequence store enforces it. A raw tracker with no per-message crypto relies on
/// this as one of its few real defences, so it defaults on.
/// </param>
public readonly record struct DeviceTrustPolicy(
    DeviceAuthMode AuthMode,
    IReadOnlyList<string>? PinnedSourceCidrs = null,
    string? PinnedSimIccid = null,
    string? PinnedImsi = null,
    bool RequireReplayDefense = true)
{
    /// <summary><see langword="true"/> when a source-IP CIDR pin is configured.</summary>
    public bool HasSourceIpPin => PinnedSourceCidrs is { Count: > 0 };

    /// <summary><see langword="true"/> when either a SIM ICCID or IMSI pin is configured.</summary>
    public bool HasSimPin =>
        !string.IsNullOrWhiteSpace(PinnedSimIccid) || !string.IsNullOrWhiteSpace(PinnedImsi);

    /// <summary>
    /// <see langword="true"/> only when <see cref="AuthMode"/> cryptographically authenticates
    /// the device. Mirrors <see cref="DeviceAuthModeExtensions.ProvidesCryptographicDeviceAuth"/>
    /// so callers can gate on the policy directly. <see langword="false"/> for
    /// <see cref="DeviceAuthMode.ImeiAllowlistOnly"/> — the spoofable baseline.
    /// </summary>
    public bool IsCryptographicDeviceAuth => AuthMode.ProvidesCryptographicDeviceAuth();

    /// <summary>The coarse trust tier this policy's <see cref="AuthMode"/> confers.</summary>
    public DeviceTrustTier TrustTier => AuthMode.TrustTier();

    /// <summary>
    /// A convenience policy for the honest raw-tracker baseline: IMEI allowlist only, replay
    /// defence required, no crypto. Explicitly <see cref="DeviceTrustTier.LowSpoofable"/>.
    /// </summary>
    public static DeviceTrustPolicy ImeiAllowlistBaseline(
        IReadOnlyList<string>? pinnedSourceCidrs = null,
        string? pinnedSimIccid = null,
        string? pinnedImsi = null) =>
        new(DeviceAuthMode.ImeiAllowlistOnly, pinnedSourceCidrs, pinnedSimIccid, pinnedImsi, RequireReplayDefense: true);
}
