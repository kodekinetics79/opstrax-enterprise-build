using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed class TripSchemaService(Database db)
{
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        foreach (var col in Columns) await EnsureColumnAsync(col.Table, col.Name, col.Definition, ct);
        foreach (var sql in Tables) await db.ExecuteAsync(sql, ct: ct);
        foreach (var sql in Indexes) { try { await db.ExecuteAsync(sql, ct: ct); } catch { } }
    }

    private async Task EnsureColumnAsync(string table, string column, string definition, CancellationToken ct)
    {
        var exists = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name=@t AND column_name=@c",
            c => { c.Parameters.AddWithValue("@t", table); c.Parameters.AddWithValue("@c", column); }, ct);
        if (exists == 0) await db.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {column} {definition}", ct: ct);
    }

    private sealed record ColumnDefinition(string Table, string Name, string Definition);

    private static readonly ColumnDefinition[] Columns =
    [
        // Bind location_events to trips for breadcrumb replay
        new("location_events", "trip_id", "BIGINT NULL"),
        // Capture odometer at trip start/end for actual_distance
        new("location_events", "trip_sequence", "INT NULL COMMENT 'Ordered position within trip (set by TripBackgroundService)'"),
    ];

    private static readonly string[] Tables =
    [
        // Core trip record — auto-created from active routes, or manually from dispatch.
        // status: planned → active → completed / exception / cancelled
        // route_compliance_score: 0–100, computed from stop completion + timing + telemetry
        @"CREATE TABLE IF NOT EXISTS trips (
            id BIGINT AUTO_INCREMENT PRIMARY KEY,
            company_id BIGINT NOT NULL,
            driver_id BIGINT NULL,
            vehicle_id BIGINT NULL,
            route_id BIGINT NULL,
            job_id BIGINT NULL,
            trip_ref VARCHAR(60) NULL COMMENT 'Unique reference: TRP-{id}',
            status VARCHAR(40) NOT NULL DEFAULT 'planned',
            planned_start_time TIMESTAMP NULL,
            actual_start_time TIMESTAMP NULL,
            planned_end_time TIMESTAMP NULL,
            actual_end_time TIMESTAMP NULL,
            origin VARCHAR(200) NULL,
            destination VARCHAR(200) NULL,
            planned_distance_miles DECIMAL(10,2) NULL,
            actual_distance_miles DECIMAL(10,2) NULL,
            planned_duration_minutes INT NULL,
            actual_duration_minutes INT NULL,
            route_compliance_score DECIMAL(5,2) NULL DEFAULT 100,
            total_planned_stops INT NOT NULL DEFAULT 0,
            stops_completed INT NOT NULL DEFAULT 0,
            stops_on_time INT NOT NULL DEFAULT 0,
            start_delay_minutes INT NOT NULL DEFAULT 0,
            max_telemetry_gap_minutes INT NOT NULL DEFAULT 0,
            speeding_events_count INT NOT NULL DEFAULT 0,
            compliance_breakdown_json JSON NULL,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci",

        // Planned stop tracking for compliance — links to route_stops.
        // stop_arrival_time / stop_departure_time set when telemetry shows vehicle near stop.
        @"CREATE TABLE IF NOT EXISTS trip_stops (
            id BIGINT AUTO_INCREMENT PRIMARY KEY,
            company_id BIGINT NOT NULL,
            trip_id BIGINT NOT NULL,
            route_stop_id BIGINT NULL,
            stop_sequence INT NOT NULL DEFAULT 0,
            stop_type VARCHAR(60) NOT NULL DEFAULT 'Delivery',
            address VARCHAR(200) NULL,
            lat DECIMAL(10,7) NULL,
            lng DECIMAL(10,7) NULL,
            planned_arrival_time TIMESTAMP NULL,
            planned_departure_time TIMESTAMP NULL,
            actual_arrival_time TIMESTAMP NULL,
            actual_departure_time TIMESTAMP NULL,
            time_window_start TIMESTAMP NULL,
            time_window_end TIMESTAMP NULL,
            status VARCHAR(40) NOT NULL DEFAULT 'pending',
            arrival_delay_minutes INT NOT NULL DEFAULT 0,
            deviation_flagged TINYINT(1) NOT NULL DEFAULT 0,
            notes TEXT NULL,
            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at TIMESTAMP NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
        ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci",
    ];

    private static readonly string[] Indexes =
    [
        "CREATE INDEX idx_trips_company_status ON trips(company_id, status)",
        "CREATE INDEX idx_trips_vehicle ON trips(vehicle_id, company_id, actual_start_time)",
        "CREATE INDEX idx_trips_driver ON trips(driver_id, company_id)",
        "CREATE INDEX idx_trips_route ON trips(route_id, company_id)",
        "CREATE INDEX idx_trip_stops_trip ON trip_stops(trip_id, stop_sequence)",
        "CREATE INDEX idx_le_trip ON location_events(trip_id, trip_sequence)",
        "CREATE INDEX idx_le_vehicle_time ON location_events(vehicle_id, event_time)",
    ];
}
