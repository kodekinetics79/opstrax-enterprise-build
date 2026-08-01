-- Stage 62 — Last-mile customer-pilot integrity contract.
BEGIN;

ALTER TABLE fleet_tms_dispatch_orders ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;
ALTER TABLE fleet_tms_delivery_routes ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;
ALTER TABLE fleet_tms_delivery_routes ADD COLUMN IF NOT EXISTS last_progress_key VARCHAR(80) NULL;
ALTER TABLE fleet_tms_last_mile_stops ADD COLUMN IF NOT EXISTS branch_id BIGINT NULL;
ALTER TABLE fleet_tms_last_mile_stops ADD COLUMN IF NOT EXISTS last_action_key VARCHAR(80) NULL;
ALTER TABLE fleet_tms_last_mile_stops ADD COLUMN IF NOT EXISTS last_action_type VARCHAR(30) NULL;

-- Never repair ambiguous production duplicates silently: fail deployment with a
-- useful gate so the tenant can reconcile its business records deliberately.
DO $preflight$
DECLARE has_last_mile_duplicates BOOLEAN;
BEGIN
  IF EXISTS (SELECT 1 FROM fleet_tms_dispatch_orders GROUP BY company_id,order_number HAVING COUNT(*)>1) THEN
    RAISE EXCEPTION 'Stage 62 blocked: duplicate fleet_tms dispatch order numbers';
  END IF;
  IF EXISTS (SELECT 1 FROM fleet_tms_delivery_routes GROUP BY company_id,route_code HAVING COUNT(*)>1) THEN
    RAISE EXCEPTION 'Stage 62 blocked: duplicate fleet_tms route codes';
  END IF;
  IF EXISTS (SELECT 1 FROM fleet_tms_last_mile_stops GROUP BY company_id,order_number HAVING COUNT(*)>1) THEN
    RAISE EXCEPTION 'Stage 62 blocked: multiple last-mile stops reference one order';
  END IF;
  IF to_regclass('public.job_charges') IS NOT NULL THEN
    EXECUTE $sql$
      SELECT EXISTS (
        SELECT 1 FROM public.job_charges WHERE charge_code='LASTMILE'
        GROUP BY company_id,job_id,charge_code HAVING COUNT(*)>1
      )
    $sql$ INTO has_last_mile_duplicates;
    IF has_last_mile_duplicates THEN
      RAISE EXCEPTION 'Stage 62 blocked: duplicate canonical LASTMILE job charges';
    END IF;
  END IF;
END
$preflight$;

CREATE UNIQUE INDEX IF NOT EXISTS uq_ftms_dorders_company_number
  ON fleet_tms_dispatch_orders(company_id,order_number);
CREATE UNIQUE INDEX IF NOT EXISTS uq_ftms_droutes_company_code
  ON fleet_tms_delivery_routes(company_id,route_code);
CREATE UNIQUE INDEX IF NOT EXISTS uq_ftms_lmstops_company_order
  ON fleet_tms_last_mile_stops(company_id,order_number);
CREATE UNIQUE INDEX IF NOT EXISTS uq_ftms_route_progress_key
  ON fleet_tms_delivery_routes(company_id,last_progress_key) WHERE last_progress_key IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_ftms_stop_action_key
  ON fleet_tms_last_mile_stops(company_id,last_action_key) WHERE last_action_key IS NOT NULL;
