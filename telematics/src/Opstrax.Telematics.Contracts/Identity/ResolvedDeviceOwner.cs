using Opstrax.Telematics.Contracts.Lifecycle;

namespace Opstrax.Telematics.Contracts.Identity;

/// <summary>
/// The trusted ownership and lifecycle facts for a device, as resolved from the
/// device registry. Every field here comes from the registry record — <b>never</b>
/// from the packet that presented the identity claim. This record is what binds an
/// incoming fix to a tenant, company and (optionally) vehicle.
/// </summary>
/// <remarks>
/// A non-null <see cref="ResolvedDeviceOwner"/> means "the registry recognises this
/// identity"; it does <em>not</em> by itself mean the device is allowed to ingest —
/// callers must still honour <see cref="LifecycleState"/> (for example, reject when
/// <see cref="DeviceLifecycleState.Suspended"/> or <see cref="DeviceLifecycleState.Retired"/>).
/// </remarks>
/// <param name="TenantId">The owning tenant. Authoritative scope for all downstream reads/writes.</param>
/// <param name="CompanyId">The owning company within the tenant.</param>
/// <param name="DeviceId">The fabric-internal device id the claim resolved to.</param>
/// <param name="VehicleId">The vehicle the device is currently bound to, if any.</param>
/// <param name="LifecycleState">The device's current lifecycle state per the registry.</param>
/// <param name="CredentialHandle">
/// An opaque handle to the device's authentication material (for example a key-vault
/// reference or PSK id). The secret itself is never carried on the contract surface;
/// this handle is dereferenced by the authentication stage only.
/// </param>
public readonly record struct ResolvedDeviceOwner(
    Guid TenantId,
    long CompanyId,
    string DeviceId,
    long? VehicleId,
    DeviceLifecycleState LifecycleState,
    string CredentialHandle);
