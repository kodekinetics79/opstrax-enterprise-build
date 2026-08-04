using Opstrax.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Services;
using System.Reflection;
using System.Text.Json;

namespace Opstrax.Tests;

public sealed class SafetyCoachingScorecardPilotTests
{
    [Theory]
    [InlineData("Draft", "Assigned")]
    [InlineData("Draft", "Cancelled")]
    [InlineData("Open", "Assigned")]
    [InlineData("Assigned", "Driver Acknowledged")]
    [InlineData("Assigned", "Escalated")]
    [InlineData("Driver Acknowledged", "Completed")]
    [InlineData("Escalated", "Assigned")]
    [InlineData("Escalated", "Completed")]
    public void WorkflowAllowsOnlyDocumentedForwardTransitions(string current, string target)
        => Assert.True(EndpointMappings.AllowedCoachingTransition(current, target));

    [Theory]
    [InlineData("Draft", "Completed")]
    [InlineData("Assigned", "Completed")]
    [InlineData("Completed", "Assigned")]
    [InlineData("Completed", "Driver Acknowledged")]
    [InlineData("Cancelled", "Assigned")]
    [InlineData("Driver Acknowledged", "Draft")]
    [InlineData("Assigned", "Draft")]
    public void WorkflowRejectsBypassesAndTerminalStateReentry(string current, string target)
        => Assert.False(EndpointMappings.AllowedCoachingTransition(current, target));

    [Fact]
    public void WorkflowComparisonIsCaseAndWhitespaceTolerant()
        => Assert.True(EndpointMappings.AllowedCoachingTransition(" assigned ", " driver acknowledged "));
}

[Trait("Category", "Integration")]
public sealed class SafetyCoachingScorecardPilotPostgresTests
{
    [Fact]
    public async Task ScoreFormulaExcludesDismissedDeletedAndHonorsWindows()
    {
        var db = Db();
        await new Batch4SchemaService(db).EnsureAsync();
        await new SafetySchemaService(db).EnsureAsync();
        var company = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(3_000_000, 3_900_000);
        await db.ExecuteAsync("INSERT INTO companies(id,company_code,name,industry) OVERRIDING SYSTEM VALUE VALUES(@id,@code,'Score Formula Test','Transportation')", c => { c.Parameters.AddWithValue("@id", company); c.Parameters.AddWithValue("@code", $"SCORE-{company}"); });
        try
        {
            var driver = await Driver(db, company, 99201, $"S-{company}", "Scored Driver");
            async Task Add(decimal impact, int days, string status, bool deleted = false) => await db.ExecuteAsync(
                @"INSERT INTO safety_events(company_id,driver_id,event_type,severity,score_impact,status,event_time,deleted_at)
                  VALUES(@c,@d,'Speeding','High',@impact,@status,NOW()-@days*INTERVAL '1 day',CASE WHEN @deleted THEN NOW() ELSE NULL END)",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@d", driver); c.Parameters.AddWithValue("@impact", impact); c.Parameters.AddWithValue("@status", status); c.Parameters.AddWithValue("@days", days); c.Parameters.AddWithValue("@deleted", deleted); });
            await Add(10, 1, "open");
            await Add(20, 10, "open");
            await Add(50, 1, "dismissed");
            await Add(40, 1, "open", deleted: true);

