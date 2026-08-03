# OpsTrax Reliability & Incident Runbook

This is the operational runbook for the OpsTrax / KynexOne fleet SaaS. It covers
observability, SLOs, alerting, rollback, backup/restore, config sanity, and the
incident response flow. It is the source of truth referenced by the Platform
Admin **Reliability Center** (`/platform/reliability`).

---

## 1. Health endpoints

| Endpoint | Purpose | Behaviour |
|---|---|---|
| `GET /health/live` | Liveness (process up) | Always 200 if the process is alive. Cheap, no deps. |
| `GET /health/ready` | Readiness (serve traffic) | 200 only when DB, critical config and the Production contract—including critical workers—are valid; otherwise **503**. Render's `healthCheckPath`. |
| `GET /health/deep` | Full diagnostic | DB latency, expected background-worker roster, config and Production-contract details. Returns 503 for unhealthy or degraded state. |
| `GET /metrics` | Prometheus exposition | Request/latency/error/DB gauges + counters for external scrapers. |

Every response carries `status`, `service`, `version`, `environment`,
`uptime_seconds`, `timestamp`, `checks`, and `failure_reason`. No secrets are
included — config checks report presence/strength only.

**External monitoring (must be provisioned outside this repository):** use
`GET /health/ready` for service routing/synthetic readiness and `/health/deep`
for a protected operational diagnostic. `/health/live` proves only that the
process is running and must not be the sole availability signal. Scrape
`/metrics` for 5xx-rate / p95 alerting. The source endpoints and rule definitions
do not prove a monitor, retention backend, notification route, or on-call receipt;
capture those separately during release rehearsal.

### Critical-worker readiness contract

The API treats these always-on workers as an explicit release contract:
`TelemetryBackgroundService`, `SafetyBackgroundService`,
`TripBackgroundService`, `MaintenanceBackgroundService`,
`EscalationBackgroundService`, `ScheduledReportBackgroundService`, and
`RetentionEnforcementService`. Production startup fails unless retention is
explicitly enabled. Because retention enforcement runs daily, it refreshes an
idle liveness heartbeat every five minutes without changing the last-run result
or resetting a failure count. Agentic Ops remains optional.

For the first **two minutes from process start**, readiness exposes
`critical_worker_startup_grace_active=true` and missing/stale expected workers as
`starting`. The grace is tied to process start, not the first health request.
After it expires, each expected worker must have a heartbeat from the current
process epoch and newer than **ten minutes**. Always-on operational workers become
invalid after three consecutive failures; retention becomes invalid after its
first failed daily cycle so a purge defect cannot remain green for days. An empty ledger, one
missing row, a stale row, or a repeatedly failing row makes Production
`/health/ready` and `/health/deep` return 503. Deep health reports the expected
and observed counts plus `missing`, `stale`, `repeated_failures`, or
`heartbeat_ledger_unavailable` without exposing stored error text.

Operationally, wait for the roster to become healthy before admitting traffic,
then alert on the first 503. A restart is not proof of recovery: verify a new
successful heartbeat for the affected worker and retain the failure/recovery
timeline in the release or incident evidence.

### Retention enforcement boundary

The retention worker removes expired rows only from `location_events`,
`notifications`, and `report_execution_log`, in bounded
tenant-scoped batches (at most 50,000 rows per category and tenant per daily
cycle), and rechecks legal hold in every delete statement. Any category failure
fails the whole cycle, is recorded in the service run/heartbeat ledgers, emits a
redacted structured event, and writes a system audit outcome. Successful deletion
of other categories is not misreported as an overall success.

This control does **not** delete uploaded files or evidence from object storage,
does not erase business/financial records or driver/customer master rows, and is
not proof of per-subject erasure. Those require the separately authorized DSR and
object-store deletion workflow plus retained execution evidence.

---

## 2. SLOs (defined in `Observability/SloService.cs`)

