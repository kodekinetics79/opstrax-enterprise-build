namespace Opstrax.Tests;

public sealed class TelemetryCredentialHardeningTests
{
    [Fact]
    public void SchemaStartup_DoesNotGeneratePredictableCredentials()
    {
        var source = ReadRepositoryFile("backend-dotnet", "Services", "TelemetrySchemaService.cs");

        Assert.DoesNotContain(
            "sha256(('opstrax-dev-' || device_serial)",
            source,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "CONCAT('opstrax-hmac-dev-', device_serial)",
            source,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gen_random", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CredentialRotationRequired", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SchemaStartup_QuarantinesIncompleteAndLegacyCredentials()
    {
        var source = ReadRepositoryFile("backend-dotnet", "Services", "TelemetrySchemaService.cs");

        Assert.Contains("SET api_key_hash = NULL", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hmac_secret = NULL", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("api_key_hash IS NULL", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hmac_secret IS NULL", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("length(hmac_secret) < 32", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("revoked_at = COALESCE(revoked_at, NOW())", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DatabaseInvariant_RejectsActiveDevicesWithoutStrongCredentials()
    {
        var migration = ReadRepositoryFile(
            "database",
            "migrations",
            "2026_07_05_stage27_iot_credential_quarantine.sql");

        Assert.Contains("ck_eld_devices_active_credentials", migration, StringComparison.Ordinal);
        Assert.Contains(
            "ADD COLUMN IF NOT EXISTS api_key_hash",
            migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "ADD COLUMN IF NOT EXISTS hmac_secret",
            migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("status <> 'Active'", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("api_key_hash IS NOT NULL", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("api_key_hash ~ '^[0-9a-fA-F]{64}$'", migration, StringComparison.Ordinal);
        Assert.Contains("hmac_secret IS NOT NULL", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("length(btrim(hmac_secret)) >= 32", migration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration_RequiresExplicitRotationInsteadOfCredentialBackfill()
    {
        var migration = ReadRepositoryFile(
            "database",
            "migrations",
            "2026_07_05_stage27_iot_credential_quarantine.sql");

        Assert.Contains("status = 'CredentialRotationRequired'", migration, StringComparison.Ordinal);
        Assert.Contains("SET api_key_hash = NULL", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "sha256(('opstrax-dev-' || device_serial)",
            migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "CONCAT('opstrax-hmac-dev-', device_serial)",
            migration,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadRepositoryFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }
}
