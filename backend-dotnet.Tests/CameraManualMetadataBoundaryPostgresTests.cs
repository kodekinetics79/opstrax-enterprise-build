using System.Collections;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;
using Opstrax.Api.Services;
using Xunit.Abstractions;

namespace Opstrax.Tests;

// Actual registered delegates + framework binding, with injected context and owner-backed
// isolated PostgreSQL. NOT real sessions, full middleware, HTTP transport, RLS or footage proof.
[Trait("Category", "Integration")]
public sealed class CameraManualMetadataBoundaryPostgresTests(ITestOutputHelper output)
{
    private const string PostPath = "/api/dashcam/events";
    private const string PutPath = "/api/dashcam/events/{id:long}";
    private const string ValidCreate = "{\"eventType\":\"Near Miss\",\"title\":\"Synthetic manual observation\",\"severity\":\"High\"}";

    [Theory]
    [InlineData("{\"eventType\":\"x\",\"title\":\"\\ud800\",\"severity\":\"High\"}",false)]
    [InlineData("{\"eventType\":\"x\",\"title\":\"\\udc00\",\"severity\":\"High\"}",false)]
    [InlineData("{\"\\ud800\":\"x\",\"title\":\"valid\",\"severity\":\"High\"}",false)]
    [InlineData("{\"eventType\":\"x\",\"title\":\"\\ud83d\\ude80\",\"severity\":\"High\"}",true)]
    [InlineData("{\"eventType\":\"x\",\"title\":\"M\u00e9tadonn\u00e9es \u5b89\u5168\",\"severity\":\"High\"}",true)]
    public async Task ParserEscapedSurrogates_NoDatabase_ActualPrivateReader(string raw,bool valid)
    {
        var http=new DefaultHttpContext();
        using var body=new MemoryStream(Encoding.UTF8.GetBytes(raw));
        http.Request.Body=body;
        http.Request.ContentLength=body.Length;
        var method=typeof(EndpointMappings).GetMethod("ReadCameraMetadataInput",BindingFlags.NonPublic|BindingFlags.Static)!;
        var task=(Task)method.Invoke(null,[http,true,CancellationToken.None])!;
        await task;
        var result=task.GetType().GetProperty("Result")!.GetValue(task);
        Assert.Equal(valid,result is not null);
    }

    [Theory]
    [InlineData("{\"event\\u0000Type\":\"x\",\"title\":\"valid\",\"severity\":\"High\"}")]
    [InlineData("{\"eventType\":\"x\\u0000y\",\"title\":\"valid\",\"severity\":\"High\"}")]
    [InlineData("{\"eventType\":\"x\",\"title\":\"escaped\\u0000nul\",\"severity\":\"High\"}")]
    [InlineData("{\"eventType\":\"x\",\"title\":\"valid\",\"severity\":\"Hi\\u0000gh\"}")]
    [InlineData("{\"eventType\":\"x\",\"title\":\"valid\",\"severity\":\"High\",\"locationDescription\":\"a\\u0000b\"}")]
    [InlineData("{\"eventType\":\"x\",\"title\":\"valid\",\"severity\":\"High\",\"occurredAt\":\"2026-01-01T00:00:00Z\\u0000\"}")]
    public async Task ParserNulIsRejected_NoDatabase_ActualPrivateReader(string raw)
    {
        var http=new DefaultHttpContext();
        using var body=new MemoryStream(Encoding.UTF8.GetBytes(raw));
        http.Request.Body=body;
        var method=typeof(EndpointMappings).GetMethod("ReadCameraMetadataInput",BindingFlags.NonPublic|BindingFlags.Static)!;
        var task=(Task)method.Invoke(null,[http,true,CancellationToken.None])!;
        await task;
        var result=task.GetType().GetProperty("Result")!.GetValue(task);
        Assert.Null(result);
    }

