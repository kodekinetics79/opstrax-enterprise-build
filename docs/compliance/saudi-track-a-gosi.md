# Saudi Track A — GOSI Readiness + Payroll Deduction Governance

**PR:** Track A PR-3 — GOSI Readiness + Payroll Deduction Governance  
**Status:** Implemented (effective-dated rules, branch separation, readiness validator, payroll integration, endpoints, tests).

---

## Module Scope

| Area | Status |
|---|---|
| Effective-dated `GosiContributionRule` model + seeded SA defaults | ✅ |
| `GosiCalculationService` — deterministic, branch-separated calculation | ✅ |
| `GosiReadinessValidator` — per-employee blocking issues + warnings | ✅ |
| Payroll processing — rule-based GOSI deductions (replaces flat SystemSettings rate) | ✅ |
| Employer-side GOSI tracked in `PayrollDeductions` (distinct from employee deductions) | ✅ |
| GL journal — branch-based employer GOSI DR/CR entries | ✅ |
| `GosiController` — readiness summary, per-employee, run summary, variance report | ✅ |
| GOSI contribution rules API (list + tenant override) | ✅ |
| `GosiRuleSeeder` — seeds 11 default rules at startup (idempotent) | ✅ |
| 22 unit tests | ✅ |
| Fake GOSI API integration | ❌ Out of scope (no external API exists) |
| Mudad GOSI e-submission | ❌ Out of scope |

---

## Data Model

### `GosiContributionRule`

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `TenantId` | `Guid` | `Guid.Empty` = system default; specific Guid = tenant override |
| `CountryCode` | `varchar(5)` | ISO-3166-1 alpha-2, default `"SA"` |
| `Classification` | `varchar(20)` | `Saudi` / `GCC` / `NonSaudi` |
| `Branch` | `varchar(30)` | `Annuities` / `SANED` / `OccupationalHazards` |
| `Payer` | `varchar(20)` | `Employee` / `Employer` |
| `Rate` | `decimal(7,4)` | Percentage (e.g. `9.00` for 9%) |
| `MinContributoryWage` | `decimal(12,2)` | Optional floor (SAR) |
| `MaxContributoryWage` | `decimal(12,2)` | Optional cap (SAR) |
| `EffectiveFrom` | `date` | Inclusive start date |
| `EffectiveTo` | `date?` | Inclusive end date, null = open-ended |
| `IsActive` | `bool` | Logical delete / deactivation |
| `SourceReference` | `varchar(200)` | e.g. "GOSI Circular 2016/1" |
| `Notes` | `varchar(500)` | Free-text, used for GCC pending-confirmation flag |

### Rule Resolution (per employee, per pay period)

1. Filter: `Classification == employee.Classification`
2. Filter: `EffectiveFrom <= periodDate AND (EffectiveTo == null OR EffectiveTo >= periodDate)`
3. Group by `(Branch, Payer)` — if a tenant-specific override exists, it wins over the system default.

---

## Default Seeded Rates (Saudi Arabia)

Seeded at startup via `GosiRuleSeeder` if no global defaults exist (`TenantId == Guid.Empty`).

| Classification | Branch | Payer | Rate |
|---|---|---|---|
| Saudi | Annuities | Employee | **9.00%** |
| Saudi | Annuities | Employer | **9.00%** |
| Saudi | SANED | Employee | **0.75%** |
| Saudi | SANED | Employer | **0.75%** |
| Saudi | OccupationalHazards | Employer | **2.00%** |
| GCC | Annuities | Employee | 9.00% ⚠️ |
| GCC | Annuities | Employer | 9.00% ⚠️ |
| GCC | SANED | Employee | 0.75% ⚠️ |
| GCC | SANED | Employer | 0.75% ⚠️ |
| GCC | OccupationalHazards | Employer | 2.00% ⚠️ |
| NonSaudi | OccupationalHazards | Employer | **2.00%** |

> ⚠️ **GCC rules are pending legal confirmation.** Rates mirror the Saudi baseline per the GCC Social Insurance Agreement but must be verified per applicable bilateral treaty before submission. A `GCC_RULES_PENDING_CONFIRMATION` warning is raised for each GCC employee during readiness validation.

Contributory wage is **basic salary only** (housing, transport, and other allowances excluded). Min/max wage caps are `null` in the baseline — update annually per current GOSI circulars (current caps: ~SAR 400 minimum, ~SAR 45,000 maximum for annuities as of 2024).

---

## Employee Classification

Nationality strings are normalised by `GosiCalculationService.DeriveClassification()`:

| Input examples | Classification |
|---|---|
| `"SA"`, `"SAU"`, `"Saudi"`, `"Saudi Arabia"` | **Saudi** |
| `"UAE"`, `"Emirati"`, `"BH"`, `"Bahrain"`, `"KW"`, `"Kuwait"`, `"OM"`, `"Oman"`, `"QA"`, `"Qatar"` | **GCC** |
| Anything else, blank, or null | **NonSaudi** |

