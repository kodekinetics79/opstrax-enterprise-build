-- ============================================================================
-- 003_live_telemetry_seed.sql
-- Real GPS test telemetry for the demo tenant (company_id = 1, OPX-DEMO).
--
-- Why this exists: the Live Fleet Map joins vehicles -> location_events (latest
-- fix per vehicle) and streams latest_vehicle_positions over SSE. Without rows in
-- those tables the map had NO real coordinates, which is why the frontend used to
-- fabricate positions. We removed that fabrication; this seed gives every demo
-- vehicle a genuine, plausible GPS fix so the map shows real, movable data.
--
-- Buckets (so all three live-status states are exercised):
--   Moving   -> recent fix, speed > 0, heading set
--   Idle     -> recent fix, speed 0
--   Offline  -> stale fix (> 15 min old) -> is_stale = true
--
-- Idempotent: clears prior demo telemetry for company 1, then re-inserts.
-- Safe to re-run. Real device ingestion (HMAC) upserts over these rows naturally.
-- ============================================================================

BEGIN;

DELETE FROM location_events          WHERE company_id = 1;
DELETE FROM latest_vehicle_positions WHERE company_id = 1;

-- Resolve one current fix per demo vehicle into a temp table the inserts share.
CREATE TEMP TABLE _seed_plan ON COMMIT DROP AS
WITH anchors(idx, lat, lng) AS (
  VALUES
    -- Dense in the DC/VA metro (a Virginia carrier's home turf) + long-haul corridor.
    ( 1, 38.8951, -77.0364), ( 2, 38.8048, -77.0469), ( 3, 38.7509, -77.4753),
    ( 4, 38.6582, -77.2497), ( 5, 38.8462, -77.3064), ( 6, 38.8816, -77.0910),
    ( 7, 38.9586, -77.3570), ( 8, 38.9517, -77.4481), ( 9, 39.0840, -77.1528),
    (10, 39.0458, -76.6413), (11, 39.2904, -76.6122), (12, 38.9072, -77.0369),
    (13, 37.5407, -77.4360), (14, 36.8508, -76.2859), (15, 39.9526, -75.1652),
    (16, 40.7128, -74.0060), (17, 35.2271, -80.8431), (18, 33.7490, -84.3880),
    (19, 41.8781, -87.6298), (20, 39.7392, -104.9903)
),
veh AS (
  SELECT v.id, v.company_id, v.vehicle_code, v.assigned_driver_id AS driver_id,
         v.odometer_miles,
         ROW_NUMBER() OVER (ORDER BY v.id) AS rn
  FROM vehicles v
  WHERE v.company_id = 1 AND v.deleted_at IS NULL
)
SELECT
  veh.id, veh.company_id, veh.vehicle_code, veh.driver_id, veh.odometer_miles, veh.rn,
  a.lat, a.lng,
  CASE WHEN veh.rn % 5 IN (1, 2, 3) THEN 'moving'
       WHEN veh.rn % 5 = 4          THEN 'idle'
       ELSE 'offline' END                                            AS bucket,
  CASE WHEN veh.rn % 5 IN (1, 2, 3)
       THEN (30 + (veh.rn * 7) % 35)::numeric ELSE 0 END             AS speed_mph,
  CASE WHEN veh.rn % 5 IN (1, 2, 3)
       THEN ((veh.rn * 47) % 360)::numeric ELSE 0 END                AS heading,
  CASE WHEN veh.rn % 5 IN (1, 2, 3) THEN 'On'
       WHEN veh.rn % 5 = 4          THEN 'Idle' ELSE 'Off' END       AS engine_status,
  (30 + (veh.rn * 13) % 65)::numeric                                 AS fuel_level,
  -- Age of the latest fix: moving/idle fresh, offline stale (> 15 min).
  CASE WHEN veh.rn % 5 IN (1, 2, 3) THEN make_interval(secs => ((veh.rn % 4) * 8)::int)
       WHEN veh.rn % 5 = 4          THEN make_interval(secs => (90 + (veh.rn % 3) * 30)::int)
       ELSE                              make_interval(mins => (22 + (veh.rn % 5) * 6)::int) END AS fix_age
FROM veh
JOIN anchors a ON a.idx = ((veh.rn - 1) % 20) + 1;

-- 6-point breadcrumb trail per vehicle. step 0 = current fix; older steps trail
-- behind along the reverse heading. ORDER BY step DESC inserts the current fix last
-- so it owns MAX(id) -- the "latest" row the map and replay select.
INSERT INTO location_events
  (company_id, vehicle_id, driver_id, vehicle_code, lat, lng, speed_mph, heading, event_type, event_time)
SELECT
  p.company_id, p.id, p.driver_id, p.vehicle_code,
  (p.lat - SIN(RADIANS(p.heading)) * 0.011 * step)::numeric(10,7),
  (p.lng - COS(RADIANS(p.heading)) * 0.011 * step)::numeric(10,7),
  CASE WHEN step = 0 THEN p.speed_mph
       WHEN p.bucket = 'moving' THEN GREATEST(0, p.speed_mph - step * 4)
       ELSE 0 END,
  p.heading,
  CASE WHEN p.bucket = 'offline' AND step = 0 THEN 'location.signal_lost'
       WHEN p.bucket = 'idle'    AND step = 0 THEN 'location.idle'
       ELSE 'location.updated' END,
  NOW() - p.fix_age - make_interval(mins => step * 3)
FROM _seed_plan p
CROSS JOIN generate_series(5, 0, -1) AS step
ORDER BY step DESC;

-- Live snapshot consumed by /api/telemetry/positions and the SSE stream.
INSERT INTO latest_vehicle_positions
  (company_id, vehicle_id, driver_id, lat, lng, speed_mph, heading,
   engine_status, fuel_level, odometer_miles, event_time, received_at, event_count)
SELECT
  p.company_id, p.id, p.driver_id, p.lat, p.lng, p.speed_mph, p.heading,
  p.engine_status, p.fuel_level, p.odometer_miles,
  NOW() - p.fix_age, NOW() - p.fix_age, 1
FROM _seed_plan p
ON CONFLICT (company_id, vehicle_id) DO UPDATE SET
  lat = EXCLUDED.lat, lng = EXCLUDED.lng, speed_mph = EXCLUDED.speed_mph,
  heading = EXCLUDED.heading, engine_status = EXCLUDED.engine_status,
  fuel_level = EXCLUDED.fuel_level, odometer_miles = EXCLUDED.odometer_miles,
  event_time = EXCLUDED.event_time, received_at = EXCLUDED.received_at;

-- A few operational geofences so the Geofences layer has real zones to draw.
INSERT INTO geofences (company_id, name, geofence_type, center_lat, center_lng, radius_meters, status)
SELECT 1, z.name, 'Circle', z.lat, z.lng, z.radius, 'Active'
FROM (VALUES
  ('DC Distribution Hub',  38.9072, -77.0369, 6000),
  ('Manassas Yard',        38.7509, -77.4753, 4000),
  ('Dulles Cross-Dock',    38.9517, -77.4481, 5000),
  ('Baltimore Port Zone',  39.2904, -76.6122, 7000)
) AS z(name, lat, lng, radius)
WHERE NOT EXISTS (
  SELECT 1 FROM geofences g WHERE g.company_id = 1 AND g.name = z.name
);

COMMIT;
