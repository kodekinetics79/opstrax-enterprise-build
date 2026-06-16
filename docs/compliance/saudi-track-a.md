# Saudi Regulatory Product Compliance — Track A

This document describes the Saudi compliance capabilities (QIWA, WPS/SIF, GOSI
readiness) implemented in the Zayra HRM SaaS platform, the data lifecycle, RBAC,
audit events, and the checklist to activate the real QIWA integration.

---

## 1. QIWA Integration

### Workflow

```
Readiness check → Credential config → Connection config → Sync enqueue
   → Background worker picks up → Adapter push → Result
   → Success | Failed (retry w/ backoff) → DeadLetter
```

1. **Readiness check** — `GET /api/qiwa/employees/{id}/readiness` (or tenant-wide
   `GET /api/qiwa/readiness-summary`) reports which of the 8 required fields an
   employee is missing. Employees with gaps are blocked from sync.
2. **Credential config** — `PUT /api/qiwa/credentials` stores the tenant's QIWA
   OAuth2 `client_id` + `client_secret`. The secret is **encrypted at rest** with
   ASP.NET Core Data Protection (AES) and is never returned or logged.
3. **Connection config** — `PUT /api/qiwa/connection` stores the establishment ID,
   name, unified org number, and environment (`sandbox` | `production`).
4. **Sync enqueue** — `POST /api/qiwa/employees/{id}/sync` creates a `QiwaSyncLog`
   row with `Status = Pending`.
5. **Background worker** — `QiwaSyncWorker` (a `BackgroundService`) polls every 30s,
   groups pending logs per tenant, acquires/caches an access token, builds the
   payload, and calls the configured `IQiwaApiAdapter`.

### Required employee fields for QIWA (8)

| Field (API)          | Employee property      |
|----------------------|------------------------|
| `saudi_or_non_saudi` | `SaudiOrNonSaudi`      |
| `id_type`            | `IdType`               |
| `id_number`          | `IdNumber`             |
| `nationality`        | `Nationality`          |
| `occupation_code`    | `OccupationCode`       |
| `establishment_id`   | `EstablishmentId`      |
| `work_location_id`   | `WorkLocationId`       |
| `contract_reference` | `ContractReference`    |

### Establishment ID mapping

Each tenant has one `QiwaTenantConnection` row carrying its MOL-issued
`EstablishmentId`. If an individual employee carries its own `EstablishmentId`
that takes precedence; otherwise the worker falls back to the tenant connection.

### Sync lifecycle states

`Pending → Success` | `Pending → Failed → (retry) → Pending → … → DeadLetter`

| Status       | Meaning                                                   |
|--------------|-----------------------------------------------------------|
| `Pending`    | Enqueued / awaiting next worker pass                      |
| `Success`    | QIWA accepted the push; employee `QiwaSyncStatus=synced`  |
| `Failed`     | Attempt failed; will retry after backoff                  |
| `DeadLetter` | Retries exhausted; `QiwaSyncStatus=error`                 |
| `Skipped`    | Reserved                                                  |

### Retry policy

- `MaxRetries = 3` per sync log.
- Exponential backoff before re-attempt: **30s, 60s, 120s** (`2^retryCount * 30s`).
- `RetryCount`, `LastRetriedAtUtc`, `DeadLetterReason` are tracked on `QiwaSyncLog`.
- A dead-lettered log can be requeued via `POST /api/qiwa/sync-logs/{id}/retry`
  (resets to `Pending`, `RetryCount = 0`).

### Adapters

- **SandboxQiwaApiAdapter** (default) — no network; returns success for complete
  payloads and `FIELD_MISSING` for incomplete ones. Used when
  `QIWA_USE_LIVE_ADAPTER` is unset/false or the environment is `sandbox`.
- **LiveQiwaApiAdapter** — real OAuth2 (`client_credentials`) + employee push via
  the `qiwa` named `HttpClient` (`https://api.qiwa.tech`). Selected when
  `QIWA_USE_LIVE_ADAPTER=true`.

---

## 2. WPS / SIF Export

### Lifecycle

```
Draft → (run Approved/Locked) → Generated → Downloaded → Submitted
   → Accepted | Rejected → Reconciled
```

The `WpsStatus` column on `PayrollPaymentBatch` tracks this. Transitions are made
via `POST /api/payroll/payment-batches/{batchId}/wps-status` and audited.

### Validation & currency

- **IBAN validation** — `IbanValidator.IsValid` performs the ISO 13616 mod-97
  check. `IbanValidator.IsSaudiIban` additionally requires the `SA` prefix.
- **Pre-export check** — `POST /api/payroll/runs/{id}/wps-validation` lists blocking
  issues (unlocked run, missing/invalid IBANs) for the UI to consult.
- **Backend enforcement** — `POST /api/payroll/payment-batches/{id}/wps-file`
  independently re-validates: the run must be `Locked`/`Paid` and every payment
  record must have a valid IBAN, else it returns `400` with the offending employees.
  The frontend pre-check is never trusted.
