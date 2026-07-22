using System.Net;
using System.Security.Cryptography;
using Opstrax.Telematics.Contracts.Identity;
using Opstrax.Telematics.Contracts.Lifecycle;

namespace Opstrax.Telematics.Gateway.Security.Auth;

/// <summary>
/// The default, fail-closed <see cref="IDeviceAuthenticator"/>. It enforces, in order:
/// lifecycle eligibility, an identifier was presented, allowlist membership, source-IP CIDR pin,
/// SIM pin, and — for <see cref="DeviceAuthMode.PerDeviceHmac"/> — cryptographic HMAC
/// verification. Any failure short-circuits to reject/quarantine; only a login that clears every
/// applicable control is authenticated.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this can and cannot prove.</b> For <see cref="DeviceAuthMode.PerDeviceHmac"/>,
/// <see cref="DeviceAuthMode.MutualTls"/> and <see cref="DeviceAuthMode.ClientCertificate"/> the
/// accepted identity is backed by cryptography (HMAC verified here; TLS/cert verified at the edge
/// and asserted upstream). For <see cref="DeviceAuthMode.ImeiAllowlistOnly"/> — the honest raw
/// GT06/Concox baseline — <b>there is no cryptographic device authentication</b>. A pass means
/// only "a provisioned IMEI/serial presented itself from an acceptable network/SIM position and
/// no anomaly was detected". The IMEI is spoofable; the result is deliberately flagged
/// <see cref="DeviceTrustTier.LowSpoofable"/> with
/// <see cref="AuthResult.IsCryptographicallyAuthenticated"/> = <see langword="false"/>. Residual
/// risk is carried by durable replay defence (surfaced via the policy), behavioural
/// trust-scoring, and quarantine — none of which this component itself performs beyond raising
/// the quarantine signal on a SIM anomaly.
/// </para>
/// </remarks>
public sealed class DefaultDeviceAuthenticator : IDeviceAuthenticator
{
    // Lifecycle states from which a login may be authenticated. Deliberately excludes the
    // provisioning states (device has never been cleared for connection), Quarantined (isolated
    // by policy), Suspended (administratively disabled) and Retired (terminal).
    private static readonly IReadOnlySet<DeviceLifecycleState> Authenticatable = new HashSet<DeviceLifecycleState>
    {
        DeviceLifecycleState.AwaitingFirstConnection,
        DeviceLifecycleState.Identified,
        DeviceLifecycleState.Authenticated,
        DeviceLifecycleState.Validating,
        DeviceLifecycleState.Online,
        DeviceLifecycleState.Delayed,
        DeviceLifecycleState.Stale,
        DeviceLifecycleState.Offline,
        DeviceLifecycleState.Degraded,
    };

    private readonly ICredentialKeyResolver _keyResolver;

    /// <summary>Creates the authenticator with the resolver used to dereference HMAC key handles.</summary>
    /// <param name="keyResolver">Resolves opaque credential handles to key bytes. Required.</param>
    public DefaultDeviceAuthenticator(ICredentialKeyResolver keyResolver)
    {
        ArgumentNullException.ThrowIfNull(keyResolver);
        _keyResolver = keyResolver;
    }

