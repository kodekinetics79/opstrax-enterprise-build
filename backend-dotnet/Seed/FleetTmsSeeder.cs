using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Opstrax.Api.Data;

namespace Opstrax.Api.Seed;

// Fleet TMS (PR1) demo seeder — ports the intent of Zayra's FleetTmsSeeder onto the
// repo's raw-Npgsql layer. Idempotent and tenant-scoped: for every company that has
// zero fleet_tms_shipments it inserts a small representative dataset so the workspace
// renders real rows instead of an empty state. Never touches existing tables.
//
// PRODUCTION SAFETY: demo/test fleet data must never be created automatically in a
// production tenant. Seeding is gated behind ENABLE_FLEET_DEMO_SEED. Resolution order:
//   1. env ENABLE_FLEET_DEMO_SEED (true/false)        → honoured verbatim
//   2. config "Fleet:EnableDemoSeed"                  → honoured verbatim
//   3. config "ENABLE_FLEET_DEMO_SEED"                → honoured verbatim
//   4. nothing configured at all                      → true only in Development,
//                                                       false everywhere else
// appsettings.json is gitignored (env-only config), so the binding guarantee lives
// HERE in code: a fresh Production/Staging deploy with no flag set never auto-seeds.
// Demo shipments are created only when an operator explicitly opts in with
// ENABLE_FLEET_DEMO_SEED=true (dev/demo box).
// The Saudi region reference table is neutral lookup data (not demo shipments) and is
// always ensured so the readiness UI has its lookups regardless of the gate.
public sealed class FleetTmsSeeder(Database db, ILogger<FleetTmsSeeder> log, IConfiguration configuration, IHostEnvironment environment)
{
    // Exposed for tests / startup logging — true when demo shipment seeding is permitted.
    public bool DemoSeedEnabled => ResolveDemoSeedEnabled();

    private bool ResolveDemoSeedEnabled()
    {
        var raw = Environment.GetEnvironmentVariable("ENABLE_FLEET_DEMO_SEED")
                  ?? configuration["Fleet:EnableDemoSeed"]
                  ?? configuration["ENABLE_FLEET_DEMO_SEED"];
        if (!string.IsNullOrWhiteSpace(raw) && bool.TryParse(raw.Trim(), out var explicitValue))
            return explicitValue;
        // Unset → enabled only in Development; never auto-seed Production/Staging.
        return environment.IsDevelopment();
    }

    public async Task EnsureAsync(CancellationToken ct = default)
    {
        // Neutral reference lookup — always present, not demo content.
        await SeedSaudiRegions(ct);

        if (!ResolveDemoSeedEnabled())
        {
            log.LogInformation(
                "[FleetTmsSeeder] demo seed skipped (environment={Environment}, ENABLE_FLEET_DEMO_SEED unset/false). No demo shipments created.",
                environment.EnvironmentName);
            return;
        }

        await SeedDemoCompaniesAsync(ct);
    }

    private async Task SeedDemoCompaniesAsync(CancellationToken ct)
    {
        var companies = await db.QueryAsync("SELECT id, name FROM companies WHERE status='Active' ORDER BY id", ct: ct);
        foreach (var company in companies)
        {
            var companyId = Convert.ToInt64(company["id"]);
            try
            {
                var existing = await db.ScalarLongAsync(
                    "SELECT COUNT(*) FROM fleet_tms_shipments WHERE company_id=@companyId",
                    c => c.Parameters.AddWithValue("@companyId", companyId), ct);
                if (existing == 0) await SeedCompany(companyId, ct);

                // PR2 cold chain + assets + readiness — independently guarded so they seed
                // whether or not PR1 already populated shipments for this company.
                await SeedColdChainAndAssets(companyId, ct);

                // PR3 last-mile logistics — independently guarded.
                await SeedLogistics(companyId, ct);
            }
            catch (Exception ex) { log.LogWarning(ex, "[FleetTmsSeeder] seed failed for company {CompanyId}", companyId); }
        }
    }

