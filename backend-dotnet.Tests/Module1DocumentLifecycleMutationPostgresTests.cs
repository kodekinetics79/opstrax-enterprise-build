using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Npgsql;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;
using Opstrax.Api.Storage;

namespace Opstrax.Tests;

// Frozen TRUTH012 decision: actual registered delegates and restricted signed DB
// scopes, but synthetic AuthItems (NOT full HTTP authentication or browser evidence).
// No TestDb fallback. Owner is used only for uniquely marked fixture setup/cleanup.
[Collection("fleet-identity-schema")]
[Trait("Category", "Integration")]
public sealed class Module1DocumentLifecycleMutationPostgresTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Automatic_CreateAndExpiryCorrection_PersistPolicyAndVersion_WithoutChangingNumber()
    {
        await using var f = await Fixture.Create();
        var t = DateOnly.FromDateTime(DateTime.UtcNow);
        var created = await f.Call("/api/documents", new { title = "Synthetic expiry policy - NOT COMPLIANCE", documentType = "Synthetic",
            entityType = "vehicle", entityId = f.VehicleA, documentNumber = "W1-EXPLICIT-NUMBER", expiresAt = t.AddDays(-1).ToString("yyyy-MM-dd") });
        Assert.Equal(201, Status(created));
        var id = Value(created).GetProperty("data").GetProperty("id").GetInt64();
        var initial = await f.Row(id);
        Assert.Equal("automatic", initial.GetProperty("lifecycle_mode").GetString());
        Assert.Equal("Expired", initial.GetProperty("status").GetString());
        Assert.Equal(90m, initial.GetProperty("risk_score").GetDecimal());
        Assert.Equal("W1-EXPLICIT-NUMBER", initial.GetProperty("document_number").GetString());
        var result = await f.Call("/api/documents/{id:long}", new { expectedVersion = Version(initial), expiresAt = t.AddDays(45).ToString("yyyy-MM-dd") }, id);
        Assert.Equal(200, Status(result));
        var after = await f.Row(id);
        Assert.Equal("Active", after.GetProperty("status").GetString());
        Assert.Equal(25m, after.GetProperty("risk_score").GetDecimal());
        Assert.Equal("Current", after.GetProperty("renewal_status").GetString());
        Assert.Equal("W1-EXPLICIT-NUMBER", after.GetProperty("document_number").GetString());
        Assert.NotEqual(Version(initial), Version(after)); // Separate committed requests, not arithmetic ordering.
        await f.AssertEvents(id, 2);
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("legacy_unknown")]
    public async Task DateEdit_PreservesRecordedNonAutomaticTupleAndFile(string mode)
    {
        await using var f = await Fixture.Create();
        var id = mode == "manual" ? f.ManualDoc : f.LegacyDoc;
        var before = await f.Row(id);
        var result = await f.Call("/api/documents/{id:long}", new { expectedVersion = Version(before), expiresAt = "2099-12-31",
            fileUrl = "objkey:foreign/forbidden", file_url = "objkey:foreign/also-forbidden" }, id);
        Assert.Equal(200, Status(result));
        var after = await f.Row(id);
        foreach (var key in new[] { "status", "risk_score", "renewal_status", "recommended_action", "lifecycle_mode", "lifecycle_assessed_on", "file_url", "document_number" })
            Assert.Equal(before.GetProperty(key).GetRawText(), after.GetProperty(key).GetRawText());
        Assert.Equal("2099-12-31", after.GetProperty("expires_at").GetString());
        await f.AssertEvents(id, 1);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    public async Task OrdinaryBlankDates_PreserveDatesAndAutomaticAssessment(string rawDate)
    {
        await using var f = await Fixture.Create();
        var before = await f.Row(f.DocA);
        var json = "{\"expectedVersion\":\"" + Version(before) + "\",\"issuedAt\":" + rawDate + ",\"expiresAt\":" + rawDate + ",\"notes\":\"Synthetic metadata update\"}";
        Assert.Equal(200, Status(await f.Raw("/api/documents/{id:long}", json, f.DocA)));
        var after = await f.Row(f.DocA);
        foreach (var key in new[] { "issued_at", "expires_at", "status", "risk_score", "renewal_status", "recommended_action", "lifecycle_assessed_on", "document_number" })
            Assert.Equal(before.GetProperty(key).GetRawText(), after.GetProperty(key).GetRawText());
        Assert.Equal("Synthetic metadata update", after.GetProperty("notes").GetString());
    }

    [Fact]
    public async Task MissingMalformedDuplicateAndInvalidEffectiveDates_HaveNoPartialPersistence()
    {
        await using var f = await Fixture.Create();
        var before = await f.Snapshot();
        var version = Version(await f.Row(f.DocA));
        foreach (var (json, status) in new[]
        {
            ("{\"notes\":\"missing token\"}", 428),
            ("{\"expectedVersion\":1}", 400),
            ("{\"expectedVersion\":\"" + version + "\",\"expectedVersion\":\"" + version + "\"}", 400),
            ("{\"expected_version\":\"" + version + "\"}", 400),
            ("{\"expectedVersion\":\"" + version + "\",\"expiresAt\":\"2000-01-01\"}", 400),
            ("{\"expectedVersion\":\"" + version + "\",\"issuedAt\":\"2099-01-01\"}", 400),
            ("{\"expectedVersion\":\"" + version + "\",\"expiresAt\":\"2026-02-30\"}", 400),
            ("{\"expectedVersion\":\"" + version + "\",\"status\":\"Expired\"}", 400)
        })
        {
            Assert.Equal(status, Status(await f.Raw("/api/documents/{id:long}", json, f.DocA)));
            Assert.Equal(before, await f.Snapshot());
        }
    }

    [Fact]
    public async Task TwoRequestsWithSameToken_OneCommitsOtherConflicts_NoDuplicateEvents()
    {
        await using var f = await Fixture.Create();
        var token = Version(await f.Row(f.DocA));
        var results = await Task.WhenAll(
            f.Call("/api/documents/{id:long}", new { expectedVersion = token, notes = "Synthetic writer A" }, f.DocA),
            f.Call("/api/documents/{id:long}", new { expectedVersion = token, notes = "Synthetic writer B" }, f.DocA));
        Assert.Equal(new[] { 200, 409 }, results.Select(Status).Order().ToArray());
        await f.AssertEvents(f.DocA, 1);
        var committed = await f.Snapshot();
        Assert.Equal(409, Status(await f.Call("/api/documents/{id:long}", new { expectedVersion = token, notes = "Stale replay" }, f.DocA)));
        Assert.Equal(committed, await f.Snapshot());
    }

    [Fact]
    public async Task Renew_QueuesWithoutChangingRiskOrFile_StaleReplayDenied_MetadataKeepsQueue()
    {
        await using var f = await Fixture.Create();
        var before = await f.Row(f.DocA);
        var payload = new { expectedVersion = Version(before) };
        Assert.Equal(200, Status(await f.Call("/api/documents/{id:long}/renew", payload, f.DocA)));
        var queued = await f.Row(f.DocA);
        Assert.Equal("manual", queued.GetProperty("lifecycle_mode").GetString());
        Assert.Equal(JsonValueKind.Null, queued.GetProperty("lifecycle_assessed_on").ValueKind);
        Assert.Equal("Renewal Queued", queued.GetProperty("renewal_status").GetString());
        foreach (var key in new[] { "risk_score", "expires_at", "issued_at", "file_url" })
            Assert.Equal(before.GetProperty(key).GetRawText(), queued.GetProperty(key).GetRawText());
        var state = await f.Snapshot();
        Assert.Equal(409, Status(await f.Call("/api/documents/{id:long}/renew", payload, f.DocA)));
        Assert.Equal(state, await f.Snapshot());
        Assert.Equal(200, Status(await f.Call("/api/documents/{id:long}", new { expectedVersion = Version(queued), expiresAt = "2099-12-31" }, f.DocA)));
        Assert.Equal("Renewal Queued", (await f.Row(f.DocA)).GetProperty("renewal_status").GetString());
        await f.AssertEvents(f.DocA, 2);
    }

    [Fact]
    public async Task TypedBranchCompanyNullOwnerAndPermission_DenyWithoutVersionDisclosureOrWrites()
    {
        await using var f = await Fixture.Create();
        var before = await f.Snapshot();
        foreach (var id in new[] { f.DocB, f.NullBranchDoc, f.ForeignDoc })
            Assert.Equal(404, Status(await f.Call("/api/documents/{id:long}", new { notes = "No token on inaccessible record" }, id)));
        Assert.Equal(403, Status(await f.Call("/api/documents/{id:long}", new { expectedVersion = "1" }, f.DocA, allowed: false)));
        var token = Version(await f.Row(f.DocA));
        Assert.Equal(404, Status(await f.Call("/api/documents/{id:long}", new { expectedVersion = token, entityType = "vehicle", entityId = f.VehicleB }, f.DocA)));
        Assert.Equal(before, await f.Snapshot());
    }

    [Fact]
    public async Task FreshWriteBarrier_CannotMintTenantAuthorityFromMissingOrMismatchedScope()
    {
        await using var f = await Fixture.Create();
        var before = await f.Snapshot();
        var body = JsonSerializer.Serialize(new { expectedVersion = Version(await f.Row(f.DocA)), notes = "Must not write" });
        await ExpectClosed(() => f.Raw("/api/documents/{id:long}", body, f.DocA, signedCompany: f.CompanyB));
        await ExpectClosed(() => f.Raw("/api/documents/{id:long}", body, f.DocA, noScope: true));
        Assert.Equal(before, await f.Snapshot());
    }

    [Fact]
    public async Task RealAuditFailure_RollsBackBusinessAndTimeline_EvenWhenOuterScopeCompletes()
    {
        await using var f = await Fixture.Create();
        var before = await f.Snapshot();
        var token = Version(await f.Row(f.DocA));
        // Oversized synthetic actor exceeds canonical actor_name VARCHAR(160),
        // forcing the real AuditService INSERT to fail (no mock or disabled constraint).
        await f.AuditFailureWithinCompletingOuterScope(token);
        Assert.Equal(before, await f.Snapshot());
    }

    [Fact]
    public async Task CanonicalReadViews_AgreeOnAdditiveAssessment_PreserveStoredZeroNull_AndDoNotWrite()
    {
        await using var f = await Fixture.Create();
        var before = await f.Snapshot();
        var list = Value(await f.Read("/api/documents")).GetProperty("data").EnumerateArray().ToArray();
        var compliance = Value(await f.Read("/api/compliance/documents")).GetProperty("data").EnumerateArray().ToArray();
        var expiring = Value(await f.Read("/api/documents/expiring")).GetProperty("data").EnumerateArray().ToArray();
        foreach (var id in new[] { f.DocA, f.ManualDoc, f.LegacyDoc, f.UnknownDoc })
        {
            var record = Value(await f.Read("/api/documents/{id:long}", id)).GetProperty("data").GetProperty("record");
            var listRow = Assert.Single(list, row => row.GetProperty("id").GetInt64() == id);
            var complianceRow = Assert.Single(compliance, row => row.GetProperty("id").GetInt64() == id);
            foreach (var key in new[] { "status", "riskScore", "documentExpiryRiskScore", "renewalStatus", "recommendedAction", "lifecycleMode", "lifecycleAssessedOn", "rowVersion", "currentDateAssessment" })
            {
                Assert.Equal(record.GetProperty(key).GetRawText(), listRow.GetProperty(key).GetRawText());
                Assert.Equal(record.GetProperty(key).GetRawText(), complianceRow.GetProperty(key).GetRawText());
            }
            if (id == f.UnknownDoc)
            {
                Assert.Equal(JsonValueKind.Null, record.GetProperty("riskScore").ValueKind);
                Assert.Equal(JsonValueKind.Null, record.GetProperty("currentDateAssessment").GetProperty("riskScore").ValueKind);
                Assert.Equal("Unknown", record.GetProperty("currentDateAssessment").GetProperty("status").GetString());
                Assert.DoesNotContain(expiring, row => row.GetProperty("id").GetInt64() == id);
            }
            else
            {
                var expiringRow = Assert.Single(expiring, row => row.GetProperty("id").GetInt64() == id);
                Assert.Equal(record.GetProperty("currentDateAssessment").GetRawText(), expiringRow.GetProperty("currentDateAssessment").GetRawText());
            }
            if (id == f.ManualDoc)
            {
                Assert.Equal(0m, record.GetProperty("riskScore").GetDecimal());
                Assert.Equal(90m, record.GetProperty("currentDateAssessment").GetProperty("riskScore").GetDecimal());
            }
        }
        Assert.Equal(before, await f.Snapshot());
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("null", null)]
    [InlineData("null,null", null)]
    [InlineData("null,7.25", null)]
    [InlineData("null,0", null)]
    [InlineData("0", "100.0%")]
    [InlineData("7.25", "92.8%")]
    [InlineData("95", "5.0%")]
    [InlineData("100", "5.0%")]
    [InlineData("0,7.25,100", "65.9%")]
    public async Task DocumentSummaryRiskIndicator_RequiresCompleteVisiblePopulation_AndReadDoesNotMutate(
        string riskValues, string? expectedIndicator)
    {
        await using var f = await Fixture.Create();
        var risks = riskValues.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value == "null" ? (decimal?)null : decimal.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        await f.PrepareSummaryRisks(risks);
        var before = await f.Snapshot();

        // Exercise the real registered summary with the existing signed, restricted
        // database role. Synthetic AuthItems are not an HTTP authentication claim.
        var summary = Value(await f.Read("/api/documents/summary")).GetProperty("data");
        Assert.Equal(before, await f.Snapshot()); // Includes row versions, audit and timeline.
        Assert.Equal(risks.Length, summary.GetProperty("totalDocuments").GetInt32());
        foreach (var (key, expected) in new[]
        {
            ("expiringSoon", 0), ("expired", 0),
            ("missingCriticalDocuments", risks.Count(risk => risk >= 80m)),
            ("vehicleDocuments", risks.Length), ("driverDocuments", 0),
            ("complianceDocuments", 0), ("pendingRenewal", 0),
            ("uploadedThisMonth", risks.Length), ("auditPackageDocuments", 0),
            ("crossBorderMissingDocs", 0)
        })
        {
            // Preserve the existing empty-SUM contract; this defect changes only
            // the risk-derived indicator, not unrelated summary aggregates.
            if (risks.Length == 0) Assert.Equal(JsonValueKind.Null, summary.GetProperty(key).ValueKind);
            else Assert.Equal(expected, summary.GetProperty(key).GetInt32());
        }
        if (expectedIndicator is null)
            Assert.Equal(JsonValueKind.Null, summary.GetProperty("dataCompletenessScore").ValueKind);
        else
            Assert.Equal(expectedIndicator, summary.GetProperty("dataCompletenessScore").GetString());
    }

    [Fact]
    public async Task ExplicitManualOverride_AuditsActualPersistedNumericPrecision_AndClearsAssessment()
    {
        await using var f = await Fixture.Create();
        var before = await f.Row(f.DocA);
        Assert.Equal(200, Status(await f.Call("/api/documents/{id:long}", new
        {
            expectedVersion = Version(before), lifecycleIntent = "manual", lifecycleReason = "Synthetic explicit precision check",
            status = "Unknown", riskScore = "0.125", renewalStatus = "Current", recommendedAction = "Synthetic recorded workflow"
        }, f.DocA)));
        var after = await f.Row(f.DocA);
        Assert.Equal("manual", after.GetProperty("lifecycle_mode").GetString());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("lifecycle_assessed_on").ValueKind);
        var audit = await f.LastAudit(f.DocA);
        Assert.Equal(after.GetProperty("risk_score").GetDecimal(), audit.GetProperty("newSnapshot").GetProperty("RiskScore").GetDecimal());
        Assert.Equal(Version(before), audit.GetProperty("oldVersion").GetString());
        Assert.Equal(Version(after), audit.GetProperty("returnedVersion").GetString());
        Assert.Equal("manual", audit.GetProperty("lifecycleIntent").GetString());
        Assert.Equal("Synthetic explicit precision check", audit.GetProperty("reason").GetString());
        await f.AssertEvents(f.DocA, 1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Reassignment_WaitsForOldAndNewOwnerMoves_ThenFailsClosed(bool moveOldOwner)
    {
        await using var f = await Fixture.Create();
        var before = await f.Row(f.DocA);
        var target = moveOldOwner ? f.VehicleA : f.VehicleA2;
        var locked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ownerMove = f.HoldOwnerMove(target, locked, release);
        await locked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var edit = f.Call("/api/documents/{id:long}", new { expectedVersion = Version(before), entityType = "vehicle", entityId = f.VehicleA2 }, f.DocA);
        await Task.Delay(150);
        Assert.False(edit.IsCompleted, "Document reassignment must wait for the competing owner movement lock.");
        release.SetResult();
        await ownerMove.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(404, Status(await edit.WaitAsync(TimeSpan.FromSeconds(5))));
        Assert.Equal(before.GetRawText(), (await f.Row(f.DocA)).GetRawText());
        await f.AssertEvents(f.DocA, 0);
    }

    [Fact]
    public async Task TwoConnectionPool_ConcurrentOuterRequestsCancelBoundedly_RestorePool_ThenHealthyWriteCommits()
    {
        await using var f = await Fixture.Create(maxAppPoolSize: 2);
        var token = Version(await f.Row(f.DocA));
        var allOuterScopes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = 0;
        async Task Gate()
        {
            if (Interlocked.Increment(ref arrived) == 2) allOuterScopes.SetResult();
            await allOuterScopes.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await release.Task;
        }
        using var bounded = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var first = f.Call("/api/documents/{id:long}", new { expectedVersion = token, notes = "Pool contender A" }, f.DocA,
            ct: bounded.Token, afterOuterStarted: Gate);
        var second = f.Call("/api/documents/{id:long}", new { expectedVersion = token, notes = "Pool contender B" }, f.DocA,
            ct: bounded.Token, afterOuterStarted: Gate);
        await allOuterScopes.Task.WaitAsync(TimeSpan.FromSeconds(5));
        release.SetResult();
        var failures = await Task.WhenAll(new[] { first, second }.Select(async task => await Record.ExceptionAsync(() => task)));
        Assert.All(failures, error => Assert.True(error is OperationCanceledException,
            $"Expected bounded pool cancellation, got {error?.GetType().Name ?? "success"}."));
        var unchanged = await f.Row(f.DocA);
        Assert.Equal(token, Version(unchanged));
        await f.AssertEvents(f.DocA, 0);
        // A fresh request uses exactly the outer+inner pair and proves no scope/lease leaked.
        var healthy = await f.Call("/api/documents/{id:long}", new { expectedVersion = token, notes = "Healthy after bounded pool cancellation" }, f.DocA);
        Assert.Equal(200, Status(healthy));
        Assert.Equal("Healthy after bounded pool cancellation", (await f.Row(f.DocA)).GetProperty("notes").GetString());
        await f.AssertEvents(f.DocA, 1);
    }

    [Fact]
    public async Task RealLocalUpload_ConfirmedAuditRollback_DeletesOnlyNewObjectAndRollsBackRows()
    {
        await using var f = await Fixture.Create();
        var before = await f.Snapshot();
        var result = await f.UploadWithForcedAuditFailure();
        using var storageCleanup = result.Store;
        Assert.Equal("22001", result.Error.SqlState);
        Assert.False(string.IsNullOrWhiteSpace(result.Store.PutKey));
        Assert.Equal(result.Store.PutKey, result.Store.DeleteKey);
        await Assert.ThrowsAsync<FileNotFoundException>(() => result.Store.GetAsync(result.Store.PutKey!));
        Assert.Equal(before, await f.Snapshot());
    }

    [Fact]
    public async Task RealLocalFile_UncertainCommit_RetainsObjectSkipsCompensationAndRollsBackObservedRow()
    {
        await using var f = await Fixture.Create();
        var before = await f.Snapshot();
        var result = await f.ForceCommitUncertaintyWithLocalFile();
        using var storageCleanup = result.Store;
        Assert.IsType<DocumentTransactionUncertainException>(result.Error);
        Assert.False(result.CompensationCalled);
        Assert.False(string.IsNullOrWhiteSpace(result.Store.PutKey));
        Assert.Null(result.Store.DeleteKey);
        await using (var retained = await result.Store.GetAsync(result.Store.PutKey!))
        using (var reader = new StreamReader(retained))
            Assert.Equal("Synthetic uncertainty bytes", await reader.ReadToEndAsync());
        Assert.Equal(before, await f.Snapshot());
        await result.Store.DeleteAsync(result.Store.PutKey!); // exact synthetic reconciliation cleanup
        Assert.Equal(result.Store.PutKey, result.Store.DeleteKey);
    }

    private static async Task ExpectClosed(Func<Task<IResult>> call)
    {
        try { Assert.Contains(Status(await call()), new[] { 400, 403, 404 }); }
        catch (InvalidOperationException error)
        {
            // Do not accidentally accept an unrelated test-harness parameter failure.
            Assert.Contains("tenant", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(nameof(Database), error.StackTrace ?? "");
        }
    }
    private static int Status(IResult result) => ((IStatusCodeHttpResult)result).StatusCode ?? 200;
    private static JsonElement Value(IResult result) => JsonSerializer.SerializeToElement(((IValueHttpResult)result).Value, JsonOptions);
    private static string Version(JsonElement row) => row.GetProperty("row_version").GetString()!;

    private sealed class Fixture(string owner, Database db, WebApplication app) : IAsyncDisposable
    {
        public long CompanyA, CompanyB, BranchA, BranchB, VehicleA, VehicleA2, VehicleB, DocA, DocB, NullBranchDoc, ForeignDoc, ManualDoc, LegacyDoc, UnknownDoc;
        private readonly string prefix = "W1LIFE-" + Guid.NewGuid().ToString("N");
        private readonly List<long> companies = [];

        public static async Task<Fixture> Create(int? maxAppPoolSize = null)
        {
            Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPSTRAX_TEST_DB")), "Explicit disposable local DB required; no fallback.");
            var owner = new NpgsqlConnectionStringBuilder(TestDb.ConnectionString);
            var runtime = new NpgsqlConnectionStringBuilder(TestDb.AppConnectionString);
            var system = new NpgsqlConnectionStringBuilder(TestDb.SystemConnectionString);
            foreach (var c in new[] { owner, runtime, system })
            {
                Assert.Contains(c.Host, new[] { "127.0.0.1", "localhost", "::1" });
                Assert.Equal(owner.Host, c.Host); Assert.Equal(owner.Port, c.Port); Assert.Equal(owner.Database, c.Database);
            }
            Assert.Equal("opstrax_app", runtime.Username); Assert.Equal("opstrax_system", system.Username);
            if (maxAppPoolSize.HasValue) runtime.MaxPoolSize = maxAppPoolSize.Value;
            Assert.DoesNotContain(owner.Username, new[] { "opstrax_app", "opstrax_system" });
            Assert.True(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PG_CONNECTION_REPLICA")));
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ASPNETCORE_ENVIRONMENT"] = "Staging", ["Rls:EnforceTenantContext"] = "true", ["ConnectionStrings:DefaultConnection"] = runtime.ConnectionString,
                ["ConnectionStrings:SystemConnection"] = system.ConnectionString }).Build();
            var db = new Database(config, new TenantScopeAccessor());
            await db.ValidateProductionIdentitiesAsync();
            var app = WebApplication.CreateBuilder().Build(); app.MapOpsTraxEndpoints();
            var f = new Fixture(owner.ConnectionString, db, app);
            try { await f.Initialize(); return f; } catch { await f.DisposeAsync(); throw; }
        }

        public Task<IResult> Call(string path, object body, long id = 0, bool allowed = true, string? actorRole = null,
            CancellationToken ct = default, Func<Task>? afterOuterStarted = null)
            => Raw(path, JsonSerializer.Serialize(body), id, allowed, actorRole, ct: ct, afterOuterStarted: afterOuterStarted);

        public async Task<IResult> Raw(string path, string body, long id = 0, bool allowed = true, string? actorRole = null,
            long? signedCompany = null, bool noScope = false, CancellationToken ct = default, Func<Task>? afterOuterStarted = null)
        {
            async Task<IResult> Execute()
            {
                var http = new DefaultHttpContext();
                http.Items[EndpointMappings.AuthCompanyIdItemKey] = CompanyA;
                http.Items[EndpointMappings.AuthBranchIdItemKey] = BranchA;
                http.Items[EndpointMappings.AuthUserIdItemKey] = 0L;
                http.Items[EndpointMappings.AuthRoleItemKey] = actorRole ?? "Synthetic lifecycle manager";
                http.Items[EndpointMappings.AuthPermissionsItemKey] = allowed ? new[] { "compliance:view", "compliance:manage" } : new[] { "compliance:view" };
                using var json = JsonDocument.Parse(body);
                var handler = Registered(path);
                var args = handler.Method.GetParameters().Select(p => p.ParameterType == typeof(HttpContext) ? (object)http :
                    p.ParameterType == typeof(long) ? id : p.ParameterType == typeof(JsonElement) ? json.RootElement :
                    p.ParameterType == typeof(Database) ? db : p.ParameterType == typeof(AuditService) ? new AuditService(db) :
                    p.ParameterType == typeof(CancellationToken) ? ct : throw new InvalidOperationException("Unexpected registered handler parameter")).ToArray();
                try { return await (Task<IResult>)handler.DynamicInvoke(args)!; }
                catch (TargetInvocationException e) when (e.InnerException is not null)
                { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
            }
            if (noScope) return await Execute();
            return await db.RunInTenantScopeAsync(signedCompany ?? CompanyA, async () =>
            {
                if (afterOuterStarted is not null) await afterOuterStarted();
                return await Execute();
            }, ct);
        }

        public Task<IResult> Read(string path, long id = 0) => db.RunInTenantScopeAsync(CompanyA, async () =>
        {
            var http = new DefaultHttpContext();
            http.Items[EndpointMappings.AuthCompanyIdItemKey] = CompanyA;
            http.Items[EndpointMappings.AuthBranchIdItemKey] = BranchA;
            http.Items[EndpointMappings.AuthUserIdItemKey] = 0L;
            http.Items[EndpointMappings.AuthRoleItemKey] = "Synthetic lifecycle reader";
            http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "compliance:view" };
            var handler = Registered(path, mutation: false);
            var args = handler.Method.GetParameters().Select(p => p.ParameterType == typeof(HttpContext) ? (object)http :
                p.ParameterType == typeof(long) ? id : p.ParameterType == typeof(Database) ? db :
                p.ParameterType == typeof(CancellationToken) ? CancellationToken.None : throw new InvalidOperationException("Unexpected registered read parameter")).ToArray();
            var result = await (Task<IResult>)handler.DynamicInvoke(args)!;
            Assert.Equal(200, Status(result)); return result;
        });

        private Delegate Registered(string path, bool mutation = true)
        {
            var matches = new List<Delegate>();
            foreach (var source in ((IEndpointRouteBuilder)app).DataSources)
            {
                if (source.GetType().GetField("_routeEntries", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(source) is not IEnumerable entries) continue;
                foreach (var entry in entries)
                    if (entry is not null && Member(entry, "RoutePattern") is RoutePattern pattern && pattern.RawText == path
                        && Member(entry, "RouteHandler") is Delegate handler
                        && (mutation ? handler.Method.GetParameters().Any(p => p.ParameterType == typeof(JsonElement))
                            : !handler.Method.GetParameters().Any(p => p.ParameterType == typeof(JsonElement) || p.ParameterType == typeof(AuditService)))) matches.Add(handler);
            }
            var match = Assert.Single(matches);
            Assert.Equal(typeof(EndpointMappings).Assembly, match.Method.Module.Assembly);
            return match;
        }
        private static object? Member(object value, string name) => value.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(value)
            ?? value.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(value);

        public Task<JsonElement> Row(long id) => db.RunInTenantScopeAsync(CompanyA, async () =>
        {
            var identity = await db.QuerySingleAsync("SELECT current_user AS role,opstrax_security.current_tenant_id() AS tenant");
            Assert.Equal("opstrax_app", identity!["role"]?.ToString()); Assert.Equal(CompanyA, Convert.ToInt64(identity["tenant"]));
            var row = await db.QuerySingleAsync("SELECT (to_jsonb(d)||jsonb_build_object('row_version',d.xmin::text))::text AS payload FROM documents d WHERE id=@id", c => c.Parameters.AddWithValue("id", id));
            return JsonDocument.Parse(row!["payload"]!.ToString()!).RootElement.Clone();
        });

        public Task<string> Snapshot() => db.RunInSystemScopeAsync(async () =>
        {
            var state = await db.QuerySingleAsync(@"SELECT jsonb_build_object(
                'documents',(SELECT COALESCE(jsonb_agg(to_jsonb(d)||jsonb_build_object('xmin',d.xmin::text) ORDER BY d.id),'[]') FROM documents d WHERE company_id=ANY(@ids)),
                'audit',(SELECT COALESCE(jsonb_agg(to_jsonb(a) ORDER BY a.id),'[]') FROM audit_logs a WHERE company_id=ANY(@ids) AND entity_name='Document'),
                'timeline',(SELECT COALESCE(jsonb_agg(to_jsonb(e) ORDER BY e.id),'[]') FROM document_timeline_events e WHERE company_id=ANY(@ids)))::text AS payload",
                c => c.Parameters.AddWithValue("ids", companies.ToArray()));
            return state!["payload"]!.ToString()!;
        });

        public async Task PrepareSummaryRisks(decimal?[] risks)
        {
            // Setup only: preserve adversarial branch/tenant/deleted documents in
            // the unique fixture, without changing schema or any shared tenant.
            await using var c = new NpgsqlConnection(owner);
            await c.OpenAsync();
            await using var tx = await c.BeginTransactionAsync();
            await using (var retire = new NpgsqlCommand(@"UPDATE documents SET deleted_at=NOW(),risk_score=NULL
                WHERE company_id=@company AND id=ANY(@ids)", c, tx))
            {
                retire.Parameters.AddWithValue("company", CompanyA);
                retire.Parameters.AddWithValue("ids", new[] { DocA, ManualDoc, LegacyDoc, UnknownDoc });
                Assert.Equal(4, await retire.ExecuteNonQueryAsync());
            }
            await using (var outsiders = new NpgsqlCommand(@"UPDATE documents SET risk_score=NULL
                WHERE company_id=ANY(@companies) AND id=ANY(@ids)", c, tx))
            {
                outsiders.Parameters.AddWithValue("companies", companies.ToArray());
                outsiders.Parameters.AddWithValue("ids", new[] { DocB, NullBranchDoc, ForeignDoc });
                Assert.Equal(3, await outsiders.ExecuteNonQueryAsync());
            }
            for (var index = 0; index < risks.Length; index++)
            {
                await using var insert = new NpgsqlCommand(@"INSERT INTO documents
                    (company_id,title,document_number,document_type,category,entity_type,entity_id,
                     issued_at,expires_at,status,risk_score,renewal_status,recommended_action,lifecycle_mode,country_code)
                    VALUES (@company,'Synthetic summary risk - NOT COMPLIANCE',@number,'Synthetic','Synthetic',
                        'vehicle',@vehicle,@today,@expiry,'Active',@risk,'Current','Synthetic summary only','legacy_unknown','US')", c, tx);
                insert.Parameters.AddWithValue("company", CompanyA);
                insert.Parameters.AddWithValue("vehicle", VehicleA);
                insert.Parameters.AddWithValue("number", prefix + "-SUMMARY-" + index);
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                insert.Parameters.AddWithValue("today", today);
                insert.Parameters.AddWithValue("expiry", today.AddDays(120));
                insert.Parameters.Add(new NpgsqlParameter("risk", NpgsqlTypes.NpgsqlDbType.Numeric)
                { Value = (object?)risks[index] ?? DBNull.Value });
                Assert.Equal(1, await insert.ExecuteNonQueryAsync());
            }
            await tx.CommitAsync();
        }

        public Task AssertEvents(long id, int expected) => db.RunInTenantScopeAsync(CompanyA, async () =>
        {
            var counts = await db.QuerySingleAsync("SELECT (SELECT count(*) FROM audit_logs WHERE company_id=@c AND entity_name='Document' AND entity_id=@id) AS audits,(SELECT count(*) FROM document_timeline_events WHERE company_id=@c AND document_id=@id) AS events", c => { c.Parameters.AddWithValue("c", CompanyA); c.Parameters.AddWithValue("id", id); });
            Assert.Equal(expected, Convert.ToInt32(counts!["audits"])); Assert.Equal(expected, Convert.ToInt32(counts["events"]));
            return true;
        });

        public Task<JsonElement> LastAudit(long id) => db.RunInTenantScopeAsync(CompanyA, async () =>
        {
            var row = await db.QuerySingleAsync("SELECT details_json::text AS payload FROM audit_logs WHERE company_id=@c AND entity_name='Document' AND entity_id=@id ORDER BY id DESC LIMIT 1",
                c => { c.Parameters.AddWithValue("c", CompanyA); c.Parameters.AddWithValue("id", id); });
            return JsonDocument.Parse(row!["payload"]!.ToString()!).RootElement.Clone();
        });

        public Task HoldOwnerMove(long vehicleId, TaskCompletionSource locked, TaskCompletionSource release)
            => db.RunInTenantScopeAsync(CompanyA, async () =>
            {
                Assert.Equal(1, await db.ExecuteAsync("UPDATE vehicles SET branch_id=@branch WHERE id=@id AND company_id=@company",
                    c => { c.Parameters.AddWithValue("branch", BranchB); c.Parameters.AddWithValue("id", vehicleId); c.Parameters.AddWithValue("company", CompanyA); }));
                locked.SetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(5));
                return true; // Commit the branch movement, forcing the waiting route to recheck and deny.
            });

        public async Task<(PostgresException Error, RecordingLocalStore Store)> UploadWithForcedAuditFailure()
        {
            var store = RecordingLocalStore.Create();
            try
            {
                var files = new FileStorageService(store, NullLogger<FileStorageService>.Instance);
                var http = Principal(new string('x', 10000));
                var bytes = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Synthetic rollback bytes"));
                var file = new FormFile(bytes, 0, bytes.Length, "file", "synthetic-rollback.txt") { Headers = new HeaderDictionary(), ContentType = "text/plain" };
                var fields = new Dictionary<string, StringValues>
                {
                    ["title"] = "Synthetic rollback upload - NOT COMPLIANCE", ["documentType"] = "Synthetic",
                    ["category"] = "Synthetic", ["entityType"] = "vehicle", ["entityId"] = VehicleA.ToString()
                };
                http.Request.ContentType = "multipart/form-data; boundary=w1synthetic";
                http.Features.Set<IFormFeature>(new StaticFormFeature(new FormCollection(fields, new FormFileCollection { file })));
                var handler = RegisteredUpload();
                var error = await Assert.ThrowsAsync<PostgresException>(() => db.RunInTenantScopeAsync(CompanyA, async () =>
                {
                    try { return await (Task<IResult>)handler.DynamicInvoke(http, files, db, new AuditService(db), CancellationToken.None)!; }
                    catch (TargetInvocationException e) when (e.InnerException is not null)
                    { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
                }));
                return (error, store);
            }
            catch { store.Dispose(); throw; }
        }

        public async Task<(Exception Error, bool CompensationCalled, RecordingLocalStore Store)> ForceCommitUncertaintyWithLocalFile()
        {
            var store = RecordingLocalStore.Create();
            var files = new FileStorageService(store, NullLogger<FileStorageService>.Instance);
            FileStorageService.UploadResult? uploaded = null;
            var compensation = false;
            var pidReady = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                var transaction = db.RunInTenantScopeAsync(CompanyA, () => db.RunInDocumentTransactionAsync(CompanyA, async () =>
                {
                    await using var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Synthetic uncertainty bytes"));
                    uploaded = await files.UploadAsync(CompanyA, "documents", "synthetic-uncertain.txt", "text/plain", content);
                    var pid = await db.QuerySingleAsync("SELECT pg_backend_pid() AS pid");
                    await db.ExecuteAsync(@"INSERT INTO documents(company_id,title,document_number,document_type,entity_type,entity_id,status,renewal_status,risk_score,recommended_action,lifecycle_mode)
                        VALUES (@c,'Synthetic uncertain commit - NOT COMPLIANCE','W1-UNCERTAIN','Synthetic','vehicle',@v,'Unknown','Unknown',NULL,'Reconcile retained object','automatic')",
                        c => { c.Parameters.AddWithValue("c", CompanyA); c.Parameters.AddWithValue("v", VehicleA); });
                    pidReady.SetResult(Convert.ToInt32(pid!["pid"]));
                    await release.Task;
                    return true;
                }, async ct =>
                {
                    compensation = true;
                    if (uploaded is not null) await files.DeleteAsync(uploaded.Reference, ct);
                }));
                var pid = await pidReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await using (var ownerConnection = new NpgsqlConnection(owner))
                {
                    await ownerConnection.OpenAsync();
                    await using var terminate = new NpgsqlCommand("SELECT pg_terminate_backend(@pid)", ownerConnection);
                    terminate.Parameters.AddWithValue("pid", pid);
                    Assert.True(Convert.ToBoolean(await terminate.ExecuteScalarAsync()));
                }
                release.SetResult();
                var error = await Record.ExceptionAsync(() => transaction);
                Assert.NotNull(error);
                return (error, compensation, store);
            }
            catch { store.Dispose(); throw; }
        }

        private DefaultHttpContext Principal(string role)
        {
            var http = new DefaultHttpContext();
            http.Items[EndpointMappings.AuthCompanyIdItemKey] = CompanyA;
            http.Items[EndpointMappings.AuthBranchIdItemKey] = BranchA;
            http.Items[EndpointMappings.AuthUserIdItemKey] = 0L;
            http.Items[EndpointMappings.AuthRoleItemKey] = role;
            http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "compliance:view", "compliance:manage" };
            return http;
        }

        private Delegate RegisteredUpload()
        {
            var matches = new List<Delegate>();
            foreach (var source in ((IEndpointRouteBuilder)app).DataSources)
            {
                if (source.GetType().GetField("_routeEntries", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(source) is not IEnumerable entries) continue;
                foreach (var entry in entries)
                    if (entry is not null && Member(entry, "RoutePattern") is RoutePattern pattern && pattern.RawText == "/api/documents/upload"
                        && Member(entry, "RouteHandler") is Delegate handler && handler.Method.GetParameters().Any(p => p.ParameterType == typeof(FileStorageService))) matches.Add(handler);
            }
            return Assert.Single(matches);
        }

        public Task AuditFailureWithinCompletingOuterScope(string token) => db.RunInTenantScopeAsync(CompanyA, async () =>
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => Raw("/api/documents/{id:long}",
                JsonSerializer.Serialize(new { expectedVersion = token, notes = "Must roll back" }), DocA,
                actorRole: new string('x', 10000), noScope: true));
            Assert.Equal("22001", error.SqlState);
            Assert.Contains("Audit", error.StackTrace ?? "");
            var restored = await db.QuerySingleAsync("SELECT current_user AS role,opstrax_security.current_tenant_id() AS tenant");
            Assert.Equal("opstrax_app", restored!["role"]?.ToString()); Assert.Equal(CompanyA, Convert.ToInt64(restored["tenant"]));
            return true; // Failure caught INSIDE the outer scope; its completion must not resurrect the failed write.
        });

        private async Task Initialize()
        {
            await using var c = new NpgsqlConnection(owner); await c.OpenAsync(); await using var tx = await c.BeginTransactionAsync();
            async Task<long> Insert(string sql, params (string, object?)[] values)
            {
                await using var cmd = new NpgsqlCommand(sql + " RETURNING id", c, tx);
                foreach (var (key, value) in values) cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);
                return Convert.ToInt64(await cmd.ExecuteScalarAsync());
            }
            async Task<long> Company(string suffix) { var id = await Insert("INSERT INTO companies(company_code,name,industry) VALUES (@code,'Synthetic lifecycle fixture','Transportation')", ("code", prefix + suffix)); companies.Add(id); return id; }
            async Task<long> Vehicle(long company, long? branch, string code, long? explicitId = null) => await Insert(
                $"INSERT INTO vehicles({(explicitId.HasValue ? "id," : "")}company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status) {(explicitId.HasValue ? "OVERRIDING SYSTEM VALUE" : "")} VALUES ({(explicitId.HasValue ? "@id," : "")}@c,@b,@code,'Truck','legacy-fleet-identifier',@alt,'Maintenance')",
                ("id", explicitId), ("c", company), ("b", branch), ("code", code), ("alt", prefix + code));
            async Task<long> Document(long company, long vehicle, string mode) => await Insert(@"INSERT INTO documents(company_id,title,document_number,document_type,entity_type,entity_id,issued_at,expires_at,status,risk_score,renewal_status,recommended_action,lifecycle_mode,lifecycle_assessed_on,file_url)
                VALUES (@c,'Synthetic lifecycle fixture - NOT COMPLIANCE','W1-PRESERVE-NUMBER','Synthetic','vehicle',@v,'2026-01-01','2026-08-30','Expired',90,'Renewal Required','Renew document',@mode,CASE WHEN @mode='automatic' THEN DATE '2026-08-31' ELSE NULL END,'objkey:synthetic-no-file')", ("c", company), ("v", vehicle), ("mode", mode));
            CompanyA = await Company("A"); CompanyB = await Company("B");
            BranchA = await Insert("INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,'A','Synthetic A','Active')", ("c", CompanyA));
            BranchB = await Insert("INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,'B','Synthetic B','Active')", ("c", CompanyA));
            VehicleA = await Vehicle(CompanyA, BranchA, "A"); VehicleA2 = await Vehicle(CompanyA, BranchA, "A2");
            var collisionId = 9_000_000_000L + RandomNumberGenerator.GetInt32(1_000_000);
            await using (var collisionGuard = new NpgsqlCommand("SELECT (SELECT count(*) FROM drivers WHERE id=@id)+(SELECT count(*) FROM vehicles WHERE id=@id)", c, tx))
            { collisionGuard.Parameters.AddWithValue("id", collisionId); Assert.Equal(0L, Convert.ToInt64(await collisionGuard.ExecuteScalarAsync())); }
            VehicleB = await Vehicle(CompanyA, BranchB, "B", collisionId);
            await Insert("INSERT INTO drivers(id,company_id,branch_id,driver_code,full_name,status) OVERRIDING SYSTEM VALUE VALUES (@id,@c,@b,'COLLIDING-A','Synthetic typed collision A','Available')",
                ("id", VehicleB), ("c", CompanyA), ("b", BranchA));
            var nullVehicle = await Vehicle(CompanyA, null, "NULL"); var foreign = await Vehicle(CompanyB, null, "FOREIGN");
            DocA = await Document(CompanyA, VehicleA, "automatic"); DocB = await Document(CompanyA, VehicleB, "automatic");
            NullBranchDoc = await Document(CompanyA, nullVehicle, "legacy_unknown"); ForeignDoc = await Document(CompanyB, foreign, "legacy_unknown");
            ManualDoc = await Document(CompanyA, VehicleA, "manual"); LegacyDoc = await Document(CompanyA, VehicleA, "legacy_unknown");
            UnknownDoc = await Document(CompanyA, VehicleA, "automatic");
            await using (var special = new NpgsqlCommand("UPDATE documents SET risk_score=0 WHERE id=@manual; UPDATE documents SET expires_at=NULL,status='Unknown',risk_score=NULL,renewal_status='Unknown',recommended_action='Add an expiry date or choose an explicit workflow override' WHERE id=@unknown", c, tx))
            { special.Parameters.AddWithValue("manual", ManualDoc); special.Parameters.AddWithValue("unknown", UnknownDoc); await special.ExecuteNonQueryAsync(); }
            await tx.CommitAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await app.DisposeAsync();
            if (companies.Count == 0) return;
            await using var c = new NpgsqlConnection(owner); await c.OpenAsync(); await using var tx = await c.BeginTransactionAsync();
            foreach (var table in new[] { "document_timeline_events", "audit_logs", "documents", "drivers", "vehicles", "branches" })
            {
                await using var cmd = new NpgsqlCommand($"DELETE FROM {table} WHERE company_id=ANY(@ids) AND company_id IN (SELECT id FROM companies WHERE company_code LIKE @prefix)", c, tx);
                cmd.Parameters.AddWithValue("ids", companies.ToArray()); cmd.Parameters.AddWithValue("prefix", prefix + "%"); await cmd.ExecuteNonQueryAsync();
            }
            await using var remove = new NpgsqlCommand("DELETE FROM companies WHERE id=ANY(@ids) AND company_code LIKE @prefix", c, tx);
            remove.Parameters.AddWithValue("ids", companies.ToArray()); remove.Parameters.AddWithValue("prefix", prefix + "%");
            await remove.ExecuteNonQueryAsync(); await tx.CommitAsync();
        }
    }

    private sealed class RecordingLocalStore : IObjectStore, IDisposable
    {
        private readonly string root;
        private readonly LocalObjectStore inner;
        public string? PutKey { get; private set; }
        public string? DeleteKey { get; private set; }
        public string Provider => inner.Provider;
        public bool IsConfigured => inner.IsConfigured;
        private RecordingLocalStore(string root) { this.root = root; inner = new LocalObjectStore(root); }
        public static RecordingLocalStore Create() => new(Path.Combine(Path.GetTempPath(), "opstrax-w1-truth012-" + Guid.NewGuid().ToString("N")));
        public async Task<string> PutAsync(string key, Stream content, string contentType, CancellationToken ct = default)
        { PutKey = key; return await inner.PutAsync(key, content, contentType, ct); }
        public Task<Stream> GetAsync(string key, CancellationToken ct = default) => inner.GetAsync(key, ct);
        public async Task DeleteAsync(string key, CancellationToken ct = default)
        { DeleteKey = key; await inner.DeleteAsync(key, ct); }
        public Task<int> DeletePrefixAsync(string prefix, CancellationToken ct = default) => inner.DeletePrefixAsync(prefix, ct);
        public Task<string?> SignedUrlAsync(string key, TimeSpan ttl, CancellationToken ct = default) => inner.SignedUrlAsync(key, ttl, ct);
        public Task<bool> HealthCheckAsync(CancellationToken ct = default) => inner.HealthCheckAsync(ct);
        public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private sealed class StaticFormFeature : IFormFeature
    {
        private readonly IFormCollection originalForm;
        public StaticFormFeature(IFormCollection form) { originalForm = form; Form = form; }
        public bool HasFormContentType => true;
        public IFormCollection? Form { get; set; }
        public IFormCollection ReadForm() => Form ?? originalForm;
        public Task<IFormCollection> ReadFormAsync(CancellationToken cancellationToken) => Task.FromResult(ReadForm());
    }
}
