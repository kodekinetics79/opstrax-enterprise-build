# Saudi Compliance Command Center

**PR:** Track A PR-4 — Saudi Compliance Command Center  
**Status:** Implemented (executive score card, real GOSI readiness, WPS export history, QIWA feature-flag awareness, rich action center, tenant isolation).

---

## Module Scope

| Area | Status |
|---|---|
| Executive compliance score (weighted QIWA + WPS + GOSI) | ✅ |
| Per-module status cards with real data (no illustrative rates) | ✅ |
| Rich action center: id, severity, title, description, affectedCount, route, canAct | ✅ |
| GOSI section uses `GosiCalculationService` + `GosiReadinessValidator` (PR-3) | ✅ |
| WPS section: MissingIbanCount, SIF export history, last batch status | ✅ |
| QIWA section: feature flag status, credential configured check | ✅ |
| GOSI variance count from most recent completed payroll run | ✅ |
| Frontend: executive score card, module-status chips, action center with expand/fix | ✅ |
| Tenant isolation on all queries | ✅ |
| No sensitive data (IBAN, GosiReference, Iqama) in any payload | ✅ |
| Feature-disabled states shown in QIWA card | ✅ |
| Permission-aware Fix buttons (route shown only when `canAct = true`) | ✅ |
| 14 backend tests | ✅ |
| Live QIWA/Mudad/GOSI external API integration | ❌ Out of scope (PR-4 constraint) |

---

## Backend API

### Existing endpoint (enhanced)

```
GET /api/saudi-compliance/dashboard
```

**Auth:** `Bearer` JWT — requires `compliance.read` OR `qiwa.read` permission.  
**Feature gate:** Tenant must have at least one of `qiwa_integration`, `wps_export`, `payroll`, `compliance` enabled. Absent flags count as enabled by default.

### Response shape

```json
{
  "overall": {
    "complianceScore": 84,
    "urgentActionCount": 2,
    "lastEvaluatedAt": "2026-06-17T10:00:00Z",
    "enabledModules": ["QIWA", "WPS", "GOSI"]
  },
  "qiwa": {
    "featureEnabled": true,
    "credentialConfigured": true,
    "connectionStatus": "Connected",
    "lastConnectedAt": "2026-06-16T08:30:00Z",
    "totalEmployees": 120,
    "readyForSync": 115,
    "blockedFromSync": 5,
    "readinessPercent": 95.8,
    "failedSyncCount": 0,
    "lastSuccessfulSync": "2026-06-17T06:00:00Z",
    "blockedEmployees": [
      { "employeeId": 42, "employeeCode": "EMP0042", "fullName": "Ahmed Al-Farsi", "missingFields": ["date_of_birth"] }
    ]
  },
  "wps": {
    "lastRunStatus": "Locked",
    "lastRunPeriod": "2026-05",
    "pendingApprovals": 0,
    "missingIbanCount": 2,
    "exportHistoryCount": 6,
    "lastExportDate": "2026-06-01T09:15:00Z",
    "lastExportStatus": "Generated",
    "blockingIssues": []
  },
  "gosi": {
    "employeesMissingGosiRef": 3,
    "employeesMissingGosiEmployerId": 0,
    "readyCount": 117,
    "blockedCount": 3,
    "warningCount": 2,
    "readinessPercent": 97.5,
    "gccEmployeeCount": 2,
    "varianceCount": 0,
    "warnings": ["2 GCC employee(s) — contribution rates pending legal confirmation."],
    "blockedEmployees": [
      { "employeeId": 7, "employeeCode": "EMP0007", "fullName": "John Doe", "blockingIssueCodes": ["MISSING_GOSI_REFERENCE"] }
    ]
  },
  "actionItems": [
    {
      "id": "gosi_blocked_employees",
      "severity": "High",
      "module": "GOSI",
      "title": "3 employee(s) blocked from GOSI calculation",
      "description": "These employees are missing a GOSI reference number...",
      "affectedCount": 3,
      "recommendedAction": "Open the People module and ensure each employee has a GOSI Reference...",
      "route": "/people?filter=missing_gosi_ref",
      "permissionRequired": "compliance.read",
      "canAct": true,
      "evaluatedAt": "2026-06-17T10:00:00Z"
    }
  ]
}
```

---

## Compliance Score Formula

The overall compliance score is computed as a weighted average of three module scores:

| Module | Weight | Score Basis |
|---|---|---|
| QIWA | 30% | `readinessPercent` (skip module, treat as 100%, if feature is disabled) |
| WPS | 35% | 100 minus 25 per blocking issue, minus up to 25 for missing IBANs, floor 0 |
| GOSI | 35% | `readinessPercent` (treat as 100% if no employees) |

**Score bands:**

