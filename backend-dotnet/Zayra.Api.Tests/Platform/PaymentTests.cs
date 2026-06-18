using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Zayra.Api.Controllers;
using Zayra.Api.Domain.Entities;
using Zayra.Api.Infrastructure.Auth;
using Zayra.Api.Models;

namespace Zayra.Api.Tests.Platform;

/// <summary>
/// Tests for tenant payments: invoice status derivation, partial payment,
/// full payment, role-based access (Finance can; Support cannot).
/// </summary>
public class PaymentTests : PlatformTestBase
{
    private static async Task<(Tenant tenant, TenantInvoice invoice)> SeedAsync(
        Zayra.Api.Data.ZayraDbContext db,
        decimal amount = 1000m,
        string status  = InvoiceStatuses.Sent)
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Beta Corp", Slug = "beta", IsActive = true };
        db.Tenants.Add(tenant);
        var invoice = new TenantInvoice
        {
            Id            = Guid.NewGuid(),
            TenantId      = tenant.Id,
            InvoiceNumber = "INV-P01",
            Amount        = amount,
            CurrencyCode  = "USD",
            Status        = status,
            InvoiceDate   = DateOnly.FromDateTime(DateTime.Today),
            DueDate       = DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
        };
        db.TenantInvoices.Add(invoice);
        await db.SaveChangesAsync();
        return (tenant, invoice);
    }

    // ── Full payment marks invoice Paid ───────────────────────────────────────

    [Fact]
    public async Task FullPayment_MarksInvoicePaid()
    {
        await using var db  = CreateDb();
        var (_, invoice)    = await SeedAsync(db, amount: 500m);
        var controller      = CreateController(db);

        var result = await controller.CreatePayment(invoice.TenantId, invoice.Id,
            new CreatePaymentRequest(500m, "USD", "BankTransfer", "REF001", PaymentStatuses.Completed, null, null, null),
            CancellationToken.None);

        result.Should().BeOfType<CreatedAtActionResult>();

        await db.Entry(invoice).ReloadAsync();
        invoice.Status.Should().Be(InvoiceStatuses.Paid);
    }

    [Fact]
    public async Task OverpaymentIsAllowed_AndStillMarksPaid()
    {
        await using var db  = CreateDb();
        var (_, invoice)    = await SeedAsync(db, amount: 500m);
        var controller      = CreateController(db);

        await controller.CreatePayment(invoice.TenantId, invoice.Id,
            new CreatePaymentRequest(600m, "USD", "Cheque", null, PaymentStatuses.Completed, null, null, "Overpaid"),
            CancellationToken.None);

        await db.Entry(invoice).ReloadAsync();
        invoice.Status.Should().Be(InvoiceStatuses.Paid);
    }

    // ── Partial payment keeps invoice PartiallyPaid ───────────────────────────

    [Fact]
    public async Task PartialPayment_KeepsInvoicePartiallyPaid()
    {
        await using var db  = CreateDb();
        var (_, invoice)    = await SeedAsync(db, amount: 1000m);
        var controller      = CreateController(db);

        await controller.CreatePayment(invoice.TenantId, invoice.Id,
            new CreatePaymentRequest(400m, "USD", "BankTransfer", "PART-01", PaymentStatuses.Completed, null, null, null),
            CancellationToken.None);

        await db.Entry(invoice).ReloadAsync();
        invoice.Status.Should().Be(InvoiceStatuses.PartiallyPaid);
    }

    [Fact]
    public async Task TwoPartialPayments_ThatSumToFull_MarksPaid()
    {
        await using var db  = CreateDb();
        var (_, invoice)    = await SeedAsync(db, amount: 1000m);
        var controller      = CreateController(db);

        await controller.CreatePayment(invoice.TenantId, invoice.Id,
            new CreatePaymentRequest(600m, "USD", "BankTransfer", "P1", PaymentStatuses.Completed, null, null, null),
            CancellationToken.None);
        await controller.CreatePayment(invoice.TenantId, invoice.Id,
            new CreatePaymentRequest(400m, "USD", "Cheque", "P2", PaymentStatuses.Completed, null, null, null),
            CancellationToken.None);

        await db.Entry(invoice).ReloadAsync();
        invoice.Status.Should().Be(InvoiceStatuses.Paid);
    }

    // ── Failed payment does NOT change invoice status ─────────────────────────

    [Fact]
    public async Task FailedPayment_DoesNotMarkPaid()
    {
        await using var db  = CreateDb();
        var (_, invoice)    = await SeedAsync(db, amount: 500m);
        var controller      = CreateController(db);

        await controller.CreatePayment(invoice.TenantId, invoice.Id,
            new CreatePaymentRequest(500m, "USD", "Online", null, PaymentStatuses.Failed, null, null, null),
            CancellationToken.None);

        await db.Entry(invoice).ReloadAsync();
        invoice.Status.Should().Be(InvoiceStatuses.Sent, "failed payment must not mark invoice as paid");
    }

    // ── Delete payment re-derives status ──────────────────────────────────────

    [Fact]
    public async Task DeletePayment_RevertsInvoiceFromPaid()
    {
        await using var db  = CreateDb();
        var (_, invoice)    = await SeedAsync(db, amount: 500m);
        var controller      = CreateController(db);

        var createResult = await controller.CreatePayment(invoice.TenantId, invoice.Id,
            new CreatePaymentRequest(500m, "USD", "BankTransfer", null, PaymentStatuses.Completed, null, null, null),
            CancellationToken.None);
        var payment = (TenantPayment)((CreatedAtActionResult)createResult).Value!;

        await db.Entry(invoice).ReloadAsync();
        invoice.Status.Should().Be(InvoiceStatuses.Paid);

        await controller.DeletePayment(invoice.TenantId, payment.Id, CancellationToken.None);

        await db.Entry(invoice).ReloadAsync();
        invoice.Status.Should().Be(InvoiceStatuses.Sent, "deleting the only payment must revert invoice to Sent");
    }

    // ── Role: Finance can manage payments ─────────────────────────────────────

    [Fact]
    public async Task Finance_CanCreatePayment_Returns201()
    {
        await using var db  = CreateDb();
        var (_, invoice)    = await SeedAsync(db);
        var controller      = CreateController(db, PlatformRoles.Finance);

        var result = await controller.CreatePayment(invoice.TenantId, invoice.Id,
            new CreatePaymentRequest(200m, "USD", "BankTransfer", null, PaymentStatuses.Completed, null, null, null),
            CancellationToken.None);

        // Finance role has [RequirePlatformRole(Finance)] on the endpoint;
        // filter is not invoked in unit tests but we verify the endpoint logic runs without throwing.
        result.Should().BeOfType<CreatedAtActionResult>();
    }

    // ── Support role cannot reach payment endpoints (permission at attribute level) ─

    [Fact]
    public async Task Support_AccessingPayments_RequirePlatformRoleAttributeExcludes()
    {
        // The [RequirePlatformRole] filter is NOT executed in unit tests.
        // This test verifies that Support is not listed in the allowed roles by checking
        // the attribute on the endpoint method via reflection.
        var method = typeof(PlatformController).GetMethod(nameof(PlatformController.CreatePayment));
        var attr = method!.GetCustomAttributes(typeof(RequirePlatformRoleAttribute), true)
            .Cast<RequirePlatformRoleAttribute>()
            .FirstOrDefault();

        attr.Should().NotBeNull("CreatePayment must have RequirePlatformRole");
        attr!.Roles.Should().NotContain(PlatformRoles.Support,
            "Support must not be able to create payments");
    }

    // ── Non-existent invoice returns 404 ──────────────────────────────────────

    [Fact]
    public async Task CreatePayment_WithUnknownInvoice_Returns404()
    {
        await using var db = CreateDb();
        var controller     = CreateController(db);

        var result = await controller.CreatePayment(Guid.NewGuid(), Guid.NewGuid(),
            new CreatePaymentRequest(100m, "USD", "Cash", null, PaymentStatuses.Completed, null, null, null),
            CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }
}
