using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// Proves the pilot data-agreement requirement: "delete on request" removes EVERY row a
// real tenant owns, provably — not by a hand-maintained list (the demo-seeder cleanup bug)
// but schema-driven across all tenant-scoped tables. Real Postgres, real seeded rows.
//
// Seeds a realistic (non-demo) tenant INCLUDING the exact child tables the old demo
// cleanup omitted (driver_documents, vehicle_documents, user_sessions, customer_contacts,
// customer_addresses), deletes via TenantOffboardingService, then asserts:
//   - the company row is gone, and
//   - ZERO rows remain in every table carrying its company_id/tenant_id.
public class TenantOffboardingPostgresTests
{
    private static readonly string LocalConnectionString = TestDb.ConnectionString;

    [Fact]
    public async Task DeleteTenant_Removes_Every_Row_For_A_Real_Tenant()
    {
        var db = CreateDatabase();
        var svc = new TenantOffboardingService(db);
        var code = $"OFFBOARD-{Guid.NewGuid():N}".ToUpperInvariant()[..20];

        long companyId = await SeedRealisticTenantAsync(db, code);

        // Sanity: the tenant genuinely has rows across multiple tables before deletion.
        Assert.True(await CountAsync(db, "users", "company_id", companyId) >= 1);
        Assert.True(await CountAsync(db, "driver_documents", "company_id", companyId) >= 1);
        Assert.True(await CountAsync(db, "user_sessions", "company_id", companyId) >= 1);
        Assert.True(await CountAsync(db, "customer_addresses", "company_id", companyId) >= 1);
        Assert.Equal(1, await CountAsync(db, "hos_logs", "company_id", companyId));
        Assert.Equal(1, await CountAsync(db, "hos_certifications", "company_id", companyId));
        Assert.Equal(1, await CountAsync(db, "detention_evidence", "company_id", companyId));

        var result = await svc.DeleteTenantAsync(companyId);

        Assert.True(result.CompanyDeleted);
        Assert.Empty(result.TablesWithResidualRows);
        Assert.True(result.TotalRowsDeleted >= 6);

        // The company itself is gone.
        Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM companies WHERE id=@id",
            c => c.Parameters.AddWithValue("@id", companyId)));

        // ZERO residual rows in EVERY tenant-scoped table (schema-driven check).
        var residual = await ResidualTenantRowsAsync(db, companyId);
        Assert.True(residual.Count == 0,
            $"Residual rows remained after offboarding in: {string.Join(", ", residual)}");
    }

    [Fact]
    public async Task DeleteTenant_Only_Touches_The_Target_Tenant()
    {
        var db = CreateDatabase();
        var svc = new TenantOffboardingService(db);
        var keepCode = $"KEEP-{Guid.NewGuid():N}".ToUpperInvariant()[..16];
        var dropCode = $"DROP-{Guid.NewGuid():N}".ToUpperInvariant()[..16];

        var keepId = await SeedRealisticTenantAsync(db, keepCode);
        var dropId = await SeedRealisticTenantAsync(db, dropCode);

        try
        {
            await svc.DeleteTenantAsync(dropId);

            // Neighbour tenant is fully intact.
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM companies WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", keepId)));
            Assert.True(await CountAsync(db, "users", "company_id", keepId) >= 1);
            Assert.True(await CountAsync(db, "driver_documents", "company_id", keepId) >= 1);
            Assert.Empty(await ResidualTenantRowsAsync(db, dropId));
        }
        finally
        {
            await svc.DeleteTenantAsync(keepId); // cleanup the neighbour
        }
    }

    // ── Seed a realistic tenant, including the child tables the demo cleanup missed ──
    private static async Task<long> SeedRealisticTenantAsync(Database db, string code)
    {
        var companyId = await db.InsertAsync(
            "INSERT INTO companies (company_code, name, industry, status) VALUES (@code, @name, 'Logistics', 'Active') RETURNING id",
            c => { c.Parameters.AddWithValue("@code", code); c.Parameters.AddWithValue("@name", $"Offboard Test {code}"); });

        var userId = await db.InsertAsync(
            "INSERT INTO users (company_id, full_name, email, role_name, status) VALUES (@cid, 'Ops User', @email, 'Company Admin', 'Active') RETURNING id",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@email", $"user-{code}@example.com"); });

        await db.ExecuteAsync(
            "INSERT INTO user_sessions (user_id, company_id, session_token, expires_at) VALUES (@uid, @cid, @tok, NOW() + INTERVAL '1 day')",
            c => { c.Parameters.AddWithValue("@uid", userId); c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@tok", $"tok-{code}"); });

        var driverId = await db.InsertAsync(
            "INSERT INTO drivers (company_id, driver_code, full_name, status) VALUES (@cid, @dc, 'Test Driver', 'Active') RETURNING id",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@dc", $"DRV-{code}"); });

        await db.ExecuteAsync(
            @"INSERT INTO hos_logs
                (company_id,driver_id,log_date,driving_hours,on_duty_hours,cycle_hours_left,status,is_certified)
              VALUES(@cid,@did,CURRENT_DATE,1,1,69,'Driving',TRUE);
              INSERT INTO hos_certifications
                (company_id,driver_id,log_date,attestation_text,attestation_hash,certified_by,source_revision,source_snapshot)
              VALUES(@cid,@did,CURRENT_DATE,'Offboarding immutable-evidence test',repeat('a',64),@uid,repeat('b',64),'[]'::jsonb);",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@did", driverId);
                c.Parameters.AddWithValue("@uid", userId);
            });

        await db.ExecuteAsync(
            "INSERT INTO driver_documents (company_id, driver_id, document_type, document_name, status) VALUES (@cid, @did, 'License', 'CDL', 'Valid')",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@did", driverId); });

        var vehicleId = await db.InsertAsync(
            "INSERT INTO vehicles (company_id, vehicle_code, type, status) VALUES (@cid, @vc, 'Truck', 'Active') RETURNING id",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@vc", $"VEH-{code}"); });

        var dwellId = await db.InsertAsync(
            @"INSERT INTO detention_dwells(company_id,geofence_id,vehicle_id,entry_event_id,entered_at)
              VALUES(@cid,1,@vid,@entry,NOW()) RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@vid", vehicleId);
                c.Parameters.AddWithValue("@entry", companyId);
            });
        await db.ExecuteAsync(
            @"INSERT INTO detention_evidence
                (company_id,dwell_id,evidence_canonical,evidence_json,evidence_sha256)
              VALUES(@cid,@dwell,'offboarding immutable-evidence test','{}'::jsonb,repeat('c',64))",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@dwell", dwellId);
            });

        // Real dispatch state is mutually linked. This nullable FK cycle must not make
        // a schema-driven tenant reset impossible or force a partial deletion.
        await db.ExecuteAsync(
            @"UPDATE drivers SET assigned_vehicle_id=@vid WHERE id=@did AND company_id=@cid;
              UPDATE vehicles SET assigned_driver_id=@did WHERE id=@vid AND company_id=@cid;",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@did", driverId);
                c.Parameters.AddWithValue("@vid", vehicleId);
            });

        await db.ExecuteAsync(
            "INSERT INTO vehicle_documents (company_id, vehicle_id, document_type, document_name, status) VALUES (@cid, @vid, 'Registration', 'Reg', 'Valid')",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@vid", vehicleId); });

        var customerId = await db.InsertAsync(
            "INSERT INTO customers (company_id, customer_code, name, status) VALUES (@cid, @cc, 'Acme Corp', 'Active') RETURNING id",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@cc", $"CUS-{code}"); });

        await db.ExecuteAsync(
            "INSERT INTO customer_contacts (company_id, customer_id, full_name, is_primary) VALUES (@cid, @cust, 'Jane Doe', true)",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@cust", customerId); });

        await db.ExecuteAsync(
            "INSERT INTO customer_addresses (company_id, customer_id, address_type, address_line) VALUES (@cid, @cust, 'Billing', '123 Main St')",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@cust", customerId); });

        return companyId;
    }

    // Schema-driven: every base table with a company_id/tenant_id, mirroring the service.
    private static async Task<List<string>> ResidualTenantRowsAsync(Database db, long companyId)
    {
        var pairs = await db.QueryAsync(
            @"SELECT c.table_name, c.column_name
              FROM information_schema.columns c
              JOIN information_schema.tables t
                ON t.table_name = c.table_name AND t.table_schema = c.table_schema
              WHERE c.table_schema='public' AND t.table_type='BASE TABLE'
                AND c.column_name IN ('company_id','tenant_id')
                AND c.data_type='bigint'
                AND c.table_name <> 'companies'");
        var residual = new List<string>();
        foreach (var p in pairs)
        {
            var table = p["tableName"]!.ToString()!;
            var col = p["columnName"]!.ToString()!;
            var n = await db.ScalarLongAsync(
                $"SELECT COUNT(*) FROM \"{table}\" WHERE {col}=@cid",
                c => c.Parameters.AddWithValue("@cid", companyId));
            if (n > 0) residual.Add($"{table}.{col}={n}");
        }
        return residual;
    }

    private static Task<long> CountAsync(Database db, string table, string col, long companyId)
        => db.ScalarLongAsync($"SELECT COUNT(*) FROM \"{table}\" WHERE {col}=@cid",
            c => c.Parameters.AddWithValue("@cid", companyId));

    private static Database CreateDatabase()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = LocalConnectionString,
                ["ConnectionStrings:SystemConnection"] = TestDb.SystemConnectionString,
            })
            .Build();
        return new Database(config);
    }
}
