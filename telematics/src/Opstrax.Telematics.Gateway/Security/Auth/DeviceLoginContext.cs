using System.Net;

namespace Opstrax.Telematics.Gateway.Security.Auth;

/// <summary>
/// A per-device HMAC proof presented on a login: the exact canonical bytes the device signed
/// and the signature it produced over them. The authenticator recomputes the MAC with the
/// device's resolved key and compares in constant time. Absent for non-HMAC auth modes.
/// </summary>
/// <param name="SignedMessage">
/// The canonical byte string the device signed (for example
/// <c>{deviceId}\n{timestamp}\n{sequence}\n{sha256hex(payload)}</c>). Reconstructed by the
/// caller from trusted/normalised inputs, never taken verbatim from an attacker-shaped field.
/// </param>
/// <param name="Signature">The signature bytes the device presented (raw HMAC output, not hex/base64 text).</param>
public readonly record struct DeviceHmacProof(byte[] SignedMessage, byte[] Signature);

/// <summary>
/// The untrusted, per-connection context for a single device login attempt: what the transport
/// observed (remote IP) and what the device asserted (identifiers, SIM identity, and — when the
/// mode supports it — a cryptographic proof). Everything here except <see cref="RemoteIp"/> is
/// attacker-controlled; the authenticator treats it as such.
/// </summary>
/// <remarks>
/// <see cref="RemoteIp"/> is the one field the platform observes rather than the device asserts,
/// which is why source-IP pinning has any value at all for spoofable protocols. Even so it is
/// only a network-position signal (NAT, carrier egress, proxies weaken it), not proof of identity.
/// </remarks>
/// <param name="RemoteIp">The observed source address of the connection, if known.</param>
/// <param name="Imei">The self-asserted IMEI from the login frame, if any. An identifier, never a credential.</param>
/// <param name="Serial">The self-asserted hardware serial, if any.</param>
/// <param name="SimIccid">The reported SIM ICCID, if the transport surfaced it. Checked against a pin if configured.</param>
/// <param name="Imsi">The reported IMSI, if surfaced. Checked against a pin if configured.</param>
/// <param name="HmacProof">
/// The per-device HMAC proof, present only for <see cref="Opstrax.Telematics.Contracts.Identity.DeviceAuthMode.PerDeviceHmac"/>.
/// </param>
public readonly record struct DeviceLoginContext(
    IPAddress? RemoteIp,
    string? Imei = null,
    string? Serial = null,
    string? SimIccid = null,
    string? Imsi = null,
    DeviceHmacProof? HmacProof = null)
{
    /// <summary><see langword="true"/> when the login carried at least one identifier to allowlist against.</summary>
    public bool HasAnyIdentifier =>
        !string.IsNullOrWhiteSpace(Imei) || !string.IsNullOrWhiteSpace(Serial);
}
