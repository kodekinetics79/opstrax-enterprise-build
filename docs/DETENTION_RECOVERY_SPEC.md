# Detention Recovery — FINAL Engineering Spec (consultant-signed, business-mandated)

_Samsara-veteran SME design -> 3 independent consultant inspections (2 blockers + 2 majors found and resolved) -> revision incorporating the director panel's binding mandates (docs/DETENTION_RECOVERY_DIRECTIVES.md). This spec governs the build._


## item

GPS-Proven Detention & Accessorial Auto-Billing v2 (Provable Money Layer wedge) — FINAL, consultant blockers resolved + business panel mandates incorporated

## summary

Turn the debounced geofence Entry/Exit stream into audit-ready, dispute-winning detention revenue: a consumed-event ledger pairs Entry/Exit into detention_dwells with zero double-charge paths (fixes both consultant blockers); the billable clock starts at LATER-OF(appointment, arrival) — never bare geofence entry — with the applied rule frozen in evidence; a 75%-of-free-time 'meter running' notice to dispatch and the customer's designated contact is logged and welded into the evidence packet; per-customer Rule Cards (free time, rate, cap, round-DOWN increment, claim window) price billable minutes fail-closed; a settle delay makes bounce-merge actually work; every charge auto-DRAFTS with evidence into an approval queue (review/edit/waive/approve — no auto-send); approval materializes a source='detention' job_charge that rides the existing consolidation → invoice → GL pipeline, with a new supplemental-draft path for charges approved after the job's invoice issued (fixes the stranded-revenue major). Evidence is a no-login shareable page per charge — map, breadcrumbs with disclosed ping cadence, bounded entry/exit interval billed from its lower bound, appointment vs actual, the exact rule applied, the notice log, and the shipper's own PO/BOL/rate-con references — marketed as 'audit-ready evidence' with hashing kept internal. Flagship dashboard: the detected → notified → billed → collected recovered-revenue funnel. Consciously deferred (unresolved risks): Samsara/Motive connectors as a separate follow-on workstream (GPS cadence until then is sparse and disclosed in the packet); notice DELIVERY stubbed at 'logged' in v1 (legal weight is strongest when actually delivered — email handler is the first fast-follow); post-timeout dwell-tail undercharge; polygon fences; daily hash-chain roll-up; sub-30s realtime, dashcam, ELD, OCR rate-con, auto-send all explicitly out (mandate 8).

## currentState

Detection substrate exists but stops at raw events; billing substrate exists but has no accessorial producer; notice and public-link substrates exist but are unused for detention. (1) GeofenceEvaluator emits debounced Entry/Exit into geofence_events from latest_vehicle_positions — backend-dotnet/Services/GeofenceEvaluator.cs:15-59 (debounce/state :30-36, company filter :48, EmitAsync with server-side NOW() event_time :61-71); runs each 5-min safety tick under system RLS scope — backend-dotnet/Services/SafetyBackgroundService.cs:21, :66-73 (call at :69), error containment :78-84. Debounce alternation holds per instance only — overlapping blue-green deploys can emit consecutive duplicate Entries (geofence_events has no uniqueness), which v2's consumed-event ledger absorbs by design. (2) geofences have no customer linkage — database/init/001_schema.sql:339-348; geofence_events :350-357; location_events breadcrumbs incl. speed_mph :323-337. (3) RatingService: accessorial comment at RatingService.cs:21 is stale (per_stop/per_hour now implemented :58-73 — ADR cites correctly) but the load-bearing fact holds: re-rating deletes only source='rating' (:114-124), so source='detention' is safe; detention/accessorial charges have no producer anywhere. (4) job_charges DDL: BusinessSpineSchemaService.cs:76-97 (job_id NOT NULL :79 — the schema-level fail-closed anchor; approved_by_user_id :93), source :112; billing_status via BillingProfileSchemaService.cs:95. (5) BillingConsolidationService: charge selection requires the job's delivered date INSIDE the run period (BillingConsolidationService.cs:82-85) and issued groups are locked (CommitGroupAsync returns null when prior draft != 'draft', :178-180) — so late-approved charges DO NOT 'ride the next cycle'; v2 adds an explicit supplemental path. Claim-flip :257-261; invoice_draft_lines carry job_charge_id :244-254. (6) Outbox fan-out + partial-unique idempotency: EndpointMappings.cs:15456-15465, ux_outbox_job_delivered at FoundationSchemaService.cs:274, multi-handler registry Program.cs:248-277. (7) GL: InvoiceIssuedGeneralLedgerHandler.cs:14-25 → GeneralLedgerService.PostEntryAsync balanced + UNIQUE(company_id, source_type, source_ref) early-return (GeneralLedgerService.cs:50-75). (8) Appointment substrate EXISTS: dispatch_assignments.planned_pickup_at/planned_delivery_at + actual_* — backend-dotnet/Services/DispatchSchemaService.cs:23-28. (9) jobs have NO reference columns (verified full DDL, database/init/001_schema.sql:252-273) — PO/BOL/rate-con must be added. (10) customers carry a designated contact: contact_name/email/phone — 001_schema.sql:99-101. (11) Notification substrate EXISTS: notifications table with event_type/audience_type/channel/dedupe_key/delivered_at — backend-dotnet/Services/NotificationSchemaService.cs:37-60, dedupe index :125. (12) No-login public-link precedent EXISTS: /api/public/shipments/track/{token} (backend-dotnet/Controllers/FleetTmsEndpoints.cs:59-67), server-generated 256-bit tokens into fleet_tms_tracking_links (:499-505, 'never accept a client-supplied token'), expiring/revocable token pattern also in CustomerVisibilitySchemaService.cs:21-37 — the evidence share page reuses this exact pattern. (13) Collected-stage substrate EXISTS: invoice_payments (FinanceActivationSchemaService.cs:84-110) with a total_collected aggregation precedent (RevenueReadinessService.cs:1805-1807). (14) Evidence hashing: TelemetryHmacHelper.Sha256Hex (backend-dotnet/TelemetryHmacHelper.cs:19-24). (15) RLS auto-enrollment for company_id BIGINT tables at end of boot (RlsReconciliationSchemaService, wired Program.cs:496); CAVEAT: under a restricted runtime DB role the whole schema-init chain is SKIPPED (Program.cs:499-503 'apply out-of-band by the DB owner') — v2 specifies the out-of-band runbook + a boot health gate. Trusted-gateway GPS ingest writes device-supplied @eventTime into location_events (EndpointMappings.cs ~12621-12627) — v2 clamps refined times and records the clock used. Nothing anywhere computes dwell, detention, appointment-relative clocks, or accessorial charges today.

## dataModel

New DetentionSchemaService.cs (idempotent CREATE/ALTER pattern), registered in the boot chain BEFORE RlsReconciliation (Program.cs:496). All tables carry company_id BIGINT for RLS auto-enrollment.

-- 1) Geofence → billable party
ALTER TABLE geofences ADD COLUMN IF NOT EXISTS customer_id BIGINT NULL;
ALTER TABLE geofences ADD COLUMN IF NOT EXISTS site_role VARCHAR(30) NULL;  -- 'customer_site' opts in
CREATE INDEX IF NOT EXISTS idx_geofences_company_customer ON geofences (company_id, customer_id) WHERE customer_id IS NOT NULL;

-- 2) Shipper references on the load (jobs has none today — verified 001_schema.sql:252-273)
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS po_number VARCHAR(80) NULL;
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS bol_number VARCHAR(80) NULL;
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS rate_con_number VARCHAR(80) NULL;
ALTER TABLE jobs ADD COLUMN IF NOT EXISTS appointment_ref VARCHAR(80) NULL;

