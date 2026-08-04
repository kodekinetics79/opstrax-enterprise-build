# OpsTrax Enterprise Platform Control Map

**Authority:** this is the release control map for tenant-visible capability.
**Audited:** 2026-08-02 against `moduleConfig.ts`, `AppShell.tsx`, `App.tsx`, `Program.cs`, Platform endpoints and tenant settings handlers.

## Decision model

A successful tenant action is the intersection of these controls:

1. tenant lifecycle and a valid tenant session;
2. Platform commercial policy (`package_allowlist` or reviewed `legacy_allow`);
3. Platform package, explicit entitlement override, country default and market-pack assignment;
4. tenant role permission and customer/Driver/internal-user boundary;
5. branch or self scope in the handler;
6. tenant feature flag or deployment control where the feature consumes one.

Platform staff control the commercial envelope. Tenant administrators control people, operational configuration and rollout inside that envelope. Customer users receive no control-plane rights: they can use only their role-, branch-, customer- or self-scoped workflows. A platform token is not a tenant super-token.

## Platform-commercially controlled catalog

These 45 tenant UI modules declare a `requiredEntitlement`. Navigation and direct deep links use the authenticated entitlement snapshot. The API middleware independently maps the owned API prefixes shown below. For `package_allowlist`, a missing or disabled row denies; for reviewed `legacy_allow`, only an explicit disabled row denies.

| Entitlement | Tenant-visible modules | Server API edge ownership | Platform mutation / audit |
|---|---|---|---|
| `telematics` | `map-view`, `fleet-live-wall`, `geofences`, `iot-devices`, `telematics-control-tower`, `gps-tracking`, `obd-j1939`, `cold-chain`, `sensor-health` | `/api/telemetry`, `/api/devices`, `/api/eld`, `/api/geofences` | package or entitlement override; audited |
| `safety` | `dashcam`, `driver-scorecards`, `safety-center`, `incidents`, `coaching`, `traffic-violations`, `evidence-packages` | `/api/safety`, `/api/dashcam`, `/api/incidents`, `/api/coaching`, `/api/traffic-violations`, `/api/evidence-packages`, `/api/driver/coaching` | package or override; audited |
| `maintenance` | `dvir-inspections`, `work-orders`, `maintenance-center`, `service-history`, `downtime`, `preventive-maintenance` | `/api/preventive-maintenance`, `/api/maintenance`, `/api/work-orders`, `/api/workorders`, `/api/service-history`, `/api/downtime`, `/api/dvir`, `/api/driver/dvir` | package or override; audited |
| `dispatch` | `dispatch-board`, `jobs`, `trips`, `route-plans`, `proof-of-delivery`, `last-mile-delivery` | `/api/dispatch`, `/api/jobs`, `/api/trips`, `/api/routes`, `/api/smart-assign`, `/api/last-mile`, `/api/proof-of-delivery`, compatibility `/api/route-planning` | package or override; audited |
| `crm` | `leads`, `sales-pipeline`, `opportunities`, `campaigns`, `customers`, `contracts`, `rate-cards`, `price-simulation`, `quotations` | `/api/customers`, `/api/contracts`, `/api/leads`, `/api/opportunities`, `/api/campaigns`, `/api/quotations`, `/api/rate-cards`, compatibility `/api/contracts-rates` | package or override; audited |
| `customer_portal` | `customer-eta`, `customer-portal`, `customer-visibility` | `/api/portal`, `/api/customer-eta`, `/api/customer-visibility`, compatibility `/api/customer-portal` | package or override; audited; customer identity remains handler-scoped |
| `compliance` | `fleet-compliance`, `hos-eld`, `compliance-center` | `/api/fleet-compliance`, `/api/compliance`, `/api/hos`, `/api/driver/hos`, compatibility `/api/hos-eld` | package/country/override; audited |
| `reports` | `reports-analytics` | `/api/reports`, `/api/analytics`, compatibility `/api/reports-analytics` | package or override; audited |
| `integrations` | `integrations` | `/api/integrations` | Platform entitlement plus tenant connector-management RBAC; both mutation planes audited |

### Market-pack overlay

Canada/NA and Saudi/GCC market packs are separately deny-by-default regardless of `legacy_allow`. Platform Revenue accepts only `active|disabled`, validates tenant and catalog, captures operator reason, atomically mirrors its entitlement and writes immutable before/after actor audit. Regional handlers also call the pack check. Stage 69 enforces the status enum in PostgreSQL.

## Tenant-governed core/open catalog

The following 46 configured modules have **no Platform commercial entitlement**. They are not anonymous: tenant authentication, RBAC, branch/self/customer predicates, country/feature/deployment conditions and handler validation still apply. Package omission alone cannot disable their UI or API. “Included core/open” therefore means **not selectively gated by the current Platform package/entitlement mechanism**; it is an enforcement classification, not proof of contractual SKU inclusion, production completeness, successful third-party integration, or unlimited usage.

