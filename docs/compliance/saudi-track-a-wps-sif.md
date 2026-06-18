# Saudi Track A — WPS/SIF Payroll Compliance

**PR:** Track A PR-2 — WPS/SIF Payroll Compliance  
**Status:** Implemented (validator, SIF generator, lifecycle enforcement, RBAC, IBAN masking, tests).

---

## Module Scope

| Area | Status |
|---|---|
| WPS export eligibility validation (`WpsSifValidator`) | ✅ |
| Isolated SIF file generator (`SifFileGenerator`, format `SIF_SA_V1`) | ✅ |
| WPS lifecycle transition enforcement (`WpsTransitions`) | ✅ |
| `payroll.export` permission check on all WPS endpoints | ✅ |
| IBAN masking for non-export roles | ✅ |
| Re-export blocking (idempotency guard) | ✅ |
| WPSFileBatch metadata: hash, format version, employee count, total, generated-by | ✅ |
| WPS export history endpoint | ✅ |
| 28 new tests (173 total passing) | ✅ |
| GOSI | ❌ Out of scope for this PR |

---

## Payroll Export Eligibility Rules

A payroll run **must not be exportable** unless ALL of these conditions are met:

| # | Rule | Error Code | Scope |
|---|---|---|---|
| 1 | Payroll run status is `Approved`, `Locked`, or `Paid` | `RUN_NOT_APPROVED` | Run |
| 2 | At least one payroll slip exists | `NO_PAYROLL_ROWS` | Run |
| 3 | Run `TotalNetSalary` matches sum of payslip `NetSalary` (±0.01) | `TOTAL_MISMATCH` | Run |
| 4 | Employee is not a duplicate within the same run | `DUPLICATE_EMPLOYEE` | Employee |
| 5 | Employee `NetSalary > 0` | `INVALID_NET_SALARY` | Employee |
| 6 | Employee `GrossSalary >= 0` | `NEGATIVE_GROSS_SALARY` | Employee |
| 7 | Employee has an IBAN on their payroll profile | `MISSING_IBAN` | Bank |
| 8 | IBAN passes ISO 13616 mod-97 check | `INVALID_IBAN` | Bank |
| 9 | Employee has a government `IdNumber` | `MISSING_ID_NUMBER` | Employee |

### Non-blocking warnings (do not block export)

| Warning Code | Condition |
|---|---|
| `NON_SAUDI_IBAN` | IBAN is valid but does not start with `SA` |
| `INACTIVE_EMPLOYEE` | Employee status is `Archived`, `Offboarded`, or `Terminated` |
| `EMPLOYEE_NOT_IN_MASTER` | Payslip exists but no master Employee record found |

Direct API calls to `/api/payroll/payment-batches/{id}/wps-file` re-run the full validator server-side; the frontend validation result is advisory only.

---

## SIF Generator Design

**Class:** `SifFileGenerator` (`Infrastructure/Payroll/SifFileGenerator.cs`)  
**Format version:** `SIF_SA_V1`

### Algorithm

1. Sort records by `EmployeeCode` for deterministic order.
2. Write header: `EDI_DC40+<agentId>+<molCode>+<paymentDate>+<count>+<total>+<currency>'`
3. Write one detail line per record: `E1EDL20+<empCode>+<iban>+<netPay>+<currency>+<paymentDate>+<seq>'`
4. Write trailer: `EOF+<count>+<total>'`
5. Compute SHA-256 of full UTF-8 file content → stored on `WPSFileBatch.FileHash`.

Same input → same bytes → same hash (idempotent, verifiable).

### Metadata stored on `WPSFileBatch`

| Field | Description |
|---|---|
| `FormatVersion` | `"SIF_SA_V1"` — version tag for future format evolution |
| `FileHash` | SHA-256 hex digest of the generated file |
| `EmployeeCount` | Number of employee records in the SIF file |
| `TotalSalaryAmount` | Sum of `NetPay` across all SIF records |
| `GeneratedByUserId` | User ID who triggered the generation |

### Known Assumptions

> ⚠️ **Production layout is PENDING CONFIRMATION.**
>
> The current SIF format follows the CBUAE SIF v2 segment structure (EDI_DC40, E1EDL20, EOF) which is widely used across GCC payroll systems. The Saudi Ministry of Human Resources & Social Development (MHRSD) and Mudad WPS may have different or additional requirements including:
> - Specific Ministry of Labor (MOL) establishment registration codes
> - Additional mandatory fields (employee nationality code, bank branch code, bank SWIFT/BIC)
> - Different character encoding (EBCDIC vs UTF-8)
> - Different field widths or segment separators
> - Mudad API submission format vs. file-based upload
>
> **Before submitting to a production Mudad gateway, validate the generated file against the current "WPS Technical Specification" published by MHRSD/Mudad, or run an acceptance test through the Mudad sandbox environment.**

---

## Validator Rules (Full Reference)

Implemented in `WpsSifValidator.Validate(run, slips, profiles, employees)`.