---

## PayrollDeduction Component Codes

| Component Code | Payer | Reduces Net Pay? | GL Account |
|---|---|---|---|
| `GOSI_ANNUITIES_EMP` | Employee | ✅ Yes | 2101 |
| `GOSI_SANED_EMP` | Employee | ✅ Yes | 2101 |
| `GOSI_ANNUITIES_ER` | Employer | ❌ No | 2106 |
| `GOSI_SANED_ER` | Employer | ❌ No | 2106 |
| `GOSI_OCHAZARDS_ER` | Employer | ❌ No | 2106 |
| `GOSI_EMPLOYEE` | Employee | ✅ (backward-compat, pre-PR3 runs) | 2101 |

**Employee contributions** are summed into the `deductions` variable and reduce the employee's net salary.  
**Employer contributions** are written as separate `PayrollDeduction` records but are NOT included in the net salary deductions sum.

---

## GL Journal Impact

The journal endpoint (`GET /api/payroll/runs/{id}/gl-entries`) now generates:

| Entry | GL Code | DR/CR | Source |
|---|---|---|---|
| GOSI Annuities Payable (Employee) | 2101 | CR | PayrollDeduction `GOSI_ANNUITIES_EMP` |
| GOSI SANED Payable (Employee) | 2101 | CR | PayrollDeduction `GOSI_SANED_EMP` |
| GOSI Annuities Payable (Employer) | 2106 | CR | PayrollDeduction `GOSI_ANNUITIES_ER` |
| GOSI SANED Payable (Employer) | 2106 | CR | PayrollDeduction `GOSI_SANED_ER` |
| GOSI OccHazards Payable (Employer) | 2106 | CR | PayrollDeduction `GOSI_OCHAZARDS_ER` |
| Employer GOSI Expense (aggregate) | 5101 | DR | Sum of `*_ER` deductions |

The old `GosiEmployerRate` SystemSettings lookup has been removed. Employer expenses are computed from the actual per-employee `PayrollDeduction` records written during processing.

---

## Readiness Validation

`GosiReadinessValidator.Validate(employee, basicSalary, applicableRules)` returns:

### Blocking Issues (prevent GOSI deduction calculation)

| Code | Condition |
|---|---|
| `MISSING_GOSI_REFERENCE` | `employee.GosiReference` is blank |
| `MISSING_BASIC_SALARY` | `basicSalary` is null or ≤ 0 |

### Warnings (non-blocking)

| Code | Condition |
|---|---|
| `MISSING_NATIONALITY` | `employee.Nationality` is blank — defaults to NonSaudi |
| `GCC_RULES_PENDING_CONFIRMATION` | Employee classified as GCC — legal confirmation required |
| `NO_APPLICABLE_RULES` | No active rules found for the employee's classification |

---

## API Endpoints

| Method | Route | Permission | Description |
|---|---|---|---|
| `GET` | `/api/gosi/contribution-rules` | Any payroll role | List active rules (defaults + tenant overrides) |
| `POST` | `/api/gosi/contribution-rules` | `payroll.manage` | Create tenant-specific override rule |
| `DELETE` | `/api/gosi/contribution-rules/{id}` | `payroll.manage` | Deactivate a tenant override |
| `GET` | `/api/gosi/readiness-summary` | Any payroll role | Tenant-level readiness summary for all active employees |
| `GET` | `/api/gosi/employees/{id}/readiness` | Any payroll role | Per-employee readiness detail + contribution preview |
| `GET` | `/api/gosi/payroll-runs/{id}/contribution-summary` | Any payroll role | GOSI summary for a completed payroll run |
| `GET` | `/api/gosi/payroll-runs/{id}/variance-report` | Any payroll role | Reconciliation: actual vs. expected deductions |

### Payroll run GOSI summary response shape

```json
{
  "runId": "...",
  "period": "2026-06",
  "totalEmployeeContrib": 12750.00,
  "totalEmployerContrib": 16250.00,
  "totalGosi": 29000.00,
  "branchBreakdown": [
    { "componentCode": "GOSI_ANNUITIES_EMP", "totalAmount": 9000.00, "employeeCount": 10 },
    { "componentCode": "GOSI_SANED_EMP",     "totalAmount":  750.00, "employeeCount": 10 },
    { "componentCode": "GOSI_ANNUITIES_ER",  "totalAmount": 9000.00, "employeeCount": 10 },
    { "componentCode": "GOSI_SANED_ER",      "totalAmount":  750.00, "employeeCount": 10 },
    { "componentCode": "GOSI_OCHAZARDS_ER",  "totalAmount": 2000.00, "employeeCount": 10 }
  ]
}
```

---

## Audit Events

| Event | Trigger | Payload |
|---|---|---|
| `gosi.rule.created` | Tenant override rule created | `{ classification, branch, payer, rate, effectiveFrom }` |
| `gosi.rule.deactivated` | Tenant override deactivated | `{ classification, branch, payer }` |