| Family | Modules | Control owner |
|---|---|---|
| Operations | `command-center`, `fleet-health`, `live-dashboard`, `alerts`, `active-shipments`, `control-tower` | tenant RBAC; deployment health for infrastructure |
| CRM adjunct | `account-health`, `follow-ups`, `support-tickets`, `renewals`, `upsell-opportunities` | tenant CRM/customer permissions; not commercially gated |
| Execution adjunct | `load-bookings`, `shipments`, `operations-proof-center`, `logistics-workspace`, `driver-messaging`, `workforce` | tenant dispatch/shipment/operations permissions |
| Fleet core | `fleet-utilization`, `fleet-workspace`, `fleet-cold-chain`, `fleet-assets`, `fleet-saudi-readiness`, `vehicles`, `drivers`, `owners`, `assignments`, `documents` | tenant fleet/dispatch/compliance RBAC; Saudi page also country/pack API checks |
| Financial core | `fuel-idling`, `expenses`, `invoices`, `ar-aging`, `payments`, `profitability`, `tax-config`, `billing-consolidation`, `driver-pay`, `revenue-recognition` | tenant finance/tax/billing/settlement RBAC |
| Governance | `user-management`, `audit-logs`, `alert-rules`, `feature-flags`, `about` | tenant admin/auditor permissions |
| Intelligence/forms | `carbon-tracking`, `digital-forms`, `ai-copilot`, `predictive-analytics` | tenant reports/safety RBAC; AI also consumes tenant `ai_copilot` flag |

“Core/open” is a commercial statement, not a security statement. These routes still require a tenant session and permissions.
The shared `/api/foundation/safety-maintenance/*` dashboard summaries are also included core and require tenant `dashboard:view`; they are deliberately not assigned a phantom Platform entitlement.

The former generic `/api/modules/{moduleKey}` surface is not routable. It was not consumed by any current tenant route and could otherwise become an entitlement/RBAC bypass through caller-selected record buckets. Supported tenant pages use canonical handlers. The entitlement edge nevertheless resolves catalogued `/api/modules/{moduleKey}` paths defensively, so a future reintroduction cannot silently bypass the Platform commercial envelope. Legacy dedicated module roots now have explicit read and write permission maps; a newly registered root without both mappings fails closed during application startup.

The compatibility Traffic Violations, Service History, Downtime and Preventive Maintenance reads now enforce their documented `safety:view` or `maintenance:view` permission and strict branch ownership. Because legacy maintenance rows do not carry a native branch, their assigned vehicle is the authoritative branch owner; branch users do not receive unassigned/tenant-level rows. Reports catalog, run-history and scheduled-report reads require `reports:view` in addition to the Platform `reports` entitlement.

## Settings and override ownership

| Control | Platform Admin | Tenant admin/user | Enforcement and audit |
|---|---|---|---|
| Lifecycle, trial, suspend/cancel, sessions | owns | cannot change commercial lifecycle | login/API boundary; Platform audit; suspend/cancel revoke sessions |
| Policy mode | owns | read-only through session effects | `package_allowlist` fail-closed; `legacy_allow` compatibility; before/after Platform audit |
| Package and module entitlement | owns | cannot self-enable | UI navigation/deep-link plus API edge; Platform audit |
| Explicit override | owns; survives package reassignment | none | `source='override'`; Platform audit |
| Country defaults | owns country profile and assignment | no commercial mutation | `source='country'`; region UI and API checks; Platform audit |
| Market pack | owns | cannot self-enable | independent deny-by-default handler check; Platform audit |
| Seats | owns limit | tenant admin creates staff users only within limit | enforced when staff users are created; existing users are not blocked from login merely because a later limit reduction leaves the tenant over quota; Platform audit |
| Other `limit_value` quotas | schema slot only | none | **not generally metered/enforced** |
| Users, roles, grants, access reviews | no silent tenant superuser | owns within tenant permissions | authoritative role grants; tenant audit where handler supports it |
| Company/localization/security settings | observes commercial profile only | tenant admin owns | tenant RBAC and tenant audit services |
| Personal notification preferences/sessions | none | user owns self | user/company predicates; audited where mutation handler records it |
| Feature flags | no Platform UI/API | tenant `users:manage` owns | only consumed flags have effect; AI API/UI consumes `ai_copilot`; tenant audit |
| Connector configuration | can include/exclude `integrations` | tenant operator configures/test/syncs | entitlement first, connector RBAC second; tenant audit |
| API keys/webhooks | no secret access | tenant `settings:manage` owns | tenant-scoped, hashed/one-time secrets, audited |
| Demo seed/simulator/environment/RLS/secrets | deployment owner only | no browser control | production readiness gates; deliberately not mutable in Platform UI |

Precedence is explicit: package reassignment replaces only `source='package'`; explicit overrides, country defaults and market-pack rows survive. A restrictive policy conversion reconciles stale package rows in one system transaction.

## Driver, customer and public surfaces

- Driver coaching, DVIR and HOS inherit `safety`, `maintenance` and `compliance` API gates respectively. Driver assignments, earnings, messages and notifications are core/open but self-scoped.
- Customer portal APIs require `customer_portal` and the customer binding; internal endpoints reject customer principals.
- `/eta/:trackingCode`, `/track/:token` and `/evidence/:token` are intentionally outside tenant navigation. Their authority is possession of a bounded public token/tracking code, not tenant RBAC. They must never become an entitlement bypass into internal records.
- Login, password reset, SSO discovery and health probes are necessarily outside tenant commercial gating.