| SLO | Target | Window |
|---|---|---|
| API availability | 99.9% | 30d |
| API p95 latency | < 500 ms | rolling 15m |
| API 5xx rate | < 0.5% | rolling 15m |
| Login availability | 99.5% | 30d |
| Fleet location updates < 60s | 95% | rolling |
| Telematics events processed < 2m | 99% | rolling |
| P1 detection | < 60 s | per incident |
| P1 acknowledgement | < 5 min | per incident |
| P1 recovery | 30–60 min | per incident |

Error-budget burn is computed live from the metrics window and shown in the
Reliability Center. Burn > 75% ⇒ `at_risk`; target breached ⇒ `breached`.

---

## 3. Alert rules (defined in `Observability/SloService.cs`)

| Rule | Severity | Condition |
|---|---|---|
| API down > 60s | critical | External monitor: no 2xx from `/health/live` for 60s |
| 5xx rate > 1% for 5m | critical | `rate_5xx_pct > 1` sustained 5m |
| p95 latency > 1s for 5m | high | `latency_p95_ms > 1000` sustained 5m |
| DB failures over threshold | critical | `db_failures` increasing / connection unavailable |
| Queue/worker delay > 2m | high | External warning before the ten-minute readiness freshness ceiling |
| Login/auth failure spike | high | `auth_failures` rate abnormal |
| Telematics ingestion delay > 2m | high | Latest telemetry `received_at` older than 2m |
| Critical create/update workflow fail | critical | 5xx on POST/PUT/PATCH create/update routes |

Wire these into your external alerting platform against `/metrics` and the
`/api/platform/reliability` snapshot. Thresholds live in code so they are
versioned and testable.

---

## 4. Observability: trace a failed request in < 60s

1. The frontend originates a **W3C `traceparent`** on every API call
   (`frontend/src/services/apiClient.ts`) and shows the returned `X-Trace-Id`.
2. The backend `RequestTelemetryMiddleware` continues that trace, binds it as
   the ambient `TelemetryContext`, and stamps **every** JSON log line and DB
   call with the same `trace_id` + `correlation_id`.
3. On a 500, the error response body includes the `correlation_id` + `trace_id`.
4. Search your log platform for `"trace_id":"<id>"` to see the full request →
   service → DB path, including `endpoint`, `tenant_id`, `user_id`, `status_code`,
   `duration_ms`, `deployment_version`, and the `error_code` + `stack_reference`.

Structured logs are JSON (enable with `Logging__Json=true`, default in Prod) and
are automatically **redacted** (`Observability/LogRedactor.cs`) for bearer
tokens, connection strings, passwords, JWTs, emails, and card numbers.

---

## 5. Rollback

Use immutable, registry-backed image digests and the deployment provider's
approved rollback/redeploy operation. Verify the provider retains the named
last-known-good artifact before the release; repository source does not prove it.

**Option A — deployment control plane:** select the recorded last-known-good
immutable image/deploy and follow the provider's approved rollback action.
Traffic may resume only after the expected version returns healthy and schema
compatibility plus critical smoke tests pass.

**Option B — Git revert + redeploy:**
```bash
git revert <bad_sha>          # reviewed, auditable inverse commit
git push origin <branch>      # autoDeploy triggers a new Render build
```

Use `git revert` on a reviewed recovery branch; do not rewrite shared history.

**Guardrail:** the startup/readiness config gate (`Program.cs`) reports critical
Production failures and prevents a bad instance becoming ready. Actual traffic
retention/rollback behavior is deployment-provider configuration and must be
verified in the target environment.

Migrations are designed for additive/expand-and-contract delivery, but a prior
binary is not assumed compatible merely because DDL was additive: constraints,
triggers, policies and data reconciliation can change behavior. Prove the
last-known-good binary against the candidate schema, otherwise use a reviewed
forward-fix or isolated PITR recovery. Never run an unreviewed destructive down
migration.

---

## 6. Database backup / restore (Neon Postgres)

- **Backups:** Neon provides continuous WAL + point-in-time restore (PITR).
  Confirm the retention window in the Neon console (Project → Settings → History).