    /// <inheritdoc />
    public async ValueTask<AuthResult> AuthenticateAsync(
        ResolvedDeviceOwner owner,
        DeviceTrustPolicy policy,
        DeviceLoginContext ctx,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 1. Lifecycle eligibility. Quarantined stays quarantined; Suspended/Retired are hard rejects.
        switch (owner.LifecycleState)
        {
            case DeviceLifecycleState.Quarantined:
                return AuthResult.Quarantine(AuthReasonCode.AlreadyQuarantined,
                    "device is quarantined pending investigation");
            case DeviceLifecycleState.Suspended:
                return AuthResult.Rejected(AuthReasonCode.Suspended, "device is administratively suspended");
            case DeviceLifecycleState.Retired:
                return AuthResult.Rejected(AuthReasonCode.Retired, "device identity is retired");
        }

        if (!Authenticatable.Contains(owner.LifecycleState))
            return AuthResult.Rejected(AuthReasonCode.LifecycleNotAllowed,
                "device lifecycle state does not permit authentication");

        // 2. An identifier must actually have been presented to allowlist against.
        if (!ctx.HasAnyIdentifier)
            return AuthResult.Rejected(AuthReasonCode.NoIdentifierPresented, "no device identifier presented");

        // 3. Allowlist membership. A real registry resolution carries a fabric device id; a
        //    default/empty owner means the identity was never provisioned — fail closed.
        if (string.IsNullOrWhiteSpace(owner.DeviceId))
            return AuthResult.Rejected(AuthReasonCode.NotOnAllowlist, "device identity is not on the allowlist");

        // 4. Source-IP CIDR pin (if configured). The remote address is the one thing the platform
        //    observes rather than the device asserts, so it is the only pin worth anything for a
        //    spoofable protocol — and even then only a network-position signal, not identity proof.
        if (policy.HasSourceIpPin && !SourceIpAllowed(policy.PinnedSourceCidrs!, ctx.RemoteIp))
            return AuthResult.Rejected(AuthReasonCode.SourceIpNotPinned,
                "source address is outside the pinned network range");

        // 5. SIM pin (if configured). A changed ICCID/IMSI is a SIM-swap signal → quarantine, not a
        //    plain reject, because it warrants investigation rather than a silent retry.
        if (policy.HasSimPin && !SimMatches(policy, ctx))
            return AuthResult.Quarantine(AuthReasonCode.SimPinMismatch,
                "reported SIM identity does not match the pinned value");

        // 6. Cryptographic proof, only where the mode provides it.
        if (policy.AuthMode == DeviceAuthMode.PerDeviceHmac)
        {
            AuthResult hmac = await VerifyHmacAsync(owner, ctx, cancellationToken).ConfigureAwait(false);
            if (!hmac.IsAuthenticated)
                return hmac;
        }

        // 7. Cleared every applicable control. Grant the honest trust tier for the mode: crypto
        //    modes are cryptographically authenticated; ImeiAllowlistOnly is explicitly not.
        return AuthResult.Authenticated(policy.IsCryptographicDeviceAuth, policy.TrustTier);
    }

    private async ValueTask<AuthResult> VerifyHmacAsync(
        ResolvedDeviceOwner owner,
        DeviceLoginContext ctx,
        CancellationToken cancellationToken)
    {
        if (ctx.HmacProof is not { } proof)
            return AuthResult.Rejected(AuthReasonCode.HmacProofMissing, "per-device HMAC proof was not presented");

        // Dereference the opaque handle to the actual key, only now, only for this verification.
        CredentialMaterial material = string.IsNullOrWhiteSpace(owner.CredentialHandle)
            ? CredentialMaterial.None
            : CredentialMaterial.Hmac(owner.CredentialHandle);

        byte[]? key = await _keyResolver.ResolveHmacKeyAsync(material, cancellationToken).ConfigureAwait(false);
        if (key is null || key.Length == 0)
            return AuthResult.Rejected(AuthReasonCode.CredentialUnavailable,
                "per-device credential could not be resolved");

        try
        {
            byte[] expected = HMACSHA256.HashData(key, proof.SignedMessage);
            // Constant-time comparison; FixedTimeEquals also safely handles length mismatch.
            if (!CryptographicOperations.FixedTimeEquals(expected, proof.Signature))
                return AuthResult.Rejected(AuthReasonCode.HmacInvalid, "per-device HMAC signature did not verify");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        return AuthResult.Authenticated(cryptographic: true, DeviceTrustTier.Cryptographic);
    }

    private static bool SourceIpAllowed(IReadOnlyList<string> cidrs, IPAddress? remote)
    {
        // Pinned but the transport gave us no address to check → fail closed.
        if (remote is null)
            return false;

        foreach (string cidr in cidrs)
        {
            if (string.IsNullOrWhiteSpace(cidr))
                continue;
            if (IPNetwork.TryParse(cidr.Trim(), out IPNetwork network) && network.Contains(remote))
                return true;
        }

        return false;
    }

    private static bool SimMatches(DeviceTrustPolicy policy, DeviceLoginContext ctx)
    {
        if (!string.IsNullOrWhiteSpace(policy.PinnedSimIccid) &&
            !string.Equals(policy.PinnedSimIccid, ctx.SimIccid, StringComparison.Ordinal))
            return false;

        if (!string.IsNullOrWhiteSpace(policy.PinnedImsi) &&
            !string.Equals(policy.PinnedImsi, ctx.Imsi, StringComparison.Ordinal))
            return false;

        return true;
    }
}
