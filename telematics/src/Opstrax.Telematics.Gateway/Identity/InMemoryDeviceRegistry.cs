using System.Collections.Concurrent;
using Opstrax.Telematics.Contracts.Identity;
using Opstrax.Telematics.Contracts.Lifecycle;

namespace Opstrax.Telematics.Gateway.Identity;

/// <summary>
/// An in-process <see cref="IDeviceRegistry"/> for local development and the gateway's
/// integration tests. The production implementation reads the device table in Postgres;
/// swapping it must not change a line of the framing loop, which is why the gateway only
/// ever depends on <see cref="IDeviceRegistry"/>.
/// </summary>
/// <remarks>
/// <para>
/// This class is the <b>only</b> place a tenant/company/vehicle binding AND a device's trust
/// policy come from. It implements the fail-closed contract literally: an identity it does not
/// hold resolves to <see langword="null"/>, never to a default or "unassigned" owner. Callers are
/// required to treat that null as "reject", and the gateway does.
/// </para>
/// </remarks>
internal sealed class InMemoryDeviceRegistry : IDeviceRegistry
{
    // ── The one seeded known device ────────────────────────────────────────────
    // This IMEI is the one carried by telematics/fixtures/gt06/login.hex, so the fixtures
    // exercise the *known-device* path end to end without special-casing anything.

    /// <summary>The single seeded known device's IMEI (matches the <c>login.hex</c> fixture).</summary>
    public const string KnownImei = "868120303337976";

    /// <summary>Registry-assigned tenant for the seeded device. Deliberately unrelated to the IMEI.</summary>
    public static readonly Guid KnownTenantId = Guid.Parse("2f1c9a54-8a0e-4a7d-9f3b-6d2c1e5b7a90");

    /// <summary>Registry-assigned company for the seeded device.</summary>
    public const long KnownCompanyId = 100L;

    /// <summary>Registry-assigned fabric device id for the seeded device. Never the IMEI.</summary>
    public const string KnownDeviceId = "dev-known-0001";

    /// <summary>Vehicle the seeded device is bound to.</summary>
    public const long KnownVehicleId = 5501L;

    // The registry is the source of truth for the full trust record (owner + policy + credential),
    // not just ownership. The trust policy is stored PER DEVICE so the gateway enforces exactly what
    // a device was provisioned for — pins, replay requirement, and (for crypto modes) proof.
    private readonly ConcurrentDictionary<string, ResolvedDeviceTrust> _byIdentifier;

    /// <summary>Creates a registry holding the supplied identifier → full trust records.</summary>
    /// <param name="seed">Identifier (IMEI/serial/device id) to registry-resolved trust record.</param>
    public InMemoryDeviceRegistry(IEnumerable<KeyValuePair<string, ResolvedDeviceTrust>> seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        _byIdentifier = new ConcurrentDictionary<string, ResolvedDeviceTrust>(seed, StringComparer.Ordinal);
    }

    /// <summary>
    /// Owner-only convenience overload: each owner is wrapped in the honest raw-tracker baseline
    /// trust policy (<see cref="DeviceTrustPolicy.ImeiAllowlistBaseline"/> — no crypto, no pins,
    /// replay defence required) with <see cref="CredentialMaterial.None"/>. For tests/callers that
    /// only care about ownership; provision pins or a cryptographic auth mode via the
    /// <see cref="ResolvedDeviceTrust"/> overload.
    /// </summary>
    /// <param name="seed">Identifier to registry-resolved owner.</param>
    public InMemoryDeviceRegistry(IEnumerable<KeyValuePair<string, ResolvedDeviceOwner>> seed)
        : this(WrapOwners(seed))
    {
    }

    private static IEnumerable<KeyValuePair<string, ResolvedDeviceTrust>> WrapOwners(
        IEnumerable<KeyValuePair<string, ResolvedDeviceOwner>> seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        foreach (KeyValuePair<string, ResolvedDeviceOwner> kv in seed)
        {
            yield return new KeyValuePair<string, ResolvedDeviceTrust>(
                kv.Key,
                new ResolvedDeviceTrust(kv.Value, DeviceTrustPolicy.ImeiAllowlistBaseline(), CredentialMaterial.None));
        }
    }

    /// <summary>
    /// Creates a registry seeded with the single known device the fixtures use. Every other
    /// identity — including a well-formed, CRC-valid login from an IMEI nobody provisioned —
    /// resolves to <see langword="null"/> and is rejected. The seeded device carries the honest
    /// raw-tracker baseline policy (IMEI allowlist only, spoofable, replay defence required); a
    /// production device would carry its real per-device policy (pins / PerDeviceHmac) here.
    /// </summary>
    public static InMemoryDeviceRegistry SeededDefault() => new(new[]
    {
        new KeyValuePair<string, ResolvedDeviceTrust>(
            KnownImei,
            new ResolvedDeviceTrust(
                new ResolvedDeviceOwner(
                    TenantId: KnownTenantId,
                    CompanyId: KnownCompanyId,
                    DeviceId: KnownDeviceId,
                    VehicleId: KnownVehicleId,
                    LifecycleState: DeviceLifecycleState.Online,
                    // An opaque handle, NOT a secret. The real credential lives in the vault this
                    // points at and never touches the gateway's memory or config.
                    CredentialHandle: "vault://opstrax/telematics/psk/dev-known-0001"),
                DeviceTrustPolicy.ImeiAllowlistBaseline(),
                CredentialMaterial.None)),
    });

    /// <inheritdoc />
    public ValueTask<ResolvedDeviceOwner?> ResolveAsync(
        DeviceIdentityRef identity,
        CancellationToken cancellationToken = default)
    {
        ResolvedDeviceTrust? trust = ResolveTrustCore(identity, cancellationToken);
        return ValueTask.FromResult<ResolvedDeviceOwner?>(trust?.Owner);
    }

    /// <inheritdoc />
    public ValueTask<ResolvedDeviceTrust?> ResolveTrustAsync(
        DeviceIdentityRef identity,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(ResolveTrustCore(identity, cancellationToken));
    }

    private ResolvedDeviceTrust? ResolveTrustCore(DeviceIdentityRef identity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // An all-empty claim can never resolve: fail closed rather than matching a null key.
        if (!identity.HasAnyIdentifier)
            return null;

        // Resolution order mirrors identifier strength. A packet-supplied DeviceId is still
        // only a *claim* — it is looked up like any other, never trusted as an owner.
        foreach (string? candidate in new[] { identity.Imei, identity.Serial, identity.DeviceId })
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            if (_byIdentifier.TryGetValue(candidate, out ResolvedDeviceTrust trust))
                return trust;
        }

        // Unknown / ambiguous / unprovisioned → null. NEVER a fabricated owner.
        return null;
    }
}