- **Currency** — the SIF file uses the batch currency, falling back to the tenant
  company's `DefaultCurrency` (**SAR** for Saudi context) instead of a hardcoded AED.

> The SIF layout follows the CBUAE v2 segment structure (`EDI_DC40` / `E1EDL20` /
> `EOF`) with the currency token driven by tenant configuration.

---

## 3. Payroll Approval Controls

- Payment batches can only be created for **Locked** runs (approval workflow must
  complete first).
- Duplicate payment batches per run are rejected.
- `GET /api/payroll/runs/{id}/mismatch-report` (requires `payroll.review`) compares
  each employee's contract basic salary vs. the processed payroll basic, flags
  variance > 5%, and surfaces IBAN / GOSI / QIWA readiness gaps per employee.

---

## 4. GOSI Readiness

`GET /api/saudi-compliance/gosi-readiness` (requires `compliance.read`) checks each
active employee for:

- GOSI reference number (`Employee.GosiReference`)
- Company GOSI employer ID (`Company.GosiEmployerId`)
- Nationality (Saudi vs Non-Saudi rate basis)
- Contract salary > 0

It returns an **illustrative** contribution estimate:

| Category   | Employee | Employer |
|------------|----------|----------|
| Saudi      | 10%      | 12%      |
| Non-Saudi  | 0%       | 2%       |

> **Disclaimer (returned in the response):** GOSI contribution rates shown are
> illustrative. Verify current rates with the GOSI portal. No real GOSI API is called.

---

## 5. Saudi Compliance Dashboard

`GET /api/saudi-compliance/dashboard` aggregates QIWA, WPS, and GOSI status plus a
severity-ordered action-item list. It is gated by `compliance.read` **or**
`qiwa.read`, and requires the tenant to have at least one of `qiwa_integration`,
`wps_export`, `payroll`, or `compliance` enabled. The frontend renders this at
`/saudi-compliance`.

---

## 6. RBAC

| Permission       | Admin | HR Director | HR Manager | Auditor | Compliance Officer |
|------------------|:-----:|:-----------:|:----------:|:-------:|:------------------:|
| `qiwa.configure` |  ✓    |             |            |         |                    |
| `qiwa.sync`      |  ✓    |     ✓       |            |         |                    |
| `qiwa.read`      |  ✓    |     ✓       |     ✓      |   ✓     |                    |
| `compliance.read`|  ✓    |     ✓       |            |   ✓     |        ✓           |
| `payroll.review` |  ✓    |     ✓       |     ✓      |         |                    |

Endpoint authorization is enforced by per-endpoint permission checks (not role
strings). QIWA routes are additionally gated by the `qiwa_integration` feature flag.

---

## 7. Audit Events

| Action                          | Emitted when                              |
|---------------------------------|-------------------------------------------|
| `qiwa.sync_enqueued`            | A sync is enqueued                        |
| `qiwa.sync_success`             | Worker push succeeds                      |
| `qiwa.sync_failed`              | Worker push fails (will retry)            |
| `qiwa.sync_dead_letter`         | Retries exhausted                         |
| `qiwa.sync_retry_requested`     | Dead-letter requeued                      |
| `QiwaApiCredential` Created/Updated | Credentials saved (metadata only)     |
| `QiwaTenantConnection` Updated  | Connection config changed                 |
| `payroll.wps.generated`         | WPS/SIF file generated                    |
| `payroll.wps.status_changed`    | WPS batch status transition               |

---

## 8. Known Assumptions

- The **sandbox** QIWA adapter is used until live credentials are configured and
  `QIWA_USE_LIVE_ADAPTER=true`.
- The WPS SIF format follows CBUAE v2 with the currency driven by tenant config
  (SAR for Saudi context).
- GOSI figures are readiness/illustrative only — no live GOSI API call is made.

---

## 9. Live Activation Checklist

1. Obtain production QIWA OAuth2 `client_id` / `client_secret` and establishment ID.
2. `PUT /api/qiwa/credentials` with `environment=production`.
3. `PUT /api/qiwa/connection` with the production establishment details.
4. Set environment variable `QIWA_USE_LIVE_ADAPTER=true` and restart the API so the
   `LiveQiwaApiAdapter` is registered.
5. Confirm the real QIWA OAuth token endpoint and employee-sync request/response
   schema in `LiveQiwaApiAdapter` match the current QIWA API contract (field names,
   payload shape) and adjust if QIWA has revised them.
6. Verify Data Protection keys are persisted (and ideally encrypted) in production
   so encrypted client secrets survive restarts — configure a key ring
   (e.g. `PersistKeysToFileSystem` + `ProtectKeysWith*`) instead of the default
   ephemeral provider.
7. Run a single-employee sync against sandbox, then production, and confirm the
   `QiwaSyncLog` transitions to `Success`.