```
Run-level:
  RUN_NOT_APPROVED    — run.Status not in {Approved, Locked, Paid}
  NO_PAYROLL_ROWS     — slips.Count == 0
  TOTAL_MISMATCH      — |run.TotalNetSalary - sum(slip.NetSalary)| > 0.01

Employee-level (per slip):
  DUPLICATE_EMPLOYEE  — same EmployeeId appears more than once
  INVALID_NET_SALARY  — slip.NetSalary <= 0
  NEGATIVE_GROSS_SALARY — slip.GrossSalary < 0
  MISSING_IBAN        — profile.Iban is null or whitespace
  INVALID_IBAN        — IbanValidator.IsValid(iban) == false
  MISSING_ID_NUMBER   — emp.IdNumber is null or whitespace

Warnings (non-blocking):
  NON_SAUDI_IBAN      — valid IBAN but not starting with "SA"
  INACTIVE_EMPLOYEE   — emp.Status in {Archived, Offboarded, Terminated}
  EMPLOYEE_NOT_IN_MASTER — no Employee record matches slip.EmployeeId
```

---

## WPS Lifecycle / Status Transitions

```
Draft
  └─▶ Generated     (after SIF file generation)
        ├─▶ Downloaded  (after file download)
        │     └─▶ Submitted
        └─▶ Submitted   (direct if no download tracking needed)
                 ├─▶ Accepted
                 │     └─▶ Reconciled (terminal)
                 └─▶ Rejected
                       └─▶ Generated  (re-export after correction)
```

**Rules:**
- Any transition not in the matrix above is rejected with `400 invalid_transition`.
- `Reconciled` is a terminal state — no further transitions allowed.
- Re-export after `Rejected` creates a **new** `WPSFileBatch` record (the old one is preserved for audit).
- Generating a second file while a batch is not in `Rejected` state returns `409 already_generated`.

---

## IBAN Masking / Security Rules

| Role | `payroll.export` permission | IBAN returned by `GET /payment-batches/{id}/records` |
|---|---|---|
| Payroll Manager | ✅ | Full IBAN (e.g. `SA0380000000608010167519`) |
| Finance Approver | ✅ | Full IBAN |
| Payroll Officer | ❌ | Masked (e.g. `SA03**************7519`) |
| HR Manager | ❌ | Masked |
| Employee (ESS) | ❌ | Masked |

**Rules:**
- `SifFileGenerator.MaskIban()` exposes first 4 + last 4 characters; all middle characters are replaced with `*`.
- Full IBANs are **never written** to audit log metadata — only `batchId` and `fileHash` appear in `payroll.wps.generated`.
- `SIFFileRecord.Iban` stores the full IBAN in the database (needed for SIF generation), but it is never returned in general list endpoints.

---

## RBAC / Permission Model

| Action | Required Permission |
|---|---|
| Create payment batch | `payroll.export` |
| Run WPS validation | `payroll.export` |
| Generate SIF file | `payroll.export` |
| Download SIF file | `payroll.export` |
| Update WPS status | `payroll.export` |
| View WPS export history | `payroll.export` |
| View payment records (masked IBAN) | Standard payroll role |
| View payment records (full IBAN) | `payroll.export` |

Roles that carry `payroll.export`: Admin, Payroll Manager, Finance Approver, Finance Controller.

---

## Audit Events

| Event | Trigger |
|---|---|
| `payroll.payment_batch.created` | Payment batch created for run |
| `payroll.wps.generated` | SIF file generated — includes `employeeCount`, `totalAmount`, `fileHash`, `formatVersion` |
| `payroll.wps.downloaded` | SIF file downloaded — includes `fileHash` |
| `payroll.wps.status_changed` | Status transition — includes `from`, `to`, `notes` |

**Security note:** No full IBAN values appear in any audit payload. Only metadata (count, total, hash) is recorded.

---

## API Endpoints

| Method | Route | Permission | Description |
|---|---|---|---|
| `POST` | `/api/payroll/runs/{id}/payment-batches` | `payroll.export` | Create WPS payment batch |
| `POST` | `/api/payroll/runs/{id}/wps-validation` | `payroll.export` | Pre-export validation |
| `POST` | `/api/payroll/payment-batches/{id}/wps-file` | `payroll.export` | Generate SIF file |
| `GET` | `/api/payroll/payment-batches/{id}/wps-file/download` | `payroll.export` | Download SIF file |
| `POST` | `/api/payroll/payment-batches/{id}/wps-status` | `payroll.export` | Update WPS status (with transition enforcement) |
| `GET` | `/api/payroll/payment-batches/{id}/wps-export-history` | `payroll.export` | Export history for a batch |
| `GET` | `/api/payroll/payment-batches/{id}/records` | Any payroll role | Payment records (IBAN masked if no export permission) |
| `GET` | `/api/payroll/payment-batches` | Any payroll role | List payment batches |

---

## Export Versioning Behavior

- Each `WPSFileBatch` record is **immutable** after creation.
- Re-generation after `Rejected` creates a new `WPSFileBatch` row (the old record is preserved).
- The `GET /wps-export-history` endpoint returns all `WPSFileBatch` records for a batch, ordered by `CreatedAtUtc` descending.
- `FormatVersion` on each record allows tracking which format was used for each export.

