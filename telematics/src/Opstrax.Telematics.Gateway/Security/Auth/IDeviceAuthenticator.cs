using Opstrax.Telematics.Contracts.Identity;

namespace Opstrax.Telematics.Gateway.Security.Auth;

/// <summary>
/// Decides whether a device that has already been <em>resolved</em> by the registry is
/// <em>allowed to ingest right now</em>, by applying its <see cref="DeviceTrustPolicy"/> against
/// the observed <see cref="DeviceLoginContext"/>. This is the trust-enforcement choke point that
/// sits after identity resolution and before any data is accepted.
/// </summary>
/// <remarks>
/// <para>
/// The authenticator does <b>not</b> resolve ownership (that is <c>IDeviceRegistry</c>'s job) and
/// does <b>not</b> perform durable replay defence (that is the shared nonce/sequence store). It
/// enforces: lifecycle eligibility, allowlist membership, source-IP pin, SIM pin, and — for
/// <see cref="DeviceAuthMode.PerDeviceHmac"/> — cryptographic proof verification. It surfaces the
/// replay requirement via the policy but relies on the ingest pipeline to enforce it.
/// </para>
/// <para>
/// <b>Fail-closed:</b> any check that cannot be satisfied — including an unresolvable credential —
/// results in <see cref="AuthOutcome.Rejected"/> or <see cref="AuthOutcome.Quarantine"/>, never a
/// silent pass.
/// </para>
/// </remarks>
public interface IDeviceAuthenticator
{
    /// <summary>
    /// Authenticates a login for a registry-resolved <paramref name="owner"/> under
    /// <paramref name="policy"/>, given the observed <paramref name="ctx"/>.
    /// </summary>
    /// <param name="owner">The registry-resolved owner. Must be a real resolution, not a fabricated default.</param>
    /// <param name="policy">The per-device trust policy that declares which controls apply.</param>
    /// <param name="ctx">The untrusted, observed login context.</param>
    /// <param name="cancellationToken">Cancels any credential dereference.</param>
    /// <returns>The authentication decision. Callers MUST honour a non-<see cref="AuthOutcome.Authenticated"/> result.</returns>
    ValueTask<AuthResult> AuthenticateAsync(
        ResolvedDeviceOwner owner,
        DeviceTrustPolicy policy,
        DeviceLoginContext ctx,
        CancellationToken cancellationToken = default);
}
