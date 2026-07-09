-- Stage 31 — Polygon (arbitrary-shape) geofences
--
-- Geofences were circle-only (center_lat/lng + radius_meters). Real operations need
-- arbitrary shapes — a warehouse yard, a depot lot, a city boundary — which a circle
-- cannot represent. polygon_json holds an array of [lat,lng] vertices; when present the
-- geofence is treated as a polygon (geofence_type='Polygon') and breach detection uses
-- a point-in-polygon (ray-casting) test in the ingest path. Circle geofences (polygon_json
-- NULL) keep the Haversine radius test. Fully back-compatible.
--
-- MUST be applied by the DB OWNER (the app runs as the restricted opstrax_app role and
-- skips startup schema init under RLS). Idempotent; safe to re-run.

ALTER TABLE geofences ADD COLUMN IF NOT EXISTS polygon_json JSONB NULL;