---

## Manual QA Checklist

- [ ] Create payroll run, process it, approve it through Finance, lock it.
- [ ] Attempt WPS validation before approval — verify `RUN_NOT_APPROVED` error.
- [ ] Attempt WPS validation for approved run with missing IBAN — verify `MISSING_IBAN` error.
- [ ] Attempt WPS validation for approved run with invalid IBAN — verify `INVALID_IBAN` error.
- [ ] Attempt WPS validation for approved run with all fields complete — verify `canExport: true`.
- [ ] Call `POST /wps-file` — verify `WPSFileBatch` record created with `FileHash`, `FormatVersion`, `EmployeeCount`.
- [ ] Call `POST /wps-file` again — verify `409 already_generated`.
- [ ] Download file — verify EDI_DC40 header, E1EDL20 rows, EOF trailer, UTF-8 encoding.
- [ ] Verify same download produces identical bytes (determinism).
- [ ] Call `POST /wps-status` with `Submitted` from `Draft` — verify `400 invalid_transition`.
- [ ] Call `POST /wps-status` with `Downloaded` from `Generated` — verify `200 OK`.
- [ ] Call `GET /payment-batches/{id}/records` as Payroll Officer — verify IBAN is masked.
- [ ] Call `GET /payment-batches/{id}/records` as Payroll Manager — verify full IBAN.
- [ ] Call WPS export endpoints as unauthorized role — verify `403 Forbidden`.
- [ ] Call WPS export endpoints for another tenant's batch — verify `404 Not Found`.

---

## Enterprise Benchmark & Gap Review

### What competitors typically provide

| Capability | ADP/UKG | Workday/SAP | Jisr/Bayzat (GCC) | Zayra (current) |
|---|---|---|---|---|
| Payroll run approval workflow | ✅ Multi-level | ✅ Configurable | ✅ Basic | ✅ 2-level SoD |
| WPS/SIF file generation | ✅ Certified | ✅ Certified | ✅ Mudad-certified | ⚠️ Not yet certified |
| IBAN validation | ✅ | ✅ | ✅ | ✅ ISO 13616 |
| IBAN masking for non-payroll roles | ✅ | ✅ | ✅ | ✅ |
| Lifecycle enforcement | ✅ | ✅ | ✅ | ✅ |
| Deterministic file hash / integrity | ✅ | ✅ | ⚠️ Partial | ✅ SHA-256 |
| Mudad API submission | ✅ | ✅ | ✅ Direct API | ❌ File-based only |
| GOSI deduction integration | ✅ | ✅ | ✅ | ❌ Pending PR-3 |
| WPS reconciliation with bank | ✅ | ✅ | ✅ | ⚠️ Manual status update only |
| Multi-payroll-run WPS batching | ✅ | ✅ | ⚠️ | ❌ One run = one batch |
| Audit trail | ✅ | ✅ | ✅ | ✅ Per-event |

### P0 — Critical (blocks enterprise use)

| Gap | Priority | Notes |
|---|---|---|
| Mudad API direct submission | P0 | Currently file-based only; production WPS requires Mudad API |
| Saudi Mudad file layout certification | P0 | SIF_SA_V1 is self-labeled; pending Mudad acceptance test |
| GOSI deduction calculation | P0 | Required for legally-compliant payslips (Track A PR-3) |

### P1 — Important (limits enterprise adoption)

| Gap | Priority | Notes |
|---|---|---|
| WPS submission status sync from Mudad | P1 | Status currently updated manually; Mudad provides callbacks |
| Multi-run WPS batch (partial months) | P1 | Current model is 1 run = 1 batch |
| WPS reconciliation against bank confirmation | P1 | Post-Accepted reconciliation is manual |

### P2 — Enhancement (nice to have)

| Gap | Priority | Notes |
|---|---|---|
| SIF format version 2 selector | P2 | Add `FormatVersion` config per tenant |
| WPS partial export (selected employees) | P2 | Currently all-or-nothing |
| SIF file preview before download | P2 | Show employee rows before committing |
| Bulk status import from Mudad response file | P2 | Parse Mudad response XML/CSV |

### Assessment

**Demo-grade or Enterprise-grade?**

With Track A PR-2 delivered, the WPS/SIF module is **strong demo-grade and approaching enterprise-grade** for core payroll export governance:

- ✅ Approval-gate enforced server-side
- ✅ Full validator with 9 blocking rules
- ✅ Deterministic, versioned SIF generator
- ✅ IBAN protection and masking
- ✅ Lifecycle transition enforcement
- ✅ Full audit trail
- ❌ Not yet Mudad-certified (pending layout confirmation)
- ❌ No Mudad API integration (file-based only)
- ❌ GOSI deductions missing (Track A PR-3)

Comparison: Jisr/Bayzat are Mudad-certified and have direct API submission. Zayra's governance model (validator, SoD, audit) is comparable or stronger — the key gap is Mudad certification and direct API submission, which are Track A PR-4 scope items.