            var seven = await SafetyBackgroundService.ComputeScoreAsync(db, company, driver, 7, CancellationToken.None);
            var thirty = await SafetyBackgroundService.ComputeScoreAsync(db, company, driver, 30, CancellationToken.None);
            Assert.Equal(90m, seven.Score); Assert.Equal(1, seven.Events);
            Assert.Equal(70m, thirty.Score); Assert.Equal(2, thirty.Events);
            Assert.Contains("Speeding", thirty.Breakdown);
        }
        finally
        {
            foreach (var sql in new[] { "DELETE FROM safety_events WHERE company_id=@c", "DELETE FROM drivers WHERE company_id=@c", "DELETE FROM companies WHERE id=@c" })
                await db.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@c", company));
        }
    }

    [Fact]
    public async Task RealPilotScenarioEnforcesPermissionsBranchIdempotencyConcurrencyAndExplainability()
    {
        var db = Db();
        await new Batch4SchemaService(db).EnsureAsync();
        await new SafetySchemaService(db).EnsureAsync();
        var company = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(2_000_000, 2_900_000);
        var otherCompany = company + 1;
        const long branchA = 99101;
        const long branchB = 99102;
        await db.ExecuteAsync("INSERT INTO companies(id,company_code,name,industry) OVERRIDING SYSTEM VALUE VALUES(@id,@code,'Safety Pilot Test','Transportation')",
            c => { c.Parameters.AddWithValue("@id", company); c.Parameters.AddWithValue("@code", $"SAFE-{company}"); });
        try
        {
            var user = await db.InsertAsync("INSERT INTO users(company_id,branch_id,full_name,email,role_name,status) VALUES(@c,@b,'Safety Manager',@email,'Safety Manager','Active')",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branchA); c.Parameters.AddWithValue("@email", $"safety-{company}@example.invalid"); });
            var branchBUser = await db.InsertAsync("INSERT INTO users(company_id,branch_id,full_name,email,role_name,status) VALUES(@c,@b,'Other Branch',@email,'Safety Manager','Active')", c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branchB); c.Parameters.AddWithValue("@email", $"branch-b-{company}@example.invalid"); });
            var inactiveUser = await db.InsertAsync("INSERT INTO users(company_id,branch_id,full_name,email,role_name,status) VALUES(@c,@b,'Inactive Assignee',@email,'Safety Manager','Inactive')", c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branchA); c.Parameters.AddWithValue("@email", $"inactive-{company}@example.invalid"); });
            await db.ExecuteAsync("INSERT INTO companies(id,company_code,name,industry) OVERRIDING SYSTEM VALUE VALUES(@id,@code,'Other Tenant','Transportation')", c => { c.Parameters.AddWithValue("@id", otherCompany); c.Parameters.AddWithValue("@code", $"OTHER-{otherCompany}"); });
            var otherTenantUser = await db.InsertAsync("INSERT INTO users(company_id,full_name,email,role_name,status) VALUES(@c,'Other Tenant User',@email,'Safety Manager','Active')", c => { c.Parameters.AddWithValue("@c", otherCompany); c.Parameters.AddWithValue("@email", $"other-{otherCompany}@example.invalid"); });
            var driverA = await Driver(db, company, branchA, $"A-{company}", "Branch A Driver");
            var driverB = await Driver(db, company, branchB, $"B-{company}", "Branch B Driver");
            await db.ExecuteAsync("UPDATE drivers SET user_id=@u WHERE id=@d AND company_id=@c", c => { c.Parameters.AddWithValue("@u", user); c.Parameters.AddWithValue("@d", driverA); c.Parameters.AddWithValue("@c", company); });
            await db.ExecuteAsync("UPDATE drivers SET user_id=@u WHERE id=@d AND company_id=@c", c => { c.Parameters.AddWithValue("@u", branchBUser); c.Parameters.AddWithValue("@d", driverB); c.Parameters.AddWithValue("@c", company); });
            await db.ExecuteAsync(@"INSERT INTO driver_safety_scores(company_id,driver_id,score_7d,score_30d,score_90d,events_7d,events_30d,events_90d,breakdown_json)
                                    VALUES(@c,@d,80,72,76,1,2,3,'{""Harsh Braking"":{""count"":2,""impact"":28}}'::jsonb)
                                    ON CONFLICT(company_id,driver_id) DO UPDATE SET score_30d=72,breakdown_json=EXCLUDED.breakdown_json,computed_at=NOW()",
                c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@d", driverA); });

            var forbidden = await Invoke("PilotCreateCoachingTask", Principal(company, branchA, user, "safety:view"), Body(driverA, "idem-forbidden"), db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<IStatusCodeHttpResult>(forbidden).StatusCode);
            foreach (var missingField in new[] { "driverId", "title", "description" })
            {
                var invalidBody = Body(driverA, $"missing-{missingField}"); invalidBody[missingField] = "   ";
                var invalidResult = await Invoke("PilotCreateCoachingTask", Principal(company, branchA, user, "safety:create"), invalidBody, db, new AuditService(db), CancellationToken.None);
                Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(invalidResult).StatusCode);
            }
            foreach (var invalidAssignee in new[] { branchBUser, otherTenantUser, inactiveUser })
            {
                var invalidBody = Body(driverA, $"bad-assignee-{invalidAssignee}"); invalidBody["assignedToUserId"] = invalidAssignee;
                var invalidResult = await Invoke("PilotCreateCoachingTask", Principal(company, branchA, user, "safety:create"), invalidBody, db, new AuditService(db), CancellationToken.None);
                Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(invalidResult).StatusCode);
            }

            var body = Body(driverA, "idem-replay");
            var created = await Invoke("PilotCreateCoachingTask", Principal(company, branchA, user, "safety:create"), body, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status201Created, Assert.IsAssignableFrom<IStatusCodeHttpResult>(created).StatusCode);
            var replay = await Invoke("PilotCreateCoachingTask", Principal(company, branchA, user, "safety:create"), body, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(replay).StatusCode);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM coaching_tasks WHERE company_id=@c AND idempotency_key='idem-replay'", c => c.Parameters.AddWithValue("@c", company)));
            var raceBody = Body(driverA, "idem-concurrent");
            var raceDbA = Db(); var raceDbB = Db();
            var raced = await Task.WhenAll(
                Invoke("PilotCreateCoachingTask", Principal(company, branchA, user, "safety:create"), raceBody, raceDbA, new AuditService(raceDbA), CancellationToken.None),
                Invoke("PilotCreateCoachingTask", Principal(company, branchA, user, "safety:create"), raceBody, raceDbB, new AuditService(raceDbB), CancellationToken.None));
            Assert.All(raced, result => Assert.Contains(Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode ?? 0, new[] { StatusCodes.Status200OK, StatusCodes.Status201Created }));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM coaching_tasks WHERE company_id=@c AND idempotency_key='idem-concurrent'", c => c.Parameters.AddWithValue("@c", company)));

            var conflictingBody = Body(driverA, "idem-replay");
            conflictingBody["title"] = "Different request";
            var conflict = await Invoke("PilotCreateCoachingTask", Principal(company, branchA, user, "safety:create"), conflictingBody, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<IStatusCodeHttpResult>(conflict).StatusCode);
            var deepConflictBody = Body(driverA, "idem-replay");
            deepConflictBody["description"] = "Different material description";
            var deepConflict = await Invoke("PilotCreateCoachingTask", Principal(company, branchA, user, "safety:create"), deepConflictBody, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<IStatusCodeHttpResult>(deepConflict).StatusCode);

            var branchBCreate = await Invoke("PilotCreateCoachingTask", Principal(company, branchB, user, "safety:create"), Body(driverB, "idem-branch-b"), db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status201Created, Assert.IsAssignableFrom<IStatusCodeHttpResult>(branchBCreate).StatusCode);
            var listA = Assert.IsAssignableFrom<IValueHttpResult>(await Invoke("PilotCoachingTasks", Principal(company, branchA, user, "safety:view"), db, CancellationToken.None));
            var listJson = JsonSerializer.Serialize(listA.Value);
            Assert.Contains("Branch A Driver", listJson);
            Assert.DoesNotContain("Branch B Driver", listJson);

            var tenantWideCreator = Principal(company, branchA, user, "safety:create", "safety:update");
            tenantWideCreator.Items.Remove(EndpointMappings.AuthBranchIdItemKey);
            Assert.Equal(StatusCodes.Status201Created, Assert.IsAssignableFrom<IStatusCodeHttpResult>(
                await Invoke("PilotCreateCoachingTask", tenantWideCreator, Body(driverA, "idem-tenant-wide"), db, new AuditService(db), CancellationToken.None)).StatusCode);
            var tenantWideTaskId = Convert.ToInt64((await db.QuerySingleAsync("SELECT id FROM coaching_tasks WHERE company_id=@c AND idempotency_key='idem-tenant-wide'", c => c.Parameters.AddWithValue("@c", company)))!["id"]);
            Assert.Equal(branchA, await db.ScalarLongAsync("SELECT branch_id FROM coaching_tasks WHERE id=@id", c => c.Parameters.AddWithValue("@id", tenantWideTaskId)));
            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(await Invoke("PilotCoachingAddNote", tenantWideCreator, tenantWideTaskId,
                new Dictionary<string, object?> { ["noteText"] = "Tenant-wide manager note" }, db, new AuditService(db), CancellationToken.None)).StatusCode);
            Assert.Equal(branchA, await db.ScalarLongAsync("SELECT branch_id FROM coaching_notes WHERE coaching_task_id=@id", c => c.Parameters.AddWithValue("@id", tenantWideTaskId)));
            Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(await Invoke("PilotCoachingAddNote", Principal(company, branchB, branchBUser, "safety:update"), tenantWideTaskId,
                new Dictionary<string, object?> { ["noteText"] = "Cross-branch note" }, db, new AuditService(db), CancellationToken.None)).StatusCode);
            await db.ExecuteAsync("UPDATE coaching_tasks SET deleted_at=NOW() WHERE id=@id", c => c.Parameters.AddWithValue("@id", tenantWideTaskId));
            Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(await Invoke("PilotCoachingAddNote", tenantWideCreator, tenantWideTaskId,
                new Dictionary<string, object?> { ["noteText"] = "Note after deletion" }, db, new AuditService(db), CancellationToken.None)).StatusCode);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM coaching_notes WHERE coaching_task_id=@id", c => c.Parameters.AddWithValue("@id", tenantWideTaskId)));

            var editableBody = Body(driverA, "idem-edit");
            Assert.Equal(StatusCodes.Status201Created, Assert.IsAssignableFrom<IStatusCodeHttpResult>(
                await Invoke("PilotCreateCoachingTask", Principal(company, branchA, user, "safety:create"), editableBody, db, new AuditService(db), CancellationToken.None)).StatusCode);
            var editableId = Convert.ToInt64((await db.QuerySingleAsync("SELECT id FROM coaching_tasks WHERE company_id=@c AND idempotency_key='idem-edit'", c => c.Parameters.AddWithValue("@c", company)))!["id"]);
            var edit = await Invoke("PilotUpdateCoachingTask", Principal(company, branchA, user, "safety:update"), editableId,
                new Dictionary<string, object?> { ["rowVersion"] = 0L, ["title"] = "Edited coaching title", ["priority"] = "Medium" },
                db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(edit).StatusCode);
            var edited = (await db.QuerySingleAsync("SELECT title,priority,status,row_version FROM coaching_tasks WHERE id=@id", c => c.Parameters.AddWithValue("@id", editableId)))!;
            Assert.Equal("Edited coaching title", edited["title"]);
            Assert.Equal("Medium", edited["priority"]);
            Assert.Equal("Draft", edited["status"]);
            Assert.Equal(1, Convert.ToInt64(edited["rowVersion"]));
            var blankEdit = await Invoke("PilotUpdateCoachingTask", Principal(company, branchA, user, "safety:update"), editableId,
                new Dictionary<string, object?> { ["rowVersion"] = 1L, ["description"] = "   " },
                db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(blankEdit).StatusCode);

            var taskId = Convert.ToInt64((await db.QuerySingleAsync("SELECT id FROM coaching_tasks WHERE company_id=@c AND idempotency_key='idem-replay'", c => c.Parameters.AddWithValue("@c", company)))!["id"]);
            var assign = await Invoke("PilotCoachingAssign", Principal(company, branchA, user, "safety:update"), taskId,
                new Dictionary<string, object?> { ["rowVersion"] = 0L, ["assignedToUserId"] = user }, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(assign).StatusCode);
            var stale = await Invoke("PilotCoachingAssign", Principal(company, branchA, user, "safety:update"), taskId,
                new Dictionary<string, object?> { ["rowVersion"] = 0L, ["assignedToUserId"] = user }, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<IStatusCodeHttpResult>(stale).StatusCode);
            var adminAck = await Invoke("PilotCoachingAcknowledge", Principal(company, branchA, user, "safety:review"), taskId,
                new Dictionary<string, object?> { ["rowVersion"] = 1L }, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<IStatusCodeHttpResult>(adminAck).StatusCode);
            var foreignDriverAck = await Invoke("PilotCoachingAcknowledge", Principal(company, branchB, branchBUser, "driver:self"), taskId,
                new Dictionary<string, object?> { ["rowVersion"] = 1L }, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<IStatusCodeHttpResult>(foreignDriverAck).StatusCode);
            var driverAck = await Invoke("PilotCoachingAcknowledge", Principal(company, branchA, user, "driver:self"), taskId,
                new Dictionary<string, object?> { ["rowVersion"] = 1L, ["note"] = "I will increase following distance and review the next safety check-in." }, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(driverAck).StatusCode);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM coaching_notes WHERE coaching_task_id=@id AND note_type='Driver Acknowledgement'", c => c.Parameters.AddWithValue("@id", taskId)));
            var illegal = await Invoke("PilotCoachingComplete", Principal(company, branchA, user, "safety:update"), 999999999L,
                new Dictionary<string, object?> { ["rowVersion"] = 0L, ["completionNote"] = "Documented outcome", ["afterSafetyScore"] = 82m }, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(illegal).StatusCode);

            var incompleteEvidence = await Invoke("PilotCoachingComplete", Principal(company, branchA, user, "safety:update"), taskId,
                new Dictionary<string, object?> { ["rowVersion"] = 2L }, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(incompleteEvidence).StatusCode);

            var dbA = Db(); var dbB = Db();
            var completion = new Dictionary<string, object?> { ["rowVersion"] = 2L, ["completionNote"] = "Driver demonstrated the corrective technique and agreed to a 14-day follow-up.", ["afterSafetyScore"] = 84m };
            var simultaneous = await Task.WhenAll(
                Invoke("PilotCoachingComplete", Principal(company, branchA, user, "safety:update"), taskId, completion, dbA, new AuditService(dbA), CancellationToken.None),
                Invoke("PilotCoachingComplete", Principal(company, branchA, user, "safety:update"), taskId, completion, dbB, new AuditService(dbB), CancellationToken.None));
            Assert.Single(simultaneous.Where(r => (r as IStatusCodeHttpResult)?.StatusCode == StatusCodes.Status200OK));
            Assert.Single(simultaneous.Where(r => (r as IStatusCodeHttpResult)?.StatusCode == StatusCodes.Status409Conflict));
            Assert.Equal("Completed", (await db.QuerySingleAsync("SELECT status FROM coaching_tasks WHERE id=@id AND company_id=@c", c => { c.Parameters.AddWithValue("@id", taskId); c.Parameters.AddWithValue("@c", company); }))!["status"]);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM coaching_notes WHERE coaching_task_id=@id AND note_type='Completion Outcome'", c => c.Parameters.AddWithValue("@id", taskId)));

            await db.ExecuteAsync(@"INSERT INTO ai_recommendations(company_id,tenant_id,recommendation_type,module_key,title,summary,body,score,status)
                                    VALUES(@c,@c,'coaching','coaching','Tenant-wide coaching narrative','Sensitive cross-branch coaching narrative','Sensitive cross-branch coaching narrative',99,'Recommended')",
                c => c.Parameters.AddWithValue("@c", company));
            var detail = Assert.IsAssignableFrom<IValueHttpResult>(await Invoke("PilotCoachingTaskDetail", Principal(company, branchA, user, "safety:view"), taskId, db, CancellationToken.None));
            var branchDetailJson = JsonSerializer.Serialize(detail.Value);
            Assert.Contains("\"recommendations\":[]", branchDetailJson);
            Assert.DoesNotContain("Sensitive cross-branch coaching narrative", branchDetailJson);
            var tenantWidePrincipal = Principal(company, branchA, user, "safety:view");
            tenantWidePrincipal.Items.Remove(EndpointMappings.AuthBranchIdItemKey);
            var tenantWideDetail = Assert.IsAssignableFrom<IValueHttpResult>(await Invoke("PilotCoachingTaskDetail", tenantWidePrincipal, taskId, db, CancellationToken.None));
            Assert.Contains("Sensitive cross-branch coaching narrative", JsonSerializer.Serialize(tenantWideDetail.Value));

            _ = await Driver(db, company, branchA, $"UNKNOWN-{company}", "Unscored Driver");
            var trends = Assert.IsAssignableFrom<IValueHttpResult>(await Invoke("SafetyScorecardTrends", Principal(company, branchA, user, "safety:view"), db, CancellationToken.None));
            var trendJson = JsonSerializer.Serialize(trends.Value);
            Assert.Contains("\"scoredDrivers\":1", trendJson);
            Assert.DoesNotContain("\"scoredDrivers\":2", trendJson);

            await db.ExecuteAsync("UPDATE driver_safety_scores SET score_30d=60,computed_at=NOW() WHERE company_id=@c AND driver_id=@d", c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@d", driverA); });
            var currentSummary = Assert.IsAssignableFrom<IValueHttpResult>(await Invoke("SafetyScorecardSummary", Principal(company, branchA, user, "safety:view"), db, CancellationToken.None));
            Assert.Contains("\"coachingNeeded\":1", JsonSerializer.Serialize(currentSummary.Value));
            await db.ExecuteAsync("UPDATE driver_safety_scores SET computed_at=NOW()-INTERVAL '3 hours' WHERE company_id=@c AND driver_id=@d", c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@d", driverA); });
            var staleSummary = Assert.IsAssignableFrom<IValueHttpResult>(await Invoke("SafetyScorecardSummary", Principal(company, branchA, user, "safety:view"), db, CancellationToken.None));
            Assert.Contains("\"coachingNeeded\":0", JsonSerializer.Serialize(staleSummary.Value));
            var scorecards = Assert.IsAssignableFrom<IValueHttpResult>(await Invoke("SafetyDriverScorecardsPilot", Principal(company, branchA, user, "safety:view"), db, CancellationToken.None));
            var scoreJson = JsonSerializer.Serialize(scorecards.Value);
            Assert.Contains("Branch A Driver", scoreJson);
            Assert.Contains("Unscored Driver", scoreJson);
            Assert.DoesNotContain("Branch B Driver", scoreJson);
            Assert.Contains("scoreFormula", scoreJson);
            Assert.Contains("formulaVersion", scoreJson);
            Assert.Contains("sourceWindowStart", scoreJson);
            Assert.Contains("sourceWindowEnd", scoreJson);
            Assert.Contains("sourceEventCount", scoreJson);
            Assert.Contains("calculationSource", scoreJson);
            Assert.Contains("stale", scoreJson);
            Assert.Contains("insufficient_data", scoreJson);
            Assert.Contains("Harsh Braking", scoreJson);
        }
        finally
        {
            foreach (var sql in new[] { "DELETE FROM coaching_notes WHERE company_id=@c", "DELETE FROM coaching_tasks WHERE company_id=@c", "DELETE FROM driver_safety_scores WHERE company_id=@c", "DELETE FROM ai_recommendations WHERE company_id=@c", "DELETE FROM audit_logs WHERE company_id=@c", "DELETE FROM users WHERE company_id=@c", "DELETE FROM drivers WHERE company_id=@c", "DELETE FROM companies WHERE id=@c" })
                await db.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@c", company));
            await db.ExecuteAsync("DELETE FROM users WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", otherCompany));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", otherCompany));
        }
    }

    private static Dictionary<string, object?> Body(long driver, string key) => new()
    {
        ["driverId"] = driver, ["coachingType"] = "Braking Behavior", ["title"] = "Targeted braking review",
        ["description"] = "Review braking telemetry and agree on a documented corrective action.",
        ["priority"] = "High", ["dueAt"] = DateTimeOffset.UtcNow.AddDays(7).ToString("O"), ["idempotencyKey"] = key
    };

    private static async Task<IResult> Invoke(string method, params object[] args)
    {
        var target = typeof(EndpointMappings).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!;
        try { return await (Task<IResult>)target.Invoke(null, args)!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString, ["Rls:EnforceTenantContext"] = "false" }).Build());

    private static DefaultHttpContext Principal(long company, long branch, long user, params string[] permissions)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = company;
        http.Items[EndpointMappings.AuthBranchIdItemKey] = branch;
        http.Items[EndpointMappings.AuthUserIdItemKey] = user;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Safety Manager";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = permissions;
        return http;
    }

    private static Task<long> Driver(Database db, long company, long branch, string code, string name) => db.InsertAsync(
        "INSERT INTO drivers(company_id,branch_id,driver_code,full_name,status,safety_score,readiness_score,compliance_score) VALUES(@c,@b,@code,@name,'Available',95,95,95)",
        c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@b", branch); c.Parameters.AddWithValue("@code", code); c.Parameters.AddWithValue("@name", name); });
}
