using System.Security.Cryptography.X509Certificates;

namespace Opstrax.Api.Security;

public static class DataProtectionCertificateLoader
{
    public sealed record CertificateSet(X509Certificate2 Current, X509Certificate2? Previous);

    public static CertificateSet LoadProductionCertificates(IConfiguration configuration)
    {
        var current = Load(
            Coalesce(configuration["DataProtection:CertificateBase64"], configuration["DATA_PROTECTION_CERTIFICATE_BASE64"]),
            Coalesce(configuration["DataProtection:CertificatePassword"], configuration["DATA_PROTECTION_CERTIFICATE_PASSWORD"]),
            required: true,
            requireCurrentlyValid: true,
            lane: "current");

        var previousBase64 = Coalesce(configuration["DataProtection:PreviousCertificateBase64"],
            configuration["DATA_PROTECTION_PREVIOUS_CERTIFICATE_BASE64"]);
        var previousPassword = Coalesce(configuration["DataProtection:PreviousCertificatePassword"],
            configuration["DATA_PROTECTION_PREVIOUS_CERTIFICATE_PASSWORD"]);
        if (string.IsNullOrWhiteSpace(previousBase64) != string.IsNullOrWhiteSpace(previousPassword))
            throw new InvalidOperationException(
                "Previous Data Protection certificate and password must be configured together.");

        var previous = Load(previousBase64, previousPassword, required: false,
            requireCurrentlyValid: false, lane: "previous");
        if (previous is not null && string.Equals(
                current!.Thumbprint, previous.Thumbprint, StringComparison.OrdinalIgnoreCase))
        {
            previous.Dispose();
            previous = null;
        }

        return new(current!, previous);
    }

    private static string? Coalesce(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static X509Certificate2? Load(
        string? base64,
        string? password,
        bool required,
        bool requireCurrentlyValid,
        string lane)
    {
        if (string.IsNullOrWhiteSpace(base64) || string.IsNullOrWhiteSpace(password))
        {
            if (required)
                throw new InvalidOperationException(
                    "Production Data Protection certificate configuration is incomplete.");
            return null;
        }

        try
        {
            var raw = Convert.FromBase64String(base64);
            if (raw.Length is < 256 or > 131_072)
                throw new InvalidOperationException("Certificate payload size is invalid.");
#pragma warning disable SYSLIB0057 // X509CertificateLoader is unavailable on net8.0.
            var certificate = new X509Certificate2(raw, password);
#pragma warning restore SYSLIB0057
            if (!certificate.HasPrivateKey)
            {
                certificate.Dispose();
                throw new InvalidOperationException("Certificate has no private key.");
            }
            var now = DateTime.UtcNow;
            if (requireCurrentlyValid &&
                (certificate.NotBefore.ToUniversalTime() > now ||
                 certificate.NotAfter.ToUniversalTime() <= now.AddDays(14)))
            {
                certificate.Dispose();
                throw new InvalidOperationException("Certificate is not valid for the required production window.");
            }
            return certificate;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"The {lane} Data Protection certificate could not be loaded.", ex);
        }
    }
}
