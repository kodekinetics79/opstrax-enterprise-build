using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.Security;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class DataProtectionCertificateConfigurationTests
{
    [Fact]
    public void ProductionWiring_UsesSharedRepository_StableApplicationName_AndRotationCertificates()
    {
        var root = FindRoot();
        var program = File.ReadAllText(Path.Combine(root, "backend-dotnet", "Program.cs"));
        var migration = File.ReadAllText(Path.Combine(root, "database", "migrations",
            "2026_07_31_stage59_data_protection_key_ring.sql"));
        Assert.Contains("SetApplicationName(\"opstrax-api-v1\")", program, StringComparison.Ordinal);
        Assert.Contains("ProtectKeysWithCertificate", program, StringComparison.Ordinal);
        Assert.Contains("UnprotectKeysWithAnyCertificate", program, StringComparison.Ordinal);
        Assert.Contains("options.XmlRepository = repository", program, StringComparison.Ordinal);
        Assert.Contains("REVOKE ALL ON TABLE public.platform_data_protection_keys FROM PUBLIC,opstrax_app,opstrax_system", migration, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT,INSERT ON TABLE public.platform_data_protection_keys TO opstrax_system", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("GRANT SELECT ON TABLE public.platform_data_protection_keys TO opstrax_app", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionCertificateLoader_FailsClosed_WhenSecretsAreMissing()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>()).Build();
        Assert.Throws<InvalidOperationException>(() =>
            DataProtectionCertificateLoader.LoadProductionCertificates(config));
    }

    [Fact]
    public void ProductionCertificateLoader_LoadsCurrentAndPreviousPrivateKeys()
    {
        const string currentPassword = "current-password-strong-123";
        const string previousPassword = "previous-password-strong-123";
        using var current = CreateCertificate("current");
        using var previous = CreateCertificate("previous");
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["DATA_PROTECTION_CERTIFICATE_BASE64"] = Convert.ToBase64String(
                    current.Export(X509ContentType.Pkcs12, currentPassword)),
                ["DATA_PROTECTION_CERTIFICATE_PASSWORD"] = currentPassword,
                ["DATA_PROTECTION_PREVIOUS_CERTIFICATE_BASE64"] = Convert.ToBase64String(
                    previous.Export(X509ContentType.Pkcs12, previousPassword)),
                ["DATA_PROTECTION_PREVIOUS_CERTIFICATE_PASSWORD"] = previousPassword,
            }).Build();

        var loaded = DataProtectionCertificateLoader.LoadProductionCertificates(config);
        using (loaded.Current)
        using (loaded.Previous)
        {
            Assert.True(loaded.Current.HasPrivateKey);
            Assert.NotNull(loaded.Previous);
            Assert.True(loaded.Previous!.HasPrivateKey);
            Assert.NotEqual(loaded.Current.Thumbprint, loaded.Previous.Thumbprint);
        }
    }

    internal static X509Certificate2 CreateCertificate(string name)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN=opstrax-data-protection-{name}", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(90));
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}

// Requires the owner runner to apply terminal Stage59 and provision independent
// opstrax_app/opstrax_system test passwords. It is intentionally a Postgres test.
public sealed class DataProtectionSharedKeyRingPostgresTests
{
    [Fact]
    public async Task Readiness_FailsClosed_WhenKeyRingTableContractDrifts()
    {
        var owner = CreateDatabase(TestDb.ConnectionString, TestDb.ConnectionString);
        await owner.ExecuteAsync(
            "ALTER TABLE platform_data_protection_keys ADD COLUMN stage59_drift_probe text NULL");
        try
        {
            using var certificate = DataProtectionCertificateConfigurationTests.CreateCertificate("drift-probe");
            using var provider = BuildProvider(
                $"opstrax-keyring-drift-{Guid.NewGuid():N}", certificate, SystemConnectionString());
            var result = await provider.GetRequiredService<DataProtectionReadinessService>().CheckAsync();
            Assert.False(result.Ready);
            Assert.Equal("data_protection_key_ring_schema_drift", result.FailureCode);
        }
        finally
        {
            await owner.ExecuteAsync(
                "ALTER TABLE platform_data_protection_keys DROP COLUMN stage59_drift_probe");
        }
    }

