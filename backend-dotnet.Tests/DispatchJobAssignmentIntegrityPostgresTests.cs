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
using Xunit.Abstractions;

namespace Opstrax.Tests;

// S1 assignment-only fixture: existing local schema, uniquely owned rows, actual
// registered delegates and restricted signed tenant scopes. AuthItems are synthetic;
// these are not browser, full HTTP authentication, or certified HOS evidence.
[Collection("fleet-identity-schema")]
[Trait("Category", "Integration")]
public sealed class DispatchJobAssignmentIntegrityPostgresTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    [InlineData("NULL")]
    public async Task CompanyWideAssignment_KeepsJobBranchAndBranchDispatchVisibility(string branch)
    {
        await using var f = await Fixture.Create();
        var job = f.Jobs[branch];
        var pair = f.Pairs[branch];
        Assert.Equal(200, Status(await f.Assign(job, pair.Driver, pair.Vehicle)));
        var assignment = await f.ActiveAssignment(job);

        // A company-wide actor may work in either branch, but must not erase the
        // persisted branch identity or hide the result from that branch's dispatcher.
        Assert.Equal(200, Status(await f.Detail(assignment.GetProperty("id").GetInt64(), f.Branch(branch))));
        Assert.Equal(404, Status(await f.Detail(assignment.GetProperty("id").GetInt64(), branch == "B" ? f.BranchA : f.BranchB)));
        Assert.Equal(f.Branch(branch), NullableId(assignment, "branch_id"));
        Assert.Equal(pair.Driver, assignment.GetProperty("driver_id").GetInt64());
        Assert.Equal(pair.Vehicle, assignment.GetProperty("vehicle_id").GetInt64());
        var persisted = await f.Job(job);
        Assert.Equal(f.Branch(branch), NullableId(persisted, "branch_id"));
        Assert.Equal("Assigned", persisted.GetProperty("status").GetString());
        Assert.Equal(pair.Driver, persisted.GetProperty("assigned_driver_id").GetInt64());
        Assert.Equal(pair.Vehicle, persisted.GetProperty("assigned_vehicle_id").GetInt64());
        await f.AssertEvents(job, 1);
    }

    [Theory]
    [InlineData("A", "B", "B")]
    [InlineData("A", "A", "B")]
    [InlineData("A", "NULL", "NULL")]
    [InlineData("NULL", "A", "A")]
    public async Task CompanyWideAssignment_RejectsJobResourceBranchMismatchWithoutWrites(string jobBranch, string driverBranch, string vehicleBranch)
    {
        await using var f = await Fixture.Create();
        var before = await f.Snapshot();
        var result = await f.Assign(f.Jobs[jobBranch], f.Pairs[driverBranch].Driver, f.Pairs[vehicleBranch].Vehicle);
        Assert.Equal(400, Status(result));
        Assert.Equal(before, await f.Snapshot());
    }

    [Theory]
    [InlineData("foreign-job", 404)]
    [InlineData("other-branch-job", 404)]
    [InlineData("null-branch-job", 404)]
    [InlineData("foreign-driver", 400)]
    [InlineData("foreign-vehicle", 400)]
    [InlineData("other-branch-driver", 400)]
    [InlineData("other-branch-vehicle", 400)]
    [InlineData("deleted-driver", 400)]
    [InlineData("deleted-vehicle", 400)]
    public async Task RestrictedAssignment_RejectsInaccessibleIdentityWithoutWrites(string scenario, int expected)
    {
        await using var f = await Fixture.Create();
        var job = scenario switch { "foreign-job" => f.Jobs["FOREIGN"], "other-branch-job" => f.Jobs["B"], "null-branch-job" => f.Jobs["NULL"], _ => f.Jobs["A"] };
        var driver = scenario switch { "foreign-driver" => f.Pairs["FOREIGN"].Driver, "other-branch-driver" => f.Pairs["B"].Driver, _ => f.Pairs["A"].Driver };
        var vehicle = scenario switch { "foreign-vehicle" => f.Pairs["FOREIGN"].Vehicle, "other-branch-vehicle" => f.Pairs["B"].Vehicle, _ => f.Pairs["A"].Vehicle };
        if (scenario == "deleted-driver") await f.SoftDelete("drivers", driver);
        if (scenario == "deleted-vehicle") await f.SoftDelete("vehicles", vehicle);
        var before = await f.Snapshot();
        Assert.Equal(expected, Status(await f.Assign(job, driver, vehicle, f.BranchA)));
        Assert.Equal(before, await f.Snapshot());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConflictingReplacement_RollsBackOriginalAssignmentJobAndEvents(bool conflictDriver)
    {
        await using var f = await Fixture.Create();
        var a = f.Pairs["A"]; var other = f.Pairs["A2"];
        Assert.Equal(200, Status(await f.Assign(f.Jobs["A"], a.Driver, a.Vehicle, f.BranchA)));
        Assert.Equal(200, Status(await f.Assign(f.Jobs["A2"], other.Driver, other.Vehicle, f.BranchA)));
        var before = await f.Snapshot();
        Assert.Equal(409, Status(await f.Assign(f.Jobs["A"], conflictDriver ? other.Driver : a.Driver,
            conflictDriver ? a.Vehicle : other.Vehicle, f.BranchA)));
        Assert.Equal(before, await f.Snapshot());
    }

    [Fact]
    public async Task CompanyWideReplacementWithinJobBranch_PreservesHistoryAndBranchVisibility()
    {
        await using var f = await Fixture.Create();
        var first = f.Pairs["A"]; var replacement = f.Pairs["A2"]; var job = f.Jobs["A"];
        Assert.Equal(200, Status(await f.Assign(job, first.Driver, first.Vehicle, f.BranchA)));
        var oldAssignment = await f.ActiveAssignment(job);
        Assert.Equal(200, Status(await f.Assign(job, replacement.Driver, replacement.Vehicle)));
        var current = await f.ActiveAssignment(job);
        Assert.NotEqual(oldAssignment.GetProperty("id").GetInt64(), current.GetProperty("id").GetInt64());
        Assert.Equal(f.BranchA, NullableId(current, "branch_id"));
        Assert.Equal(replacement.Driver, current.GetProperty("driver_id").GetInt64());
        Assert.Equal(replacement.Vehicle, current.GetProperty("vehicle_id").GetInt64());
        var previous = await f.Assignment(oldAssignment.GetProperty("id").GetInt64());
        Assert.Equal("cancelled", previous.GetProperty("assignment_status").GetString());
        Assert.Equal("assigned", previous.GetProperty("previous_status").GetString());
        Assert.Equal(first.Driver, previous.GetProperty("driver_id").GetInt64());
        Assert.Equal(first.Vehicle, previous.GetProperty("vehicle_id").GetInt64());
        Assert.Equal(200, Status(await f.Detail(current.GetProperty("id").GetInt64(), f.BranchA)));
        Assert.Equal(2, await f.AssignmentCount());
        await f.AssertEvents(job, 2, statusEvents: 1);
    }

    [Fact]
    public async Task IncompatibleReplacement_DoesNotCloseOrRewriteExistingAssignment()
    {
        await using var f = await Fixture.Create();
        var first = f.Pairs["A"]; var other = f.Pairs["B"];
        Assert.Equal(200, Status(await f.Assign(f.Jobs["A"], first.Driver, first.Vehicle, f.BranchA)));
        var before = await f.Snapshot();
        Assert.Equal(400, Status(await f.Assign(f.Jobs["A"], other.Driver, other.Vehicle)));
        Assert.Equal(before, await f.Snapshot());
    }

    [Fact]
    public async Task AuditFailureAfterAssignmentWrites_RollsBackJobAssignmentAndEvents()
    {
        await using var f = await Fixture.Create();
        var before = await f.Snapshot();
        var pair = f.Pairs["A"];
        var error = await Assert.ThrowsAsync<PostgresException>(() => f.Assign(f.Jobs["A"], pair.Driver, pair.Vehicle,
            f.BranchA, actorRole: new string('x', 10000)));
        Assert.Equal("22001", error.SqlState);
        Assert.Contains("Audit", error.StackTrace ?? "");
        Assert.Equal(before, await f.Snapshot());
    }

    [Fact]
    public async Task CompetingJobsForOnePair_OneWinnerAndOneConflict_NoPartialLoser()
    {
        await using var f = await Fixture.Create();
        var pair = f.Pairs["A"];
        var jobs = new[] { f.Jobs["A"], f.Jobs["A2"] };
        var before = await f.Job(jobs[0]); var otherBefore = await f.Job(jobs[1]);
        var results = await Task.WhenAll(jobs.Select(job => f.Assign(job, pair.Driver, pair.Vehicle, f.BranchA)));
        Assert.Equal(new[] { 200, 409 }, results.Select(Status).Order().ToArray());
        for (var index = 0; index < jobs.Length; index++)
        {
            var row = await f.Job(jobs[index]);
            if (Status(results[index]) == 409)
            {
                Assert.Equal(index == 0 ? before.GetRawText() : otherBefore.GetRawText(), row.GetRawText());
                await f.AssertEvents(jobs[index], 0);
            }
            else
            {
                Assert.Equal("Assigned", row.GetProperty("status").GetString());
                await f.AssertEvents(jobs[index], 1);
            }
        }
        Assert.Equal(1, await f.AssignmentCount());
    }

    [Theory]
    [InlineData("drivers", false)]
    [InlineData("drivers", true)]
    [InlineData("vehicles", false)]
    [InlineData("vehicles", true)]
    public async Task ResourceChangeFirst_AssignmentWaitsThenRejectsWithoutAssignmentEffects(string table, bool archive)
    {
        await using var f = await Fixture.Create();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ct = deadline.Token;
        var pair = f.Pairs["A"]; var job = f.Jobs["A"];
        var resourceId = table == "drivers" ? pair.Driver : pair.Vehicle;
        var before = await f.AssignmentEffectsSnapshot();
        await using var changing = await f.OpenConcurrencyConnection(ct);
        await using var changeTransaction = await changing.BeginTransactionAsync(ct);
        await Fixture.SetTransactionDeadlines(changing, changeTransaction, ct);
        Task<IResult>? assignment = null;
        try
        {
            // This owner transaction changes exactly one fixture-owned row. The
            // handler must wait for that row, then re-evaluate its branch/archive predicate.
            Assert.Equal(1, await f.ChangeResource(changing, changeTransaction, table, resourceId, archive, ct));
            var started = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            assignment = f.Assign(job, pair.Driver, pair.Vehicle, onBackendPid: pid => started.TrySetResult(pid), ct: ct);
            var assignmentPid = await started.Task.WaitAsync(ct);
            Assert.NotEqual(changing.ProcessID, assignmentPid);
            output.WriteLine(await f.ObserveBlocking(assignmentPid, changing.ProcessID, "FOR SHARE OF v,d", ct));
            Assert.False(assignment.IsCompleted);
            await changeTransaction.CommitAsync(ct);

            Assert.Equal(400, Status(await assignment.WaitAsync(ct)));
            await f.AssertResourceChanged(table, resourceId, archive);
            Assert.Equal(before, await f.AssignmentEffectsSnapshot());
            Assert.Equal(0, await f.AssignmentCount());
            await f.AssertEvents(job, 0);
        }
        finally
        {
            deadline.Cancel();
            // Dispose rolls back only an active transaction; Connection also stays
            // non-null after a successful commit, so it is not a completion test.
            await changeTransaction.DisposeAsync();
            await DrainCanceledOperations(assignment);
        }
    }

    [Theory]
    [InlineData("drivers", false)]
    [InlineData("drivers", true)]
    [InlineData("vehicles", false)]
    [InlineData("vehicles", true)]
    public async Task AssignmentFirst_ResourceChangeWaitsUntilAssignmentCommit(string table, bool archive)
    {
        await using var f = await Fixture.Create();
        var original = f.Pairs["A"]; var replacement = f.Pairs["A2"]; var job = f.Jobs["A"];
        Assert.Equal(200, Status(await f.Assign(job, original.Driver, original.Vehicle, f.BranchA)));
        var previous = await f.ActiveAssignment(job);
        var previousId = previous.GetProperty("id").GetInt64();
        var resourceId = table == "drivers" ? replacement.Driver : replacement.Vehicle;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ct = deadline.Token;
        await using var gate = await f.OpenConcurrencyConnection(ct);
        await using var gateTransaction = await gate.BeginTransactionAsync(ct);
        await Fixture.SetTransactionDeadlines(gate, gateTransaction, ct);
        await using var changing = await f.OpenConcurrencyConnection(ct);
        await using var changeTransaction = await changing.BeginTransactionAsync(ct);
        await Fixture.SetTransactionDeadlines(changing, changeTransaction, ct);
        Task<IResult>? assignment = null;
        Task<int>? resourceChange = null;
        try
        {
            // Only the owned prior assignment row is gated. Reaching its UPDATE
            // proves the real handler has already acquired its resource FOR SHARE locks.
            await f.HoldPriorAssignment(gate, gateTransaction, previousId, ct);
            var started = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            assignment = f.Assign(job, replacement.Driver, replacement.Vehicle,
                onBackendPid: pid => started.TrySetResult(pid), ct: ct);
            var assignmentPid = await started.Task.WaitAsync(ct);
            Assert.NotEqual(gate.ProcessID, assignmentPid);
            Assert.NotEqual(changing.ProcessID, assignmentPid);
            output.WriteLine(await f.ObserveBlocking(assignmentPid, gate.ProcessID, "UPDATE dispatch_assignments", ct));
            Assert.False(assignment.IsCompleted);

            resourceChange = f.ChangeResource(changing, changeTransaction, table, resourceId, archive, ct);
            output.WriteLine(await f.ObserveBlocking(changing.ProcessID, assignmentPid, "UPDATE " + table, ct));
            Assert.False(resourceChange.IsCompleted);
            Assert.False(assignment.IsCompleted);
            await gateTransaction.RollbackAsync(ct);

            Assert.Equal(200, Status(await assignment.WaitAsync(ct)));
            Assert.Equal(1, await resourceChange.WaitAsync(ct));
            // Read through another connection before committing the later resource
            // mutation: success must mean the assignment transaction really committed.
            var current = await f.ActiveAssignment(job);
            Assert.NotEqual(previousId, current.GetProperty("id").GetInt64());
            Assert.Equal(f.BranchA, NullableId(current, "branch_id"));
            Assert.Equal(replacement.Driver, current.GetProperty("driver_id").GetInt64());
            Assert.Equal(replacement.Vehicle, current.GetProperty("vehicle_id").GetInt64());
            var persistedJob = await f.Job(job);
            Assert.Equal(f.BranchA, NullableId(persistedJob, "branch_id"));
            Assert.Equal("Assigned", persistedJob.GetProperty("status").GetString());
            Assert.Equal(replacement.Driver, persistedJob.GetProperty("assigned_driver_id").GetInt64());
            Assert.Equal(replacement.Vehicle, persistedJob.GetProperty("assigned_vehicle_id").GetInt64());
            var closed = await f.Assignment(previousId);
            Assert.Equal("cancelled", closed.GetProperty("assignment_status").GetString());
            Assert.Equal("assigned", closed.GetProperty("previous_status").GetString());
            Assert.Equal(f.BranchA, NullableId(closed, "branch_id"));
            Assert.Equal(2, await f.AssignmentCount());
            await f.AssertEvents(job, 2, statusEvents: 1);
            var committedAssignmentEffects = await f.AssignmentEffectsSnapshot();

            await changeTransaction.CommitAsync(ct);
            await f.AssertResourceChanged(table, resourceId, archive);
            Assert.Equal(committedAssignmentEffects, await f.AssignmentEffectsSnapshot());
            Assert.Equal(200, Status(await f.Detail(current.GetProperty("id").GetInt64(), f.BranchA)));
        }
        finally
        {
            deadline.Cancel();
            await gateTransaction.DisposeAsync();
            try { await DrainCanceledOperations(assignment, resourceChange); }
            finally
            {
                await changeTransaction.DisposeAsync();
            }
        }
    }

    private static async Task DrainCanceledOperations(params Task?[] operations)
    {
        var all = Task.WhenAll(operations.OfType<Task>());
        try { await all.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch when (all.IsCompleted)
        {
            // Main-path assertions observe operation outcomes. Cleanup observes any
            // cancellation/failure too, before transaction and fixture disposal.
        }
    }

    [Fact]
    public async Task InvalidIdsAndInsufficientPermission_AreRejectedWithoutWrites()
    {
        await using var f = await Fixture.Create();
        var before = await f.Snapshot(); var pair = f.Pairs["A"];
        foreach (var value in new object?[] { null, 0L, -1L, "not-an-id", "1.5" })
        {
            Assert.Equal(400, Status(await f.Assign(f.Jobs["A"], value, pair.Vehicle, f.BranchA)));
            Assert.Equal(400, Status(await f.Assign(f.Jobs["A"], pair.Driver, value, f.BranchA)));
        }
        Assert.Equal(403, Status(await f.Assign(f.Jobs["A"], pair.Driver, pair.Vehicle, f.BranchA, allowed: false)));
        Assert.Equal(before, await f.Snapshot());
    }

    private static int Status(IResult result) => ((IStatusCodeHttpResult)result).StatusCode ?? 200;
    private static long? NullableId(JsonElement row, string name) => row.GetProperty(name).ValueKind == JsonValueKind.Null ? null : row.GetProperty(name).GetInt64();

    private sealed class Fixture(string owner, IConfiguration config, WebApplication app) : IAsyncDisposable
    {
        private readonly string prefix = "AHFS1-" + Guid.NewGuid().ToString("N");
        private readonly List<long> companies = [];
        public long CompanyA, CompanyB, BranchA, BranchB;
        public readonly Dictionary<string, long> Jobs = [];
        public readonly Dictionary<string, (long Driver, long Vehicle)> Pairs = [];
        public long? Branch(string key) => key switch { "A" or "A2" => BranchA, "B" => BranchB, _ => null };

        public static async Task<Fixture> Create()
        {
            foreach (var variable in new[] { "OPSTRAX_TEST_DB", "OPSTRAX_TEST_DB_APP", "OPSTRAX_TEST_DB_SYSTEM" })
                Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)), "Explicit local fixture identities required; no fallback.");
            var owner = new NpgsqlConnectionStringBuilder(TestDb.ConnectionString);
            var runtime = new NpgsqlConnectionStringBuilder(TestDb.AppConnectionString);
            var system = new NpgsqlConnectionStringBuilder(TestDb.SystemConnectionString);
            foreach (var connection in new[] { owner, runtime, system })
            {
                Assert.Equal("127.0.0.1", connection.Host); Assert.Equal(5433, connection.Port);
                Assert.Equal("opstrax_local", connection.Database);
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

        public Task<IResult> Assign(long id, object? driver, object? vehicle, long? branch = null, bool allowed = true, string? actorRole = null,
            Action<int>? onBackendPid = null, CancellationToken ct = default)
            => Call("/api/jobs/{id:long}/assign", id, branch, new() { ["driverId"] = driver, ["vehicleId"] = vehicle }, allowed, actorRole, onBackendPid, ct);
        public Task<IResult> Detail(long id, long? branch) => Call("/api/dispatch/assignments/{id:long}", id, branch);

        private async Task<IResult> Call(string path, long id, long? branch, Dictionary<string, object?>? body = null, bool allowed = true, string? actorRole = null,
            Action<int>? onBackendPid = null, CancellationToken ct = default)
        {
            // A fresh runtime models an independent request/restart, not shared connection state.
            var db = new Database(config, new TenantScopeAccessor());
            return await db.RunInTenantScopeAsync(CompanyA, async () =>
            {
                var identity = await db.QuerySingleAsync("SELECT current_user AS role,opstrax_security.current_tenant_id() AS tenant,pg_backend_pid() AS backend_pid", ct: ct);
                Assert.Equal("opstrax_app", identity!["role"]); Assert.Equal(CompanyA, Convert.ToInt64(identity["tenant"]));
                if (onBackendPid is not null)
                {
                    // RunInTenantScopeAsync pins this signed connection + transaction;
                    // AssignJob reuses it. This is not a separate pooled PID probe.
                    await db.ExecuteAsync("SET LOCAL lock_timeout='10s'; SET LOCAL statement_timeout='15s'", ct: ct);
                    onBackendPid(Convert.ToInt32(identity["backendPid"]));
                }
                var http = new DefaultHttpContext();
                http.Items[EndpointMappings.AuthCompanyIdItemKey] = CompanyA;
                if (branch.HasValue) http.Items[EndpointMappings.AuthBranchIdItemKey] = branch.Value;
                http.Items[EndpointMappings.AuthUserIdItemKey] = 0L;
                http.Items[EndpointMappings.AuthRoleItemKey] = actorRole ?? "Synthetic dispatch operator";
                http.Items[EndpointMappings.AuthPermissionsItemKey] = allowed ? new[] { "dispatch:assign", "dispatch:view" } : new[] { "dispatch:view" };
                var handler = Registered(path);
                var args = handler.Method.GetParameters().Select(p => p.ParameterType == typeof(HttpContext) ? (object)http :
                    p.ParameterType == typeof(long) ? id : p.ParameterType == typeof(Dictionary<string, object?>) ? body! :
                    p.ParameterType == typeof(Database) ? db : p.ParameterType == typeof(AuditService) ? new AuditService(db) :
                    p.ParameterType == typeof(CancellationToken) ? ct : throw new InvalidOperationException("Unexpected registered assignment parameter")).ToArray();
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
                        matches.Add(handler);
            }
            return Assert.Single(matches);
        }
        private static object? Member(object value, string name) => value.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(value)
            ?? value.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(value);

        private async Task<T> Owner<T>(string sql, Func<NpgsqlCommand, Task<T>> execute, params (string Key, object? Value)[] values)
        {
            await using var connection = new NpgsqlConnection(owner); await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            foreach (var (key, value) in values) command.Parameters.AddWithValue(key, value ?? DBNull.Value);
            return await execute(command);
        }
        private Task<string> Text(string sql, params (string, object?)[] values) => Owner(sql, async command => (await command.ExecuteScalarAsync())!.ToString()!, values);
        public async Task<NpgsqlConnection> OpenConcurrencyConnection(CancellationToken ct)
        {
            var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(owner)
                { Pooling = false, Timeout = 5, CommandTimeout = 15 }.ConnectionString);
            try { await connection.OpenAsync(ct); return connection; }
            catch { await connection.DisposeAsync(); throw; }
        }
        public static async Task SetTransactionDeadlines(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
        {
            await using var command = new NpgsqlCommand("SET LOCAL lock_timeout='10s'; SET LOCAL statement_timeout='15s'", connection, transaction);
            await command.ExecuteNonQueryAsync(ct);
        }
        public async Task HoldPriorAssignment(NpgsqlConnection connection, NpgsqlTransaction transaction, long id, CancellationToken ct)
        {
            await using var command = new NpgsqlCommand("SELECT id FROM dispatch_assignments WHERE id=@id AND company_id=@company FOR UPDATE", connection, transaction);
            command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("company", CompanyA);
            Assert.Equal(id, Convert.ToInt64(await command.ExecuteScalarAsync(ct)));
        }
        public async Task<int> ChangeResource(NpgsqlConnection connection, NpgsqlTransaction transaction, string table, long id, bool archive, CancellationToken ct)
        {
            Assert.Contains(table, new[] { "drivers", "vehicles" });
            var change = archive ? "deleted_at=NOW()" : "branch_id=@branch";
            await using var command = new NpgsqlCommand($"UPDATE {table} SET {change} WHERE id=@id AND company_id=@company", connection, transaction);
            command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("company", CompanyA);
            if (!archive) command.Parameters.AddWithValue("branch", BranchB);
            return await command.ExecuteNonQueryAsync(ct);
        }
        public async Task AssertResourceChanged(string table, long id, bool archive)
        {
            Assert.Contains(table, new[] { "drivers", "vehicles" });
            using var row = JsonDocument.Parse(await Text($"SELECT jsonb_build_object('branch_id',branch_id,'deleted_at',deleted_at)::text FROM {table} WHERE id=@id AND company_id=@company", ("id", id), ("company", CompanyA)));
            Assert.Equal(archive ? BranchA : BranchB, NullableId(row.RootElement, "branch_id"));
            Assert.Equal(archive, row.RootElement.GetProperty("deleted_at").ValueKind != JsonValueKind.Null);
        }
        public async Task<string> ObserveBlocking(int waiterPid, int blockerPid, string expectedQuery, CancellationToken ct)
        {
            using var observation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            observation.CancelAfter(TimeSpan.FromSeconds(5));
            await using var connection = await OpenConcurrencyConnection(observation.Token);
            await using var command = new NpgsqlCommand(@"SELECT EXISTS (
                SELECT 1 FROM pg_stat_activity
                WHERE pid=@waiter AND datname=current_database() AND wait_event_type='Lock'
                  AND @blocker=ANY(pg_blocking_pids(pid)) AND POSITION(@query IN query)>0)", connection);
            command.Parameters.AddWithValue("waiter", waiterPid);
            command.Parameters.AddWithValue("blocker", blockerPid);
            command.Parameters.AddWithValue("query", expectedQuery);
            using var poll = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
            while (!(bool)(await command.ExecuteScalarAsync(observation.Token))!)
                await poll.WaitForNextTickAsync(observation.Token);
            // Only an observed DB wait is evidence. The polling interval itself
            // never establishes ordering or counts as proof of blocking.
            return $"Observed PostgreSQL Lock wait: waiter={waiterPid}; blocker={blockerPid}; query contains '{expectedQuery}'.";
        }
        public async Task<string> AssignmentEffectsSnapshot()
        {
            using var snapshot = JsonDocument.Parse(await Snapshot());
            return JsonSerializer.Serialize(snapshot.RootElement.EnumerateObject()
                .Where(property => property.Name is not "drivers" and not "vehicles")
                .ToDictionary(property => property.Name, property => property.Value.Clone()));
        }
        public async Task<JsonElement> Job(long id) => JsonDocument.Parse(await Text("SELECT (to_jsonb(j)||jsonb_build_object('xmin',j.xmin::text))::text FROM jobs j WHERE id=@id AND company_id=@c", ("id", id), ("c", CompanyA))).RootElement.Clone();
        public async Task<JsonElement> ActiveAssignment(long job) => JsonDocument.Parse(await Text("SELECT to_jsonb(a)::text FROM dispatch_assignments a WHERE company_id=@c AND job_id=@job AND assignment_status='assigned'", ("c", CompanyA), ("job", job))).RootElement.Clone();
        public async Task<JsonElement> Assignment(long id) => JsonDocument.Parse(await Text("SELECT to_jsonb(a)::text FROM dispatch_assignments a WHERE company_id=@c AND id=@id", ("c", CompanyA), ("id", id))).RootElement.Clone();
        public async Task<long> AssignmentCount() => long.Parse(await Text("SELECT COUNT(*) FROM dispatch_assignments WHERE company_id=@c", ("c", CompanyA)));
        public async Task AssertEvents(long job, int expected, int? statusEvents = null)
        {
            foreach (var (table, predicate) in new[] { ("audit_logs", "entity_id=@job AND action_name='job.assigned'"), ("entity_timeline_events", "entity_id=@job AND event_type='job.assigned'"), ("job_status_events", "job_id=@job") })
                Assert.Equal(table == "job_status_events" ? statusEvents ?? expected : expected, long.Parse(await Text($"SELECT COUNT(*) FROM {table} WHERE company_id=@c AND {predicate}", ("c", CompanyA), ("job", job))));
        }
        public Task<int> SoftDelete(string table, long id)
        {
            Assert.Contains(table, new[] { "drivers", "vehicles" });
            return Owner($"UPDATE {table} SET deleted_at=NOW() WHERE id=@id AND company_id=@c", command => command.ExecuteNonQueryAsync(), ("id", id), ("c", CompanyA));
        }
        public Task<string> Snapshot() => Text(@"SELECT jsonb_build_object(
            'jobs',(SELECT COALESCE(jsonb_agg(to_jsonb(j)||jsonb_build_object('xmin',j.xmin::text) ORDER BY j.id),'[]') FROM jobs j WHERE company_id=ANY(@ids)),
            'assignments',(SELECT COALESCE(jsonb_agg(to_jsonb(a)||jsonb_build_object('xmin',a.xmin::text) ORDER BY a.id),'[]') FROM dispatch_assignments a WHERE company_id=ANY(@ids)),
            'drivers',(SELECT COALESCE(jsonb_agg(to_jsonb(d)||jsonb_build_object('xmin',d.xmin::text) ORDER BY d.id),'[]') FROM drivers d WHERE company_id=ANY(@ids)),
            'vehicles',(SELECT COALESCE(jsonb_agg(to_jsonb(v)||jsonb_build_object('xmin',v.xmin::text) ORDER BY v.id),'[]') FROM vehicles v WHERE company_id=ANY(@ids)),
            'audit',(SELECT COALESCE(jsonb_agg(to_jsonb(a) ORDER BY a.id),'[]') FROM audit_logs a WHERE company_id=ANY(@ids)),
            'timeline',(SELECT COALESCE(jsonb_agg(to_jsonb(e) ORDER BY e.id),'[]') FROM entity_timeline_events e WHERE company_id=ANY(@ids)),
            'status',(SELECT COALESCE(jsonb_agg(to_jsonb(s) ORDER BY s.id),'[]') FROM job_status_events s WHERE company_id=ANY(@ids)))::text", ("ids", companies.ToArray()));

        private async Task Initialize()
        {
            await using var connection = new NpgsqlConnection(owner); await connection.OpenAsync(); await using var transaction = await connection.BeginTransactionAsync();
            async Task<long> Insert(string sql, params (string, object?)[] values)
            {
                await using var command = new NpgsqlCommand(sql + " RETURNING id", connection, transaction);
                foreach (var (key, value) in values) command.Parameters.AddWithValue(key, value ?? DBNull.Value);
                return Convert.ToInt64(await command.ExecuteScalarAsync());
            }
            async Task<long> Company(string suffix)
            {
                var id = await Insert("INSERT INTO companies(company_code,name,industry) VALUES (@code,'Synthetic S1 dispatch fixture','Transportation')", ("code", prefix + suffix));
                companies.Add(id); return id;
            }
            CompanyA = await Company("A"); CompanyB = await Company("B");
            BranchA = await Insert("INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,'A','Synthetic S1 A','Active')", ("c", CompanyA));
            BranchB = await Insert("INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,'B','Synthetic S1 B','Active')", ("c", CompanyA));
            foreach (var key in new[] { "A", "A2", "B", "NULL", "FOREIGN" })
            {
                var company = key == "FOREIGN" ? CompanyB : CompanyA; var branch = Branch(key);
                var driver = await Insert("INSERT INTO drivers(company_id,branch_id,driver_code,full_name,status,safety_score,readiness_score,compliance_score) VALUES (@c,@b,@code,'Synthetic S1 driver','Available',95,95,95)", ("c", company), ("b", branch), ("code", key));
                var vehicle = await Insert("INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status,availability_status,out_of_service,readiness_score,risk_score) VALUES (@c,@b,@code,'Truck','legacy-fleet-identifier',@alt,'Available','available',false,95,5)", ("c", company), ("b", branch), ("code", key), ("alt", prefix + key));
                await Insert("INSERT INTO hos_records(company_id,driver_id,shift_date,remaining_drive_hours,remaining_shift_hours,hos_status) VALUES (@c,@d,CURRENT_DATE,8,8,'On Duty')", ("c", company), ("d", driver));
                var job = await Insert("INSERT INTO jobs(company_id,branch_id,job_code,job_type,status,required_vehicle_type) VALUES (@c,@b,@code,'Delivery','Unassigned','Truck')", ("c", company), ("b", branch), ("code", prefix + key));
                Pairs[key] = (driver, vehicle); Jobs[key] = job;
            }
            await transaction.CommitAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await app.DisposeAsync(); if (companies.Count == 0) return;
            await using var connection = new NpgsqlConnection(owner); await connection.OpenAsync(); await using var transaction = await connection.BeginTransactionAsync();
            foreach (var table in new[] { "job_status_events", "entity_timeline_events", "audit_logs", "dispatch_assignments", "jobs", "hos_records", "drivers", "vehicles", "branches" })
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
