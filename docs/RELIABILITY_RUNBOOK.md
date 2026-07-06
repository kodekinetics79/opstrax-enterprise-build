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
| `GET /health/ready` | Readiness (serve traffic) | 200 only when DB is reachable **and** critical config is valid; otherwise **503**. Render's `healthCheckPath`. |
| `GET /health/deep` | Full diagnostic | DB latency, background-service heartbeats, config validation. 503 if DB is down. |
| `GET /metrics` | Prometheus exposition | Request/latency/error/DB gauges + counters for external scrapers. |

Every response carries `status`, `service`, `version`, `environment`,
`uptime_seconds`, `timestamp`, `checks`, and `failure_reason`. No secrets are
included — config checks report presence/strength only.

**External monitoring (alert within 60s):** point an uptime monitor (Better
Stack / Pingdom / UptimeRobot / Grafana Synthetic) at `GET /health/live` with a
30s interval and a 60s alert threshold. Point a metrics scraper at `/metrics`
for 5xx-rate / p95 alerting.

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
| Queue/worker delay > 2m | high | Background service heartbeat stale > 2m |
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

Render keeps every prior image. To roll back:

**Option A — Render dashboard (fastest):**
`opstrax-api service → Deploys → pick the last known-good deploy → "Rollback to
this version"`. Traffic shifts once `/health/ready` returns 200 on the old image.

**Option B — Git revert + redeploy:**
```bash
git revert <bad_sha>          # or: git reset --hard <good_sha> on a hotfix branch
git push origin <branch>      # autoDeploy triggers a new Render build
```

**Guardrail:** the startup config gate (`Program.cs`) aborts boot on critical
config failure in Production, so a broken-config deploy fails health checks and
Render **keeps the previous version** rather than serving a bad instance.

Migrations are **additive-only** (see `docs/` migration runners), so a code
rollback does not require a schema rollback. Never write a destructive migration
that a prior app version cannot tolerate.

---

## 6. Database backup / restore (Neon Postgres)

- **Backups:** Neon provides continuous WAL + point-in-time restore (PITR).
  Confirm the retention window in the Neon console (Project → Settings → History).
- **Verify restore (quarterly drill):**
  1. In Neon, create a **branch** from a timestamp ~1h ago (instant, copy-on-write).
  2. Point a scratch `PG_CONNECTION` at the branch and boot the API against it.
  3. Confirm `/health/ready` is 200 and spot-check row counts in `companies`,
     `dispatch_assignments`, `telemetry_events`.
  4. Record the drill in the Platform Admin **Backup Verifications**
     (`/api/compliance/backup-verifications`).
- **Restore for real:** create a branch at the target timestamp, validate, then
  repoint the production `PG_CONNECTION` to the restored branch (or promote it).

---

## 7. Environment variable sanity

Validated at startup and on demand by `ConfigValidationService`
(`GET /api/ops/config/check`, and surfaced in `/health/deep`). Required in Prod:

| Var | Required | Notes |
|---|---|---|
| `PG_CONNECTION` | ✅ | Neon connection string. Missing ⇒ **startup abort**. |
| `Jwt__Key` | ✅ | ≥ 32 chars (≥ 64 recommended). Missing ⇒ **startup abort**. |
| `PLATFORM_SUPERADMIN_PASSWORD` | ✅ (Prod) | Must not be the default. Default/unset ⇒ **startup abort** in Prod. |
| `Cors__AllowedOrigins` | ✅ | Explicit Vercel origin(s); no wildcard in Prod. |
| `ASPNETCORE_ENVIRONMENT` | ✅ | `Production` in prod. |
| `Logging__Json` | ⬜ | `true` for structured logs (default in Prod). |
| `OPSTRAX_DEPLOY_VERSION` | ⬜ | Falls back to `RENDER_GIT_COMMIT`. |
| `Telemetry__DeviceSecret`, `Telemetry__SseTicketKey` | ⬜ | Warn if missing (device auth / SSE degraded). |

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
