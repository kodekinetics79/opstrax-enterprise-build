-- ─────────────────────────────────────────────────────────────────────────────
-- KHALID-DEMO (company 8) — live-map telemetry: positions + breadcrumb trails
--
-- WHY
--   The tenant had ZERO latest_vehicle_positions and ZERO location_events, so the Live
--   Map was simply empty. Everywhere else in the database the newest fix is 2026-07-11,
--   which is well past the 900s 'stale' threshold, so every vehicle system-wide renders
--   Offline. Fixing the API plumbing does not put water in the pipe; this does.
--
-- HONESTY — read this before assuming it is a shortcut
--   Every row is stamped source='seed'. The frontend's provenance classifier maps
--   legacy|seed -> "seeded" (frontend/src/utils/telemetryProvenance.ts:27-43) and rings
--   the marker with a SEED badge. So these dots are visibly, deliberately labelled as
--   seeded data — they cannot be mistaken for device fixes, and they will not be
--   confused with the PT40 when it starts reporting as source='gps-tracker'.
--
--   That is the whole point: a demo may show seeded data honestly; it must never show
--   seeded data dressed as live hardware.
--
-- SEED *AND* REFRESH — run it again right before you demo
--   Freshness is computed from received_at: <=120s 'live', <=900s 'delayed', else
--   'stale'. A static seed therefore decays to Offline about 15 minutes after you run
--   it. This script UPSERTs, so re-running it re-stamps every timestamp to NOW() and
--   regenerates the trails. Run it a minute before walking into the demo.
--
--   For genuinely moving dots you need a producer, not a seed — either
--   tools/telematics/live_feed.py or the physical PT40.
--
-- FLEET MIX (deliberate — a board where everything is green reads as fake)
--   8 moving, 4 idle/parked, 2 offline. The offline pair is stamped ~40 minutes old so
--   it ages honestly into the Offline bucket rather than being faked with a flag.
--
-- GEOGRAPHY
--   Greater Toronto Area, consistent with the tenant (CA / CAD / America/Toronto) and
--   with the routes seeded by script 06. Trails are dead-reckoned along a bearing from
--   each anchor, 30 points at 2-minute spacing = the last hour of travel.
--
-- SCOPE
--   company_id = 8 ONLY. The DELETE is scoped to company 8 AND source='seed', so a real
--   PT40 fix (source='gps-tracker') is never removed by a re-run.
--
-- RUN:  psql "$NEON_PG_URI" -f tools/pt40/10-khalid-demo-telemetry-seed.sql
-- ─────────────────────────────────────────────────────────────────────────────

SET lock_timeout = '5s';

-- The fleet, anchored on the GTA lanes from script 06.
-- NB: no ON COMMIT DROP. psql runs each statement in its own implicit transaction, so
-- ON COMMIT DROP destroys the table the instant CREATE commits and every later
-- statement fails with 'relation "_seed_fleet" does not exist'. A plain TEMP table is
-- session-scoped and disappears when psql exits, which is what we want here.
DROP TABLE IF EXISTS _seed_fleet;
CREATE TEMP TABLE _seed_fleet (
  vehicle_code TEXT, lat NUMERIC, lng NUMERIC, bearing NUMERIC,
  speed_kmh NUMERIC, engine TEXT, fuel NUMERIC, odo NUMERIC,
  age_minutes INT,          -- how old the newest fix should be
  tstatus TEXT, risk TEXT
);

INSERT INTO _seed_fleet VALUES
  -- The PT40 pilot asset, mid-route on GTA West between Oakville and Burlington.
  ('KHALID-PILOT-01', 43.4102, -79.7405,  232, 84, 'Moving', 61.5, 148230, 0, 'healthy','low'),
  ('TRK-TST-0001',    43.7942, -79.5199,  318, 72, 'Moving', 48.0,  92140, 0, 'healthy','low'),
  ('TRK-TST-0002',    43.7764, -79.2318,   64, 79, 'Moving', 55.5, 133870, 0, 'healthy','low'),
  ('TRK-TST-0003',    43.6777, -79.6248,  145, 41, 'Moving', 33.0, 176420, 0, 'watch','medium'),
  ('TRK-TST-0004',    43.6426, -79.3871,   88, 27, 'Moving', 72.5,  64310, 0, 'healthy','low'),
  ('TRK-TST-0006',    43.7315, -79.7624,  205, 91, 'Moving', 44.5, 118960, 0, 'healthy','low'),
  ('TRK-TST-0007',    43.8561, -79.3370,  271, 68, 'Moving', 39.0, 155200, 0, 'healthy','low'),
  ('TRK-TST-0008',    43.5890, -79.6441,   12, 76, 'Moving', 66.0,  88740, 0, 'healthy','low'),
  -- Parked but reporting: depot, customer dock, truck stop, yard.
  ('TRK-TST-0005',    43.2609, -79.8214,    0,  0, 'Idle',   88.5, 201350, 0, 'healthy','low'),
  ('TRK-TST-0009',    43.6205, -79.6248,    0,  0, 'Idle',   94.0,  71620, 0, 'healthy','low'),
  ('TRK-TST-0010',    43.4675, -79.6877,    0,  0, 'Idle',   29.5, 162480, 0, 'watch','medium'),
  ('TRK-TST-0011',    43.8384, -79.0868,    0,  0, 'Idle',   57.0, 109930, 0, 'healthy','low'),
  -- Genuinely offline: last heard from ~40 min ago, so freshness ages them out honestly.
  ('TRK-TST-0012',    43.6532, -79.7620,    0,  0, 'Off',    18.0, 187640, 41, 'stale','high'),
  ('TRK-TST-0013',    43.3255, -79.7990,    0,  0, 'Off',    23.5, 143210, 47, 'stale','high');

