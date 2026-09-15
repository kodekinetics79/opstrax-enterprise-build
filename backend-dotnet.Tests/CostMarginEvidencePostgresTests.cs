using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class CostMarginEvidencePostgresTests
{
    [Fact]
    public async Task MarginEvidence_UsesIssuedRevenueAndApprovedRecordedCosts_WithoutCrossTenantOrCurrencyMixing()
    {
        var db = Db();
        await new RevenueReadinessSchemaService(db).EnsureAsync();
        await new FinanceActivationSchemaService(db).EnsureAsync();
        await new BillingProfileSchemaService(db).EnsureAsync();
        await new Batch5SchemaService(db).EnsureAsync();

        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = await Company(db, $"CM-A-{suffix}");
        var otherCompany = await Company(db, $"CM-B-{suffix}");
        try
        {
            var customer = await Customer(db, company, $"CMC-A-{suffix}");
            var otherCustomer = await Customer(db, otherCompany, $"CMC-B-{suffix}");
            var completeJob = await Job(db, company, customer, $"CMJ-USD-{suffix}");
            var costOnlyJob = await Job(db, company, customer, $"CMJ-CAD-{suffix}");
            var revenueOnlyJob = await Job(db, company, customer, $"CMJ-GBP-{suffix}");
            var foreignJob = await Job(db, otherCompany, otherCustomer, $"CMJ-X-{suffix}");

            await IssuedInvoice(db, company, customer, completeJob, "USD", 1_000m, suffix + "-USD");
            await IssuedInvoice(db, company, customer, revenueOnlyJob, "GBP", 400m, suffix + "-GBP");
            await IssuedInvoice(db, otherCompany, otherCustomer, foreignJob, "USD", 999_999m, suffix + "-X");

            await Expense(db, company, completeJob, "USD", 250m, "Approved", $"EXP-REC-{suffix}");
            await Expense(db, company, completeJob, "USD", 500m, "Pending", $"EXP-PENDING-{suffix}");
            await Expense(db, company, completeJob, "USD", 900m, "Approved", $"EXP-B5-{suffix}");
            await Expense(db, company, costOnlyJob, "CAD", 80m, "Approved", $"EXP-CAD-{suffix}");
            await Expense(db, otherCompany, foreignJob, "USD", 888_888m, "Approved", $"EXP-X-{suffix}");

            var service = new CostMarginEvidenceService(db);
            var rows = await service.ListJobsAsync(company);

            Assert.Equal(3, rows.Count);
            var usd = Assert.Single(rows, row => row.Currency == "USD");
            Assert.Equal($"job:{completeJob}:USD", usd.Id);
            Assert.Equal(1_000m, usd.RevenueEstimate);
            Assert.Equal(250m, usd.TotalCost);
            Assert.Equal(750m, usd.MarginEstimate);
            Assert.Equal(75m, usd.MarginPercent);
            Assert.Equal(1, usd.InvoiceCount);
            Assert.Equal(1, usd.CostRecordCount);
            Assert.Equal("Calculated", usd.Status);

            var cad = Assert.Single(rows, row => row.Currency == "CAD");
            Assert.Equal(0m, cad.RevenueEstimate);
            Assert.Equal(80m, cad.TotalCost);
            Assert.Null(cad.MarginEstimate);
            Assert.Equal("Issued revenue unavailable", cad.Status);

            var gbp = Assert.Single(rows, row => row.Currency == "GBP");
            Assert.Equal(400m, gbp.RevenueEstimate);
            Assert.Equal(0m, gbp.TotalCost);
            Assert.Null(gbp.MarginEstimate);
            Assert.Equal("Cost evidence unavailable", gbp.Status);

            var summary = await service.SummaryAsync(company);
            Assert.Equal(3, summary.JobsWithEvidence);
            Assert.Equal(1, summary.CompleteMargins);
            Assert.Equal(1, summary.MissingCostEvidence);
            Assert.Equal(1, summary.MissingIssuedRevenue);
            Assert.Collection(summary.ByCurrency,
                item => { Assert.Equal("CAD", item.Currency); Assert.Equal(0m, item.RevenueEstimate); Assert.Equal(80m, item.TotalCost); Assert.Null(item.MarginEstimate); },
                item => { Assert.Equal("GBP", item.Currency); Assert.Equal(400m, item.RevenueEstimate); Assert.Equal(0m, item.TotalCost); Assert.Null(item.MarginEstimate); },
                item => { Assert.Equal("USD", item.Currency); Assert.Equal(1_000m, item.RevenueEstimate); Assert.Equal(250m, item.TotalCost); Assert.Equal(750m, item.MarginEstimate); });
        }
        finally
        {
            await Cleanup(db, company, otherCompany);
        }
    }

    [Fact]
    public async Task RecalculationEndpoints_FailClosedInsteadOfReturningInventedResults()
    {
        var db = Db();
        await new Batch5SchemaService(db).EnsureAsync();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = await Company(db, $"CM-R-{suffix}");
        try
        {
            var customer = await Customer(db, company, $"CMRC-{suffix}");
            var job = await Job(db, company, customer, $"CMRJ-{suffix}");
            var http = Principal(company);

            var all = await InvokeAsync("CostMarginRecalculate", http);
            Assert.Equal(StatusCodes.Status501NotImplemented, Status(all));

            var one = await InvokeAsync("CostMarginRecalculateJob", http, job, db, CancellationToken.None);
            Assert.Equal(StatusCodes.Status501NotImplemented, Status(one));

            var missing = await InvokeAsync("CostMarginRecalculateJob", http, long.MaxValue, db, CancellationToken.None);
            Assert.Equal(StatusCodes.Status404NotFound, Status(missing));

            var predictions = Invoke("CostMarginPredictions", http);
            Assert.Equal(StatusCodes.Status200OK, Status(predictions));
        }
        finally
        {
            await Cleanup(db, company, 0);
        }
    }

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
        ["Rls:EnforceTenantContext"] = "false"
    }).Build());

    private static DefaultHttpContext Principal(long company)
    {
        var http = new DefaultHttpContext { TraceIdentifier = $"cost-margin-{Guid.NewGuid():N}" };
        http.Items[EndpointMappings.AuthUserIdItemKey] = 81L;
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = company;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Finance Manager";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "finance:view", "finance:manage" };
        return http;
    }

    private static async Task<IResult> InvokeAsync(string name, params object?[] args)
    {
        var method = Method(name);
        try
        {
            return await ((Task<IResult>?)method.Invoke(null, args)
                ?? throw new InvalidOperationException($"{name} did not return Task<IResult>"));
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static IResult Invoke(string name, params object?[] args)
    {
        try
        {
            return (IResult)(Method(name).Invoke(null, args)
                ?? throw new InvalidOperationException($"{name} did not return IResult"));
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static MethodInfo Method(string name) => typeof(EndpointMappings).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Missing endpoint {name}");

    private static int? Status(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static Task<long> Company(Database db, string code) => db.InsertAsync(
        "INSERT INTO companies(company_code,name,industry) VALUES(@code,@code,'Logistics') RETURNING id",
        c => c.Parameters.AddWithValue("@code", code));

    private static Task<long> Customer(Database db, long company, string code) => db.InsertAsync(
        "INSERT INTO customers(company_id,customer_code,name) VALUES(@company,@code,@code) RETURNING id",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@code", code); });

    private static Task<long> Job(Database db, long company, long customer, string code) => db.InsertAsync(
        "INSERT INTO jobs(company_id,customer_id,job_code,job_type,status) VALUES(@company,@customer,@code,'freight','delivered') RETURNING id",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@customer", customer); c.Parameters.AddWithValue("@code", code); });

    private static async Task IssuedInvoice(Database db, long company, long customer, long job, string currency, decimal total, string suffix)
    {
        var draftId = Guid.NewGuid();
        var draftNo = $"CMD-{suffix}";
        await db.ExecuteAsync(
            @"INSERT INTO invoice_drafts(id,company_id,customer_id,job_id,invoice_draft_no,status,currency,subtotal,tax_total,total)
              VALUES(@id,@company,@customer,@job,@number,'issued',@currency,@total,0,@total)",
            c => { c.Parameters.AddWithValue("@id", draftId); c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@customer", customer); c.Parameters.AddWithValue("@job", job); c.Parameters.AddWithValue("@number", draftNo); c.Parameters.AddWithValue("@currency", currency); c.Parameters.AddWithValue("@total", total); });
        await db.ExecuteAsync(
            @"INSERT INTO issued_invoices(company_id,customer_id,job_id,source_invoice_draft_id,source_invoice_draft_no,invoice_number,status,currency,subtotal,tax_total,total,balance_due,payment_status,document_type,credit_total)
              VALUES(@company,@customer,@job,@draft,@draftNo,@invoice,'issued',@currency,@total,0,@total,@total,'unpaid','invoice',0)",
            c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@customer", customer); c.Parameters.AddWithValue("@job", job); c.Parameters.AddWithValue("@draft", draftId); c.Parameters.AddWithValue("@draftNo", draftNo); c.Parameters.AddWithValue("@invoice", $"CMI-{suffix}"); c.Parameters.AddWithValue("@currency", currency); c.Parameters.AddWithValue("@total", total); });
    }

    private static Task Expense(Database db, long company, long job, string currency, decimal amount, string approval, string number) => db.ExecuteAsync(
        @"INSERT INTO expenses(company_id,expense_number,category,title,category_name,amount,currency,expense_date,job_id,status,approval_status,receipt_status)
          VALUES(@company,@number,'Fuel','Recorded cost','Fuel',@amount,@currency,CURRENT_DATE,@job,@approval,@approval,'Uploaded')",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@number", number); c.Parameters.AddWithValue("@amount", amount); c.Parameters.AddWithValue("@currency", currency); c.Parameters.AddWithValue("@job", job); c.Parameters.AddWithValue("@approval", approval); });

    private static async Task Cleanup(Database db, long company, long otherCompany)
    {
        foreach (var table in new[] { "issued_invoices", "invoice_drafts", "expenses", "jobs", "customers" })
            await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@company OR (@other > 0 AND company_id=@other)", c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@other", otherCompany); });
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@company OR (@other > 0 AND id=@other)", c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@other", otherCompany); });
    }
}