-- 3) RULE CARDS (mandate 3; fail-closed: no row => detect+show, price NOTHING)
CREATE TABLE IF NOT EXISTS detention_rule_cards (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  scope_type VARCHAR(20) NOT NULL DEFAULT 'customer',   -- 'tenant' | 'customer'
  scope_id BIGINT NULL,
  free_minutes INT NOT NULL DEFAULT 120,
  rate_per_hour DECIMAL(12,2) NOT NULL,
  currency VARCHAR(10) NOT NULL DEFAULT 'USD',
  billing_increment_minutes INT NOT NULL DEFAULT 15,    -- billable minutes rounded DOWN (mandate 3)
  max_charge_amount DECIMAL(12,2) NULL,
  claim_window_days INT NOT NULL DEFAULT 30,            -- mandate 6
  notice_percent INT NOT NULL DEFAULT 75,               -- pre-expiry notice trigger (mandate 2)
  grace_minutes INT NOT NULL DEFAULT 0,                 -- late-arrival grace vs appointment
  late_arrival_policy VARCHAR(20) NOT NULL DEFAULT 'flag',  -- 'flag' (review state) | 'allow'
  max_dwell_hours INT NOT NULL DEFAULT 24,
  merge_gap_minutes INT NOT NULL DEFAULT 10,
  applies_to VARCHAR(20) NOT NULL DEFAULT 'any',        -- 'pickup'|'delivery'|'any'
  effective_date DATE NOT NULL DEFAULT CURRENT_DATE, expiry_date DATE NULL,
  version INT NOT NULL DEFAULT 1, status VARCHAR(20) NOT NULL DEFAULT 'active',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(), updated_at TIMESTAMPTZ NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_detention_rule_cards_scope ON detention_rule_cards (company_id, scope_type, COALESCE(scope_id,0), effective_date, version);

-- 4) Dwell state machine (v2 adds clock/appointment/notice/claim/bounded-interval columns)
CREATE TABLE IF NOT EXISTS detention_dwells (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  geofence_id BIGINT NOT NULL, vehicle_id BIGINT NOT NULL, driver_id BIGINT NULL,
  customer_id BIGINT NULL, job_id BIGINT NULL, dispatch_assignment_id BIGINT NULL,
  stop_role VARCHAR(20) NULL,
  entry_event_id BIGINT NOT NULL, exit_event_id BIGINT NULL,   -- original anchors (ledger below is authoritative for consumption)
  entered_at TIMESTAMPTZ NOT NULL, exited_at TIMESTAMPTZ NULL,
  arrival_lower TIMESTAMPTZ NULL, arrival_upper TIMESTAMPTZ NULL,     -- bounded entry interval (evidence)
  departure_lower TIMESTAMPTZ NULL, departure_upper TIMESTAMPTZ NULL, -- bounded exit interval (evidence)
  billed_from_at TIMESTAMPTZ NULL, billed_to_at TIMESTAMPTZ NULL,     -- = arrival_upper / departure_lower: dwell interval's LOWER bound (mandate 5)
  appointment_at TIMESTAMPTZ NULL,
  appointment_source VARCHAR(30) NULL,   -- 'assignment_planned'|'manual'|'attested_none'
  clock_start_at TIMESTAMPTZ NULL,       -- LATER-OF(appointment_at, billed_from_at) — mandate 1
  clock_rule VARCHAR(60) NULL,           -- 'later_of_appointment_arrival_v1' frozen into evidence
  dwell_minutes INT NULL, free_minutes_applied INT NULL, billable_minutes INT NULL,
  rule_card_id BIGINT NULL, rule_card_version INT NULL,
  quantity_hours DECIMAL(8,3) NULL, unit_rate DECIMAL(12,2) NULL, amount DECIMAL(12,2) NULL, currency VARCHAR(10) NULL,
  warning_notified_at TIMESTAMPTZ NULL,          -- pre-expiry notice stamp (race lock)
  claim_deadline_at TIMESTAMPTZ NULL,            -- exited + claim_window_days
  status VARCHAR(30) NOT NULL DEFAULT 'open',
    -- open|closed(settling)|below_free_time|unpriced_no_terms|needs_appointment|late_arrival|unattributed|priced_pending_review|approved|charged|dismissed
  close_reason VARCHAR(40) NULL,                 -- 'exit_event'|'timeout'|'gps_gap'
  truncated BOOLEAN NOT NULL DEFAULT FALSE,      -- timeout close while vehicle still inside
  review_required BOOLEAN NOT NULL DEFAULT TRUE,
  reviewed_by_user_id BIGINT NULL, reviewed_at TIMESTAMPTZ NULL, review_note TEXT NULL,
  job_charge_id BIGINT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(), updated_at TIMESTAMPTZ NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_detention_dwell_entry ON detention_dwells (company_id, entry_event_id);
CREATE UNIQUE INDEX IF NOT EXISTS uq_detention_dwell_open ON detention_dwells (company_id, geofence_id, vehicle_id) WHERE status='open';
CREATE INDEX IF NOT EXISTS idx_detention_dwells_company_status ON detention_dwells (company_id, status, entered_at DESC);

-- 5) CONSUMED-EVENT LEDGER (fixes both blockers: every geofence event a dwell absorbs — original entry, merged
-- re-entries, duplicate Entries from evaluator-instance overlap, cleared and final Exits, post-timeout orphan
-- Exits — gets exactly one row here, ever; Phase A/B select only unconsumed events)
CREATE TABLE IF NOT EXISTS detention_dwell_events (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL,
  dwell_id BIGINT NOT NULL,
  geofence_event_id BIGINT NOT NULL,
  event_type VARCHAR(10) NOT NULL,               -- 'Entry'|'Exit'
  event_time TIMESTAMPTZ NOT NULL,
  consume_role VARCHAR(30) NOT NULL,             -- 'open'|'close'|'merged_entry'|'superseded_exit'|'duplicate_entry'|'absorbed_post_close'
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_detention_event_consumed ON detention_dwell_events (company_id, geofence_event_id);
CREATE INDEX IF NOT EXISTS idx_detention_dwell_events_dwell ON detention_dwell_events (company_id, dwell_id, event_time);

-- 6) NOTICE LOG (mandate 2; welded into evidence)
CREATE TABLE IF NOT EXISTS detention_notices (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL, dwell_id BIGINT NOT NULL,
  notice_type VARCHAR(30) NOT NULL,              -- 'dispatch_warning'|'customer_meter_running'|'no_rule_card_nudge'
  recipient_name VARCHAR(160) NULL, recipient_address VARCHAR(220) NULL,  -- customers.contact_name/email/phone (001_schema.sql:99-101)
  channel VARCHAR(20) NOT NULL DEFAULT 'email',
  body_snapshot TEXT NOT NULL,                   -- exact timestamped wording
  delivery_status VARCHAR(20) NOT NULL DEFAULT 'logged',  -- 'logged'|'sent'|'failed' (v1 records; delivery via handler seam)
  sent_at TIMESTAMPTZ NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_detention_notice ON detention_notices (company_id, dwell_id, notice_type);

-- 7) Evidence (frozen at pricing; immutable BY TRIGGER; downsampled + share token)
CREATE TABLE IF NOT EXISTS detention_evidence (
  id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  company_id BIGINT NOT NULL, dwell_id BIGINT NOT NULL,
  schema_version INT NOT NULL DEFAULT 2,
  evidence_canonical TEXT NOT NULL,              -- exact hashed bytes (canonical JSON; TEXT because jsonb reorders keys)
  evidence_json JSONB NOT NULL,
  evidence_sha256 CHAR(64) NOT NULL,             -- internal only; customer-facing label is 'Evidence ref' (mandate 5: no crypto language)
  breadcrumb_count INT NOT NULL DEFAULT 0,       -- FULL in-window count
  breadcrumbs_included INT NOT NULL DEFAULT 0,   -- downsampled count actually embedded
  full_trail_sha256 CHAR(64) NULL,               -- streaming hash over the complete raw trail (not stored inline)
  share_token VARCHAR(64) NULL,                  -- 32 random bytes hex, server-generated (FleetTmsEndpoints.cs:499-505 pattern)
  share_expires_at TIMESTAMPTZ NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_detention_evidence_dwell ON detention_evidence (company_id, dwell_id);
CREATE UNIQUE INDEX IF NOT EXISTS uq_detention_evidence_token ON detention_evidence (share_token) WHERE share_token IS NOT NULL;
-- Immutability trigger: BEFORE UPDATE OR DELETE ON detention_evidence RAISE EXCEPTION, except UPDATEs whose
-- OLD/NEW differ only in share_token/share_expires_at (WHEN clause compares all other columns).

-- 8) Weld to the money spine
ALTER TABLE job_charges ADD COLUMN IF NOT EXISTS detention_dwell_id BIGINT NULL;
ALTER TABLE job_charges ADD COLUMN IF NOT EXISTS evidence_sha256 CHAR(64) NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_job_charges_detention ON job_charges (company_id, detention_dwell_id) WHERE detention_dwell_id IS NOT NULL;  -- one charge per dwell, ever

