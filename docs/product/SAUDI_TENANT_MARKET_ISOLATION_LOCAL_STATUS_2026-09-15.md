# Saudi tenant market isolation — local implementation status

Date: 2026-09-15
Branch: `redesign/customer-finance-workspaces-local`
Release status: local review only; not pushed or deployed

## Operating rule

`companies.country` is the single operating-market authority. Tenant users cannot
change this value from Company Profile, localization preferences, branches, tax,
DVIR, compliance, or market-pack screens. A Platform Admin must deliberately change
the operating country, and that change applies the full market profile atomically.

The approved mappings in this release are:

| Tenant country | Market pack | Currency | Time zone | Distance | Volume | Tax/invoice regime |
|---|---|---|---|---|---|---|
| Saudi Arabia (`SA`) | `saudi_gcc` | SAR | Asia/Riyadh | Kilometers | Liters | ZATCA Phase 2 / 15% VAT |
| Canada (`CA`) | `canada_na` | CAD | America/Toronto | Kilometers | Liters | GST |
| United States (`US`) | `canada_na` | USD | America/New_York | Miles | Gallons | U.S. sales tax |

Other GCC countries fail closed in this release. They are not silently assigned the
Saudi pack because ZATCA and TGA controls are Saudi-specific.

## Isolation implemented

- Company Profile country is read-only and always comes from the canonical company record.
- Organization and personal locale settings lock country, currency, time zone, and units to the tenant market.
- Saudi language choices are limited to Arabic and English; incompatible locale writes return a conflict.
- Market-pack lists and detail routes expose only the pack compatible with the tenant country.
- Canadian compliance routes return forbidden for Saudi tenants, including when a stale entitlement exists.
- Compliance profiles, rules, and summary data are filtered to the tenant country.
- Tax profiles and seller registration reject an incompatible regime, currency, or jurisdiction.
- Branches, DVIR templates, DVIR reports, and audit packages derive their country from the tenant and reject spoofed country input.
- Driver and vehicle references used in DVIR writes must belong to the same tenant.
- Changing the operating country removes stale country entitlements, activates the correct market pack, rewrites the tenant locale, and normalizes branch and DVIR country values in one transaction.
- Database triggers reject direct SQL attempts to save an incompatible locale, market pack, tax profile, seller registration, branch, or DVIR country.
- A deferred database check prevents a partial country change from being committed.
- Duplicate Saudi navigation was removed; Fleet Compliance resolves to the tenant's market rather than showing both Canada and Saudi workspaces.
- Saudi finance readiness appears only for Saudi tenants.
- The primary vehicle roster, vehicle detail, records, fleet-health view, and driver
  DVIR form display and accept Saudi distance/speed values in kilometers and km/h,
  while preserving the existing canonical miles fields at the API boundary.
- Route planning, optimization savings, and live-map route spans also render in the
  tenant distance unit.
- Protected API sessions now return users to login consistently when authentication
  expires, rather than leaving Saudi settings screens in a local network-error state.
- Spreadsheet exports use one formula-safe encoder, and tenant/company database
  columns no longer fall back to company 1.

## Local verification

- Backend build: passed with zero errors.
- Frontend lint: passed.
- Frontend production build and bundle budget: passed.
- Combined market, migration, authentication, export, validation, and branch suite:
  132 passed, 0 failed against a disposable PostgreSQL database.
- Full non-database backend suite: 2,550 passed, 0 failed.
- Canonical market/profile restart and clean-migration compatibility: 24 focused tests passed.
- Full frontend contracts, production build, and bundle budget: passed.
- Local API readiness: Ready.
- Live Saudi API checks: 11 passed, 0 failed.
- Verified live behavior: Saudi market context, SAR, Asia/Riyadh, kilometers,
  liters, Saudi-only market pack, Saudi-only compliance profile/rules, and no foreign tax-profile visibility.
- Deliberate U.S. locale spoof: rejected with HTTP 409.
- Deliberate Canadian branch spoof: rejected with HTTP 409.
- Deliberate Canadian market-pack access: rejected with HTTP 403.
- Saudi market-pack access: accepted with HTTP 200.
- Database two-company isolation: Saudi and Canadian tenants retained separate locale,
  pack, branch, DVIR, tax, and seller-registration boundaries; cross-market writes rolled back.
- Clean migration-only database: Stage145 populated canonical Saudi/Canada reference rows,
  preserved profile/rule identity across repeated seeding, and Stage102/103 HOS shadow/retention verification passed.
- Broad legacy PostgreSQL fixture run identified 37 old test files that create country-null
  tenants. Those fixtures must be updated individually; the production fail-closed country
  trigger was deliberately not weakened to accommodate obsolete test setup.

## What remains before production

These items require deployment or an external authority and are not represented as
complete by the local market lock:

1. Review and approve the local changes, then create the single combined pull request.
2. Run the full CI, migration dry run, Render deployment, and Vercel deployment from the approved commit.
3. Repeat the Saudi/Canada production isolation test with two real non-production tenants and restricted application database credentials.
4. Complete ZATCA taxpayer/device onboarding, certificates, signing, clearance/reporting adapter, and ZATCA test-service certification.
5. Select and contract a Saudi-licensed payment provider, then add checkout, signed webhooks, settlement, refund, and dispute reconciliation.
6. Obtain Locus and Elm/TAMM agreements, sandbox credentials, scopes, and test cases before enabling live connector actions.
7. Obtain Saudi regulatory/accounting sign-off for the final ZATCA, VAT, TGA, Iqama, record-retention, and Arabic-document rules.
8. Audit remaining legacy screens that expose provider-native distance fields. Primary
   vehicle, DVIR, route-planning, optimization, and live-map workflows now convert safely;
   other provider fields need source-unit confirmation before conversion.

The current local app is ready for tenant-market UI review and isolation testing. It
must not be described as ZATCA-certified, payment-acquiring, TAMM-connected, or
Locus-connected until those external onboarding steps pass.
