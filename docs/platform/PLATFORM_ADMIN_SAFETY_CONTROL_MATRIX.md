# Platform Admin Safety and Pilot Control Matrix

Status: engineering review, 2026-08-02. This is a code-evidence control map, not a claim that the current runtime is production ready.

## Executive finding

OpsTrax has three distinct authorization layers: Platform Admin tenant controls, tenant RBAC, and branch/driver identity scope. They are complementary. Platform Admin entitlements are server enforced through an explicit tenant policy: migrated tenants retain `legacy_allow`, while tenants newly provisioned through the Platform API default to `package_allowlist` and deny missing module grants. Package reassignment now replaces only package-derived rights while preserving reviewed overrides and country/add-on grants.

Market packs are also deny-by-default and Platform Admin controlled. Their mutation path now validates tenant/catalog/status/price state, mirrors the effective entitlement atomically and writes a durable Platform audit; the resolved control is detailed below. Demo seeding and the telemetry simulator are deployment configuration, not Platform Admin controls. The only route-level feature-flag kill switch is `ai_copilot`, and feature flags are administered by tenant users with `users:manage`, not by Platform Admin.

## Control matrix

| Surface | Customer-visible routes | Platform Admin control | Tenant control | Server enforcement | Branch/persona scope | Current disposition |
|---|---|---|---|---|---|---|
| Incidents | `/incidents`; `/api/incidents/*` | `safety` entitlement; package may seed `safety` | Safety RBAC and workflow guards | Edge entitlement plus handler permissions | Tenant and branch predicates in incident handlers | Allowlist tenants require an enabled row; reviewed legacy tenants retain default-on compatibility |
| Driver coaching | `/coaching`, `/driver/coaching`; `/api/coaching/*`, `/api/driver/coaching/*` | `safety` entitlement | Safety RBAC; Driver self-only acknowledgment | Edge entitlement covers manager and driver paths | Driver portal derives driver identity; manager APIs use tenant/branch scope | Allowlist-denied unless enabled; legacy-compatible |
| Driver scorecards | `/driver-scorecards`; `/api/safety/*` | `safety` entitlement | `safety:view`; tenant safety rule configuration | Edge entitlement plus handler permissions | Scorecard queries join driver branch | Allowlist-denied unless enabled; no dedicated scorecard kill switch |
| DVIR | `/dvir-inspections`, `/driver/dvir`; `/api/dvir/*`, `/api/driver/dvir/*` | `maintenance` entitlement | Maintenance RBAC; Driver has narrow `driver:self` + `maintenance:create` | Edge entitlement covers manager and driver paths | Manager DVIR paths are tenant/branch scoped; driver identity is session-derived | Allowlist-denied unless Maintenance is enabled; grouped commercially under Maintenance, not Safety |
| HOS | `/hos-eld`, `/driver/hos`; `/api/hos/*`, `/api/driver/hos/*` | `compliance` entitlement | Compliance RBAC; driver certification is self-only | Edge entitlement covers manager and driver paths | HOS records are tenant/branch/driver scoped | Allowlist-denied unless Compliance is enabled |
| ELD device operations | HOS/ELD device tab; `/api/eld/*` | `telematics` entitlement | Compliance/telematics permissions by handler | Edge entitlement is `telematics` | Tenant/branch device scope | Split gate: HOS can be enabled while ELD APIs are disabled, producing a partial page |
| Dashcam and traffic violations | Existing module/API surfaces | `safety` entitlement | Safety/dashcam RBAC | Edge entitlement plus handler permission | Tenant/branch safety scope | Allowlist-denied unless Safety is enabled; not in the streamlined Safety navigation |
| Integrations | `/integrations`; `/api/integrations/*` | `integrations` entitlement and package option | `telematics:providers:manage` controls configure/test/sync/actions | Edge entitlement plus handler RBAC | Tenant-scoped connector rows | Allowlist-denied unless enabled; no per-provider Platform kill switch |
| Canada/NA and Saudi/GCC market packs | Regional compliance APIs and pages | Platform Revenue enable/disable | Tenant cannot self-enable | Deny-by-default `tenant_market_packs` checks | Tenant and branch scoped | Fail-closed read gate; mutation validates tenant/catalog pack and `active\|disabled`, atomically mirrors entitlement/metering, and writes actor plus before/after/reason to Platform audit |
| Packages and plans | Package assignment and module bundle list | Platform Packages/Tenants plus audited access-policy control | None | `package_allowlist` denies omitted modules; `legacy_allow` preserves compatibility | Tenant-wide | Restrictive for new tenants; existing tenants remain explicitly labeled legacy until reviewed conversion |
| Tenant lifecycle | Active, trial, suspended, cancelled; session revocation | Platform Tenants | None | Login checks company status; suspend/cancel revoke sessions | Tenant-wide | Effective emergency tenant kill switch |
| Roles and grants | User/role administration | Platform creates tenant/admin and can revoke tenant sessions | Tenant admins manage roles/users | Middleware resolves authoritative role grants; handlers enforce permissions | User and branch binding | Platform has no read-only effective-access preview for a selected persona |
| Feature flags | Tenant `/feature-flags` | No Platform UI/API | Tenant `users:manage` can create/update/delete | Only mapped/consumed flags affect behavior | Per tenant/user deterministic rollout | UI overstates generality: arbitrary flags are inert; only `ai_copilot` is route-gated and `pod_media_capture` is directly consumed |
| AI emergency stop | `/api/ai/*` | No Platform control | Tenant-admin `ai_copilot` flag | Route-level 403 | Tenant/user rollout | Tenant-local only; does not cover stored recommendation routes outside `/api/ai/*` |
| Demo tenant seed | Startup demo seeder | Deployment configuration only | None | Production readiness reports failure when enabled | Seeder-selected tenants | Not remotely controllable; safe default in compose is false |
| Legacy batch demo seed | Batch schema synthetic mutations | Deployment configuration only | None | Explicit opt-in gate, no environment default | Historically cross-tenant mutation | Must remain disabled outside isolated demo DBs |
| Telemetry simulator | Background simulated positions | Deployment configuration only | None | Production readiness reports failure when enabled | Cross-tenant system worker | No Platform kill switch; restart/config change required |