| Range | Label | Colour |
|---|---|---|
| 90–100 | Excellent | Emerald |
| 75–89 | Good | Amber |
| 60–74 | Needs Attention | Amber |
| 0–59 | At Risk | Rose |

---

## Action Items

Each action item contains:

| Field | Type | Description |
|---|---|---|
| `id` | `string` | Stable identifier (e.g. `gosi_blocked_employees`) |
| `severity` | `Critical \| High \| Medium \| Low` | Drives sort order and colour |
| `module` | `QIWA \| WPS \| GOSI` | Source module |
| `title` | `string` | Short one-line title |
| `description` | `string` | Full explanation |
| `affectedCount` | `int` | Number of employees or runs affected (0 = configuration issue) |
| `recommendedAction` | `string` | Plain-English next step |
| `route` | `string?` | Deep link to the remediation UI (null if no direct link) |
| `permissionRequired` | `string` | Minimum permission for the action |
| `canAct` | `bool` | Always `true` from the backend; frontend shows Fix button when true |
| `evaluatedAt` | `DateTime` | When the item was generated (UTC) |

Action items are sorted: **Critical → High → Medium → Low**.

### Action item catalogue

| Id | Trigger | Severity |
|---|---|---|
| `qiwa_feature_disabled` | QIWA feature flag off | Medium |
| `qiwa_credentials_missing` | No `QiwaApiCredential` row | High |
| `qiwa_not_configured` | Connection status `NotConfigured` | High |
| `qiwa_connection_error` | Connection status `Error/ApiError/ConfigurationError` | Critical |
| `qiwa_blocked_employees` | `BlockedFromSync > 0` | High (≥10 → Critical) |
| `qiwa_failed_syncs` | `FailedSyncCount > 0` | Medium |
| `wps_missing_iban` | `MissingIbanCount > 0` | High (≥5 → Critical) |
| `wps_pending_approvals` | `PendingApprovals > 0` | Medium |
| `gosi_missing_employer_id` | Company GosiEmployerId blank | High |
| `gosi_blocked_employees` | `BlockedCount > 0` | High (≥5 → Critical) |
| `gosi_gcc_pending_confirmation` | `GccEmployeeCount > 0` | Medium |
| `gosi_variance_detected` | `VarianceCount > 0` | High |

---

## Security

### Sensitive data exclusions

The following values **never appear** in any dashboard payload:

| Field | Why excluded |
|---|---|
| `Employee.GosiReference` | GOSI personal ID — PII |
| `EmployeePayrollProfile.Iban` | Bank account number — PCI |
| `Employee.NationalId` / `IqamaNumber` | Government ID — PII |
| `QiwaApiCredential.EncryptedClientSecret` | OAuth2 secret |

The `GosiBlockedEmployee.BlockingIssueCodes` list contains symbolic codes only (`MISSING_GOSI_REFERENCE`, `MISSING_BASIC_SALARY`) — never the actual field values.

### Tenant isolation

All queries in `SaudiComplianceDashboardService` are scoped to the calling tenant's `TenantId`. GOSI global default rules are loaded via `IgnoreQueryFilters()` (same as `GosiController`) but are read-only configuration, not tenant data.

### RBAC

The `/api/saudi-compliance/dashboard` endpoint requires `compliance.read` or `qiwa.read`. Actions that mutate data (GOSI rule creation, WPS export) are protected by their own endpoints with appropriate permissions.

---

## Frontend

### Component: `SaudiComplianceDashboard`

Located at [frontend/src/views/SaudiComplianceDashboard.tsx](../../frontend/src/views/SaudiComplianceDashboard.tsx).

**Dashboard tab sections:**
1. **Refresh button** — re-fetches `/api/saudi-compliance/dashboard`
2. **Executive Score Card** (`OverallScoreCard`) — score ring, label, module-status chips
3. **Action Center** (`ActionCenter`) — expandable list with severity pills, affected counts, Fix buttons
4. **Module Cards** — QIWA, WPS, GOSI in a 3-column grid:
   - QIWA: feature status, credential configured, readiness bar, blocked employee table
   - WPS: last run period, pending approvals, missing IBAN count, SIF export history
   - GOSI: readiness bar, blocked count, blocked employee table, GCC warning count

**Fix buttons** link directly to the remediation route (e.g. `/people?filter=missing_gosi_ref`). They are shown only when `canAct === true` and a `route` is present — no dead buttons.

**Feature-disabled state:** When `qiwa.featureEnabled === false`, the QIWA card shows an informational placeholder instead of metrics.

**Configure tab:** `SaudiComplianceConfig` (unchanged from PR-2/PR-3).

---

## Tests

**File:** `backend-dotnet/Zayra.Api.Tests/ComplianceDashboardTests.cs`