    [Fact]
    public async Task ConcurrentColdStart_ConvergesOnSharedEncryptedRing_AcrossFreshInstances()
    {
        var owner = CreateDatabase(TestDb.ConnectionString, TestDb.ConnectionString);
        var baselineId = await owner.ScalarLongAsync(
            "SELECT COALESCE(MAX(id),0) FROM platform_data_protection_keys");
        Assert.Equal(0, baselineId);
        using var certificate = DataProtectionCertificateConfigurationTests.CreateCertificate("cold-start");
        var applicationName = $"opstrax-keyring-cold-{Guid.NewGuid():N}";

        try
        {
            using var first = BuildProvider(applicationName, certificate, SystemConnectionString());
            using var second = BuildProvider(applicationName, certificate, SystemConnectionString());
            var firstProtector = first.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("concurrent-cold-start");
            var secondProtector = second.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("concurrent-cold-start");

            // Both independent service providers encounter the same empty ring at once.
            var tokens = await Task.WhenAll(
                Task.Run(() => firstProtector.Protect("issued-by-first")),
                Task.Run(() => secondProtector.Protect("issued-by-second")));

            // Sticky-free traffic can return to either already-running instance
            // immediately. The original racing providers must converge too; proving
            // only fresh restarts would hide a key-cache split during cold start.
            Assert.Equal("issued-by-second", firstProtector.Unprotect(tokens[1]));
            Assert.Equal("issued-by-first", secondProtector.Unprotect(tokens[0]));

            // Fresh instances model convergence after the concurrent cold start. Each
            // must read every persisted key and decrypt tokens issued by either origin.
            using var verifierOne = BuildProvider(applicationName, certificate, SystemConnectionString());
            using var verifierTwo = BuildProvider(applicationName, certificate, SystemConnectionString());
            var one = verifierOne.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("concurrent-cold-start");
            var two = verifierTwo.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("concurrent-cold-start");
            Assert.Equal("issued-by-first", one.Unprotect(tokens[0]));
            Assert.Equal("issued-by-second", one.Unprotect(tokens[1]));
            Assert.Equal("issued-by-first", two.Unprotect(tokens[0]));
            Assert.Equal("issued-by-second", two.Unprotect(tokens[1]));

            var stored = await owner.QueryAsync(
                "SELECT xml_payload FROM platform_data_protection_keys ORDER BY id");
            Assert.NotEmpty(stored);
            Assert.All(stored, row => Assert.Contains(
                "encryptedSecret", row["xmlPayload"]?.ToString() ?? string.Empty,
                StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await owner.ExecuteAsync(
                "DELETE FROM platform_data_protection_keys WHERE id>@id",
                c => c.Parameters.AddWithValue("@id", baselineId));
        }
    }

    [Fact]
    public async Task TwoProviders_AndRestart_ShareEncryptedKeys_WithPreviousCertificateRotation()
    {
        var owner = CreateDatabase(TestDb.ConnectionString, TestDb.ConnectionString);
        var appConnection = TestDb.AppConnectionString;
        var systemConnection = SystemConnectionString();
        var baselineId = await owner.ScalarLongAsync(
            "SELECT COALESCE(MAX(id),0) FROM platform_data_protection_keys");
        using var oldCertificate = DataProtectionCertificateConfigurationTests.CreateCertificate("old");
        using var newCertificate = DataProtectionCertificateConfigurationTests.CreateCertificate("new");
        var applicationName = $"opstrax-keyring-test-{Guid.NewGuid():N}";

        try
        {
            // App identity has zero access even though the repository contains no tenant data.
            await using (var app = new NpgsqlConnection(appConnection))
            {
                await app.OpenAsync();
                await using var denied = new NpgsqlCommand(
                    "SELECT COUNT(*) FROM platform_data_protection_keys", app);
                var ex = await Assert.ThrowsAsync<PostgresException>(() => denied.ExecuteScalarAsync());
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
            }

            string protectedValue;
            using (var first = BuildProvider(applicationName, oldCertificate, systemConnection))
            {
                var protector = first.GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("restart-proof");
                protectedValue = protector.Protect("shared-across-instances");
                var readiness = await first.GetRequiredService<DataProtectionReadinessService>()
                    .CheckAsync();
                Assert.True(readiness.Ready, readiness.FailureCode);
                Assert.True(readiness.KeyCount >= 1);
            }

            // A completely new DI container simulates another instance / restart.
            using (var restarted = BuildProvider(applicationName, oldCertificate, systemConnection))
            {
                var protector = restarted.GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("restart-proof");
                Assert.Equal("shared-across-instances", protector.Unprotect(protectedValue));
            }

            var oldKeyMaxId = await owner.ScalarLongAsync(
                "SELECT COALESCE(MAX(id),0) FROM platform_data_protection_keys WHERE id>@baseline",
                c => c.Parameters.AddWithValue("@baseline", baselineId));

            // During certificate rotation the current certificate protects new keys while
            // the previous private key keeps every existing token/cookie decryptable.
            using (var rotating = BuildProvider(
                       applicationName, newCertificate, systemConnection, oldCertificate))
            {
                var protector = rotating.GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("restart-proof");
                Assert.Equal("shared-across-instances", protector.Unprotect(protectedValue));
                var activation = DateTimeOffset.UtcNow;
                rotating.GetRequiredService<IKeyManager>().CreateNewKey(
                    activation,
                    activation.AddDays(90));
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));

            string newCertificateValue;
            using (var afterKeyGeneration = BuildProvider(
                       applicationName, newCertificate, systemConnection, oldCertificate))
            {
                newCertificateValue = afterKeyGeneration.GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("restart-proof")
                    .Protect("issued-under-new-certificate");
            }

            Assert.True(await owner.ScalarLongAsync(
                "SELECT COUNT(*) FROM platform_data_protection_keys WHERE id>@old",
                c => c.Parameters.AddWithValue("@old", oldKeyMaxId)) >= 1);

            // This is the operator's phase-two removal after the old-token retention
            // window: remove only the rows encrypted with the retired certificate.
            await owner.ExecuteAsync(
                "DELETE FROM platform_data_protection_keys WHERE id>@baseline AND id<=@old",
                c =>
                {
                    c.Parameters.AddWithValue("@baseline", baselineId);
                    c.Parameters.AddWithValue("@old", oldKeyMaxId);
                });

            // A new-cert-only restart must now load the reduced ring and decrypt data
            // protected with the explicitly generated current-certificate key.
            using (var newOnly = BuildProvider(applicationName, newCertificate, systemConnection))
            {
                var protector = newOnly.GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("restart-proof");
                Assert.Equal("issued-under-new-certificate", protector.Unprotect(newCertificateValue));
                Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(protectedValue));
            }

            var stored = await owner.QueryAsync(
                "SELECT xml_payload FROM platform_data_protection_keys WHERE id>@id ORDER BY id",
                c => c.Parameters.AddWithValue("@id", baselineId));
            Assert.NotEmpty(stored);
            Assert.All(stored, row =>
            {
                var xml = row["xmlPayload"]?.ToString() ?? string.Empty;
                Assert.Contains("encryptedSecret", xml, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("shared-across-instances", xml, StringComparison.Ordinal);
            });
        }
        finally
        {
            await owner.ExecuteAsync(
                "DELETE FROM platform_data_protection_keys WHERE id>@id",
                c => c.Parameters.AddWithValue("@id", baselineId));
        }
    }

    private static ServiceProvider BuildProvider(
        string applicationName,
        X509Certificate2 current,
        string systemConnection,
        X509Certificate2? previous = null)
    {
        var services = new ServiceCollection();
        var database = CreateDatabase(TestDb.AppConnectionString, systemConnection);
        services.AddSingleton(database);
        services.AddSingleton<PostgresDataProtectionXmlRepository>();
        services.AddSingleton<DataProtectionReadinessService>();
        var protection = services.AddDataProtection()
            .SetApplicationName(applicationName)
            .ProtectKeysWithCertificate(current);
        if (previous is not null)
            protection.UnprotectKeysWithAnyCertificate(current, previous);
        services.AddOptions<KeyManagementOptions>()
            .Configure<PostgresDataProtectionXmlRepository>(
                (options, repository) => options.XmlRepository = repository);
        return services.BuildServiceProvider();
    }

    private static Database CreateDatabase(string appConnection, string systemConnection)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = appConnection,
                ["ConnectionStrings:SystemConnection"] = systemConnection,
                ["Rls:EnforceTenantContext"] = "true",
            }).Build();
        return new Database(config, new TenantScopeAccessor());
    }

    private static string SystemConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("OPSTRAX_TEST_DB_SYSTEM");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        var builder = new NpgsqlConnectionStringBuilder(TestDb.ConnectionString)
        {
            Username = "opstrax_system",
            Password = "opstrax_system_local",
        };
        return builder.ConnectionString;
    }
}