## Code evidence

- Session, authoritative role resolution, branch binding, entitlement evaluation, and feature-flag evaluation: the authenticated middleware branch and `EntitlementModuleForPath` in `backend-dotnet/Program.cs`.
- Safety/maintenance/telematics/compliance/integrations path ownership: `EntitlementModuleForPath` in `backend-dotnet/Program.cs`.
- Platform Admin entitlement catalog, policy warning/confirmation, and inherited-state display: `frontend/src/pages/platform/PlatformTenantsPage.tsx`.
- Package module options: `frontend/src/pages/platform/PlatformPackagesPage.tsx`.
- Policy-aware module semantics and uncapped limits: `backend-dotnet/Services/EntitlementService.cs`.
- Tenant feature-flag administration and `users:manage` guard: the Feature Flags endpoint block in `backend-dotnet/Controllers/EndpointMappings.cs`.
- Only route-level flag mapping (`/api/ai/*`): `FeatureFlagForPath` in `backend-dotnet/Program.cs`; direct POD-media consumption is in `backend-dotnet/Controllers/EndpointMappings.cs`.
- Persona grants, including Driver isolation and Safety/Fleet/Maintenance roles: `RolePermissionDefaults` and demo fixture role grants in `backend-dotnet/Controllers/EndpointMappings.cs` and `backend-dotnet/Services/DemoTenantSeeder.cs`.
- Login, `/api/auth/me`, and refresh return the authoritative policy mode plus explicit entitlement snapshot via `ResolveAuthEntitlementsAsync` in `backend-dotnet/Controllers/EndpointMappings.cs`; `moduleAllowedByEntitlement` in `frontend/src/layouts/AppShell.tsx` filters navigation and renders a deep-link plan boundary.
- Market-pack Platform permissions and entitlement mirroring: `backend-dotnet/Controllers/MarketPackEndpoints.cs`.
- Demo/simulator production readiness checks: `backend-dotnet/Services/ConfigValidationService.cs`; explicit legacy seed gate: `backend-dotnet/Services/DemoSeedGate.cs`.
- Tenant lifecycle status and session revocation: tenant status handlers in `backend-dotnet/Controllers/PlatformEndpoints.cs`; login company-status checks in `backend-dotnet/Controllers/EndpointMappings.cs`.
- Policy/navigation contracts: `backend-dotnet.Tests/EntitlementPolicyModePostgresTests.cs`, `backend-dotnet.Tests/EntitlementAwareNavigationTests.cs`, `backend-dotnet.Tests/PlatformControlPlaneTests.cs`, and `backend-dotnet.Tests/PlatformSafetyControlPlaneContractTests.cs`.
- Golden Safety fixture/version/persona assertions: `backend-dotnet/Services/DemoTenantSeeder.cs` and `backend-dotnet.Tests/DemoTenantSeederPostgresTests.cs`.

## Prioritized gaps

### P0 — commercial isolation policy delivered; conversion is controlled

The additive `companies.entitlement_policy_mode` migration preserves every existing tenant as `legacy_allow`. Platform API tenant provisioning explicitly writes `package_allowlist`, where a missing module row is denied at the API edge and in the reusable entitlement evaluator. A new Platform-provisioned trial with no package therefore receives no governed core modules unless explicitly granted. The database-column default remains `legacy_allow` for compatibility, so any approved out-of-band provisioning procedure must set the policy explicitly and is not a substitute for the audited Platform API.

Platform Admin exposes a separately permissioned, confirmed and audited policy transition. Transition and package reassignment reconcile the current package's derived rows atomically, remove stale package-derived access, and preserve explicit overrides and country/add-on grants. Existing tenants must still be reviewed and intentionally converted; the migration never silently removes their access.

### Delivered — customer UI consumes evaluated entitlement state

Login, `/api/auth/me`, and refresh return `entitlementPolicyMode` and explicit entitlement rows. Customer navigation composes those values with RBAC, and a disabled deep link renders a consistent “Not included in your plan” boundary while server enforcement remains authoritative.

