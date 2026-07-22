-- Stage 44 — preventive-maintenance service baseline (engine hours).
-- The PM evaluator arms the next interval from the odometer/engine-hours recorded on the last COMPLETED
-- maintenance_item for the service_type. maintenance_items.odometer_miles already existed but was never
-- written on completion, and there was no engine-hours column at all — so baselines stayed ~0 and every
-- vehicle past its first interval was flagged overdue forever (audit P1, both mileage and engine-hours PM).
-- The completion handlers now stamp the vehicle's odo + hours onto the closed item; this adds the column.
ALTER TABLE maintenance_items ADD COLUMN IF NOT EXISTS engine_hours DECIMAL(12,2) NULL;