No sensitive identifiers (Iqama, IBAN, GosiReference) appear in any audit payload.

---

## RBAC / Permission Model

| Action | Required Permission |
|---|---|
| View rules, readiness, summary, variance | Any payroll role (Admin, HR Manager, Payroll Manager, Payroll Officer) |
| Create/deactivate tenant override rules | `payroll.manage` |

All payroll and GOSI data is tenant-scoped. System default rules (TenantId == Guid.Empty) are loaded via `IgnoreQueryFilters()` but are read-only through the API (cannot be deactivated by tenants).

---

## Security Constraints

- All GOSI endpoints are authenticated (`[Authorize]`) and tenant-scoped.
- Global rules are read-only via API — tenants can only create/deactivate their own overrides.
- Audit payloads do not contain: full Iqama/National ID, GOSI reference number, IBAN, or raw salary figures.
- No cross-tenant access: `GosiController.LoadRulesAsync()` scopes to `TenantId == Guid.Empty || TenantId == currentTenant`.

---

## Manual QA Checklist

- [ ] Verify `GET /api/gosi/contribution-rules` returns 11 default rules on a fresh installation.
- [ ] Create a `POST /api/gosi/contribution-rules` override for Saudi/Annuities/Employee at 8.5%; verify the tenant-specific rate is used in readiness preview and next payroll run.
- [ ] Process a payroll run with Saudi employees; verify `PayrollDeductions` contains `GOSI_ANNUITIES_EMP` and `GOSI_SANED_EMP` records.
- [ ] Verify employer-side records (`GOSI_ANNUITIES_ER`, `GOSI_OCHAZARDS_ER`) are present but do NOT reduce net salary.
- [ ] Call `GET /api/gosi/payroll-runs/{id}/contribution-summary` and verify branch totals match expected rates.
- [ ] Call `GET /api/gosi/payroll-runs/{id}/variance-report` with an unmodified run; verify `withVariance == 0`.
- [ ] Remove an employee's GOSI reference; call `GET /api/gosi/employees/{id}/readiness`; verify `MISSING_GOSI_REFERENCE` blocking issue.
- [ ] Set an employee's nationality to "UAE"; call readiness; verify `GCC_RULES_PENDING_CONFIRMATION` warning.
- [ ] Verify `DELETE /api/gosi/contribution-rules/{id}` cannot deactivate a system default (TenantId == Guid.Empty).
- [ ] Call GOSI endpoints without authentication — verify 401. Call with a role lacking `payroll.manage` and attempt POST — verify 403.
- [ ] Verify `GET /api/payroll/runs/{id}/gl-entries` includes employer GOSI DR entry after payroll processing.

---

## Enterprise Benchmark & Gap Review

| Capability | ADP/UKG | Workday/SAP | Jisr/Bayzat (GCC) | Zayra (current) |
|---|---|---|---|---|
| Effective-dated GOSI rules | ✅ | ✅ | ✅ | ✅ |
| Branch separation (Annuities/SANED/OccHazards) | ✅ | ✅ | ✅ | ✅ |
| Employee + employer contribution tracking | ✅ | ✅ | ✅ | ✅ |
| Nationality-based classification | ✅ | ✅ | ✅ | ✅ |
| Wage cap enforcement | ✅ | ✅ | ✅ | ✅ (configurable via rules) |
| Tenant override rules | ✅ | ✅ | ⚠️ | ✅ |
| GOSI readiness check per employee | ✅ | ✅ | ✅ | ✅ |
| Variance / reconciliation report | ✅ | ✅ | ⚠️ Partial | ✅ |
| Live GOSI API / e-GOSI submission | ✅ | ✅ | ✅ | ❌ Pending |
| GOSI certificate of enrollment check | ✅ | ✅ | ⚠️ | ❌ Pending |

### P0 — Critical (blocks enterprise compliance)

| Gap | Notes |
|---|---|
| Live GOSI API (e-GOSI) submission | GOSI requires monthly contribution filing via e-GOSI portal. Currently file-based only. |
| Contributory wage min/max caps not pre-configured | Seeded rules have null caps — must be set to current GOSI thresholds (≈SAR 400/45,000). |

### P1 — Important

| Gap | Notes |
|---|---|
| GCC bilateral treaty rate confirmation | GCC rules marked pending — requires legal review per country. |
| GOSI payment reconciliation with bank | Contribution payment confirmation not yet linked to payroll run. |
| Annualized contribution projection report | Needed for budgeting; not yet available. |

### P2 — Enhancement

| Gap | Notes |
|---|---|
| GOSI contribution arrears calculation | Retroactive adjustments for missed months. |
| Voluntary GOSI coverage for high earners | Opt-in coverage above the SAR 45,000 cap. |
| Saudization (Nitaqat) contribution tracking | Link GOSI data to Nitaqat reporting for Saudi Aramco / Vision 2030 compliance. |
