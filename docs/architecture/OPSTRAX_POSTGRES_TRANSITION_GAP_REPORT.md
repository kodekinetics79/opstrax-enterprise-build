# OpsTrax PostgreSQL Transition Gap Report

| Item | Current State | PostgreSQL Target | Gap | Risk | Recommended Fix | P0/P1/P2 |
|---|---|---|---|---|---|---|
| DB provider | PostgreSQL active in `backend-dotnet/` and `backend/` | PostgreSQL only | Legacy docs/config still mention MySQL | Medium | Re-baseline docs and examples | P0 |
| ORM/migrations | Startup schema bootstrap services | Formal versioned migrations | No migration ledger or rollback discipline | High | Introduce local migration framework | P0 |
| Seed handling | Seed data embedded in schema services and in-memory demo paths | Explicit seed/dev separation | Demo/data boundary not formalized everywhere | Medium | Document dev-only seed policy | P1 |
| Test DB | Tests run without a dedicated formal migration harness | Dedicated local test DB path | Hard to verify migration drift | Medium | Add test DB workflow in runbook | P1 |
| Startup behavior | Schema bootstrap runs during app startup | Startup migration gating | Auto-bootstrap is not clearly environment-gated | High | Gate production changes behind explicit approval | P0 |
| Rollback | No canonical rollback strategy file | Expand-and-contract plus backups | Rollback path not formalized | High | Create rollback strategy and review checklist | P0 |

