using System.Net;
using System.Security.Cryptography;
using System.Text;
using Opstrax.Telematics.Contracts.Identity;
using Opstrax.Telematics.Contracts.Lifecycle;
using Opstrax.Telematics.Gateway.Identity;
using Opstrax.Telematics.Gateway.Security.Auth;

namespace Opstrax.Telematics.Security.Tests;

/// <summary>
/// Behavioural contract for <see cref="DefaultDeviceAuthenticator"/> and the honesty guarantees
/// of the trust model. These tests assert both the enforced controls (lifecycle, allowlist,
/// source-IP pin, SIM pin, per-device HMAC) and the explicit residual-risk labelling of the
/// spoofable IMEI-allowlist baseline.
/// </summary>
public class DefaultDeviceAuthenticatorTests
{
    private const string HmacHandle = "vault://opstrax/telematics/psk/dev-known-0001";
    private static readonly byte[] DeviceKey = Encoding.UTF8.GetBytes("a-32-byte-per-device-hmac-secret!");

    private static readonly Guid Tenant = Guid.Parse("2f1c9a54-8a0e-4a7d-9f3b-6d2c1e5b7a90");

    private static ResolvedDeviceOwner Owner(
        DeviceLifecycleState state = DeviceLifecycleState.Online,
        string deviceId = "dev-known-0001",
        string credentialHandle = HmacHandle) =>
        new(Tenant, 100L, deviceId, 5501L, state, credentialHandle);

    private static DefaultDeviceAuthenticator NewAuth(bool withKey = true)
    {
        var keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (withKey)
            keys[HmacHandle] = DeviceKey;
        return new DefaultDeviceAuthenticator(new InMemoryHmacKeyResolver(keys));
    }

    private static DeviceLoginContext ImeiLogin(
        IPAddress? ip = null,
        string imei = InMemoryDeviceRegistry.KnownImei,
        string? iccid = null,
        string? imsi = null,
        DeviceHmacProof? proof = null) =>
        new(ip ?? IPAddress.Parse("203.0.113.10"), Imei: imei, SimIccid: iccid, Imsi: imsi, HmacProof: proof);

    private static DeviceHmacProof SignedProof(byte[] key, string message)
    {
        byte[] msg = Encoding.UTF8.GetBytes(message);
        byte[] sig = HMACSHA256.HashData(key, msg);
        return new DeviceHmacProof(msg, sig);
    }

    // ── Allowlist happy path ────────────────────────────────────────────────────

    [Fact]
    public async Task Allowlisted_imei_passes_under_allowlist_only_policy()
    {
        var result = await NewAuth().AuthenticateAsync(
            Owner(), DeviceTrustPolicy.ImeiAllowlistBaseline(), ImeiLogin());

        Assert.Equal(AuthOutcome.Authenticated, result.Outcome);
        Assert.True(result.IsAuthenticated);
    }

    [Fact]
    public async Task Unknown_imei_does_not_resolve_to_an_owner_and_is_rejected()
    {
        // The IMEI allowlist IS the registry: an unprovisioned IMEI resolves to null (fail-closed),
        // so no owner ever reaches the authenticator.
        var registry = InMemoryDeviceRegistry.SeededDefault();
        ResolvedDeviceOwner? resolved = await registry.ResolveAsync(new DeviceIdentityRef(Imei: "000000000000000"));
        Assert.Null(resolved);

        // Defence-in-depth: even if a default/unresolved owner is fed in, the authenticator rejects it.
        var result = await NewAuth().AuthenticateAsync(
            Owner(deviceId: ""), DeviceTrustPolicy.ImeiAllowlistBaseline(), ImeiLogin(imei: "000000000000000"));
        Assert.Equal(AuthOutcome.Rejected, result.Outcome);
        Assert.Equal(AuthReasonCode.NotOnAllowlist, result.Code);
    }

    [Fact]
    public async Task Login_with_no_identifier_is_rejected()
    {
        var ctx = new DeviceLoginContext(IPAddress.Parse("203.0.113.10"));
        var result = await NewAuth().AuthenticateAsync(
            Owner(), DeviceTrustPolicy.ImeiAllowlistBaseline(), ctx);

        Assert.Equal(AuthOutcome.Rejected, result.Outcome);
        Assert.Equal(AuthReasonCode.NoIdentifierPresented, result.Code);
    }

    // ── Lifecycle gate ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Quarantined_device_is_kept_quarantined()
    {
        var result = await NewAuth().AuthenticateAsync(
            Owner(DeviceLifecycleState.Quarantined), DeviceTrustPolicy.ImeiAllowlistBaseline(), ImeiLogin());

        Assert.Equal(AuthOutcome.Quarantine, result.Outcome);
        Assert.Equal(AuthReasonCode.AlreadyQuarantined, result.Code);
    }

