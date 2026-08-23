# OpsTrax Full Journey Test Plan

Run ID: `UAT-20260821-1035`  
Local build: `4653d2ec745004b16ea3eb644d4be66a72c10f07` with pre-existing telemetry worktree changes  
Certification target: staging frontend/API `979c142b3b0b228e7c84b88a37c2eacb66b76d38` (does not match local source)

## Objective

Execute the requested company-to-daily-operations journey in a dedicated, non-production OpsTrax staging tenant using visible Chrome evidence, API/database/runtime corroboration, negative and permission checks, and targeted defect remediation.

## Safety gates

1. Verify exact staging UI/API hosts and deployed SHAs.
2. Verify `/health/ready` and `/health/deep`, PostgreSQL migrations, restricted app/system identities, tenant RLS, critical workers, telemetry, and object storage.
3. Verify the staging tenant is disposable and contains no unrelated customer data.
4. Establish authenticated platform, tenant, driver, customer, auditor, and cross-tenant test personas without sharing credentials in chat.
5. Enable mutations only through the repository's staging mutation guard and acknowledgements.
6. Prefix created records with `UAT-20260821-1035`; never delete them without explicit approval.

## Controlled execution

| Gate | Scope | Current status |
|---:|---|---:|
| 0 | Environment truth and safety approval | BLOCKED |
| 1 | Company, users, roles, RBAC, tenant isolation | NOT IMPLEMENTED |
| 2 | Depots, drivers, vehicles, devices | NOT IMPLEMENTED |
| 3 | Assignments, dispatch, trips, telemetry, control tower | NOT IMPLEMENTED |
| 4 | Safety, maintenance, fuel/finance, reporting | NOT IMPLEMENTED |
| 5 | Negative, security, accessibility, responsive, performance, regression | NOT IMPLEMENTED |
| 6 | Illustrated manual, DOCX render inspection, PDF verification | NOT IMPLEMENTED |
| 7 | CTO release certification | NOT IMPLEMENTED |

The detailed scenario inventory is the user-approved phase specification attached to this run. Each scenario will receive an execution-ledger row before execution; no unexecuted item will be marked PASS.