    [Fact]
    public async Task ParentRed_RegisteredSelectedDelegatesDeferBodyBinding()
    {
        await using var fixture = await Fixture.CreateAsync(output);
        Assert.DoesNotContain(fixture.Post.Method.GetParameters(), parameter => parameter.ParameterType == typeof(Dictionary<string, object?>));
        Assert.DoesNotContain(fixture.Put.Method.GetParameters(), parameter => parameter.ParameterType == typeof(Dictionary<string, object?>));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParentRed_PresentBranchDeniesBeforeBodyAndBusinessWork(bool update)
    {
        await using var fixture = await Fixture.CreateAsync(output);
        var before = await fixture.SnapshotAsync();
        var response = await fixture.InvokeAsync(update, "{", context => context.Items[EndpointMappings.AuthBranchIdItemKey] = 9L);
        Assert.Equal(403, response.Status);
        Assert.Equal(0, response.BytesRead);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task ParentRed_ForbiddenProviderFieldIsRejectedInsteadOfSilentlyAccepted()
    {
        await using var fixture = await Fixture.CreateAsync(output);
        var before = await fixture.SnapshotAsync();
        var response = await fixture.InvokeAsync(false, ValidCreate[..^1] + ",\"aiConfidence\":99}");
        Assert.Equal(400, response.Status);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task ParentRed_AuthoritativeTargetIsOpaqueBeforeVersionConflict()
    {
        await using var fixture = await Fixture.CreateAsync(output);
        await fixture.SeedCameraAsync("Authoritative");
        var before = await fixture.SnapshotAsync();
        var response = await fixture.InvokeAsync(true, "{\"title\":\"must not change provider row\",\"rowVersion\":0}");
        Assert.Equal(404, response.Status);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task ParentRed_StaleVersionDoesNotCommitAnUpdateOrAudit()
    {
        await using var fixture = await Fixture.CreateAsync(output);
        await fixture.SeedCameraAsync();
        var before = await fixture.SnapshotAsync();
        var response = await fixture.InvokeAsync(true, "{\"title\":\"stale\",\"rowVersion\":0}");
        Assert.Equal(409, response.Status);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task ParentRed_MissingTriggerRefusesWriteWithoutFallback()
    {
        await using var fixture = await Fixture.CreateAsync(output);
        await fixture.ExecuteAsync("DROP TRIGGER trg_stage100_enforce_dashcam_provider_truth ON dashcam_events");
        var before = await fixture.SnapshotAsync();
        var response = await fixture.InvokeAsync(false, ValidCreate);
        Assert.Equal(503, response.Status);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactCurrentStage100_CreateThenNullableUpdate_UsesStoredVersionsAndNeutralAudits(bool ambient)
    {
        await using var fixture = await Fixture.CreateAsync(output);
        Assert.Equal("b8c9ab16ca90a6afb57814376de2e380c433ef6a7283dcd3d8c1666b86b81ebb",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((string)(await fixture.ScalarAsync("SELECT prosrc FROM pg_proc WHERE oid='stage100_enforce_dashcam_provider_truth()'::regprocedure"))!))).ToLowerInvariant());
        var input = ValidCreate[..^1] + ",\"driverId\":110,\"vehicleId\":120,\"jobId\":130,\"routeId\":140,\"locationDescription\":\"private synthetic location\",\"occurredAt\":\"2026-01-01T00:00:00Z\"}";
        var created = await fixture.InvokeAsync(false, input, ambient: ambient);
        AssertReceipt(created, 201, 1);
        Assert.Equal("LegacyUnverified|Unavailable|Needs Review|Pending Review|Not Packaged|false", await fixture.ScalarAsync("SELECT source_authority||'|'||media_status||'|'||coaching_status||'|'||review_status||'|'||evidence_status||'|'||false_positive::text FROM dashcam_events"));
        var updated = await fixture.InvokeAsync(true, "{\"rowVersion\":1,\"title\":\" Changed title \",\"driverId\":null,\"vehicleId\":null,\"jobId\":null,\"routeId\":null,\"locationDescription\":null,\"occurredAt\":null}", ambient: ambient);
        AssertReceipt(updated, 200, 2);
        Assert.True((bool)(await fixture.ScalarAsync("SELECT title='Changed title' AND severity='High' AND driver_id IS NULL AND vehicle_id IS NULL AND job_id IS NULL AND route_id IS NULL AND location_description IS NULL AND occurred_at IS NULL AND ai_confidence IS NULL AND video_provider IS NULL AND provider_event_id IS NULL AND road_facing_clip_url IS NULL FROM dashcam_events"))!);
        Assert.Equal(2L, await fixture.ScalarAsync("SELECT COUNT(*) FROM audit_logs WHERE company_id=11 AND actor_user_id=7 AND entity_name='DashcamEvent' AND entity_id=1"));
        var audits = (string)(await fixture.ScalarAsync("SELECT jsonb_agg(details_json)::text FROM audit_logs"))!;
        Assert.DoesNotContain("private synthetic", audits);
        Assert.DoesNotContain("Changed title", audits);
        Assert.DoesNotContain("Synthetic manual", audits);
    }

    private static void AssertReceipt(Response response, int status, long version)
    {
        Assert.Equal(status, response.Status);
        var json = response.Json;
        Assert.True(json.GetProperty("success").GetBoolean());
        var data = json.GetProperty("data");
        Assert.Equal(new[] { "automatedAssessmentAvailable", "dataSource", "id", "mediaAvailable", "provenanceStatus", "rowVersion" }, data.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(1, data.GetProperty("id").GetInt64());
        Assert.Equal(version, data.GetProperty("rowVersion").GetInt64());
        Assert.Equal("stored_metadata", data.GetProperty("dataSource").GetString());
        Assert.Equal("unverified", data.GetProperty("provenanceStatus").GetString());
        Assert.False(data.GetProperty("mediaAvailable").GetBoolean());
        Assert.False(data.GetProperty("automatedAssessmentAvailable").GetBoolean());
    }

    public static IEnumerable<object[]> InvalidBodies()
    {
        foreach (var body in new[] { "", "{", "null", "[]", "{}", "{\"title\":\"x\",\"eventType\":\"x\"}",
            ValidCreate[..^1]+",\"title\":\"duplicate\"}", ValidCreate[..^1]+",\"Title\":\"wrong case\"}",
            ValidCreate.Replace("High"," High "), ValidCreate.Replace("High","high"), ValidCreate.Replace("Near Miss"," "),
            ValidCreate.Replace("Synthetic manual observation","\\ud800"), ValidCreate.Replace("Synthetic manual observation","\\udc00"),
            "{\"\\ud800\":\"x\",\"title\":\"valid\",\"severity\":\"High\"}",
            ValidCreate.Replace("Near Miss","x\\u0000y"), ValidCreate.Replace("Synthetic manual observation","escaped\\u0000nul"),
            ValidCreate.Replace("High","Hi\\u0000gh"), "{\"event\\u0000Type\":\"x\",\"title\":\"valid\",\"severity\":\"High\"}",
            ValidCreate[..^1]+",\"locationDescription\":\"a\\u0000b\"}",
            ValidCreate[..^1]+",\"occurredAt\":\"2026-01-01T00:00:00Z\\u0000\"}",
            ValidCreate.Replace("Near Miss",new string('x',121)), ValidCreate.Replace("Synthetic manual observation",new string('x',221)) }) yield return [false, body];
        foreach (var field in new[] { "eventNumber", "aiConfidence", "aiSummary", "sourceAuthority", "videoProvider", "reviewStatus", "evidenceStatus", "coachingStatus", "falsePositive", "roadFacingClipUrl", "rowVersion" })
            yield return [false, ValidCreate[..^1]+$",\"{field}\":0}}"];
        foreach (var id in new[] { "0", "-1", "1.2", "\"110\"", "true", "9223372036854775808" }) yield return [false, ValidCreate[..^1]+$",\"driverId\":{id}}}"];
        foreach (var value in new[] { "\"\"", "\"   \"", "123", "true", JsonSerializer.Serialize(new string('x',221)) }) yield return [false, ValidCreate[..^1]+$",\"locationDescription\":{value}}}"];
        foreach (var time in new[] { "2026-01-01", "2026-01-01T00:00:00", "2026-01-01T00:00:00+01:00", "2099-01-01T00:00:00Z", "not a date" }) yield return [false, ValidCreate[..^1]+$",\"occurredAt\":\"{time}\"}}"];
        foreach (var body in new[] { "{}", "{\"title\":\"x\"}", "{\"rowVersion\":1}", "{\"rowVersion\":null,\"title\":\"x\"}", "{\"rowVersion\":-1,\"title\":\"x\"}", "{\"rowVersion\":\"1\",\"title\":\"x\"}", "{\"rowVersion\":1,\"title\":null}", "{\"rowVersion\":1,\"safetyEventId\":150}", "{\"rowVersion\":1,\"eventNumber\":\"x\"}" }) yield return [true, body];
    }
    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task StrictInputRefusesUnsupportedOrMalformedWithoutBusinessWork(bool update, string body)
    {
        await using var fixture = await Fixture.CreateAsync(output);
        var before = await fixture.SnapshotAsync();
        // Remove a prerequisite too: invalid input must still resolve to400 before catalogs.
        await fixture.ExecuteAsync("DROP TRIGGER trg_stage100_enforce_dashcam_provider_truth ON dashcam_events");
        var response = await fixture.InvokeAsync(update, body);
        Assert.Equal(400, response.Status);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1L)]
    [InlineData(32769L)]
    public async Task ActualByteLimitAppliesWithoutTrustingContentLength(long? length)
    {
        await using var fixture = await Fixture.CreateAsync(output);
        var before = await fixture.SnapshotAsync();
        var response = await fixture.InvokeAsync(false, ValidCreate + new string(' ',32769), c => c.Request.ContentLength = length);
        Assert.Equal(400, response.Status);
        Assert.True(response.BytesRead <= 32769);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("application/json; charset=utf-16")]
    [InlineData("application/json; charset=\"\"")]
    [InlineData("application/json; charset=utf-8; charset=utf-16")]
    [InlineData("not a media type")]
    public async Task UnsupportedMediaIsRefusedWithoutReading(string? media)
    {
        await using var fixture=await Fixture.CreateAsync(output);
        var response=await fixture.InvokeAsync(false,ValidCreate,c => c.Request.ContentType=media);
        Assert.Equal(415,response.Status);
        Assert.Equal(0,response.BytesRead);
    }
    [Theory]
    [InlineData("Application/Json; charset=\"UTF-8\"")]
    [InlineData("application/problem+json")]
    public async Task SupportedUtf8MediaRetainsJsonSemantics(string media)
    {
        await using var fixture=await Fixture.CreateAsync(output);
        var malformed=await fixture.InvokeAsync(false,"{",c => c.Request.ContentType=media);
        Assert.Equal(400,malformed.Status);
        AssertReceipt(await fixture.InvokeAsync(false,ValidCreate,c => c.Request.ContentType=media),201,1);
    }
    [Fact]
    public async Task MalformedUtf8IsGenericValidationNotTranscodingException()
    {
        await using var fixture=await Fixture.CreateAsync(output);
        var response=await fixture.InvokeAsync(false,"",rawBytes: [123,34,116,105,116,108,101,34,58,34,0xC3,0x28,34,125]);
        Assert.Equal(400,response.Status);
        Assert.Equal(0L,await fixture.ScalarAsync("SELECT COUNT(*) FROM dashcam_events"));
    }

    [Theory]
    [InlineData("missing",401)]
    [InlineData("denied",403)]
    [InlineData("customer",403)]
    [InlineData("branch-null",403)]
    [InlineData("branch-malformed",403)]
    public async Task ExistingAdmissionPrecedesBody(string mode, int status)
    {
        await using var fixture = await Fixture.CreateAsync(output);
        var response = await fixture.InvokeAsync(false,"{",c => {
            if (mode=="missing") c.Items.Remove(EndpointMappings.AuthUserIdItemKey);
            if (mode=="denied") c.Items[EndpointMappings.AuthPermissionsItemKey] = Array.Empty<string>();
            if (mode=="customer") c.Items[EndpointMappings.AuthCustomerIdItemKey] = 777L;
            if (mode.StartsWith("branch",StringComparison.Ordinal)) c.Items[EndpointMappings.AuthBranchIdItemKey] = mode=="branch-null" ? null : "malformed";
        });
        Assert.Equal(status,response.Status);
        Assert.Equal(0,response.BytesRead);
        Assert.Equal(0L,await fixture.ScalarAsync("SELECT COUNT(*) FROM dashcam_events"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BranchTelemetryServerFailureDoesNotPoisonDeniedOperation(bool ambient)
    {
        await using var fixture = await Fixture.CreateAsync(output);
        await fixture.ExecuteAsync("ALTER TABLE security_events ADD CONSTRAINT synthetic_denial_failure CHECK(false)");
        var response = await fixture.InvokeAsync(false,"{",c => c.Items[EndpointMappings.AuthBranchIdItemKey]=9L,ambient,
            () => fixture.Db.ExecuteAsync("SELECT 1"));
        Assert.Equal(403,response.Status);
        Assert.Equal(0,response.BytesRead);
        Assert.Equal(0L,await fixture.ScalarAsync("SELECT COUNT(*) FROM dashcam_events"));
        Assert.True((bool)(await fixture.ScalarAsync("SELECT is_called FROM security_events_id_seq"))!);
        Assert.Equal(0L,await fixture.ScalarAsync("SELECT COUNT(*) FROM security_events"));
    }

    [Theory]
    [InlineData("ProviderPending")]
    [InlineData("Authoritative")]
    [InlineData("foreign")]
    [InlineData("archived")]
    [InlineData("unknown")]
    [InlineData("blank")]
    [InlineData("missing")]
    [InlineData("nonpositive")]
    public async Task AllNonManualTargetsAreOpaqueBeforeStale(string state)
    {
        await using var fixture = await Fixture.CreateAsync(output);
        if (state!="missing") await fixture.SeedCameraAsync(state is "ProviderPending" or "Authoritative" ? state : "LegacyUnverified");
        if (state=="foreign") await fixture.ExecuteAsync("UPDATE dashcam_events SET company_id=22");
        if (state=="archived") await fixture.ExecuteAsync("UPDATE dashcam_events SET deleted_at=NOW()");
        if (state is "unknown" or "blank")
        {
            // Synthetic legacy-drift target, not an accepted schema relaxation.
            await fixture.ExecuteAsync("ALTER TABLE dashcam_events DISABLE TRIGGER trg_stage100_enforce_dashcam_provider_truth; ALTER TABLE dashcam_events DROP CONSTRAINT ck_dashcam_source_authority");
            await fixture.ExecuteAsync(state=="blank" ? "UPDATE dashcam_events SET source_authority=''" : "UPDATE dashcam_events SET source_authority='Unknown'");
            await fixture.ExecuteAsync("ALTER TABLE dashcam_events ENABLE TRIGGER trg_stage100_enforce_dashcam_provider_truth");
        }
        var before = await fixture.SnapshotAsync();
        var response = await fixture.InvokeAsync(true,"{\"title\":\"no\",\"rowVersion\":0}",c => { if(state=="nonpositive") c.Request.RouteValues["id"]="0"; });
        Assert.Equal(404,response.Status);
        Assert.Equal(before,await fixture.SnapshotAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BusinessAuditFailurePropagatesAndRollsBackWholeWrite(bool ambient)
    {
        await using var fixture = await Fixture.CreateAsync(output);
        await fixture.ExecuteAsync("ALTER TABLE audit_logs ADD CONSTRAINT synthetic_audit_failure CHECK(false)");
        var before=await fixture.SnapshotAsync();
        var error=await Assert.ThrowsAsync<PostgresException>(() => fixture.InvokeAsync(false,ValidCreate,ambient:ambient));
        Assert.Equal("23514",error.SqlState);
        Assert.Equal("synthetic_audit_failure",error.ConstraintName);
        Assert.Equal(before,await fixture.SnapshotAsync());
    }

    [Theory]
    [InlineData("driver-company", "UPDATE drivers SET company_id=22", "\"driverId\":110")]
    [InlineData("driver-archived", "UPDATE drivers SET deleted_at=NOW()", "\"driverId\":110")]
    [InlineData("driver-missing", "DELETE FROM drivers", "\"driverId\":110")]
    [InlineData("reference-branch", "UPDATE vehicles SET branch_id=10", "\"driverId\":110,\"vehicleId\":120")]
    [InlineData("reciprocal-link", "UPDATE vehicles SET assigned_driver_id=999", "\"driverId\":110,\"vehicleId\":120")]
    [InlineData("safety-link", "UPDATE safety_events SET vehicle_id=999", "\"safetyEventId\":150,\"vehicleId\":120")]
    [InlineData("job-route", "UPDATE jobs SET route_id=999", "\"jobId\":130,\"routeId\":140")]
    public async Task ReferencesRemainActiveOwnedAndLinked(string kind,string sql,string fields)
    {
        await using var fixture=await Fixture.CreateAsync(output);
        await fixture.ExecuteAsync(sql);
        var before=await fixture.SnapshotAsync();
        var response=await fixture.InvokeAsync(false,ValidCreate[..^1]+","+fields+"}");
        Assert.Equal(400,response.Status);
        Assert.Equal(before,await fixture.SnapshotAsync());
        output.WriteLine($"REFERENCE {kind}: rejected.");
    }

    [Fact]
    public async Task LockedTargetBranchCannotBeSilentlyChangedByNewReferences()
    {
        await using var fixture=await Fixture.CreateAsync(output);
        await fixture.SeedCameraAsync();
        await fixture.ExecuteAsync("UPDATE dashcam_events SET branch_id=10");
        var before=await fixture.SnapshotAsync();
        var response=await fixture.InvokeAsync(true,"{\"rowVersion\":2,\"driverId\":110,\"vehicleId\":120}");
        Assert.Equal(400,response.Status);
        Assert.Equal(before,await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task CreateHasExplicitNullBranchAndServerTimeEvenWithDifferentColumnDefault()
    {
        await using var fixture=await Fixture.CreateAsync(output);
        await fixture.ExecuteAsync("ALTER TABLE dashcam_events ALTER COLUMN branch_id SET DEFAULT 999");
        var response=await fixture.InvokeAsync(false,ValidCreate[..^1]+",\"occurredAt\":null}");
        AssertReceipt(response,201,1);
        Assert.True((bool)(await fixture.ScalarAsync("SELECT branch_id IS NULL AND occurred_at BETWEEN NOW()-interval '1 minute' AND NOW()+interval '1 minute' FROM dashcam_events"))!);
    }

    [Theory]
    [InlineData("drivers","deleted_at=NOW()","\"driverId\":110",110L)]
    [InlineData("vehicles","assigned_driver_id=999","\"driverId\":110,\"vehicleId\":120",120L)]
    public async Task ReferenceNonKeyCompetitorWaitsForActualApiTransaction(string table,string change,string fields,long referenceId)
    {
        await using var fixture=await Fixture.CreateAsync(output);
        await using var barrier=await fixture.HoldAuditBarrierAsync();
        Task<Response>? api=null;
        Task<int>? competitor=null;
        await using var connection=await fixture.OpenConnectionAsync();
        await using var command=new NpgsqlCommand($"UPDATE {table} SET {change} WHERE id=@id",connection);
        command.Parameters.AddWithValue("id",referenceId);
        try
        {
            api=fixture.InvokeAsync(false,ValidCreate[..^1]+","+fields+"}");
            var apiPid=await fixture.WaitForBlockedAsync(fixture.Owner.ProcessID);
            competitor=command.ExecuteNonQueryAsync();
            var competitorPid=await fixture.WaitForBlockedAsync(apiPid,connection.ProcessID);
            Assert.Equal(connection.ProcessID,competitorPid);
            Assert.False(competitor.IsCompleted);
            await barrier.ReleaseAsync();
            AssertReceipt(await api.WaitAsync(TimeSpan.FromSeconds(10)),201,1);
            Assert.Equal(1,await competitor.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(1L,await fixture.ScalarAsync("SELECT COUNT(*) FROM audit_logs"));
        }
        finally
        {
            await Fixture.ReleaseAndDrainAsync(barrier.ReleaseAsync,api,competitor);
        }
    }

    [Theory]
    [InlineData("drivers","deleted_at=NOW()","\"driverId\":110",110L)]
    [InlineData("vehicles","assigned_driver_id=999","\"driverId\":110,\"vehicleId\":120",120L)]
    public async Task ReferenceCompetitorWinningFirstMakesWaitingRequestInvalid(string table,string change,string fields,long id)
    {
        await using var fixture=await Fixture.CreateAsync(output);
        await using var winner=await fixture.Owner.BeginTransactionAsync();
        await fixture.ExecuteAsync($"UPDATE {table} SET {change} WHERE id={id}");
        Task<Response>? api=null;
        var released=false;
        async Task Release()
        {
            if(released) return;
            await winner.CommitAsync();
            released=true;
        }
        try
        {
            api=fixture.InvokeAsync(false,ValidCreate[..^1]+","+fields+"}");
            await fixture.WaitForBlockedAsync(fixture.Owner.ProcessID);
            await Release();
            Assert.Equal(400,(await api.WaitAsync(TimeSpan.FromSeconds(10))).Status);
            Assert.Equal(0L,await fixture.ScalarAsync("SELECT COUNT(*) FROM dashcam_events"));
            Assert.Equal(0L,await fixture.ScalarAsync("SELECT COUNT(*) FROM audit_logs"));
        }
        finally { await Fixture.ReleaseAndDrainAsync(Release,api); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoSameVersionRequestsProduceOneCommittedWinner(bool ambient)
    {
        await using var fixture=await Fixture.CreateAsync(output);
        await fixture.SeedCameraAsync();
        await using var barrier=await fixture.HoldAuditBarrierAsync();
        Task<Response>? first=null,second=null;
        try
        {
            first=fixture.InvokeAsync(true,"{\"title\":\"winner\",\"rowVersion\":1}",ambient:ambient);
            var firstPid=await fixture.WaitForBlockedAsync(fixture.Owner.ProcessID);
            second=fixture.InvokeAsync(true,"{\"title\":\"stale contender\",\"rowVersion\":1}",ambient:ambient);
            await fixture.WaitForBlockedAsync(firstPid);
            Assert.False(second.IsCompleted);
            await barrier.ReleaseAsync();
            AssertReceipt(await first.WaitAsync(TimeSpan.FromSeconds(10)),200,2);
            Assert.Equal(409,(await second.WaitAsync(TimeSpan.FromSeconds(10))).Status);
            Assert.Equal("winner",await fixture.ScalarAsync("SELECT title FROM dashcam_events"));
            Assert.Equal(1L,await fixture.ScalarAsync("SELECT COUNT(*) FROM audit_logs"));
        }
        finally { await Fixture.ReleaseAndDrainAsync(barrier.ReleaseAsync,first,second); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAfterBusinessWriteDoesNotReturnAcknowledgementOrLeaveUnauditedRow(bool ambient)
    {
        await using var fixture=await Fixture.CreateAsync(output);
        var before=await fixture.SnapshotAsync();
        await using var barrier=await fixture.HoldAuditBarrierAsync();
        using var canceled=new CancellationTokenSource();
        Task<Response>? api=null;
        try
        {
            api=fixture.InvokeAsync(false,ValidCreate,c => c.RequestAborted=canceled.Token,ambient);
            await fixture.WaitForBlockedAsync(fixture.Owner.ProcessID);
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await api.WaitAsync(TimeSpan.FromSeconds(10)));
            await barrier.ReleaseAsync();
            Assert.Equal(before,await fixture.SnapshotAsync());
        }
        finally { await Fixture.ReleaseAndDrainAsync(barrier.ReleaseAsync,api); }
    }

    [Fact]
    public async Task PrerequisiteLockTimeoutRestoresAmbientTransactionAndDoesNotReachBusinessWrite()
    {
        await using var fixture=await Fixture.CreateAsync(output);
        await using var locked=await fixture.Owner.BeginTransactionAsync();
        await fixture.ExecuteAsync("LOCK TABLE dashcam_events IN ACCESS EXCLUSIVE MODE");
        Task<Response>? api=null;
        try
        {
            api=fixture.InvokeAsync(false,ValidCreate,ambient:true,afterHandler: () => fixture.Db.ExecuteAsync("SELECT 1"));
            await fixture.WaitForBlockedAsync(fixture.Owner.ProcessID);
            Assert.Equal(503,(await api.WaitAsync(TimeSpan.FromSeconds(8))).Status);
        }
        finally { await Fixture.ReleaseAndDrainAsync(() => locked.RollbackAsync(),api); }
        Assert.Equal(0L,await fixture.ScalarAsync("SELECT COUNT(*) FROM dashcam_events"));
        Assert.Equal(0L,await fixture.ScalarAsync("SELECT COUNT(*) FROM audit_logs"));
    }

    [Fact]
    public async Task ManagedCloseOfExactFixtureConnectionWhilePrerequisiteBlockedPropagatesWithoutWrite()
    {
        await using var fixture=await Fixture.CreateAsync(output);
        var before=await fixture.SnapshotAsync();
        await using var locked=await fixture.Owner.BeginTransactionAsync();
        await fixture.ExecuteAsync("LOCK TABLE dashcam_events IN ACCESS EXCLUSIVE MODE");
        TenantScope? apiScope=null;
        Task<Response>? api=null;
        try
        {
            api=fixture.InvokeAsync(false,ValidCreate,ambient:true,beforeHandler: () =>
            {
                apiScope=fixture.Scopes.Current;
                return Task.CompletedTask;
            });
            var apiPid=await fixture.WaitForBlockedAsync(fixture.Owner.ProcessID);
            Assert.NotNull(apiScope);
            Assert.Equal(apiPid,apiScope!.Connection.ProcessID);
            await apiScope.Connection.CloseAsync().WaitAsync(TimeSpan.FromSeconds(3));
            var failure=await Assert.ThrowsAnyAsync<Exception>(async () => await api.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.IsNotType<TimeoutException>(failure);
            Assert.True(failure is NpgsqlException or InvalidOperationException or OperationCanceledException or ObjectDisposedException,
                $"Expected managed connection-close propagation, got {failure.GetType().FullName}.");
        }
        finally { await Fixture.ReleaseAndDrainAsync(() => locked.RollbackAsync(),api); }
        Assert.Equal(before,await fixture.SnapshotAsync());
    }

    [Theory]
    [InlineData("local",201)]
    [InlineData("replica",503)]
    public async Task ActualSessionTriggerModeIsChecked(string mode,int expected)
    {
        await using var fixture=await Fixture.CreateAsync(output);
        var response=await fixture.InvokeAsync(false,ValidCreate,ambient:true,
            beforeHandler: () => fixture.Db.ExecuteAsync($"SET LOCAL session_replication_role='{mode}'"));
        Assert.Equal(expected,response.Status);
        Assert.Equal(expected==201 ? 1L : 0L,await fixture.ScalarAsync("SELECT COUNT(*) FROM dashcam_events"));
    }

    [Fact]
    public async Task ActualCleanupHelperPropagatesMissingSavepoint_LayeredNotHandlerRaceEvidence()
    {
        await using var fixture=await Fixture.CreateAsync(output);
        await using var scope=await fixture.Db.BeginTenantScopeAsync(11);
        fixture.Scopes.Current=scope;
        try
        {
            var restore=typeof(EndpointMappings).GetMethod("RestoreCameraSavepoint",BindingFlags.NonPublic|BindingFlags.Static)!;
            var task=(Task)restore.Invoke(null,[fixture.Db,"camera_metadata_prerequisite"])!;
            var error=await Assert.ThrowsAsync<PostgresException>(() => task);
            Assert.Equal("3B001",error.SqlState);
        }
        finally { fixture.Scopes.Current=null; }
        // This source pin covers the caller's exclusion; it is NOT an actual handler
        // cleanup-race or lost-commit test. The helper above uses the real PostgreSQL failure.
        var source=File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../backend-dotnet/Controllers/EndpointMappings.cs")));
        Assert.Contains("restoring = true;",source,StringComparison.Ordinal);
        Assert.Contains("catch (PostgresException ex) when (!restoring && CameraRecoverableDatabaseError(ex, ct))",source,StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> PrerequisiteVariants()
    {
        yield return ["missing", "DROP TRIGGER trg_stage100_enforce_dashcam_provider_truth ON dashcam_events"];
        yield return ["disabled", "ALTER TABLE dashcam_events DISABLE TRIGGER trg_stage100_enforce_dashcam_provider_truth"];
        yield return ["replica-only", "ALTER TABLE dashcam_events ENABLE REPLICA TRIGGER trg_stage100_enforce_dashcam_provider_truth"];
        yield return ["always", "ALTER TABLE dashcam_events ENABLE ALWAYS TRIGGER trg_stage100_enforce_dashcam_provider_truth"];
        yield return ["condition", "DROP TRIGGER trg_stage100_enforce_dashcam_provider_truth ON dashcam_events; CREATE TRIGGER trg_stage100_enforce_dashcam_provider_truth BEFORE INSERT OR UPDATE ON dashcam_events FOR EACH ROW WHEN (NEW.id>0) EXECUTE FUNCTION stage100_enforce_dashcam_provider_truth()"];
        yield return ["column-restricted", "DROP TRIGGER trg_stage100_enforce_dashcam_provider_truth ON dashcam_events; CREATE TRIGGER trg_stage100_enforce_dashcam_provider_truth BEFORE INSERT OR UPDATE OF title ON dashcam_events FOR EACH ROW EXECUTE FUNCTION stage100_enforce_dashcam_provider_truth()"];
        yield return ["altered-body", "CREATE OR REPLACE FUNCTION stage100_enforce_dashcam_provider_truth() RETURNS TRIGGER LANGUAGE plpgsql AS $$ BEGIN RETURN NEW; END $$"];
        yield return ["security-definer", "ALTER FUNCTION stage100_enforce_dashcam_provider_truth() SECURITY DEFINER"];
        yield return ["settings", "ALTER FUNCTION stage100_enforce_dashcam_provider_truth() SET search_path=pg_catalog"];
        yield return ["extra-before-trigger", "CREATE TRIGGER synthetic_extra BEFORE UPDATE ON dashcam_events FOR EACH ROW EXECUTE FUNCTION stage100_enforce_dashcam_provider_truth()"];
        yield return ["media-column", "ALTER TABLE dashcam_events DROP COLUMN media_expires_at"];
        yield return ["version-type", "ALTER TABLE dashcam_events ALTER COLUMN row_version TYPE integer"];
        yield return ["version-default", "ALTER TABLE dashcam_events ALTER COLUMN row_version SET DEFAULT 9"];
        yield return ["version-nullable", "ALTER TABLE dashcam_events ALTER COLUMN row_version DROP NOT NULL"];
        yield return ["wrong-function", "CREATE FUNCTION synthetic_wrong() RETURNS TRIGGER LANGUAGE plpgsql AS $$ BEGIN RETURN NEW; END $$; DROP TRIGGER trg_stage100_enforce_dashcam_provider_truth ON dashcam_events; CREATE TRIGGER trg_stage100_enforce_dashcam_provider_truth BEFORE INSERT OR UPDATE ON dashcam_events FOR EACH ROW EXECUTE FUNCTION synthetic_wrong()"];
        yield return ["wrong-relation", "ALTER TABLE dashcam_events RENAME TO synthetic_wrong_relation; CREATE TABLE dashcam_events (LIKE synthetic_wrong_relation INCLUDING ALL)"];
        yield return ["inherited-child", "CREATE TABLE synthetic_child() INHERITS(dashcam_events)"];
        yield return ["inherited-parent", "CREATE TABLE synthetic_parent(synthetic_column bigint); ALTER TABLE dashcam_events ADD COLUMN synthetic_column bigint; ALTER TABLE dashcam_events INHERIT synthetic_parent"];
    }
    [Theory]
    [MemberData(nameof(PrerequisiteVariants))]
    public async Task IncompatiblePrerequisiteRefusesBeforeReferenceQueryAndLeavesAmbientUsable(string variant, string sql)
    {
        await using var fixture = await Fixture.CreateAsync(output);
        await fixture.ExecuteAsync(sql);
        await fixture.ExecuteAsync("DROP TABLE drivers");
        var response=await fixture.InvokeAsync(false,ValidCreate[..^1]+",\"driverId\":110}",ambient:true,
            afterHandler: () => fixture.Db.ExecuteAsync("SELECT 1"));
        Assert.Equal(503,response.Status);
        Assert.Equal(0L,await fixture.ScalarAsync("SELECT COUNT(*) FROM dashcam_events"));
        Assert.Equal(0L,await fixture.ScalarAsync("SELECT COUNT(*) FROM audit_logs"));
        output.WriteLine($"PREREQUISITE {variant}: rejected before missing reference table; ambient SELECT1 survived.");
    }

    private sealed record Response(int Status, string Body, int BytesRead)
    {
        public JsonElement Json => JsonDocument.Parse(Body).RootElement.Clone();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _schema = $"camera_metadata_{Guid.NewGuid():N}";
        private readonly ITestOutputHelper _output;
        private readonly NpgsqlConnectionStringBuilder _settings;
        private bool _created;
        private WebApplication? _app;
        public NpgsqlConnection Owner { get; }
        public Database Db { get; }
        public TenantScopeAccessor Scopes { get; } = new();
        public Delegate Post { get; private set; } = null!;
        public Delegate Put { get; private set; } = null!;

        private Fixture(ITestOutputHelper output)
        {
            var configured = Environment.GetEnvironmentVariable("OPSTRAX_TEST_DB");
            if (string.IsNullOrWhiteSpace(configured)) throw new InvalidOperationException("Explicit disposable local OPSTRAX_TEST_DB is required.");
            _settings = new NpgsqlConnectionStringBuilder(configured);
            if (_settings.Host != "127.0.0.1" || _settings.Port != 5433 || _settings.Database != "opstrax_local" || _settings.Username != "zayra")
                throw new InvalidOperationException("Camera API fixture requires the approved local-only test target and identity.");
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PG_CONNECTION_REPLICA")) ||
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES")))
                throw new InvalidOperationException("Ambient read-replica or hosting startup is not permitted in this fixture.");
            _settings.SearchPath = $"{_schema},pg_catalog";
            _settings.Pooling = false;
            _settings.Timeout = 5;
            _settings.CommandTimeout = 10;
            _settings.IncludeErrorDetail = false;
            _settings.ApplicationName = _schema;
            _settings.Options = "-c lock_timeout=3000 -c statement_timeout=10000";
            _output = output;
            Owner = new NpgsqlConnection(_settings.ConnectionString);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = Environments.Development,
                ["Rls:EnforceTenantContext"] = "false",
                ["ConnectionStrings:DefaultConnection"] = _settings.ConnectionString,
                ["ConnectionStrings:SystemConnection"] = _settings.ConnectionString
            }).Build();
            Db = new Database(configuration, Scopes);
            Assert.False(Db.HasReadReplica);
        }

        public static async Task<Fixture> CreateAsync(ITestOutputHelper output)
        {
            var fixture = new Fixture(output);
            try
            {
                await fixture.Owner.OpenAsync();
                await fixture.ExecuteAsync($"CREATE SCHEMA {fixture._schema}");
                fixture._created = true;
                Assert.Equal(fixture._schema, await fixture.ScalarAsync("SELECT current_schema()"));
                await fixture.ExecuteAsync("""
                    CREATE TABLE dashcam_events(
                      id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, company_id BIGINT NOT NULL,
                      safety_event_id BIGINT NULL, title VARCHAR(220) NOT NULL, severity VARCHAR(50) NOT NULL,
                      coaching_status VARCHAR(60) NOT NULL DEFAULT 'Needs Review', event_time TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                      event_number VARCHAR(80) NULL, event_type VARCHAR(120) NULL,
                      driver_id BIGINT NULL,vehicle_id BIGINT NULL,job_id BIGINT NULL,route_id BIGINT NULL,
                      location_description VARCHAR(220) NULL, latitude DECIMAL(10,7) NULL,longitude DECIMAL(10,7) NULL,
                      road_facing_clip_url VARCHAR(400) NULL,driver_facing_clip_url VARCHAR(400) NULL,thumbnail_url VARCHAR(400) NULL,
                      video_provider VARCHAR(120) NULL,ai_summary TEXT NULL,ai_confidence DECIMAL(6,2) NULL,
                      review_status VARCHAR(80) NOT NULL DEFAULT 'Pending Review',false_positive BOOLEAN NOT NULL DEFAULT FALSE,
                      evidence_status VARCHAR(80) NOT NULL DEFAULT 'Not Packaged',recommended_action VARCHAR(260) NULL,
                      occurred_at TIMESTAMPTZ NULL,created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),updated_at TIMESTAMPTZ NULL,deleted_at TIMESTAMPTZ NULL);
                    CREATE TABLE safety_events(id BIGINT PRIMARY KEY,company_id BIGINT NOT NULL,branch_id BIGINT,
                      driver_id BIGINT,vehicle_id BIGINT,job_id BIGINT,route_id BIGINT,deleted_at TIMESTAMPTZ);
                    CREATE TABLE drivers(id BIGINT PRIMARY KEY,company_id BIGINT NOT NULL,branch_id BIGINT,assigned_vehicle_id BIGINT,deleted_at TIMESTAMPTZ);
                    CREATE TABLE vehicles(id BIGINT PRIMARY KEY,company_id BIGINT NOT NULL,branch_id BIGINT,assigned_driver_id BIGINT,deleted_at TIMESTAMPTZ);
                    CREATE TABLE jobs(id BIGINT PRIMARY KEY,company_id BIGINT NOT NULL,branch_id BIGINT,assigned_driver_id BIGINT,assigned_vehicle_id BIGINT,route_id BIGINT,deleted_at TIMESTAMPTZ);
                    CREATE TABLE routes(id BIGINT PRIMARY KEY,company_id BIGINT NOT NULL,branch_id BIGINT,assigned_driver_id BIGINT,assigned_vehicle_id BIGINT,deleted_at TIMESTAMPTZ);
                    CREATE TABLE audit_logs(id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,company_id BIGINT NOT NULL,
                      actor_user_id BIGINT,actor_name TEXT,action_name TEXT,entity_name TEXT,entity_id BIGINT,details_json JSONB);
                    CREATE TABLE security_events(id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,company_id BIGINT NOT NULL,
                      user_id BIGINT,event_type TEXT,severity TEXT,source_ip_truncated TEXT,user_agent_hash TEXT,
                      success BOOLEAN,safe_message TEXT,metadata_json JSONB,created_at TIMESTAMPTZ);
                    INSERT INTO drivers VALUES(110,11,9,120,NULL);
                    INSERT INTO vehicles VALUES(120,11,9,110,NULL);
                    INSERT INTO jobs VALUES(130,11,9,110,120,140,NULL);
                    INSERT INTO routes VALUES(140,11,9,110,120,NULL);
                    INSERT INTO safety_events VALUES(150,11,9,110,120,130,140,NULL);
                    """);
                var source = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                    "../../../../database/migrations/2026_09_03_stage100_dashcam_provider_media_truth.sql")));
                await fixture.ExecuteAsync(ExactSlice(source, "ALTER TABLE dashcam_events\n  ADD COLUMN IF NOT EXISTS branch_id", "  ALTER COLUMN ai_confidence DROP NOT NULL;"));
                await fixture.ExecuteAsync(ExactSlice(source, "CREATE OR REPLACE FUNCTION stage100_enforce_dashcam_provider_truth()", "FOR EACH ROW EXECUTE FUNCTION stage100_enforce_dashcam_provider_truth();"));
                await fixture.ExecuteAsync(ExactSlice(source, "DO $stage100$", "ALTER TABLE dashcam_events VALIDATE CONSTRAINT ck_dashcam_ready_media_reference;"));
                Assert.Equal(fixture._schema, await fixture.ScalarAsync("SELECT n.nspname FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE c.oid='dashcam_events'::regclass"));
                Assert.Equal(fixture._schema, await fixture.ScalarAsync("SELECT n.nspname FROM pg_trigger t JOIN pg_proc p ON p.oid=t.tgfoid JOIN pg_namespace n ON n.oid=p.pronamespace WHERE t.tgrelid='dashcam_events'::regclass AND t.tgname='trg_stage100_enforce_dashcam_provider_truth'"));
                Assert.Equal(2L, await fixture.ScalarAsync("SELECT COUNT(*) FROM pg_constraint WHERE conrelid='dashcam_events'::regclass AND convalidated AND conname IN ('ck_dashcam_source_authority','ck_dashcam_ready_media_reference')"));
                fixture.BuildMappings();
                output.WriteLine($"FIXTURE {fixture._schema}: controlled context; no full session/RLS/HTTP startup.");
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        private void BuildMappings()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development, Args = [] });
            builder.Configuration.Sources.Clear();
            builder.Services.AddSingleton(Db);
            builder.Services.AddSingleton(Scopes);
            builder.Services.AddSingleton<AuditService>();
            builder.Services.AddSingleton<SecurityEventService>();
            builder.Services.AddSingleton<IAuthorizationDecisionService, AuthorizationDecisionService>();
            builder.Services.AddSingleton<IAuditLogService, InMemoryAuditLogService>();
            _app = builder.Build();
            _app.MapOpsTraxEndpoints();
            Post = Registered(_app, PostPath, HttpMethods.Post);
            Put = Registered(_app, PutPath, HttpMethods.Put);
        }

        public async Task<Response> InvokeAsync(bool update, string body, Action<HttpContext>? configure = null, bool ambient = false, Func<Task>? afterHandler = null, byte[]? rawBytes = null, Func<Task>? beforeHandler = null)
        {
            var context = new DefaultHttpContext { RequestServices = _app!.Services };
            context.Request.Method = update ? HttpMethods.Put : HttpMethods.Post;
            context.Request.Path = update ? "/api/dashcam/events/1" : PostPath;
            context.Request.RouteValues["id"] = "1";
            context.Request.ContentType = "application/json";
            var bytes = rawBytes ?? Encoding.UTF8.GetBytes(body);
            using var tracked = new TrackingBody(bytes);
            context.Request.Body = tracked;
            context.Request.ContentLength = bytes.Length;
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyDetection());
            context.Response.Body = new MemoryStream();
            context.Items[EndpointMappings.AuthUserIdItemKey] = 7L;
            context.Items[EndpointMappings.AuthCompanyIdItemKey] = 11L;
            context.Items[EndpointMappings.AuthRoleItemKey] = "synthetic-camera-role";
            context.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "dashcam:manage" };
            configure?.Invoke(context);
            var handler = RequestDelegateFactory.Create(update ? Put : Post, new RequestDelegateFactoryOptions
            {
                ServiceProvider = _app.Services,
                RouteParameterNames = update ? new[] { "id" } : Array.Empty<string>(),
                ThrowOnBadRequest = false
            }).RequestDelegate;
            if (ambient)
            {
                await using var scope = await Db.BeginTenantScopeAsync(11);
                Scopes.Current = scope;
                try { if (beforeHandler is not null) await beforeHandler(); await handler(context); if (afterHandler is not null) await afterHandler(); await scope.CompleteAsync(); }
                finally { Scopes.Current = null; }
            }
            else { if (beforeHandler is not null) await beforeHandler(); await handler(context); if (afterHandler is not null) await afterHandler(); }
            context.Response.Body.Position = 0;
            using var reader = new StreamReader(context.Response.Body);
            return new Response(context.Response.StatusCode, await reader.ReadToEndAsync(), tracked.BytesRead);
        }

        public async Task SeedCameraAsync(string authority = "LegacyUnverified")
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO dashcam_events(company_id,title,severity,event_type,source_authority,
                  video_provider,provider_event_id,provider_received_at,provider_payload_hash,media_status,road_facing_media_ref)
                VALUES(11,'synthetic existing','High','Near Miss',@authority,
                  'synthetic-provider','synthetic-event',NOW(),repeat('a',64),'Ready','synthetic-reference')
                """, Owner);
            command.Parameters.AddWithValue("authority", authority);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<string> SnapshotAsync()
            => (string)(await ScalarAsync("SELECT jsonb_build_object('camera',COALESCE((SELECT jsonb_agg(to_jsonb(d) ORDER BY id) FROM dashcam_events d),'[]'::jsonb),'audit',COALESCE((SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_logs a),'[]'::jsonb))::text"))!;

        public async Task ExecuteAsync(string sql)
        {
            await using var command = new NpgsqlCommand(sql, Owner);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<object?> ScalarAsync(string sql)
        {
            await using var command = new NpgsqlCommand(sql, Owner);
            return await command.ExecuteScalarAsync();
        }

        public async Task<NpgsqlConnection> OpenConnectionAsync()
        {
            var connection=new NpgsqlConnection(_settings.ConnectionString);
            try { await connection.OpenAsync(); return connection; }
            catch { await connection.DisposeAsync(); throw; }
        }
        public async Task<AuditBarrier> HoldAuditBarrierAsync()
        {
            await ExecuteAsync("""
                CREATE TABLE synthetic_audit_barrier(id integer PRIMARY KEY);
                INSERT INTO synthetic_audit_barrier VALUES(1);
                CREATE FUNCTION synthetic_wait_for_audit() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN PERFORM 1 FROM synthetic_audit_barrier WHERE id=1 FOR UPDATE; RETURN NEW; END $$;
                CREATE TRIGGER synthetic_audit_wait BEFORE INSERT ON audit_logs FOR EACH ROW EXECUTE FUNCTION synthetic_wait_for_audit();
                """);
            var transaction=await Owner.BeginTransactionAsync();
            try { await ExecuteAsync("SELECT 1 FROM synthetic_audit_barrier WHERE id=1 FOR UPDATE"); return new(transaction); }
            catch { await transaction.DisposeAsync(); throw; }
        }
        public async Task<int> WaitForBlockedAsync(int blocker,int? expectedPid=null)
        {
            using var bound=new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (true)
            {
                // Owner may hold a barrier transaction. Refresh only this observer's
                // cached statistics snapshot so newly started contenders are visible.
                await using (var refresh=new NpgsqlCommand("SELECT pg_stat_clear_snapshot()",Owner))
                    await refresh.ExecuteNonQueryAsync(bound.Token);
                await using var command=new NpgsqlCommand("""
                    SELECT a.pid FROM pg_stat_activity a
                    WHERE a.datname=current_database() AND a.usename=current_user AND a.application_name=@application
                      AND a.pid<>pg_backend_pid() AND @blocker=ANY(pg_blocking_pids(a.pid))
                      AND (@expected::integer IS NULL OR a.pid=@expected)
                      AND EXISTS(SELECT 1 FROM pg_locks l JOIN pg_class c ON c.oid=l.relation JOIN pg_namespace n ON n.oid=c.relnamespace WHERE l.pid=a.pid AND n.nspname=@schema)
                    """,Owner);
                command.Parameters.AddWithValue("application",_schema);
                command.Parameters.AddWithValue("schema",_schema);
                command.Parameters.AddWithValue("blocker",blocker);
                command.Parameters.Add(new NpgsqlParameter("expected",NpgsqlTypes.NpgsqlDbType.Integer) { Value=(object?)expectedPid ?? DBNull.Value });
                var pid=await command.ExecuteScalarAsync(bound.Token);
                if (pid is int id) { _output.WriteLine($"BARRIER {_schema}: backend{id} blocked by owned backend{blocker}."); return id; }
                await Task.Delay(10,bound.Token);
            }
        }
        public static async Task DrainAsync(params Task?[] tasks)
        {
            // Releasing the fixture barrier happens before draining. All DB commands have
            // bounded waits. Observe expected failures here; assertions own the test result.
            var all=Task.WhenAll(tasks.OfType<Task>());
            try { await all.WaitAsync(TimeSpan.FromSeconds(12)); }
            catch when (all.IsCompleted) { }
        }
        public static async Task ReleaseAndDrainAsync(Func<Task> release,params Task?[] tasks)
        {
            Exception? releaseFailure=null;
            try { await release(); }
            catch(Exception ex) { releaseFailure=ex; }
            try { await DrainAsync(tasks); }
            catch(Exception drainFailure) when(releaseFailure is not null)
            {
                throw new AggregateException("Fixture release and task drain both failed; cleanup is not established.",releaseFailure,drainFailure);
            }
            if(releaseFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(releaseFailure).Throw();
        }
        public sealed class AuditBarrier(NpgsqlTransaction transaction) : IAsyncDisposable
        {
            private bool _released;
            public async Task ReleaseAsync()
            {
                if (_released) return;
                await transaction.RollbackAsync();
                _released=true;
            }
            public async ValueTask DisposeAsync() { try { await ReleaseAsync(); } finally { await transaction.DisposeAsync(); } }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_app is not null) await _app.DisposeAsync();
                if (_created)
                {
                    if (Owner.State != System.Data.ConnectionState.Open) await Owner.OpenAsync();
                    await ExecuteAsync("ROLLBACK; SET search_path TO pg_catalog;");
                    await ExecuteAsync($"DROP SCHEMA {_schema} CASCADE");
                    await using var command = new NpgsqlCommand("SELECT COUNT(*) FROM pg_namespace WHERE nspname=@schema", Owner);
                    command.Parameters.AddWithValue("schema", _schema);
                    Assert.Equal(0L, await command.ExecuteScalarAsync());
                    _created = false;
                    _output.WriteLine($"CLEANUP {_schema}: zero generated schema/table/function residue.");
                }
            }
            finally { await Owner.DisposeAsync(); }
        }
    }

    private static string ExactSlice(string source, string start, string end)
    {
        var first = source.IndexOf(start, StringComparison.Ordinal);
        var last = source.IndexOf(end, StringComparison.Ordinal);
        if (first < 0 || last < first || source.IndexOf(start, first + start.Length, StringComparison.Ordinal) >= 0 || source.IndexOf(end, last + end.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("Current migration anchors must be unique and ordered.");
        return source[first..(last + end.Length)];
    }

    private static Delegate Registered(WebApplication app, string path, string method)
    {
        var matches = new List<Delegate>();
        foreach (var source in ((IEndpointRouteBuilder)app).DataSources)
        {
            if (source.GetType().GetField("_routeEntries", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(source) is not IEnumerable entries) continue;
            foreach (var entry in entries)
            {
                if (entry is null || Member(entry, "RoutePattern") is not RoutePattern pattern || pattern.RawText != path || Member(entry, "RouteHandler") is not Delegate handler) continue;
                if (Member(entry, "HttpMethods") is IEnumerable<string> methods && methods.Contains(method, StringComparer.OrdinalIgnoreCase)) matches.Add(handler);
            }
        }
        return Assert.Single(matches);
    }

    private static object? Member(object value, string name)
        => value.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(value)
           ?? value.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(value);

    private sealed class BodyDetection : IHttpRequestBodyDetectionFeature { public bool CanHaveBody => true; }

    private sealed class TrackingBody(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public int BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) { var read = _inner.Read(buffer, offset, count); BytesRead += read; return read; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { var read = await _inner.ReadAsync(buffer, cancellationToken); BytesRead += read; return read; }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
}