    [Fact]
    public async Task Suspended_device_is_rejected()
    {
        var result = await NewAuth().AuthenticateAsync(
            Owner(DeviceLifecycleState.Suspended), DeviceTrustPolicy.ImeiAllowlistBaseline(), ImeiLogin());

        Assert.Equal(AuthOutcome.Rejected, result.Outcome);
        Assert.Equal(AuthReasonCode.Suspended, result.Code);
    }

    [Fact]
    public async Task Retired_device_is_rejected()
    {
        var result = await NewAuth().AuthenticateAsync(
            Owner(DeviceLifecycleState.Retired), DeviceTrustPolicy.ImeiAllowlistBaseline(), ImeiLogin());

        Assert.Equal(AuthOutcome.Rejected, result.Outcome);
        Assert.Equal(AuthReasonCode.Retired, result.Code);
    }

    [Fact]
    public async Task Provisioned_but_never_connected_device_cannot_authenticate()
    {
        var result = await NewAuth().AuthenticateAsync(
            Owner(DeviceLifecycleState.Provisioned), DeviceTrustPolicy.ImeiAllowlistBaseline(), ImeiLogin());

        Assert.Equal(AuthOutcome.Rejected, result.Outcome);
        Assert.Equal(AuthReasonCode.LifecycleNotAllowed, result.Code);
    }

    // ── Source-IP pin ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Wrong_source_ip_is_rejected_when_pinned()
    {
        var policy = DeviceTrustPolicy.ImeiAllowlistBaseline(pinnedSourceCidrs: new[] { "203.0.113.0/24" });
        var result = await NewAuth().AuthenticateAsync(
            Owner(), policy, ImeiLogin(ip: IPAddress.Parse("198.51.100.7")));

        Assert.Equal(AuthOutcome.Rejected, result.Outcome);
        Assert.Equal(AuthReasonCode.SourceIpNotPinned, result.Code);
    }

    [Fact]
    public async Task Matching_source_ip_passes_when_pinned()
    {
        var policy = DeviceTrustPolicy.ImeiAllowlistBaseline(pinnedSourceCidrs: new[] { "203.0.113.0/24" });
        var result = await NewAuth().AuthenticateAsync(
            Owner(), policy, ImeiLogin(ip: IPAddress.Parse("203.0.113.55")));

        Assert.Equal(AuthOutcome.Authenticated, result.Outcome);
    }

    [Fact]
    public async Task Missing_source_ip_is_rejected_when_a_pin_is_configured()
    {
        var policy = DeviceTrustPolicy.ImeiAllowlistBaseline(pinnedSourceCidrs: new[] { "203.0.113.0/24" });
        var ctx = new DeviceLoginContext(RemoteIp: null, Imei: InMemoryDeviceRegistry.KnownImei);
        var result = await NewAuth().AuthenticateAsync(Owner(), policy, ctx);

        Assert.Equal(AuthOutcome.Rejected, result.Outcome);
        Assert.Equal(AuthReasonCode.SourceIpNotPinned, result.Code);
    }

    // ── SIM pin ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Changed_sim_iccid_quarantines_the_device()
    {
        var policy = DeviceTrustPolicy.ImeiAllowlistBaseline(pinnedSimIccid: "8901000000000000001");
        var result = await NewAuth().AuthenticateAsync(
            Owner(), policy, ImeiLogin(iccid: "8901999999999999999"));

        Assert.Equal(AuthOutcome.Quarantine, result.Outcome);
        Assert.Equal(AuthReasonCode.SimPinMismatch, result.Code);
    }

    [Fact]
    public async Task Matching_sim_iccid_passes()
    {
        var policy = DeviceTrustPolicy.ImeiAllowlistBaseline(pinnedSimIccid: "8901000000000000001");
        var result = await NewAuth().AuthenticateAsync(
            Owner(), policy, ImeiLogin(iccid: "8901000000000000001"));

        Assert.Equal(AuthOutcome.Authenticated, result.Outcome);
    }

    // ── Per-device HMAC ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Valid_per_device_hmac_authenticates_cryptographically()
    {
        const string message = "dev-known-0001\n1720800000\n42\nabc123";
        var ctx = ImeiLogin(proof: SignedProof(DeviceKey, message));
        var policy = new DeviceTrustPolicy(DeviceAuthMode.PerDeviceHmac);

        var result = await NewAuth().AuthenticateAsync(Owner(), policy, ctx);

        Assert.Equal(AuthOutcome.Authenticated, result.Outcome);
        Assert.True(result.IsCryptographicallyAuthenticated);
        Assert.Equal(DeviceTrustTier.Cryptographic, result.TrustTier);
    }

