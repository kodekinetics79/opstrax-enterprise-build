using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;

namespace Opstrax.Tests;

// D-02 regression: typed document ownership, not numeric-ID coincidence, controls branch access.
// Real restricted app/system logins + signed DB tenant scope. HttpContext AuthItems are synthetic;
// these are not HTTP authentication, browser, upload/download, or field-certification tests.
[Collection("fleet-identity-schema")]
[Trait("Category", "Integration")]
public sealed class Module1DocumentBranchCollisionPostgresTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ComplianceDocuments_EqualVehicleAndDriverIds_DoNotConferOtherBranchAccess(bool branchA)
    {
        await using var f = await Fixture.Create();
        await f.Scope(f.CompanyA, async () =>
        {
            var branch = branchA ? f.BranchA : f.BranchB;
            long[] expected = branchA ? [f.OwnVehicleDoc, f.CollisionDriverDoc, f.AssetDoc] : [f.CollisionVehicleDoc];
            AssertRows(f, expected, await Compliance(f, f.CompanyA, branch));
            AssertRows(f, expected, await Canonical(f, f.CompanyA, branch));
        });
    }

    [Fact]
    public async Task ComplianceDocuments_TenantWide_HasUniqueDocumentsAndCorrectTypedNames()
    {
        await using var f = await Fixture.Create();
        await f.Scope(f.CompanyA, async () =>
        {
            // Nondeleted documents remain tenant-visible even if their master is deleted or
            // unassigned; unknown types use owner_name. No new asset lifecycle rule is introduced.
            AssertRows(f, f.ActiveA, await Compliance(f, f.CompanyA));
            AssertRows(f, f.ActiveA, await Canonical(f, f.CompanyA));
        });
    }

    [Fact]
    public async Task ComplianceDocuments_SignedTenantIsolation_MismatchedHttpTenantAndNoScopeFailClosed()
    {
        await using var f = await Fixture.Create();
        await f.Scope(f.CompanyA, async () =>
        {
            // Unfiltered SQL is an independent RLS oracle, not another copy of handler predicates.
            var rows = await f.Runtime.QueryAsync("SELECT id FROM documents ORDER BY id");
            Assert.Equal(f.AllA.Order(), rows.Select(x => Convert.ToInt64(x["id"])).Order());
            AssertRows(f, [], await Compliance(f, f.CompanyB));
            AssertRows(f, [], await Canonical(f, f.CompanyB));
            await AssertDetail(f, f.CompanyA, f.BranchA, f.ForeignDoc, 404);
        });
        await f.Scope(f.CompanyB, async () =>
        {
            AssertRows(f, [f.ForeignDoc], await Compliance(f, f.CompanyB, f.ForeignBranch));
            AssertRows(f, [f.ForeignDoc], await Canonical(f, f.CompanyB, f.ForeignBranch));
            AssertRows(f, [], await Compliance(f, f.CompanyA));
            AssertRows(f, [], await Canonical(f, f.CompanyA));
        });
        var outside = await f.Runtime.QuerySingleAsync("SELECT current_user AS role,opstrax_security.current_tenant_id() AS tenant");
        Assert.Equal("opstrax_app", outside!["role"]?.ToString());
        Assert.True(outside["tenant"] is null or DBNull);
        Assert.Empty(await f.Runtime.QueryAsync("SELECT id FROM documents"));
        AssertRows(f, [], await Compliance(f, f.CompanyA));
        AssertRows(f, [], await Canonical(f, f.CompanyA));
    }

    [Fact]
    public async Task ComplianceDocuments_MissingPermissionDenied_EvenInsideValidSignedScope()
    {
        await using var f = await Fixture.Create();
        await f.Scope(f.CompanyA, async () =>
        {
            Assert.Equal(403, Status(await EndpointMappings.ComplianceDocuments(
                Principal(f.CompanyA, f.BranchA, allowed: false), f.Runtime, CancellationToken.None)));
            Assert.Equal(403, Status(await Invoke("Documents",
                Principal(f.CompanyA, f.BranchA, allowed: false), f.Runtime, CancellationToken.None)));
        });
    }

    [Fact]
    public async Task CanonicalDetail_ValidatesActualTypedMasterAndDocumentLifecycle()
    {
        await using var f = await Fixture.Create();
        await f.Scope(f.CompanyA, async () =>
        {
            await AssertDetail(f, f.CompanyA, f.BranchA, f.OwnVehicleDoc, 200);
            await AssertDetail(f, f.CompanyA, f.BranchA, f.CollisionDriverDoc, 200);
            await AssertDetail(f, f.CompanyA, f.BranchB, f.CollisionVehicleDoc, 200);
            await AssertDetail(f, f.CompanyA, f.BranchA, f.AssetDoc, 200);
            await AssertDetail(f, f.CompanyA, f.BranchA, f.CollisionVehicleDoc, 404);
            await AssertDetail(f, f.CompanyA, f.BranchB, f.CollisionDriverDoc, 404);
            foreach (var id in new[] { f.UnknownTypeDoc, f.CustomerDoc, f.NullBranchDoc,
                f.DeletedMasterDoc, f.DeletedDriverDoc, f.ArchivedDoc, f.ForeignDoc })
                await AssertDetail(f, f.CompanyA, f.BranchA, id, 404);
        });
    }

    [Fact]
    public async Task TargetedDocumentRelations_KeepForceRlsAndRestrictedReadGrants()
    {
        await using var f = await Fixture.Create();
        var tables = new[] { "documents", "drivers", "vehicles", "fleet_tms_assets", "customers",
            "document_timeline_events", "audit_logs", "ai_recommendations" };
        var rows = await f.Runtime.QueryAsync(@"SELECT c.relname,c.relrowsecurity,c.relforcerowsecurity,
            pg_get_userbyid(c.relowner) AS owner,has_table_privilege(current_user,c.oid,'SELECT') AS can_select
            FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE n.nspname='public' AND c.relname=ANY(@tables)", c => c.Parameters.AddWithValue("tables", tables));
        Assert.Equal(tables.Length, rows.Count);
        foreach (var row in rows)
        {
            Assert.True(Convert.ToBoolean(row["relrowsecurity"]));
            Assert.True(Convert.ToBoolean(row["relforcerowsecurity"]));
            Assert.True(Convert.ToBoolean(row["canSelect"]));
            Assert.DoesNotContain(row["owner"]?.ToString(), new[] { "opstrax_app", "opstrax_system" });
        }
    }

    private static async Task<JsonElement[]> Compliance(Fixture f, long company, long? branch = null)
        => Rows(await EndpointMappings.ComplianceDocuments(Principal(company, branch), f.Runtime, CancellationToken.None));

    private static async Task<JsonElement[]> Canonical(Fixture f, long company, long? branch = null)
        => Rows(await Invoke("Documents", Principal(company, branch), f.Runtime, CancellationToken.None));

    private static JsonElement[] Rows(IResult result)
    {
        Assert.Equal(200, Status(result));
        return Value(result).GetProperty("data").EnumerateArray().Select(x => x.Clone()).ToArray();
    }

    private static void AssertRows(Fixture f, long[] expected, JsonElement[] rows)
    {
        var actual = rows.Select(x => x.GetProperty("id").GetInt64()).ToArray();
        Assert.Equal(expected.Order(), actual.Order());
        Assert.Equal(actual.Length, actual.Distinct().Count());
        foreach (var row in rows)
            Assert.Equal(f.Names[row.GetProperty("id").GetInt64()], row.GetProperty("entityName").GetString());
    }

    private static async Task AssertDetail(Fixture f, long company, long? branch, long id, int expected)
    {
        var result = await Invoke("DocumentDetail", Principal(company, branch), id, f.Runtime, CancellationToken.None);
        Assert.Equal(expected, Status(result));
        if (expected == 200)
        {
            var record = Value(result).GetProperty("data").GetProperty("record");
            Assert.Equal(id, record.GetProperty("id").GetInt64());
            Assert.Equal(f.Names[id], record.GetProperty("entityName").GetString());
        }
    }

    private static DefaultHttpContext Principal(long company, long? branch = null, bool allowed = true)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = company;
        http.Items[EndpointMappings.AuthUserIdItemKey] = 0L;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Controlled branch reader";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = allowed ? new[] { "compliance:view" } : Array.Empty<string>();
        if (branch.HasValue) http.Items[EndpointMappings.AuthBranchIdItemKey] = branch.Value;
        return http;
    }

    private static async Task<IResult> Invoke(string name, params object[] args)
    {
        try { return await (Task<IResult>)typeof(EndpointMappings).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args)!; }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }

    private static int Status(IResult result) => ((IStatusCodeHttpResult)result).StatusCode ?? 200;
    private static JsonElement Value(IResult result) => JsonSerializer.SerializeToElement(((IValueHttpResult)result).Value, JsonOptions);
    private static void Require(bool condition, string message) => Assert.True(condition, message);

    private sealed class Fixture(string owner, Database runtime) : IAsyncDisposable
    {
        public Database Runtime { get; } = runtime;
        public Dictionary<long, string> Names { get; } = [];
        public long CompanyA, CompanyB, BranchA, BranchB, ForeignBranch, CollisionId;
        public long OwnVehicleDoc, CollisionVehicleDoc, CollisionDriverDoc, UnknownTypeDoc, NullBranchDoc, DeletedMasterDoc, DeletedDriverDoc, AssetDoc, CustomerDoc, ArchivedDoc, ForeignDoc;
        public long[] ActiveA => [OwnVehicleDoc, CollisionVehicleDoc, CollisionDriverDoc, UnknownTypeDoc, NullBranchDoc, DeletedMasterDoc, DeletedDriverDoc, AssetDoc, CustomerDoc];
        public long[] AllA => [.. ActiveA, ArchivedDoc];
        private readonly string _prefix = "W1DOC-" + Guid.NewGuid().ToString("N");
        private readonly List<long> _companies = [];

        public static async Task<Fixture> Create()
        {
            // No historical TestDb fallback and no remote or mismatched database targets.
            Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPSTRAX_TEST_DB")),
                "Explicit disposable local OPSTRAX_TEST_DB is required.");
            var owner = new NpgsqlConnectionStringBuilder(TestDb.ConnectionString);
            var app = new NpgsqlConnectionStringBuilder(TestDb.AppConnectionString);
            var system = new NpgsqlConnectionStringBuilder(TestDb.SystemConnectionString);
            foreach (var connection in new[] { owner, app, system })
            {
                Assert.Contains(connection.Host, new[] { "127.0.0.1", "localhost", "::1" });
                Assert.Equal(owner.Host, connection.Host);
                Assert.Equal(owner.Port, connection.Port);
                Assert.Equal(owner.Database, connection.Database);
            }
            Assert.Equal("opstrax_app", app.Username);
            Assert.Equal("opstrax_system", system.Username);
            Assert.DoesNotContain(owner.Username, new[] { "opstrax_app", "opstrax_system" });
            Assert.True(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PG_CONNECTION_REPLICA")),
                "No inherited replica is permitted for these disposable tests.");
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Staging",
                ["Rls:EnforceTenantContext"] = "true",
                ["ConnectionStrings:DefaultConnection"] = app.ConnectionString,
                ["ConnectionStrings:SystemConnection"] = system.ConnectionString
            }).Build();
            var runtime = new Database(config, new TenantScopeAccessor());
            await runtime.ValidateProductionIdentitiesAsync();
            var fixture = new Fixture(owner.ConnectionString, runtime);
            try { await fixture.Initialize(); return fixture; }
            catch { await fixture.DisposeAsync(); throw; }
        }

        public async Task Scope(long company, Func<Task> action)
            => await Runtime.RunInTenantScopeAsync(company, async () =>
            {
                var row = await Runtime.QuerySingleAsync("SELECT current_user AS role,session_user AS login,opstrax_security.current_tenant_id() AS tenant");
                Assert.Equal("opstrax_app", row!["role"]?.ToString());
                Assert.Equal("opstrax_app", row["login"]?.ToString());
                Assert.Equal(company, Convert.ToInt64(row["tenant"]));
                await action();
                return true;
            });

        private async Task Initialize()
        {
            await using var conn = new NpgsqlConnection(owner); await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            async Task<long> Insert(string sql, params (string, object?)[] parameters)
            {
                await using var command = new NpgsqlCommand(sql + " RETURNING id", conn, tx);
                foreach (var (key, value) in parameters) command.Parameters.AddWithValue(key, value ?? DBNull.Value);
                return Convert.ToInt64(await command.ExecuteScalarAsync());
            }
            async Task<long> Company(string suffix)
            {
                var id = await Insert("INSERT INTO companies(company_code,name,industry) VALUES (@code,'Synthetic document scope fixture','Transportation')", ("code", _prefix + suffix));
                _companies.Add(id); return id;
            }
            async Task<long> Branch(long company, string code) => await Insert("INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,@code,@code,'Active')", ("c", company), ("code", code));
            async Task<long> Vehicle(long company, long? branch, string code, long? explicitId = null, bool deleted = false)
                => await Insert($"INSERT INTO vehicles({(explicitId.HasValue ? "id," : "")}company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status,deleted_at) {(explicitId.HasValue ? "OVERRIDING SYSTEM VALUE" : "")} VALUES ({(explicitId.HasValue ? "@id," : "")}@c,@b,@code,'Truck','legacy-fleet-identifier',@identity,'Available',CASE WHEN @deleted THEN NOW() ELSE NULL END)",
                    ("id", explicitId), ("c", company), ("b", branch), ("code", code), ("identity", _prefix + "-" + code), ("deleted", deleted));
            async Task<long> Document(long company, string type, long entityId, string title, bool deleted = false)
                => await Insert("INSERT INTO documents(company_id,title,document_type,owner_name,status,entity_type,entity_id,expires_at,deleted_at) VALUES (@c,@title,'Synthetic fixture','Fixture owner','Active',@type,@entity,CURRENT_DATE+30,CASE WHEN @deleted THEN NOW() ELSE NULL END)",
                    ("c", company), ("title", title), ("type", type), ("entity", entityId), ("deleted", deleted));
            CompanyA = await Company("A"); CompanyB = await Company("B");
            BranchA = await Branch(CompanyA, "A"); BranchB = await Branch(CompanyA, "B"); ForeignBranch = await Branch(CompanyB, "F");
            CollisionId = 9_000_000_000L + RandomNumberGenerator.GetInt32(1_000_000);
            await using (var unused = new NpgsqlCommand("SELECT (SELECT count(*) FROM vehicles WHERE id=@id)+(SELECT count(*) FROM drivers WHERE id=@id)", conn, tx))
            { unused.Parameters.AddWithValue("id", CollisionId); Require(Convert.ToInt64(await unused.ExecuteScalarAsync()) == 0, "collision ID unused in both master tables"); }
            await Vehicle(CompanyA, BranchB, "COLLISION-VEHICLE", CollisionId);
            _ = await Insert("INSERT INTO drivers(id,company_id,branch_id,driver_code,full_name,status) OVERRIDING SYSTEM VALUE VALUES (@id,@c,@b,'COLLISION-DRIVER','Synthetic collision driver A','Available')",
                ("id", CollisionId), ("c", CompanyA), ("b", BranchA));
            var own = await Vehicle(CompanyA, BranchA, "OWN-A");
            var noBranch = await Vehicle(CompanyA, null, "NULL-BRANCH");
            var deletedMaster = await Vehicle(CompanyA, BranchA, "DELETED-MASTER", deleted: true);
            var deletedDriver = await Insert("INSERT INTO drivers(company_id,branch_id,driver_code,full_name,status,deleted_at) VALUES (@c,@b,'DELETED-DRIVER','Synthetic deleted driver','Available',NOW())",
                ("c", CompanyA), ("b", BranchA));
            var assetType = await Insert("INSERT INTO fleet_tms_asset_types(company_id,code,name) VALUES (@c,'W1DOC','Synthetic document asset type')", ("c", CompanyA));
            var asset = await Insert("INSERT INTO fleet_tms_assets(company_id,branch_id,asset_type_id,asset_tag,name) VALUES (@c,@b,@type,'W1DOC-ASSET','Synthetic owned asset')",
                ("c", CompanyA), ("b", BranchA), ("type", assetType));
            _ = await Insert("INSERT INTO customers(id,company_id,customer_code,name) OVERRIDING SYSTEM VALUE VALUES (@id,@c,'W1DOC-CUSTOMER','Synthetic collided customer')",
                ("id", CollisionId), ("c", CompanyA));
            var foreign = await Vehicle(CompanyB, ForeignBranch, "FOREIGN");
            OwnVehicleDoc = await Document(CompanyA, "vehicle", own, "Own branch A vehicle document");
            CollisionVehicleDoc = await Document(CompanyA, "vehicle", CollisionId, "Branch B vehicle document must not leak to A");
            CollisionDriverDoc = await Document(CompanyA, "driver", CollisionId, "Branch A driver document must not leak to B");
            UnknownTypeDoc = await Document(CompanyA, "unknown-synthetic", CollisionId, "Unknown type must not inherit collided master branch");
            NullBranchDoc = await Document(CompanyA, "vehicle", noBranch, "Unassigned branch document");
            DeletedMasterDoc = await Document(CompanyA, "vehicle", deletedMaster, "Deleted master document");
            DeletedDriverDoc = await Document(CompanyA, "driver", deletedDriver, "Deleted driver document");
            AssetDoc = await Document(CompanyA, "asset", asset, "Owned branch asset document");
            CustomerDoc = await Document(CompanyA, "customer", CollisionId, "Customer document must not inherit collided master branch");
            ArchivedDoc = await Document(CompanyA, "vehicle", own, "Archived document", deleted: true);
            ForeignDoc = await Document(CompanyB, "vehicle", foreign, "Other company document");
            foreach (var (id, name) in new[] { (OwnVehicleDoc, "OWN-A"), (CollisionVehicleDoc, "COLLISION-VEHICLE"),
                (CollisionDriverDoc, "Synthetic collision driver A"), (UnknownTypeDoc, "Fixture owner"),
                (NullBranchDoc, "NULL-BRANCH"), (DeletedMasterDoc, "DELETED-MASTER"), (DeletedDriverDoc, "Synthetic deleted driver"),
                (AssetDoc, "Synthetic owned asset"), (CustomerDoc, "Synthetic collided customer"), (ArchivedDoc, "OWN-A"), (ForeignDoc, "FOREIGN") })
                Names.Add(id, name);
            await tx.CommitAsync();

        }

        public async ValueTask DisposeAsync()
        {
            if (_companies.Count == 0) return;
            await using var conn = new NpgsqlConnection(owner); await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            foreach (var table in new[] { "document_timeline_events", "audit_logs", "documents", "fleet_tms_assets", "fleet_tms_asset_types", "customers", "drivers", "vehicles", "branches" })
            foreach (var company in _companies)
            {
                await using var command = new NpgsqlCommand($"DELETE FROM {table} WHERE company_id=@c AND EXISTS (SELECT 1 FROM companies WHERE id=@c AND company_code LIKE @prefix)", conn, tx);
                command.Parameters.AddWithValue("c", company); command.Parameters.AddWithValue("prefix", _prefix + "%"); await command.ExecuteNonQueryAsync();
            }
            await using (var remove = new NpgsqlCommand("DELETE FROM companies WHERE id=ANY(@ids) AND company_code LIKE @prefix", conn, tx))
            { remove.Parameters.AddWithValue("ids", _companies.ToArray()); remove.Parameters.AddWithValue("prefix", _prefix + "%"); await remove.ExecuteNonQueryAsync(); }
            await using (var verify = new NpgsqlCommand("SELECT count(*) FROM companies WHERE id=ANY(@ids) AND company_code LIKE @prefix", conn, tx))
            { verify.Parameters.AddWithValue("ids", _companies.ToArray()); verify.Parameters.AddWithValue("prefix", _prefix + "%"); Require(Convert.ToInt64(await verify.ExecuteScalarAsync()) == 0, "scoped fixture cleanup"); }
            await tx.CommitAsync();
        }
    }
}

