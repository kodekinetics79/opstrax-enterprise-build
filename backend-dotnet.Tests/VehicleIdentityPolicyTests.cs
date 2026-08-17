using Opstrax.Api.Services;
using Xunit;

namespace Opstrax.Tests;

public sealed class VehicleIdentityPolicyTests
{
    [Theory]
    [InlineData("1HGCM82633A004352", "1HGCM82633A004352")]
    [InlineData(" 1m8gdm9axkp042788 ", "1M8GDM9AXKP042788")]
    public void ValidVin_IsNormalizedAndAccepted(string input, string expected)
    {
        var result = VehicleIdentityPolicy.ValidateVin(input);

        Assert.True(result.IsValid);
        Assert.Equal(expected, result.NormalizedValue);
        Assert.Null(result.ErrorCode);
    }

    [Theory]
    [InlineData(null, "vin_required")]
    [InlineData("", "vin_required")]
    [InlineData("1HGCM82633A00435", "vin_length")]
    [InlineData("1HGCM82633A0043520", "vin_length")]
    [InlineData("1HGCM82633A00I352", "vin_characters")]
    [InlineData("1HGCM82633A00O352", "vin_characters")]
    [InlineData("1HGCM82633A00Q352", "vin_characters")]
    [InlineData("1HGCM82633A00-352", "vin_characters")]
    [InlineData("1HGCM82633A00É352", "vin_characters")]
    public void MalformedVin_IsRejected(string? input, string expectedError)
    {
        var result = VehicleIdentityPolicy.ValidateVin(input);

        Assert.False(result.IsValid);
        Assert.Equal(expectedError, result.ErrorCode);
        Assert.Null(result.NormalizedValue);
    }

    [Theory]
    [InlineData("1HGCM82643A004352", "3")]
    [InlineData("1M8GDM9A1KP042788", "X")]
    public void IncorrectCheckDigit_IsRejectedWithExpectedDigit(string input, string expected)
    {
        var result = VehicleIdentityPolicy.ValidateVin(input);

        Assert.False(result.IsValid);
        Assert.Equal("vin_check_digit", result.ErrorCode);
        Assert.Contains($"expected '{expected}'", result.ErrorMessage);
    }

    [Theory]
    [InlineData("manufacturer_serial_number", " sn-42/alpha ", VehicleIdentityPolicy.ManufacturerSerialNumber, "SN-42/ALPHA")]
    [InlineData("government registration number", "ny.trk_2048", VehicleIdentityPolicy.GovernmentRegistrationNumber, "NY.TRK_2048")]
    [InlineData("legacy-fleet-identifier", "legacy-0007", VehicleIdentityPolicy.LegacyFleetIdentifier, "LEGACY-0007")]
    public void GovernedAlternateIdentity_IsNormalizedAndAccepted(
        string kind, string value, string expectedKind, string expectedValue)
    {
        var result = VehicleIdentityPolicy.ValidateAlternateIdentity(kind, value);

        Assert.True(result.IsValid);
        Assert.Equal(expectedKind, result.AlternateIdentityKind);
        Assert.Equal(expectedValue, result.NormalizedValue);
    }

    [Theory]
    [InlineData(null, "SERIAL-42", "alternate_kind_required")]
    [InlineData("temporary-tag", "SERIAL-42", "alternate_kind_unsupported")]
    [InlineData("manufacturer-serial-number", null, "alternate_value_required")]
    [InlineData("manufacturer-serial-number", "N/A", "alternate_value_placeholder")]
    [InlineData("manufacturer-serial-number", "NONE", "alternate_value_placeholder")]
    [InlineData("manufacturer-serial-number", "SERIAL 42", "alternate_value_characters")]
    [InlineData("manufacturer-serial-number", "SERIAL#42", "alternate_value_characters")]
    public void InvalidAlternateIdentity_IsRejected(string? kind, string? value, string expectedError)
    {
        var result = VehicleIdentityPolicy.ValidateAlternateIdentity(kind, value);

        Assert.False(result.IsValid);
        Assert.Equal(expectedError, result.ErrorCode);
        Assert.Null(result.NormalizedValue);
    }

    [Theory]
    [InlineData("1HGCM82643A004352")]
    [InlineData("1HGCM82633A004352")]
    [InlineData("1HGCM82633A00I352")]
    public void VinShapedAlternateIdentity_CannotBypassVinPolicy(string value)
    {
        var result = VehicleIdentityPolicy.ValidateAlternateIdentity(
            VehicleIdentityPolicy.LegacyFleetIdentifier, value);

        Assert.False(result.IsValid);
        Assert.Equal("alternate_vin_bypass", result.ErrorCode);
    }
}
