using System.Collections;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class ExpenseWorkflowPostgresTests
{
    [Fact]
    public async Task ExpenseIntegrityMigration_EnforcesRulesForNewWrites()
    {
        var db = Db();
        await new Batch5SchemaService(db).EnsureAsync();
        var migration = File.ReadAllText(Path.Combine(RepoRoot, "database", "migrations", "2026_09_08_expense_workflow_integrity.sql"));
        await db.ExecuteAsync(migration);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = await Company(db, $"EXP-CONSTRAINT-{suffix}");
        try
        {
            var invalid = await Assert.ThrowsAsync<PostgresException>(() => db.InsertAsync(
                @"INSERT INTO expenses(company_id,expense_number,category,title,category_name,amount,currency,expense_date,status,approval_status,receipt_status)
                  VALUES(@c,@number,'Fuel','Invalid zero expense','Fuel',0,'usd',CURRENT_DATE,'Pending','Pending','Missing')",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@number", $"EXP-20260908010101001-{suffix[..9].ToUpperInvariant()}"); }));
            Assert.Equal("23514", invalid.SqlState);
        }
        finally
        {
            await Cleanup(db, company, 0);
        }
    }

    [Fact]
    public async Task ExpenseWorkflow_ForcesPending_ValidatesTenantLinks_AndGuardsTransitions()
    {
        var db = Db();
        await new Batch5SchemaService(db).EnsureAsync();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = await Company(db, $"EXP-A-{suffix}");
        var otherCompany = await Company(db, $"EXP-B-{suffix}");
        try
        {
            var customer = await Customer(db, company, $"CA-{suffix}", "Tenant A customer");
            var foreignCustomer = await Customer(db, otherCompany, $"CB-{suffix}", "Tenant B customer");
            var http = Principal(company);
            var audit = new AuditService(db);

            var forgedApproval = Body(customer);
            forgedApproval["approvalStatus"] = "Approved";
            forgedApproval["status"] = "Approved";
            forgedApproval["riskScore"] = 99;
            forgedApproval["recommendedAction"] = "System verified";
            var created = await Invoke("CreateExpense", http, forgedApproval, db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status201Created, Status(created));

            var stored = await db.QuerySingleAsync(
                "SELECT * FROM expenses WHERE company_id=@c AND customer_id=@customer ORDER BY id DESC LIMIT 1",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@customer", customer); });
            Assert.NotNull(stored);
            Assert.Equal("Pending", stored!["approvalStatus"]);
            Assert.Equal("Pending", stored["status"]);
            Assert.Equal(0m, Convert.ToDecimal(stored["riskScore"]));
            Assert.Equal("Repairs", stored["category"]);
            Assert.Equal("Repairs expense", stored["title"]);
            var expenseId = Convert.ToInt64(stored["id"]);

            var foreign = Body(foreignCustomer);
            var rejectedForeign = await Invoke("CreateExpense", http, foreign, db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest, Status(rejectedForeign));

            var zero = Body(customer);
            zero["amount"] = 0;
            Assert.Equal(StatusCodes.Status400BadRequest,
                Status(await Invoke("CreateExpense", http, zero, db, audit, CancellationToken.None)));

            Assert.Equal(StatusCodes.Status200OK,
                Status(await Invoke("ExpenseApprove", http, expenseId, new Dictionary<string, object?>(), db, audit, CancellationToken.None)));
            Assert.Equal(StatusCodes.Status409Conflict,
                Status(await Invoke("ExpenseApprove", http, expenseId, new Dictionary<string, object?>(), db, audit, CancellationToken.None)));
            Assert.Equal(StatusCodes.Status409Conflict,
                Status(await Invoke("UpdateExpense", http, expenseId, Body(customer), db, audit, CancellationToken.None)));
            Assert.Equal(StatusCodes.Status404NotFound,
                Status(await Invoke("ExpenseReject", http, long.MaxValue, new Dictionary<string, object?>(), db, audit, CancellationToken.None)));
            var missingReceipt = Body(customer);
            missingReceipt["receiptStatus"] = "Missing";
            var missingCreated = await Invoke("CreateExpense", http, missingReceipt, db, audit, CancellationToken.None);
            Assert.Equal(StatusCodes.Status201Created, Status(missingCreated));
            var missingId = await db.ScalarLongAsync(
                "SELECT id FROM expenses WHERE company_id=@c AND approval_status='Pending' AND receipt_status='Missing' ORDER BY id DESC LIMIT 1",
                c => c.Parameters.AddWithValue("@c", company));
            Assert.Equal(StatusCodes.Status409Conflict,
                Status(await Invoke("ExpenseApprove", http, missingId, new Dictionary<string, object?>(), db, audit, CancellationToken.None)));
            Assert.Equal(StatusCodes.Status501NotImplemented,
                Status(await Invoke("ExpenseImportPreview", http, new Dictionary<string, object?>(), CancellationToken.None)));
        }
        finally
        {
            await Cleanup(db, company, otherCompany);
        }
    }

    [Fact]
    public async Task ExpenseReads_LabelDemoRows_AndNeverJoinForeignTenantNames()
    {
        var db = Db();
        await new Batch5SchemaService(db).EnsureAsync();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = await Company(db, $"EXP-READ-A-{suffix}");
        var otherCompany = await Company(db, $"EXP-READ-B-{suffix}");
        try
        {
            var foreignCustomer = await Customer(db, otherCompany, $"RC-{suffix}", "Foreign tenant name");
            var id = await db.InsertAsync(
                @"INSERT INTO expenses
                    (company_id,expense_number,category,title,category_name,amount,currency,expense_date,
                     customer_id,status,approval_status,receipt_status,risk_score)
                  VALUES(@c,@number,'Fuel','Fuel expense','Fuel',25,'USD',CURRENT_DATE,@customer,
                         'Pending','Pending','Missing',88)",
                c =>
                {
                    c.Parameters.AddWithValue("@c", company);
                    c.Parameters.AddWithValue("@number", $"EXP-B5-READ-{suffix}");
                    c.Parameters.AddWithValue("@customer", foreignCustomer);
                });

            var result = await Invoke("Expenses", Principal(company), db, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(result));
            var rows = Data(result).Cast<Dictionary<string, object?>>().ToList();
            var row = Assert.Single(rows, value => Convert.ToInt64(value["id"]) == id);
            Assert.Null(row["customerName"]);
            Assert.Equal("Demo Data", row["recordOrigin"]);
            Assert.Equal("Receipt Required", row["recordAttention"]);
            Assert.False(row.ContainsKey("riskHeatScore"));
        }
        finally
        {
            await Cleanup(db, company, otherCompany);
        }
    }

    private static Dictionary<string, object?> Body(long customerId) => new()
    {
        ["categoryName"] = "Repairs",
        ["amount"] = 125.50m,
        ["currency"] = "usd",
        ["expenseDate"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
        ["customerId"] = customerId,
        ["vendorName"] = "Tenant vendor",
        ["receiptStatus"] = "Uploaded",
        ["notes"] = "Persisted test expense"
    };

    private static DefaultHttpContext Principal(long company)
    {
        var http = new DefaultHttpContext { TraceIdentifier = $"expense-{Guid.NewGuid():N}" };
        http.Items[EndpointMappings.AuthUserIdItemKey] = 71L;
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = company;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Finance Manager";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "finance:view", "finance:manage" };
        return http;
    }

    private static async Task<IResult> Invoke(string name, params object?[] args)
    {
        var method = typeof(EndpointMappings).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Missing endpoint {name}");
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

    private static int? Status(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static IEnumerable Data(IResult result)
    {
        var envelope = Assert.IsAssignableFrom<IValueHttpResult>(result).Value!;
        return Assert.IsAssignableFrom<IEnumerable>(envelope.GetType().GetProperty("Data")!.GetValue(envelope));
    }

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
        ["Rls:EnforceTenantContext"] = "false"
    }).Build());

    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    private static Task<long> Company(Database db, string code) => db.InsertAsync(
        "INSERT INTO companies(company_code,name,industry) VALUES(@code,@code,'Logistics') RETURNING id",
        c => c.Parameters.AddWithValue("@code", code));

    private static Task<long> Customer(Database db, long company, string code, string name) => db.InsertAsync(
        "INSERT INTO customers(company_id,customer_code,name) VALUES(@company,@code,@name) RETURNING id",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@code", code); c.Parameters.AddWithValue("@name", name); });

    private static async Task Cleanup(Database db, long company, long otherCompany)
    {
        await db.ExecuteAsync("DELETE FROM audit_logs WHERE company_id=@company OR (@other > 0 AND company_id=@other)", c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@other", otherCompany); });
        await db.ExecuteAsync("DELETE FROM expenses WHERE company_id=@company OR (@other > 0 AND company_id=@other)", c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@other", otherCompany); });
        await db.ExecuteAsync("DELETE FROM customers WHERE company_id=@company OR (@other > 0 AND company_id=@other)", c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@other", otherCompany); });
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@company OR (@other > 0 AND id=@other)", c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@other", otherCompany); });
    }
}