    [Fact]
    public async Task Invalid_per_device_hmac_is_rejected()
    {
        const string message = "dev-known-0001\n1720800000\n42\nabc123";
        var proof = SignedProof(Encoding.UTF8.GetBytes("the-wrong-signing-key-entirely!!"), message);
        var ctx = ImeiLogin(proof: proof);
        var policy = new DeviceTrustPolicy(DeviceAuthMode.PerDeviceHmac);

        var result = await NewAuth().AuthenticateAsync(Owner(), policy, ctx);

        Assert.Equal(AuthOutcome.Rejected, result.Outcome);
        Assert.Equal(AuthReasonCode.HmacInvalid, result.Code);
        Assert.False(result.IsCryptographicallyAuthenticated);
    }

    [Fact]
    public async Task Missing_hmac_proof_is_rejected_under_hmac_policy()
    {
        var policy = new DeviceTrustPolicy(DeviceAuthMode.PerDeviceHmac);
        var result = await NewAuth().AuthenticateAsync(Owner(), policy, ImeiLogin());

        Assert.Equal(AuthOutcome.Rejected, result.Outcome);
        Assert.Equal(AuthReasonCode.HmacProofMissing, result.Code);
    }

    [Fact]
    public async Task Unresolvable_credential_fails_closed()
    {
        const string message = "dev-known-0001\n1720800000\n42\nabc123";
        var ctx = ImeiLogin(proof: SignedProof(DeviceKey, message));
        var policy = new DeviceTrustPolicy(DeviceAuthMode.PerDeviceHmac);

        // Resolver holds no key for the handle → null → reject, never a silent pass.
        var result = await NewAuth(withKey: false).AuthenticateAsync(Owner(), policy, ctx);

        Assert.Equal(AuthOutcome.Rejected, result.Outcome);
        Assert.Equal(AuthReasonCode.CredentialUnavailable, result.Code);
    }

    // ── Residual-risk / honesty guarantees ──────────────────────────────────────

    [Fact]
    public void ImeiAllowlistOnly_is_flagged_low_trust_and_non_cryptographic()
    {
        var policy = DeviceTrustPolicy.ImeiAllowlistBaseline();

        Assert.False(policy.IsCryptographicDeviceAuth);
        Assert.Equal(DeviceTrustTier.LowSpoofable, policy.TrustTier);
        Assert.False(DeviceAuthMode.ImeiAllowlistOnly.ProvidesCryptographicDeviceAuth());
    }

    [Fact]
    public async Task Successful_imei_allowlist_login_is_explicitly_not_cryptographically_authenticated()
    {
        // Even a fully-passing allowlist login must NOT masquerade as cryptographic proof:
        // the IMEI is spoofable and the result says so.
        var result = await NewAuth().AuthenticateAsync(
            Owner(), DeviceTrustPolicy.ImeiAllowlistBaseline(), ImeiLogin());

        Assert.Equal(AuthOutcome.Authenticated, result.Outcome);
        Assert.False(result.IsCryptographicallyAuthenticated);
        Assert.Equal(DeviceTrustTier.LowSpoofable, result.TrustTier);
    }

    [Fact]
    public void Cryptographic_modes_are_classified_as_such()
    {
        Assert.True(DeviceAuthMode.PerDeviceHmac.ProvidesCryptographicDeviceAuth());
        Assert.True(DeviceAuthMode.MutualTls.ProvidesCryptographicDeviceAuth());
        Assert.True(DeviceAuthMode.ClientCertificate.ProvidesCryptographicDeviceAuth());
        Assert.Equal(DeviceTrustTier.StrongCryptographic, DeviceAuthMode.MutualTls.TrustTier());
        Assert.Equal(DeviceTrustTier.StrongCryptographic, DeviceAuthMode.ClientCertificate.TrustTier());
    }

    /// <summary>Test double: an in-memory HMAC key store. Returns a COPY, since the authenticator
    /// zeroes the key buffer after use.</summary>
    private sealed class InMemoryHmacKeyResolver : ICredentialKeyResolver
    {
        private readonly IReadOnlyDictionary<string, byte[]> _keys;

        public InMemoryHmacKeyResolver(IReadOnlyDictionary<string, byte[]> keys) => _keys = keys;

        public ValueTask<byte[]?> ResolveHmacKeyAsync(CredentialMaterial credential, CancellationToken cancellationToken = default)
        {
            if (credential.HasHandle && credential.Handle is { } h && _keys.TryGetValue(h, out byte[]? key))
                return ValueTask.FromResult<byte[]?>((byte[])key.Clone());
            return ValueTask.FromResult<byte[]?>(null);
        }
    }
}
