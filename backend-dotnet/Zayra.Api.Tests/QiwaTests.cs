using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Zayra.Api.Data;
using Zayra.Api.Infrastructure.Payroll;
using Zayra.Api.Infrastructure.Qiwa;
using Zayra.Api.Models;

namespace Zayra.Api.Tests;

public class QiwaTests
{
    private static ZayraDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ZayraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new ZayraDbContext(options);
    }

    private static QiwaIntegrationService CreateService(ZayraDbContext db)
        => new(db, NullLogger<QiwaIntegrationService>.Instance, DataProtectionProvider.Create("ZayraTests"));

    private static Employee ReadyEmployee(Guid tenantId, int id = 1) => new()
    {
        Id = id,
        TenantId = tenantId,
        EmployeeCode = $"EMP-{id:D3}",
        FullName = "Test Employee",
        Status = "Active",
        SaudiOrNonSaudi = "Saudi",
        IdType = "NationalId",
        IdNumber = "1234567890",
        Nationality = "Saudi",
        OccupationCode = "2421",
        EstablishmentId = "7000123456",
        WorkLocationId = "WL-1",
        ContractReference = "CONTRACT-1",
    };

    // ── Readiness ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckReadiness_AllFieldsPresent_ReturnsReady()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        db.Employees.Add(ReadyEmployee(tenantId));
        await db.SaveChangesAsync();

        var report = await CreateService(db).CheckEmployeeReadinessAsync(tenantId, 1);

        Assert.True(report.IsReady);
        Assert.Empty(report.MissingFields);
    }

    [Fact]
    public async Task CheckReadiness_MissingOccupationCode_ReturnsNotReady()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        var emp = ReadyEmployee(tenantId);
        emp.OccupationCode = "";
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        var report = await CreateService(db).CheckEmployeeReadinessAsync(tenantId, 1);

        Assert.False(report.IsReady);
        Assert.Contains("occupation_code", report.MissingFields);
    }

    [Fact]
    public async Task SaveApiCredential_EncryptsSecret_NeverStoresPlaintext()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        var svc = CreateService(db);

        await svc.SaveApiCredentialAsync(tenantId, "client-1", "super-secret", "sandbox", Guid.NewGuid(), "127.0.0.1");

        var cred = await db.QiwaApiCredentials.FirstAsync();
        Assert.Equal("client-1", cred.ClientId);
        Assert.NotEqual("super-secret", cred.EncryptedClientSecret);
        Assert.False(string.IsNullOrEmpty(cred.EncryptedClientSecret));
    }

    // ── IBAN validation ───────────────────────────────────────────────────────

    [Fact]
    public void IbanValidator_ValidSaudiIban_ReturnsTrue()
    {
        // Valid Saudi IBAN (24 chars, passes mod-97).
        const string iban = "SA0380000000608010167519";
        Assert.True(IbanValidator.IsValid(iban));
        Assert.True(IbanValidator.IsSaudiIban(iban));
    }

    [Fact]
    public void IbanValidator_InvalidIban_ReturnsFalse()
    {
        Assert.False(IbanValidator.IsValid("SA0000000000000000000000"));
        Assert.False(IbanValidator.IsValid(""));
        Assert.False(IbanValidator.IsValid(null));
        Assert.False(IbanValidator.IsValid("NOTANIBAN"));
    }

    [Fact]
    public void IbanValidator_UaeIban_ReturnsFalseForSaudi()
    {
        // A structurally valid UAE IBAN must NOT pass the Saudi-specific check.
        const string uae = "AE070331234567890123456";
        Assert.True(IbanValidator.IsValid(uae));     // valid IBAN
        Assert.False(IbanValidator.IsSaudiIban(uae)); // but not Saudi
    }

    // ── Sandbox adapter ───────────────────────────────────────────────────────

    [Fact]
    public async Task SandboxAdapter_ValidPayload_ReturnsSuccess()
    {
        var adapter = new SandboxQiwaApiAdapter(NullLogger<SandboxQiwaApiAdapter>.Instance);
        var payload = new QiwaEmployeePayload(
            "EMP-001", "1234567890", "NationalId", "Saudi", "Saudi", "2421", "7000123456", "WL-1", "CONTRACT-1");

        var result = await adapter.PushEmployeeAsync("token", payload, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task SandboxAdapter_EmptyIdNumber_ReturnsFieldMissingError()
    {
        var adapter = new SandboxQiwaApiAdapter(NullLogger<SandboxQiwaApiAdapter>.Instance);
        var payload = new QiwaEmployeePayload(
            "EMP-001", "", "NationalId", "Saudi", "Saudi", "2421", "7000123456", "WL-1", "CONTRACT-1");

        var result = await adapter.PushEmployeeAsync("token", payload, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("FIELD_MISSING", result.ErrorCode);
    }

    // ── SyncWorker: credential validation ────────────────────────────────────

    private static QiwaSyncWorker CreateWorker(ZayraDbContext db, IQiwaApiAdapter adapter)
        => new QiwaSyncWorker(
            new SingletonScopeFactory(db),
            adapter,
            new QiwaOAuthTokenCache(),
            DataProtectionProvider.Create("ZayraTests"),
            NullLogger<QiwaSyncWorker>.Instance);

    [Fact]
    public async Task SyncWorker_NoCredentialRow_DoesNotCallAdapter_DeadLettersLog()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        db.Employees.Add(ReadyEmployee(tenantId));
        db.QiwaTenantConnections.Add(new QiwaTenantConnection { TenantId = tenantId });
        db.QiwaSyncLogs.Add(new QiwaSyncLog
        {
            TenantId = tenantId, EmployeeId = 1,
            Status = QiwaSyncLogStatuses.Pending, Direction = "Push", MaxRetries = 3
        });
        await db.SaveChangesAsync();

        var spy = new SpyQiwaApiAdapter();
        await CreateWorker(db, spy).ProcessOnceAsync(CancellationToken.None);

        Assert.Empty(spy.TokenCalls); // adapter must NOT be called
        var log = await db.QiwaSyncLogs.FirstAsync();
        Assert.Equal(QiwaSyncLogStatuses.DeadLetter, log.Status);
        Assert.NotNull(log.DeadLetterReason);
        Assert.Contains("client ID", log.DeadLetterReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SyncWorker_EmptyClientId_DoesNotCallAdapter_DeadLettersLog()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        db.Employees.Add(ReadyEmployee(tenantId));
        db.QiwaTenantConnections.Add(new QiwaTenantConnection { TenantId = tenantId });
        // Credential row exists but ClientId is blank
        db.QiwaApiCredentials.Add(new QiwaApiCredential
        {
            TenantId = tenantId, ClientId = "  ",
            EncryptedClientSecret = "any-encrypted-value", Environment = "sandbox"
        });
        db.QiwaSyncLogs.Add(new QiwaSyncLog
        {
            TenantId = tenantId, EmployeeId = 1,
            Status = QiwaSyncLogStatuses.Pending, Direction = "Push", MaxRetries = 3
        });
        await db.SaveChangesAsync();

        var spy = new SpyQiwaApiAdapter();
        await CreateWorker(db, spy).ProcessOnceAsync(CancellationToken.None);

        Assert.Empty(spy.TokenCalls);
        var log = await db.QiwaSyncLogs.FirstAsync();
        Assert.Equal(QiwaSyncLogStatuses.DeadLetter, log.Status);
    }

    [Fact]
    public async Task SyncWorker_MissingCredentials_SetsConnectionToConfigurationError()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        db.Employees.Add(ReadyEmployee(tenantId));
        db.QiwaTenantConnections.Add(new QiwaTenantConnection
        {
            TenantId = tenantId, Status = QiwaConnectionStatuses.Disconnected
        });
        db.QiwaSyncLogs.Add(new QiwaSyncLog
        {
            TenantId = tenantId, EmployeeId = 1,
            Status = QiwaSyncLogStatuses.Pending, Direction = "Push", MaxRetries = 3
        });
        await db.SaveChangesAsync();

        await CreateWorker(db, new SpyQiwaApiAdapter()).ProcessOnceAsync(CancellationToken.None);

        var conn = await db.QiwaTenantConnections.FirstAsync();
        Assert.Equal(QiwaConnectionStatuses.ConfigurationError, conn.Status);
        Assert.NotNull(conn.LastErrorMessage);
        Assert.NotNull(conn.LastCheckedAtUtc);
    }

    [Fact]
    public async Task SyncWorker_MissingCredentials_AuditEventContainsNoSecretValues()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        db.Employees.Add(ReadyEmployee(tenantId));
        db.QiwaTenantConnections.Add(new QiwaTenantConnection { TenantId = tenantId });
        db.QiwaSyncLogs.Add(new QiwaSyncLog
        {
            TenantId = tenantId, EmployeeId = 1,
            Status = QiwaSyncLogStatuses.Pending, Direction = "Push", MaxRetries = 3
        });
        await db.SaveChangesAsync();

        await CreateWorker(db, new SpyQiwaApiAdapter()).ProcessOnceAsync(CancellationToken.None);

        var entries = await db.AuditLogs.ToListAsync();
        Assert.Contains(entries, a => a.Action == "qiwa.sync_missing_credentials");
        foreach (var entry in entries)
        {
            // Audit payloads must never contain secret values
            var meta = entry.Metadata ?? string.Empty;
            Assert.DoesNotContain("secret", meta, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", meta, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task SyncWorker_ValidCredentials_CallsAcquireAccessToken_SecretNotInAudit()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        db.Employees.Add(ReadyEmployee(tenantId));
        db.QiwaTenantConnections.Add(new QiwaTenantConnection { TenantId = tenantId, Environment = "sandbox" });

        // Use the integration service to store properly-encrypted credentials.
        await CreateService(db).SaveApiCredentialAsync(
            tenantId, "test-client-id", "test-super-secret", "sandbox", Guid.NewGuid(), "127.0.0.1");

        db.QiwaSyncLogs.Add(new QiwaSyncLog
        {
            TenantId = tenantId, EmployeeId = 1,
            Status = QiwaSyncLogStatuses.Pending, Direction = "Push", MaxRetries = 3
        });
        await db.SaveChangesAsync();

        var spy = new SpyQiwaApiAdapter();
        await CreateWorker(db, spy).ProcessOnceAsync(CancellationToken.None);

        // Adapter was called exactly once for token acquisition
        Assert.Single(spy.TokenCalls);
        var (clientId, _, environment) = spy.TokenCalls[0];
        Assert.Equal("test-client-id", clientId);
        Assert.Equal("sandbox", environment);

        // Secret value must never appear in any audit log
        var entries = await db.AuditLogs.ToListAsync();
        foreach (var entry in entries)
            Assert.DoesNotContain("test-super-secret", entry.Metadata ?? "", StringComparison.OrdinalIgnoreCase);
    }
}

// ── Test doubles ──────────────────────────────────────────────────────────────

/// <summary>
/// Records every call to AcquireAccessTokenAsync so tests can assert the adapter
/// was or was not called without making any network requests.
/// </summary>
file sealed class SpyQiwaApiAdapter : IQiwaApiAdapter
{
    public string AdapterName => "spy";

    /// <summary>Arguments captured from each AcquireAccessTokenAsync call.</summary>
    public List<(string ClientId, string ClientSecret, string Environment)> TokenCalls { get; } = [];

    public Task<string?> AcquireAccessTokenAsync(
        string clientId, string clientSecret, string environment, CancellationToken ct)
    {
        TokenCalls.Add((clientId, clientSecret, environment));
        return Task.FromResult<string?>($"spy-token-{Guid.NewGuid():N}");
    }

    public Task<QiwaApiResult> PushEmployeeAsync(
        string accessToken, QiwaEmployeePayload payload, CancellationToken ct)
        => Task.FromResult(new QiwaApiResult(true, null, null, "{\"status\":\"synced\"}"));

    public Task<QiwaApiResult> GetEmployeeStatusAsync(
        string accessToken, string establishmentId, string employeeIdNumber, CancellationToken ct)
        => Task.FromResult(new QiwaApiResult(true, null, null, "{\"status\":\"active\"}"));
}

/// <summary>
/// Minimal IServiceScopeFactory that always returns the same ZayraDbContext instance.
/// Avoids a full DI container in tests while satisfying QiwaSyncWorker's scope creation.
/// </summary>
file sealed class SingletonScopeFactory(ZayraDbContext db) : IServiceScopeFactory
{
    public IServiceScope CreateScope() => new DbScope(db);

    private sealed class DbScope(ZayraDbContext db) : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = new DbProvider(db);
        public void Dispose() { }
    }

    private sealed class DbProvider(ZayraDbContext db) : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(ZayraDbContext) ? db : null;
    }
}
