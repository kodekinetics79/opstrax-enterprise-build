using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Security;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class SafetyCustomerReadEvidencePostgresTests
{
    [Fact]
    public async Task CustomerSafetyReads_ExcludeLegacyDemoAndUnverifiedScores()
    {
        var db = Db();
        await new Batch4SchemaService(db).EnsureAsync();
        await new SafetySchemaService(db).EnsureAsync();
        await ApplyMigration(db, "2026_09_08_safety_analytics_evidence_integrity.sql");
        await ApplyMigration(db, "2026_09_08_driver_safety_score_evidence_integrity.sql");

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var companyId = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES(@code,'Safety Read Integrity','logistics') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"SAFE-{suffix}"));
        var qualifiedDriver = await Driver(db, companyId, $"DRV-Q-{suffix}");
        var unverifiedDriver = await Driver(db, companyId, $"DRV-U-{suffix}");

        try
        {
            var qualifiedEvent = await SafetyEvent(db, companyId, qualifiedDriver, $"EV-Q-{suffix}",
                "Speeding", "High", "runtime_detection", "derived_from_qualified_source", 12m);
            var legacyEvent = await SafetyEvent(db, companyId, qualifiedDriver, $"EV-L-{suffix}",
                "Harsh Braking", "Critical", "legacy_unverified", "unverified", 25m);
            _ = await SafetyEvent(db, companyId, unverifiedDriver, $"EV-D-{suffix}",
                "Geofence Breach", "Critical", "demo_seed", "demo_seed", 30m);

            await Score(db, companyId, qualifiedDriver, 88m, 1,
                "runtime_computed", "calculated_from_qualified_sources");
            await Score(db, companyId, unverifiedDriver, 45m, 7,
                "legacy_unverified", "unverified");

            await Coaching(db, companyId, qualifiedDriver, qualifiedEvent, $"COACH-Q-{suffix}",
                "user_workflow", "recorded_by_authenticated_actor");
            await Coaching(db, companyId, qualifiedDriver, legacyEvent, $"COACH-BAD-LINK-{suffix}",
                "user_workflow", "recorded_by_authenticated_actor");
            await Coaching(db, companyId, unverifiedDriver, null, $"COACH-U-{suffix}",
                "legacy_unverified", "unverified");

            var http = Principal(companyId);
            var dashboard = JsonSerializer.Serialize(Data(await Invoke("SafetyDashboard", http, db, CancellationToken.None)));
            Assert.Contains("\"fleetSafetyScore\":88", dashboard);
            Assert.Contains("\"totalEvents\":1", dashboard);
            Assert.Contains("\"openEvents\":1", dashboard);
            Assert.Contains("\"criticalOpen\":0", dashboard);
            Assert.Contains("\"speedingEvents\":1", dashboard);
            Assert.Contains("\"geofenceEvents\":0", dashboard);
            Assert.Contains("\"openCoachingTasks\":1", dashboard);
            Assert.Contains("\"overdueCoachingTasks\":1", dashboard);
            Assert.DoesNotContain("45", dashboard);

            var events = Assert.IsAssignableFrom<IEnumerable>(Data(await Invoke(
                "SafetyEventsList", http, db, CancellationToken.None)))
                .Cast<Dictionary<string, object?>>().ToList();
            var visibleEvent = Assert.Single(events);
            Assert.Equal(qualifiedEvent, Convert.ToInt64(visibleEvent["id"]));
            Assert.Equal("derived_from_qualified_source", visibleEvent["verificationStatus"]);
            Assert.NotNull(visibleEvent["coachingTaskId"]);

            var qualifiedDetail = JsonSerializer.Serialize(Data(await Invoke(
                "SafetyEventGet", http, qualifiedEvent, db, CancellationToken.None)));
            Assert.Contains($"\"id\":{qualifiedEvent}", qualifiedDetail);
            Assert.Contains("\"coachingTasks\":[{", qualifiedDetail);
            Assert.Contains("\"dataOrigin\":\"user_workflow\"", qualifiedDetail);

            var hiddenDetail = await Invoke("SafetyEventGet", http, legacyEvent, db, CancellationToken.None);
            Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(hiddenDetail).StatusCode);

            var scores = Assert.IsAssignableFrom<IEnumerable>(Data(await Invoke(
                "SafetyDriverScores", http, db, CancellationToken.None)))
                .Cast<Dictionary<string, object?>>().ToList();
            var visibleScore = Assert.Single(scores);
            Assert.Equal(qualifiedDriver, Convert.ToInt64(visibleScore["driverId"]));
            Assert.Equal(88m, Convert.ToDecimal(visibleScore["score30D"]));
            Assert.Equal("calculated_from_qualified_sources", visibleScore["verificationStatus"]);
        }
        finally
        {
            foreach (var table in new[] { "coaching_tasks", "driver_safety_scores", "safety_events", "drivers" })
                await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
        }
    }

    private static async Task ApplyMigration(Database db, string name) =>
        await db.ExecuteAsync(File.ReadAllText(Path.Combine(RepoRoot, "database", "migrations", name)));

    private static Task<long> Driver(Database db, long companyId, string code) => db.InsertAsync(
        "INSERT INTO drivers(company_id,driver_code,full_name,email,status) VALUES(@cid,@code,@code,@email,'Available') RETURNING id",
        c =>
        {
            c.Parameters.AddWithValue("@cid", companyId);
            c.Parameters.AddWithValue("@code", code);
            c.Parameters.AddWithValue("@email", $"{code.ToLowerInvariant()}@example.test");
        });

    private static Task<long> SafetyEvent(Database db, long companyId, long driverId, string number,
        string type, string severity, string origin, string verification, decimal impact) => db.InsertAsync(
        @"INSERT INTO safety_events
            (company_id,event_number,driver_id,event_type,severity,status,review_status,event_time,occurred_at,
             score_impact,data_origin,verification_status)
          VALUES(@cid,@number,@driver,@type,@severity,'open','New',NOW(),NOW(),@impact,@origin,@verification)
          RETURNING id",
        c =>
        {
            c.Parameters.AddWithValue("@cid", companyId);
            c.Parameters.AddWithValue("@number", number);
            c.Parameters.AddWithValue("@driver", driverId);
            c.Parameters.AddWithValue("@type", type);
            c.Parameters.AddWithValue("@severity", severity);
            c.Parameters.AddWithValue("@impact", impact);
            c.Parameters.AddWithValue("@origin", origin);
            c.Parameters.AddWithValue("@verification", verification);
        });

    private static Task Score(Database db, long companyId, long driverId, decimal score, int events,
        string origin, string verification) => db.ExecuteAsync(
        @"INSERT INTO driver_safety_scores
            (company_id,driver_id,score_7d,score_30d,score_90d,events_7d,events_30d,events_90d,
             breakdown_json,computed_at,data_origin,verification_status)
          VALUES(@cid,@driver,@score,@score,@score,@events,@events,@events,'{}'::jsonb,NOW(),@origin,@verification)
          ON CONFLICT(company_id,driver_id) DO UPDATE SET
            score_7d=EXCLUDED.score_7d,score_30d=EXCLUDED.score_30d,score_90d=EXCLUDED.score_90d,
            events_7d=EXCLUDED.events_7d,events_30d=EXCLUDED.events_30d,events_90d=EXCLUDED.events_90d,
            computed_at=NOW(),data_origin=EXCLUDED.data_origin,verification_status=EXCLUDED.verification_status",
        c =>
        {
            c.Parameters.AddWithValue("@cid", companyId);
            c.Parameters.AddWithValue("@driver", driverId);
            c.Parameters.AddWithValue("@score", score);
            c.Parameters.AddWithValue("@events", events);
            c.Parameters.AddWithValue("@origin", origin);
            c.Parameters.AddWithValue("@verification", verification);
        });

    private static Task Coaching(Database db, long companyId, long driverId, long? eventId, string number,
        string origin, string verification) => db.ExecuteAsync(
        @"INSERT INTO coaching_tasks
            (company_id,task_number,driver_id,safety_event_id,coaching_type,priority,status,title,description,due_at,
             data_origin,verification_status)
          VALUES(@cid,@number,@driver,@event,'Speed Management','High','Assigned',@number,'Recorded coaching',
                 NOW()-INTERVAL '1 day',@origin,@verification)",
        c =>
        {
            c.Parameters.AddWithValue("@cid", companyId);
            c.Parameters.AddWithValue("@number", number);
            c.Parameters.AddWithValue("@driver", driverId);
            c.Parameters.AddWithValue("@event", (object?)eventId ?? DBNull.Value);
            c.Parameters.AddWithValue("@origin", origin);
            c.Parameters.AddWithValue("@verification", verification);
        });

    private static DefaultHttpContext Principal(long companyId)
    {
        var pii = new PiiProtectionService(new DisabledKeyProvider(), NullLogger<PiiProtectionService>.Instance);
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(pii).BuildServiceProvider()
        };
        http.Items[EndpointMappings.AuthUserIdItemKey] = 91L;
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Company Admin";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "safety:view" };
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

    private static object Data(IResult result)
    {
        var value = Assert.IsAssignableFrom<IValueHttpResult>(result).Value!;
        return value.GetType().GetProperty("Data")!.GetValue(value)!;
    }

    private sealed class DisabledKeyProvider : IDataKeyProvider
    {
        public (byte KeyId, byte[] Key) ActiveKey => (0, new byte[32]);
        public byte[]? ResolveKey(byte keyId) => null;
        public byte[] IndexKey => new byte[32];
        public bool IsConfigured => false;
    }

    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
        ["Rls:EnforceTenantContext"] = "false"
    }).Build());
}
