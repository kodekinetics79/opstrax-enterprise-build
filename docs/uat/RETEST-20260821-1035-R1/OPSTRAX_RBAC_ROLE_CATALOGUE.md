# OpsTrax RBAC Role Catalogue

Run: `RETEST-20260821-1035-R1` · Status: **DRAFT — reflects code as measured pre-repair; the "post-repair" columns are filled in after Packet 2/5 land and tests execute.** Documentation policy: this catalogue records what executed tests and code enforce — never aspirational permissions.

## Source-of-truth architecture (principal finding)

Role definitions exist in THREE places that disagree:
1. **Backend runtime (authoritative for enforcement)**: `RolePermissionDefaults` in [EndpointMappings.cs:2174-2208](../../backend-dotnet/Controllers/EndpointMappings.cs#L2174) (22 roles); effective permissions resolved `users.role_id → roles.permissions_json ∪ role_permissions` → `users.permissions_json` → defaults (:2236-2273).
2. **DB seed**: `database/init/002_seed.sql:28-44` — 16 global rows with a *different* permission vocabulary (`fleet:view`, `driver:portal`, `pod:view` — tokens absent from the defaults).
3. **Frontend mirror (drives nav + landing)**: `rbacConfig.ts:452-494` (23 keys).
Name drift exists (`Read-Only Auditor` vs `Read-only Auditor`; `Finance/Billing User` vs `Finance & Billing Manager`). Reconciling the three sources is a tracked follow-up (NEW-R1 register), not attempted this run.

## Roles

Measured sidebar counts are pre-repair (broken alias closure). Post-repair values to be re-measured by Packet 2's blast-radius table.

| Role | User type | Purpose | Default landing (pre-repair → intended) | Modules (pre-repair count) | Explicitly prohibited | Data scope | Direct-URL behavior (intended) | Sensitive fields | Audit responsibility |
|---|---|---|---|---|---|---|---|---|---|
| Super Admin / Tenant Admin / Company Admin | internal | full tenant administration (`["*"]`) | /live-dashboard (correct) | 48 | none by design | own tenant via company_id + RLS | all internal routes allowed | license masked last-four (policy R1-G0-017) | owns audit review |
| Fleet Owner | internal | fleet oversight | /live-dashboard | 46 | user/role governance writes | tenant | /user-management denied post-repair | masked | — |
| Fleet Manager / Operations Manager | internal | operations + maintenance + safety | /live-dashboard | 45 | governance surfaces | tenant | /user-management denied | masked | — |
| Dispatcher | internal | dispatch board, routes, assignments | /live-dashboard | 42 | **Users & Roles, audit governance, settings** | tenant | **/user-management + /admin DENIED (DEF-025 fix)** | masked | — |
| Safety Manager | internal | safety events, coaching | /live-dashboard | 42 | governance | tenant | denied outside safety scope | masked | — |
| Maintenance Manager | internal | work orders, PM | /live-dashboard | 42 | governance | tenant | denied | masked | — |
| Read-Only Auditor | internal | read-everything, write-nothing | /live-dashboard | 48 | all writes (API-enforced) | tenant | read routes only | masked | audit consumer |
| Finance/Billing User | internal | finance modules | /live-dashboard | 40 | ops/governance | tenant | denied | masked | — |
| CRM & Sales Manager | internal | CRM | /live-dashboard | 16 | ops/governance | tenant | denied | — | — |
| Carrier Partner | external | carrier collaboration | /live-dashboard (misrouted pre-repair) | 39 → to be collapsed | internal governance + fleet ops | tenant subset | deny internal modules | none | — |
| Driver | external | driver portal only | **/driver (works — the one correct boundary)** | driver shell only | ALL internal modules | own records only (binding via drivers.user_id) | internal routes bounce to /driver | own DSAR only | — |
| **Customer Portal User / Customer / Customer Viewer** | external, customer-bound | portal: own shipments, feedback, invoices | **/live-dashboard + internal 35-module shell (DEF-024) → /customer-portal in CustomerLayout** | 35-37 → portal shell only | ALL internal modules incl. Control Tower (DEF-006) | own customer's rows only (users.customer_id binding) | internal deep links redirect to /customer-portal | none | — |
| Vendor Service Provider | external | vendor tasks | /live-dashboard (misrouted) | 36 → collapse | internal modules | scoped | deny | none | — |
| Mechanic / Compliance Manager / Customer Service (backend-only) | internal | no frontend key — falls to empty ROLE_PERMISSIONS | n/a | n/a | — | tenant | — | — | — |
| **Reseller / Partner Admin (backend-only)** | ? | **UNAUDITED WILDCARD `["*"]`** — flagged NEW-R1 follow-up | n/a | n/a | — | — | — | — | — |

## Enforcement layers (post-repair contract)

1. **Post-login destination**: identity-boundary-first ladder (driver:self → /driver; customer_portal:view → /customer-portal; dashboard:view → /live-dashboard; fallback **/login**, fail-closed).
2. **Route guards**: exact-match (`direct`) permissions on governance routes; customer routes in a dedicated CustomerLayout outside the internal shell.
3. **Navigation metadata**: filtered by directed permission match; module counts re-measured per role after repair.
4. **API + tenant boundary**: `RequirePermission`/`RequireAnyDirectPermission` reject customer-portal principals from internal endpoints; previously ungated Timeline/Recommendations/SimpleAction factories gated by Packet 5; company_id scoping + Postgres RLS.

## Known fail-opens being closed this run

`?? "Company Admin"` wildcard default (NEW-R1-02, Critical); sessionRouting terminal fallback; accessScope missing-identity returns-all-rows (F7); RequireFlag fallback=true (documented, server-gated). See OPSTRAX_DEFECT_REGISTER_DELTA.md.