-- ── Breadcrumb trails ──────────────────────────────────────────────────────
-- Scoped delete: only this tenant's SEED events. A real device fix is source
-- 'gps-tracker'/'native_eld'/'gateway' and is deliberately left untouched.
DELETE FROM location_events WHERE company_id = 8 AND source = 'seed';

INSERT INTO location_events
  (company_id, vehicle_id, lat, lng, speed_mph, heading, engine_status,
   fuel_level, odometer_miles, event_type, event_time, received_at, source)
SELECT 8, v.id,
       -- Dead-reckon backwards from the anchor: point i is (29-i) steps behind.
       ROUND((f.lat - (s.i * (f.speed_kmh / 30.0) * COS(RADIANS(f.bearing)) / 110.574))::numeric, 6),
       ROUND((f.lng - (s.i * (f.speed_kmh / 30.0) * SIN(RADIANS(f.bearing))
              / (111.320 * COS(RADIANS(f.lat)))))::numeric, 6),
       ROUND((f.speed_kmh * 0.621371)::numeric, 1),
       f.bearing::smallint,
       f.engine,
       ROUND((f.fuel + s.i * 0.35)::numeric, 1),
       ROUND((f.odo - s.i * (f.speed_kmh * 0.621371 / 30.0))::numeric, 1),
       'position',
       NOW() - ((f.age_minutes + s.i * 2) || ' minutes')::interval,
       NOW() - ((f.age_minutes + s.i * 2) || ' minutes')::interval,
       'seed'
FROM _seed_fleet f
JOIN vehicles v ON v.company_id = 8 AND v.vehicle_code = f.vehicle_code AND v.deleted_at IS NULL
CROSS JOIN generate_series(0, 29) AS s(i)
WHERE f.speed_kmh > 0;   -- parked units get a position but no trail

-- ── Current positions ──────────────────────────────────────────────────────
INSERT INTO latest_vehicle_positions
  (company_id, vehicle_id, lat, lng, speed_mph, heading, engine_status,
   fuel_level, odometer_miles, event_time, received_at, event_count,
   telemetry_status, risk_level, source)
SELECT 8, v.id, f.lat, f.lng,
       ROUND((f.speed_kmh * 0.621371)::numeric, 1),
       f.bearing::smallint, f.engine, f.fuel, f.odo,
       NOW() - (f.age_minutes || ' minutes')::interval,
       NOW() - (f.age_minutes || ' minutes')::interval,
       30, f.tstatus, f.risk, 'seed'
FROM _seed_fleet f
JOIN vehicles v ON v.company_id = 8 AND v.vehicle_code = f.vehicle_code AND v.deleted_at IS NULL
ON CONFLICT (company_id, vehicle_id) DO UPDATE
  SET lat              = EXCLUDED.lat,
      lng              = EXCLUDED.lng,
      speed_mph        = EXCLUDED.speed_mph,
      heading          = EXCLUDED.heading,
      engine_status    = EXCLUDED.engine_status,
      fuel_level       = EXCLUDED.fuel_level,
      odometer_miles   = EXCLUDED.odometer_miles,
      event_time       = EXCLUDED.event_time,
      received_at      = EXCLUDED.received_at,
      event_count      = latest_vehicle_positions.event_count + 1,
      telemetry_status = EXCLUDED.telemetry_status,
      risk_level       = EXCLUDED.risk_level,
      source           = EXCLUDED.source
  -- Never let a seed overwrite a genuine device fix.
  WHERE latest_vehicle_positions.source = 'seed';

-- ── Verification ───────────────────────────────────────────────────────────
\echo ''
\echo '== KHALID-DEMO telemetry =='
SELECT count(*) AS positions,
       count(*) FILTER (WHERE received_at > NOW() - INTERVAL '120 seconds')  AS live,
       count(*) FILTER (WHERE received_at <= NOW() - INTERVAL '900 seconds') AS offline,
       (SELECT count(*) FROM location_events WHERE company_id = 8)           AS breadcrumbs
FROM latest_vehicle_positions WHERE company_id = 8;

\echo ''
\echo '== fleet board (what the Live Map will show) =='
SELECT v.vehicle_code, p.engine_status, p.speed_mph, p.source,
       ROUND(EXTRACT(EPOCH FROM (NOW() - p.received_at))::numeric) AS age_seconds,
       CASE WHEN EXTRACT(EPOCH FROM (NOW() - p.received_at)) <= 120 THEN 'live'
            WHEN EXTRACT(EPOCH FROM (NOW() - p.received_at)) <= 900 THEN 'delayed'
            ELSE 'stale' END AS freshness
FROM latest_vehicle_positions p
JOIN vehicles v ON v.id = p.vehicle_id
WHERE p.company_id = 8
ORDER BY p.speed_mph DESC, v.vehicle_code;
