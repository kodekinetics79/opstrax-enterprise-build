using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// Actual registered endpoint delegates with signed tenant DB scope. AuthItems are
// synthetic; this exercises persistence and authorization predicates, not HTTP login.
[Collection("fleet-identity-schema")]
[Trait("Category", "Integration")]
public sealed class FleetMasterAssignmentHosPostgresTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task MasterPair_WithMissingOrStaleHos_PersistsSymmetricHistoryButDispatchRemainsBlocked(bool fromVehicle, bool stale)
    {
        await using var f = await Fixture.Create();
        var pair = f.Pairs["A"];
        if (stale) await f.StaleHos(pair.Driver);
        Assert.Equal(200, Status(await f.Pair(fromVehicle, pair.Driver, pair.Vehicle)));
        await f.AssertPair(pair.Driver, pair.Vehicle);
        var history = Assert.Single(await f.History());
        Assert.Equal(pair.Driver, history.GetProperty("driver_id").GetInt64());
        Assert.Equal(pair.Vehicle, history.GetProperty("vehicle_id").GetInt64());
        Assert.Equal(f.BranchA, history.GetProperty("branch_id").GetInt64());
        Assert.Equal("Active", history.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, history.GetProperty("released_at").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, history.GetProperty("assigned_at").ValueKind);

        // Saving the same pair again must not fabricate another history interval.
        Assert.Equal(200, Status(await f.Pair(fromVehicle, pair.Driver, pair.Vehicle)));
        Assert.Single(await f.History());
        var before = await f.Snapshot();
        var dispatch = await f.Dispatch(pair.Driver, pair.Vehicle);
        Assert.Equal(422, Status(dispatch));
        Assert.Contains("Authoritative HOS clock unavailable or stale", JsonSerializer.Serialize(((IValueHttpResult)dispatch).Value));
        Assert.Equal(before, await f.Snapshot());
        Assert.Equal(0, await f.CountDispatch());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReplacingMasterPairWithoutHos_ClearsBothDisplacedLinksAndReleasesHistory(bool fromVehicle)
    {
        await using var f = await Fixture.Create();
        var first = f.Pairs["A"]; var second = f.Pairs["A2"];
        Assert.Equal(200, Status(await f.Pair(fromVehicle, first.Driver, first.Vehicle)));
        Assert.Equal(200, Status(await f.Pair(fromVehicle, second.Driver, second.Vehicle)));
        Assert.Equal(200, Status(await f.Pair(fromVehicle, second.Driver, first.Vehicle)));
        await f.AssertPair(second.Driver, first.Vehicle);
        Assert.Equal(JsonValueKind.Null, (await f.Row("drivers", first.Driver)).GetProperty("assigned_vehicle_id").ValueKind);
        Assert.Equal(JsonValueKind.Null, (await f.Row("vehicles", second.Vehicle)).GetProperty("assigned_driver_id").ValueKind);
        var history = await f.History();
        Assert.Equal(3, history.Length);
        Assert.Equal(2, history.Count(row => row.GetProperty("status").GetString() == "Released" && row.GetProperty("released_at").ValueKind != JsonValueKind.Null));
        Assert.Single(history, row => row.GetProperty("status").GetString() == "Active");
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    public async Task ReplacingOneSidedLegacyPairs_ClearsInverseLinksAndPreservesHistory(bool fromVehicle, bool breakSource, bool breakTarget)
    {
        await using var f = await Fixture.Create();
        var first = f.Pairs["A"]; var second = f.Pairs["A2"];
        Assert.Equal(200, Status(await f.Pair(fromVehicle, first.Driver, first.Vehicle)));
        Assert.Equal(200, Status(await f.Pair(fromVehicle, second.Driver, second.Vehicle)));
        if (breakSource)
            await f.Link(fromVehicle ? "vehicles" : "drivers", fromVehicle ? first.Vehicle : first.Driver, null);
        if (breakTarget)
            await f.Link(fromVehicle ? "drivers" : "vehicles", fromVehicle ? second.Driver : second.Vehicle, null);
        var driver = fromVehicle ? second.Driver : first.Driver;
        var vehicle = fromVehicle ? first.Vehicle : second.Vehicle;
        Assert.Equal(200, Status(await f.Pair(fromVehicle, driver, vehicle)));
        await f.AssertPair(driver, vehicle);
        Assert.Equal(JsonValueKind.Null, (await f.Row("drivers", fromVehicle ? first.Driver : second.Driver)).GetProperty("assigned_vehicle_id").ValueKind);
        Assert.Equal(JsonValueKind.Null, (await f.Row("vehicles", fromVehicle ? second.Vehicle : first.Vehicle)).GetProperty("assigned_driver_id").ValueKind);
        var history = await f.History();
        Assert.Equal(3, history.Length);
        Assert.Equal(2, history.Count(row => row.GetProperty("status").GetString() == "Released" && row.GetProperty("released_at").ValueKind != JsonValueKind.Null));
        Assert.Single(history, row => row.GetProperty("status").GetString() == "Active");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnassigningOneSidedLegacyPair_ReleasesInverseLinkAndHistory(bool fromVehicle)
    {
        await using var f = await Fixture.Create();
        var pair = f.Pairs["A"];
        Assert.Equal(200, Status(await f.Pair(fromVehicle, pair.Driver, pair.Vehicle)));
        await f.Link(fromVehicle ? "vehicles" : "drivers", fromVehicle ? pair.Vehicle : pair.Driver, null);
        Assert.Equal(200, Status(await f.PairBody(fromVehicle, pair.Driver, pair.Vehicle, new() { ["targetId"] = null })));
        Assert.Equal(JsonValueKind.Null, (await f.Row("drivers", pair.Driver)).GetProperty("assigned_vehicle_id").ValueKind);
        Assert.Equal(JsonValueKind.Null, (await f.Row("vehicles", pair.Vehicle)).GetProperty("assigned_driver_id").ValueKind);
        Assert.Equal("Released", Assert.Single(await f.History()).GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OneSidedCrossBranchInverseLink_ReturnsConflictWithoutWrites(bool fromVehicle)
    {
        await using var f = await Fixture.Create();
        var source = f.Pairs["A"]; var target = f.Pairs["A2"]; var otherBranch = f.Pairs["B"];
        await f.Link(fromVehicle ? "drivers" : "vehicles", fromVehicle ? otherBranch.Driver : otherBranch.Vehicle,
            fromVehicle ? source.Vehicle : source.Driver);
        var before = await f.Snapshot();
        Assert.Equal(409, Status(await f.Pair(fromVehicle, fromVehicle ? target.Driver : source.Driver,
            fromVehicle ? source.Vehicle : target.Vehicle)));
        Assert.Equal(before, await f.Snapshot());
    }

    [Theory]
    [InlineData(true, "FOREIGN", false, 400)]
    [InlineData(false, "FOREIGN", false, 400)]
    [InlineData(true, "B", false, 400)]
    [InlineData(false, "B", false, 400)]
    [InlineData(true, "B", true, 422)]
    [InlineData(false, "B", true, 422)]
    public async Task MasterPairWithoutHos_StillRejectsForeignTenantAndBranchWithoutWrites(bool fromVehicle, string target, bool companyWide, int expected)
    {
        await using var f = await Fixture.Create();
        var source = f.Pairs["A"]; var other = f.Pairs[target];
        var before = await f.Snapshot();
        Assert.Equal(expected, Status(await f.Pair(fromVehicle,
            fromVehicle ? other.Driver : source.Driver, fromVehicle ? source.Vehicle : other.Vehicle, companyWide)));
        Assert.Equal(before, await f.Snapshot());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MasterPair_RequiresFleetManagePermissionWithoutWrites(bool fromVehicle)
    {
        await using var f = await Fixture.Create();
        var pair = f.Pairs["A"];
        var before = await f.Snapshot();
        Assert.Equal(403, Status(await f.PairBody(fromVehicle, pair.Driver, pair.Vehicle,
            new() { ["targetId"] = fromVehicle ? pair.Driver : pair.Vehicle }, allowed: false)));
        Assert.Equal(before, await f.Snapshot());
    }

    public static IEnumerable<object[]> InvalidPairBodies()
    {
        foreach (var fromVehicle in new[] { true, false })
            foreach (var json in new[] { "{}", "{\"targetId\":0}", "{\"targetId\":-1}",
                "{\"targetId\":\"invalid\"}", "{\"targetId\":1.5}", "{\"targetId\":true}",
                "{\"targetId\":{}}", "{\"targetId\":\"\"}", "{\"targetId\":\"   \"}" })
                yield return new object[] { fromVehicle, json };
    }

    [Theory]
    [MemberData(nameof(InvalidPairBodies))]
    public async Task MasterPair_InvalidOrMissingTargetCannotClearExistingPair(bool fromVehicle, string json)
    {
        await using var f = await Fixture.Create();
        var pair = f.Pairs["A"];
        Assert.Equal(200, Status(await f.Pair(fromVehicle, pair.Driver, pair.Vehicle)));
        var before = await f.Snapshot();
        var body = JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;
        Assert.Equal(400, Status(await f.PairBody(fromVehicle, pair.Driver, pair.Vehicle, body)));
        Assert.Equal(before, await f.Snapshot());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MasterPair_ExplicitNullUnassignsBothSidesAndReleasesHistoryIdempotently(bool fromVehicle)
    {
        await using var f = await Fixture.Create();
        var pair = f.Pairs["A"];
        Assert.Equal(200, Status(await f.Pair(fromVehicle, pair.Driver, pair.Vehicle)));
        Assert.Equal(200, Status(await f.PairBody(fromVehicle, pair.Driver, pair.Vehicle, new() { ["targetId"] = null })));
        Assert.Equal(JsonValueKind.Null, (await f.Row("drivers", pair.Driver)).GetProperty("assigned_vehicle_id").ValueKind);
        Assert.Equal(JsonValueKind.Null, (await f.Row("vehicles", pair.Vehicle)).GetProperty("assigned_driver_id").ValueKind);
        var history = Assert.Single(await f.History());
        Assert.Equal("Released", history.GetProperty("status").GetString());
        Assert.True(history.GetProperty("released_at").GetDateTimeOffset() >= history.GetProperty("assigned_at").GetDateTimeOffset());
        var before = await f.Snapshot();
        Assert.Equal(200, Status(await f.PairBody(fromVehicle, pair.Driver, pair.Vehicle, new() { ["targetId"] = null })));
        Assert.Equal(before, await f.Snapshot());
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task MasterPair_ArchivedSourceOrTargetRejectedWithoutWrites(bool fromVehicle, bool sourceArchived)
    {
        await using var f = await Fixture.Create();
        var pair = f.Pairs["A"];
        var archiveVehicle = fromVehicle == sourceArchived;
        await f.Archive(archiveVehicle ? "vehicles" : "drivers", archiveVehicle ? pair.Vehicle : pair.Driver);
        var before = await f.Snapshot();
        Assert.Equal(sourceArchived ? 404 : 400, Status(await f.Pair(fromVehicle, pair.Driver, pair.Vehicle)));
        Assert.Equal(before, await f.Snapshot());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MasterPair_CompetingUpdatesSerializeIntoOneSymmetricPairAndReleasedHistory(bool sharedVehicle)
    {
        await using var f = await Fixture.Create();
        var first = f.Pairs["A"]; var second = f.Pairs["A2"];
        var results = await f.CompetingPairs(sharedVehicle, first, second);
        Assert.All(results, result => Assert.Equal(200, Status(result)));
        var history = await f.History();
        Assert.Equal(2, history.Length);
        var active = Assert.Single(history, row => row.GetProperty("status").GetString() == "Active");
        var released = Assert.Single(history, row => row.GetProperty("status").GetString() == "Released");
        Assert.NotEqual(JsonValueKind.Null, released.GetProperty("released_at").ValueKind);
        var driver = active.GetProperty("driver_id").GetInt64();
        var vehicle = active.GetProperty("vehicle_id").GetInt64();
        await f.AssertPair(driver, vehicle);
        if (sharedVehicle)
        {
            Assert.Equal(first.Vehicle, vehicle);
            Assert.Equal(JsonValueKind.Null, (await f.Row("drivers", driver == first.Driver ? second.Driver : first.Driver)).GetProperty("assigned_vehicle_id").ValueKind);
        }
        else
        {
            Assert.Equal(first.Driver, driver);
            Assert.Equal(JsonValueKind.Null, (await f.Row("vehicles", vehicle == first.Vehicle ? second.Vehicle : first.Vehicle)).GetProperty("assigned_driver_id").ValueKind);
        }
    }

    [Fact]
    public async Task HistoryRead_IdentifiesCurrentReciprocalPair_WithoutHidingReleasedOrStaleHistory()
    {
        await using var f = await Fixture.Create();
        var first = f.Pairs["A"]; var second = f.Pairs["A2"];
        Assert.Equal(200, Status(await f.Pair(true, first.Driver, first.Vehicle)));
        Assert.True(Assert.Single(await f.HistoryRead()).GetProperty("isCurrent").GetBoolean());
        await f.Link("vehicles", first.Vehicle, null);
        var stale = Assert.Single(await f.HistoryRead());
        Assert.False(stale.GetProperty("isCurrent").GetBoolean());
        Assert.Equal("Active", stale.GetProperty("status").GetString());
        Assert.Equal(200, Status(await f.Pair(true, second.Driver, first.Vehicle)));
        var rows = await f.HistoryRead();
        Assert.Equal(2, rows.Length);
        var current = Assert.Single(rows, row => row.GetProperty("isCurrent").GetBoolean());
        Assert.Equal(second.Driver, current.GetProperty("driverId").GetInt64());
        Assert.Equal(first.Vehicle, current.GetProperty("vehicleId").GetInt64());
        Assert.Single(rows, row => row.GetProperty("status").GetString() == "Released");
    }

    private static int Status(IResult result) => ((IStatusCodeHttpResult)result).StatusCode ?? 200;

    private sealed class Fixture(string owner, IConfiguration config, WebApplication app) : IAsyncDisposable
    {
        private readonly string prefix = "MASTER-HOS-" + Guid.NewGuid().ToString("N");
        private readonly List<long> companies = [];
        public long CompanyA, CompanyB, BranchA, BranchB;
        public readonly Dictionary<string, (long Driver, long Vehicle)> Pairs = [];
        public static async Task<Fixture> Create()
        {
            foreach (var variable in new[] { "OPSTRAX_TEST_DB", "OPSTRAX_TEST_DB_APP", "OPSTRAX_TEST_DB_SYSTEM" })
                Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)), "Explicit local fixture identities required; no fallback.");
            var owner = new NpgsqlConnectionStringBuilder(TestDb.ConnectionString);
            var runtime = new NpgsqlConnectionStringBuilder(TestDb.AppConnectionString);
            var system = new NpgsqlConnectionStringBuilder(TestDb.SystemConnectionString);
            foreach (var connection in new[] { owner, runtime, system })
            {
                Assert.Equal("127.0.0.1", connection.Host);
                Assert.True(connection.Database == "opstrax_local" && connection.Port == 5433 ||
                    connection.Database?.StartsWith("opstrax_assignment_http_", StringComparison.Ordinal) == true,
                    "Only the local fixture or an explicitly named disposable assignment database is allowed.");
                Assert.Equal(owner.Port, connection.Port); Assert.Equal(owner.Database, connection.Database);
            }
            Assert.Equal("opstrax_app", runtime.Username); Assert.Equal("opstrax_system", system.Username);
            Assert.DoesNotContain(owner.Username, new[] { "opstrax_app", "opstrax_system" });
            Assert.True(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PG_CONNECTION_REPLICA")));
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Staging", ["Rls:EnforceTenantContext"] = "true",
                ["ConnectionStrings:DefaultConnection"] = runtime.ConnectionString,
                ["ConnectionStrings:SystemConnection"] = system.ConnectionString
            }).Build();
            await new Database(config).ValidateProductionIdentitiesAsync();
            var app = WebApplication.CreateBuilder().Build(); app.MapOpsTraxEndpoints();
            var fixture = new Fixture(owner.ConnectionString, config, app);
            try { await fixture.Initialize(); return fixture; }
            catch { await fixture.DisposeAsync(); throw; }
        }


        public Task<IResult> Pair(bool fromVehicle, long driver, long vehicle, bool companyWide = false)
            => Call(fromVehicle ? "/api/vehicles/{id:long}/assign-driver" : "/api/drivers/{id:long}/assign-vehicle",
                fromVehicle ? vehicle : driver, new() { ["targetId"] = fromVehicle ? driver : vehicle }, companyWide);
        public Task<IResult> PairBody(bool fromVehicle, long driver, long vehicle, Dictionary<string, object?> body, bool allowed = true)
            => Call(fromVehicle ? "/api/vehicles/{id:long}/assign-driver" : "/api/drivers/{id:long}/assign-vehicle",
                fromVehicle ? vehicle : driver, body, allowed: allowed);
        public Task<IResult> Dispatch(long driver, long vehicle)
            => Call("/api/dispatch/assignments", 0, new() { ["DriverId"] = driver, ["VehicleId"] = vehicle });

        private async Task<IResult> Call(string path, long id, Dictionary<string, object?> body, bool companyWide = false, bool allowed = true, CancellationToken ct = default)
        {
            var db = new Database(config, new TenantScopeAccessor());
            return await db.RunInTenantScopeAsync(CompanyA, async () =>
            {
                var http = new DefaultHttpContext();
                http.Items[EndpointMappings.AuthCompanyIdItemKey] = CompanyA;
                if (!companyWide) http.Items[EndpointMappings.AuthBranchIdItemKey] = BranchA;
                http.Items[EndpointMappings.AuthUserIdItemKey] = 0L;
                http.Items[EndpointMappings.AuthRoleItemKey] = "Synthetic fleet operator";
                http.Items[EndpointMappings.AuthPermissionsItemKey] = allowed ? new[] { "fleet:manage", "dispatch:assign", "vehicles:view" } : new[] { "fleet:view", "dispatch:assign" };
                var handler = Registered(path);
                var args = handler.Method.GetParameters().Select(p => p.ParameterType == typeof(HttpContext) ? (object)http :
                    p.ParameterType == typeof(long) ? id : p.ParameterType == typeof(Dictionary<string, object?>) ? body :
                    p.ParameterType == typeof(Database) ? db : p.ParameterType == typeof(AuditService) ? new AuditService(db) :
                    p.ParameterType == typeof(NotificationService) ? new NotificationService(db) :
                    p.ParameterType == typeof(CancellationToken) ? ct :
                    p.ParameterType.Name == "DispatchAssignBody" ? JsonSerializer.Deserialize(JsonSerializer.Serialize(body), p.ParameterType)! :
                    throw new InvalidOperationException("Unexpected registered endpoint parameter")).ToArray();
                try { return await (Task<IResult>)handler.DynamicInvoke(args)!; }
                catch (TargetInvocationException error) when (error.InnerException is not null)
                { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
            }, ct);
        }
        private Delegate Registered(string path)
        {
            var matches = new List<Delegate>();
            foreach (var source in ((IEndpointRouteBuilder)app).DataSources)
            {
                if (source.GetType().GetField("_routeEntries", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(source) is not IEnumerable entries) continue;
                foreach (var entry in entries)
                    if (entry is not null && Member(entry, "RoutePattern") is RoutePattern pattern && pattern.RawText == path && Member(entry, "RouteHandler") is Delegate handler)
                        if (Member(entry, "HttpMethods") is IEnumerable<string> methods && methods.Contains(path == "/api/vehicle-assignments" ? HttpMethods.Get : HttpMethods.Post)) matches.Add(handler);
            }
            return Assert.Single(matches);
        }
        private static object? Member(object value, string name) => value.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(value)
            ?? value.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(value);


        private async Task<string> Sql(string sql, params (string Key, object? Value)[] values)
        {
            await using var connection = new NpgsqlConnection(owner); await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            foreach (var (key, value) in values) command.Parameters.AddWithValue(key, value ?? DBNull.Value);
            return (await command.ExecuteScalarAsync())?.ToString() ?? "";
        }
        public Task<string> Archive(string table, long id)
        {
            Assert.Contains(table, new[] { "drivers", "vehicles" });
            return Sql($"UPDATE {table} SET deleted_at=NOW() WHERE id=@id AND company_id=@c", ("id", id), ("c", CompanyA));
        }
        public Task<string> Link(string table, long id, long? target)
        {
            Assert.Contains(table, new[] { "drivers", "vehicles" });
            var column = table == "drivers" ? "assigned_vehicle_id" : "assigned_driver_id";
            return Sql($"UPDATE {table} SET {column}=@target::BIGINT WHERE id=@id AND company_id=@c", ("target", target), ("id", id), ("c", CompanyA));
        }
        public async Task<IResult[]> CompetingPairs(bool sharedVehicle, (long Driver, long Vehicle) first, (long Driver, long Vehicle) second)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var ct = deadline.Token;
            await using var gate = new NpgsqlConnection(owner); await gate.OpenAsync(ct);
            await using var transaction = await gate.BeginTransactionAsync(ct);
            await using var hold = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@company)", gate, transaction);
            hold.Parameters.AddWithValue("company", CompanyA);
            await hold.ExecuteNonQueryAsync(ct);
            var path = sharedVehicle ? "/api/vehicles/{id:long}/assign-driver" : "/api/drivers/{id:long}/assign-vehicle";
            var source = sharedVehicle ? first.Vehicle : first.Driver;
            var requests = new[] {
                Call(path, source, new() { ["targetId"] = sharedVehicle ? first.Driver : first.Vehicle }, ct: ct),
                Call(path, source, new() { ["targetId"] = sharedVehicle ? second.Driver : second.Vehicle }, ct: ct)
            };
            try
            {
                // Both request transactions must demonstrably wait on this fixture's
                // tenant lock before it is released; simultaneous Task starts alone
                // would not prove contention or serialization.
                await using var waiters = new NpgsqlCommand(@"SELECT COUNT(*) FROM pg_stat_activity
                    WHERE @blocker=ANY(pg_blocking_pids(pid)) AND query LIKE '%pg_advisory_xact_lock%'", gate, transaction);
                waiters.Parameters.AddWithValue("blocker", gate.ProcessID);
                using var poll = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
                await using var refresh = new NpgsqlCommand("SELECT pg_stat_clear_snapshot()", gate, transaction);
                while (true)
                {
                    await refresh.ExecuteNonQueryAsync(ct);
                    if (Convert.ToInt64(await waiters.ExecuteScalarAsync(ct)) == 2) break;
                    await poll.WaitForNextTickAsync(ct);
                }
                Assert.All(requests, request => Assert.False(request.IsCompleted));
                await transaction.CommitAsync(ct);
                return await Task.WhenAll(requests).WaitAsync(ct);
            }
            finally
            {
                deadline.Cancel();
                await transaction.DisposeAsync();
                var all = Task.WhenAll(requests);
                try { await all.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch when (all.IsCompleted) { }
            }
        }
        public async Task<JsonElement> Row(string table, long id)
        {
            Assert.Contains(table, new[] { "drivers", "vehicles" });
            using var json = JsonDocument.Parse(await Sql($"SELECT to_jsonb(r)::text FROM {table} r WHERE id=@id AND company_id=@c", ("id", id), ("c", CompanyA)));
            return json.RootElement.Clone();
        }
        public async Task AssertPair(long driver, long vehicle)
        {
            Assert.Equal(vehicle, (await Row("drivers", driver)).GetProperty("assigned_vehicle_id").GetInt64());
            Assert.Equal(driver, (await Row("vehicles", vehicle)).GetProperty("assigned_driver_id").GetInt64());
        }
        public async Task<JsonElement[]> History()
        {
            using var json = JsonDocument.Parse(await Sql("SELECT COALESCE(jsonb_agg(to_jsonb(a) ORDER BY id),'[]')::text FROM vehicle_assignments a WHERE company_id=@c", ("c", CompanyA)));
            return json.RootElement.EnumerateArray().Select(row => row.Clone()).ToArray();
        }
        public async Task<JsonElement[]> HistoryRead()
        {
            var result = await Call("/api/vehicle-assignments", 0, new());
            Assert.Equal(200, Status(result));
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(((IValueHttpResult)result).Value));
            return json.RootElement.GetProperty("Data").EnumerateArray().Select(row => row.Clone()).ToArray();
        }
        public async Task<long> CountDispatch() => long.Parse(await Sql("SELECT COUNT(*) FROM dispatch_assignments WHERE company_id=@c", ("c", CompanyA)));
        public Task<string> StaleHos(long driver) => Sql(@"INSERT INTO hos_clocks(company_id,branch_id,driver_id,status,drive_time_remaining_minutes,shift_time_remaining_minutes,cycle_time_remaining_minutes,clock_source,source_authority,source_observed_at)
            VALUES (@c,@b,@d,'OK',480,600,1200,'Synthetic regression fixture','Authoritative',NOW() - INTERVAL '25 hours')", ("c", CompanyA), ("b", BranchA), ("d", driver));
        public async Task<string> Snapshot()
        {
            var rows = new List<string>();
            foreach (var table in new[] { "drivers", "vehicles", "vehicle_assignments", "dispatch_assignments", "audit_logs", "entity_timeline_events" })
                rows.Add(await Sql($"SELECT COALESCE(jsonb_agg(to_jsonb(r) ORDER BY id),'[]')::text FROM {table} r WHERE company_id=ANY(@ids)", ("ids", companies.ToArray())));
            return string.Join("\n", rows);
        }
        private async Task Initialize()
        {
            CompanyA = long.Parse(await Sql("INSERT INTO companies(company_code,name,industry) VALUES (@code,'Synthetic master pairing fixture','Transportation') RETURNING id", ("code", prefix + "A")));
            companies.Add(CompanyA);
            CompanyB = long.Parse(await Sql("INSERT INTO companies(company_code,name,industry) VALUES (@code,'Synthetic foreign fixture','Transportation') RETURNING id", ("code", prefix + "B")));
            companies.Add(CompanyB);
            BranchA = long.Parse(await Sql("INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,'A','Synthetic A','Active') RETURNING id", ("c", CompanyA)));
            BranchB = long.Parse(await Sql("INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,'B','Synthetic B','Active') RETURNING id", ("c", CompanyA)));
            foreach (var key in new[] { "A", "A2", "B", "FOREIGN" })
            {
                var company = key == "FOREIGN" ? CompanyB : CompanyA;
                long? branch = key == "FOREIGN" ? null : key == "B" ? BranchB : BranchA;
                var driver = long.Parse(await Sql("INSERT INTO drivers(company_id,branch_id,driver_code,full_name,status,safety_score) VALUES (@c,@b,@code,'Synthetic driver','Available',95) RETURNING id", ("c", company), ("b", branch), ("code", key)));
                var vehicle = long.Parse(await Sql("INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status,availability_status,out_of_service) VALUES (@c,@b,@code,'Truck','legacy-fleet-identifier',@alt,'Available','available',false) RETURNING id", ("c", company), ("b", branch), ("code", key), ("alt", prefix + key)));
                Pairs[key] = (driver, vehicle);
            }
        }
        public async ValueTask DisposeAsync()
        {
            await app.DisposeAsync(); if (companies.Count == 0) return;
            await using var connection = new NpgsqlConnection(owner); await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            foreach (var (table, column) in new[] { ("drivers", "assigned_vehicle_id"), ("vehicles", "assigned_driver_id") })
            {
                await using var unlink = new NpgsqlCommand($"UPDATE {table} SET {column}=NULL WHERE company_id=ANY(@ids) AND company_id IN (SELECT id FROM companies WHERE company_code LIKE @prefix)", connection, transaction);
                unlink.Parameters.AddWithValue("ids", companies.ToArray()); unlink.Parameters.AddWithValue("prefix", prefix + "%");
                await unlink.ExecuteNonQueryAsync();
            }
            foreach (var table in new[] { "entity_timeline_events", "audit_logs", "dispatch_assignments", "vehicle_assignments", "hos_clocks", "drivers", "vehicles", "branches" })
            {
                await using var command = new NpgsqlCommand($"DELETE FROM {table} WHERE company_id=ANY(@ids) AND company_id IN (SELECT id FROM companies WHERE company_code LIKE @prefix)", connection, transaction);
                command.Parameters.AddWithValue("ids", companies.ToArray()); command.Parameters.AddWithValue("prefix", prefix + "%"); await command.ExecuteNonQueryAsync();
            }
            await using var remove = new NpgsqlCommand("DELETE FROM companies WHERE id=ANY(@ids) AND company_code LIKE @prefix", connection, transaction);
            remove.Parameters.AddWithValue("ids", companies.ToArray()); remove.Parameters.AddWithValue("prefix", prefix + "%");
            await remove.ExecuteNonQueryAsync(); await transaction.CommitAsync();
        }
    }
}