- **Verify restore (quarterly and before a client-data pilot):**
  1. In Neon, create a **branch** from a timestamp ~1h ago (instant, copy-on-write).
  2. Point scratch restricted application/system identities at the branch and boot the exact candidate against it; never boot the runtime as owner.
  3. Confirm `/health/ready` is 200 and spot-check row counts in `companies`,
     `dispatch_assignments`, `telemetry_events`.
  4. For the Safety pilot, run `tools/dr-restore-drill.sh` with `DR_PILOT_COMPANY_CODE`, verify evidence objects and use `docs/pilot/SAFETY_PILOT_ROLLBACK_RECOVERY_PLAN.md`.
  5. Record the drill in the Platform Admin **Backup Verifications**
     (`/api/compliance/backup-verifications`).
- **Restore for real:** create a branch at the target timestamp, validate it in
  isolation, provision distinct `PG_CONNECTION_APP` and `PG_CONNECTION_SYSTEM`
  credentials on that branch, then use the approved database cutover/promotion
  procedure. The owner credential remains migration/recovery-only.

---

## 7. Environment variable sanity

Validated at startup and on demand by `ConfigValidationService`
(`GET /api/ops/config/check`, and surfaced in `/health/deep`). Required in Prod:

| Var | Required | Notes |
|---|---|---|
| `PG_CONNECTION_APP` / `ConnectionStrings__DefaultConnection` | ✅ | Exact restricted `opstrax_app` identity for tenant requests; never owner. |
| `PG_CONNECTION_SYSTEM` / `ConnectionStrings__SystemConnection` | ✅ | Distinct restricted `opstrax_system` control/background identity. |
| `Jwt__Key` | ✅ | ≥ 32 chars (≥ 64 recommended). Missing ⇒ **startup abort**. |
| `PLATFORM_SUPERADMIN_PASSWORD` | ✅ (Prod) | Must not be the default. Default/unset ⇒ **startup abort** in Prod. |
| `Cors__AllowedOrigins` | ✅ | Explicit Vercel origin(s); no wildcard in Prod. |
| `ASPNETCORE_ENVIRONMENT` | ✅ | `Production` in prod. |
| `Logging__Json` | ⬜ | `true` for structured logs (default in Prod). |
| `OPSTRAX_DEPLOY_VERSION` | ⬜ | Falls back to `RENDER_GIT_COMMIT`. |
| `DATA_PROTECTION_CERTIFICATE_BASE64` / password | ✅ | Shared certificate-protected database key ring for multi-instance/session continuity. |
| `DATA_ENCRYPTION_KEY` | ✅ | Valid 32-byte key for encrypted per-device credentials and protected data. |
| `SSE_TICKET_KEY` | Conditional | Required when telemetry SSE is in pilot scope. |
| `RetentionWorker__Enabled` | ✅ | Must be explicitly `true` in Production; missing, false, or invalid values abort startup. |

---

## 8. Incident response flow

1. **Detect** — alert fires (external monitor / Reliability Center). Incidents
   also auto-open when a background service reports 3+ consecutive failures.
2. **Acknowledge** (< 5 min for P1) — in the Reliability Center, click
   *Acknowledge* (`POST /api/platform/reliability/incidents/{id}/ack`). This
   stamps `acknowledged_at` + `acknowledged_by`.
3. **Diagnose** — open the linked `trace_id` + `deployment_version` on the
   incident; search logs by trace. Check `/health/deep` and top failing endpoints.
4. **Mitigate** — roll back (§5) if a bad deploy; scale/restart if resource-bound.
5. **Resolve** (target 30–60 min for P1) — *Resolve* with `root_cause` +
   `actions_taken` (`POST /api/platform/reliability/incidents/{id}/resolve`).
   These persist to the incident audit trail (`platform_incidents`).
6. **Review** — the incident record (severity, affected service/tenants,
   started/ack/resolved timestamps, root cause, actions, trace, deploy version)
   is the postmortem artifact.

---

## 9. Graceful shutdown

On SIGTERM (Render deploy/restart), the host drains in-flight requests for up to
25s (`HostOptions.ShutdownTimeout`) before force-stopping. Readiness flips to 503
so the load balancer stops routing new traffic first — preventing dropped
requests and partial writes during a rolling deploy. DB writes that must be
atomic use `Database.WithTransactionAsync` (commit-or-rollback), so an
interrupted request never leaves a partial write.
