using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class SafetyIncidentsPilotRegressionTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task PersistedIncidentScenario_EnforcesScopeLifecycleConcurrencyAndIdempotency()
    {
        var db = Db();
        await new Batch4SchemaService(db).EnsureAsync();
        var companyA = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(4_100_000, 4_900_000);
        var companyB = companyA + 1;
        const long branchA = 65101;
        const long branchB = 65102;
        await SeedCompany(db, companyA, "A"); await SeedCompany(db, companyB, "B");
        try
        {
            var driverA = await db.InsertAsync("INSERT INTO drivers(company_id,branch_id,driver_code,full_name,status) VALUES(@c,@b,@code,'Incident Driver','Available')", c => { c.Parameters.AddWithValue("@c", companyA); c.Parameters.AddWithValue("@b", branchA); c.Parameters.AddWithValue("@code", $"DRV-INC-{companyA}"); });
            var body = new Dictionary<string, object?>
            {
                ["incidentType"] = "Near Miss", ["severity"] = "High", ["status"] = "New",
                ["driverId"] = driverA, ["occurredAt"] = "2026-08-02T12:30:00Z", ["locationDescription"] = "Pilot yard",
                ["aiSummary"] = "Driver reported a low-speed near miss at the yard gate.", ["idempotencyKey"] = $"incident-{companyA}"
            };
            var replayBody = new Dictionary<string, object?>(body);
            var created = await Invoke("CreateIncident", Principal(companyA, branchA), body, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status201Created, Status(created));
            var incident = (await db.QuerySingleAsync("SELECT id,row_version,branch_id FROM incidents WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyA)))!;
            var incidentId = Convert.ToInt64(incident["id"]);
            Assert.Equal(branchA, Convert.ToInt64(incident["branchId"]));
            var viewOnlyDelete = Principal(companyA, branchA); viewOnlyDelete.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "safety:view" }; viewOnlyDelete.Request.Headers.IfMatch = "1";
            Assert.Equal(StatusCodes.Status403Forbidden, Status(await Invoke("DeleteIncident", viewOnlyDelete, incidentId, db, new AuditService(db), CancellationToken.None)));

            var replay = await Invoke("CreateIncident", Principal(companyA, branchA), replayBody, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(replay));
            Assert.Equal(1, await Count(db, "incidents", companyA));
            var conflictingBody = new Dictionary<string, object?>(body) { ["locationDescription"] = "Different payload" };
            var keyConflict = await Invoke("CreateIncident", Principal(companyA, branchA), conflictingBody, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Status(keyConflict));

            Assert.Equal(StatusCodes.Status404NotFound, Status(await Invoke("IncidentDetail", Principal(companyA, branchB), incidentId, db, CancellationToken.None)));
            Assert.Equal(StatusCodes.Status404NotFound, Status(await Invoke("IncidentDetail", Principal(companyB, branchA), incidentId, db, CancellationToken.None)));

            var update = await Invoke("UpdateIncident", Principal(companyA, branchA), incidentId,
                new Dictionary<string, object?> { ["severity"] = "Critical", ["rowVersion"] = 1L }, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(update));
            var stale = await Invoke("UpdateIncident", Principal(companyA, branchA), incidentId,
                new Dictionary<string, object?> { ["severity"] = "Low", ["rowVersion"] = 1L }, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status409Conflict, Status(stale));
            Assert.Equal(StatusCodes.Status404NotFound, Status(await Invoke("UpdateIncident", Principal(companyA, branchA), incidentId + 999_999,
                new Dictionary<string, object?> { ["severity"] = "Low", ["rowVersion"] = 1L }, db, new AuditService(db), CancellationToken.None)));

            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("IncidentStatus", Principal(companyA, branchA), incidentId,
                new Dictionary<string, object?> { ["status"] = "Under Review", ["rowVersion"] = 2L }, db, new AuditService(db), CancellationToken.None)));
            Assert.Equal(StatusCodes.Status409Conflict, Status(await Invoke("IncidentStatus", Principal(companyA, branchA), incidentId,
                new Dictionary<string, object?> { ["status"] = "Evidence Collected", ["rowVersion"] = 3L }, db, new AuditService(db), CancellationToken.None)));

            Assert.Equal(StatusCodes.Status400BadRequest, Status(await Invoke("IncidentAttachEvidence", Principal(companyA, branchA), incidentId,
                new Dictionary<string, object?> { ["evidenceType"] = "Photo", ["evidenceTitle"] = "Metadata only", ["rowVersion"] = 3L }, db, new AuditService(db), CancellationToken.None)));
            var tenantWideAdmin = Principal(companyA, branchA);
            tenantWideAdmin.Items.Remove(EndpointMappings.AuthBranchIdItemKey);
            var attach = await Invoke("IncidentAttachEvidence", tenantWideAdmin, incidentId,
                new Dictionary<string, object?> { ["evidenceType"] = "Photo", ["evidenceTitle"] = "Scene overview", ["evidenceUrl"] = "https://evidence.example.invalid/scene.jpg", ["contentHash"] = new string('a', 64), ["rowVersion"] = 3L }, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(attach));
            Assert.Equal(branchA, await db.ScalarLongAsync("SELECT branch_id FROM incident_evidence WHERE incident_id=@id", c => c.Parameters.AddWithValue("@id", incidentId)));
            Assert.Equal(StatusCodes.Status404NotFound, Status(await Invoke("IncidentAttachEvidence", Principal(companyA, branchB), incidentId,
                new Dictionary<string, object?> { ["evidenceType"] = "Photo", ["evidenceTitle"] = "Cross branch", ["evidenceUrl"] = "https://evidence.example.invalid/cross.jpg", ["contentHash"] = new string('b', 64), ["rowVersion"] = 4L }, db, new AuditService(db), CancellationToken.None)));
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("IncidentStatus", Principal(companyA, branchA), incidentId,
                new Dictionary<string, object?> { ["status"] = "Evidence Collected", ["rowVersion"] = 4L }, db, new AuditService(db), CancellationToken.None)));

            var report = await Invoke("IncidentCreateInsuranceReport", tenantWideAdmin, incidentId,
                new Dictionary<string, object?> { ["rowVersion"] = 5L }, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(report));
            Assert.Equal(1, await Count(db, "insurance_reports", companyA));
            Assert.Equal(branchA, await db.ScalarLongAsync("SELECT branch_id FROM insurance_reports WHERE company_id=@c AND incident_id=@i", c => { c.Parameters.AddWithValue("@c", companyA); c.Parameters.AddWithValue("@i", incidentId); }));
            Assert.Equal("Draft", (await db.QuerySingleAsync("SELECT status FROM insurance_reports WHERE company_id=@c AND incident_id=@i", c => { c.Parameters.AddWithValue("@c", companyA); c.Parameters.AddWithValue("@i", incidentId); }))!["status"]);
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("IncidentCreateInsuranceReport", Principal(companyA, branchA), incidentId,
                new Dictionary<string, object?> { ["rowVersion"] = 5L }, db, new AuditService(db), CancellationToken.None)));
            Assert.Equal(1, await Count(db, "insurance_reports", companyA));
            Assert.Equal(StatusCodes.Status400BadRequest, Status(await Invoke("IncidentStatus", Principal(companyA, branchA), incidentId,
                new Dictionary<string, object?> { ["status"] = "Insurance Report Ready", ["rowVersion"] = 6L }, db, new AuditService(db), CancellationToken.None)));
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("IncidentStatus", Principal(companyA, branchA), incidentId,
                new Dictionary<string, object?> { ["status"] = "Closed", ["rowVersion"] = 6L }, db, new AuditService(db), CancellationToken.None)));
            Assert.Equal(StatusCodes.Status409Conflict, Status(await Invoke("UpdateIncident", Principal(companyA, branchA), incidentId,
                new Dictionary<string, object?> { ["severity"] = "Low", ["rowVersion"] = 7L }, db, new AuditService(db), CancellationToken.None)));
            var closed = (await db.QuerySingleAsync("SELECT status,severity,row_version FROM incidents WHERE id=@id", c => c.Parameters.AddWithValue("@id", incidentId)))!;
            Assert.Equal("Closed", closed["status"]);
            Assert.Equal("Critical", closed["severity"]);
            Assert.Equal(7, Convert.ToInt64(closed["rowVersion"]));
            Assert.Equal(StatusCodes.Status409Conflict, Status(await Invoke("IncidentAttachEvidence", tenantWideAdmin, incidentId,
                new Dictionary<string, object?> { ["evidenceType"] = "Photo", ["evidenceTitle"] = "Late evidence", ["evidenceUrl"] = "https://evidence.example.invalid/late.jpg", ["contentHash"] = new string('d', 64), ["rowVersion"] = 7L }, db, new AuditService(db), CancellationToken.None)));
            Assert.Equal(StatusCodes.Status409Conflict, Status(await Invoke("IncidentCreateInsuranceReport", tenantWideAdmin, incidentId,
                new Dictionary<string, object?> { ["rowVersion"] = 7L }, db, new AuditService(db), CancellationToken.None)));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM incident_evidence WHERE incident_id=@id", c => c.Parameters.AddWithValue("@id", incidentId)));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM insurance_reports WHERE incident_id=@id", c => c.Parameters.AddWithValue("@id", incidentId)));
            Assert.Equal(7, await db.ScalarLongAsync("SELECT row_version FROM incidents WHERE id=@id", c => c.Parameters.AddWithValue("@id", incidentId)));

            await db.ExecuteAsync(@"INSERT INTO ai_recommendations(company_id,tenant_id,recommendation_type,module_key,title,summary,body,score,status)
                                    VALUES(@c,@c,'incidents','incidents','Tenant-wide legal narrative','Sensitive cross-branch narrative','Sensitive cross-branch narrative',99,'Recommended')",
                c => c.Parameters.AddWithValue("@c", companyA));
            var branchRecommendations = Assert.IsAssignableFrom<IValueHttpResult>(await Invoke("IncidentRecommendations", Principal(companyA, branchA), incidentId, db, CancellationToken.None));
            Assert.DoesNotContain("Sensitive cross-branch narrative", System.Text.Json.JsonSerializer.Serialize(branchRecommendations.Value));
            var tenantRecommendations = Assert.IsAssignableFrom<IValueHttpResult>(await Invoke("IncidentRecommendations", tenantWideAdmin, incidentId, db, CancellationToken.None));
            Assert.Contains("Sensitive cross-branch narrative", System.Text.Json.JsonSerializer.Serialize(tenantRecommendations.Value));

            Assert.Equal(new string('a', 64), (await db.QuerySingleAsync("SELECT content_hash FROM incident_evidence WHERE incident_id=@id", c => c.Parameters.AddWithValue("@id", incidentId)))!["contentHash"]);
            Assert.Equal(StatusCodes.Status428PreconditionRequired, Status(await Invoke("DeleteIncident", Principal(companyA, branchA), incidentId, db, new AuditService(db), CancellationToken.None)));
            var wrongBranchDelete = Principal(companyA, branchB); wrongBranchDelete.Request.Headers.IfMatch = "6";
            Assert.Equal(StatusCodes.Status404NotFound, Status(await Invoke("DeleteIncident", wrongBranchDelete, incidentId, db, new AuditService(db), CancellationToken.None)));
            var delete = Principal(companyA, branchA); delete.Request.Headers.IfMatch = "6";
            Assert.Equal(StatusCodes.Status409Conflict, Status(await Invoke("DeleteIncident", delete, incidentId, db, new AuditService(db), CancellationToken.None)));
            var deletableId = await db.InsertAsync("INSERT INTO incidents(company_id,branch_id,incident_number,incident_type,severity,status,row_version) VALUES(@c,@b,@n,'Near Miss','Low','New',1)", c => { c.Parameters.AddWithValue("@c", companyA); c.Parameters.AddWithValue("@b", branchA); c.Parameters.AddWithValue("@n", $"INC-DELETE-{companyA}"); });
            var deleteNew = Principal(companyA, branchA); deleteNew.Request.Headers.IfMatch = "1";
            Assert.Equal(StatusCodes.Status200OK, Status(await Invoke("DeleteIncident", deleteNew, deletableId, db, new AuditService(db), CancellationToken.None)));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM incidents WHERE company_id=@c AND deleted_at IS NULL", c => c.Parameters.AddWithValue("@c", companyA)));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND entity_name='Incident' AND entity_id=@id AND action_name='incident.evidence.attached'", c => { c.Parameters.AddWithValue("@c", companyA); c.Parameters.AddWithValue("@id", incidentId); }));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND entity_name='Incident' AND entity_id=@id AND action_name='incident.deleted'", c => { c.Parameters.AddWithValue("@c", companyA); c.Parameters.AddWithValue("@id", deletableId); }));
        }
        finally { await Cleanup(db, companyA); await Cleanup(db, companyB); }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task BlankIncidentCreate_FailsClosedWithoutRecordOrAudit()
    {
        var db = Db(); await new Batch4SchemaService(db).EnsureAsync();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(4_900_001, 5_099_999);
        const long branchId = 65151;
        await SeedCompany(db, companyId, "BLANK");
        try
        {
            var result = await Invoke("CreateIncident", Principal(companyId, branchId), new Dictionary<string, object?>
            {
                ["incidentType"] = "Near Miss", ["severity"] = "High", ["status"] = "New",
                ["locationDescription"] = "", ["idempotencyKey"] = $"blank-{companyId}"
            }, db, new AuditService(db), CancellationToken.None);

            Assert.Equal(StatusCodes.Status400BadRequest, Status(result));
            var json = System.Text.Json.JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
            Assert.Contains("Location is required", json, StringComparison.Ordinal);
            Assert.Contains("Occurred date and time are required", json, StringComparison.Ordinal);
            Assert.Contains("Incident summary is required", json, StringComparison.Ordinal);
            Assert.Contains("Link at least one driver", json, StringComparison.Ordinal);
            Assert.Equal(0, await Count(db, "incidents", companyId));
            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND action_name='incident.created'", c => c.Parameters.AddWithValue("@c", companyId)));
        }
        finally { await Cleanup(db, companyId); }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task EvidenceInsertFailure_RollsBackParentVersionAtomically()
    {
        var db = Db(); await new Batch4SchemaService(db).EnsureAsync();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(5_100_000, 5_900_000);
        const long branchId = 65201;
        await SeedCompany(db, companyId, "ROLLBACK");
        try
        {
            var incidentId = await db.InsertAsync("INSERT INTO incidents(company_id,branch_id,incident_number,incident_type,severity,status,row_version) VALUES(@c,@b,@n,'Near Miss','High','Under Review',7)", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@n", $"INC-RB-{companyId}"); });
            await db.ExecuteAsync(@"CREATE OR REPLACE FUNCTION opstrax_test_reject_incident_evidence() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.evidence_title='FORCE-ROLLBACK' THEN RAISE EXCEPTION 'forced evidence failure'; END IF; RETURN NEW; END $$;
                DROP TRIGGER IF EXISTS opstrax_test_reject_incident_evidence ON incident_evidence;
                CREATE TRIGGER opstrax_test_reject_incident_evidence BEFORE INSERT ON incident_evidence FOR EACH ROW EXECUTE FUNCTION opstrax_test_reject_incident_evidence();");
            await Assert.ThrowsAnyAsync<Exception>(() => Invoke("IncidentAttachEvidence", Principal(companyId, branchId), incidentId,
                new Dictionary<string, object?> { ["evidenceType"] = "Photo", ["evidenceTitle"] = "FORCE-ROLLBACK", ["evidenceUrl"] = "https://evidence.example.invalid/failure.jpg", ["contentHash"] = new string('c', 64), ["rowVersion"] = 7L }, db, new AuditService(db), CancellationToken.None));
            Assert.Equal(7, await db.ScalarLongAsync("SELECT row_version FROM incidents WHERE id=@id", c => c.Parameters.AddWithValue("@id", incidentId)));
            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM incident_evidence WHERE incident_id=@id", c => c.Parameters.AddWithValue("@id", incidentId)));
        }
        finally
        {
            await db.ExecuteAsync("DROP TRIGGER IF EXISTS opstrax_test_reject_incident_evidence ON incident_evidence; DROP FUNCTION IF EXISTS opstrax_test_reject_incident_evidence();");
            await Cleanup(db, companyId);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConcurrentCreateAndEvidenceCas_HaveSingleWinnersAndSingleAudits()
    {
        var db = Db(); await new Batch4SchemaService(db).EnsureAsync();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(6_100_000, 6_900_000);
        const long branchId = 65301;
        await SeedCompany(db, companyId, "RACE");
        try
        {
            var key = $"incident-race-{companyId}";
            var driverId = await db.InsertAsync("INSERT INTO drivers(company_id,branch_id,driver_code,full_name,status) VALUES(@c,@b,@code,'Race Driver','Available')", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@code", $"DRV-RACE-{companyId}"); });
            var bodyA = new Dictionary<string, object?> { ["incidentType"] = "Collision", ["severity"] = "High", ["status"] = "New", ["driverId"] = driverId, ["occurredAt"] = "2026-08-02T13:00:00Z", ["locationDescription"] = "Concurrency test yard", ["aiSummary"] = "Two callers submitted the same collision report.", ["idempotencyKey"] = key };
            var bodyB = new Dictionary<string, object?>(bodyA);
            var dbA = Db(); var dbB = Db();
            var creates = await Task.WhenAll(
                Invoke("CreateIncident", Principal(companyId, branchId), bodyA, dbA, new AuditService(dbA), CancellationToken.None),
                Invoke("CreateIncident", Principal(companyId, branchId), bodyB, dbB, new AuditService(dbB), CancellationToken.None));
            Assert.Single(creates, result => Status(result) == StatusCodes.Status201Created);
            Assert.Single(creates, result => Status(result) == StatusCodes.Status200OK);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM incidents WHERE company_id=@c AND idempotency_key=@key", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@key", key); }));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND action_name='incident.created'", c => c.Parameters.AddWithValue("@c", companyId)));

            var incidentId = await db.InsertAsync("INSERT INTO incidents(company_id,branch_id,incident_number,incident_type,severity,status,row_version) VALUES(@c,@b,@n,'Collision','High','Under Review',1)", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@n", $"INC-CAS-{companyId}"); });
            var evidenceA = new Dictionary<string, object?> { ["evidenceType"] = "Photo", ["evidenceTitle"] = "A", ["evidenceUrl"] = "https://evidence.example.invalid/a.jpg", ["contentHash"] = new string('a', 64), ["rowVersion"] = 1L };
            var evidenceB = new Dictionary<string, object?> { ["evidenceType"] = "Photo", ["evidenceTitle"] = "B", ["evidenceUrl"] = "https://evidence.example.invalid/b.jpg", ["contentHash"] = new string('b', 64), ["rowVersion"] = 1L };
            dbA = Db(); dbB = Db();
            var attaches = await Task.WhenAll(
                Invoke("IncidentAttachEvidence", Principal(companyId, branchId), incidentId, evidenceA, dbA, new AuditService(dbA), CancellationToken.None),
                Invoke("IncidentAttachEvidence", Principal(companyId, branchId), incidentId, evidenceB, dbB, new AuditService(dbB), CancellationToken.None));
            Assert.Single(attaches, result => Status(result) == StatusCodes.Status200OK);
            Assert.Single(attaches, result => Status(result) == StatusCodes.Status409Conflict);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM incident_evidence WHERE company_id=@c AND incident_id=@id", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@id", incidentId); }));
            Assert.Equal(2, await db.ScalarLongAsync("SELECT row_version FROM incidents WHERE id=@id", c => c.Parameters.AddWithValue("@id", incidentId)));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM audit_logs WHERE company_id=@c AND entity_id=@id AND action_name='incident.evidence.attached'", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@id", incidentId); }));
        }
        finally { await Cleanup(db, companyId); }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task IncidentReferences_RejectMixedDirectAndEventAssetBranches()
    {
        var db = Db(); await new Batch4SchemaService(db).EnsureAsync();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(7_100_000, 7_900_000);
        const long branchA = 65401; const long branchB = 65402;
        await SeedCompany(db, companyId, "REFS");
        try
        {
            var driverA = await db.InsertAsync("INSERT INTO drivers(company_id,branch_id,driver_code,full_name,status) VALUES(@c,@b,@code,'Driver A','Available')", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchA); c.Parameters.AddWithValue("@code", $"DRV-A-{companyId}"); });
            var vehicleA = await db.InsertAsync("INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status) VALUES(@c,@b,@code,'Truck','legacy-fleet-identifier',@code,'Available')", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchA); c.Parameters.AddWithValue("@code", $"VEH-A-{companyId}"); });
            var vehicleB = await db.InsertAsync("INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status) VALUES(@c,@b,@code,'Truck','legacy-fleet-identifier',@code,'Available')", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchB); c.Parameters.AddWithValue("@code", $"VEH-B-{companyId}"); });
            var mixed = new Dictionary<string, object?> { ["incidentType"] = "Near Miss", ["severity"] = "High", ["status"] = "New", ["driverId"] = driverA, ["vehicleId"] = vehicleB, ["occurredAt"] = "2026-08-02T14:00:00Z", ["locationDescription"] = "Branch boundary", ["aiSummary"] = "Mixed-branch references must be rejected." };
            Assert.Equal(StatusCodes.Status400BadRequest, Status(await Invoke("CreateIncident", Principal(companyId, branchA), mixed, db, new AuditService(db), CancellationToken.None)));

            var eventId = await db.InsertAsync("INSERT INTO safety_events(company_id,event_number,event_type,severity,driver_id,vehicle_id,status) VALUES(@c,@n,'Near Miss','High',@d,@v,'New')", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@n", $"SAFE-MIX-{companyId}"); c.Parameters.AddWithValue("@d", driverA); c.Parameters.AddWithValue("@v", vehicleB); });
            var mixedEvent = new Dictionary<string, object?> { ["incidentType"] = "Near Miss", ["severity"] = "High", ["status"] = "New", ["safetyEventId"] = eventId, ["occurredAt"] = "2026-08-02T14:05:00Z", ["locationDescription"] = "Branch boundary", ["aiSummary"] = "Mixed event assets must be rejected." };
            Assert.Equal(StatusCodes.Status400BadRequest, Status(await Invoke("CreateIncident", Principal(companyId, branchA), mixedEvent, db, new AuditService(db), CancellationToken.None)));
            Assert.Equal(StatusCodes.Status400BadRequest, Status(await Invoke("SafetyCreateIncident", Principal(companyId, branchA), eventId, db, new AuditService(db), CancellationToken.None)));

            var coherent = new Dictionary<string, object?> { ["incidentType"] = "Near Miss", ["severity"] = "High", ["status"] = "New", ["driverId"] = driverA, ["vehicleId"] = vehicleA, ["occurredAt"] = "2026-08-02T14:10:00Z", ["locationDescription"] = "Authorized branch", ["aiSummary"] = "Coherent driver and vehicle context." };
            Assert.Equal(StatusCodes.Status201Created, Status(await Invoke("CreateIncident", Principal(companyId, branchA), coherent, db, new AuditService(db), CancellationToken.None)));
            var coherentId = Convert.ToInt64((await db.QuerySingleAsync("SELECT id FROM incidents WHERE company_id=@c AND driver_id=@d", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", driverA); }))!["id"]);
            var crossBranch = await Invoke("UpdateIncident", Principal(companyId, branchA), coherentId,
                new Dictionary<string, object?> { ["vehicleId"] = vehicleB, ["rowVersion"] = 1L }, db, new AuditService(db), CancellationToken.None);
            var nonexistent = await Invoke("UpdateIncident", Principal(companyId, branchA), coherentId,
                new Dictionary<string, object?> { ["vehicleId"] = vehicleB + 9_000_000, ["rowVersion"] = 1L }, db, new AuditService(db), CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest, Status(crossBranch));
            Assert.Equal(StatusCodes.Status400BadRequest, Status(nonexistent));
            var crossBranchJson = System.Text.Json.JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(crossBranch).Value);
            var nonexistentJson = System.Text.Json.JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(nonexistent).Value);
            Assert.Contains("vehicleId was not found in the authorized scope", crossBranchJson);
            Assert.Contains("vehicleId was not found in the authorized scope", nonexistentJson);
        }
        finally { await Cleanup(db, companyId); }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task IncidentCreate_IgnoresSpoofedResolvedBranchAndUsesOnlyAuthoritativeOwnership()
    {
        var db = Db(); await new Batch4SchemaService(db).EnsureAsync();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(8_100_000, 8_900_000);
        const long branchA = 65501; const long branchB = 65502;
        await SeedCompany(db, companyId, "OWNER");
        try
        {
            var driverA = await db.InsertAsync(
                "INSERT INTO drivers(company_id,branch_id,driver_code,full_name,status) VALUES(@c,@b,@code,'Authoritative Driver','Available')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchA); c.Parameters.AddWithValue("@code", $"DRV-OWNER-{companyId}"); });

            var scopedNumber = $"INC-SCOPED-{companyId}";
            var scopedSpoof = new Dictionary<string, object?>
            {
                ["incidentNumber"] = scopedNumber, ["incidentType"] = "Near Miss", ["severity"] = "High", ["status"] = "New",
                ["driverId"] = driverA, ["occurredAt"] = "2026-08-02T15:00:00Z", ["locationDescription"] = "Scoped branch",
                ["aiSummary"] = "Caller attempted to spoof the resolved branch.", ["resolvedBranchId"] = branchB
            };
            Assert.Equal(StatusCodes.Status201Created, Status(await Invoke("CreateIncident", Principal(companyId, branchA), scopedSpoof, db, new AuditService(db), CancellationToken.None)));
            Assert.Equal(branchA, await db.ScalarLongAsync("SELECT branch_id FROM incidents WHERE company_id=@c AND incident_number=@n", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@n", scopedNumber); }));

            var tenantPrincipal = Principal(companyId, branchA);
            tenantPrincipal.Items.Remove(EndpointMappings.AuthBranchIdItemKey);
            var linkedNumber = $"INC-LINKED-{companyId}";
            var tenantLinkedSpoof = new Dictionary<string, object?>
            {
                ["incidentNumber"] = linkedNumber, ["incidentType"] = "Near Miss", ["severity"] = "High", ["status"] = "New",
                ["driverId"] = driverA, ["occurredAt"] = "2026-08-02T15:05:00Z", ["locationDescription"] = "Tenant-linked branch",
                ["aiSummary"] = "Tenant-wide caller supplied an authoritative driver.", ["resolvedBranchId"] = branchB
            };
            Assert.Equal(StatusCodes.Status201Created, Status(await Invoke("CreateIncident", tenantPrincipal, tenantLinkedSpoof, db, new AuditService(db), CancellationToken.None)));
            Assert.Equal(branchA, await db.ScalarLongAsync("SELECT branch_id FROM incidents WHERE company_id=@c AND incident_number=@n", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@n", linkedNumber); }));

            var unlinkedNumber = $"INC-UNLINKED-{companyId}";
            var tenantUnlinkedSpoof = new Dictionary<string, object?>
            {
                ["incidentNumber"] = unlinkedNumber, ["incidentType"] = "Near Miss", ["severity"] = "High", ["status"] = "New",
                ["occurredAt"] = "2026-08-02T15:10:00Z", ["locationDescription"] = "Unlinked report",
                ["aiSummary"] = "No driver, vehicle, or source event was supplied.", ["resolvedBranchId"] = branchB
            };
            Assert.Equal(StatusCodes.Status400BadRequest, Status(await Invoke("CreateIncident", tenantPrincipal, tenantUnlinkedSpoof, db, new AuditService(db), CancellationToken.None)));
            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM incidents WHERE company_id=@c AND incident_number=@n", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@n", unlinkedNumber); }));
        }
        finally { await Cleanup(db, companyId); }
    }

    [Theory]
    [InlineData("New", "Under Review", true)]
    [InlineData("New", "Evidence Collected", false)]
    [InlineData("Under Review", "Evidence Collected", true)]
    [InlineData("Evidence Collected", "Insurance Report Ready", false)]
    [InlineData("Insurance Report Ready", "Closed", true)]
    [InlineData("Closed", "Under Review", false)]
    public void IncidentLifecycle_OnlyAllowsAuditableForwardTransitions(string from, string to, bool expected)
    {
        var method = typeof(EndpointMappings).GetMethod("IsIncidentTransitionAllowed", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.Equal(expected, (bool)method!.Invoke(null, [from, to])!);
    }

    [Fact]
    public void IncidentCreate_RejectsMissingFieldsAndNonNewInitialStatus()
    {
        var errors = Validate(new Dictionary<string, object?>
        {
            ["incidentType"] = "",
            ["severity"] = "Emergency",
            ["status"] = "Closed"
        }, creating: true);

        Assert.Contains(errors, error => error.Contains("type is required", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("Severity must", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("must start in New", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Location is required", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Occurred date and time are required", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Incident summary is required", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Link at least one driver", StringComparison.Ordinal));
    }

    [Fact]
    public void IncidentUpdate_RequiresDedicatedStatusWorkflow()
    {
        var errors = Validate(new Dictionary<string, object?>
        {
            ["incidentType"] = "Near Miss",
            ["severity"] = "High",
            ["status"] = "Closed"
        }, creating: false);

        Assert.Contains(errors, error => error.Contains("status action", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IncidentCreate_MinimumFactsRequireTimezoneButNotAContemporaneousStatement()
    {
        var body = new Dictionary<string, object?>
        {
            ["incidentType"] = "Collision", ["severity"] = "Critical", ["status"] = "New",
            ["driverId"] = 42L, ["occurredAt"] = "2026-08-02T16:15:00Z",
            ["locationDescription"] = "I-95 northbound near Exit 160",
            ["aiSummary"] = "Fleet vehicle contacted a barrier; emergency response pending."
        };
        Assert.Empty(Validate(body, creating: true));

        body["occurredAt"] = "2026-08-02T16:15:00";
        Assert.Contains(Validate(body, creating: true), error => error.Contains("with timezone", StringComparison.Ordinal));
    }

    [Fact]
    public void IncidentMutations_RequireExplicitSafetyWritePermission()
    {
        var method = typeof(EndpointMappings).GetMethod("RequireAnyDirectPermission", BindingFlags.Static | BindingFlags.NonPublic)!;
        var viewOnly = Principal(65199, 65101);
        viewOnly.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "safety:view" };
        var denied = Assert.IsAssignableFrom<IResult>(method.Invoke(null, [viewOnly, new[] { "safety:create", "safety:manage" }]));
        Assert.Equal(StatusCodes.Status403Forbidden, Status(denied));
        var manager = Principal(65199, 65101);
        manager.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "safety:manage" };
        Assert.Null(method.Invoke(null, [manager, new[] { "safety:create", "safety:manage" }]));
    }

    [Fact]
    public void IncidentEndpoints_EnforceBranchConcurrencyAndHonestDraftContracts()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "backend-dotnet", "Controllers", "EndpointMappings.cs"));
        Assert.Contains("StrictBranchFilter(http, \"i\")", source, StringComparison.Ordinal);
        Assert.Contains("row_version=row_version+1", source, StringComparison.Ordinal);
        Assert.Contains("RequireAnyDirectPermission(http, \"safety:create\", \"safety:manage\")", source, StringComparison.Ordinal);
        Assert.Contains("RequireAnyDirectPermission(http, \"safety:update\", \"safety:manage\")", source, StringComparison.Ordinal);
        Assert.Contains("RequireAnyDirectPermission(http, \"safety:review\", \"safety:manage\")", source, StringComparison.Ordinal);
        Assert.Contains("RequireAnyDirectPermission(http, \"safety:delete\", \"safety:manage\")", source, StringComparison.Ordinal);
        Assert.Contains("Insurance report draft created; no external file has been generated", source, StringComparison.Ordinal);
        Assert.DoesNotContain("'Insurance Report Ready' WHERE id=@id", source, StringComparison.Ordinal);
        Assert.DoesNotContain("'/placeholder/insurance-report.pdf'", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CsvExport_DefusesSpreadsheetFormulaCells()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "frontend", "src", "components", "ui.tsx"));
        Assert.Contains("/^[=+\\-@\\t\\r]/", source, StringComparison.Ordinal);
        Assert.Contains("URL.revokeObjectURL", source, StringComparison.Ordinal);
    }

    [Fact]
    public void IncidentUi_UsesStructuredEvidenceAndLifecycleControls()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "frontend", "src", "pages", "Batch4SafetyPage.tsx"));
        Assert.DoesNotContain("window.prompt(\"Target status", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"Insurance Ready\",\"insuranceReady\"]", source, StringComparison.Ordinal);
        Assert.Contains("SHA-256 content hash", source, StringComparison.Ordinal);
        Assert.Contains("does not upload or verify", source, StringComparison.Ordinal);
        Assert.Contains("incidentNextStatuses", source, StringComparison.Ordinal);
        Assert.Contains("[\"coachingType\",\"Coaching Type\"],[\"priority\",\"Priority\"],[\"title\",\"Title\"]", source, StringComparison.Ordinal);
        Assert.Contains("kind === \"coaching\" || kind === \"incidents\"", source, StringComparison.Ordinal);
        Assert.Contains("[\"occurredAt\",\"Occurred Date / Time\"]", source, StringComparison.Ordinal);
        Assert.Contains("[\"aiSummary\",\"Incident Summary\"]", source, StringComparison.Ordinal);
        Assert.Contains("isIncidentCreate && !hasIncidentLink", source, StringComparison.Ordinal);
        Assert.Contains("occurredAt: \"\", locationDescription: \"\", aiSummary: \"\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("incidentType: \"Near Miss\", severity: \"High\", status: \"New\", occurredAt", source, StringComparison.Ordinal);
    }

    private static List<string> Validate(Dictionary<string, object?> body, bool creating)
    {
        var method = typeof(EndpointMappings).GetMethod("ValidateIncidentInput", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<List<string>>(method!.Invoke(null, [body, creating]));
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString, ["Rls:EnforceTenantContext"] = "false" }).Build());

    private static DefaultHttpContext Principal(long companyId, long branchId)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId; http.Items[EndpointMappings.AuthBranchIdItemKey] = branchId;
        http.Items[EndpointMappings.AuthUserIdItemKey] = 42L; http.Items[EndpointMappings.AuthRoleItemKey] = "Safety Manager";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "safety:view", "safety:create", "safety:update", "safety:review", "safety:delete" };
        return http;
    }

    private static async Task<IResult> Invoke(string method, params object[] args)
    {
        var target = typeof(EndpointMappings).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!;
        try { return await (Task<IResult>)target.Invoke(null, args)!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static int Status(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode ?? StatusCodes.Status200OK;
    private static Task<long> Count(Database db, string table, long companyId) => db.ScalarLongAsync($"SELECT COUNT(*) FROM {table} WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
    private static Task SeedCompany(Database db, long id, string suffix) => db.ExecuteAsync("INSERT INTO companies(id,company_code,name,industry) OVERRIDING SYSTEM VALUE VALUES(@id,@code,'Incident Pilot','Transportation')", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@code", $"INC-{suffix}-{id}"); });
    private static async Task Cleanup(Database db, long companyId)
    {
        foreach (var sql in new[] { "DELETE FROM insurance_reports WHERE company_id=@c", "DELETE FROM incident_evidence WHERE company_id=@c", "DELETE FROM ai_recommendations WHERE company_id=@c", "DELETE FROM audit_logs WHERE company_id=@c", "DELETE FROM incidents WHERE company_id=@c", "DELETE FROM safety_events WHERE company_id=@c", "DELETE FROM vehicles WHERE company_id=@c", "DELETE FROM drivers WHERE company_id=@c", "DELETE FROM companies WHERE id=@c" })
            await db.ExecuteAsync(sql, c => c.Parameters.AddWithValue("@c", companyId));
    }
}