DO $job_charges_index$
BEGIN
  IF to_regclass('public.job_charges') IS NOT NULL THEN
    EXECUTE 'CREATE UNIQUE INDEX IF NOT EXISTS uq_job_charges_last_mile
             ON public.job_charges(company_id,job_id,charge_code) WHERE charge_code=''LASTMILE''';
  END IF;
END
$job_charges_index$;
CREATE UNIQUE INDEX IF NOT EXISTS uq_branches_company_id_id ON branches(company_id,id);

CREATE INDEX IF NOT EXISTS idx_ftms_dorders_branch_status
  ON fleet_tms_dispatch_orders(company_id,branch_id,status);
CREATE INDEX IF NOT EXISTS idx_ftms_droutes_branch_status
  ON fleet_tms_delivery_routes(company_id,branch_id,status);
CREATE INDEX IF NOT EXISTS idx_ftms_lmstops_branch_status
  ON fleet_tms_last_mile_stops(company_id,branch_id,status);

DO $constraints$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_ftms_dorders_status') THEN
    ALTER TABLE fleet_tms_dispatch_orders ADD CONSTRAINT ck_ftms_dorders_status
      CHECK (status IN ('Queued','Dispatched','InTransit','Exception','Delivered','Returned')) NOT VALID;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_ftms_dorders_values') THEN
    ALTER TABLE fleet_tms_dispatch_orders ADD CONSTRAINT ck_ftms_dorders_values
      CHECK (btrim(order_number)<>'' AND item_count>=0 AND order_value>=0) NOT VALID;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_ftms_droutes_status') THEN
    ALTER TABLE fleet_tms_delivery_routes ADD CONSTRAINT ck_ftms_droutes_status
      CHECK (status IN ('Planned','Ready','Active','Delayed','Closed','Completed')) NOT VALID;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_ftms_droutes_values') THEN
    ALTER TABLE fleet_tms_delivery_routes ADD CONSTRAINT ck_ftms_droutes_values
      CHECK (btrim(route_code)<>'' AND planned_stops>=0 AND completed_stops>=0 AND completed_stops<=planned_stops AND distance_km>=0 AND completion_percent BETWEEN 0 AND 100) NOT VALID;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_ftms_lmstops_status') THEN
    ALTER TABLE fleet_tms_last_mile_stops ADD CONSTRAINT ck_ftms_lmstops_status
      CHECK (status IN ('OutForDelivery','Attempted','Failed','Rescheduled','Delivered')) NOT VALID;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_ftms_lmstops_values') THEN
    ALTER TABLE fleet_tms_last_mile_stops ADD CONSTRAINT ck_ftms_lmstops_values
      CHECK (btrim(order_number)<>'' AND btrim(route_code)<>'' AND attempt_count>=0) NOT VALID;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_ftms_dorders_company') THEN
    ALTER TABLE fleet_tms_dispatch_orders ADD CONSTRAINT fk_ftms_dorders_company FOREIGN KEY(company_id) REFERENCES companies(id) NOT VALID;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_ftms_droutes_company') THEN
    ALTER TABLE fleet_tms_delivery_routes ADD CONSTRAINT fk_ftms_droutes_company FOREIGN KEY(company_id) REFERENCES companies(id) NOT VALID;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_ftms_lmstops_company') THEN
    ALTER TABLE fleet_tms_last_mile_stops ADD CONSTRAINT fk_ftms_lmstops_company FOREIGN KEY(company_id) REFERENCES companies(id) NOT VALID;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_ftms_dorders_branch') THEN
    ALTER TABLE fleet_tms_dispatch_orders ADD CONSTRAINT fk_ftms_dorders_branch FOREIGN KEY(company_id,branch_id) REFERENCES branches(company_id,id) NOT VALID;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_ftms_droutes_branch') THEN
    ALTER TABLE fleet_tms_delivery_routes ADD CONSTRAINT fk_ftms_droutes_branch FOREIGN KEY(company_id,branch_id) REFERENCES branches(company_id,id) NOT VALID;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_ftms_lmstops_branch') THEN
    ALTER TABLE fleet_tms_last_mile_stops ADD CONSTRAINT fk_ftms_lmstops_branch FOREIGN KEY(company_id,branch_id) REFERENCES branches(company_id,id) NOT VALID;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_ftms_lmstops_order') THEN
    ALTER TABLE fleet_tms_last_mile_stops ADD CONSTRAINT fk_ftms_lmstops_order FOREIGN KEY(company_id,order_number) REFERENCES fleet_tms_dispatch_orders(company_id,order_number) NOT VALID;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_ftms_lmstops_route') THEN
    ALTER TABLE fleet_tms_last_mile_stops ADD CONSTRAINT fk_ftms_lmstops_route FOREIGN KEY(company_id,route_code) REFERENCES fleet_tms_delivery_routes(company_id,route_code) NOT VALID;
  END IF;
END
$constraints$;

DO $rls$
DECLARE t TEXT;
BEGIN
  FOREACH t IN ARRAY ARRAY['fleet_tms_dispatch_orders','fleet_tms_delivery_routes','fleet_tms_last_mile_stops'] LOOP
    EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY',t);
    EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY',t);
  END LOOP;
END
$rls$;

COMMIT;
