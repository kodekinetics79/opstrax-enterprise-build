using Microsoft.Extensions.Logging;
using Npgsql;
using Opstrax.Api.Data;

namespace Opstrax.Api.Seed;

// Fleet TMS (PR1) demo seeder — ports the intent of Zayra's FleetTmsSeeder onto the
// repo's raw-Npgsql layer. Idempotent and tenant-scoped: for every company that has
// zero fleet_tms_shipments it inserts a small representative dataset so the workspace
// renders real rows instead of an empty state. Never touches existing tables.
public sealed class FleetTmsSeeder(Database db, ILogger<FleetTmsSeeder> log)
{
    public async Task EnsureAsync(CancellationToken ct = default)
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
                if (existing > 0) continue;

                await SeedCompany(companyId, ct);
            }
            catch (Exception ex) { log.LogWarning(ex, "[FleetTmsSeeder] seed failed for company {CompanyId}", companyId); }
        }
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
}