| Test | Assertion |
|---|---|
| `GosiSection_ReflectsRealReadinessValidator_NotIllustrativeRates` | ReadyCount/BlockedCount from real validator |
| `GosiBlockedEmployees_ExposesIssueCodes_NotGosiReferenceValues` | BlockingIssueCodes match `^[A-Z_]+$` |
| `GosiSection_GccEmployeeCount_ReflectsGccNationality` | GccEmployeeCount = 1 for UAE nationality |
| `WpsSection_MissingIbanCount_ReflectsInvalidIbans` | Count = 1 for mixed IBAN validity |
| `WpsSection_ExportHistoryCount_ReflectsWpsFileBatches` | Count = 2 after seeding 2 batches |
| `QiwaSection_FeatureEnabled_DefaultsTrueWhenNoFlagExists` | Default = true (absent = enabled) |
| `QiwaSection_FeatureEnabled_FalseWhenFlagExplicitlyDisabled` | False when row exists with IsEnabled=false |
| `QiwaSection_CredentialConfigured_TrueWhenCredentialRowExists` | True when QiwaApiCredential present |
| `OverallScore_IsBetweenZeroAndHundred` | Score ∈ [0, 100] |
| `OverallScore_HighWhenTenantIsClean` | Score ≥ 70 for fully-populated tenant |
| `ActionItems_AreRichDtos_WithRequiredFields` | Id, Title, Module, Severity present |
| `ActionItems_NeverContainSensitiveValues_GosiRefOrIban` | JSON serialisation contains no IBAN/GosiRef |
| `ActionItems_SortedCriticalFirst` | Severity order is non-decreasing |
| `TenantIsolation_TenantBDataNeverAppearsInTenantADashboard` | Tenant B's employees, runs, batches excluded |

---

## Enterprise Benchmark

| Capability | Workday / SAP | ADP / UKG | Jisr / Bayzat | Zayra (current) |
|---|---|---|---|---|
| Executive compliance score | ✅ | ✅ | ⚠️ Basic | ✅ |
| Weighted module score | ✅ | ✅ | ❌ | ✅ |
| Real GOSI readiness (rule-based) | ✅ | ✅ | ✅ | ✅ |
| GOSI variance detection | ✅ | ✅ | ⚠️ | ✅ |
| WPS IBAN validation in dashboard | ✅ | ✅ | ✅ | ✅ |
| WPS SIF export history | ✅ | ✅ | ⚠️ | ✅ |
| QIWA feature-flag aware status | ✅ | ✅ | ✅ | ✅ |
| Rich action center with deep links | ✅ | ✅ | ⚠️ | ✅ |
| Permission-aware action buttons | ✅ | ✅ | ⚠️ | ✅ |
| No sensitive data in payloads | ✅ | ✅ | ✅ | ✅ |
| Tenant isolation | ✅ | ✅ | ✅ | ✅ |
| Live QIWA/Mudad/GOSI API | ✅ | ✅ | ✅ | ❌ Pending |

### Gaps vs enterprise tier

**P0 — Critical:**
- Live GOSI API submission (e-GOSI) — currently dashboard-only; no automated filing

**P1 — Important:**
- QIWA sync scheduling from dashboard (currently manual trigger from People module)
- WPS rejected batch detection (SAMA rejection codes not yet ingested)

**P2 — Enhancement:**
- Historical compliance score trend chart (score over time)
- Email/in-app alerts when compliance score drops below threshold

---

## Manual QA Checklist

- [ ] `GET /api/saudi-compliance/dashboard` returns 200 with `overall.complianceScore` ∈ [0, 100]
- [ ] GOSI section `readyCount + blockedCount` equals total active employee count
- [ ] Remove an employee's GosiReference; call dashboard; verify `gosi.blockedCount` increases and action item `gosi_blocked_employees` appears
- [ ] Add an invalid IBAN to a payroll profile; call dashboard; verify `wps.missingIbanCount` > 0 and action item `wps_missing_iban` appears
- [ ] Disable QIWA feature flag (`IsEnabled = false`); call dashboard; verify `qiwa.featureEnabled = false` and QIWA card shows disabled state
- [ ] Verify `qiwa.blockedEmployees[*].missingFields` contains field names, not actual National ID values
- [ ] Verify `gosi.blockedEmployees[*].blockingIssueCodes` contains `MISSING_GOSI_REFERENCE` (not the GosiReference value)
- [ ] Create SIF export (WPS export); call dashboard; verify `wps.exportHistoryCount` increments and `wps.lastExportDate` is set
- [ ] Call dashboard as a user without `compliance.read` or `qiwa.read` → expect 403
- [ ] Call dashboard from tenant A; verify tenant B employees, runs, and batches are absent
