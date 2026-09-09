-- Fleet Utilization commercial-truth contract.
-- Existing rows remain untouched and unverified. Operational queries exclude
-- demo tenants and accept only provenance pairs written by authenticated/runtime writers.

ALTER TABLE public.trips
    ADD COLUMN IF NOT EXISTS data_origin VARCHAR(80) NOT NULL DEFAULT 'legacy_unverified',
    ADD COLUMN IF NOT EXISTS verification_status VARCHAR(80) NOT NULL DEFAULT 'unverified';

ALTER TABLE public.fuel_transactions
    ADD COLUMN IF NOT EXISTS verification_status VARCHAR(80) NOT NULL DEFAULT 'unverified';

ALTER TABLE public.idling_events
    ADD COLUMN IF NOT EXISTS verification_status VARCHAR(80) NOT NULL DEFAULT 'unverified';

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_trip_evidence'
                 AND conrelid='public.trips'::regclass) THEN
    ALTER TABLE public.trips ADD CONSTRAINT ck_trip_evidence CHECK (
      (data_origin='runtime_route_projection' AND verification_status='derived_from_recorded_route')
      OR (data_origin='legacy_unverified' AND verification_status='unverified')
      OR (data_origin='demo_seed' AND verification_status IN ('demo_seed','unverified'))) NOT VALID;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_fuel_transaction_evidence'
                 AND conrelid='public.fuel_transactions'::regclass) THEN
    ALTER TABLE public.fuel_transactions ADD CONSTRAINT ck_fuel_transaction_evidence CHECK (
      (data_origin='manual_entry' AND verification_status='recorded_by_authenticated_actor')
      OR (data_origin='provider_import' AND verification_status='provider_verified')
      OR (COALESCE(data_origin,'legacy_unverified')='legacy_unverified' AND verification_status='unverified')
      OR (data_origin='demo_seed' AND verification_status IN ('demo_seed','unverified'))) NOT VALID;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_idling_event_evidence'
                 AND conrelid='public.idling_events'::regclass) THEN
    ALTER TABLE public.idling_events ADD CONSTRAINT ck_idling_event_evidence CHECK (
      (data_origin='manual_entry' AND verification_status='recorded_by_authenticated_actor')
      OR (data_origin='telematics_event' AND verification_status='derived_from_qualified_telemetry')
      OR (COALESCE(data_origin,'legacy_unverified')='legacy_unverified' AND verification_status='unverified')
      OR (data_origin='demo_seed' AND verification_status IN ('demo_seed','unverified'))) NOT VALID;
  END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_trips_company_utilization_evidence
    ON public.trips(company_id,data_origin,verification_status,vehicle_id,started_at DESC);
CREATE INDEX IF NOT EXISTS idx_fuel_company_utilization_evidence
    ON public.fuel_transactions(company_id,data_origin,verification_status,vehicle_id,fuel_date DESC);
CREATE INDEX IF NOT EXISTS idx_idling_company_utilization_evidence
    ON public.idling_events(company_id,data_origin,verification_status,vehicle_id,started_at DESC);
