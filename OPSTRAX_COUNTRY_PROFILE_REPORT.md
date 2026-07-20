# OpsTrax — Country Profile Structure & Tenant Cascade

**Scope:** additive schema + platform-admin cascade mechanism. Builds the country
**profile structure** and the **cascade mechanism** only. Does **not** implement
ZATCA invoice generation, Hijri calendar rendering, or Arabic RTL layout — those
remain separate builds that this structure now has a clean place to plug into.

Repo: `github.com/kodekinetics79/opstrax-enterprise-build` (confirmed `git remote -v`).
Does not touch RLS-staging, UI/UX Phase 1, or the demo-readiness sweep.

---

## 1. Schema

### `country_profiles` (new, additive)
Created by [CountryProfileSchemaService.cs](backend-dotnet/Services/CountryProfileSchemaService.cs),
run as schema step `CountryProfiles` in [Program.cs](backend-dotnet/Program.cs) right
after the Platform step. Idempotent (`CREATE TABLE IF NOT EXISTS`, `ON CONFLICT` seed).

| column | type | notes |
|---|---|---|
| `country_code` | `VARCHAR(2)` **PK** | ISO 3166-1 alpha-2 |
| `country_name` | `VARCHAR(120)` NOT NULL | |
| `default_currency` | `VARCHAR(8)` NOT NULL | |
| `default_locale` | `VARCHAR(20)` NOT NULL | |
| `text_direction` | `VARCHAR(3)` NOT NULL | CHECK `ltr`/`rtl` |
| `calendar_system` | `VARCHAR(40)` NOT NULL | |
| `invoicing_scheme` | `VARCHAR(40)` NOT NULL | |
| `tax_id_label` | `VARCHAR(60)` NOT NULL | |
| `default_tax_rate` | `NUMERIC(6,4)` **NULL** | |
| `data_residency_note` | `VARCHAR(400)` **NULL** | |
| `auto_enabled_features` | `JSONB` NOT NULL `[]` | array of feature keys |
| `created_at` / `updated_at` | `TIMESTAMPTZ` | |

### `companies` (additive columns)
`ALTER TABLE companies ADD COLUMN IF NOT EXISTS country VARCHAR(2)` and
`... currency VARCHAR(8)`. `companies.timezone` already existed. No destructive changes.

### Seed — exactly 2 rows (STEP 1b/1c)
| | SA | CA |
|---|---|---|
| country_name | Saudi Arabia | Canada |
| default_currency | **SAR** | **CAD** |
| default_locale | ar-SA | en-CA |
| text_direction | **rtl** | ltr |
| calendar_system | gregorian_hijri_dual | gregorian |
| invoicing_scheme | zatca_phase2 | standard |
| auto_enabled_features | `[zatca_invoicing, hijri_calendar_toggle, arabic_rtl]` | `[]` |

### Manageable via real endpoint — **no code deploy for new countries** (STEP 1b)
`country_profiles` is a full platform-admin CRUD surface, not a one-time seed:

- `GET  /api/platform/country-profiles` (perm `platform:countries:view`)
- `GET  /api/platform/country-profiles/{code}`
- `POST /api/platform/country-profiles` (upsert; perm `platform:countries:manage`)
- `PUT  /api/platform/country-profiles/{code}`
- `DELETE /api/platform/country-profiles/{code}`

New permission keys `platform:countries:view` / `platform:countries:manage` granted to
Super Admin (via `platform:*`), Product Admin, and (view) Sales Admin in
[PlatformSchemaService.cs](backend-dotnet/Services/PlatformSchemaService.cs).
Test `Upsert_And_Delete_A_New_Country_Profile_Roundtrips` provisions a brand-new
country (AE) purely through the service — proving no deploy is needed.

---

## 2. Cascade logic (STEP 2) — confirmed

`POST /api/platform/tenants` now accepts `countryCode`. Cascade lives in the reusable,
HttpContext-free [CountryProfileService.ApplyToTenantAsync](backend-dotnet/Services/CountryProfileService.cs)
(so it is provable without a web host); the endpoint in
[PlatformEndpoints.cs](backend-dotnet/Controllers/PlatformEndpoints.cs) delegates to it.

