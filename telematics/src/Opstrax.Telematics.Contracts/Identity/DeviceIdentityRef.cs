namespace Opstrax.Telematics.Contracts.Identity;

/// <summary>
/// An identity <em>claim</em> extracted from a device frame or vendor payload. This
/// is deliberately just the raw self-asserted identifiers the packet carried —
/// nothing here is trusted. It is the lookup key handed to an
/// <see cref="IDeviceRegistry"/>, which resolves the real owner.
/// </summary>
/// <remarks>
/// <para>
/// IMEI/serial are <b>identifiers, never credentials</b>. A device claiming an IMEI
/// does not prove it owns that IMEI; ownership and trust come exclusively from the
/// registry record the identifier resolves to. Treat every field here as attacker-
/// controlled input.
/// </para>
/// </remarks>
/// <param name="Imei">The self-reported IMEI, if the protocol carries one. Digits only, unvalidated.</param>
/// <param name="Serial">A vendor/hardware serial number, if reported.</param>
/// <param name="DeviceId">
/// An opaque fabric device id, present only when a prior stage already resolved this
/// device (for example a re-identification after reconnect). Absent on first contact.
/// </param>
public readonly record struct DeviceIdentityRef(
    string? Imei = null,
    string? Serial = null,
    string? DeviceId = null)
{
    /// <summary>
    /// <see langword="true"/> when at least one identifier is present, so the claim is
    /// worth attempting to resolve. An all-empty claim can never resolve to an owner.
    /// </summary>
    public bool HasAnyIdentifier =>
        !string.IsNullOrWhiteSpace(Imei) ||
        !string.IsNullOrWhiteSpace(Serial) ||
        !string.IsNullOrWhiteSpace(DeviceId);
}
