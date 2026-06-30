# Stage 6A Scope Reconciliation

The canonical business spine remains:

`Customer -> Contract -> Rate Card -> Job -> Trip -> Charge`

| Area | Status | Evidence | Risk | Fix Applied | Remaining Gap | Ready for P0-B1C? |
|---|---|---|---|---|---|---|
| Customer master | Partial legacy bridge | `customers` exists in legacy schema; `CreateCustomer` now uses tenant `company_id` and emits `customer.account.created` | Medium | Tenant-safe create/update and event publication | Full canonical customer API still rides legacy tables | Yes, with bridge notes |
| Customer contacts | Legacy table | `customer_contacts` exists in canonical init schema | Medium | Documented as tenant-owned legacy sub-resource | No dedicated canonical service layer yet | Yes |
| Customer addresses | Legacy table | `customer_addresses` exists in canonical init schema | Medium | Documented as tenant-owned legacy sub-resource | No dedicated canonical service layer yet | Yes |
| Contract foundation | Partial legacy bridge | `contracts` exists in legacy schema; `CreateContract`/`UpdateContract` now emit domain events | Medium | Canonical permission names added, tenant-scoped writes enforced | No dedicated contract service table in disposable DB | Yes |
| Contract rate cards/items | Bridged | `contract_rates` is legacy; `rate_cards` is canonical stage table; listing now bridges both when legacy exists | Medium | Canonical `rate_cards` mirror added for legacy rate writes | Legacy schema may still be the write path for some routes | Yes |
| Jobs | Partial legacy bridge | `jobs` exists in legacy schema; create/update now emit `job.created` / `job.updated` | Medium | Canonical job permissions added | Full job/dispatch model still depends on legacy schema | Yes |
| Trips | Legacy runtime surface | `trips` and `trip_stops` exist in canonical init schema | Medium | Tenant boundary preserved in docs and permission mapping | No dedicated trip bridge service yet | Yes |
| Trip stops | Legacy runtime surface | `trip_stops` is present in canonical init schema | Medium | Documented as tenant-owned child data | No canonical API bridge yet | Yes |
| Job charges | Stage 6 canonical table | `job_charges` is present locally and is queryable through `BusinessSpineService` | Low | Added read/create/update bridge endpoints and events | No revenue posting module yet | Yes |
| Domain events/outbox | Ready | Foundation tables and dispatcher runtime exist | Low | Dispatcher and event logs were already delivered and remain wired | None material for this stage | Yes |
| Idempotency | Ready | Foundation idempotency tables and service exist | Low | No regression introduced | None material for this stage | Yes |
| AI recommendations | Ready | Foundation AI tables and smoke handler exist | Medium | No direct business-table writes added | Still no live external AI execution | Yes |
| Vertical flexibility | Ready | `business_surface_profiles` persists labels/verticals | Low | Profile endpoint remains tenant-scoped | No vertical-specific ERD split | Yes |
| Tenant boundary | Clear | `company_id` remains the tenant boundary in legacy and stage tables | Low | No rename attempted | Cross-table consistency still depends on legacy schema discipline | Yes |
| Authorization / RBAC | Improved | `RequirePermission` now recognizes canonical business permissions and legacy equivalents | Medium | Canonical permission aliases added in the auth engine | Permission vocabulary still needs broader product rollout | Yes |

### Canonical rate-card decision

`rate_cards` is the canonical path.

`contract_rates` is retained as the legacy compatibility surface and is bridged into `rate_cards` when the legacy table exists.

### Local DB note

The disposable `opstrax_local` database initially only contained the stage 6 tables `rate_cards` and `job_charges`. The bridge now tolerates that reduced local shape while still documenting the full legacy business schema.

