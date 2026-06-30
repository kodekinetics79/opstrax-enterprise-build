# OpsTrax Risk Register

| Risk | Impact | Likelihood | Mitigation |
|---|---|---|---|
| Legacy MySQL-era documentation drifts from code | Medium | High | Re-baseline docs on PostgreSQL source of truth |
| No formal migration framework | High | High | Add migrations after P0-A verification |
| Tenant isolation inconsistencies | High | Medium | Audit every tenant-owned table and query path |
| AI makes unsafe or opaque actions | High | Medium | Approval gates, audit logs, no direct table writes |
| IoT command abuse or replay | High | Medium | Signed ingestion, nonce validation, approval gating |