## Residual enterprise risks

1. **P1 — 46 modules are not Platform-commercially gated.** Fleet, finance, execution adjuncts, governance and AI cannot be disabled by package omission. Sales must not translate this technical classification into a promise that every capability is contractually free, complete, integrated or unlimited. Package definitions may call them included core only after commercial/product approval; selective control requires both UI and API gates.
2. **P1 — API ownership is prefix-based and manually maintained.** A new endpoint under a gated UI can be left outside `ModuleKeyForPath`. The drift test protects the current catalog/keys, but endpoint-to-product ownership still requires review in every new endpoint PR.
3. **P1 — composite pages cross entitlement boundaries.** Live Map may combine Telematics with Dispatch overlays; HOS UI combines Compliance and ELD/Telematics; Fleet workspaces combine multiple open and gated APIs. They need explicit degraded states, not inferred all-or-nothing ownership.
4. **P1 — Saudi Readiness has a split boundary.** Its navigation is country-filtered but not `requiredEntitlement`; its APIs enforce Compliance and Saudi/GCC pack. A deep link can render before receiving API denial.
5. **P1 — quotas are incomplete.** Seat enforcement exists; general vehicle, driver, device, AI and usage `limit_value` enforcement does not.
6. **P2 — tenant feature flags are not a general product-control system.** Arbitrary keys can be stored but are inert until code consumes them; only known consumers such as `ai_copilot` should be represented as effective controls.
7. **P2 — route RBAC vocabulary has compatibility aliases and some page/API mismatches.** Server handlers remain authoritative; UI permission labels must be reconciled rather than treated as proof of access.
8. **P2 — entitlement changes are not pushed into an already-open tenant SPA.** API denial/restoration is immediate, but navigation and deep-link presentation use the authenticated session snapshot and require `/api/auth/me` refresh or a page reload. The control-plane demo must refresh the tenant session after each Platform change; no tenant re-login is required.
9. **P2 — legacy dedicated module roots remain compatibility handlers.** Their read/write RBAC is now explicit and caller-selected generic buckets are closed, but product teams should retire compatibility CRUD roots when the canonical workflow owns the full route. Platform entitlement does not substitute for tenant workflow authorization.
10. **Gate — bounded support access is implemented but not enabled for this pilot.** Stage 75 replaces the write-capable/time-window design with one UUID-referenced grant bound to one target tenant/user and one session, exact FK revocation, a reviewed Safety/audit read-route allowlist, dual outcome audit, a tenant banner and a 120-request-per-grant-per-minute audit ceiling. Completed reads are classified by the returned HTTP status; central request telemetry covers exceptions that abort before an outcome returns. The seeded Support Admin permission was removed and `PlatformImpersonation:Enabled` defaults false. Keep it outside the Safety pilot; broader support enablement requires explicit route-by-route read-side-effect review, immutable candidate/target migration proof and operational approval.

## Release evidence

- `tools/rehearse-platform-control-plane.sh` creates a redacted evidence bundle and runs the deterministic disposable-tenant package/override/deny/audit/restoration sequence documented in `PLATFORM_CONTROL_PLANE_REHEARSAL_RUNBOOK.md`.
- `PLATFORM_CONTROL_PLANE_DEMO_GAP_REVIEW.md` separates release gates, required demo disclosures and production-shaped follow-up from the controls proven here.
- `PlatformControlPlaneRehearsalTests` exercises real Platform endpoint methods for package and override transitions, verifies audit actor/action order and proves cleanup; its companion contract assertion binds nav/deep-link/API denial to the same authenticated policy. Rendered-browser proof remains a separate required observation.
- `PlatformEnterpriseControlMapTests` fails when the 91-module catalog changes without updating this control map, when entitlement counts/keys drift, or when navigation/deep-link/API fail-closed contracts disappear.
- The same contract now proves every configured module has a registered tenant route and that every catalog key appears in exactly one correct documented commercial bucket; a name merely appearing elsewhere in this document is no longer sufficient.
- `PlatformEnterpriseControlMapTests` also prevents restoration of the arbitrary generic module bucket API, requires RBAC mappings for every registered compatibility root and binds Traffic Violations reads to `safety:view`.
- `EntitlementPolicyModePostgresTests` proves new-tenant deny-by-default, legacy compatibility, package/override precedence and audited policy conversion.
- `MarketPackPlatformControlPostgresTests` proves Platform authorization, validation, atomic mirroring and before/after audit.
- `PlatformImpersonationPolicyTests` and `PlatformImpersonationPostgresTests` prove default-off behavior, the explicit Safety/audit read allowlist, mutation/unreviewed-route denial, target/session binding, pseudonymous tenant audit and exact revocation. They do not authorize enabling the feature for this pilot.
- `PlatformControlPlaneTests` covers Platform/tenant identity separation, lifecycle, entitlements and audit behavior.
