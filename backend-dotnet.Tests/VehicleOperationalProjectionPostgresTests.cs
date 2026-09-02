using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;

namespace Opstrax.Tests;

// Supporting handler/SQL tests only: owner connection, not independent FORCE-RLS proof.
// Run exclusively against an explicitly selected disposable local PostgreSQL database.
[Trait("Category", "Integration")]
public sealed class VehicleOperationalProjectionPostgresTests
{
    [Theory]
    [InlineData("Maintenance", 0, "none", "High", false)]
    [InlineData("Maintenance", 10, "none", "High", false)]
    [InlineData("Delayed", 0, "none", "High", false)]
    [InlineData("Available", 70, "fresh", "High", true)]
    [InlineData("Available", 69, "fresh", "Medium", true)]
    [InlineData("Available", 40, "fresh", "Medium", true)]
    [InlineData("Available", 39, "fresh", "Low", true)]
    [InlineData("Available", 0, "none", "Medium", false)]
    [InlineData("Available", 0, "missing-heartbeat", "Medium", true)]
    [InlineData("Available", 0, "stale", "Medium", true)]
    [InlineData("Available", 0, "fresh", "Low", true)]
    [InlineData("Available", 0, "camera-only", "Medium", true)]
    [InlineData("Available", 0, "ended", "Medium", false)]
    [InlineData("Available", 0, "failed", "Medium", false)]
    [InlineData("Available", 0, "other-role", "Medium", false)]
    public async Task ActualListAndDetailPreserveOperationalPolicy(
        string status, int risk, string installation, string expectedRisk, bool hasReadiness)
    {
        await using var fixture = await Fixture.Create();
        var vehicle = await fixture.Vehicle(fixture.BranchA, "PARITY", status, risk);
        if (installation != "none") await fixture.Install(vehicle, installation);

        var http = Principal(fixture.Company, fixture.BranchA);
        using var list = Payload(await Invoke("Vehicles", http, fixture.Db, CancellationToken.None), 200);
        var row = Assert.Single(list.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal("1", http.Response.Headers["X-Total-Count"].ToString());
        Assert.Equal(vehicle, row.GetProperty("id").GetInt64());
        using var detail = Payload(await Invoke("VehicleDetail", Principal(fixture.Company, fixture.BranchA),
            vehicle, fixture.Db, CancellationToken.None), 200);
        var record = detail.RootElement.GetProperty("data").GetProperty("record");
        Assert.Equal(vehicle, record.GetProperty("id").GetInt64());

        foreach (var actual in new[] { row, record })
        {
            Assert.Equal(expectedRisk, actual.GetProperty("riskHeatScore").GetString());
            var readiness = actual.GetProperty("fleetReadinessScore");
            if (hasReadiness)
                Assert.Equal(decimal.Round((80m + 60m + 100m - risk) / 3m, 1,
                    MidpointRounding.AwayFromZero), readiness.GetDecimal());
            else Assert.Equal(JsonValueKind.Null, readiness.ValueKind);
        }
        Assert.Equal(row.GetProperty("riskHeatScore").GetRawText(), record.GetProperty("riskHeatScore").GetRawText());
        Assert.Equal(row.GetProperty("fleetReadinessScore").GetRawText(), record.GetProperty("fleetReadinessScore").GetRawText());
    }

    [Fact]
    public async Task ActualHandlersRetainTenantBranchAndLifecycleBoundaries()
    {
        await using var own = await Fixture.Create();
        await using var other = await Fixture.Create();
        var visible = await own.Vehicle(own.BranchA, "OWN");
        var wrongBranch = await own.Vehicle(own.BranchB, "OTHER-BRANCH");
        var nullBranch = await own.Vehicle(null, "NO-BRANCH");
        var archived = await own.Vehicle(own.BranchA, "ARCHIVED", archived: true);
        var foreign = await other.Vehicle(other.BranchA, "FOREIGN");

        using var active = Payload(await Invoke("Vehicles", Principal(own.Company, own.BranchA), own.Db, CancellationToken.None), 200);
        Assert.Equal(visible, Assert.Single(active.RootElement.GetProperty("data").EnumerateArray()).GetProperty("id").GetInt64());
        foreach (var excluded in new[] { wrongBranch, nullBranch, archived, foreign })
            using (Payload(await Invoke("VehicleDetail", Principal(own.Company, own.BranchA), excluded, own.Db, CancellationToken.None), 404)) { }

        using var archiveList = Payload(await Invoke("Vehicles", Principal(own.Company, own.BranchA, "archived"), own.Db, CancellationToken.None), 200);
        var archivedRow = Assert.Single(archiveList.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(archived, archivedRow.GetProperty("id").GetInt64());
        using var archiveDetail = Payload(await Invoke("VehicleDetail", Principal(own.Company, own.BranchA, "archived"), archived, own.Db, CancellationToken.None), 200);
        var record = archiveDetail.RootElement.GetProperty("data").GetProperty("record");
        Assert.Equal("Archived", record.GetProperty("lifecycleStatus").GetString());
        Assert.Equal("Medium", record.GetProperty("riskHeatScore").GetString());
        Assert.Equal(JsonValueKind.Null, record.GetProperty("fleetReadinessScore").ValueKind);
        Assert.Equal(archivedRow.GetProperty("riskHeatScore").GetString(), record.GetProperty("riskHeatScore").GetString());
        using (Payload(await Invoke("VehicleDetail", Principal(own.Company, own.BranchA, "archived"), visible, own.Db, CancellationToken.None), 404)) { }

        using var tenantList = Payload(await Invoke("Vehicles", Principal(own.Company), own.Db, CancellationToken.None), 200);
        Assert.Equal(new[] { visible, wrongBranch, nullBranch }.Order(), tenantList.RootElement.GetProperty("data")
            .EnumerateArray().Select(row => row.GetProperty("id").GetInt64()).Order());
    }

    [Fact]
    public async Task ActualHandlersRejectMissingPermissionAndInvalidLifecycle()
    {
        await using var fixture = await Fixture.Create();
        var id = await fixture.Vehicle(fixture.BranchA, "DENIAL");
        using (Payload(await Invoke("Vehicles", Principal(fixture.Company, allowed: false), fixture.Db, CancellationToken.None), 403)) { }
        using (Payload(await Invoke("VehicleDetail", Principal(fixture.Company, allowed: false), id, fixture.Db, CancellationToken.None), 403)) { }
        using (Payload(await Invoke("Vehicles", Principal(fixture.Company, lifecycle: "all"), fixture.Db, CancellationToken.None), 400)) { }
        using (Payload(await Invoke("VehicleDetail", Principal(fixture.Company, lifecycle: "all"), id, fixture.Db, CancellationToken.None), 400)) { }
    }

    private static DefaultHttpContext Principal(long company, long? branch = null, string lifecycle = "active", bool allowed = true)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = company;
        http.Items[EndpointMappings.AuthUserIdItemKey] = 0L;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Company Admin";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = allowed ? new[] { "vehicles:view" } : Array.Empty<string>();
        if (branch.HasValue) http.Items[EndpointMappings.AuthBranchIdItemKey] = branch.Value;
        http.Request.QueryString = new QueryString($"?lifecycle={lifecycle}&limit=50");
        return http;
    }

    private static JsonDocument Payload(IResult result, int status)
    {
        Assert.Equal(status, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        return JsonDocument.Parse(JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(result).Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static async Task<IResult> Invoke(string name, params object[] args)
    {
        var method = typeof(EndpointMappings).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        try { return await (Task<IResult>)method.Invoke(null, args)!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required Database Db { get; init; }
        public long Company { get; private set; }
        public long BranchA { get; private set; }
        public long BranchB { get; private set; }

        public static async Task<Fixture> Create()
        {
            // Refuse the historical TestDb fallback and all remote hosts. The caller must
            // additionally ensure this explicit local target is a disposable test database.
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPSTRAX_TEST_DB")))
                throw new InvalidOperationException("Set OPSTRAX_TEST_DB explicitly to a disposable local PostgreSQL database.");
            var connection = new NpgsqlConnectionStringBuilder(TestDb.ConnectionString);
            if (connection.Host is not ("127.0.0.1" or "localhost" or "::1"))
                throw new InvalidOperationException("Operational projection tests refuse remote database hosts.");
            var fixture = new Fixture { Db = new Database(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
                    ["Rls:EnforceTenantContext"] = "false" }).Build()) };
            try
            {
                fixture.Company = await fixture.Db.InsertAsync(
                    "INSERT INTO companies(company_code,name,industry) VALUES (@code,'Projection test','Transportation')",
                    c => c.Parameters.AddWithValue("@code", $"VOP-{Guid.NewGuid():N}"));
                fixture.BranchA = await fixture.Branch("A");
                fixture.BranchB = await fixture.Branch("B");
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        private Task<long> Branch(string code) => Db.InsertAsync(
            "INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,@code,@code,'Active')",
            c => { c.Parameters.AddWithValue("@c", Company); c.Parameters.AddWithValue("@code", code); });

        public Task<long> Vehicle(long? branch, string code, string status = "Available", int risk = 0, bool archived = false) => Db.InsertAsync(
            @"INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,
                     status,risk_score,readiness_score,data_quality_score,deleted_at)
              VALUES (@c,@b,@code,'Truck','legacy-fleet-identifier',@alternate,@status,@risk,80,60,
                      CASE WHEN @archived THEN NOW() ELSE NULL END)",
            c => { c.Parameters.AddWithValue("@c", Company); c.Parameters.AddWithValue("@b", (object?)branch ?? DBNull.Value);
                c.Parameters.AddWithValue("@code", code); c.Parameters.AddWithValue("@alternate", $"VOP-{code}-IDENTITY");
                c.Parameters.AddWithValue("@status", status); c.Parameters.AddWithValue("@risk", risk);
                c.Parameters.AddWithValue("@archived", archived); });

        public async Task Install(long vehicle, string kind)
        {
            var serial = $"VOP-{Guid.NewGuid():N}";
            var device = await Db.InsertAsync(
                @"INSERT INTO eld_devices(company_id,device_serial,status,device_state,api_key_hash,hmac_secret_encrypted,hmac_key_version,last_seen_at)
                  VALUES (@c,@serial,'Active','Registered',encode(sha256(@serial::bytea),'hex'),repeat('b',32),1,
                    CASE WHEN @kind='missing-heartbeat' THEN NULL WHEN @kind='stale' THEN NOW()-INTERVAL '30 minutes' ELSE NOW()-INTERVAL '1 minute' END)",
                c => { c.Parameters.AddWithValue("@c", Company); c.Parameters.AddWithValue("@serial", serial); c.Parameters.AddWithValue("@kind", kind); });
            await Db.ExecuteAsync(
                @"INSERT INTO device_installations(company_id,branch_id,device_id,vehicle_id,status,device_role,is_primary,
                    effective_from,effective_to,installed_at,removed_at,source)
                  VALUES (@c,@b,@d,@v,@status,@role,TRUE,NOW()-INTERVAL '2 hours',
                    CASE WHEN @ended THEN NOW()-INTERVAL '1 hour' ELSE NULL END,NOW()-INTERVAL '2 hours',
                    CASE WHEN @ended THEN NOW()-INTERVAL '1 hour' ELSE NULL END,'test')",
                c => { c.Parameters.AddWithValue("@c", Company); c.Parameters.AddWithValue("@b", BranchA);
                    c.Parameters.AddWithValue("@d", device); c.Parameters.AddWithValue("@v", vehicle);
                    c.Parameters.AddWithValue("@status", kind == "ended" ? "Removed" : kind == "failed" ? "Failed" : "Installed");
                    c.Parameters.AddWithValue("@role", kind == "camera-only" ? "Dashcam" : kind == "other-role" ? "Temperature" : "GPS");
                    c.Parameters.AddWithValue("@ended", kind == "ended"); });
        }

        public async ValueTask DisposeAsync()
        {
            if (Company == 0) return;
            foreach (var table in new[] { "audit_logs", "device_state_transitions", "device_installation_evidence", "device_installations", "eld_devices", "vehicles", "branches" })
                await Db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", Company));
            await Db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", Company));
        }
    }
}
