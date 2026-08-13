using Opstrax.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Data;
using Opstrax.Api.Services;
using System.Reflection;
using System.Text.Json;

namespace Opstrax.Tests;

public sealed class FleetTmsWorkspaceWave1Tests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task OverviewReturnsOnlyTheAuthenticatedBranchButTenantWideReturnsAllBranches()
    {
        var db = Database();
        await new FleetTmsSchemaService(db, NullLogger<FleetTmsSchemaService>.Instance).EnsureAsync();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            foreach (var branchId in new long?[] { 101, 202, null })
                await db.ExecuteAsync("INSERT INTO fleet_tms_shipments(company_id,branch_id,shipment_number,status) VALUES (@company,@branch,@number,'Booked')",
                    c => { c.Parameters.AddWithValue("@company", companyId); c.Parameters.AddWithValue("@branch", (object?)branchId ?? DBNull.Value); c.Parameters.AddWithValue("@number", $"T-{branchId?.ToString() ?? "ALL"}"); });

            Assert.Equal(1, await ActiveShipmentsFromOverview(db, companyId, 101));
            Assert.Equal(3, await ActiveShipmentsFromOverview(db, companyId, null));
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM fleet_tms_shipments WHERE company_id=@company", c => c.Parameters.AddWithValue("@company", companyId));
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DispatchCreatesBranchOwnedTasksAndCannotMutateAnotherBranch()
    {
        var db = Database();
        await new FleetTmsSchemaService(db, NullLogger<FleetTmsSchemaService>.Instance).EnsureAsync();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var branchId = 301L;
        var hasCanonicalCore = await db.ScalarLongAsync(
            "SELECT CASE WHEN to_regclass('public.companies') IS NOT NULL AND to_regclass('public.drivers') IS NOT NULL AND to_regclass('public.vehicles') IS NOT NULL THEN 1 ELSE 0 END") == 1;
        try
        {
            if (hasCanonicalCore)
            {
                await db.ExecuteAsync(
                    "INSERT INTO companies(id,company_code,name,industry) OVERRIDING SYSTEM VALUE VALUES (@company,@code,'Fleet TMS Test','Transportation')",
                    c => { c.Parameters.AddWithValue("@company", companyId); c.Parameters.AddWithValue("@code", $"FTMS-{companyId}"); });
                // In an integrated Core schema, dispatch is intentionally gated by
                // canonical Fleet ownership and eligibility, not only by the workspace projection.
                await db.ExecuteAsync(
                    "INSERT INTO drivers(company_id,branch_id,driver_code,full_name,status) VALUES (@company,@branch,@driverCode,'Driver One','Available')",
                    c => { c.Parameters.AddWithValue("@company", companyId); c.Parameters.AddWithValue("@branch", branchId); c.Parameters.AddWithValue("@driverCode", $"DRV-{companyId}"); });
                await db.ExecuteAsync(
                    "INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin_exception_type,alternate_identifier,status) VALUES (@company,@branch,'UNIT-301','Truck','legacy-fleet-identifier','UNIT-301','Available')",
                    c => { c.Parameters.AddWithValue("@company", companyId); c.Parameters.AddWithValue("@branch", branchId); });
            }
            var vehicleId = await db.InsertAsync("INSERT INTO fleet_tms_vehicles(company_id,branch_id,vehicle_number,status) VALUES (@company,@branch,'UNIT-301','Available')",
                c => { c.Parameters.AddWithValue("@company", companyId); c.Parameters.AddWithValue("@branch", branchId); });
            var shipmentId = await db.InsertAsync("INSERT INTO fleet_tms_shipments(company_id,branch_id,shipment_number,status) VALUES (@company,@branch,'SHIP-301','Booked')",
                c => { c.Parameters.AddWithValue("@company", companyId); c.Parameters.AddWithValue("@branch", branchId); });
            await db.ExecuteAsync("INSERT INTO fleet_tms_shipment_stops(company_id,branch_id,shipment_id,stop_type,sequence_no,location_name,planned_arrival_at) VALUES (@company,@branch,@shipment,'Delivery',1,'Customer Dock',NOW()+INTERVAL '1 hour')",
                c => { c.Parameters.AddWithValue("@company", companyId); c.Parameters.AddWithValue("@branch", branchId); c.Parameters.AddWithValue("@shipment", shipmentId); });

            await Invoke("DispatchShipment", Principal(companyId, branchId), shipmentId,
                new DispatchShipmentRequest("UNIT-301", "Driver One", "ROUTE-301", "Pilot dispatch"), db, CancellationToken.None);
            Assert.Equal("InTransit", (await db.QuerySingleAsync("SELECT status FROM fleet_tms_shipments WHERE id=@id", c => c.Parameters.AddWithValue("@id", shipmentId)))!["status"]);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_driver_tasks WHERE shipment_id=@id AND branch_id=@branch",
                c => { c.Parameters.AddWithValue("@id", shipmentId); c.Parameters.AddWithValue("@branch", branchId); }));
            Assert.Equal("OnTrip", (await db.QuerySingleAsync("SELECT status FROM fleet_tms_vehicles WHERE id=@id", c => c.Parameters.AddWithValue("@id", vehicleId)))!["status"]);
            var taskId = await db.ScalarLongAsync("SELECT id FROM fleet_tms_driver_tasks WHERE shipment_id=@id", c => c.Parameters.AddWithValue("@id", shipmentId));
            await Invoke("ArriveDriverTask", Principal(companyId, branchId), taskId, db, CancellationToken.None);
            await Invoke("CompleteDriverTask", Principal(companyId, branchId), taskId, new NotesRequest("Task complete"), db, CancellationToken.None);
            Assert.Equal("Completed", (await db.QuerySingleAsync("SELECT status FROM fleet_tms_driver_tasks WHERE id=@id", c => c.Parameters.AddWithValue("@id", taskId)))!["status"]);
            Assert.Equal("Completed", (await db.QuerySingleAsync("SELECT status FROM fleet_tms_shipment_stops WHERE shipment_id=@id", c => c.Parameters.AddWithValue("@id", shipmentId)))!["status"]);

            var otherShipment = await db.InsertAsync("INSERT INTO fleet_tms_shipments(company_id,branch_id,shipment_number,status) VALUES (@company,999,'SHIP-999','Booked')",
                c => c.Parameters.AddWithValue("@company", companyId));
            await Invoke("DispatchShipment", Principal(companyId, branchId), otherShipment,
                new DispatchShipmentRequest("UNIT-301", "Driver One", "ROUTE-301", null), db, CancellationToken.None);
            Assert.Equal("Booked", (await db.QuerySingleAsync("SELECT status FROM fleet_tms_shipments WHERE id=@id", c => c.Parameters.AddWithValue("@id", otherShipment)))!["status"]);
        }
        finally
        {
            foreach (var table in new[] { "fleet_tms_driver_tasks", "fleet_tms_shipment_events", "fleet_tms_shipment_stops", "fleet_tms_shipments", "fleet_tms_vehicles" })
                await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@company", c => c.Parameters.AddWithValue("@company", companyId));
            if (hasCanonicalCore)
            {
                await db.ExecuteAsync("DELETE FROM vehicles WHERE company_id=@company", c => c.Parameters.AddWithValue("@company", companyId));
                await db.ExecuteAsync("DELETE FROM drivers WHERE company_id=@company", c => c.Parameters.AddWithValue("@company", companyId));
                await db.ExecuteAsync("DELETE FROM companies WHERE id=@company", c => c.Parameters.AddWithValue("@company", companyId));
            }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StopLifecyclePersistsOrderedTransitionsAndInheritedBranchEvents()
    {
        var db = Database();
        await new FleetTmsSchemaService(db, NullLogger<FleetTmsSchemaService>.Instance).EnsureAsync();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var branchId = 401L;
        try
        {
            var shipmentId = await db.InsertAsync("INSERT INTO fleet_tms_shipments(company_id,branch_id,shipment_number,status) VALUES (@company,@branch,'SHIP-401','Booked')",
                c => { c.Parameters.AddWithValue("@company", companyId); c.Parameters.AddWithValue("@branch", branchId); });
            var request = new ShipmentStopRequest("Delivery", 1, "Pilot Customer", null, null, null, null, "Toronto", "ON", "M5V", "Canada",
                null, null, null, 43.65m, -79.38m, DateTime.UtcNow.AddHours(2), "First delivery");
            await Invoke("CreateStop", Principal(companyId, branchId), shipmentId, request, db, CancellationToken.None);
            var stop = await db.QuerySingleAsync("SELECT * FROM fleet_tms_shipment_stops WHERE shipment_id=@id", c => c.Parameters.AddWithValue("@id", shipmentId));
            Assert.NotNull(stop);
            Assert.Equal(branchId, Convert.ToInt64(stop!["branchId"]));
            var stopId = Convert.ToInt64(stop["id"]);

            await Invoke("ArriveStop", Principal(companyId, branchId), shipmentId, stopId, db, CancellationToken.None);
            await Invoke("CompleteStop", Principal(companyId, branchId), shipmentId, stopId, new NotesRequest("Delivered"), db, CancellationToken.None);
            Assert.Equal("Completed", (await db.QuerySingleAsync("SELECT status FROM fleet_tms_shipment_stops WHERE id=@id", c => c.Parameters.AddWithValue("@id", stopId)))!["status"]);
            Assert.Equal(3, await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_shipment_events WHERE shipment_id=@id AND branch_id=@branch",
                c => { c.Parameters.AddWithValue("@id", shipmentId); c.Parameters.AddWithValue("@branch", branchId); }));
        }
        finally
        {
            foreach (var table in new[] { "fleet_tms_shipment_events", "fleet_tms_shipment_stops", "fleet_tms_shipments" })
                await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@company", c => c.Parameters.AddWithValue("@company", companyId));
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PodSubmitVerifyAndPublicTrackingExposeOnlyVerifiedTokenScopedProof()
    {
        var db = Database();
        await new FleetTmsSchemaService(db, NullLogger<FleetTmsSchemaService>.Instance).EnsureAsync();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var branchId = 501L;
        const string token = "pilot-proof-token-with-enough-entropy-501";
        try
        {
            var shipmentId = await db.InsertAsync("INSERT INTO fleet_tms_shipments(company_id,branch_id,shipment_number,status,origin,destination) VALUES (@company,@branch,'SHIP-501','InTransit','A','B')",
                c => { c.Parameters.AddWithValue("@company", companyId); c.Parameters.AddWithValue("@branch", branchId); });
            var stopId = await db.InsertAsync("INSERT INTO fleet_tms_shipment_stops(company_id,branch_id,shipment_id,stop_type,sequence_no,location_name,status,planned_arrival_at,completed_at) VALUES (@company,@branch,@shipment,'Delivery',1,'Pilot Dock','Completed',NOW(),NOW())",
                c => { c.Parameters.AddWithValue("@company", companyId); c.Parameters.AddWithValue("@branch", branchId); c.Parameters.AddWithValue("@shipment", shipmentId); });
            var podRequest = new PodRequest(stopId, "Receiver", null, null, "https://storage.example/proof.png", null, "Intact", "Good", 43.6m, -79.3m);
            await Invoke("CreatePod", Principal(companyId, branchId), shipmentId, podRequest, db, CancellationToken.None);
            var podId = await db.ScalarLongAsync("SELECT id FROM fleet_tms_pods WHERE shipment_id=@id", c => c.Parameters.AddWithValue("@id", shipmentId));
            await Invoke("SubmitPod", Principal(companyId, branchId), shipmentId, podId, db, CancellationToken.None);
            await Invoke("VerifyPod", Principal(companyId, branchId), shipmentId, podId, db, CancellationToken.None);
            Assert.Equal("Verified", (await db.QuerySingleAsync("SELECT status FROM fleet_tms_pods WHERE id=@id", c => c.Parameters.AddWithValue("@id", podId)))!["status"]);
            Assert.Equal("Delivered", (await db.QuerySingleAsync("SELECT status FROM fleet_tms_shipments WHERE id=@id", c => c.Parameters.AddWithValue("@id", shipmentId)))!["status"]);
            await Invoke("MarkInvoiceReady", Principal(companyId, branchId), shipmentId, new InvoiceReadyRequest(false, "Verified for pilot billing"), db, CancellationToken.None);
            Assert.True(Convert.ToBoolean((await db.QuerySingleAsync("SELECT is_invoice_ready FROM fleet_tms_shipments WHERE id=@id", c => c.Parameters.AddWithValue("@id", shipmentId)))!["isInvoiceReady"]));

            await db.ExecuteAsync("INSERT INTO fleet_tms_tracking_links(company_id,branch_id,shipment_id,token_hash,expires_at_utc) VALUES (@company,@branch,@shipment,@hash,NOW()+INTERVAL '1 day')",
                c => { c.Parameters.AddWithValue("@company", companyId); c.Parameters.AddWithValue("@branch", branchId); c.Parameters.AddWithValue("@shipment", shipmentId); c.Parameters.AddWithValue("@hash", FleetTmsEndpoints.HashTrackingToken(token)); });
            var publicResult = Assert.IsAssignableFrom<IValueHttpResult>(await Invoke("PublicTrack", token, db, CancellationToken.None));
            var payload = JsonSerializer.Serialize(publicResult.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Contains($"/api/public/shipments/track/{token}/pod/{podId}/asset/photo", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("https://storage.example/proof.png", payload, StringComparison.Ordinal);
        }
        finally
        {
            foreach (var table in new[] { "fleet_tms_tracking_links", "fleet_tms_pods", "fleet_tms_shipment_events", "fleet_tms_shipment_stops", "fleet_tms_shipments" })
                await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@company", c => c.Parameters.AddWithValue("@company", companyId));
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MultiStopShipmentDeliversAndInvoicesOnlyAfterEveryDeliveryHasVerifiedPod()
    {
        var db = Database();
        await new FleetTmsSchemaService(db, NullLogger<FleetTmsSchemaService>.Instance).EnsureAsync();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(1000, 9999);
        const long branchId = 551;
        try
        {
            var shipmentId = await db.InsertAsync("INSERT INTO fleet_tms_shipments(company_id,branch_id,shipment_number,status) VALUES (@company,@branch,@number,'InTransit')",
                c => { c.Parameters.AddWithValue("@company", companyId); c.Parameters.AddWithValue("@branch", branchId); c.Parameters.AddWithValue("@number", $"MULTI-{companyId}"); });
            var stopIds = new List<long>();
            foreach (var seq in new[] { 1, 2 })
                stopIds.Add(await db.InsertAsync("INSERT INTO fleet_tms_shipment_stops(company_id,branch_id,shipment_id,stop_type,sequence_no,location_name,status,planned_arrival_at,completed_at) VALUES (@company,@branch,@shipment,'Delivery',@seq,@loc,'Completed',NOW(),NOW())",
                    c => { c.Parameters.AddWithValue("@company", companyId); c.Parameters.AddWithValue("@branch", branchId); c.Parameters.AddWithValue("@shipment", shipmentId); c.Parameters.AddWithValue("@seq", seq); c.Parameters.AddWithValue("@loc", $"Dock {seq}"); }));

            var podIds = new List<long>();
            foreach (var stopId in stopIds)
            {
                await Invoke("CreatePod", Principal(companyId, branchId), shipmentId,
                    new PodRequest(stopId, "Receiver", null, null, $"https://storage.example/{stopId}.jpg", null, null, "Good", null, null), db, CancellationToken.None);
                var podId = await db.ScalarLongAsync("SELECT id FROM fleet_tms_pods WHERE shipment_id=@shipment AND stop_id=@stop", c => { c.Parameters.AddWithValue("@shipment", shipmentId); c.Parameters.AddWithValue("@stop", stopId); });
                podIds.Add(podId);
                await Invoke("SubmitPod", Principal(companyId, branchId), shipmentId, podId, db, CancellationToken.None);
            }

            await Invoke("VerifyPod", Principal(companyId, branchId), shipmentId, podIds[0], db, CancellationToken.None);
            var partial = await db.QuerySingleAsync("SELECT status,pod_status,is_invoice_ready FROM fleet_tms_shipments WHERE id=@id", c => c.Parameters.AddWithValue("@id", shipmentId));
            Assert.Equal("InTransit", partial!["status"]);
            Assert.Equal("Partial", partial["podStatus"]);
            await Invoke("MarkInvoiceReady", Principal(companyId, branchId), shipmentId, new InvoiceReadyRequest(false, null), db, CancellationToken.None);
            Assert.False(Convert.ToBoolean((await db.QuerySingleAsync("SELECT is_invoice_ready FROM fleet_tms_shipments WHERE id=@id", c => c.Parameters.AddWithValue("@id", shipmentId)))!["isInvoiceReady"]));

            await Invoke("VerifyPod", Principal(companyId, branchId), shipmentId, podIds[1], db, CancellationToken.None);
            await Invoke("MarkInvoiceReady", Principal(companyId, branchId), shipmentId, new InvoiceReadyRequest(false, "All stops verified"), db, CancellationToken.None);
            var complete = await db.QuerySingleAsync("SELECT status,pod_status,is_invoice_ready FROM fleet_tms_shipments WHERE id=@id", c => c.Parameters.AddWithValue("@id", shipmentId));
            Assert.Equal("Delivered", complete!["status"]);
            Assert.Equal("Verified", complete["podStatus"]);
            Assert.True(Convert.ToBoolean(complete["isInvoiceReady"]));
        }
        finally
        {
            foreach (var table in new[] { "fleet_tms_pods", "fleet_tms_shipment_events", "fleet_tms_shipment_stops", "fleet_tms_shipments" })
                await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@company", c => c.Parameters.AddWithValue("@company", companyId));
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConcurrentDuplicateStopCreationIsRejectedByDatabaseIdentityConstraint()
    {
        var db = Database();
        await new FleetTmsSchemaService(db, NullLogger<FleetTmsSchemaService>.Instance).EnsureAsync();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.Next(10000, 99999);
        try
        {
            var shipmentId = await db.InsertAsync("INSERT INTO fleet_tms_shipments(company_id,shipment_number,status) VALUES (@company,@number,'Booked')",
                c => { c.Parameters.AddWithValue("@company", companyId); c.Parameters.AddWithValue("@number", $"RACE-{companyId}"); });
            async Task<bool> Insert()
            {
                try
                {
                    await db.ExecuteAsync("INSERT INTO fleet_tms_shipment_stops(company_id,shipment_id,stop_type,sequence_no,location_name,planned_arrival_at) VALUES (@company,@shipment,'Delivery',1,'Race Dock',NOW())",
                        c => { c.Parameters.AddWithValue("@company", companyId); c.Parameters.AddWithValue("@shipment", shipmentId); });
                    return true;
                }
                catch (Npgsql.PostgresException ex) when (ex.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation) { return false; }
            }
            var outcomes = await Task.WhenAll(Insert(), Insert());
            Assert.Single(outcomes, static success => success);
            Assert.Equal(1, await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_shipment_stops WHERE company_id=@company AND shipment_id=@shipment AND sequence_no=1",
                c => { c.Parameters.AddWithValue("@company", companyId); c.Parameters.AddWithValue("@shipment", shipmentId); }));
        }
        finally
        {
            foreach (var table in new[] { "fleet_tms_shipment_stops", "fleet_tms_shipments" })
                await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@company", c => c.Parameters.AddWithValue("@company", companyId));
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task VehicleServiceAndMaintenanceCloseRestoreBranchOwnedVehicleReadiness()
    {
        var db = Database();
        await new FleetTmsSchemaService(db, NullLogger<FleetTmsSchemaService>.Instance).EnsureAsync();
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var branchId = 601L;
        try
        {
            var vehicleId = await db.InsertAsync("INSERT INTO fleet_tms_vehicles(company_id,branch_id,vehicle_number,status,health_status) VALUES (@company,@branch,'UNIT-601','Available','Healthy')",
                c => { c.Parameters.AddWithValue("@company", companyId); c.Parameters.AddWithValue("@branch", branchId); });
            await Invoke("ServiceVehicle", Principal(companyId, branchId), vehicleId,
                new VehicleServiceRequest("Maintenance", "Needs Service", DateTime.UtcNow.AddDays(30), "Scheduled service"), db, CancellationToken.None);
            Assert.Equal("Maintenance", (await db.QuerySingleAsync("SELECT status FROM fleet_tms_vehicles WHERE id=@id", c => c.Parameters.AddWithValue("@id", vehicleId)))!["status"]);

            var ticketId = await db.InsertAsync("INSERT INTO fleet_tms_maintenance_tickets(company_id,branch_id,work_order_number,vehicle_number,status) VALUES (@company,@branch,'WO-601','UNIT-601','Open')",
                c => { c.Parameters.AddWithValue("@company", companyId); c.Parameters.AddWithValue("@branch", branchId); });
            await Invoke("CloseMaintenance", Principal(companyId, branchId), ticketId,
                new CloseMaintenanceRequest("Closed", 250m, "Work complete"), db, CancellationToken.None);
            Assert.Equal("Closed", (await db.QuerySingleAsync("SELECT status FROM fleet_tms_maintenance_tickets WHERE id=@id", c => c.Parameters.AddWithValue("@id", ticketId)))!["status"]);
            Assert.Equal("Available", (await db.QuerySingleAsync("SELECT status FROM fleet_tms_vehicles WHERE id=@id", c => c.Parameters.AddWithValue("@id", vehicleId)))!["status"]);

            var fuelId = await db.InsertAsync("INSERT INTO fleet_tms_fuel_events(company_id,branch_id,vehicle_number,liters,cost) VALUES (@company,@branch,'UNIT-601',500,900)",
                c => { c.Parameters.AddWithValue("@company", companyId); c.Parameters.AddWithValue("@branch", branchId); });
            await Invoke("FlagFuelEvent", Principal(companyId, branchId), fuelId, new FlagFuelEventRequest(true, "Volume exceeds expected tank capacity"), db, CancellationToken.None);
            Assert.True(Convert.ToBoolean((await db.QuerySingleAsync("SELECT anomaly_flag FROM fleet_tms_fuel_events WHERE id=@id", c => c.Parameters.AddWithValue("@id", fuelId)))!["anomalyFlag"]));
        }
        finally
        {
            foreach (var table in new[] { "fleet_tms_fuel_events", "fleet_tms_maintenance_tickets", "fleet_tms_vehicles" })
                await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@company", c => c.Parameters.AddWithValue("@company", companyId));
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CarrierAssignmentUsesCanonicalTenantCarrierAndPersistsCommercialTerms()
    {
        var db = Database();
        await new FleetTmsSchemaService(db, NullLogger<FleetTmsSchemaService>.Instance).EnsureAsync();
        await EnsureCarrierFixtureSchema(db);
        var companyId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var branchId = 701L;
        try
        {
            var carrierId = await db.InsertAsync("INSERT INTO carriers(company_id,name,carrier_number,status,compliance_status) VALUES (@company,'Pilot Carrier','CAR-701','Active','Compliant')",
                c => c.Parameters.AddWithValue("@company", companyId));
            var shipmentId = await db.InsertAsync("INSERT INTO fleet_tms_shipments(company_id,branch_id,shipment_number,status) VALUES (@company,@branch,'SHIP-701','Booked')",
                c => { c.Parameters.AddWithValue("@company", companyId); c.Parameters.AddWithValue("@branch", branchId); });
            await Invoke("AssignShipmentCarrier", Principal(companyId, branchId), shipmentId,
                new AssignCarrierRequest(carrierId, 1200m, 1100m, "Pilot lane terms"), db, CancellationToken.None);
            var shipment = await db.QuerySingleAsync("SELECT * FROM fleet_tms_shipments WHERE id=@id", c => c.Parameters.AddWithValue("@id", shipmentId));
            Assert.Equal(carrierId, Convert.ToInt64(shipment!["carrierId"]));
            Assert.Equal("Pilot Carrier", shipment["carrierName"]);
            Assert.Equal(1100m, Convert.ToDecimal(shipment["carrierAgreedAmount"]));
        }
        finally
        {
            foreach (var table in new[] { "fleet_tms_shipment_events", "fleet_tms_shipments", "carriers" })
                await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@company", c => c.Parameters.AddWithValue("@company", companyId));
        }
    }
    [Fact]
    public void OverviewAndAllWorkspaceListsApplyStrictBranchOwnership()
    {
        var source = Source("backend-dotnet", "Controllers", "FleetTmsEndpoints.cs");
        Assert.Contains("var owned = Owned(http);", source, StringComparison.Ordinal);
        Assert.Contains("WHERE company_id=@companyId{owned}", source, StringComparison.Ordinal);
        Assert.Contains("\"WHERE company_id=@companyId\" + Owned(http)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Fleet TMS is not available for branch-scoped accounts.", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Transfer")]
    [InlineData("")]
    public void StopValidationRejectsUnknownTypes(string stopType)
    {
        var request = new ShipmentStopRequest(stopType, 1, "Dock", null, null, null, null, null, null, null, null,
            null, null, null, null, null, DateTime.UtcNow.AddHours(1), null);
        Assert.NotNull(FleetTmsEndpoints.ValidateStopRequest(request));
    }

    [Fact]
    public void PodValidationRejectsUnsafeAssetUrlsAndCoordinates()
    {
        var unsafeUrl = new PodRequest(1, "Recipient", null, "http://storage/proof.png", null, null, null, "Good", null, null);
        var unsafeCoordinate = unsafeUrl with { SignatureUrl = "https://storage.example/proof.png", CapturedLatitude = 91 };
        Assert.Contains("HTTPS", FleetTmsEndpoints.ValidatePodRequest(unsafeUrl, true), StringComparison.Ordinal);
        Assert.Contains("latitude", FleetTmsEndpoints.ValidatePodRequest(unsafeCoordinate, true), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CarrierAssignmentIsRealAndBookingQuoteAuthoringIsExplicitlyOutsideWorkspace()
    {
        var api = Source("frontend", "src", "services", "fleetTmsApi.ts");
        Assert.Contains("/api/fleet-tms/shipments/${shipmentId}/carrier", api, StringComparison.Ordinal);
        Assert.Contains("outside the Fleet Workspace contract", api, StringComparison.Ordinal);
        Assert.DoesNotContain("Carrier & booking management ships in a later Fleet TMS release", api, StringComparison.Ordinal);
    }

    private static string Source(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }

    private static Database Database()
    {
        var connection = TestDb.ConnectionString;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connection,
        }).Build();
        return new Database(configuration);
    }

    private static async Task<long> ActiveShipmentsFromOverview(Database db, long companyId, long? branchId)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
        if (branchId is not null) http.Items[EndpointMappings.AuthBranchIdItemKey] = branchId;
        var method = typeof(FleetTmsEndpoints).GetMethod("Overview", BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task<IResult>)method.Invoke(null, [http, db, CancellationToken.None])!;
        var result = Assert.IsAssignableFrom<IValueHttpResult>(await task);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return json.RootElement.GetProperty("data").GetProperty("summary").GetProperty("activeShipments").GetInt64();
    }

    private static DefaultHttpContext Principal(long companyId, long branchId)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
        http.Items[EndpointMappings.AuthBranchIdItemKey] = branchId;
        http.Items[EndpointMappings.AuthUserIdItemKey] = 42L;
        return http;
    }

    private static async Task<IResult> Invoke(string methodName, params object[] arguments)
    {
        var method = typeof(FleetTmsEndpoints).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
        return await (Task<IResult>)method.Invoke(null, arguments)!;
    }

    private static Task<int> EnsureCarrierFixtureSchema(Database db) => db.ExecuteAsync(@"
CREATE TABLE IF NOT EXISTS carriers (
 id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, company_id BIGINT NOT NULL, name VARCHAR(220) NOT NULL,
 carrier_number VARCHAR(80), mc_number VARCHAR(80), contact_name VARCHAR(160), phone VARCHAR(50), email VARCHAR(220),
 region VARCHAR(120), status VARCHAR(50) NOT NULL DEFAULT 'Active', compliance_status VARCHAR(80) NOT NULL DEFAULT 'Compliant',
 insurance_expiry DATE, contract_status VARCHAR(80) NOT NULL DEFAULT 'Active', on_time_percent DECIMAL(6,2) NOT NULL DEFAULT 90,
 safety_score DECIMAL(6,2) NOT NULL DEFAULT 88, cost_score DECIMAL(6,2) NOT NULL DEFAULT 82,
 performance_score DECIMAL(6,2) NOT NULL DEFAULT 86, risk_score DECIMAL(6,2) NOT NULL DEFAULT 20,
 recommended_action VARCHAR(260), notes TEXT, updated_at TIMESTAMPTZ, deleted_at TIMESTAMPTZ)");
}
