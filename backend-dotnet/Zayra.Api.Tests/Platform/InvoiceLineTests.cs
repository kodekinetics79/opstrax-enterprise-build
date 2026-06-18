using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Zayra.Api.Controllers;
using Zayra.Api.Domain.Entities;
using Zayra.Api.Models;

namespace Zayra.Api.Tests.Platform;

/// <summary>
/// Tests for invoice line items: total calculation, CRUD guard rules, and
/// backward-compat with legacy invoices that have no line rows.
/// </summary>
public class InvoiceLineTests : PlatformTestBase
{
    private static async Task<(Tenant tenant, TenantInvoice invoice)> SeedInvoiceAsync(
        Zayra.Api.Data.ZayraDbContext db,
        decimal invoiceAmount = 0m)
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Acme Corp", Slug = "acme", IsActive = true };
        db.Tenants.Add(tenant);
        var invoice = new TenantInvoice
        {
            Id            = Guid.NewGuid(),
            TenantId      = tenant.Id,
            InvoiceNumber = "INV-001",
            Amount        = invoiceAmount,
            CurrencyCode  = "USD",
            Status        = InvoiceStatuses.Draft,
            InvoiceDate   = DateOnly.FromDateTime(DateTime.Today),
            DueDate       = DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
        };
        db.TenantInvoices.Add(invoice);
        await db.SaveChangesAsync();
        return (tenant, invoice);
    }

    // ── AddInvoiceLine ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AddLine_UpdatesInvoiceTotalCorrectly()
    {
        await using var db  = CreateDb();
        var (_, invoice)    = await SeedInvoiceAsync(db);
        var controller      = CreateController(db);

        // 2 units × $100, no discount, 15% tax → lineTotal = $230
        var req = new AddInvoiceLineRequest("Workforce Licence", 2, 100m, 0m, 15m, 1);
        var result = await controller.AddInvoiceLine(invoice.TenantId, invoice.Id, req, CancellationToken.None);

        result.Should().BeOfType<CreatedAtActionResult>();

        await db.Entry(invoice).ReloadAsync();
        invoice.Amount.Should().BeApproximately(230m, 0.01m);
    }

    [Fact]
    public async Task AddTwoLines_TotalEqualsSum()
    {
        await using var db  = CreateDb();
        var (_, invoice)    = await SeedInvoiceAsync(db);
        var controller      = CreateController(db);

        // Line 1: 1 × $500, no discount, no tax → $500
        await controller.AddInvoiceLine(invoice.TenantId, invoice.Id,
            new AddInvoiceLineRequest("Base plan", 1, 500m, 0m, 0m, 1), CancellationToken.None);

        // Line 2: 3 × $50, $10 discount, 10% tax → (150 − 10) × 1.1 = $154
        await controller.AddInvoiceLine(invoice.TenantId, invoice.Id,
            new AddInvoiceLineRequest("Add-on seats", 3, 50m, 10m, 10m, 2), CancellationToken.None);

        await db.Entry(invoice).ReloadAsync();
        invoice.Amount.Should().BeApproximately(654m, 0.01m); // 500 + 154
    }

    [Fact]
    public async Task DeleteLine_RecalculatesTotalDown()
    {
        await using var db  = CreateDb();
        var (_, invoice)    = await SeedInvoiceAsync(db);
        var controller      = CreateController(db);

        await controller.AddInvoiceLine(invoice.TenantId, invoice.Id,
            new AddInvoiceLineRequest("Plan A", 1, 300m, 0m, 0m, 1), CancellationToken.None);
        var addResult = await controller.AddInvoiceLine(invoice.TenantId, invoice.Id,
            new AddInvoiceLineRequest("Plan B", 1, 200m, 0m, 0m, 2), CancellationToken.None);

        var lineId = ((CreatedAtActionResult)addResult).Value!;
        var line = (TenantInvoiceLine)lineId;

        var deleteResult = await controller.DeleteInvoiceLine(invoice.TenantId, invoice.Id, line.Id, CancellationToken.None);
        deleteResult.Should().BeOfType<NoContentResult>();

        await db.Entry(invoice).ReloadAsync();
        invoice.Amount.Should().BeApproximately(300m, 0.01m);
    }

    [Fact]
    public async Task UpdateLine_RecalculatesTotalCorrectly()
    {
        await using var db  = CreateDb();
        var (_, invoice)    = await SeedInvoiceAsync(db);
        var controller      = CreateController(db);

        var addResult = await controller.AddInvoiceLine(invoice.TenantId, invoice.Id,
            new AddInvoiceLineRequest("Old plan", 1, 100m, 0m, 0m, 1), CancellationToken.None);
        var line = (TenantInvoiceLine)((CreatedAtActionResult)addResult).Value!;

        // Update unit price to $250
        await controller.UpdateInvoiceLine(invoice.TenantId, invoice.Id, line.Id,
            new UpdateInvoiceLineRequest(null, null, 250m, null, null, null), CancellationToken.None);

        await db.Entry(invoice).ReloadAsync();
        invoice.Amount.Should().BeApproximately(250m, 0.01m);
    }

    // ── Guard: cannot edit Paid/Cancelled invoices ─────────────────────────────

    [Fact]
    public async Task AddLine_ToPaidInvoice_Returns400()
    {
        await using var db  = CreateDb();
        var (_, invoice)    = await SeedInvoiceAsync(db);
        invoice.Status = InvoiceStatuses.Paid;
        await db.SaveChangesAsync();
        var controller = CreateController(db);

        var result = await controller.AddInvoiceLine(invoice.TenantId, invoice.Id,
            new AddInvoiceLineRequest("Blocked", 1, 100m, 0m, 0m, 1), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task DeleteLine_FromCancelledInvoice_Returns400()
    {
        await using var db  = CreateDb();
        var (_, invoice)    = await SeedInvoiceAsync(db);
        invoice.Status = InvoiceStatuses.Cancelled;
        await db.SaveChangesAsync();
        var controller = CreateController(db);

        var result = await controller.DeleteInvoiceLine(invoice.TenantId, invoice.Id, Guid.NewGuid(), CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── Legacy invoice (no lines) still lists ─────────────────────────────────

    [Fact]
    public async Task ListLines_WithNoLines_ReturnsEmpty()
    {
        await using var db  = CreateDb();
        var (_, invoice)    = await SeedInvoiceAsync(db, invoiceAmount: 1500m);
        var controller      = CreateController(db);

        var result = await controller.ListInvoiceLines(invoice.TenantId, invoice.Id, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        json.Should().Be("[]");
    }
}
