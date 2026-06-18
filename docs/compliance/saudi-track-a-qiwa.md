# Saudi Track A — QIWA Integration

**PR:** Track A PR-1 — QIWA Readiness + Sync Engine  
**Status:** Implemented (background worker + sync queue). Real Qiwa API calls require production credentials.

---

## Overview

This document describes the Qiwa integration architecture for Track A of Saudi HR compliance. Qiwa is the Ministry of Human Resources & Social Development (MHRSD) platform for managing employment contracts, workforce data, and labour compliance.

---

## What's in Scope (This PR)

| Area | Status |
|---|---|
| Readiness gate on sync enqueue (Push direction) | ✅ |
| Enhanced readiness report (blocking reasons, warnings, IsEligibleForSync) | ✅ |
| Feature flag check in worker (skip disabled tenants) | ✅ |
| Readiness pre-check in worker (dead-letter non-ready employees) | ✅ |
| Bulk sync endpoint `POST /api/qiwa/sync/bulk` | ✅ |
| `EnqueueBulkSyncAsync` service method | ✅ |
| Test coverage: 25 Qiwa tests, 145 total | ✅ |

**Out of scope for this PR:** WPS/SIF export, GOSI, real Qiwa API HTTP calls (covered in a future PR).

---

## Required Employee Fields for Qiwa Sync

An employee must have all 8 fields populated to be considered Qiwa-ready:

| Field | Description |
|---|---|
| `saudi_or_non_saudi` | Saudi/Non-Saudi classification |
| `id_type` | Government ID type (NationalId, Iqama, Passport, etc.) |
| `id_number` | Government ID number matching the ID type |
| `nationality` | Employee nationality |
| `occupation_code` | ISCO-88 / Qiwa occupation code |
| `establishment_id` | MOL establishment ID |
| `work_location_id` | Qiwa work location ID within the establishment |
| `contract_reference` | Qiwa labour contract reference number |

**Non-blocking recommendation:** `work_permit_reference` is strongly recommended for Non-Saudi employees (surfaced as a warning, not a blocking reason).

---

## Feature Flag

The Qiwa integration is controlled by the `qiwa_integration` feature flag (`FeatureKeys.QiwaIntegration`).

- **Absent row = enabled** (default on)
- **`IsEnabled = false`** = worker skips this tenant; API endpoints return 403

To disable Qiwa for a tenant:
```sql
INSERT INTO TenantFeatureFlags (Id, TenantId, FeatureKey, IsEnabled)
VALUES (UUID(), '<tenant-id>', 'qiwa_integration', false);
```

---

## Sync Flow

```
HR Admin                 API                   QiwaSyncWorker
   |                      |                          |
   |-- POST /sync/bulk --> |                          |
   |                      |-- EnqueueBulkSyncAsync() |
   |                      |   [readiness check]      |
   |                      |   [skip pending dupes]   |
   |                      |   [create SyncLogs]      |
   |                      |   [audit]                |
   |<-- 202 Accepted ---   |                          |
   |                      |         (30s poll)        |
   |                      |--- ProcessOnceAsync() -->  |
   |                      |     [feature flag check]  |
   |                      |     [load credentials]    |
   |                      |     [validate creds]      |
   |                      |     [acquire token]       |
   |                      |     [readiness pre-check] |
   |                      |     [PushEmployeeAsync]   |
   |                      |     [update status]       |
   |                      |     [audit]               |
```

---

## Sync Log Lifecycle

```
Pending → Processing → Success
                     → Failed (retry with exponential backoff: 2^n × 30s)
                              → DeadLetter (after MaxRetries=3)
```

Dead-letter triggers:
- Missing or invalid Qiwa credentials (non-retryable)
- Employee not found or deleted
- Employee not Qiwa-ready at worker time (missing required fields)
- API returns non-retryable error code

---

## Readiness Report Fields

```json
{
  "employeeId": 42,
  "employeeCode": "EMP-042",
  "fullName": "Ahmed Al-Rashidi",
  "isReady": false,
  "isEligibleForSync": false,
  "missingFields": ["occupation_code", "contract_reference"],
  "blockingReasons": [
    "ISCO-88 / Qiwa occupation code is required.",
    "Qiwa labour contract reference number is required."
  ],
  "warnings": [],
  "checkedAtUtc": "2026-06-17T10:00:00Z"
}
```

`isEligibleForSync = isReady AND status IN (Active, Invited)` — archived/offboarded employees are excluded.

---

## API Endpoints

| Method | Route | Permission | Description |
|---|---|---|---|
| `GET` | `/api/qiwa/connection` | `qiwa.read` | Tenant connection status |
| `PUT` | `/api/qiwa/connection` | `qiwa.configure` | Save establishment config |
| `PUT` | `/api/qiwa/credentials` | `qiwa.configure` | Save OAuth2 credentials (encrypted) |
| `GET` | `/api/qiwa/employees/{id}/readiness` | `qiwa.read` | Single employee readiness |
| `GET` | `/api/qiwa/readiness-summary` | `qiwa.read` | Tenant-wide readiness summary |
| `GET` | `/api/qiwa/compliance-summary` | `qiwa.read` | Dashboard compliance figures |
| `POST` | `/api/qiwa/employees/{id}/sync` | `qiwa.sync` | Enqueue single employee sync |
| `POST` | `/api/qiwa/sync/bulk` | `qiwa.sync` | Enqueue all ready employees |
| `POST` | `/api/qiwa/sync-logs/{id}/retry` | `qiwa.sync` | Retry dead-lettered log |
| `GET` | `/api/qiwa/sync-logs` | `qiwa.read` | Paginated sync log history |

---

## Security Constraints

- **Credentials never logged.** `EncryptedClientSecret` is decrypted in memory and immediately discarded; it is never written to logs, audit payloads, or API responses.
- **Purpose-locked encryption.** ASP.NET Core Data Protection with purpose string `Zayra.Qiwa.ClientSecret.v1` isolates secrets from other protected payloads.
- **Tenant isolation.** All service methods scope queries to the calling tenant's `TenantId`. The worker re-applies explicit tenant scoping after `IgnoreQueryFilters()`.
- **RBAC enforced per endpoint.** `qiwa.read` / `qiwa.sync` / `qiwa.configure` permissions are checked per endpoint, not at controller level.

---

## Configuration (Production)

```json
{
  "DataProtection": {
    "ApplicationName": "Zayra",
    "PersistKeysToFileSystem": "/keys/data-protection"
  }
}
```

Production Qiwa API credentials are configured per tenant via `PUT /api/qiwa/credentials` and stored encrypted. They are **never stored in `appsettings.json`** or environment variables.

---

## Not Yet Implemented

- Real Qiwa API HTTP adapter (currently using `SandboxQiwaApiAdapter`)
- WPS (Wage Protection System) / SIF export
- GOSI integration
- Qiwa establishment lookup API
- Pull direction sync (currently only Push is processed by the worker)