Local rendered-browser UAT has proved a Safety disable/hidden-navigation/direct-deep-link/API-403/re-enable/restoration sequence. Release acceptance still requires exporting that sequence, its audit references and the complete before/after control snapshot as immutable evidence from the exact candidate. Entitlement changes do not push into an already-rendered session; the server denies immediately, while the UI snapshot refreshes through the normal auth revalidation lifecycle.

### P1 — no Platform Admin operational kill-switch console

Feature flags are tenant-admin controlled. Platform Admin cannot centrally stop AI, POD media, connector sync/actions, Safety mutations, outbound notifications, or simulator/demo services. Arbitrary flags created in the tenant UI have no effect unless code explicitly consumes them.

Required delivery: a registry-backed Platform Controls page showing owner, scope, default, consumer routes/jobs, last change, reason, expiry, and audit record. Separate customer-configurable flags from operator emergency controls. High-risk controls need dual confirmation and automatic expiry/review.

### P1 — HOS/ELD has split commercial ownership

The same customer page calls HOS APIs gated by `compliance` and ELD APIs gated by `telematics`. Platform Admin can create a partially functioning page. Define a product dependency (`hos_eld` requires both) or split the UI into separately entitled modules with explicit degraded states.

### P1 — configuration-only synthetic-data controls

Demo seed and telemetry simulation are cross-tenant deployment settings. They correctly fail production readiness, but Platform Admin cannot observe or stop them. Surface their effective state read-only in Platform Health and require deployment automation for mutation. Never permit a browser toggle to start cross-tenant synthetic mutation in production.

### Resolved P1 — market-pack mutation validation/audit invariants

`PlatformSetTenantMarketPack` now accepts only `active|disabled`, rejects unknown tenants/catalog packs, inactive catalog activation and invalid prices, serializes concurrent changes on the tenant, and applies the assignment, mirrored entitlement and immutable `tenant.market_pack.changed` Platform audit in one system transaction. Metering remains explicitly best-effort and is not treated as the audit record. The audit captures the authenticated Platform actor, optional operator reason and redacted before/after state. Stage 69 adds the same closed status enum at the database boundary and intentionally fails migration when historical invalid state needs operator review; `MarketPackPlatformControlPostgresTests` proves authorization, negative validation, mirroring and audit persistence against PostgreSQL.

### P2 — effective-access evidence improved; interactive decision preview remains

The version-2 audited control snapshot now exports effective permission grants for every applicable global/tenant role and every user-to-branch binding. User identities are tenant-scoped opaque references; names, emails and raw user IDs are excluded, while each binding carries a digest/count of the permissions the shared runtime resolver actually grants. This closes the evidence gap but is not yet an interactive “what will this Driver/Dispatcher/Safety Manager at Branch X see and mutate?” evaluator. That future evaluator should compose lifecycle, package, entitlement, market pack, feature flag, role grant, branch, integration readiness, and environment control into one explainable decision trace.

## Enterprise acceptance controls

Before a customer pilot, capture an exported control snapshot for the demo tenant containing tenant lifecycle, package policy mode, all entitlements, market packs, feature/operator flags, persona grants, branch bindings, integration status, demo/simulator state, environment, RLS posture, and audit event IDs. Recompute the snapshot after the rehearsal and require no unexplained drift.

The version-2 snapshot derives its 91/45/46 ownership counts and per-entitlement totals from a complete server catalog reconciled by tests with the SPA catalog. It includes effective role grants and pseudonymous user-to-branch bindings, excludes actor email from recent Platform audit, and provides a stable semantic SHA-256 profile that omits capture time, rolling audit rows and operational timestamps. The Platform UI retains only the prior digests in session storage and reports “no drift” or “drift detected” on the next capture; raw evidence hashes remain unique and independently auditable. Its environment section still reports configuration posture, not runtime DB-role/RLS, edge or deployed-digest proof, so those acceptance artifacts remain separately required.

## Composite and degraded customer surfaces

`requiredEntitlement` intentionally represents one commercial owner. Canonical pages whose primary APIs have one owner now match the server edge map (Dispatch trips/routes/POD, Telematics live map/geofences/device surfaces, Safety dashcam/violations/evidence, CRM, Reports). The following composite pages must not be mislabeled with a single module until the product dependency is explicit:

- HOS / ELD primarily requires `compliance`, while device operations call `telematics`; the page must present an explicit device-operations unavailable state when Telematics is absent.
- Control Tower composes Dispatch trips with Telematics alerts and live state. It currently degrades per widget; a future bundle/dependency must decide whether both entitlements are mandatory.
- Fleet Overview combines ungated fleet registry, Dispatch job summary, and cross-domain alerts. It must retain widget-level unavailable states rather than hide the whole operational overview behind one module.
- Live Map is commercially owned by Telematics, but route overlays call Dispatch routes. With Dispatch disabled the live telemetry map remains valid and the route overlay must degrade explicitly.
- Operational Proof Center composes execution, access, dispatch recommendation, proof, and billing-confidence APIs under its own permission family. It is not assigned to Dispatch by inference; commercial ownership needs a product decision before a restrictive gate is added.
