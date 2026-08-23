# OpsTrax RBAC Retest Matrix

Run: `RETEST-20260821-1035-R1`. Two tiers: automated (source-contract + DB where marked) and live (Gate 7, tenant `T-C221B5BA` personas only). Status column filled as tests execute — nothing is marked PASS unexecuted.

## Automated tier

| # | Test | Defect | Kind | Status |
|---|---|---|---|---|
| N1 | No external role (customer*, driver*, carrier, vendor) satisfies dashboard:view / users:view / roles:view / audit:view / settings:view / telemetry.devices.read / ops:view | 024, 006 | contract script (P2) | PENDING |
| N2 | Alias graph directed: fleet:view ⇏ dashboard:view; reports:view ⇏ audit:view; shipments:view ⇏ telemetry.devices.read | 024, 025, 006 | contract script (P2) | PENDING |
| N3 | Landing routes table-driven over all role keys: customer_portal_user → /customer-portal, driver → /driver, internal → /live-dashboard, empty → /login | 024 | contract script (P2) | PENDING |
| N4 | Dispatcher deep-link /user-management + /admin → denied | 025 | contract (route guard direct) + live R12 | PENDING |
| N5 | Customer deep-link /live-dashboard, /telematics-control-tower, /user-management → redirect to /customer-portal, zero internal chrome | 024, 006 | live R11 + contract | PENDING |
| N6 | Every app.Map*("/api/…") handler carries RequirePermission/RequireAnyDirectPermission/RequireInternalUser (explicit public allowlist) | 006 | xUnit source contract (P5) | PENDING |
| N7 | Manipulated localStorage role/permissions overwritten by /api/auth/me revalidation; internal APIs still 403 | 024, 025 | live (Gate 7) | PENDING |
| N8 | Unauthenticated: all SPA routes → /login; all non-allowlisted /api/* → 401 | all | existing + N6 | PENDING |
| N9 | Pending/Suspended user session rejected (Program.cs u.status='Active') | 021 | xUnit | PENDING |
| N10 | Stale-session demotion stripped before first render | 024, 025 | live | PENDING |
| N11 | Cross-tenant timeline/recommendations/alerts probes return 403/404, zero rows | tenant | xUnit Postgres (P5) + live R-cross | PENDING |
| N12 | Audit tile count == audit list count after N mutations; audit_logs has severity+module_key | 020 | xUnit Postgres | PENDING |
| N13 | Role card userCount == active roster count after platform-path provisioning | 021 | xUnit Postgres | PENDING |
| N14 | Every INSERT INTO users supplies role_id; zero NULL role_id post-backfill | 021 | xUnit contract + stage87 | PENDING |

## Live tier (Gate 7) — personas of tenant T-C221B5BA

Persona checks R11 (customer landing + shell), R12 (dispatcher direct URL), R13 (driver dashboard 200), R14 (customer sees only UAT-1035-J01), plus cross-tenant negative via T-F1A74082 admin. Full 14-row matrix with evidence requirements: see OPSTRAX_LIVE_RETEST_REPORT.md (skeleton) / SDET matrix. Cases per instruction §8: Company Admin, Dispatcher, Fleet Manager, Driver, Customer Portal User, unauthenticated, inactive, stale-session, deep-link, manipulated-role, cross-tenant.
