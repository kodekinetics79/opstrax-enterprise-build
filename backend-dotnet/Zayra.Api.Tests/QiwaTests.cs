using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
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
}