On tenant creation with a `country_code`:
1. resolves the `country_profiles` row (unknown code → `400`, never silently ignored);
2. `billing_currency` for the new subscription inherits the profile default (explicit
   `billingCurrency` in the body still wins);
3. writes `companies.country` / `companies.currency` / `companies.timezone`
   (timezone from a per-country default: SA → `Asia/Riyadh`, CA → `America/Toronto`);
4. mirrors the currency onto `tenant_subscriptions.billing_currency`;
5. for each key in `auto_enabled_features`, upserts a `tenant_entitlements` row with
   `source='country'`, **without clobbering** any existing `source='override'` row.

## 2b. Override-friendly — **defaults, never locks** — confirmed

The existing `PUT /api/platform/tenants/{id}/entitlements` path writes `source='override'`.
The cascade's upsert explicitly preserves any `override` row on conflict, so after creation
an admin can toggle any auto-enabled feature **off**, or turn a non-default feature **on**,
and that override persists — even if the cascade re-runs (e.g. package reassignment).

---

## 3. Platform admin UI (STEP 3)

[PlatformTenantsPage.tsx](frontend/src/pages/platform/PlatformTenantsPage.tsx) —
`CreateTenantDrawer`:
- Country `<PSelect>` populated from `GET /api/platform/country-profiles`
  (via new `platformApi.countryProfiles` in
  [platformApi.ts](frontend/src/services/platformApi.ts)) — **not hardcoded**.
- On selection, a preview panel renders **before** confirm:
  *"On creation, this country will apply"* → currency, locale, text direction, calendar,
  invoicing, and the auto-enabled features (as `PBadge` chips), plus an explicit
  *"these are defaults — every feature can still be toggled per-tenant after creation."*
- Uses existing shared components only (`PField`, `PSelect`, `PBadge`, `PButton`,
  `PDrawer`, `PInput`) — no new one-off UI patterns.

---

## 4. Verify (STEP 4)

- **Backend build:** `dotnet build` → **0 errors**.
- **Frontend:** `tsc --noEmit` clean; `npm run build` → built OK.
- **Full backend test suite (real Postgres @ :5433):**
  **882 passed / 0 failed** (`Passed! - Failed: 0, Passed: 882`).

### Test count vs baseline
| | count |
|---|---|
| Baseline executable tests | 876 |
| New (`CountryProfilePostgresTests`) | **+6** |
| **New total** | **882** |

New tests (all real seeded Postgres data, no mocks/fallbacks):
1. `Seeded_Profiles_SA_And_CA_Return_Exact_Expected_Values` — STEP 1c.
2. `Upsert_And_Delete_A_New_Country_Profile_Roundtrips` — STEP 1b (no-deploy CRUD).
3. `Create_SA_Tenant_Cascades_SAR_Currency_And_Three_Entitlements` — STEP 2c (SA).
4. `Create_CA_Tenant_Cascades_CAD_And_No_SA_Features` — STEP 2c (CA).
5. `Country_Defaults_Do_Not_Lock_Overrides_Persist` — STEP 2c (defaults-not-locks).
6. `Unknown_Country_Code_Returns_Null_Cascade` — negative path.

### Note on suite hygiene
A pre-existing failure in `DemoTenantSeederPostgresTests` (unrelated to this work) was
surfacing because that test's `DeleteDemoTenantAsync` cleanup list omits several child
tables (`driver_documents`, `driver_certifications`, `vehicle_documents`, `user_sessions`,
`customer_contacts`, `customer_addresses`), so a stale `MERIDIAN-DEMO` tenant left in the
shared local DB could not be deleted and `AlreadySeeded` returned true. This is a data /
test-cleanup gap independent of country profiles (which only add a table + two additive
columns). The stale tenant row was cleared from the local DB (data-only); the unrelated
test file was not modified.

---

## Scope boundary (restated)
This session delivered the **profile structure** and **cascade mechanism** only.
ZATCA invoice generation, Hijri calendar rendering, and Arabic RTL layout are **out of
scope** and intentionally not implemented — the `auto_enabled_features` keys
(`zatca_invoicing`, `hijri_calendar_toggle`, `arabic_rtl`) are the clean plug-in points
those future builds will switch on.