-- 9) Outbox idempotency (mirrors ux_outbox_job_delivered, FoundationSchemaService.cs:274)
CREATE UNIQUE INDEX IF NOT EXISTS ux_outbox_detention_closed ON outbox_messages (tenant_id, aggregate_id) WHERE event_type='detention.dwell.closed';
CREATE UNIQUE INDEX IF NOT EXISTS ux_outbox_detention_warning ON outbox_messages (tenant_id, aggregate_id) WHERE event_type='detention.dwell.warning';

## detectionAlgorithm

DetentionService.DetectAsync runs each safety tick after GeofenceEvaluator.EvaluateAsync (one call added at SafetyBackgroundService.cs:69, inside RunInSystemScopeAsync, explicit company_id joins per GeofenceEvaluator.cs:48; exceptions contained per :78-84). Boot gate: DetectAsync no-ops with a logged health warning unless detention tables exist AND pg_class.relrowsecurity is true for them (covers restricted-role prod where schema init is skipped, Program.cs:499-503).

PHASE A — OPEN (consumed-event ledger replaces v1's inference; fixes blocker #1). Candidates: geofence_events ge JOIN geofences g (same company) WHERE ge.event_type='Entry' AND g.site_role='customer_site' AND g.customer_id IS NOT NULL AND ge.event_time > NOW()-INTERVAL '7 days' AND NOT EXISTS (SELECT 1 FROM detention_dwell_events c WHERE c.company_id=ge.company_id AND c.geofence_event_id=ge.id) — consumption is explicit and total, not inferred from entry_event_id. Belt-and-braces: any Entry whose event_time falls inside an existing dwell's [entered_at, COALESCE(exited_at,'infinity')] for the same (company,geofence,vehicle) is consumed 'absorbed_post_close' and skipped. Dispatch per unconsumed Entry: (a) OPEN dwell exists for (fence,vehicle) → consume as 'duplicate_entry' (multi-instance evaluator overlap; no state change); (b) merge-eligible dwell exists — status IN ('closed','below_free_time','unpriced_no_terms','needs_appointment'), reviewed_at IS NULL, job_charge_id IS NULL, exited_at within merge_gap_minutes of this Entry — → REOPEN (status='open', clear exit/pricing fields), consume the Entry as 'merged_entry' and ledger the cleared Exit as 'superseded_exit'; merge-eligible states NEVER have an evidence row (evidence freezes only at pricing), so reopen never touches evidence; (c) else INSERT new dwell (ON CONFLICT via uq_detention_dwell_entry / uq_detention_dwell_open DO NOTHING) + consume Entry as 'open', same transaction.

PHASE B — CLOSE (fixes blocker #2). Close each open dwell with the earliest UNCONSUMED Exit for (fence,vehicle) whose event_time > MAX(event_time) over the dwell's ledger rows — a cleared pre-bounce Exit is ledgered 'superseded_exit' and can never re-close the dwell; consume the closing Exit as 'close'. Then compute BOUNDED intervals from breadcrumbs (mandate 5): arrival_lower = last breadcrumb provably outside before entry; arrival_upper = first breadcrumb provably inside — with a continuous-inside walk-back from entered_at (capped 6h) to recover late-tick entries (consultant minor: evaluator event_time is NOW()-at-tick, GeofenceEvaluator.cs:64); departure symmetric. Clock-skew clamp (consultant minor): breadcrumb times clamped to fence_event_time ± 10min outside the walk-back; when |event_time - received_at| > 5min (gateway-supplied @eventTime path, EndpointMappings.cs ~12621-12627) prefer received_at; record clock_used in evidence. billed_from_at = arrival_upper (LATEST plausible arrival), billed_to_at = departure_lower (EARLIEST plausible departure) — the dwell's LOWER bound, biasing every ambiguity in the customer's favor; fence-event times only when no breadcrumbs, disclosed as method='fence_event'.

PHASE B½ — CLOCK & APPOINTMENT (mandate 1). appointment_at := attributed assignment's planned_pickup_at / planned_delivery_at by stop_role (DispatchSchemaService.cs:24-25), overridable by audited manual entry until approval. clock_start_at = GREATEST(appointment_at, billed_from_at) — early arrival excluded from the billable clock by construction; rule string 'later_of_appointment_arrival_v1' stored on the dwell and in evidence. No appointment recorded → status='needs_appointment' after close: detected and shown, NEVER priced; two one-click resolutions: enter the time, or an audited attestation 'no appointment scheduled — clock from arrival' (appointment_source='attested_none'). Late arrival: billed_from_at > appointment_at + grace_minutes with late_arrival_policy='flag' → dwell rests in 'late_arrival' (math shown, unapprovable without an explicit override note) — resolves the market blocker with human judgment instead of silent void.

PHASE C — TIMEOUT / GPS-GAP. Open dwell older than max_dwell_hours → close close_reason='timeout', forced review; if latest_vehicle_positions still shows the vehicle inside, truncated=TRUE and the card says 'dwell truncated at Nh — additional time not billed' (documented undercharge; the eventual orphan Exit is consumed 'absorbed_post_close' so it never seeds a phantom dwell). Position staleness >60min while open → close at last-known-inside breadcrumb, close_reason='gps_gap', forced review. Never bill un-witnessed time.

PHASE C½ — PRE-EXPIRY NOTICE (mandate 2, market major). Each tick, for OPEN dwells with a resolved rule card AND a known clock start: when NOW() >= clock_start_at + free_minutes*notice_percent/100 and warning_notified_at IS NULL, one transaction (guarded UPDATE WHERE warning_notified_at IS NULL is the race lock): stamp warning_notified_at; insert dispatch notification (notifications table, NotificationSchemaService.cs:37-60; event_type='detention.warning', dedupe_key='detention-warning-{dwellId}', channel='in_app') — 'Free time at {site} expires {at}; meter starts at {rate}/h'; insert detention_notices 'customer_meter_running' to customers.contact_name/email with exact body snapshot, delivery_status='logged'; publish outbox 'detention.dwell.warning' (ON CONFLICT ux_outbox_detention_warning DO NOTHING) — DetentionWarningNotificationHandler is the email/SMS delivery seam, a stub in v1 that flips delivery_status when real delivery ships. Open dwell ≥60min at a rule-card-less site → one 'no_rule_card_nudge' dispatch notification (config-adoption hook). The notice log rides into the evidence bundle verbatim.

PHASE D — ATTRIBUTE (consultant minor fixed). On close: dispatch_assignments da JOIN jobs j ON j.customer_id=fence.customer_id (same company) WHERE da.vehicle_id=dwell.vehicle_id AND da.assigned_at <= billed_to_at AND COALESCE(da.actual_delivery_at, da.completed_at, 'infinity') >= entered_at — STRICT overlap (the old 2h backwards grace removed; a delivery stamp landing mid-dwell still overlaps by construction); rank by overlap duration. Populate driver_id from the winning assignment (market minor). stop_role via haversine of fence center vs jobs pickup/dropoff lat/lng (radius+250m); applies_to filters on it. No match → 'unattributed' with manual attach-to-job (validated job.customer_id=fence.customer_id); job_charges.job_id NOT NULL (BusinessSpineSchemaService.cs:79) makes unattributed charging schema-impossible.

PHASE E — PRICE WITH SETTLE DELAY (fixes the merge-timing majors). A closed dwell prices only when NOW() > exited_at + merge_gap_minutes — it rests in 'closed' through at least one full merge window (one extra 5-min tick of latency, invisible to operators), so next-tick bounce re-entries find a merge-eligible dwell and actually merge instead of fragmenting (and free_minutes is granted once). claim_deadline_at = exited_at + claim_window_days set at close (mandate 6). Multi-stop: each visit is its own dwell/evidence; same-site visits beyond the merge window are distinct. 7-day lookback bounds first-deploy backlog.

## pricingAndBilling

RULE CARD RESOLUTION (fail-closed, mandate 3): most-specific-wins exactly like billing_profiles (BillingConsolidationService.cs:32-47) — active card, effective as-of entered_at, customer scope beats tenant, newest effective_date/version wins. NO CARD → 'unpriced_no_terms', ZERO job_charges writes; surfaced as recoverable revenue (the sales wedge). Rule-card editor: one customer picker + five numbers, save in under 60 seconds; preview-first — 'these terms would have produced N charges totaling $X over the last 30 days' before commit; saving never retro-bills.

MATH (mandates 1/3/5 — every ambiguity resolved in the customer's favor): dwell interval = [billed_from_at, billed_to_at] (lower bound of the bounded entry/exit intervals); dwell_minutes = billed_to_at - clock_start_at where clock_start_at = GREATEST(appointment_at, billed_from_at) — early arrival never accrues; billable_minutes = max(0, dwell_minutes - free_minutes); 0 → 'below_free_time' (terminal). Else round billable DOWN to billing_increment_minutes (mandate overrides v1 round-up; the market consultant's overbilling attack dies here); quantity_hours = round(billable/60, 3); amount = min(round2(quantity_hours * rate_per_hour), max_charge_amount ?? ∞). Persist rule_card_id + version + all inputs on the dwell (the priced snapshot never re-resolves); build + hash + freeze evidence in the SAME transaction; status='priced_pending_review'; publish 'detention.dwell.closed' (aggregate=dwell id, partial-unique) carrying the FULL evidence_sha256 in the payload (external anchor).

APPROVAL QUEUE ONLY (mandate 4 — machine proposes, human approves; NO auto-send path exists). POST /api/detention/dwells/{id}/approve, one transaction: (1) UPDATE detention_dwells SET status='approved', reviewed_by_user_id=@u, reviewed_at=NOW() WHERE company_id=@c AND id=@id AND status='priced_pending_review' — rowcount 0 → 409; the status guard IS the double-approve lock. Pre-checks: (a) REFERENCES (mandate 5): at least one of jobs.po_number/bol_number/rate_con_number/appointment_ref present, else 422 with quick-add; audited reviewer override note may waive; (b) CLAIM WINDOW (mandate 6): past claim_deadline_at → approval requires an explicit override note and the packet permanently shows 'approved outside the {N}-day claim window'; late_arrival dwells require the override path. (2) INSERT job_charges (company_id, job_id, 'DETENTION', 'Detention — <site>', 'accessorial', description, quantity_hours, rate, amount, currency, 'approved', source='detention', 'per_hour', billing_status='unbilled', detention_dwell_id, evidence_sha256, approved_by_user_id, NOW()) — uq_job_charges_detention turns any raced double-insert into an error, never a duplicate. (3) dwell → 'charged', job_charge_id set. (4) audit.LogAsync('detention.approve') with amount + full evidence_sha256 (second external anchor). Edit-before-approve: downward-only amount adjustment with mandatory note recorded on the charge — the frozen bundle is never touched. POST /dismiss (waive): status-guarded → 'dismissed', mandatory reason.

DOWNSTREAM — NORMAL PATH (100% existing code): BillingConsolidationService selects the unbilled charge when the job's delivered assignment falls in the run period (BillingConsolidationService.cs:74-97), claim-flips :257-261, invoice line carries job_charge_id :244-254; invoice.issued → InvoiceIssuedGeneralLedgerHandler.cs:14-25 → balanced idempotent GL post (GeneralLedgerService.cs:50-75). source='detention' untouchable by re-rating (RatingService.cs:114-124).

DOWNSTREAM — SUPPLEMENTAL PATH (fixes the stranded-revenue major; v1's 'rides the next cycle' was FALSE per BillingConsolidationService.cs:82-85 + :178-180): at approval, if the job already appears on issued_invoice_lines, route the charge to a SUPPLEMENTAL draft — a new invoice_draft under group key 'job:{jobId}:supplemental:{yyyyMM}' with its OWN billing_consolidation_runs row (so the issued-group lock never applies), source='detention_supplemental', flowing through the same draft→review→issue→GL pipeline. Charges missing both paths (job never delivered, etc.) appear in a 'Stranded / not yet billable' report tile with one-click supplemental-draft — never silently lost, never force-billed. UI label updated; the false 'bills next cycle' promise deleted.

FUNNEL (mandate 7 — flagship): GET /api/detention/funnel per period: DETECTED = dwells closed (count + gross potential $, incl. unpriced_no_terms estimated at tenant-default rate, labeled estimate); NOTIFIED = dwells with warning_notified_at; BILLED = detention charges on issued_invoice_lines; COLLECTED = invoice_payments allocation (FinanceActivationSchemaService.cs:84-110; aggregation precedent RevenueReadinessService.cs:1805-1807) proportional by line. Headline: 'Detention recovered: $X collected of $Y detected'.

## evidenceBundle

Built and frozen inside the pricing transaction, one bundle per dwell (uq_detention_evidence_dwell), immutable by BEFORE UPDATE/DELETE trigger (only share_token/share_expires_at updatable). Canonical JSON schema_version 2: {schemaVersion, companyId, dwellId, customer:{id,name}, geofence:{id,name,centerLat,centerLng,radiusMeters}, vehicleId, driverId, job:{id,jobCode,stopRole, references:{poNumber,bolNumber,rateConNumber,appointmentRef}}, appointment:{plannedAt,source,arrivedAt,onTime,varianceMinutes,graceMinutes,policyApplied} (from dispatch_assignments planned_* — DispatchSchemaService.cs:24-25 — the AP clerk's first check; market blocker resolved), clock:{rule:'later_of_appointment_arrival_v1', clockStartAt, earlyArrivalExcludedMinutes} (mandate 1: applied clock rule stored in the bundle), intervals:{arrival:{lower,upper,method,clockUsed}, departure:{lower,upper,method,clockUsed}, billedFrom, billedTo, note:'billed from the shortest provable interval'} (mandate 5: bounded interval billed from the lower bound), pingCadence:{medianSecondsBetweenPings,gapsOver5Min} (disclosed cadence — honesty about GPS density is credibility), stationarity:{pctBreadcrumbsUnder3Mph,maxDisplacementMeters} (speed_mph, 001_schema.sql:332 — market minor), breadcrumbs:[downsampled: all points within 5min of each interval bound + 1 per 5min between, cap ~500], breadcrumbCount = full in-window count, fullTrailSha256 = streaming hash of the complete raw set (bounded bundle size — architecture minor resolved), noticeLog:[{type,recipient,channel,bodySnapshot,loggedAt,deliveryStatus}] (mandate 2 — the contemporaneous-notice proof that legally wins the claim), ruleCardSnapshot:{id,version,freeMinutes,ratePerHour,incrementMinutes,roundingDirection:'down',maxChargeAmount,claimWindowDays,currency}, claimWindow:{deadlineAt,approvedAt,withinWindow} (mandate 6: window compliance visible in the packet), computation:{dwellMinutes,freeMinutesApplied,billableMinutes,quantityHours,unitRate,amount,currency}}. Canonicalization: sorted keys, InvariantCulture, UTC ISO-8601, no whitespace; exact string in evidence_canonical (TEXT), sha via TelemetryHmacHelper.Sha256Hex (TelemetryHmacHelper.cs:19-24), JSONB copy for queries. STORAGE: detention_evidence row; hash duplicated onto job_charges.evidence_sha256 at approval; full sha externally anchored in the audit log AND the 'detention.dwell.closed' outbox payload (a DB-write tamperer must also rewrite append-only audit history).

CUSTOMER-DELIVERABLE ARTIFACT (market major + mandate 5): a NO-LOGIN shareable evidence page per charge. 'Share proof' (or approval auto-share) mints a server-generated 256-bit token (exact precedent: FleetTmsEndpoints.cs:499-505 — 'never accept a client-supplied token'; anonymous token-scoped route registration :59-67) into detention_evidence.share_token with expiry. GET /api/public/detention/evidence/{token} renders: fence over map, breadcrumb trail with disclosed ping cadence, appointment vs actual arrival, the bounded entry/exit intervals with 'billed from the shortest provable window', the exact rule in plain words, the notice log ('your designated contact was notified at 11:32 — before charges began'), the shipper's own PO/BOL/rate-con references, claim-window compliance, and a self-serve 'Verify this record' button that recomputes the hash server-side and shows 'Record verified — unaltered since {date}'. NO cryptography language customer-facing — label is 'Evidence ref: a1b2c3d4e5f6', brand is 'audit-ready evidence'; sha256 stays internal. Share URL + evidence ref print on the invoice line; a print stylesheet on the same page yields the one-page PDF (FileStorageService available if a stored PDF is later wanted).

SURFACED ON THE INVOICE LINE: invoice_draft_lines carry job_charge_id (BillingConsolidationService.cs:246) → line → charge → dwell → evidence is a keyed three-hop chain; line text: 'Detention 2.0h @ $60/h — GPS-verified, evidence ref a1b2c3d4e5f6, proof: <share link>'. Internal GET .../evidence/verify cross-checks stored vs recomputed vs charge-level sha.

## idempotencyAndFailClosed

Idempotency keys, one per hop — v2 closes both blocker holes: (1) EVENT CONSUMPTION — detention_dwell_events UNIQUE(company_id, geofence_event_id): every absorbed Entry/Exit (open, close, merged re-entry, superseded exit, duplicate Entry from evaluator-instance overlap, post-timeout orphan Exit) is ledgered exactly once; Phases A/B select ONLY unconsumed events, so a merged-then-charged dwell's re-entry can never seed a duplicate dwell, and a reopened dwell can never re-close on its cleared Exit; belt-and-braces span-exclusion catches anything inside an existing dwell's interval. (2) dwell open — uq_detention_dwell_entry + partial uq_detention_dwell_open, ON CONFLICT DO NOTHING. (3) SETTLE DELAY + guarded UPDATEs (WHERE status='open' / 'closed'; rowcount 0 = another tick won); pricing only after exited_at + merge_gap_minutes; evidence UNIQUE(company_id,dwell_id) + immutability trigger. (4) notices — guarded UPDATE WHERE warning_notified_at IS NULL + UNIQUE(company_id,dwell_id,notice_type) + partial-unique ux_outbox_detention_warning; at-least-once outbox redelivery safe by construction. (5) charge — status-guarded approve UPDATE + partial UNIQUE uq_job_charges_detention: raced double-approve = exactly one charge, second gets 409. (6) billing — existing claim-flip (BillingConsolidationService.cs:257-261) + issued-lines guards (:86-94); supplemental drafts get their OWN consolidation-run rows so issued-group locks can never strand a charge. (7) GL — existing UNIQUE(company_id, source_type, source_ref) early-return (GeneralLedgerService.cs:70-75).

FAIL-CLOSED LADDER (no config ⇒ bill NOTHING new; every rung mandated or schema-enforced): fence without customer_id + site_role → no dwell ever opens; no rule card → 'unpriced_no_terms', detected and SHOWN but zero pricing writes (mandate 3); no appointment and no attestation → 'needs_appointment', never priced (mandate 1); no attributable job → 'unattributed', schema-blocked (job_charges.job_id NOT NULL, BusinessSpineSchemaService.cs:79); below free time → terminal; late arrival under 'flag' → unapprovable without override; no operator approval → never a charge, and no auto-send path exists anywhere (mandate 4); timeout/gps_gap → forced review, truncated tails never billed; missing PO/BOL/rate-con → 422 at approval; claim window expired → override-only with permanent packet flag; restricted-role prod without out-of-band DDL → detector health-gate refuses to run rather than half-run. Detector exceptions contained per SafetyBackgroundService.cs:78-84 — a detention failure never blocks safety alerts; a missed tick is idempotently recovered because unconsumed events stay unconsumed.

## tenantScopingAndSecurity

All new tables carry company_id BIGINT and DetentionSchemaService registers BEFORE RlsReconciliation (Program.cs:496) so tenant_isolation + FORCE auto-enroll. PROD DEPLOYMENT (architecture minor resolved): when the runtime role is restricted, schema init is skipped (Program.cs:499-503) — the ADR ships an out-of-band DB-owner script (the exact DDL plus explicit ENABLE/FORCE ROW LEVEL SECURITY + tenant_isolation policies per table, mirroring RlsReconciliationSchemaService's policy shape) and DetectAsync health-gates on table existence + relrowsecurity=true. Detector runs cross-tenant under RunInSystemScopeAsync (SafetyBackgroundService.cs:66) with explicit company_id equality in every JOIN (GeofenceEvaluator.cs:48 precedent) — fence, position, assignment, job, and rule card must all share company_id or nothing matches. Authenticated endpoints derive companyId from context (GetCompanyId pattern, EndpointMappings.cs:584) with WHERE company_id=@cid everywhere; approve/dismiss/attach/attest/appointment-edit/share require billing-manage (RequireManage pattern, FleetTmsLogisticsEndpoints.cs:302) and write audit rows (who, amount, evidence sha). Rule cards admin-gated and versioned (new version row, never in-place edit of an effective row) so a priced dwell's snapshot stays historically true. PUBLIC SHARE PAGE: anonymous but token-scoped exactly like /api/public/shipments/track/{token} (FleetTmsEndpoints.cs:59-67, bypass registered in Program.cs per its header comment): tokens always server-generated 256-bit (the P2 fix at :499-502 is the precedent — never client-supplied), expiring, revocable (null-out allowed by the trigger carve-out), single-dwell-scoped, read-only, rate-limited; issuance/revocation audited. Evidence immutability trigger-enforced; external anchors (full sha in audit log + outbox payload) mean hiding tampering requires rewriting append-only audit history; daily hash-chain roll-up deferred to ADR hardening backlog.

## uxNotes

One page, /detention. Headline card = the FUNNEL (mandate 7): 'Detected $18,400 → Notified $15,100 → Billed $9,650 → Collected $7,200 this quarter' — four plain tiles, no gauges. Below it a task queue, not a NASA dashboard: Ready to approve / Needs attention / History. Each candidate is a sentence, not a chart: 'Truck 12 waited 3h 40m past its 10:00 appointment at Costco DC — 2h free per your rule card → $120 detention', with the appointment line ('arrived 09:40 — on time'), an entry/exit timeline strip showing the bounded interval ('billed from the shortest provable window'), a breadcrumb mini-map (reusing live-map components), the notice line ('their contact was notified at 11:32 — before charges began'), a claim-window countdown chip ('12 days left to bill' — mandate 6), and the shipper's references (PO/BOL/rate-con) with inline quick-add when missing. Two buttons: Approve & bill / Dismiss (reason required); amount edits are downward-only with a note. Needs-attention sells honestly: 'Truck 7 waited 5h at a site with no rule card — set terms to start capturing this' (one-click rule-card shortcut); 'No appointment on record — enter it or attest none was scheduled'; 'Arrived late — review'. Rule Cards (mandate 3): one form — customer picker + five numbers — saveable in under 60 seconds, with a preview line ('these terms would have produced N charges totaling $X over the last 30 days') before commit; saving never retro-bills. The LIVE moment (mandate 2): dispatch gets an in-app notification at 75% of free time — 'Free time at Costco DC expires 12:00; meter starts at $60/h' — early enough to call the facility, and the customer's designated contact gets the logged 'meter running' notice. Evidence is one tap: 'View proof' opens the same page the customer will see; 'Share proof' mints the no-login link printed on the invoice. Customer-facing language is 'audit-ready evidence' and 'Record verified — unaltered', with an Evidence ref code; zero cryptography words (mandate 5). Invoice line reads human: 'Detention 2.0h @ $60/h — GPS-verified, evidence ref a1b2c3d4e5f6, proof: <link>'. Suggested-sites bootstrap in setup: 'We found 14 frequent stop locations for your customers — create fences?' No nav sprawl: link from billing admin + a sidebar badge count.

## effort

XL

## endpoints

- GET  /api/detention/rule-cards + PUT /api/detention/rule-cards — list/upsert versioned rule cards (admin; editor designed for <60s entry; PUT returns a 30-day would-have-billed preview before commit)
- GET  /api/detention/dwells?status=&customerId= — review queue (tabs: ready-to-approve | needs attention: unattributed/unpriced_no_terms/needs_appointment/late_arrival/timeout | history); every row carries a claim-window countdown
- GET  /api/detention/dwells/{id} — detail incl. computation breakdown, notice log, claim countdown
- PUT  /api/detention/dwells/{id}/appointment — {appointmentAt} manual entry (audited, pre-approval only)
- POST /api/detention/dwells/{id}/attest-no-appointment — audited 'clock from arrival' attestation
- POST /api/detention/dwells/{id}/approve — maker-checker approve → job_charge (tx; validates references + claim window; auto-routes to a supplemental draft when the job is already invoiced)
- POST /api/detention/dwells/{id}/dismiss — {reason} → 'dismissed' (waive)
- POST /api/detention/dwells/{id}/attach-job — {jobId} for unattributed dwells (validates job.customer_id = fence.customer_id, then prices after settle)
- GET  /api/detention/dwells/{id}/evidence + /evidence/verify — internal bundle + hash recompute {valid, storedSha, computedSha, chargeSha}
- POST /api/detention/dwells/{id}/evidence/share + DELETE …/share — mint/revoke expiring server-generated 256-bit share token
- GET  /api/public/detention/evidence/{token} — NO-LOGIN customer-facing evidence page (map, breadcrumbs + disclosed ping cadence, appointment vs actual, bounded interval billed from lower bound, exact rule applied, notice log, PO/BOL/rate-con refs, claim-window compliance, 'Verify this record' button; print stylesheet = one-page PDF)
- GET  /api/detention/funnel?from=&to= — detected → notified → billed → collected recovered-revenue funnel (flagship dashboard)
- GET  /api/detention/stranded — approved-but-unbilled charges + one-click supplemental-draft action
- GET  /api/detention/suggested-sites — cluster jobs pickup/dropoff lat/lng per customer (90d) → one-click 'create customer_site fence (300m)' rows
- POST /api/detention/detect/preview — dry-run detector over a date range: would-be candidates, zero writes (preview-first)

## filesToChange

- /Users/zackkhan/Downloads/opstrax-enterprise-build-fixed-nginx/backend-dotnet/Services/SafetyBackgroundService.cs (add DetentionService.DetectAsync after GeofenceEvaluator.EvaluateAsync at line 69)
- /Users/zackkhan/Downloads/opstrax-enterprise-build-fixed-nginx/backend-dotnet/Program.cs (register DetentionSchemaService before RlsReconciliation at :496; register DetentionService; register DetentionWarningNotificationHandler in the handler registry :248-277; map DetentionEndpoints incl. public-route auth bypass per the tracking-link precedent)
- /Users/zackkhan/Downloads/opstrax-enterprise-build-fixed-nginx/backend-dotnet/Controllers/EndpointMappings.cs (geofence create/update at :584/:591 accept customerId/siteRole; job create/update accept poNumber/bolNumber/rateConNumber/appointmentRef)
- /Users/zackkhan/Downloads/opstrax-enterprise-build-fixed-nginx/frontend/src/pages/AdminPage.tsx (Rule Cards config section link)
- /Users/zackkhan/Downloads/opstrax-enterprise-build-fixed-nginx/frontend/src/pages/DispatchCommandPage.tsx (appointment-time entry on assignments where planned_* is empty — mandate 1 manual capture)
- frontend route registration for /detention and the public /evidence/:token route (App router file)

## newFiles

- /Users/zackkhan/Downloads/opstrax-enterprise-build-fixed-nginx/backend-dotnet/Services/DetentionSchemaService.cs
- /Users/zackkhan/Downloads/opstrax-enterprise-build-fixed-nginx/backend-dotnet/Services/DetentionService.cs (detect/clock/notice/price/evidence/approve/supplemental; Preview|Commit modes like RateMode)
- /Users/zackkhan/Downloads/opstrax-enterprise-build-fixed-nginx/backend-dotnet/Services/DetentionWarningNotificationHandler.cs (IOutboxMessageHandler for detention.dwell.warning — v1 stub records/flips delivery_status; real email/SMS delivery is the seam)
- /Users/zackkhan/Downloads/opstrax-enterprise-build-fixed-nginx/backend-dotnet/Controllers/DetentionEndpoints.cs (incl. the anonymous token-scoped public evidence route per the FleetTmsEndpoints.cs:59-67 pattern)
- /Users/zackkhan/Downloads/opstrax-enterprise-build-fixed-nginx/backend-dotnet.Tests/DetentionPostgresTests.cs
- /Users/zackkhan/Downloads/opstrax-enterprise-build-fixed-nginx/frontend/src/pages/DetentionReviewPage.tsx (queue + funnel header + rule-card editor + suggested sites)
- /Users/zackkhan/Downloads/opstrax-enterprise-build-fixed-nginx/frontend/src/pages/PublicDetentionEvidencePage.tsx (no-login share page, print-friendly)
- /Users/zackkhan/Downloads/opstrax-enterprise-build-fixed-nginx/frontend/src/services/detentionApi.ts
- /Users/zackkhan/Downloads/opstrax-enterprise-build-fixed-nginx/docs/telematics/adr/ADR-007-gps-proven-detention.md (incl. out-of-band prod DDL runbook, undercharge disclosures, corrected RatingService citation, deferred-items register)

## testsToWrite

- Clock rule happy path: appointment 10:00, arrival 09:40 (early), exit 14:10, card 120min free/$60/h/15-min increment → clock starts 10:00 (early 20min excluded), dwell 250min, billable 130 → rounded DOWN to 120 → 2.000h → $120.00; evidence records clock_rule + earlyArrivalExcludedMinutes=20
- Late arrival: arrival 60min after appointment+grace with policy='flag' → 'late_arrival', unapprovable without override note (which is audited and shown in packet)
- No appointment: close with no planned_* and no manual entry → 'needs_appointment', zero pricing; attest-no-appointment → clock from arrival, audited, appointment_source='attested_none'
- Fail-closed no rule card: full Entry/Exit, NO card → 'unpriced_no_terms', zero job_charges; dispatch gets exactly one no_rule_card_nudge
- CONSUMED-EVENT LEDGER (blocker regression): e1/x1/re-entry e2 within merge gap/x2 → ONE merged dwell; approve it; run DetectAsync 3x more → dwell count unchanged, e2 ledgered 'merged_entry', x1 'superseded_exit', no second charge ever
- Reopened-dwell close (blocker regression): after merge-reopen, next tick closes on x2, NOT the cleared x1 (ledger max-event-time rule)
- SETTLE DELAY: dwell closed at T not priced at T (rests 'closed'); priced only after T+merge_gap; bounce at T+5min merges instead of fragmenting; free_minutes granted exactly once
- Duplicate consecutive Entries (multi-instance evaluator overlap): two Entries, no interleaved Exit → one dwell, second Entry ledgered 'duplicate_entry', no reprocessable orphan
- PRE-EXPIRY NOTICE: crossing 75% of free time → warning_notified_at set exactly once under concurrent ticks; notifications row (dedupe_key) + detention_notices 'customer_meter_running' with body snapshot; outbox warning exactly one row on redelivery; noticeLog present in frozen evidence
- Round DOWN + lower-bound billing: sparse breadcrumbs → billed_from=arrival_upper, billed_to=departure_lower; billable 100min at 15-min increment → 90min billed (not 105)
- Clock-skew clamp: breadcrumb event_time 20min ahead of received_at → received_at used, clock_used recorded in evidence
- Cap + truncation: 30h dwell with cap $500 → clamped; timeout close while still inside → truncated=TRUE, late orphan Exit ledgered 'absorbed_post_close', no phantom dwell
- Approve idempotence/race: concurrent approves → one job_charge (uq_job_charges_detention + status guard), second 409
- Reference validation: job with no PO/BOL/rate-con/appointment ref → approve 422; add bol_number → approves; override-note path audited
- Claim window: approval past claim_deadline_at without override → 422; with override → packet permanently shows out-of-window flag
- SUPPLEMENTAL PATH (major regression): approve AFTER the job's invoice issued → supplemental draft under job:{id}:supplemental:{yyyyMM} with its own run row → issue → exactly one balanced GL entry even on invoice.issued redelivery; charge never stranded
- Normal money path e2e: approved charge on delivered job → consolidation drafts (line carries job_charge_id) → issue → GL idempotent on redelivery
- Attribution strict overlap: assignment ended 90min BEFORE fence entry → NOT matched (old grace bug); delivery stamped mid-dwell → matched; driver_id populated from winning assignment
- Evidence: immutability trigger blocks UPDATE/DELETE except share-token columns; verify pristine=true, mutate canonical → false; 30h dwell → breadcrumbs_included ≤ cap, breadcrumb_count = full count, full_trail_sha256 stable across recomputes
- Public share page: valid token renders only that dwell's tenant data; expired/revoked token 404; tokens never accepted from client input (server-generated 256-bit only)
- Cross-tenant: company-2 vehicle never matches company-1 fence; RLS isolation on all five new tables via the RlsTenantIsolationPostgresTests pattern
- Re-rating safety: RateJobAsync(Commit) leaves source='detention' rows intact
- Funnel: seeded pipeline (2 detected, 1 notified, 1 billed, partial payment) → correct four-stage counts and proportional collected $
- Preview endpoints write nothing: detect/preview + rule-card preview return candidates with all table counts unchanged

## edgeCases

- Early arrival (mandate 1): clock starts at appointment, never bare entry — a truck 3h early accrues nothing until the appointment; card says 'arrived early — clock started at appointment'
- Bounce/GPS jitter: settle delay + merge window now actually engage (v1's same-tick pricing made merges unreachable); every absorbed event is ledgered so no fragment can ever re-bill
- Overnight/weekend dwell: prices large → cap + forced review; truncated at max_dwell_hours with honest 'additional time not billed' label; post-timeout tail undercharge is a documented, conservative-direction unresolved risk
- Vehicle goes dark inside fence: gps_gap close at last-witnessed-inside time, forced review — un-witnessed time never billed
- Sparse ping cadence (this deployment's GPS freshness decays ~15min): bounded intervals shrink the billable dwell (customer-favorable) and the packet discloses cadence; accuracy improves automatically when the deferred Samsara/Motive connector workstream lands
- Notice never fired (detector down through the 75% point): packet shows notice status honestly; charge still requires human approval and the operator sees the gap before billing
- Shared dock / overlapping fences of two customers: two dwells; only the one matching the vehicle's attributed job survives; the other dies 'unattributed', unbilled
- Multi-stop job visiting the same site twice: distinct dwells, charges, evidence bundles beyond the merge window; per-dwell uniqueness welds hold
- Charge approved after invoice issued: supplemental-draft path (v1's 'bills next cycle' was false against BillingConsolidationService.cs:82-85/:178-180 — fixed, not papered over)
- Job never delivered: charge sits unbilled, surfaced in the stranded report — visible leakage, never force-billed
- Rule card changed mid-dwell: resolved as-of entered_at and snapshotted (id+version); later edits never silently re-price
- Driver/vehicle swap mid-job: attribution keys on vehicle_id + strict window overlap; each assignment wins its own window; driver_id per dwell from its winning assignment
- Appointment entered wrong then corrected: editable with audit until approval; after evidence freeze, correction requires dismiss + re-detect/re-price into a new bundle — frozen bundles are never edited
- First deploy: 7-day lookback bounds backlog; suggested-sites bootstrap turns fence setup from GIS data-entry into approval clicks
- Polygon fences: evaluator emits circles only (GeofenceEvaluator.cs:18-21); inherited limitation, ADR follow-up
- Multi-instance evaluator overlap (blue-green deploys): duplicate consecutive Entries absorbed by the consumed-event ledger — explicitly inside the idempotency envelope, with a dedicated test
- Restricted-role prod: schema init skipped (Program.cs:499-503) → out-of-band DB-owner script incl. RLS statements + detector health gate; detention can never half-run without RLS
- DEFERRED (unresolved-risk register): Samsara/Motive API connectors — v1-must for GTM but specced as a separate follow-on workstream per mandate 8; actual email/SMS notice delivery (v1 logs the notice; delivery handler is the first fast-follow — until then the packet says 'notice logged', not 'notice delivered'); daily evidence hash-chain roll-up; OCR rate-con extraction (manual rule cards first); sub-30s realtime push, AI dashcam, ELD, auto-send — all explicitly out

## Consultant findings (resolved in this revision)

### Lens: functionality — verdict: revise
- **[blocker]** Bounce-merge leaves the re-entry geofence event unconsumed, defeating every idempotency anchor. Walkthrough against the real semantics: Entry e1 opens dwell D (entry_event_id=e1); Exit x1 closes it; re-Entry e2 within merge_gap reopens D keeping entry_event_id=e1. Event e2 now has NO detention_dwells row referencing it. While D is open, Phase A's insert for e2 is blocked only by the partial unique
  - Fix: Make event consumption explicit rather than inferred: record every geofence_event id a dwell has absorbed (a dwell_geofence_events link table with UNIQUE(company_id, geofence_event_id), or merged_entry_event_ids on the dwell) and change Phase A's guard from 'no dwell with entry_event_id=ge.id' to 't
- **[blocker]** Phase B's close rule re-selects the already-consumed Exit after a merge-reopen. A reopened dwell keeps entered_at = e1's time with 'exit cleared'; Phase B closes an open dwell with 'the earliest Exit for (fence, vehicle) with event_time > entered_at' — that is x1, the pre-bounce Exit that was just cleared. On the very next tick the merged dwell re-closes at x1, erasing the post-bounce segment (and
  - Fix: Close with the earliest UNCONSUMED Exit whose event_time is greater than the latest consumed event's time (or track last_entry_event_time on the dwell and require exit event_time > that). This follows directly from the consumed-event ledger in the first blocker's fix.
- **[major]** The merge window is nearly dead code given the design's own tick timing, which makes the duplicate-dwell blocker fire on essentially EVERY bounce. Phases B and E run in the same 5-minute tick (close then immediately price), so a closed dwell reaches 'priced_pending_review' within the same tick it closes. A bounce re-entry is observed at the earliest one tick (5 min) after the Exit — by which time 
  - Fix: Introduce a settle delay: do not price a closed dwell until NOW() > exited_at + merge_gap_minutes (dwell rests in 'closed' for at least one merge window, i.e. price on a later tick). Alternatively allow merge to reopen 'priced_pending_review' dwells with a full re-price and evidence rebuild — but th
- **[major]** The claim 'if the dwell closes after the job's invoice already issued, the charge sits unbilled and rides the next consolidation run' is FALSE against the real BillingConsolidationService. The charge query only selects charges whose job has a delivered assignment with delivery date inside the run's period (BillingConsolidationService.cs:82-85), and CommitGroupAsync returns null — skipping the grou
  - Fix: Add an explicit supplemental path instead of relying on 'the next cycle': either (a) approval of a dwell whose job already has an issued invoice creates a supplemental invoice_draft (source='detention_supplemental', reusing the existing draft→issue→GL pipeline, which the credit-note document_type/ad
- **[minor]** The clock-skew claim ('device clocks appear only as breadcrumb metadata; all money-math times are server-side') is contradicted by the design's own billing rule: billable minutes are computed from refined_entered_at/refined_exited_at, which come from location_events.event_time — and the trusted-gateway GPS ingest path inserts a gateway/device-supplied @eventTime into that column (EndpointMappings.
  - Fix: Refine using COALESCE-style clamping: bound refined times to [fence_event_time - 10min, fence_event_time] and prefer received_at over event_time when they diverge beyond a tolerance; record which clock was used in the evidence bundle's refined.method field.
- **[minor]** Two conservative under-capture gaps worth stating honestly in the ADR rather than fixing: (1) Entry event_time is NOW() at tick time (GeofenceEvaluator.cs:64), so if the safety loop is delayed/down >10min the ±10min refinement window cannot recover the true entry and billing starts late (undercharge only). (2) After a timeout or gps_gap close while the vehicle is physically still inside, the debou
  - Fix: For (1) widen the refinement search backwards while breadcrumbs remain continuously inside the fence (walk back from entered_at until the first outside breadcrumb, capped at e.g. 6h). For (2) on timeout-close, if latest_vehicle_positions still shows the vehicle inside, either extend max_dwell via te
- **[minor]** Attribution window grace is directionally odd: 'COALESCE(actual_delivery_at, completed_at, infinity) >= entered_at - 2 hours' means an assignment that ENDED up to 2h BEFORE the vehicle entered the fence still matches — attaching, e.g., a post-delivery yard/return visit at the same customer to the already-completed job. The plausible intent (dwell continuing after actual_delivery_at is stamped mid-
  - Fix: Use strict overlap (assignment end >= entered_at) and, if a grace is wanted, apply it on the other side (assignment end >= entered_at means the stamp landed during the dwell); rank ties by overlap duration as designed.
### Lens: architecture — verdict: revise
- **[blocker]** Bounce-merge orphans the reopening Entry event and later double-charges it. Phase A reopens a merged dwell but keeps the ORIGINAL entry_event_id, so the second Entry event never gets a detention_dwells row. The idempotency anchor uq_detention_dwell_entry(company_id, entry_event_id) therefore does not consume it: on a later tick, after the merged dwell has been priced/approved/charged (so the merge
  - Fix: Make Entry-event consumption explicit and total: add a junction table detention_dwell_entry_events (company_id, dwell_id, entry_event_id) with UNIQUE(company_id, entry_event_id), written for the original entry AND every merged/absorbed entry; Phase A's 'no dwell yet' predicate becomes NOT EXISTS aga
- **[major]** The merge-eligible states ('closed'/'below_free_time') are inconsistent with the state machine's actual resting states, because Phase E prices in the same tick that Phase B closes. A dwell that exceeds free time rests as 'priced_pending_review' (or 'unpriced_no_terms'/'unattributed'), never as 'closed', so an exit/re-entry bounce that straddles a tick boundary will NOT merge: a second dwell opens 
  - Fix: Introduce a settling window: Phase E only prices dwells where NOW() > exited_at + merge_gap_minutes, so a freshly closed dwell rests in status 'closed' through the merge window (one extra tick of latency, invisible to operators). Extend merge eligibility to 'unpriced_no_terms' as well, and if you ev
- **[major]** The claim 'if the dwell closes after the job's invoice already issued, the charge sits unbilled and rides the next consolidation run' is contradicted by the real BillingConsolidationService. Charge selection requires the job's delivery date to fall BETWEEN the run's periodStart/periodEnd (BillingConsolidationService.cs:82-85), so a next-period run never sees a prior-period delivery; and re-running
  - Fix: Either (a) add a supplemental-billing path: a distinct consolidation group key such as job:{id}:supplemental:{yyyymm} (a NEW billing_consolidation_runs row, so the issued-group lock doesn't apply) that sweeps unbilled charges on jobs whose delivery predates the period, or (b) at minimum drop the fal
- **[minor]** Production deployment path for the new DDL is unaddressed: when the runtime connects as the restricted RLS role, the entire schema-init chain — including the proposed DetentionSchemaService and the RlsReconciliation step that would enroll the new tables — is SKIPPED (Program.cs:499-503 logs 'Schema init SKIPPED... apply out-of-band by the DB owner'). In exactly the environment where money moves, d
  - Fix: State the ops procedure in the ADR: apply DetentionSchemaService DDL out-of-band as the DB owner AND re-run RlsReconciliation (or include equivalent ENABLE/FORCE RLS + tenant_isolation policy statements in the out-of-band script). Add a boot-time assertion or health check that detention tables exist
- **[minor]** Evidence bundle size is unbounded: breadcrumbs include every location_events row in [entered-15min, exited+15min]; a legitimate 30-hour overnight dwell with frequent pings puts thousands of rows into evidence_canonical TEXT plus a JSONB copy, stored twice per dwell — row bloat and slow hash/verify, and the review UI ships it all to the browser.
  - Fix: Downsample breadcrumbs above a threshold (e.g., keep first/last N around entry and exit plus one point per M minutes in between), record breadcrumb_count for the full set, and include a second hash of the full raw set (computed streaming, not stored) so the complete trail remains provable without st
- **[minor]** Evidence 'immutable after' pricing is asserted but unenforced — no trigger, no REVOKE, no external anchor beyond the 12-hex-char prefix printed in the invoice description. Anyone with DB write access can rewrite evidence_canonical AND recompute/overwrite both stored sha256 copies; the verify endpoint would still report valid. Tamper-evidence currently holds only against edits that forget to update
  - Fix: Add a BEFORE UPDATE OR DELETE trigger on detention_evidence that raises (matching the GL period-lock trigger culture), and give the hash a genuinely external anchor: include the full evidence_sha256 in the audit.LogAsync payload at approval time and in the outbox 'detention.dwell.closed' payload, an
- **[minor]** Two small ground-truth drifts: (1) RatingService already implements per_stop and per_hour bases (RatingService.cs:58-73) — only the line-21 comment still says they're next-phase; the 'accessorials are unowned' premise still holds, but the citation overstates it. (2) The 'debounce guarantees strict Entry/Exit alternation' premise assumes a single evaluator instance; an overlapping blue-green deploy
  - Fix: Correct the RatingService citation in the ADR. Add an explicit test: two consecutive Entry events (no interleaved Exit) for the same fence+vehicle produce exactly one dwell and leave no re-processable orphan Entry, documenting that multi-instance evaluator overlap is within the idempotency envelope.
### Lens: market: MARKET + UX vs SAMSARA/MOTIVE/MCLEOD — dispute-winning evidence, operator simplicity, day-one sellability — verdict: revise
- **[blocker]** The evidence bundle omits the single fact a shipper's AP clerk checks first: was the truck ON TIME for its appointment? Standard detention terms only pay when the carrier arrived within the scheduled window; a claim without arrival-vs-appointment comparison is denied by default ('your driver was 3 hours late — detention void'). The data already exists — dispatch_assignments.planned_pickup_at/plann
  - Fix: Add appointment context end-to-end: (a) evidence bundle gains appointment:{plannedAt,arrivedAt,onTime,varianceMinutes} sourced from the attributed assignment's planned_* columns; (b) detention_terms gains an on_time_required BOOLEAN (default false, fail-closed stance preserved) and optional grace_mi
- **[major]** There is no customer-deliverable evidence artifact, so the 'proof' never actually reaches the disputing party in verifiable form. The invoice line carries only 'evidence a1b2c3d4e5f6' (first 12 hex chars); the verify endpoint (GET /api/detention/dwells/{id}/evidence/verify) sits behind the tenant's own auth, and the design defers the customer portal view to 'later'. An AP clerk receiving an invoic
  - Fix: Ship a shareable evidence artifact on day one: either (a) a tokenized read-only public link (signed, expiring, dwell-scoped) rendering the timeline + breadcrumb map + hash + a self-serve 'verify' button, printed on the invoice line/PDF next to the hash, or (b) a generated one-page evidence PDF store
- **[major]** Rounding billable minutes UP on GPS-coarse timestamps hands the AP clerk a legitimate line of attack. Fence-event times are quantized to the 5-minute safety tick (SafetyBackgroundService interval), breadcrumb refinement only helps if location_events density is good — in this deployment GPS ingestion is sparse/decaying (latest_vehicle_positions freshness known to go stale in ~15min with no simulato
  - Fix: Bias every ambiguity in the customer's favor and say so in the evidence: use the LATEST plausible entry and EARLIEST plausible exit when refinement is unavailable (record measurement method + uncertainty in the bundle), and make rounding direction a terms field defaulting to 'down' (or exact) rather
- **[major]** Detention is only surfaced after the money is already lost — the outbox event fires on dwell close. Samsara/Motive's stickiest detention feature is the live alert while the clock runs ('free time at Costco DC expires in 25 min'), which lets dispatch call the facility and either get the truck loaded or create a contemporaneous paper trail (notice-to-shipper is itself dispute ammunition: many contra
  - Fix: Add a Phase C½ in the same tick: for open dwells where NOW() - entered_at approaches free_minutes (e.g. 30-min warning) publish a 'detention.dwell.warning' outbox event (same partial-unique idempotency shape as ux_outbox_detention_closed, one warning per dwell) feeding the existing notification fan-
- **[minor]** Day-one onboarding friction: capturing any detention requires an operator to hand-draw a circular geofence per customer site AND set customer_id + site_role='customer_site' — for a fleet serving dozens of sites that is an afternoon of GIS work before the first dollar appears, and geofences DDL has no address field to assist (database/init/001_schema.sql:339-348). Samsara auto-suggests sites from s
  - Fix: Add a 'suggested sites' bootstrap: cluster jobs.pickup/dropoff lat/lng (columns confirmed in RatingService.cs:27-34) per customer over the last 90 days and offer one-click 'create customer_site fence here (300m)' rows in the Needs-attention tab or terms editor. Pure query + existing insert path; tur
- **[minor]** The dispute-facing narrative omits corroborating context an experienced AP clerk looks for: driver identity and vehicle stationary-ness. detention_dwells.driver_id is nullable and nothing populates it explicitly, and breadcrumbs include speed_mph (001_schema.sql:332) but the computation never asserts 'vehicle stationary during dwell' — a truck circling a yard for 3 hours prices identically to one 
  - Fix: Populate driver_id from the attributed assignment, and add a cheap stationarity stat to the evidence computation (e.g. percent of in-window breadcrumbs with speed_mph < 3, max displacement between breadcrumbs); surface it as one sentence on the card and in the bundle ('vehicle stationary 97% of the 