    private async Task SeedSaudiRegions(CancellationToken ct)
    {
        if (await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_saudi_regions", ct: ct) > 0) return;
        var regions = new (string Code, string En, string Ar, int Sort, string Cities)[]
        {
            ("RD", "Riyadh", "الرياض", 1, "[\"Riyadh\",\"Al Kharj\",\"Al Majma'ah\"]"),
            ("MK", "Makkah", "مكة المكرمة", 2, "[\"Jeddah\",\"Mecca\",\"Taif\"]"),
            ("EP", "Eastern Province", "المنطقة الشرقية", 3, "[\"Dammam\",\"Khobar\",\"Jubail\",\"Hofuf\"]"),
            ("MD", "Madinah", "المدينة المنورة", 4, "[\"Medina\",\"Yanbu\"]"),
            ("QS", "Qassim", "القصيم", 5, "[\"Buraidah\",\"Unaizah\"]"),
            ("AS", "Asir", "عسير", 6, "[\"Abha\",\"Khamis Mushait\"]"),
        };
        foreach (var r in regions)
        {
            await db.ExecuteAsync(@"
INSERT INTO fleet_tms_saudi_regions (code, name_en, name_ar, country_code, cities_json, sort_order, is_gcc_ready)
VALUES (@code, @en, @ar, 'SA', @cities::jsonb, @sort, true)",
                c => { c.Parameters.AddWithValue("@code", r.Code); c.Parameters.AddWithValue("@en", r.En); c.Parameters.AddWithValue("@ar", r.Ar); c.Parameters.AddWithValue("@cities", r.Cities); c.Parameters.AddWithValue("@sort", r.Sort); }, ct);
        }
        log.LogInformation("[FleetTmsSeeder] seeded Saudi region reference data");
    }

    private async Task SeedColdChainAndAssets(long companyId, CancellationToken ct)
    {
        var rng = new Random((int)(companyId * 104729));
        var shipments = await db.QueryAsync(
            "SELECT id, shipment_number FROM fleet_tms_shipments WHERE company_id=@companyId ORDER BY id LIMIT 5",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct);

        // ── Cold chain ──
        if (await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_temperature_devices WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId), ct) == 0)
        {
            var zones = new (string Code, string Name, decimal Min, decimal Max, string Color)[]
            {
                ("CHILL", "Chilled (2-8C)", 2m, 8m, "#22d3ee"),
                ("FROZEN", "Frozen (-18C)", -22m, -16m, "#3b82f6"),
                ("AMBIENT", "Ambient (15-25C)", 15m, 25m, "#f59e0b"),
            };
            var zoneIds = new List<long>();
            foreach (var z in zones)
            {
                var zid = await db.InsertAsync(@"
INSERT INTO fleet_tms_temperature_zones (company_id, code, name, min_celsius, max_celsius, color, is_active, notes, created_at_utc, updated_at_utc)
VALUES (@companyId, @code, @name, @min, @max, @color, true, '', NOW(), NOW())",
                    c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@code", z.Code); c.Parameters.AddWithValue("@name", z.Name); c.Parameters.AddWithValue("@min", z.Min); c.Parameters.AddWithValue("@max", z.Max); c.Parameters.AddWithValue("@color", z.Color); }, ct);
                zoneIds.Add(zid);
            }

            for (var i = 0; i < 4; i++)
            {
                var chilledZone = zoneIds[0];
                long? shipId = shipments.Count > i ? Convert.ToInt64(shipments[i]["id"]) : null;
                var temp = i == 1 ? 11.5m : 4m + (decimal)rng.NextDouble() * 3; // device 1 is breaching
                var deviceId = await db.InsertAsync(@"
INSERT INTO fleet_tms_temperature_devices (company_id, device_code, name, zone_id, shipment_id, vehicle_number, status, last_reported_temperature_celsius, battery_percent, last_ping_at_utc, notes, created_at_utc, updated_at_utc)
VALUES (@companyId, @code, @name, @zone, @ship, @vehicle, 'Active', @temp, @battery, NOW() - (@i || ' minutes')::interval, '', NOW(), NOW())",
                    c =>
                    {
                        c.Parameters.AddWithValue("@companyId", companyId);
                        c.Parameters.AddWithValue("@code", $"TMP-{companyId:D2}{i + 1}");
                        c.Parameters.AddWithValue("@name", $"Reefer Sensor {i + 1}");
                        c.Parameters.AddWithValue("@zone", chilledZone);
                        c.Parameters.AddWithValue("@ship", (object?)shipId ?? DBNull.Value);
                        c.Parameters.AddWithValue("@vehicle", $"REF-{400 + i}");
                        c.Parameters.AddWithValue("@temp", Math.Round(temp, 1));
                        c.Parameters.AddWithValue("@battery", (decimal)rng.Next(60, 100));
                        c.Parameters.AddWithValue("@i", i);
                    }, ct);

                for (var r = 0; r < 4; r++)
                {
                    var rtemp = i == 1 && r == 0 ? 11.5m : 3m + (decimal)rng.NextDouble() * 4;
                    var breach = rtemp < 2m || rtemp > 8m;
                    var readingId = await db.InsertAsync(@"
INSERT INTO fleet_tms_temperature_readings (company_id, device_id, shipment_id, zone_id, temperature_celsius, humidity_percent, source, status, notes, recorded_at_utc, created_at_utc)
VALUES (@companyId, @device, @ship, @zone, @temp, @humidity, 'Sensor', @status, '', NOW() - (@mins || ' minutes')::interval, NOW())",
                        c =>
                        {
                            c.Parameters.AddWithValue("@companyId", companyId);
                            c.Parameters.AddWithValue("@device", deviceId);
                            c.Parameters.AddWithValue("@ship", (object?)shipId ?? DBNull.Value);
                            c.Parameters.AddWithValue("@zone", chilledZone);
                            c.Parameters.AddWithValue("@temp", Math.Round(rtemp, 1));
                            c.Parameters.AddWithValue("@humidity", (decimal)rng.Next(40, 80));
                            c.Parameters.AddWithValue("@status", breach ? "Breach" : "Normal");
                            c.Parameters.AddWithValue("@mins", r * 30);
                        }, ct);
                    if (breach)
                    {
                        await db.ExecuteAsync(@"
INSERT INTO fleet_tms_temperature_alerts (company_id, device_id, shipment_id, reading_id, alert_type, severity, status, threshold_min, threshold_max, measured_temperature, triggered_at_utc, notes)
VALUES (@companyId, @device, @ship, @reading, 'TemperatureBreach', 'High', 'Open', 2, 8, @temp, NOW(), 'Seeded breach alert.')",
                            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@device", deviceId); c.Parameters.AddWithValue("@ship", (object?)shipId ?? DBNull.Value); c.Parameters.AddWithValue("@reading", readingId); c.Parameters.AddWithValue("@temp", Math.Round(rtemp, 1)); }, ct);
                    }
                }
            }

            await db.ExecuteAsync(@"
INSERT INTO fleet_tms_refrigeration_unit_health (company_id, vehicle_number, unit_serial, status, compressor_hours, last_service_at_utc, next_service_due_at_utc, temperature_deviation_count, notes, created_at_utc)
VALUES (@companyId, 'REF-412', 'CARR-998812', 'NeedsAttention', 8421, NOW() - INTERVAL '40 days', NOW() + INTERVAL '5 days', 3, 'Compressor hours approaching service interval.', NOW())",
                c => c.Parameters.AddWithValue("@companyId", companyId), ct);
        }

        // ── Assets ──
        if (await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_asset_types WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId), ct) == 0)
        {
            var types = new (string Code, string Name)[] { ("PALLET", "Euro Pallet"), ("ROLLCAGE", "Roll Cage"), ("CRATE", "Cold Crate") };
            var typeIds = new List<long>();
            foreach (var t in types)
            {
                var tid = await db.InsertAsync(@"
INSERT INTO fleet_tms_asset_types (company_id, code, name, description, is_returnable, created_at_utc, updated_at_utc)
VALUES (@companyId, @code, @name, @desc, true, NOW(), NOW())",
                    c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@code", t.Code); c.Parameters.AddWithValue("@name", t.Name); c.Parameters.AddWithValue("@desc", $"Returnable {t.Name.ToLower()}"); }, ct);
                typeIds.Add(tid);
            }
            for (var i = 0; i < 6; i++)
            {
                var typeId = typeIds[i % typeIds.Count];
                await db.ExecuteAsync(@"
INSERT INTO fleet_tms_assets (company_id, asset_type_id, asset_tag, name, status, current_location, condition, is_returnable, quantity, unit_of_measure, notes, last_seen_at_utc, created_at_utc, updated_at_utc)
VALUES (@companyId, @type, @tag, @name, @status, @loc, 'Good', true, @qty, 'Each', '', NOW() - (@i || ' hours')::interval, NOW(), NOW())",
                    c =>
                    {
                        c.Parameters.AddWithValue("@companyId", companyId);
                        c.Parameters.AddWithValue("@type", typeId);
                        c.Parameters.AddWithValue("@tag", $"AST-{companyId:D2}{100 + i}");
                        c.Parameters.AddWithValue("@name", $"Asset {100 + i}");
                        c.Parameters.AddWithValue("@status", i % 3 == 0 ? "InUse" : "Available");
                        c.Parameters.AddWithValue("@loc", i % 2 == 0 ? "Riyadh DC" : "Jeddah DC");
                        c.Parameters.AddWithValue("@qty", rng.Next(1, 12));
                        c.Parameters.AddWithValue("@i", i + 1);
                    }, ct);
            }
        }

        // ── Readiness documents ──
        if (await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_readiness_documents WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId), ct) == 0)
        {
            var docs = new (string Kind, string SubjType, string SubjName, string DocType, string DocNo, int ExpiryDays, string Status)[]
            {
                ("Compliance", "Carrier", "In-house Fleet", "Commercial Registration", "CR-1010234567", 220, "Active"),
                ("Transport", "Vehicle", "REF-412", "Transport Operating Card", "TOC-55821", 12, "Active"),
                ("Driver", "Driver", "Abdullah Al-Harbi", "Driver Iqama", "IQ-2384756", -5, "Active"),
                ("Compliance", "Company", "OpsTrax KSA", "VAT Certificate", "VAT-300012345600003", 95, "Active"),
            };
            foreach (var d in docs)
            {
                var status = d.ExpiryDays < 0 ? "Expired" : d.ExpiryDays <= 30 ? "ExpiringSoon" : "Healthy";
                await db.ExecuteAsync(@"
INSERT INTO fleet_tms_readiness_documents
 (company_id, kind, subject_type, subject_id, subject_name, document_type, document_number, country_code,
  document_status, expiry_status, issue_date, gregorian_expiry_date, notes, created_at_utc, updated_at_utc)
VALUES (@companyId, @kind, @subjType, '', @subjName, @docType, @docNo, 'SA', 'Active', @expiryStatus,
  (NOW() - INTERVAL '300 days')::date, (NOW() + (@days || ' days')::interval)::date, '', NOW(), NOW())",
                    c =>
                    {
                        c.Parameters.AddWithValue("@companyId", companyId);
                        c.Parameters.AddWithValue("@kind", d.Kind);
                        c.Parameters.AddWithValue("@subjType", d.SubjType);
                        c.Parameters.AddWithValue("@subjName", d.SubjName);
                        c.Parameters.AddWithValue("@docType", d.DocType);
                        c.Parameters.AddWithValue("@docNo", d.DocNo);
                        c.Parameters.AddWithValue("@expiryStatus", status);
                        c.Parameters.AddWithValue("@days", d.ExpiryDays);
                    }, ct);
            }
        }

        log.LogInformation("[FleetTmsSeeder] seeded cold chain + assets + readiness for company {CompanyId}", companyId);
    }

    private async Task SeedCompany(long companyId, CancellationToken ct)
    {
        var rng = new Random((int)(companyId * 7919));
        string[] customers = { "Najd Foods", "Tamimi Markets", "SACO Hardware", "AlSafi Danone", "Mahmoud Saeed Group" };
        string[] cities = { "Riyadh", "Jeddah", "Dammam", "Mecca", "Medina" };
        string[] regions = { "Riyadh", "Makkah", "Eastern Province", "Madinah", "Qassim" };
        string[] statuses = { "Booked", "Loaded", "InTransit", "InTransit", "Delivered" };
        string[] priorities = { "Normal", "High", "Critical", "Normal", "High" };
        string[] drivers = { "Abdullah Al-Harbi", "Saud Al-Qahtani", "Faisal Al-Otaibi", "Khalid Al-Dosari" };
        string[] vehicleNumbers = { "TRK-101", "TRK-204", "VAN-309", "REF-412" };

        // Vehicles
        foreach (var (vn, idx) in vehicleNumbers.Select((v, i) => (v, i)))
        {
            await db.ExecuteAsync(@"
INSERT INTO fleet_tms_vehicles (company_id, vehicle_number, plate_number, type, status, driver_name,
    capacity_kg, capacity_cbm, current_load_kg, fuel_level_percent, odometer_km, health_status,
    is_refrigerated, temperature_celsius, last_known_location, last_ping_at_utc, last_service_at_utc, next_service_at_utc, notes)
VALUES (@companyId, @vn, @plate, @type, @status, @driver, @capKg, @capCbm, @loadKg, @fuel, @odo, @health,
    @refrigerated, @temp, @loc, NOW() - (@idx || ' hours')::interval, NOW() - INTERVAL '20 days', NOW() + INTERVAL '40 days', '')",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@vn", vn);
                    c.Parameters.AddWithValue("@plate", $"{rng.Next(1000, 9999)} ASD");
                    c.Parameters.AddWithValue("@type", vn.StartsWith("REF") ? "Reefer" : vn.StartsWith("VAN") ? "Van" : "Truck");
                    c.Parameters.AddWithValue("@status", idx == 0 ? "OnTrip" : idx == 3 ? "Maintenance" : "Available");
                    c.Parameters.AddWithValue("@driver", drivers[idx % drivers.Length]);
                    c.Parameters.AddWithValue("@capKg", 12000m + idx * 1500);
                    c.Parameters.AddWithValue("@capCbm", 40m + idx * 5);
                    c.Parameters.AddWithValue("@loadKg", rng.Next(0, 8000));
                    c.Parameters.AddWithValue("@fuel", (decimal)rng.Next(35, 100));
                    c.Parameters.AddWithValue("@odo", (decimal)rng.Next(40000, 220000));
                    c.Parameters.AddWithValue("@health", idx == 3 ? "Needs Service" : "Healthy");
                    c.Parameters.AddWithValue("@refrigerated", vn.StartsWith("REF"));
                    c.Parameters.AddWithValue("@temp", vn.StartsWith("REF") ? 4.0m : (object)DBNull.Value);
                    c.Parameters.AddWithValue("@loc", cities[idx % cities.Length]);
                    c.Parameters.AddWithValue("@idx", idx + 1);
                }, ct);
        }

        // Maintenance + fuel
        await db.ExecuteAsync(@"
INSERT INTO fleet_tms_maintenance_tickets (company_id, work_order_number, vehicle_number, type, status, priority, vendor_name, description, estimated_cost, downtime_hours, opened_at_utc, due_at_utc)
VALUES (@companyId, 'WO-2041', 'REF-412', 'Refrigeration', 'Open', 'High', 'CoolTech KSA', 'Reefer unit losing temperature on long hauls.', 3200, 8, NOW() - INTERVAL '2 days', NOW() + INTERVAL '1 day')",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct);
        await db.ExecuteAsync(@"
INSERT INTO fleet_tms_fuel_events (company_id, vehicle_number, fuel_card_number, station_name, city, event_type, anomaly_flag, liters, cost, odometer_km, recorded_at_utc)
VALUES (@companyId, 'TRK-204', 'FC-8841', 'Aldrees Station', 'Riyadh', 'Fuel', true, 410, 738, 118420, NOW() - INTERVAL '6 hours')",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct);

        // Shipments + stops + events + tracking points
        for (var i = 0; i < 5; i++)
        {
            var status = statuses[i];
            var shipmentNumber = $"SHP-{companyId:D2}{1000 + i}";
            var origin = cities[i % cities.Length];
            var destination = cities[(i + 2) % cities.Length];
            var driver = drivers[i % drivers.Length];
            var vehicle = vehicleNumbers[i % vehicleNumbers.Length];
            var delivered = status == "Delivered";

            var shipmentId = await db.InsertAsync(@"
INSERT INTO fleet_tms_shipments (company_id, shipment_number, customer_name, customer_segment, origin, destination, city,
    status, priority, mode, piece_count, weight_kg, volume_cbm, declared_value, carrier_name, driver_name, vehicle_number,
    route_code, pod_status, temperature_range, pickup_scheduled_at_utc, picked_up_at_utc, delivered_at_utc, created_at_utc)
VALUES (@companyId, @num, @customer, @segment, @origin, @destination, @city, @status, @priority, 'Road', @pieces,
    @weight, @volume, @value, @carrier, @driver, @vehicle, @route, @podStatus, @tempRange,
    NOW() - (@iOffset || ' days')::interval, @pickedUp, @deliveredAt, NOW() - (@iOffset || ' days')::interval)",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@num", shipmentNumber);
                    c.Parameters.AddWithValue("@customer", customers[i % customers.Length]);
                    c.Parameters.AddWithValue("@segment", i % 2 == 0 ? "Retail" : "Wholesale");
                    c.Parameters.AddWithValue("@origin", origin);
                    c.Parameters.AddWithValue("@destination", destination);
                    c.Parameters.AddWithValue("@city", destination);
                    c.Parameters.AddWithValue("@status", status);
                    c.Parameters.AddWithValue("@priority", priorities[i]);
                    c.Parameters.AddWithValue("@pieces", rng.Next(4, 60));
                    c.Parameters.AddWithValue("@weight", (decimal)rng.Next(500, 9000));
                    c.Parameters.AddWithValue("@volume", (decimal)rng.Next(5, 38));
                    c.Parameters.AddWithValue("@value", (decimal)rng.Next(5000, 90000));
                    c.Parameters.AddWithValue("@carrier", "In-house Fleet");
                    c.Parameters.AddWithValue("@driver", status == "Booked" ? "" : driver);
                    c.Parameters.AddWithValue("@vehicle", status == "Booked" ? "" : vehicle);
                    c.Parameters.AddWithValue("@route", $"RT-{origin[..3].ToUpper()}-{destination[..3].ToUpper()}");
                    c.Parameters.AddWithValue("@podStatus", delivered ? "Captured" : "Pending");
                    c.Parameters.AddWithValue("@tempRange", vehicle.StartsWith("REF") ? "2C - 8C" : "");
                    c.Parameters.AddWithValue("@iOffset", i + 1);
                    c.Parameters.AddWithValue("@pickedUp", status is "Booked" ? DBNull.Value : DateTime.UtcNow.AddDays(-(i + 1)).AddHours(4));
                    c.Parameters.AddWithValue("@deliveredAt", delivered ? DateTime.UtcNow.AddHours(-6) : (object)DBNull.Value);
                }, ct);

            // Two stops per shipment
            var pickupStopId = await db.InsertAsync(@"
INSERT INTO fleet_tms_shipment_stops (company_id, shipment_id, stop_type, sequence_no, location_name, contact_name, contact_phone, city, region, country, status, planned_arrival_at, actual_arrival_at, completed_at)
VALUES (@companyId, @sid, 'Pickup', 1, @loc, 'Warehouse Lead', '+96650' || @phone, @city, @region, 'Saudi Arabia', @stopStatus, NOW() - INTERVAL '1 day', @arrived, @completed)",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@sid", shipmentId);
                    c.Parameters.AddWithValue("@loc", $"{origin} Distribution Center");
                    c.Parameters.AddWithValue("@phone", rng.Next(1000000, 9999999));
                    c.Parameters.AddWithValue("@city", origin);
                    c.Parameters.AddWithValue("@region", regions[i % regions.Length]);
                    c.Parameters.AddWithValue("@stopStatus", status == "Booked" ? "Planned" : "Completed");
                    c.Parameters.AddWithValue("@arrived", status == "Booked" ? DBNull.Value : DateTime.UtcNow.AddDays(-(i + 1)).AddHours(4));
                    c.Parameters.AddWithValue("@completed", status == "Booked" ? DBNull.Value : DateTime.UtcNow.AddDays(-(i + 1)).AddHours(5));
                }, ct);

            var deliveryStopId = await db.InsertAsync(@"
INSERT INTO fleet_tms_shipment_stops (company_id, shipment_id, stop_type, sequence_no, location_name, contact_name, contact_phone, city, region, country, status, planned_arrival_at, actual_arrival_at, completed_at)
VALUES (@companyId, @sid, 'Delivery', 2, @loc, 'Receiving Manager', '+96655' || @phone, @city, @region, 'Saudi Arabia', @stopStatus, NOW() + INTERVAL '4 hours', @arrived, @completed)",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@sid", shipmentId);
                    c.Parameters.AddWithValue("@loc", $"{destination} Customer Hub");
                    c.Parameters.AddWithValue("@phone", rng.Next(1000000, 9999999));
                    c.Parameters.AddWithValue("@city", destination);
                    c.Parameters.AddWithValue("@region", regions[(i + 2) % regions.Length]);
                    c.Parameters.AddWithValue("@stopStatus", delivered ? "Completed" : status == "Booked" ? "Planned" : "Arrived");
                    c.Parameters.AddWithValue("@arrived", delivered ? DateTime.UtcNow.AddHours(-7) : (object)DBNull.Value);
                    c.Parameters.AddWithValue("@completed", delivered ? DateTime.UtcNow.AddHours(-6) : (object)DBNull.Value);
                }, ct);

            await db.ExecuteAsync(@"
INSERT INTO fleet_tms_shipment_events (company_id, shipment_id, event_type, message, actor_name, visibility, occurred_at_utc)
VALUES (@companyId, @sid, 'ShipmentBooked', @msg, 'system', 'Public', NOW() - (@iOffset || ' days')::interval)",
                c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@sid", shipmentId); c.Parameters.AddWithValue("@msg", $"Shipment {shipmentNumber} booked from {origin} to {destination}."); c.Parameters.AddWithValue("@iOffset", i + 1); }, ct);

            if (status != "Booked")
            {
                await db.ExecuteAsync(@"
INSERT INTO fleet_tms_tracking_points (company_id, shipment_number, vehicle_number, location_label, status, geofence_name, latitude, longitude, speed_kph, recorded_at_utc, estimated_arrival_utc)
VALUES (@companyId, @num, @vehicle, @loc, @status, 'Highway 40', @lat, @lng, @speed, NOW() - INTERVAL '30 minutes', NOW() + INTERVAL '3 hours')",
                    c =>
                    {
                        c.Parameters.AddWithValue("@companyId", companyId);
                        c.Parameters.AddWithValue("@num", shipmentNumber);
                        c.Parameters.AddWithValue("@vehicle", vehicle);
                        c.Parameters.AddWithValue("@loc", $"En route to {destination}");
                        c.Parameters.AddWithValue("@status", delivered ? "Delivered" : "InTransit");
                        c.Parameters.AddWithValue("@lat", 24.5m + (decimal)rng.NextDouble());
                        c.Parameters.AddWithValue("@lng", 46.5m + (decimal)rng.NextDouble());
                        c.Parameters.AddWithValue("@speed", (decimal)rng.Next(0, 110));
                    }, ct);
            }

            // Driver task for the active delivery
            if (status is "InTransit" or "Loaded")
            {
                await db.ExecuteAsync(@"
INSERT INTO fleet_tms_driver_tasks (company_id, shipment_id, stop_id, task_type, title, description, status, driver_name, vehicle_number, due_at_utc)
VALUES (@companyId, @sid, @stopId, 'Delivery', @title, 'Deliver shipment and capture POD.', 'Open', @driver, @vehicle, NOW() + INTERVAL '3 hours')",
                    c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@sid", shipmentId); c.Parameters.AddWithValue("@stopId", deliveryStopId); c.Parameters.AddWithValue("@title", $"Deliver {shipmentNumber}"); c.Parameters.AddWithValue("@driver", driver); c.Parameters.AddWithValue("@vehicle", vehicle); }, ct);
            }

            // Delivered shipment: verified POD + a public tracking link
            if (delivered)
            {
                await db.ExecuteAsync(@"
INSERT INTO fleet_tms_pods (company_id, shipment_id, stop_id, recipient_name, recipient_phone, delivery_condition, status, captured_at, verified_at)
VALUES (@companyId, @sid, @stopId, 'Receiving Manager', '+966551234567', 'Good', 'Verified', NOW() - INTERVAL '6 hours', NOW() - INTERVAL '5 hours')",
                    c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@sid", shipmentId); c.Parameters.AddWithValue("@stopId", deliveryStopId); }, ct);
                await db.ExecuteAsync(@"
INSERT INTO fleet_tms_tracking_links (company_id, shipment_id, token, shared_by)
VALUES (@companyId, @sid, @token, 'system')",
                    c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@sid", shipmentId); c.Parameters.AddWithValue("@token", $"demo-{companyId}-{shipmentId}"); }, ct);
            }
        }

        log.LogInformation("[FleetTmsSeeder] seeded Fleet TMS demo data for company {CompanyId}", companyId);
    }

    private async Task SeedLogistics(long companyId, CancellationToken ct)
    {
        if (await db.ScalarLongAsync("SELECT COUNT(*) FROM fleet_tms_dispatch_orders WHERE company_id=@companyId", c => c.Parameters.AddWithValue("@companyId", companyId), ct) > 0) return;
        var rng = new Random((int)(companyId * 15485863));
        string[] customers = { "Lulu Hypermarket", "Danube", "Panda Retail", "Othaim Markets", "Carrefour KSA" };
        string[] cities = { "Riyadh", "Jeddah", "Dammam", "Mecca", "Medina" };
        string[] areas = { "Al Olaya", "Al Malqa", "An Nahdah", "Al Rawdah", "Al Hamra" };
        string[] drivers = { "Yousef Al-Shehri", "Majed Al-Zahrani", "Tariq Al-Ghamdi", "Nawaf Al-Mutairi" };
        string[] riders = { "Bilal", "Imran", "Hamza", "Salman" };

        var routeCodes = new List<string>();
        for (var r = 0; r < 3; r++)
        {
            var routeCode = $"RT-{companyId:D2}{cities[r][..3].ToUpper()}";
            routeCodes.Add(routeCode);
            var planned = rng.Next(6, 14);
            var completed = rng.Next(0, planned);
            await db.ExecuteAsync(@"
INSERT INTO fleet_tms_delivery_routes (company_id, route_code, hub, territory, driver_name, vehicle_number, status, planned_stops, completed_stops, distance_km, completion_percent, current_stop, next_stop, planned_for_date, departure_time_utc, eta_complete_utc, notes)
VALUES (@companyId, @code, @hub, @territory, @driver, @vehicle, @status, @planned, @completed, @distance, @percent, @current, @next, CURRENT_DATE, NOW() - INTERVAL '2 hours', NOW() + INTERVAL '4 hours', '')",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@code", routeCode);
                    c.Parameters.AddWithValue("@hub", $"{cities[r]} Hub");
                    c.Parameters.AddWithValue("@territory", areas[r % areas.Length]);
                    c.Parameters.AddWithValue("@driver", drivers[r % drivers.Length]);
                    c.Parameters.AddWithValue("@vehicle", $"VAN-{300 + r}");
                    c.Parameters.AddWithValue("@status", completed == 0 ? "Ready" : completed >= planned ? "Closed" : "Active");
                    c.Parameters.AddWithValue("@planned", planned);
                    c.Parameters.AddWithValue("@completed", completed);
                    c.Parameters.AddWithValue("@distance", (decimal)rng.Next(40, 180));
                    c.Parameters.AddWithValue("@percent", planned == 0 ? 0m : Math.Round(completed / (decimal)planned * 100m, 1));
                    c.Parameters.AddWithValue("@current", $"Stop {completed}");
                    c.Parameters.AddWithValue("@next", $"Stop {Math.Min(planned, completed + 1)}");
                }, ct);
        }

        for (var i = 0; i < 8; i++)
        {
            var routeCode = routeCodes[i % routeCodes.Count];
            var orderNumber = $"ORD-{companyId:D2}{2000 + i}";
            var city = cities[i % cities.Length];
            var statusPool = new[] { "Queued", "Picking", "Dispatched", "InTransit", "Delivered", "Exception" };
            var status = statusPool[i % statusPool.Length];
            var driver = drivers[i % drivers.Length];
            var delivered = status == "Delivered";
            await db.ExecuteAsync(@"
INSERT INTO fleet_tms_dispatch_orders (company_id, order_number, customer_name, customer_segment, sales_channel, city, area, status, priority, item_count, order_value, route_code, driver_name, vehicle_number, dispatch_notes, created_at_utc, promised_at_utc, dispatched_at_utc, delivered_at_utc, updated_at_utc)
VALUES (@companyId, @num, @customer, @segment, @channel, @city, @area, @status, @priority, @items, @value, @route, @driver, @vehicle, '', NOW() - (@i || ' hours')::interval, NOW() + INTERVAL '6 hours', @dispatched, @deliveredAt, NOW())",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@num", orderNumber);
                    c.Parameters.AddWithValue("@customer", customers[i % customers.Length]);
                    c.Parameters.AddWithValue("@segment", i % 2 == 0 ? "Retail" : "Key Account");
                    c.Parameters.AddWithValue("@channel", i % 3 == 0 ? "App" : "Portal");
                    c.Parameters.AddWithValue("@city", city);
                    c.Parameters.AddWithValue("@area", areas[i % areas.Length]);
                    c.Parameters.AddWithValue("@status", status);
                    c.Parameters.AddWithValue("@priority", i % 4 == 0 ? "High" : "Normal");
                    c.Parameters.AddWithValue("@items", rng.Next(1, 24));
                    c.Parameters.AddWithValue("@value", (decimal)rng.Next(150, 5200));
                    c.Parameters.AddWithValue("@route", routeCode);
                    c.Parameters.AddWithValue("@driver", status is "Queued" or "Picking" ? "" : driver);
                    c.Parameters.AddWithValue("@vehicle", status is "Queued" or "Picking" ? "" : $"VAN-{300 + (i % 3)}");
                    c.Parameters.AddWithValue("@i", i + 1);
                    c.Parameters.AddWithValue("@dispatched", status is "Queued" or "Picking" ? DBNull.Value : DateTime.UtcNow.AddHours(-(i + 1)));
                    c.Parameters.AddWithValue("@deliveredAt", delivered ? DateTime.UtcNow.AddHours(-2) : (object)DBNull.Value);
                }, ct);

            var stopStatus = status switch { "Delivered" => "Delivered", "Exception" => "Attempted", _ => "OutForDelivery" };
            await db.ExecuteAsync(@"
INSERT INTO fleet_tms_last_mile_stops (company_id, order_number, route_code, customer_name, address_line, city, region, country, status, proof_status, recipient_name, attempt_count, rider_name, time_window, eta_utc, delivered_at_utc, exception_reason, created_at_utc, updated_at_utc)
VALUES (@companyId, @num, @route, @customer, @addr, @city, @region, 'Saudi Arabia', @status, @proof, @recipient, @attempts, @rider, @window, NOW() + (@etaHrs || ' hours')::interval, @deliveredAt, @exception, NOW(), NOW())",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@num", orderNumber);
                    c.Parameters.AddWithValue("@route", routeCode);
                    c.Parameters.AddWithValue("@customer", customers[i % customers.Length]);
                    c.Parameters.AddWithValue("@addr", $"{rng.Next(1000, 9999)} {areas[i % areas.Length]} St");
                    c.Parameters.AddWithValue("@city", city);
                    c.Parameters.AddWithValue("@region", city);
                    c.Parameters.AddWithValue("@status", stopStatus);
                    c.Parameters.AddWithValue("@proof", delivered ? "POD" : "None");
                    c.Parameters.AddWithValue("@recipient", delivered ? "Store Receiver" : "");
                    c.Parameters.AddWithValue("@attempts", status == "Exception" ? 2 : delivered ? 1 : 0);
                    c.Parameters.AddWithValue("@rider", status is "Queued" or "Picking" ? "" : riders[i % riders.Length]);
                    c.Parameters.AddWithValue("@window", i % 2 == 0 ? "09:00-12:00" : "14:00-18:00");
                    c.Parameters.AddWithValue("@etaHrs", (i % 6) + 1);
                    c.Parameters.AddWithValue("@deliveredAt", delivered ? DateTime.UtcNow.AddHours(-2) : (object)DBNull.Value);
                    c.Parameters.AddWithValue("@exception", status == "Exception" ? "Customer not available at first attempt." : "");
                }, ct);
        }
        log.LogInformation("[FleetTmsSeeder] seeded last-mile logistics for company {CompanyId}", companyId);
    }
}
