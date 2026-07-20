using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public class IntegratedModuleSimulationTests
{
    private const string LocalConnectionString =
        "Host=127.0.0.1;Port=5433;Database=opstrax_local;Username=zayra;Password=zayra";

    [Fact]
    public async Task Connected_Module_Simulation_Runs_Core_Workspaces_Together()
    {
        var db = CreateDatabase();
        var companyId = NextCompanyId();
        var ambient = new AmbientCorrelationContext();
        var ai = new PostgresAiFoundationService(db, ambient);
        var approval = new PostgresApprovalWorkflowService(db, ambient);
        var events = new PostgresDomainEventPublisher(db, ambient);
        var idempotency = new InMemoryIdempotencyService();
        var stage9 = new Stage9OperationalFoundationService(db, ai, approval, events, idempotency, ambient);
        var telemetry = new TelemetryLiveStateService(db);
        var safety = new SafetyMaintenanceFoundationService(db, ai);
        var telemetrySchema = new TelemetrySchemaService(db);
        var safetySchema = new SafetySchemaService(db);
        var maintenanceSchema = new MaintenanceSchemaService(db);
        var batch3Schema = new Batch3SchemaService(db);
        var batch4Schema = new Batch4SchemaService(db);
        var smfSchema = new SafetyMaintenanceFoundationSchemaService(db);
        var fleetSchema = new FleetTmsSchemaService(db, NullLogger<FleetTmsSchemaService>.Instance);
        var coldChainSchema = new FleetTmsColdChainSchemaService(db, NullLogger<FleetTmsColdChainSchemaService>.Instance);
        var logisticsSchema = new FleetTmsLogisticsSchemaService(db, NullLogger<FleetTmsLogisticsSchemaService>.Instance);
        var stage9Schema = new Stage9SchemaService(db);

        try
        {
            await fleetSchema.EnsureAsync();
            await coldChainSchema.EnsureAsync();
            await logisticsSchema.EnsureAsync();
            await stage9Schema.EnsureAsync();
            await telemetrySchema.EnsureAsync();
            await safetySchema.EnsureAsync();
            await maintenanceSchema.EnsureAsync();
            await batch3Schema.EnsureAsync();
            await batch4Schema.EnsureAsync();
            await smfSchema.EnsureAsync();

            var seeded = await SeedSimulationTenantAsync(db, companyId, stage9, telemetry);

            var fleetTask = GetFleetWorkspaceSnapshotAsync(db, companyId);
            var coldChainTask = GetColdChainSnapshotAsync(db, companyId);
            var logisticsTask = GetLogisticsSnapshotAsync(db, companyId);
            var readinessTask = GetSaudiReadinessSnapshotAsync(db, companyId);
            var stage9Task = stage9.GetExecutionSummaryAsync(companyId, seeded.JobId);
            var telemetryTask = telemetry.BuildSummaryAsync(companyId);
            var safetyTask = safety.RefreshFleetHealthSnapshotAsync(companyId);
            var safetySummaryTask = safety.GetSummaryAsync(companyId);

            await Task.WhenAll(fleetTask, coldChainTask, logisticsTask, readinessTask, stage9Task, telemetryTask, safetyTask, safetySummaryTask);

            var fleet = await fleetTask;
            var coldChain = await coldChainTask;
            var logistics = await logisticsTask;
            var readiness = await readinessTask;
            var stage9Summary = await stage9Task;
            var telemetrySummary = await telemetryTask;
            var safetySummary = await safetySummaryTask;

            Assert.True(Convert.ToInt64(fleet["active_shipments"]) >= 1);
            Assert.True(Convert.ToInt64(fleet["active_vehicles"]) >= 1);
            Assert.True(Convert.ToInt64(coldChain["open_alerts"]) >= 1);
            Assert.True(Convert.ToInt64(coldChain["device_count"]) >= 1);
            Assert.True(Convert.ToInt64(logistics["active_orders"]) >= 1);
            Assert.True(Convert.ToInt64(logistics["active_routes"]) >= 1);
            Assert.True(Convert.ToInt64(readiness["document_count"]) >= 1);
            Assert.True(Convert.ToInt64(readiness["invoice_ready_count"]) >= 1);

            Assert.NotNull(stage9Summary);
            Assert.Equal(seeded.JobId, Convert.ToInt64(stage9Summary!["job_id"]));
            Assert.Equal(seeded.TripId, Convert.ToInt64(stage9Summary["trip_id"]));
            Assert.Equal("validated", ((Dictionary<string, object?>)stage9Summary["proof_package_summary"])["status"]?.ToString());
            Assert.Equal("ready", ((Dictionary<string, object?>)stage9Summary["billing_confidence_summary"])["status"]?.ToString());
            var mobileReadyActions = stage9Summary["mobile_ready_actions"] as IEnumerable<object>;
            Assert.NotEmpty(mobileReadyActions ?? Array.Empty<object>());

            Assert.NotEmpty((IEnumerable<Dictionary<string, object?>>)telemetrySummary["alerts"]!);
            Assert.True((bool)safetySummary["foundation_ready"]!);
            Assert.Contains("fleet_health_summary", safetySummary.Keys);
            Assert.Contains("next_best_actions", safetySummary.Keys);
        }
        finally
        {
            await CleanupTenantAsync(db, companyId);
        }
    }

    private static async Task<Dictionary<string, object?>> GetFleetWorkspaceSnapshotAsync(Database db, long companyId)
    {
        var activeShipments = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM fleet_tms_shipments WHERE company_id=@companyId AND status NOT IN ('Delivered','Cancelled')",
            c => c.Parameters.AddWithValue("@companyId", companyId));
        var activeVehicles = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM fleet_tms_vehicles WHERE company_id=@companyId AND status IN ('Available','OnTrip','Maintenance')",
            c => c.Parameters.AddWithValue("@companyId", companyId));

        return new Dictionary<string, object?>
        {
            ["active_shipments"] = activeShipments,
            ["active_vehicles"] = activeVehicles,
        };
    }

    private static async Task<Dictionary<string, object?>> GetColdChainSnapshotAsync(Database db, long companyId)
    {
        var openAlerts = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM fleet_tms_temperature_alerts WHERE company_id=@companyId AND status IN ('Open','InReview')",
            c => c.Parameters.AddWithValue("@companyId", companyId));
        var deviceCount = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM fleet_tms_temperature_devices WHERE company_id=@companyId",
            c => c.Parameters.AddWithValue("@companyId", companyId));

        return new Dictionary<string, object?>
        {
            ["open_alerts"] = openAlerts,
            ["device_count"] = deviceCount,
        };
    }

    private static async Task<Dictionary<string, object?>> GetLogisticsSnapshotAsync(Database db, long companyId)
    {
        var activeOrders = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM fleet_tms_dispatch_orders WHERE company_id=@companyId AND status NOT IN ('Delivered','Returned')",
            c => c.Parameters.AddWithValue("@companyId", companyId));
        var activeRoutes = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM fleet_tms_delivery_routes WHERE company_id=@companyId AND status IN ('Ready','Active','Delayed')",
            c => c.Parameters.AddWithValue("@companyId", companyId));

        return new Dictionary<string, object?>
        {
            ["active_orders"] = activeOrders,
            ["active_routes"] = activeRoutes,
        };
    }

    private static async Task<Dictionary<string, object?>> GetSaudiReadinessSnapshotAsync(Database db, long companyId)
    {
        var documentCount = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM fleet_tms_readiness_documents WHERE company_id=@companyId",
            c => c.Parameters.AddWithValue("@companyId", companyId));
        var invoiceReadyCount = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM fleet_tms_shipments WHERE company_id=@companyId AND is_invoice_ready",
            c => c.Parameters.AddWithValue("@companyId", companyId));

        return new Dictionary<string, object?>
        {
            ["document_count"] = documentCount,
            ["invoice_ready_count"] = invoiceReadyCount,
        };
    }

    private static async Task<SeededSimulationContext> SeedSimulationTenantAsync(
        Database db,
        long companyId,
        Stage9OperationalFoundationService stage9,
        TelemetryLiveStateService telemetry)
    {
        var vehicleId = await GetAnyVehicleIdAsync(db);
        var driverId = await GetAnyDriverIdAsync(db);
        var shipmentNumber = $"SIM-SHP-{companyId}";
        var routeCode = $"SIM-ROUTE-{companyId}";
        var orderNumber = $"SIM-ORD-{companyId}";
        var jobId = 900000 + companyId;
        var tripId = 910000 + companyId;

        await db.ExecuteAsync(
            @"INSERT INTO fleet_tms_shipments
                (company_id, shipment_number, customer_name, customer_segment, origin, destination, city, status, priority, mode,
                 piece_count, weight_kg, volume_cbm, declared_value, carrier_name, customer_vat_number,
                 customer_commercial_registration_no, customer_national_address_building_no,
                 customer_national_address_additional_no, customer_national_address_district,
                 customer_national_address_city, customer_national_address_region, customer_national_address_postal_code,
                 customer_national_address_country, driver_name, vehicle_number, route_code, pod_status,
                 temperature_range, notes, is_invoice_ready, invoice_ready_at_utc, invoice_readiness_notes,
                 pickup_scheduled_at_utc, picked_up_at_utc, delivered_at_utc, updated_at_utc)
              SELECT
                @companyId, @shipmentNumber, 'Integration Test Customer', 'Retail', 'Riyadh DC', 'Dammam Hub', 'Dammam',
                'PickedUp', 'High', 'Road', 12, 1840.5, 24.5, 35000, 'SIM Carrier', '300000000000001',
                '1010101010', '1001', '2002', 'Olaya', 'Riyadh', 'Riyadh', '11564', 'SA',
                'Test Driver', 'SIM-TRK-001', @routeCode, 'Verified', '2-8C',
                'Integration seed shipment for live module simulation.', true, NOW(), 'Invoice ready through simulation seed.',
                NOW() - INTERVAL '4 hours', NOW() - INTERVAL '3 hours', NOW() - INTERVAL '1 hour', NOW()
              WHERE NOT EXISTS (
                SELECT 1 FROM fleet_tms_shipments WHERE company_id=@companyId AND shipment_number=@shipmentNumber
              )",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@shipmentNumber", shipmentNumber);
                c.Parameters.AddWithValue("@routeCode", routeCode);
            });

        await db.ExecuteAsync(
            @"INSERT INTO fleet_tms_shipment_stops
                (company_id, shipment_id, stop_type, sequence_no, location_name, contact_name, contact_phone, address_line1, city,
                 region, postal_code, country, saudi_national_address_building_no, saudi_national_address_additional_no,
                 saudi_national_address_district, latitude, longitude, planned_arrival_at, actual_arrival_at, completed_at,
                 status, notes, updated_at)
              SELECT @companyId, s.id, 'Pickup', 1, 'Riyadh DC', 'Warehouse Lead', '+96650001001', 'King Fahd Road', 'Riyadh',
                     'Riyadh', '11564', 'Saudi Arabia', '1001', '2002', 'Olaya', 24.7136, 46.6753,
                     NOW() - INTERVAL '4 hours', NOW() - INTERVAL '4 hours', NOW() - INTERVAL '4 hours',
                     'Completed', 'Seed pickup complete', NOW()
              FROM fleet_tms_shipments s
              WHERE s.company_id=@companyId AND s.shipment_number=@shipmentNumber
              ON CONFLICT DO NOTHING",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@shipmentNumber", shipmentNumber);
            });

        await db.ExecuteAsync(
            @"INSERT INTO fleet_tms_pods
                (company_id, shipment_id, stop_id, captured_by_user_id, driver_id, vehicle_id, recipient_name, recipient_phone,
                 signature_url, photo_url, document_url, notes, delivery_condition, captured_latitude, captured_longitude,
                 captured_at, verified_at, status, created_at, updated_at)
              SELECT @companyId, s.id, st.id, NULL, @driverId, @vehicleId, 'Receiving Desk', '+96650001002',
                     'https://seed.local/signatures/sim-pod.png', 'https://seed.local/photos/sim-pod.png', 'https://seed.local/docs/sim-pod.pdf',
                     'Simulation POD for integrated module coverage.', 'Good', 24.7136, 46.6753,
                     NOW() - INTERVAL '45 minutes', NOW() - INTERVAL '35 minutes', 'Verified', NOW() - INTERVAL '45 minutes', NOW() - INTERVAL '35 minutes'
              FROM fleet_tms_shipments s
              JOIN fleet_tms_shipment_stops st ON st.company_id=s.company_id AND st.shipment_id=s.id
              WHERE s.company_id=@companyId AND s.shipment_number=@shipmentNumber AND st.stop_type='Pickup'
              ON CONFLICT DO NOTHING",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@shipmentNumber", shipmentNumber);
                c.Parameters.AddWithValue("@driverId", driverId);
                c.Parameters.AddWithValue("@vehicleId", vehicleId);
            });

        await db.ExecuteAsync(
            @"INSERT INTO fleet_tms_tracking_links
                (company_id, shipment_id, token, expires_at_utc, is_revoked, shared_by, created_at_utc, updated_at_utc)
              SELECT @companyId, s.id, @token, NOW() + INTERVAL '7 days', false, 'simulation.seed', NOW(), NOW()
              FROM fleet_tms_shipments s
              WHERE s.company_id=@companyId AND s.shipment_number=@shipmentNumber
              ON CONFLICT DO NOTHING",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@shipmentNumber", shipmentNumber);
                c.Parameters.AddWithValue("@token", $"sim-track-{companyId}");
            });

        await db.ExecuteAsync(
            @"INSERT INTO fleet_tms_vehicles
                (company_id, vehicle_number, plate_number, type, status, driver_name, capacity_kg, capacity_cbm, current_load_kg,
                 fuel_level_percent, odometer_km, health_status, is_refrigerated, temperature_celsius, last_known_location,
                 last_ping_at_utc, last_service_at_utc, next_service_at_utc, notes, created_at_utc, updated_at_utc)
              VALUES
                (@companyId, 'SIM-TRK-001', 'SIM-PLATE-001', 'Reefer', 'OnTrip', 'Test Driver', 25000, 62, 8400,
                 77, 142000, 'Healthy', true, 4.2, 'Riyadh DC', NOW() - INTERVAL '2 minutes', NOW() - INTERVAL '12 days',
                 NOW() + INTERVAL '18 days', 'Simulation vehicle for integrated testing.', NOW(), NOW())
              ON CONFLICT DO NOTHING",
            c => c.Parameters.AddWithValue("@companyId", companyId));

        await db.ExecuteAsync(
            @"INSERT INTO fleet_tms_temperature_zones
                (company_id, code, name, min_celsius, max_celsius, color, is_active, notes, created_at_utc, updated_at_utc)
              VALUES
                (@companyId, 'SIM-CHILL', 'Simulation Chilled', 2, 8, '#22d3ee', true, 'Simulation zone', NOW(), NOW()),
                (@companyId, 'SIM-FROZEN', 'Simulation Frozen', -22, -16, '#3b82f6', true, 'Simulation zone', NOW(), NOW())
              ON CONFLICT DO NOTHING",
            c => c.Parameters.AddWithValue("@companyId", companyId));

        await db.ExecuteAsync(
            @"INSERT INTO fleet_tms_temperature_devices
                (company_id, device_code, name, zone_id, shipment_id, vehicle_number, status,
                 last_reported_temperature_celsius, battery_percent, last_ping_at_utc, notes,
                 created_at_utc, updated_at_utc)
              SELECT @companyId, 'SIM-TMP-001', 'Simulation Reefer Sensor', z.id, s.id, 'SIM-TRK-001', 'Active',
                     4.1, 92, NOW() - INTERVAL '3 minutes', 'Simulation cold-chain sensor.',
                     NOW(), NOW()
              FROM fleet_tms_shipments s
              JOIN fleet_tms_temperature_zones z ON z.company_id=s.company_id AND z.code='SIM-CHILL'
              WHERE s.company_id=@companyId AND s.shipment_number=@shipmentNumber
              ON CONFLICT DO NOTHING",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@shipmentNumber", shipmentNumber);
            });

        await db.ExecuteAsync(
            @"INSERT INTO fleet_tms_temperature_readings
                (company_id, device_id, shipment_id, zone_id, temperature_celsius, humidity_percent, latitude, longitude,
                 source, status, notes, recorded_at_utc, created_at_utc, applied_policy_code, applied_policy_scope,
                 applied_min_celsius, applied_max_celsius)
              SELECT @companyId, d.id, s.id, z.id, 4.6, 53, 24.7136, 46.6753,
                     'Simulation', 'Normal', 'Simulation compliant reading.', NOW() - INTERVAL '2 minutes', NOW(),
                     'SIM-CHILL', 'shipment', 2, 8
              FROM fleet_tms_shipments s
              JOIN fleet_tms_temperature_zones z ON z.company_id=s.company_id AND z.code='SIM-CHILL'
              JOIN fleet_tms_temperature_devices d ON d.company_id=s.company_id AND d.shipment_id=s.id
              WHERE s.company_id=@companyId AND s.shipment_number=@shipmentNumber
              ON CONFLICT DO NOTHING",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@shipmentNumber", shipmentNumber);
            });

        var breachReadingId = await db.InsertAsync(
            @"INSERT INTO fleet_tms_temperature_readings
                (company_id, device_id, shipment_id, zone_id, temperature_celsius, humidity_percent, latitude, longitude,
                 source, status, notes, recorded_at_utc, created_at_utc, applied_policy_code, applied_policy_scope,
                 applied_min_celsius, applied_max_celsius)
              SELECT @companyId, d.id, s.id, z.id, 10.8, 58, 21.5433, 39.1728, 'Simulation', 'Breach',
                     'Simulation breach reading.', NOW() - INTERVAL '1 minute', NOW(), 'SIM-FROZEN', 'shipment', -22, -16
              FROM fleet_tms_shipments s
              JOIN fleet_tms_temperature_zones z ON z.company_id=s.company_id AND z.code='SIM-CHILL'
              JOIN fleet_tms_temperature_devices d ON d.company_id=s.company_id AND d.shipment_id=s.id
              WHERE s.company_id=@companyId AND s.shipment_number=@shipmentNumber",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@shipmentNumber", shipmentNumber);
            });

        await db.ExecuteAsync(
            @"INSERT INTO fleet_tms_temperature_alerts
                (company_id, device_id, shipment_id, reading_id, alert_type, severity, status,
                 threshold_min, threshold_max, measured_temperature, triggered_at_utc, resolved_at_utc, resolved_by,
                 resolution_notes, notes, applied_policy_code, applied_policy_scope, acknowledged_at_utc, acknowledged_by)
              SELECT @companyId, d.id, s.id, @readingId, 'TemperatureBreach', 'High', 'Open',
                     2, 8, 10.8, NOW() - INTERVAL '1 minute', NULL, '', '', 'Simulation alert for breach coverage',
                     'SIM-FROZEN', 'shipment', NOW() - INTERVAL '30 seconds', 'simulation.seed'
              FROM fleet_tms_shipments s
              JOIN fleet_tms_temperature_zones z ON z.company_id=s.company_id AND z.code='SIM-CHILL'
              JOIN fleet_tms_temperature_devices d ON d.company_id=s.company_id AND d.shipment_id=s.id
              WHERE s.company_id=@companyId AND s.shipment_number=@shipmentNumber
              ON CONFLICT DO NOTHING",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@shipmentNumber", shipmentNumber);
                c.Parameters.AddWithValue("@readingId", breachReadingId);
            });

        await db.ExecuteAsync(
            @"INSERT INTO fleet_tms_readiness_documents
                (company_id, kind, subject_type, subject_id, subject_name, document_type, document_number, transport_document_no,
                 permit_no, vat_number, commercial_registration_no, country_code, national_address_building_no,
                 national_address_additional_no, district, city, region, postal_code, document_status, expiry_status,
                 issue_date, gregorian_expiry_date, notes)
              VALUES
                (@companyId, 'Transport', 'Vehicle', 'SIM-TRK-001', 'Simulation Reefer', 'Transport Operating Card',
                 'SIM-TOC-001', 'SIM-TDN-001', 'SIM-PERMIT-001', '', '', 'SA', '1001', '2002', 'Olaya', 'Riyadh',
                 'Riyadh Province', '11564', 'Active', 'Healthy', CURRENT_DATE - INTERVAL '180 days',
                 CURRENT_DATE + INTERVAL '180 days', 'Simulation Saudi/GCC readiness document.')
              ON CONFLICT DO NOTHING",
            c => c.Parameters.AddWithValue("@companyId", companyId));

        await db.ExecuteAsync(
            @"INSERT INTO fleet_tms_asset_types
                (company_id, code, name, description, is_returnable, created_at_utc, updated_at_utc)
              VALUES
                (@companyId, 'SIM-PALLET', 'Simulation Pallet', 'Returnable pallet for integrated simulation', true, NOW(), NOW())
              ON CONFLICT DO NOTHING",
            c => c.Parameters.AddWithValue("@companyId", companyId));

        await db.ExecuteAsync(
            @"INSERT INTO fleet_tms_assets
                (company_id, asset_type_id, asset_tag, name, status, current_location, condition, is_returnable,
                 quantity, unit_of_measure, notes, last_seen_at_utc, created_at_utc, updated_at_utc)
              SELECT @companyId, t.id, 'SIM-AST-001', 'Simulation Pallet Stack', 'InUse', 'Riyadh DC', 'Good', true,
                     4, 'Each', 'Simulation returnable asset.', NOW() - INTERVAL '8 minutes', NOW(), NOW()
              FROM fleet_tms_asset_types t
              WHERE t.company_id=@companyId AND t.code='SIM-PALLET'
              ON CONFLICT DO NOTHING",
            c => c.Parameters.AddWithValue("@companyId", companyId));

        await db.ExecuteAsync(
            @"INSERT INTO fleet_tms_asset_assignments
                (company_id, asset_id, shipment_id, carrier_id, assignee_type, assignee_name, quantity, status,
                 assigned_at_utc, released_at_utc, notes)
              SELECT @companyId, a.id, s.id, NULL, 'Shipment', s.shipment_number, 4, 'Assigned', NOW() - INTERVAL '20 minutes', NULL,
                     'Simulation asset assignment.'
              FROM fleet_tms_assets a
              JOIN fleet_tms_shipments s ON s.company_id=a.company_id
              WHERE a.company_id=@companyId AND a.asset_tag='SIM-AST-001' AND s.shipment_number=@shipmentNumber
              ON CONFLICT DO NOTHING",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@shipmentNumber", shipmentNumber);
            });

        await db.ExecuteAsync(
            @"INSERT INTO fleet_tms_asset_events
                (company_id, asset_id, event_type, quantity, location, actor_name, occurred_at_utc, notes)
              SELECT @companyId, a.id, 'check_out', 4, 'Riyadh DC', 'simulation.seed', NOW() - INTERVAL '18 minutes', 'Simulation asset check-out.'
              FROM fleet_tms_assets a
              WHERE a.company_id=@companyId AND a.asset_tag='SIM-AST-001'
              ON CONFLICT DO NOTHING",
            c => c.Parameters.AddWithValue("@companyId", companyId));

        await db.ExecuteAsync(
            @"INSERT INTO fleet_tms_dispatch_orders
                (company_id, order_number, customer_name, customer_segment, sales_channel, city, area, status, priority, item_count,
                 order_value, route_code, driver_name, vehicle_number, dispatch_notes, created_at_utc, promised_at_utc, dispatched_at_utc,
                 delivered_at_utc, updated_at_utc)
              VALUES
                (@companyId, @orderNumber, 'Simulation Customer', 'Retail', 'Portal', 'Riyadh', 'North Hub', 'Dispatched',
                 'High', 7, 18250, @routeCode, 'Test Driver', 'SIM-TRK-001',
                 'Simulation dispatch order for integrated coverage.', NOW() - INTERVAL '2 hours', NOW() + INTERVAL '2 hours',
                 NOW() - INTERVAL '90 minutes', NULL, NOW())
              ON CONFLICT DO NOTHING",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@orderNumber", orderNumber);
                c.Parameters.AddWithValue("@routeCode", routeCode);
            });

        await db.ExecuteAsync(
            @"INSERT INTO fleet_tms_delivery_routes
                (company_id, route_code, hub, territory, driver_name, vehicle_number, status, planned_stops, completed_stops,
                 distance_km, completion_percent, current_stop, next_stop, planned_for_date, departure_time_utc, eta_complete_utc, notes)
              VALUES
                (@companyId, @routeCode, 'Riyadh North Hub', 'North Corridor', 'Test Driver', 'SIM-TRK-001', 'Active',
                 3, 2, 42.5, 66.7, 'Warehouse', 'Customer Drop', CURRENT_DATE, NOW() - INTERVAL '2 hours',
                 NOW() + INTERVAL '1 hour', 'Simulation route for integrated coverage.')
              ON CONFLICT DO NOTHING",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@routeCode", routeCode);
            });

        await db.ExecuteAsync(
            @"INSERT INTO fleet_tms_last_mile_stops
                (company_id, order_number, route_code, customer_name, address_line, city, region, postal_code, country,
                 saudi_national_address_building_no, saudi_national_address_additional_no, saudi_national_address_district,
                 status, proof_status, recipient_name, attempt_count, rider_name, time_window, eta_utc, delivered_at_utc,
                 exception_reason, created_at_utc, updated_at_utc)
              VALUES
                (@companyId, @orderNumber, @routeCode, 'Simulation Customer', 'King Fahd Road', 'Riyadh', 'Riyadh Province',
                 '11564', 'Saudi Arabia', '1001', '2002', 'Olaya', 'OutForDelivery', 'Captured', 'Receiving Desk', 0,
                 'Test Driver', '14:00-16:00', NOW() + INTERVAL '45 minutes', NULL, '', NOW() - INTERVAL '2 hours', NOW())
              ON CONFLICT DO NOTHING",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@orderNumber", orderNumber);
                c.Parameters.AddWithValue("@routeCode", routeCode);
            });

        using var scope = AmbientCorrelationContext.Begin(
            $"sim-{companyId}-{Guid.NewGuid():N}",
            $"cause-{Guid.NewGuid():N}",
            $"req-{Guid.NewGuid():N}",
            companyId.ToString(),
            ActorTypes.TenantUser,
            "42");

        var recommendation = await stage9.RecommendSmartAssignmentAsync(
            companyId,
            jobId,
            tripId,
            new Dictionary<string, object?>
            {
                ["recommendedDriverId"] = driverId,
                ["recommendedVehicleId"] = vehicleId,
                ["score"] = 0.88m,
                ["confidenceScore"] = 0.82m,
                ["riskLevel"] = "low",
                ["recommendationType"] = "operations.execution_summary",
                ["sourceChannel"] = "simulation",
                ["clientGeneratedId"] = $"sim-reco-{companyId}",
            },
            "simulation",
            $"sim-reco-{companyId}",
            $"sim-reco-idem-{companyId}");

        Assert.NotNull(recommendation);

        var siteAccess = await stage9.CreateSiteAccessRequirementAsync(companyId, jobId, tripId, new Dictionary<string, object?>
        {
            ["requirementType"] = "gate_pass",
            ["instructions"] = "Simulation gate pass verified at loading bay",
            ["contactName"] = "Gate Desk",
            ["contactPhone"] = "+96650001003",
        });

        var accessDocument = await stage9.CreateAccessDocumentAsync(companyId, jobId, tripId, new Dictionary<string, object?>
        {
            ["documentType"] = "gate_pass",
            ["documentNo"] = $"GP-SIM-{companyId}",
            ["status"] = "required",
        }, $"sim-access-{companyId}");

        var pickupAuthorization = await stage9.CreatePickupAuthorizationAsync(companyId, jobId, tripId, new Dictionary<string, object?>
        {
            ["authorizationNo"] = $"PA-SIM-{companyId}",
            ["thirdPartyName"] = "Simulation 3PL",
            ["authorizedPersonName"] = "Noura Al-Faisal",
            ["status"] = "required",
        }, $"sim-pickup-{companyId}");

        var handover = await stage9.CreateWarehouseHandoverAsync(companyId, jobId, tripId, new Dictionary<string, object?>
        {
            ["handoverType"] = "pickup",
            ["warehouseName"] = "Simulation Warehouse",
            ["warehouseReferenceNo"] = $"WH-SIM-{companyId}",
            ["status"] = "scheduled",
        }, $"sim-handover-{companyId}");

        var proof = await stage9.CreateProofPackageAsync(companyId, jobId, tripId, new Dictionary<string, object?>
        {
            ["proofType"] = "proof_of_delivery",
            ["receiverName"] = "Receiving Desk",
            ["receiverPhone"] = "+96650001004",
            ["status"] = "draft",
        }, $"sim-proof-{companyId}");

        var artifact = await stage9.CreateProofArtifactAsync(companyId, Convert.ToInt64(proof!["id"]), new Dictionary<string, object?>
        {
            ["artifactType"] = "photo",
            ["capturedByUserId"] = 42,
            ["notes"] = "Simulation handoff photo",
            ["deviceId"] = "sim-device-1",
        }, $"sim-artifact-{companyId}");

        await stage9.UpdateAccessDocumentStatusAsync(companyId, Convert.ToInt64(accessDocument!["id"]), new Dictionary<string, object?>
        {
            ["status"] = "verified",
        });

        await stage9.PatchSiteAccessRequirementAsync(companyId, Convert.ToInt64(siteAccess!["id"]), new Dictionary<string, object?>
        {
            ["status"] = "verified",
        });

        await stage9.UpdatePickupAuthorizationAsync(companyId, Convert.ToInt64(pickupAuthorization!["id"]), new Dictionary<string, object?>
        {
            ["status"] = "verified",
        });

        await stage9.UpdateWarehouseHandoverAsync(companyId, Convert.ToInt64(handover!["id"]), new Dictionary<string, object?>
        {
            ["status"] = "completed",
        });

        var submit = await stage9.SubmitProofPackageAsync(companyId, Convert.ToInt64(proof["id"]), new Dictionary<string, object?>
        {
            ["exceptionNote"] = "Simulation proof package submitted",
        });
        Assert.True(submit.Success);

        var validate = await stage9.ValidateProofPackageAsync(companyId, Convert.ToInt64(proof["id"]), new Dictionary<string, object?>());
        Assert.True(validate.Success);
        Assert.Equal("passed", validate.ValidationStatus);

        await db.ExecuteAsync(
            @"INSERT INTO telemetry_rules (company_id, rule_type, threshold_value, severity, enabled, notes, created_at, updated_at)
              VALUES (@companyId, 'speeding', 65, 'High', true, 'Simulation rule', NOW(), NOW())
              ON CONFLICT (company_id, rule_type) DO UPDATE SET threshold_value=EXCLUDED.threshold_value, severity=EXCLUDED.severity, enabled=TRUE, updated_at=NOW()",
            c => c.Parameters.AddWithValue("@companyId", companyId));

        await db.ExecuteAsync(
            @"INSERT INTO telemetry_rules (company_id, rule_type, threshold_value, severity, enabled, notes, created_at, updated_at)
              VALUES (@companyId, 'stale_device', 900, 'Warning', true, 'Simulation rule', NOW(), NOW())
              ON CONFLICT (company_id, rule_type) DO UPDATE SET threshold_value=EXCLUDED.threshold_value, severity=EXCLUDED.severity, enabled=TRUE, updated_at=NOW()",
            c => c.Parameters.AddWithValue("@companyId", companyId));

        // Active devices require real credentials (ck_eld_devices_active_credentials).
        var rawApiKey  = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var hmacSecret = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var deviceId = await db.InsertAsync(
            @"INSERT INTO eld_devices (company_id, device_serial, vehicle_id, driver_id, api_key_hash, hmac_secret, status, created_at)
              VALUES (@companyId, @serial, @vehicleId, @driverId, encode(sha256(@rawKey::bytea), 'hex'), @hmac, 'Active', NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@serial", $"SIM-ELD-{companyId}");
                c.Parameters.AddWithValue("@vehicleId", vehicleId);
                c.Parameters.AddWithValue("@driverId", driverId);
                c.Parameters.AddWithValue("@rawKey", rawApiKey);
                c.Parameters.AddWithValue("@hmac", hmacSecret);
            });

        await db.ExecuteAsync(
            @"INSERT INTO latest_vehicle_positions
                (company_id, vehicle_id, device_id, driver_id, lat, lng, speed_mph, heading,
                 accuracy_meters, engine_status, fuel_level, odometer_miles, battery_voltage,
                 event_time, received_at, event_count, source_event_id, telemetry_status,
                 risk_level, alert_count, open_alert_count, next_action, summary_json, updated_at)
              VALUES
                (@companyId, @vehicleId, @deviceId, @driverId, 24.7136000, 46.6753000, 51.0, 90,
                 5.0, 'Running', 78.5, 220123.4, 12.6, NOW() - INTERVAL '3 minutes',
                 NOW() - INTERVAL '3 minutes', 4, 90001, 'healthy',
                 'low', 1, 1, 'Continue route monitoring', '{}'::jsonb, NOW())
              ON CONFLICT (company_id, vehicle_id) DO UPDATE SET
                 device_id=EXCLUDED.device_id,
                 driver_id=EXCLUDED.driver_id,
                 speed_mph=EXCLUDED.speed_mph,
                 event_time=EXCLUDED.event_time,
                 received_at=EXCLUDED.received_at,
                 updated_at=NOW()",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@vehicleId", vehicleId);
                c.Parameters.AddWithValue("@deviceId", deviceId);
                c.Parameters.AddWithValue("@driverId", driverId);
            });

        await db.ExecuteAsync(
            @"INSERT INTO telemetry_alerts
                (company_id, vehicle_id, device_id, driver_id, alert_type, severity, message, source_event_id, status,
                 acknowledged_at, acknowledged_by, resolved_at, resolved_by, created_at, updated_at)
              VALUES
                (@companyId, @vehicleId, @deviceId, @driverId, 'speeding', 'Medium',
                 'Simulation alert for module coverage.', 90001, 'Open', NOW() - INTERVAL '2 minutes', 'simulation.seed', NULL, NULL,
                 NOW() - INTERVAL '2 minutes', NULL)
              ON CONFLICT DO NOTHING",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@vehicleId", vehicleId);
                c.Parameters.AddWithValue("@deviceId", deviceId);
                c.Parameters.AddWithValue("@driverId", driverId);
            });

        await telemetry.RefreshVehicleAsync(companyId, vehicleId);

        return new SeededSimulationContext(companyId, jobId, tripId, shipmentNumber, routeCode, orderNumber, vehicleId, driverId, deviceId);
    }

    private static Database CreateDatabase()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = LocalConnectionString,
            })
            .Build();
        return new Database(config);
    }

    private static long NextCompanyId() => Interlocked.Increment(ref _nextCompanyId);

    private static long _nextCompanyId = 77000;

    private static async Task<long> GetAnyVehicleIdAsync(Database db)
        => await db.ScalarLongAsync("SELECT id FROM vehicles ORDER BY id LIMIT 1");

    private static async Task<long> GetAnyDriverIdAsync(Database db)
        => await db.ScalarLongAsync("SELECT id FROM drivers ORDER BY id LIMIT 1");

    private static async Task CleanupTenantAsync(Database db, long companyId)
    {
        await db.ExecuteAsync("DELETE FROM telemetry_alerts WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM telemetry_live_asset_states WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM latest_vehicle_positions WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM telemetry_rules WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM eld_devices WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM fleet_health_snapshots WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM safety_events WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM maintenance_items WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM dvir_defects WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM dvir_reports WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM incidents WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM evidence_package_items WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM evidence_packages WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));

        await db.ExecuteAsync("DELETE FROM fleet_tms_last_mile_stops WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM fleet_tms_delivery_routes WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM fleet_tms_dispatch_orders WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM fleet_tms_asset_events WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM fleet_tms_asset_assignments WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM fleet_tms_assets WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM fleet_tms_asset_types WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM fleet_tms_readiness_documents WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM fleet_tms_temperature_alerts WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM fleet_tms_temperature_readings WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM fleet_tms_temperature_devices WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM fleet_tms_temperature_zones WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM fleet_tms_pods WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM fleet_tms_tracking_links WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM fleet_tms_shipment_stops WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM fleet_tms_shipment_events WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM fleet_tms_shipments WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM fleet_tms_vehicles WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));

        await db.ExecuteAsync("DELETE FROM site_access_requirements WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM access_documents WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM pickup_authorizations WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM warehouse_handovers WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM proof_artifacts WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM proof_packages WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM billing_confidence_records WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM smart_assignment_recommendations WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
        await db.ExecuteAsync("DELETE FROM assignment_confirmations WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId));
    }

    private sealed record SeededSimulationContext(
        long CompanyId,
        long JobId,
        long TripId,
        string ShipmentNumber,
        string RouteCode,
        string OrderNumber,
        long VehicleId,
        long DriverId,
        long TelemetryDeviceId);
}
