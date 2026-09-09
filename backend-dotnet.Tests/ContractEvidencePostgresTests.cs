using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class ContractEvidencePostgresTests
{
    [Fact]
    public async Task ContractWorkflow_HidesDemo_ValidatesTenantLinks_AndPersistsRateProvenance()
    {
        var db = Db();
        await new Batch5SchemaService(db).EnsureAsync();
        await new CommercialFoundationSchemaService(db).EnsureAsync();
        await new BusinessSpineSchemaService(db).EnsureAsync();
        await db.ExecuteAsync(File.ReadAllText(Path.Combine(RepoRoot, "database", "migrations", "2026_09_08_contract_evidence_integrity.sql")));
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var company = await Company(db, $"CON-A-{suffix}");
        var other = await Company(db, $"CON-B-{suffix}");
        try
        {
            var customer = await Customer(db, company, $"CA-{suffix}", "Tenant A customer");
            var foreignCustomer = await Customer(db, other, $"CB-{suffix}", "Foreign customer");
            var carrier = await Carrier(db, company, $"CAR-A-{suffix}", "Tenant A carrier");
            var foreignContract = await Contract(db, other, foreignCustomer, $"FOREIGN-{suffix}", "manual_entry");
            _ = await Contract(db, company, customer, $"CON-B5-{suffix}", "demo_seed");
            var http = Principal(company);
            var audit = new AuditService(db);
            var commercial = new CommercialFoundationService(db);
            var spine = new BusinessSpineService(db);
            var events = new InMemoryDomainEventPublisher();

            var forged = ContractBody($"FORGED-{suffix}", foreignCustomer, carrier);
            Assert.Equal(StatusCodes.Status400BadRequest,
                Status(await Invoke("CreateContract", http, forged, db, audit, commercial, events, CancellationToken.None)));

            var body = ContractBody($"REAL-{suffix}", customer, carrier);
            Assert.Equal(StatusCodes.Status201Created,
                Status(await Invoke("CreateContract", http, body, db, audit, commercial, events, CancellationToken.None)));
            var contractId = await db.ScalarLongAsync(
                "SELECT id FROM contracts WHERE company_id=@company AND contract_number=@number",
                c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@number", $"REAL-{suffix}"); });
            var persisted = await db.QuerySingleAsync("SELECT data_origin,currency,margin_risk FROM contracts WHERE id=@id", c => c.Parameters.AddWithValue("@id", contractId));
            Assert.Equal("manual_entry", persisted!["dataOrigin"]);
            Assert.Equal("CAD", persisted["currency"]);
            Assert.Equal("Unassessed", persisted["marginRisk"]);

            var rows = Data(await Invoke("Contracts", http, db, CancellationToken.None)).Cast<Dictionary<string, object?>>().ToList();
            var visible = Assert.Single(rows);
            Assert.Equal(contractId, Convert.ToInt64(visible["id"]));
            Assert.Equal("Manual entry", visible["recordOrigin"]);
            Assert.DoesNotContain(rows, row => row["contractCode"]?.ToString()?.StartsWith("CON-B5-") == true);

            var badRate = RateBody($"RATE-X-{suffix}");
            Assert.Equal(StatusCodes.Status404NotFound,
                Status(await Invoke("CreateContractRate", http, foreignContract, badRate, db, audit, spine, events, CancellationToken.None)));
            var rateBody = RateBody($"RATE-A-{suffix}");
            Assert.Equal(StatusCodes.Status201Created,
                Status(await Invoke("CreateContractRate", http, contractId, rateBody, db, audit, spine, events, CancellationToken.None)));
            var rate = await db.QuerySingleAsync(
                "SELECT currency,base_rate,data_origin FROM contract_rates WHERE company_id=@company AND rate_code=@code",
                c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@code", $"RATE-A-{suffix}"); });
            Assert.Equal("CAD", rate!["currency"]);
            Assert.Equal(3.25m, Convert.ToDecimal(rate["baseRate"]));
            Assert.Equal("manual_entry", rate["dataOrigin"]);

            var summaryJson = JsonSerializer.Serialize(Value(await Invoke("ContractsSummary", http, db, CancellationToken.None)));
            Assert.Contains("\"currency\":\"CAD\"", summaryJson);
            Assert.Contains("\"rateType\":\"Per Mile\"", summaryJson);
            Assert.DoesNotContain("contractRevenueEstimate", summaryJson);
            Assert.DoesNotContain("underpricedContracts", summaryJson);

            var detailJson = JsonSerializer.Serialize(Value(await Invoke("ContractDetail", http, contractId, db, commercial, CancellationToken.None)));
            Assert.Contains("\"recordOrigin\":\"Manual entry\"", detailJson);
            Assert.Contains($"\"rateCardCode\":\"RATE-A-{suffix}\"", detailJson);
            Assert.DoesNotContain("Foreign customer", detailJson);
            Assert.DoesNotContain("marginRisk", detailJson);

            var forgedUpdate = new Dictionary<string, object?> { ["customerId"] = foreignCustomer };
            Assert.Equal(StatusCodes.Status400BadRequest,
                Status(await Invoke("UpdateContract", http, contractId, forgedUpdate, db, audit, commercial, events, CancellationToken.None)));
        }
        finally
        {
            await Cleanup(db, company, other);
        }
    }

    private static Dictionary<string, object?> ContractBody(string number, long customer, long carrier) => new()
    {
        ["contractNumber"] = number,
        ["title"] = "Recorded lane agreement",
        ["customerId"] = customer,
        ["carrierId"] = carrier,
        ["contractType"] = "Customer",
        ["rateType"] = "Per Mile",
        ["baseRate"] = 3.10m,
        ["currency"] = "cad",
        ["effectiveDate"] = DateTime.UtcNow.Date.ToString("yyyy-MM-dd"),
        ["expiryDate"] = DateTime.UtcNow.Date.AddYears(1).ToString("yyyy-MM-dd"),
        ["status"] = "Draft",
        ["marginRisk"] = "High"
    };

    private static Dictionary<string, object?> RateBody(string code) => new()
    {
        ["rateCode"] = code,
        ["rateType"] = "Per Mile",
        ["baseRate"] = 3.25m,
        ["currency"] = "cad",
        ["effectiveDate"] = DateTime.UtcNow.Date.ToString("yyyy-MM-dd")
    };

    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
        ["Rls:EnforceTenantContext"] = "false"
    }).Build());

    private static DefaultHttpContext Principal(long company)
    {
        var http = new DefaultHttpContext { TraceIdentifier = $"contract-evidence-{Guid.NewGuid():N}" };
        http.Items[EndpointMappings.AuthUserIdItemKey] = 92L;
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = company;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Finance Manager";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[]
        {
            "contract.view", "contract.create", "contract.update", "rate_card.create", "rate_card.update", "finance:manage"
        };
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
    private static object Value(IResult result) => Assert.IsAssignableFrom<IValueHttpResult>(result).Value!
        .GetType().GetProperty("Data")!.GetValue(Assert.IsAssignableFrom<IValueHttpResult>(result).Value!)!;
    private static IEnumerable Data(IResult result) => Assert.IsAssignableFrom<IEnumerable>(Value(result));

    private static Task<long> Company(Database db, string code) => db.InsertAsync(
        "INSERT INTO companies(company_code,name,industry) VALUES(@code,@code,'Logistics') RETURNING id",
        c => c.Parameters.AddWithValue("@code", code));
    private static Task<long> Customer(Database db, long company, string code, string name) => db.InsertAsync(
        "INSERT INTO customers(company_id,customer_code,name) VALUES(@company,@code,@name) RETURNING id",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@code", code); c.Parameters.AddWithValue("@name", name); });
    private static Task<long> Carrier(Database db, long company, string number, string name) => db.InsertAsync(
        "INSERT INTO carriers(company_id,carrier_number,name,status) VALUES(@company,@number,@name,'Active') RETURNING id",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@number", number); c.Parameters.AddWithValue("@name", name); });
    private static Task<long> Contract(Database db, long company, long customer, string code, string origin) => db.InsertAsync(
        @"INSERT INTO contracts(company_id,customer_id,contract_code,title,contract_number,rate_type,status,data_origin)
          VALUES(@company,@customer,@code,@code,@code,'Per Mile','Active',@origin) RETURNING id",
        c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@customer", customer); c.Parameters.AddWithValue("@code", code); c.Parameters.AddWithValue("@origin", origin); });

    private static async Task Cleanup(Database db, long company, long other)
    {
        foreach (var table in new[] { "audit_logs", "contract_versions", "rate_cards", "contract_rates", "contracts", "carriers", "customers" })
            await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@company OR company_id=@other",
                c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@other", other); });
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@company OR id=@other",
            c => { c.Parameters.AddWithValue("@company", company); c.Parameters.AddWithValue("@other", other); });
    }
}
