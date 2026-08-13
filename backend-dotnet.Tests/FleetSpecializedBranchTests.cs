using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Http;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;
using System.Reflection;
using System.Text.Json;
using Npgsql;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class FleetSpecializedBranchTests
{
    [Fact]
    public async Task ReturnableAssets_RejectDuplicateTagsOverAllocationAndConcurrentCustody()
    {
        var db = Db();
        await new FleetTmsColdChainSchemaService(db, NullLogger<FleetTmsColdChainSchemaService>.Instance).EnsureAsync();
        var companyId = 882_000L + Random.Shared.Next(1, 5_000);
        const long branchId = 111;
        try
        {
            var typeId = await db.InsertAsync("INSERT INTO fleet_tms_asset_types(company_id,branch_id,code,name) VALUES (@c,NULL,'BIN','Bin')", c => c.Parameters.AddWithValue("@c", companyId));
            var assetId = await db.InsertAsync("INSERT INTO fleet_tms_assets(company_id,branch_id,asset_type_id,asset_tag,name,quantity) VALUES (@c,@b,@t,' Bin-001 ','Bin',5)", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@t", typeId); });
            await Assert.ThrowsAsync<PostgresException>(() => db.InsertAsync("INSERT INTO fleet_tms_assets(company_id,branch_id,asset_type_id,asset_tag,name,quantity) VALUES (@c,@b,@t,'bin-001','Duplicate',1)", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@t", typeId); }));

            var over = await Invoke("AssignAsset", Principal(companyId, branchId), assetId,
                new AssetAssignmentRequest(null, null, "Warehouse", "Dock", 6m, "Assigned", "Dock", null, "too many"), db, CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(over).StatusCode);

            var calls = Enumerable.Range(0, 2).Select(i => Invoke("AssignAsset", Principal(companyId, branchId), assetId,
                new AssetAssignmentRequest(null, null, "Warehouse", $"Dock {i}", 2m, "Assigned", $"Dock {i}", null, "race"), Db(), CancellationToken.None));
            await Task.WhenAll(calls);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_asset_assignments WHERE company_id=@c AND asset_id=@a AND released_at_utc IS NULL", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@a", assetId); }));
        }
        finally
        {
            foreach (var table in new[] { "fleet_tms_asset_events", "fleet_tms_asset_assignments", "fleet_tms_assets", "fleet_tms_asset_types" })
                await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
        }
    }

    [Fact]
    public async Task ReturnableAssets_TypeCodeAndCheckInAreStableUnderTwentyWayRaces()
    {
        var db = Db();
        await new FleetTmsColdChainSchemaService(db, NullLogger<FleetTmsColdChainSchemaService>.Instance).EnsureAsync();
        await new FleetTmsColdChainFoundationSchemaService(db).EnsureAsync();
        var companyId = 882_500L + Random.Shared.Next(1, 300);
        var otherCompanyId = companyId + 1_000;
        const long branchOne = 311;
        const long branchTwo = 312;
        try
        {
            Task<IResult> CreateType(long tenant, long branch, string name) => Invoke("CreateAssetType",
                Principal(tenant, branch), new AssetTypeRequest("RACE-PALLET", name, "race", true), Db(), CancellationToken.None);
            var typeRace = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => CreateType(companyId, i % 2 == 0 ? branchOne : branchTwo, $"Pallet {i}")));
            Assert.Equal(1, typeRace.Count(r => Assert.IsAssignableFrom<IStatusCodeHttpResult>(r).StatusCode == StatusCodes.Status200OK));
            Assert.Equal(19, typeRace.Count(r => Assert.IsAssignableFrom<IStatusCodeHttpResult>(r).StatusCode == StatusCodes.Status409Conflict));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_asset_types WHERE company_id=@c AND lower(btrim(code))='race-pallet'", c => c.Parameters.AddWithValue("@c", companyId)));
            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(await CreateType(otherCompanyId, branchOne, "Other tenant pallet")).StatusCode);

            var typeId = Convert.ToInt64((await db.QuerySingleAsync("SELECT id FROM fleet_tms_asset_types WHERE company_id=@c AND code='RACE-PALLET'", c => c.Parameters.AddWithValue("@c", companyId)))!["id"]);
            var assetId = await db.InsertAsync("INSERT INTO fleet_tms_assets(company_id,branch_id,asset_type_id,asset_tag,name,status,current_location,quantity) VALUES (@c,@b,@t,'CHECKIN-RACE','Checkin race','InUse','Truck',1)", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchOne); c.Parameters.AddWithValue("@t", typeId); });
            await db.ExecuteAsync("INSERT INTO fleet_tms_asset_assignments(company_id,branch_id,asset_id,assignee_type,assignee_name,quantity,status) VALUES (@c,@b,@a,'Shipment','Race',1,'CheckedOut')", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchOne); c.Parameters.AddWithValue("@a", assetId); });
            var movement = new AssetMovementRequest("Returns dock", "Good", "concurrent retry", null, null, null, null, null);
            var checkIns = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Invoke("CheckInAsset", Principal(companyId, branchOne), assetId, movement, Db(), CancellationToken.None)));
            Assert.All(checkIns, r => Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(r).StatusCode));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_asset_events WHERE company_id=@c AND asset_id=@a AND event_type='CheckIn'", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@a", assetId); }));
            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_asset_assignments WHERE company_id=@c AND asset_id=@a AND released_at_utc IS NULL", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@a", assetId); }));
            await Invoke("CheckInAsset", Principal(companyId, branchOne), assetId, movement, Db(), CancellationToken.None);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_asset_events WHERE company_id=@c AND asset_id=@a AND event_type='CheckIn'", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@a", assetId); }));

            await db.ExecuteAsync("UPDATE fleet_tms_assets SET status='InUse' WHERE id=@id", c => c.Parameters.AddWithValue("@id", assetId));
            await Invoke("CheckInAsset", Principal(companyId, branchOne), assetId, movement with { Notes = "state recovery" }, Db(), CancellationToken.None);
            Assert.Equal(2, await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_asset_events WHERE company_id=@c AND asset_id=@a AND event_type='CheckIn'", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@a", assetId); }));
            Assert.Equal("Available", (await db.QuerySingleAsync("SELECT status FROM fleet_tms_assets WHERE id=@id", c => c.Parameters.AddWithValue("@id", assetId)))!["status"]?.ToString());
        }
        finally
        {
            foreach (var company in new[] { companyId, otherCompanyId })
                foreach (var table in new[] { "fleet_tms_asset_events", "fleet_tms_asset_assignments", "fleet_tms_assets", "fleet_tms_asset_types" })
                    await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", company));
        }
    }

    [Fact]
    public async Task ColdChain_ConcurrentIdempotentRetryCreatesOneAtomicFlow()
    {
        var db = Db();
        await new FleetTmsColdChainSchemaService(db, NullLogger<FleetTmsColdChainSchemaService>.Instance).EnsureAsync();
        await new FleetTmsColdChainFoundationSchemaService(db).EnsureAsync();
        var companyId = 883_000L + Random.Shared.Next(1, 5_000);
        const long branchId = 112;
        try
        {
            var zoneId = await db.InsertAsync("INSERT INTO fleet_tms_temperature_zones(company_id,branch_id,code,name,min_celsius,max_celsius) VALUES (@c,NULL,'FROZEN','Frozen',-25,-15)", c => c.Parameters.AddWithValue("@c", companyId));
            var deviceId = await InsertDevice(db, companyId, branchId, zoneId, "IDEM-DEVICE");
            await db.ExecuteAsync("UPDATE fleet_tms_temperature_devices SET idempotency_key='device-registration-key' WHERE id=@id", c => c.Parameters.AddWithValue("@id", deviceId));
            var request = new TemperatureReadingRequest(deviceId, null, zoneId, -5m, 40m, null, null, "Sensor", "Normal", "retry", null, null, "idem-reading-1", "corr-1", null, "{}");
            var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
                new FleetTmsColdChainFoundationService(Db()).RecordTemperatureReadingAsync(
                    companyId, branchId, request with { TemperatureCelsius = i % 2 == 0 ? -5m : -6m })));
            Assert.All(results, row => Assert.Equal(Convert.ToInt64(results[0]["id"]), Convert.ToInt64(row["id"])));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_temperature_readings WHERE company_id=@c AND branch_id=@b AND idempotency_key='idem-reading-1'", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); }));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_temperature_alerts WHERE company_id=@c AND branch_id=@b AND idempotency_key='idem-reading-1'", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); }));
            Assert.Equal(2, await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_cold_chain_event_log WHERE company_id=@c AND branch_id=@b AND idempotency_key='idem-reading-1'", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); }));
            Assert.Equal("device-registration-key", (await db.QuerySingleAsync(
                "SELECT idempotency_key FROM fleet_tms_temperature_devices WHERE id=@id",
                c => c.Parameters.AddWithValue("@id", deviceId)))!["idempotencyKey"]?.ToString());

            var alertId = Convert.ToInt64((await db.QuerySingleAsync(
                "SELECT id FROM fleet_tms_temperature_alerts WHERE company_id=@c AND branch_id=@b AND idempotency_key='idem-reading-1'",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); }))!["id"]);
            var resolved = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
                new FleetTmsColdChainFoundationService(Db()).ResolveAlertAsync(companyId, branchId, alertId,
                    new TemperatureAlertResolveRequest("concurrent resolution"), "test-user")));
            Assert.All(resolved, row => Assert.Equal("Resolved", row["status"]?.ToString()));
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM fleet_tms_cold_chain_event_log WHERE company_id=@c AND branch_id=@b AND event_type='cold_chain.alert.resolved' AND aggregate_id=@id",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@id", alertId.ToString()); }));
        }
        finally
        {
            foreach (var table in new[] { "fleet_tms_cold_chain_event_log", "fleet_tms_temperature_alerts", "fleet_tms_temperature_readings", "fleet_tms_temperature_devices", "fleet_tms_temperature_zones" })
                await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
        }
    }

    [Fact]
    public async Task ColdChain_DeviceRegistration_HandlesTwentyWayRetryAndCodeRacesAcrossScopes()
    {
        var db = Db();
        await new FleetTmsColdChainSchemaService(db, NullLogger<FleetTmsColdChainSchemaService>.Instance).EnsureAsync();
        await new FleetTmsColdChainFoundationSchemaService(db).EnsureAsync();
        var companyId = 883_500L + Random.Shared.Next(1, 300);
        var otherCompanyId = companyId + 1_000;
        const long branchOne = 211;
        const long branchTwo = 212;
        try
        {
            var zoneId = await db.InsertAsync("INSERT INTO fleet_tms_temperature_zones(company_id,branch_id,code,name,min_celsius,max_celsius) VALUES (@c,NULL,'DEVICE-RACE','Device race',2,8)", c => c.Parameters.AddWithValue("@c", companyId));
            var otherZoneId = await db.InsertAsync("INSERT INTO fleet_tms_temperature_zones(company_id,branch_id,code,name,min_celsius,max_celsius) VALUES (@c,NULL,'DEVICE-RACE','Device race',2,8)", c => c.Parameters.AddWithValue("@c", otherCompanyId));

            TemperatureDeviceRequest Request(string code, string idem, long zone) =>
                new(code, code, zone, null, "TRUCK", "Active", 4m, 90m, null, "race", "api", null, idem, null, null, "{}");
            Task<IResult> Create(long tenant, long branch, TemperatureDeviceRequest request) =>
                Invoke("CreateDevice", Principal(tenant, branch), request, Db(), CancellationToken.None);

            var retries = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
                Create(companyId, branchOne, Request(i % 2 == 0 ? "IDEM-DEVICE" : "IGNORED-REPLAY-CODE", "device-idem-1", zoneId))));
            Assert.All(retries, result => Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode));
            var replayIds = retries.Select(result => Payload(result).RootElement.GetProperty("data").GetProperty("id").GetInt64()).Distinct().ToList();
            Assert.Single(replayIds);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_temperature_devices WHERE company_id=@c AND branch_id=@b AND idempotency_key='device-idem-1'", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchOne); }));

            // A tenant administrator has no branch claim and must still be able to
            // replay the single original after sole-branch rollout/backfill.
            var tenantReplay = await Invoke("CreateDevice", Principal(companyId, null),
                Request("IGNORED-TENANT-REPLAY", "device-idem-1", zoneId), Db(), CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(tenantReplay).StatusCode);
            Assert.Equal(replayIds[0], Payload(tenantReplay).RootElement.GetProperty("data").GetProperty("id").GetInt64());

            var codeRace = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
                Create(companyId, branchOne, Request("CODE-RACE", $"code-race-{i}", zoneId))));
            Assert.Equal(1, codeRace.Count(r => Assert.IsAssignableFrom<IStatusCodeHttpResult>(r).StatusCode == StatusCodes.Status200OK));
            Assert.Equal(19, codeRace.Count(r => Assert.IsAssignableFrom<IStatusCodeHttpResult>(r).StatusCode == StatusCodes.Status409Conflict));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_temperature_devices WHERE company_id=@c AND lower(btrim(device_code))='code-race'", c => c.Parameters.AddWithValue("@c", companyId)));

            var branchOneResult = await Create(companyId, branchOne, Request("SCOPE-CODE", "scope-b1", zoneId));
            var branchTwoResult = await Create(companyId, branchTwo, Request("SCOPE-CODE", "scope-b2", zoneId));
            var otherTenantResult = await Create(otherCompanyId, branchOne, Request("SCOPE-CODE", "scope-other", otherZoneId));
            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(branchOneResult).StatusCode);
            Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<IStatusCodeHttpResult>(branchTwoResult).StatusCode);
            Assert.All(new[] { branchOneResult, otherTenantResult },
                result => Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode));
        }
        finally
        {
            foreach (var company in new[] { companyId, otherCompanyId })
            {
                await db.ExecuteAsync("DELETE FROM fleet_tms_temperature_devices WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", company));
                await db.ExecuteAsync("DELETE FROM fleet_tms_temperature_zones WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", company));
            }
        }
    }

    [Fact]
    public async Task ColdChain_PolicyTwentyWayIdempotencyRaceReturnsOriginalResourceWithout500()
    {
        var db = Db();
        await new FleetTmsColdChainSchemaService(db, NullLogger<FleetTmsColdChainSchemaService>.Instance).EnsureAsync();
        await new FleetTmsColdChainFoundationSchemaService(db).EnsureAsync();
        var companyId = 883_900L + Random.Shared.Next(1, 300);
        const long branchId = 221;
        try
        {
            var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
                new FleetTmsColdChainFoundationService(Db()).UpsertPolicyAsync(
                    companyId, branchId, $"POLICY-{i}", i % 2 == 0 ? "default" : "vehicle", i % 2 == 0 ? "" : $"TRUCK-{i}",
                    i % 2 == 0 ? 2m : -20m, i % 2 == 0 ? 8m : -10m, 30m, 80m,
                    "Critical", true, "Active", "api", $"client-{i}", "policy-idem-race", $"corr-{i}", null, "{}", $"payload-{i}")));
            Assert.All(results, row => Assert.Equal(results[0].Id, row.Id));
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_cold_chain_policies WHERE company_id=@c AND branch_id=@b AND idempotency_key='policy-idem-race'", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); }));
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM fleet_tms_cold_chain_policies WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
        }
    }

    [Fact]
    public async Task SaudiReadiness_UsesFullCountsBranchUnavailableSemanticsAndLiveExpiry()
    {
        var db = Db();
        await new FleetTmsSchemaService(db, NullLogger<FleetTmsSchemaService>.Instance).EnsureAsync();
        await new FleetTmsColdChainSchemaService(db, NullLogger<FleetTmsColdChainSchemaService>.Instance).EnsureAsync();
        await new MarketPackSchemaService(db).EnsureAsync();
        var companyId = 884_000L + Random.Shared.Next(1, 5_000);
        const long branchId = 113;
        try
        {
            await db.ExecuteAsync("INSERT INTO companies(id,company_code,name,industry) OVERRIDING SYSTEM VALUE VALUES (@c,@code,'Specialized test','transport') ON CONFLICT DO NOTHING", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", $"SP-{companyId}"); });
            await db.ExecuteAsync("INSERT INTO tenant_market_packs(company_id,pack_code,status) VALUES (@c,'saudi_gcc','active') ON CONFLICT (company_id,pack_code) DO UPDATE SET status='active'", c => c.Parameters.AddWithValue("@c", companyId));
            for (var i = 0; i < 12; i++) await db.ExecuteAsync("INSERT INTO fleet_tms_shipments(company_id,shipment_number,is_invoice_ready,customer_vat_number,customer_commercial_registration_no) VALUES (@c,@n,true,'VAT','CR')", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@n", $"READY-{i}"); });
            for (var i = 0; i < 3; i++) await db.ExecuteAsync("INSERT INTO fleet_tms_shipments(company_id,shipment_number,is_invoice_ready) VALUES (@c,@n,false)", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@n", $"BLOCKED-{i}"); });
            await db.ExecuteAsync("INSERT INTO fleet_tms_readiness_documents(company_id,branch_id,kind,subject_type,subject_name,document_type,document_status,expiry_status,hijri_expiry_date) VALUES (@c,@b,'Compliance','Branch','Legacy Riyadh','Permit','Active','Healthy',CURRENT_DATE-1)", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); });
            await new MarketPackSchemaService(db).EnsureAsync();
            await db.ExecuteAsync("INSERT INTO compliance_records(company_id,branch_id,pack_code,subject_type,subject_name,doc_key,document_status,hijri_expiry_date) VALUES (@c,@b,'saudi_gcc','branch','Hijri Riyadh','permit','valid','1440-01-01')", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); });
            await db.ExecuteAsync("INSERT INTO business_tax_readiness(company_id,branch_id,pack_code,vat_number,commercial_registration_no) VALUES (@c,NULL,'saudi_gcc','310000000000003','1010123456'),(@c,@b,'saudi_gcc','320000000000003','1010654321')", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); });

            var tenantVat = Payload(await InvokeMarket("SaudiVatReadiness", Principal(companyId, null), db, CancellationToken.None));
            Assert.Equal("310000000000003", tenantVat.RootElement.GetProperty("data").GetProperty("readiness").GetProperty("vatNumber").GetString());
            var branchVat = Payload(await InvokeMarket("SaudiVatReadiness", Principal(companyId, branchId), db, CancellationToken.None));
            Assert.Equal("320000000000003", branchVat.RootElement.GetProperty("data").GetProperty("readiness").GetProperty("vatNumber").GetString());

            var tenantPayload = Payload(await Invoke("VatInvoiceReady", Principal(companyId, null), db, CancellationToken.None));
            Assert.Equal(12, tenantPayload.RootElement.GetProperty("data").GetProperty("summary").GetProperty("readyCount").GetInt64());
            Assert.Equal(3, tenantPayload.RootElement.GetProperty("data").GetProperty("summary").GetProperty("blockedCount").GetInt64());
            var branchPayload = Payload(await Invoke("VatInvoiceReady", Principal(companyId, branchId), db, CancellationToken.None));
            var branchSummary = branchPayload.RootElement.GetProperty("data").GetProperty("summary");
            Assert.False(branchSummary.GetProperty("shipmentMetricsAvailable").GetBoolean());
            Assert.Equal(JsonValueKind.Null, branchSummary.GetProperty("readyCount").ValueKind);
            var retired = await Invoke("ComplianceDocuments", Principal(companyId, branchId), db, null, null, CancellationToken.None);
            Assert.Equal(StatusCodes.Status410Gone, Assert.IsAssignableFrom<IStatusCodeHttpResult>(retired).StatusCode);
            var docsPayload = Payload(await InvokeMarket("SaudiDocuments", Principal(companyId, branchId), db, CancellationToken.None));
            var canonicalData = docsPayload.RootElement.GetProperty("data");
            Assert.True(canonicalData.ValueKind != JsonValueKind.Null, docsPayload.RootElement.ToString());
            var docs = canonicalData.GetProperty("items").EnumerateArray().ToList();
            Assert.Contains(docs, d => d.GetProperty("subjectName").GetString() == "Legacy Riyadh");
            var legacy = docs.Single(d => d.GetProperty("subjectName").GetString() == "Legacy Riyadh");
            Assert.Equal("unknown", legacy.GetProperty("documentStatus").GetString());
            Assert.True(legacy.GetProperty("needsReview").GetBoolean());
            var hijri = docs.Single(d => d.GetProperty("subjectName").GetString() == "Hijri Riyadh");
            Assert.Equal("expired", hijri.GetProperty("documentStatus").GetString());
            Assert.Equal("hijri_converted", hijri.GetProperty("expiryBasis").GetString());
            var expiryPayload = Payload(await InvokeMarket("SaudiExpiries", Principal(companyId, branchId), db, CancellationToken.None));
            var expiryItems = expiryPayload.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
            Assert.Contains(expiryItems, item => item.GetProperty("recordId").GetInt64() == legacy.GetProperty("id").GetInt64() && item.GetProperty("severity").GetString() == "needs_review");
            var legacyRecordId = legacy.GetProperty("id").GetInt64();
            Assert.Equal("needs_review", (await db.QuerySingleAsync("SELECT severity FROM compliance_expiry_events WHERE record_id=@id AND retired_at IS NULL", c => c.Parameters.AddWithValue("@id", legacyRecordId)))!["severity"]?.ToString());
            await db.ExecuteAsync("UPDATE compliance_records SET expiry_date=CURRENT_DATE+90,hijri_expiry_date=NULL,document_status='expired' WHERE id=@id", c => c.Parameters.AddWithValue("@id", legacyRecordId));
            var renewed = Payload(await InvokeMarket("SaudiExpiries", Principal(companyId, branchId), db, CancellationToken.None));
            Assert.DoesNotContain(renewed.RootElement.GetProperty("data").GetProperty("items").EnumerateArray(), item => item.GetProperty("recordId").GetInt64() == legacyRecordId);
            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM compliance_expiry_events WHERE record_id=@id AND retired_at IS NULL", c => c.Parameters.AddWithValue("@id", legacyRecordId)));
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM business_tax_readiness WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM fleet_tms_readiness_documents WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM compliance_records WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM fleet_tms_shipments WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM tenant_market_packs WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", companyId));
        }
    }
    [Fact]
    public async Task ColdChain_TenantWideReading_UsesResourceBranchAndRejectsCrossBranchZonesWithoutSideEffects()
    {
        var db = Db();
        await new FleetTmsColdChainSchemaService(db, NullLogger<FleetTmsColdChainSchemaService>.Instance).EnsureAsync();
        await new FleetTmsColdChainFoundationSchemaService(db).EnsureAsync();
        var service = new FleetTmsColdChainFoundationService(db);
        var companyId = 879_000L + Random.Shared.Next(1, 5_000);
        const long branchOne = 81;
        const long branchTwo = 82;

        try
        {
            var sharedZone = await db.InsertAsync(@"INSERT INTO fleet_tms_temperature_zones
                (company_id, branch_id, code, name, min_celsius, max_celsius)
                VALUES (@companyId, NULL, 'SHARED', 'Shared chilled', 2, 8)",
                c => c.Parameters.AddWithValue("@companyId", companyId));
            var branchOneZone = await db.InsertAsync(@"INSERT INTO fleet_tms_temperature_zones
                (company_id, branch_id, code, name, min_celsius, max_celsius)
                VALUES (@companyId, @branchId, 'B1', 'Branch one chilled', 3, 7)",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branchId", branchOne); });
            var branchTwoZone = await db.InsertAsync(@"INSERT INTO fleet_tms_temperature_zones
                (company_id, branch_id, code, name, min_celsius, max_celsius)
                VALUES (@companyId, @branchId, 'B2', 'Branch two chilled', 1, 9)",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branchId", branchTwo); });

            var validDevice = await InsertDevice(db, companyId, branchOne, branchOneZone, "TENANT-WIDE-B1");
            var invalidDerivedDevice = await InsertDevice(db, companyId, branchOne, branchTwoZone, "DERIVED-CROSS-BRANCH");
            var sharedDevice = await db.InsertAsync(@"INSERT INTO fleet_tms_temperature_devices
                (company_id, branch_id, device_code, name, zone_id, status)
                VALUES (@companyId, NULL, 'SHARED-DEVICE', 'Shared device', @zoneId, 'Active')",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@zoneId", sharedZone); });

            await service.UpsertPolicyAsync(companyId, null, "SHARED-POLICY", "default", "", 2, 8, null, null,
                "High", true, "Active", null, null, null, null, null, "{}", "shared policy");
            await service.UpsertPolicyAsync(companyId, branchOne, "BRANCH-1-POLICY", "default", "", -50, 50, null, null,
                "Critical", true, "Active", null, null, null, null, null, "{}", "must not apply to shared device");

            async Task AssertNoCrossBranchSideEffects(TemperatureReadingRequest request)
            {
                var before = await db.QuerySingleAsync(@"SELECT
                    (SELECT COUNT(*) FROM fleet_tms_temperature_readings WHERE company_id=@companyId) readings,
                    (SELECT COUNT(*) FROM fleet_tms_temperature_alerts WHERE company_id=@companyId) alerts,
                    (SELECT COUNT(*) FROM fleet_tms_cold_chain_event_log WHERE company_id=@companyId) events",
                    c => c.Parameters.AddWithValue("@companyId", companyId));
                var beforeDevice = await db.QuerySingleAsync("SELECT last_reported_temperature_celsius,last_ping_at_utc FROM fleet_tms_temperature_devices WHERE id=@id",
                    c => c.Parameters.AddWithValue("@id", request.DeviceId));

                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    service.RecordTemperatureReadingAsync(companyId, null, request));

                var after = await db.QuerySingleAsync(@"SELECT
                    (SELECT COUNT(*) FROM fleet_tms_temperature_readings WHERE company_id=@companyId) readings,
                    (SELECT COUNT(*) FROM fleet_tms_temperature_alerts WHERE company_id=@companyId) alerts,
                    (SELECT COUNT(*) FROM fleet_tms_cold_chain_event_log WHERE company_id=@companyId) events",
                    c => c.Parameters.AddWithValue("@companyId", companyId));
                var afterDevice = await db.QuerySingleAsync("SELECT last_reported_temperature_celsius,last_ping_at_utc FROM fleet_tms_temperature_devices WHERE id=@id",
                    c => c.Parameters.AddWithValue("@id", request.DeviceId));
                Assert.Equal(Convert.ToInt64(before!["readings"]), Convert.ToInt64(after!["readings"]));
                Assert.Equal(Convert.ToInt64(before["alerts"]), Convert.ToInt64(after["alerts"]));
                Assert.Equal(Convert.ToInt64(before["events"]), Convert.ToInt64(after["events"]));
                Assert.Equal(beforeDevice!["lastReportedTemperatureCelsius"], afterDevice!["lastReportedTemperatureCelsius"]);
                Assert.Equal(beforeDevice["lastPingAtUtc"], afterDevice["lastPingAtUtc"]);
            }

            await AssertNoCrossBranchSideEffects(new TemperatureReadingRequest(
                validDevice, null, branchTwoZone, 5m, null, null, null, "Sensor", "Normal", "explicit cross-branch zone"));
            await AssertNoCrossBranchSideEffects(new TemperatureReadingRequest(
                invalidDerivedDevice, null, null, 5m, null, null, null, "Sensor", "Normal", "derived cross-branch zone"));

            var branchReading = await service.RecordTemperatureReadingAsync(companyId, null,
                new TemperatureReadingRequest(validDevice, null, branchOneZone, 5m, null, null, null, "Sensor", "Normal", "tenant-wide valid branch reading"));
            Assert.Equal(branchOne, Convert.ToInt64(branchReading["branchId"]));

            var sharedReading = await service.RecordTemperatureReadingAsync(companyId, null,
                new TemperatureReadingRequest(sharedDevice, null, sharedZone, 9m, null, null, null, "Sensor", "Normal", "shared resource reading"));
            Assert.Equal("SHARED-POLICY", sharedReading["appliedPolicyCode"]?.ToString());
            Assert.Equal("Breach", sharedReading["status"]?.ToString());
            var alert = await db.QuerySingleAsync("SELECT id FROM fleet_tms_temperature_alerts WHERE reading_id=@readingId",
                c => c.Parameters.AddWithValue("@readingId", Convert.ToInt64(sharedReading["id"])));
            var resolved = await service.ResolveAlertAsync(companyId, null, Convert.ToInt64(alert!["id"]),
                new TemperatureAlertResolveRequest("tenant-wide resolution"), "test-user");
            Assert.Equal("Resolved", resolved["status"]?.ToString());
        }
        finally
        {
            foreach (var table in new[] { "fleet_tms_cold_chain_event_log", "fleet_tms_temperature_alerts", "fleet_tms_temperature_readings", "fleet_tms_temperature_devices", "fleet_tms_cold_chain_policies", "fleet_tms_temperature_zones" })
                await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        }
    }

    [Fact]
    public async Task ColdChain_SummaryAndPolicies_AcceptTenantWideAndBranchScopes()
    {
        var db = Db();
        await new FleetTmsColdChainSchemaService(db, NullLogger<FleetTmsColdChainSchemaService>.Instance).EnsureAsync();
        await new FleetTmsColdChainFoundationSchemaService(db).EnsureAsync();
        var service = new FleetTmsColdChainFoundationService(db);
        var companyId = 880_000L + Random.Shared.Next(1, 5_000);
        const long branchOne = 91;
        const long branchTwo = 92;

        try
        {
            await service.UpsertPolicyAsync(companyId, null, "SHARED", "default", "", 2, 8, null, null,
                "High", true, "Active", null, null, null, null, null, "{}", "tenant default");
            await service.UpsertPolicyAsync(companyId, branchOne, "BRANCH-1", "default", "", 3, 7, null, null,
                "Critical", true, "Active", null, null, null, null, null, "{}", "branch one");
            await service.UpsertPolicyAsync(companyId, branchTwo, "BRANCH-2", "default", "", 1, 9, null, null,
                "High", true, "Active", null, null, null, null, null, "{}", "branch two");

            foreach (var endpoint in new[] { "ColdChainPolicies", "ColdChainSummary" })
            {
                var tenantResult = endpoint == "ColdChainPolicies"
                    ? await Invoke(endpoint, Principal(companyId, null), service, CancellationToken.None)
                    : await Invoke(endpoint, Principal(companyId, null), db, service, CancellationToken.None);
                var tenantData = Payload(tenantResult).RootElement.GetProperty("data");
                var tenantPolicies = (endpoint == "ColdChainPolicies" ? tenantData.GetProperty("items") : tenantData.GetProperty("policies"))
                    .EnumerateArray().Select(item => item.GetProperty("policyCode").GetString()).ToList();
                Assert.Contains("SHARED", tenantPolicies);
                Assert.Contains("BRANCH-1", tenantPolicies);
                Assert.Contains("BRANCH-2", tenantPolicies);

                var branchResult = endpoint == "ColdChainPolicies"
                    ? await Invoke(endpoint, Principal(companyId, branchOne), service, CancellationToken.None)
                    : await Invoke(endpoint, Principal(companyId, branchOne), db, service, CancellationToken.None);
                var branchData = Payload(branchResult).RootElement.GetProperty("data");
                var branchPolicies = (endpoint == "ColdChainPolicies" ? branchData.GetProperty("items") : branchData.GetProperty("policies"))
                    .EnumerateArray().Select(item => item.GetProperty("policyCode").GetString()).ToList();
                Assert.Contains("SHARED", branchPolicies);
                Assert.Contains("BRANCH-1", branchPolicies);
                Assert.DoesNotContain("BRANCH-2", branchPolicies);
            }
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM fleet_tms_cold_chain_policies WHERE company_id=@companyId",
                c => c.Parameters.AddWithValue("@companyId", companyId));
        }
    }

    [Fact]
    public async Task ColdChain_Branch_Ownership_Isolates_Devices_Readings_Alerts_And_Policies()
    {
        var db = Db();
        var schema = new FleetTmsColdChainSchemaService(db, NullLogger<FleetTmsColdChainSchemaService>.Instance);
        await schema.EnsureAsync();
        await new FleetTmsColdChainFoundationSchemaService(db).EnsureAsync();
        var service = new FleetTmsColdChainFoundationService(db);
        var companyId = 881_000L + Random.Shared.Next(1, 5_000);
        const long branchOne = 101;
        const long branchTwo = 202;

        try
        {
            var zoneId = await db.InsertAsync(@"INSERT INTO fleet_tms_temperature_zones
                (company_id, branch_id, code, name, min_celsius, max_celsius)
                VALUES (@companyId, NULL, 'CHILL', 'Shared chilled', 2, 8)",
                c => c.Parameters.AddWithValue("@companyId", companyId));
            var deviceOne = await InsertDevice(db, companyId, branchOne, zoneId, "DEV-B1");
            var deviceTwo = await InsertDevice(db, companyId, branchTwo, zoneId, "DEV-B2");

            await service.UpsertPolicyAsync(companyId, null, "SHARED", "default", "", 2, 8, null, null,
                "High", true, "Active", null, null, null, null, null, "{}", "tenant default");
            await service.UpsertPolicyAsync(companyId, branchOne, "BRANCH-1", "device", deviceOne.ToString(), 3, 7, null, null,
                "Critical", true, "Active", null, null, null, null, null, "{}", "branch override");
            await service.UpsertPolicyAsync(companyId, branchTwo, "BRANCH-2", "device", deviceTwo.ToString(), 1, 9, null, null,
                "High", true, "Active", null, null, null, null, null, "{}", "other branch");

            var policies = await service.ListPoliciesAsync(companyId, branchOne);
            Assert.Contains(policies, p => p.PolicyCode == "SHARED");
            Assert.Contains(policies, p => p.PolicyCode == "BRANCH-1");
            Assert.DoesNotContain(policies, p => p.PolicyCode == "BRANCH-2");

            var reading = await service.RecordTemperatureReadingAsync(companyId, branchOne,
                new TemperatureReadingRequest(deviceOne, null, zoneId, 10m, 50m, null, null, "Sensor", "Normal", "branch test"));
            Assert.Equal(branchOne, Convert.ToInt64(reading["branchId"]));
            Assert.Equal("Breach", reading["status"]?.ToString());

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordTemperatureReadingAsync(companyId, branchOne,
                new TemperatureReadingRequest(deviceTwo, null, zoneId, 5m, null, null, null, "Sensor", "Normal", null)));

            var otherBranchRows = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM fleet_tms_temperature_readings WHERE company_id=@companyId AND branch_id=@branchId",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branchId", branchTwo); });
            Assert.Equal(0, otherBranchRows);
        }
        finally
        {
            foreach (var table in new[] { "fleet_tms_cold_chain_event_log", "fleet_tms_temperature_alerts", "fleet_tms_temperature_readings", "fleet_tms_temperature_devices", "fleet_tms_cold_chain_policies", "fleet_tms_temperature_zones" })
                await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        }
    }

    [Fact]
    public async Task ReturnableAssets_Branch_Custody_Workflow_Is_Isolated()
    {
        var db = Db();
        await new FleetTmsColdChainSchemaService(db, NullLogger<FleetTmsColdChainSchemaService>.Instance).EnsureAsync();
        var companyId = 886_000L + Random.Shared.Next(1, 5_000);
        const long branchOne = 301;
        const long branchTwo = 302;

        try
        {
            var typeId = await db.InsertAsync(@"INSERT INTO fleet_tms_asset_types
                (company_id, branch_id, code, name) VALUES (@companyId, NULL, 'PALLET', 'Shared pallet')",
                c => c.Parameters.AddWithValue("@companyId", companyId));
            var assetOne = await InsertAsset(db, companyId, branchOne, typeId, "PAL-B1");
            var assetTwo = await InsertAsset(db, companyId, branchTwo, typeId, "PAL-B2");

            var assignmentId = await db.InsertAsync(@"INSERT INTO fleet_tms_asset_assignments
                (company_id, branch_id, asset_id, assignee_type, assignee_name, quantity, status)
                VALUES (@companyId, @branchId, @assetId, 'Warehouse', 'Branch 1 dock', 1, 'CheckedOut')",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branchId", branchOne); c.Parameters.AddWithValue("@assetId", assetOne); });
            await db.ExecuteAsync(@"UPDATE fleet_tms_assets SET status='InUse', current_location='Branch 1 dock'
                WHERE company_id=@companyId AND branch_id=@branchId AND id=@assetId",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branchId", branchOne); c.Parameters.AddWithValue("@assetId", assetOne); });
            await db.InsertAsync(@"INSERT INTO fleet_tms_barcode_scan_events
                (company_id, branch_id, asset_id, scanned_value, scanner_id)
                VALUES (@companyId, @branchId, @assetId, 'PAL-B1', 'BRANCH-READER')",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branchId", branchOne); c.Parameters.AddWithValue("@assetId", assetOne); });

            var visible = await db.QueryAsync("SELECT id, status FROM fleet_tms_assets WHERE company_id=@companyId AND branch_id=@branchId",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branchId", branchOne); });
            Assert.Single(visible);
            Assert.Equal(assetOne, Convert.ToInt64(visible[0]["id"]));
            Assert.Equal("InUse", visible[0]["status"]?.ToString());

            var tamperRows = await db.ExecuteAsync(@"UPDATE fleet_tms_assets SET status='Available'
                WHERE company_id=@companyId AND branch_id=@branchId AND id=@assetId",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branchId", branchOne); c.Parameters.AddWithValue("@assetId", assetTwo); });
            Assert.Equal(0, tamperRows);

            await db.ExecuteAsync(@"UPDATE fleet_tms_asset_assignments SET status='Returned', released_at_utc=NOW()
                WHERE company_id=@companyId AND branch_id=@branchId AND id=@id",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branchId", branchOne); c.Parameters.AddWithValue("@id", assignmentId); });
            var returned = await db.QuerySingleAsync("SELECT status FROM fleet_tms_asset_assignments WHERE id=@id", c => c.Parameters.AddWithValue("@id", assignmentId));
            Assert.Equal("Returned", returned!["status"]?.ToString());
        }
        finally
        {
            foreach (var table in new[] { "fleet_tms_barcode_scan_events", "fleet_tms_asset_events", "fleet_tms_asset_assignments", "fleet_tms_assets", "fleet_tms_asset_types" })
                await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        }
    }

    [Fact]
    public async Task SaudiReadiness_Branch_Documents_Expiry_And_VatEvidence_Are_Isolated()
    {
        var db = Db();
        await new FleetTmsColdChainSchemaService(db, NullLogger<FleetTmsColdChainSchemaService>.Instance).EnsureAsync();
        var companyId = 891_000L + Random.Shared.Next(1, 5_000);
        const long branchOne = 501;
        const long branchTwo = 502;
        var expiry = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));

        try
        {
            Assert.Equal("ExpiringSoon", FleetTmsColdChainEndpoints.ComputeExpiryStatus(expiry, "Active"));
            Assert.Equal("Expired", FleetTmsColdChainEndpoints.ComputeExpiryStatus(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), "Active"));

            var firstId = await InsertReadinessDoc(db, companyId, branchOne, "Branch", "Riyadh branch", "VAT-B1", "CR-B1", expiry);
            var secondId = await InsertReadinessDoc(db, companyId, branchTwo, "Branch", "Jeddah branch", "VAT-B2", "CR-B2", expiry);
            var visible = await db.QueryAsync(@"SELECT id, expiry_status, vat_number, commercial_registration_no
                FROM fleet_tms_readiness_documents WHERE company_id=@companyId AND branch_id=@branchId",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branchId", branchOne); });
            Assert.Single(visible);
            Assert.Equal(firstId, Convert.ToInt64(visible[0]["id"]));
            Assert.Equal("ExpiringSoon", visible[0]["expiryStatus"]?.ToString());

            var tamper = await db.ExecuteAsync(@"UPDATE fleet_tms_readiness_documents SET vat_number='TAMPERED'
                WHERE company_id=@companyId AND branch_id=@branchId AND id=@id",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branchId", branchOne); c.Parameters.AddWithValue("@id", secondId); });
            Assert.Equal(0, tamper);

            var ready = visible.Count(d => !string.IsNullOrWhiteSpace(d["vatNumber"]?.ToString()) && !string.IsNullOrWhiteSpace(d["commercialRegistrationNo"]?.ToString()));
            Assert.Equal(1, ready);
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM fleet_tms_readiness_documents WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        }
    }

    [Fact]
    public async Task RegionalCompliance_Branch_CanadaAndSaudi_Workflows_Are_Isolated()
    {
        var db = Db();
        await new MarketPackSchemaService(db).EnsureAsync();
        var companyId = 896_000L + Random.Shared.Next(1, 3_000);
        const long branchOne = 601;
        const long branchTwo = 602;

        try
        {
            var canadaOne = await InsertComplianceRecord(db, companyId, branchOne, "canada_na", "driver", "Ontario driver", "drivers_license");
            var canadaTwo = await InsertComplianceRecord(db, companyId, branchTwo, "canada_na", "driver", "Quebec driver", "drivers_license");
            var saudiOne = await InsertComplianceRecord(db, companyId, branchOne, "saudi_gcc", "transport", "Riyadh vehicle", "transport_permit");
            await db.InsertAsync(@"INSERT INTO compliance_expiry_events
                (company_id, branch_id, pack_code, record_id, subject_name, severity, message, expiry_date)
                VALUES (@companyId, @branchId, 'canada_na', @recordId, 'Ontario driver', 'warning', 'Expiring', CURRENT_DATE + 10)",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branchId", branchOne); c.Parameters.AddWithValue("@recordId", canadaOne); });
            await db.InsertAsync(@"INSERT INTO vehicle_inspection_records
                (company_id, branch_id, vehicle_label, inspector_name, status)
                VALUES (@companyId, @branchId, 'TRK-B1', 'Inspector B1', 'pass')",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branchId", branchOne); });
            await db.InsertAsync(@"INSERT INTO jurisdiction_mileage_records
                (company_id, branch_id, province_state, country, distance, tax_period)
                VALUES (@companyId, @branchId, 'ON', 'CA', 1200, '2026-Q2')",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branchId", branchOne); });
            await db.InsertAsync(@"INSERT INTO jurisdiction_fuel_records
                (company_id, branch_id, province_state, country, fuel_volume, tax_period)
                VALUES (@companyId, @branchId, 'ON', 'CA', 300, '2026-Q2')",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branchId", branchOne); });
            await db.InsertAsync(@"INSERT INTO business_tax_readiness
                (company_id, branch_id, pack_code, vat_number, commercial_registration_no, e_invoice_readiness_status)
                VALUES (@companyId, @branchId, 'saudi_gcc', 'VAT-B1', 'CR-B1', 'ready')",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branchId", branchOne); });

            var branchDocs = await db.QueryAsync("SELECT id, pack_code FROM compliance_records WHERE company_id=@companyId AND branch_id=@branchId",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branchId", branchOne); });
            Assert.Equal(2, branchDocs.Count);
            Assert.Contains(branchDocs, r => Convert.ToInt64(r["id"]) == saudiOne);
            Assert.DoesNotContain(branchDocs, r => Convert.ToInt64(r["id"]) == canadaTwo);

            var crossBranchUpdate = await db.ExecuteAsync("UPDATE compliance_records SET document_status='valid' WHERE company_id=@companyId AND branch_id=@branchId AND id=@id",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branchId", branchOne); c.Parameters.AddWithValue("@id", canadaTwo); });
            Assert.Equal(0, crossBranchUpdate);

            var iftaMileage = await db.ScalarDecimalAsync("SELECT SUM(distance) FROM jurisdiction_mileage_records WHERE company_id=@companyId AND branch_id=@branchId",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branchId", branchOne); });
            var iftaFuel = await db.ScalarDecimalAsync("SELECT SUM(fuel_volume) FROM jurisdiction_fuel_records WHERE company_id=@companyId AND branch_id=@branchId",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branchId", branchOne); });
            Assert.Equal(1200m, iftaMileage);
            Assert.Equal(300m, iftaFuel);

            var tax = await db.QuerySingleAsync("SELECT e_invoice_readiness_status FROM business_tax_readiness WHERE company_id=@companyId AND branch_id=@branchId AND pack_code='saudi_gcc'",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branchId", branchOne); });
            Assert.Equal("ready", tax!["eInvoiceReadinessStatus"]?.ToString());
        }
        finally
        {
            foreach (var table in new[] { "compliance_expiry_events", "inspection_defects", "vehicle_inspection_records", "jurisdiction_mileage_records", "jurisdiction_fuel_records", "business_tax_readiness", "compliance_records" })
                await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        }
    }

    [Fact]
    public async Task CanadaDvir_FailureAndRepairCertificationAreAtomicAndVehicleOwned()
    {
        var db = Db();
        await new FleetTmsSchemaService(db, NullLogger<FleetTmsSchemaService>.Instance).EnsureAsync();
        await new MarketPackSchemaService(db).EnsureAsync();
        var companyId = 898_000L + Random.Shared.Next(1, 1_000);
        const long branchId = 701;
        try
        {
            await db.ExecuteAsync("INSERT INTO companies(id,company_code,name,industry) OVERRIDING SYSTEM VALUE VALUES (@c,@code,'DVIR test','transport') ON CONFLICT DO NOTHING", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", $"DVIR-{companyId}"); });
            await db.ExecuteAsync("INSERT INTO tenant_market_packs(company_id,pack_code,status) VALUES (@c,'canada_na','active') ON CONFLICT (company_id,pack_code) DO UPDATE SET status='active'", c => c.Parameters.AddWithValue("@c", companyId));
            var vehicleId = await db.InsertAsync("INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status,availability_status) VALUES (@c,@b,'DVIR-701','Truck','legacy-fleet-identifier','DVIR-701','Available','available')", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); });
            var defects = JsonSerializer.SerializeToElement(new[] { new { description = "Brake pressure below threshold", severity = "critical", repairRequired = true } });
            var created = Payload(await InvokeMarket("CreateVehicleInspection", Principal(companyId, branchId), new Dictionary<string, object?>
            {
                ["vehicleId"] = vehicleId, ["vehicleLabel"] = "DVIR-701", ["inspectorName"] = "Senior Inspector",
                ["inspectionType"] = "pre_trip", ["status"] = "fail", ["defects"] = defects,
            }, db, CancellationToken.None));
            var inspectionId = created.RootElement.GetProperty("data").GetProperty("id").GetInt64();
            var held = await db.QuerySingleAsync("SELECT out_of_service,availability_status FROM vehicles WHERE id=@id", c => c.Parameters.AddWithValue("@id", vehicleId));
            Assert.True((bool)held!["outOfService"]!); Assert.Equal("out_of_service", held["availabilityStatus"]?.ToString());
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM inspection_defects WHERE inspection_id=@id AND repair_required AND repair_certified_at IS NULL", c => c.Parameters.AddWithValue("@id", inspectionId)));

            var secondCreated = Payload(await InvokeMarket("CreateVehicleInspection", Principal(companyId, branchId), new Dictionary<string, object?>
            {
                ["vehicleId"] = vehicleId, ["vehicleLabel"] = "DVIR-701", ["inspectorName"] = "Second Inspector",
                ["inspectionType"] = "post_trip", ["status"] = "needs_repair", ["defects"] = defects,
            }, db, CancellationToken.None));
            var secondInspectionId = secondCreated.RootElement.GetProperty("data").GetProperty("id").GetInt64();

            await InvokeMarket("UpdateVehicleInspection", inspectionId, Principal(companyId, branchId), new Dictionary<string, object?>
            {
                ["status"] = "pass", ["repairCertified"] = true, ["repairCertifiedBy"] = "Licensed Mechanic", ["repairNotes"] = "Brake system repaired and retested",
            }, db, CancellationToken.None);
            Assert.Equal("certified", (await db.QuerySingleAsync("SELECT repair_status FROM vehicle_inspection_records WHERE id=@id", c => c.Parameters.AddWithValue("@id", inspectionId)))!["repairStatus"]?.ToString());
            Assert.Equal(0, await db.ScalarLongAsync("SELECT COUNT(*) FROM inspection_defects WHERE inspection_id=@id AND repair_required AND repair_certified_at IS NULL", c => c.Parameters.AddWithValue("@id", inspectionId)));
            var stillHeld = await db.QuerySingleAsync("SELECT out_of_service,availability_status FROM vehicles WHERE id=@id", c => c.Parameters.AddWithValue("@id", vehicleId));
            Assert.True((bool)stillHeld!["outOfService"]!); Assert.Equal("out_of_service", stillHeld["availabilityStatus"]?.ToString());

            await InvokeMarket("UpdateVehicleInspection", secondInspectionId, Principal(companyId, branchId), new Dictionary<string, object?>
            {
                ["status"] = "pass", ["repairCertified"] = true, ["repairCertifiedBy"] = "Licensed Mechanic", ["repairNotes"] = "Second defect repaired",
            }, db, CancellationToken.None);
            var released = await db.QuerySingleAsync("SELECT out_of_service,availability_status FROM vehicles WHERE id=@id", c => c.Parameters.AddWithValue("@id", vehicleId));
            Assert.False((bool)released!["outOfService"]!); Assert.Equal("available", released["availabilityStatus"]?.ToString());
        }
        finally
        {
            foreach (var table in new[] { "inspection_defects", "vehicle_inspection_records", "usage_events", "usage_counters", "vehicles", "tenant_market_packs" })
                await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", companyId));
        }
    }

    [Fact]
    public async Task SaudiSurfaces_DenyWithoutEntitlementAndLegacyLedgerCannotMutate()
    {
        var db = Db();
        await new FleetTmsColdChainSchemaService(db, NullLogger<FleetTmsColdChainSchemaService>.Instance).EnsureAsync();
        await new MarketPackSchemaService(db).EnsureAsync();
        var http = Principal(999_991, 801);
        var legacyRequest = new FleetReadinessDocumentRequest(
            Kind: "Compliance", SubjectType: "Vehicle", SubjectId: null, SubjectName: "Truck", DocumentType: "Permit",
            DocumentNumber: null, TransportDocumentNo: null, PermitNo: null, VATNumber: null, CommercialRegistrationNo: null,
            CountryCode: "SA", NationalAddressBuildingNo: null, NationalAddressAdditionalNo: null, District: null, City: null,
            Region: null, PostalCode: null, DocumentStatus: "Active", IssueDate: null, HijriExpiryDate: null,
            GregorianExpiryDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), Notes: null);
        var fleetResults = new[]
        {
            await Invoke("SaudiRegions", http, db, CancellationToken.None),
            await Invoke("ComplianceDocuments", http, db, null!, null!, CancellationToken.None),
            await Invoke("CreateComplianceDocument", http, legacyRequest, db, CancellationToken.None),
            await Invoke("UpdateComplianceDocument", http, 1L, legacyRequest, db, CancellationToken.None),
            await Invoke("ComplianceExpiries", http, db, CancellationToken.None),
            await Invoke("VatInvoiceReady", http, db, CancellationToken.None),
        };
        var marketResults = new[]
        {
            await InvokeMarket("SaudiRegions", http, db, CancellationToken.None),
            await InvokeMarket("SaudiDocuments", http, db, CancellationToken.None),
            await InvokeMarket("CreateSaudiDocument", http, new Dictionary<string, object?>(), db, CancellationToken.None),
            await InvokeMarket("UpdateSaudiDocument", 1L, http, new Dictionary<string, object?>(), db, CancellationToken.None),
            await InvokeMarket("SaudiExpiries", http, db, CancellationToken.None),
            await InvokeMarket("SaudiVatReadiness", http, db, CancellationToken.None),
            await InvokeMarket("SetSaudiVatReadiness", http, new Dictionary<string, object?>(), db, CancellationToken.None),
        };
        Assert.All(fleetResults.Concat(marketResults), result => Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode));
    }

    [Fact]
    public async Task CanadaIftaAndHos_AreExplicitNonOperablePreviewContracts()
    {
        var db = Db(); await new MarketPackSchemaService(db).EnsureAsync();
        var companyId = 899_000L + Random.Shared.Next(1, 500);
        try
        {
            await db.ExecuteAsync("INSERT INTO companies(id,company_code,name,industry) OVERRIDING SYSTEM VALUE VALUES (@c,@code,'Preview test','transport') ON CONFLICT DO NOTHING", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", $"PREVIEW-{companyId}"); });
            await db.ExecuteAsync("INSERT INTO tenant_market_packs(company_id,pack_code,status) VALUES (@c,'canada_na','active') ON CONFLICT (company_id,pack_code) DO UPDATE SET status='active'", c => c.Parameters.AddWithValue("@c", companyId));
            var http = Principal(companyId, null);
            foreach (var method in new[] { "IftaReadiness", "HosReadiness" })
            {
                var payload = Payload(await InvokeMarket(method, http, db, CancellationToken.None)).RootElement.GetProperty("data");
                Assert.Equal("preview", payload.GetProperty("workflowStatus").GetString());
                Assert.False(payload.GetProperty("operable").GetBoolean());
            }
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM tenant_market_packs WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", companyId));
        }
    }

    [Fact]
    public async Task SaudiVatReadiness_IsDerivedFromValidIdentifiersAndLiveEvidence()
    {
        var db = Db(); await new MarketPackSchemaService(db).EnsureAsync();
        var companyId = 900_000L + Random.Shared.Next(1, 500); const long branchId = 802;
        try
        {
            await db.ExecuteAsync("INSERT INTO companies(id,company_code,name,industry) OVERRIDING SYSTEM VALUE VALUES (@c,@code,'VAT test','transport') ON CONFLICT DO NOTHING", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", $"VAT-{companyId}"); });
            await db.ExecuteAsync("INSERT INTO branches(id,company_id,branch_code,name,status) OVERRIDING SYSTEM VALUE VALUES (@b,@c,'SOLE','Sole VAT branch','Active')", c => { c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@c", companyId); });
            await db.ExecuteAsync("INSERT INTO tenant_market_packs(company_id,pack_code,status) VALUES (@c,'saudi_gcc','active') ON CONFLICT (company_id,pack_code) DO UPDATE SET status='active'", c => c.Parameters.AddWithValue("@c", companyId));
            var evidenceId = await db.InsertAsync("INSERT INTO compliance_records(company_id,branch_id,pack_code,subject_type,subject_name,doc_key,document_status,expiry_date) VALUES (@c,@b,'saudi_gcc','business','VAT Evidence','vat_registration','valid',CURRENT_DATE+90)", c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId); });
            var http = Principal(companyId, branchId);
            var rejected = await InvokeMarket("SetSaudiVatReadiness", http, new Dictionary<string, object?> { ["vatNumber"] = "bad", ["commercialRegistrationNo"] = "", ["evidenceRecordId"] = evidenceId, ["eInvoiceReadinessStatus"] = "ready" }, db, CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(rejected).StatusCode);
            var saved = Payload(await InvokeMarket("SetSaudiVatReadiness", http, new Dictionary<string, object?> { ["vatNumber"] = "312345678901233", ["commercialRegistrationNo"] = "1234567890", ["evidenceRecordId"] = evidenceId, ["eInvoiceReadinessStatus"] = "ready" }, db, CancellationToken.None));
            Assert.Equal("ready", saved.RootElement.GetProperty("data").GetProperty("eInvoiceReadinessStatus").GetString());
            var tenantAdminRead = Payload(await InvokeMarket("SaudiVatReadiness", Principal(companyId, null), db, CancellationToken.None));
            Assert.Equal("ready", tenantAdminRead.RootElement.GetProperty("data").GetProperty("readiness").GetProperty("eInvoiceReadinessStatus").GetString());
            await db.ExecuteAsync("UPDATE compliance_records SET expiry_date=CURRENT_DATE-1,document_status='valid' WHERE id=@id", c => c.Parameters.AddWithValue("@id", evidenceId));
            var read = Payload(await InvokeMarket("SaudiVatReadiness", http, db, CancellationToken.None));
            Assert.Equal("in_progress", read.RootElement.GetProperty("data").GetProperty("readiness").GetProperty("eInvoiceReadinessStatus").GetString());
            Assert.False(read.RootElement.GetProperty("data").GetProperty("readiness").GetProperty("evidenceValid").GetBoolean());
        }
        finally
        {
            foreach (var table in new[] { "business_tax_readiness", "compliance_records", "tenant_market_packs" }) await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM branches WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", companyId));
        }
    }

    [Fact]
    public async Task MarketPackBranchRollout_BackfillsSoleBranchAndAuditsMultiBranchNulls()
    {
        var db = Db(); await new MarketPackSchemaService(db).EnsureAsync();
        var soleCompany = 901_000L + Random.Shared.Next(1, 300); var multiCompany = soleCompany + 1_000;
        try
        {
            foreach (var company in new[] { soleCompany, multiCompany })
                await db.ExecuteAsync("INSERT INTO companies(id,company_code,name,industry) OVERRIDING SYSTEM VALUE VALUES (@c,@code,'Branch rollout','transport') ON CONFLICT DO NOTHING", c => { c.Parameters.AddWithValue("@c", company); c.Parameters.AddWithValue("@code", $"BR-{company}"); });
            var soleBranch = await db.InsertAsync("INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,'SOLE','Sole','Active')", c => c.Parameters.AddWithValue("@c", soleCompany));
            await db.ExecuteAsync("INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,'ONE','One','Active'),(@c,'TWO','Two','Active')", c => c.Parameters.AddWithValue("@c", multiCompany));
            var soleRecord = await db.InsertAsync("INSERT INTO compliance_records(company_id,pack_code,subject_type,doc_key) VALUES (@c,'saudi_gcc','business','permit')", c => c.Parameters.AddWithValue("@c", soleCompany));
            var soleInspection = await db.InsertAsync("INSERT INTO vehicle_inspection_records(company_id,vehicle_label,inspector_name) VALUES (@c,'SOLE-TRUCK','Inspector')", c => c.Parameters.AddWithValue("@c", soleCompany));
            var soleDefect = await db.InsertAsync("INSERT INTO inspection_defects(company_id,inspection_id,description) VALUES (@c,@i,'Sole defect')", c => { c.Parameters.AddWithValue("@c", soleCompany); c.Parameters.AddWithValue("@i", soleInspection); });
            var multiRecord = await db.InsertAsync("INSERT INTO compliance_records(company_id,pack_code,subject_type,doc_key) VALUES (@c,'saudi_gcc','business','permit')", c => c.Parameters.AddWithValue("@c", multiCompany));
            await db.InsertAsync("INSERT INTO compliance_expiry_events(company_id,pack_code,record_id,message) VALUES (@c,'saudi_gcc',@r,'Review')", c => { c.Parameters.AddWithValue("@c", multiCompany); c.Parameters.AddWithValue("@r", multiRecord); });
            var multiInspection = await db.InsertAsync("INSERT INTO vehicle_inspection_records(company_id,vehicle_label,inspector_name) VALUES (@c,'MULTI-TRUCK','Inspector')", c => c.Parameters.AddWithValue("@c", multiCompany));
            await db.InsertAsync("INSERT INTO inspection_defects(company_id,inspection_id,description) VALUES (@c,@i,'Multi defect')", c => { c.Parameters.AddWithValue("@c", multiCompany); c.Parameters.AddWithValue("@i", multiInspection); });
            await db.InsertAsync("INSERT INTO jurisdiction_mileage_records(company_id,province_state,distance,tax_period) VALUES (@c,'ON',10,'2026-Q2')", c => c.Parameters.AddWithValue("@c", multiCompany));
            await db.InsertAsync("INSERT INTO jurisdiction_fuel_records(company_id,province_state,fuel_volume,tax_period) VALUES (@c,'ON',5,'2026-Q2')", c => c.Parameters.AddWithValue("@c", multiCompany));
            await db.InsertAsync("INSERT INTO driver_duty_status_records(company_id,driver_name) VALUES (@c,'Driver')", c => c.Parameters.AddWithValue("@c", multiCompany));
            await db.InsertAsync("INSERT INTO eld_device_registry(company_id,eld_provider_name) VALUES (@c,'Provider')", c => c.Parameters.AddWithValue("@c", multiCompany));
            await db.InsertAsync("INSERT INTO business_tax_readiness(company_id,pack_code) VALUES (@c,'saudi_gcc')", c => c.Parameters.AddWithValue("@c", multiCompany));

            await new MarketPackSchemaService(db).EnsureAsync();
            Assert.Equal(soleBranch, Convert.ToInt64((await db.QuerySingleAsync("SELECT branch_id FROM compliance_records WHERE id=@id", c => c.Parameters.AddWithValue("@id", soleRecord)))!["branchId"]));
            Assert.Equal(soleBranch, Convert.ToInt64((await db.QuerySingleAsync("SELECT branch_id FROM inspection_defects WHERE id=@id", c => c.Parameters.AddWithValue("@id", soleDefect)))!["branchId"]));
            Assert.Equal(9, await db.ScalarLongAsync("SELECT COUNT(*) FROM market_pack_branch_migration_audit WHERE company_id=@c AND classification='tenant_unassigned'", c => c.Parameters.AddWithValue("@c", multiCompany)));
            Assert.Equal(9, await db.ScalarLongAsync("SELECT COUNT(*) FROM market_pack_branch_migration_audit a WHERE company_id=@c AND NOT EXISTS (SELECT 1 FROM branches b WHERE b.id=a.resolved_branch_id)", c => c.Parameters.AddWithValue("@c", multiCompany)));
            Assert.True((await db.QuerySingleAsync("SELECT branch_id FROM compliance_records WHERE id=@id", c => c.Parameters.AddWithValue("@id", multiRecord)))!["branchId"] is null or DBNull);
        }
        finally
        {
            foreach (var table in new[] { "market_pack_branch_migration_audit", "compliance_expiry_events", "inspection_defects", "vehicle_inspection_records", "jurisdiction_mileage_records", "jurisdiction_fuel_records", "driver_duty_status_records", "eld_device_registry", "business_tax_readiness", "compliance_records" })
                await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id IN (@a,@b)", c => { c.Parameters.AddWithValue("@a", soleCompany); c.Parameters.AddWithValue("@b", multiCompany); });
            await db.ExecuteAsync("DELETE FROM branches WHERE company_id IN (@a,@b)", c => { c.Parameters.AddWithValue("@a", soleCompany); c.Parameters.AddWithValue("@b", multiCompany); });
            await db.ExecuteAsync("DELETE FROM companies WHERE id IN (@a,@b)", c => { c.Parameters.AddWithValue("@a", soleCompany); c.Parameters.AddWithValue("@b", multiCompany); });
        }
    }

    private static async Task<long> InsertDevice(Database db, long companyId, long branchId, long zoneId, string code)
        => await db.InsertAsync(@"INSERT INTO fleet_tms_temperature_devices
            (company_id, branch_id, device_code, name, zone_id, status)
            VALUES (@companyId, @branchId, @code, @code, @zoneId, 'Active')",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branchId", branchId); c.Parameters.AddWithValue("@code", code); c.Parameters.AddWithValue("@zoneId", zoneId); });

    private static async Task<long> InsertAsset(Database db, long companyId, long branchId, long typeId, string tag)
        => await db.InsertAsync(@"INSERT INTO fleet_tms_assets
            (company_id, branch_id, asset_type_id, asset_tag, name, status, quantity)
            VALUES (@companyId, @branchId, @typeId, @tag, @tag, 'Available', 1)",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branchId", branchId); c.Parameters.AddWithValue("@typeId", typeId); c.Parameters.AddWithValue("@tag", tag); });

    private static async Task<long> InsertReadinessDoc(Database db, long companyId, long branchId, string subjectType, string subjectName, string vat, string cr, DateOnly expiry)
        => await db.InsertAsync(@"INSERT INTO fleet_tms_readiness_documents
            (company_id, branch_id, kind, subject_type, subject_name, document_type, vat_number,
             commercial_registration_no, document_status, expiry_status, gregorian_expiry_date)
            VALUES (@companyId, @branchId, 'Compliance', @subjectType, @subjectName, 'VAT registration',
                    @vat, @cr, 'Active', 'ExpiringSoon', @expiry)",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branchId", branchId); c.Parameters.AddWithValue("@subjectType", subjectType); c.Parameters.AddWithValue("@subjectName", subjectName); c.Parameters.AddWithValue("@vat", vat); c.Parameters.AddWithValue("@cr", cr); c.Parameters.AddWithValue("@expiry", expiry.ToDateTime(TimeOnly.MinValue)); });

    private static async Task<long> InsertComplianceRecord(Database db, long companyId, long branchId, string pack, string subjectType, string subjectName, string docKey)
        => await db.InsertAsync(@"INSERT INTO compliance_records
            (company_id, branch_id, pack_code, subject_type, subject_name, doc_key, document_status, expiry_date)
            VALUES (@companyId, @branchId, @pack, @subjectType, @subjectName, @docKey, 'expiring', CURRENT_DATE + 10)",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@branchId", branchId); c.Parameters.AddWithValue("@pack", pack); c.Parameters.AddWithValue("@subjectType", subjectType); c.Parameters.AddWithValue("@subjectName", subjectName); c.Parameters.AddWithValue("@docKey", docKey); });

    private static Database Db()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
        }).Build();
        return new Database(config);
    }

    private static DefaultHttpContext Principal(long companyId, long? branchId)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
        if (branchId.HasValue) http.Items[EndpointMappings.AuthBranchIdItemKey] = branchId.Value;
        http.Items[EndpointMappings.AuthUserIdItemKey] = 42L;
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "fleet:view", "fleet:manage", "compliance:view", "compliance:manage" };
        http.Items[EndpointMappings.AuthRoleItemKey] = "Tenant Admin";
        return http;
    }

    private static async Task<IResult> Invoke(string methodName, params object[] arguments)
    {
        var method = typeof(FleetTmsColdChainEndpoints).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
        try { return await (Task<IResult>)method.Invoke(null, arguments)!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static async Task<IResult> InvokeMarket(string methodName, params object[] arguments)
    {
        var method = typeof(MarketPackEndpoints).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
        try { return await (Task<IResult>)method.Invoke(null, arguments)!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static JsonDocument Payload(IResult result)
    {
        var value = Assert.IsAssignableFrom<IValueHttpResult>(result).Value;
        return JsonDocument.Parse(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
}
