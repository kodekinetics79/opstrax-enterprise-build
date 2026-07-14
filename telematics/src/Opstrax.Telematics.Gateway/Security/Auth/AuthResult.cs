using Opstrax.Telematics.Contracts.Identity;

namespace Opstrax.Telematics.Gateway.Security.Auth;

/// <summary>The three terminal outcomes of a device authentication decision.</summary>
public enum AuthOutcome
{
    /// <summary>The login satisfied every applicable control. Proceed to ingest (subject to replay defence).</summary>
    Authenticated = 0,

    /// <summary>The login is refused. Do not ingest. No state change to the device.</summary>
    Rejected = 1,

    /// <summary>
    /// The login is refused <em>and</em> the device should be isolated for investigation
    /// (suspected spoofing/tamper/SIM-swap). Data retained but not trusted; caller drives the
    /// device toward <see cref="Opstrax.Telematics.Contracts.Lifecycle.DeviceLifecycleState.Quarantined"/>.
    /// </summary>
    Quarantine = 2,
}

/// <summary>A stable, machine-comparable reason code for an auth decision (safe to log/metric).</summary>
public enum AuthReasonCode
{
    /// <summary>Authenticated successfully.</summary>
    Ok = 0,

    /// <summary>The device's lifecycle state does not permit authentication (e.g. Suspended, Retired, pre-connection).</summary>
    LifecycleNotAllowed = 1,

    /// <summary>The device is administratively suspended.</summary>
    Suspended = 2,

    /// <summary>The device identity is retired/terminal.</summary>
    Retired = 3,

    /// <summary>The login presented no identifier to allowlist against.</summary>
    NoIdentifierPresented = 4,

    /// <summary>The resolved owner is not a real allowlist entry (empty device id / unresolved identity).</summary>
    NotOnAllowlist = 5,

    /// <summary>Source IP is required to match a pinned CIDR and did not (or was unknown).</summary>
    SourceIpNotPinned = 6,

    /// <summary>A per-device HMAC proof was required but not presented.</summary>
    HmacProofMissing = 7,

    /// <summary>The per-device HMAC key could not be resolved from its handle (fail closed).</summary>
    CredentialUnavailable = 8,

    /// <summary>The presented HMAC signature did not verify.</summary>
    HmacInvalid = 9,

    /// <summary>The device is already quarantined by policy.</summary>
    AlreadyQuarantined = 10,

    /// <summary>The reported SIM identity (ICCID/IMSI) differs from the pinned value — SIM-swap anomaly.</summary>
    SimPinMismatch = 11,
}

/// <summary>
/// The immutable result of a device authentication decision: an <see cref="Outcome"/>, a stable
/// <see cref="Code"/>, a human <see cref="Detail"/>, and — importantly — an honest
/// <see cref="IsCryptographicallyAuthenticated"/> flag plus the <see cref="TrustTier"/> granted.
/// </summary>
/// <remarks>
/// <para>
/// <b>Honesty is encoded in the result.</b> A successful login under
/// <see cref="DeviceAuthMode.ImeiAllowlistOnly"/> returns
/// <see cref="AuthOutcome.Authenticated"/> but with
/// <see cref="IsCryptographicallyAuthenticated"/> = <see langword="false"/> and
/// <see cref="TrustTier"/> = <see cref="DeviceTrustTier.LowSpoofable"/>. Consumers must not
/// treat that as proof of identity — the IMEI is spoofable and only defence-in-depth
/// (pinning, replay, scoring, quarantine) stands behind it.
/// </para>
/// </remarks>
public readonly record struct AuthResult
{
    private AuthResult(AuthOutcome outcome, AuthReasonCode code, string detail, bool cryptographic, DeviceTrustTier tier)
    {
        Outcome = outcome;
        Code = code;
        Detail = detail;
        IsCryptographicallyAuthenticated = cryptographic;
        TrustTier = tier;
    }

    /// <summary>The terminal decision.</summary>
    public AuthOutcome Outcome { get; }

    /// <summary>Stable machine reason code.</summary>
    public AuthReasonCode Code { get; }

    /// <summary>Human-readable detail. Never contains secrets or reveals credential validity to the device.</summary>
    public string Detail { get; }

    /// <summary>
    /// <see langword="true"/> only when the accepted login was backed by a cryptographic device
    /// proof (HMAC / mTLS / client cert). Always <see langword="false"/> for the spoofable
    /// IMEI-allowlist baseline, and for any non-authenticated outcome.
    /// </summary>
    public bool IsCryptographicallyAuthenticated { get; }

    /// <summary>The trust tier granted by this decision. Meaningful only when <see cref="IsAuthenticated"/>.</summary>
    public DeviceTrustTier TrustTier { get; }

    /// <summary><see langword="true"/> when the outcome is <see cref="AuthOutcome.Authenticated"/>.</summary>
    public bool IsAuthenticated => Outcome == AuthOutcome.Authenticated;

    /// <summary>Builds an accept result carrying its honest trust tier and crypto flag.</summary>
    public static AuthResult Authenticated(bool cryptographic, DeviceTrustTier tier) =>
        new(AuthOutcome.Authenticated, AuthReasonCode.Ok, "authenticated", cryptographic, tier);

    /// <summary>Builds a reject result. No trust is granted.</summary>
    public static AuthResult Rejected(AuthReasonCode code, string detail) =>
        new(AuthOutcome.Rejected, code, detail, cryptographic: false, DeviceTrustTier.LowSpoofable);

    /// <summary>Builds a quarantine result. No trust is granted; the caller should isolate the device.</summary>
    public static AuthResult Quarantine(AuthReasonCode code, string detail) =>
        new(AuthOutcome.Quarantine, code, detail, cryptographic: false, DeviceTrustTier.LowSpoofable);
}
