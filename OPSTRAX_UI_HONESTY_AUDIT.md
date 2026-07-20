# OpsTrax — UI Honesty Audit (from live screenshots, localhost:10000)

**Correction up front:** an earlier claim of mine ("14 flagship pages are hardcoded
mockups") was wrong. The screenshots show a **competent, professional UI shell** (clean
IA, KPI cards, real tables, consistent theme) with **live data** on most pages. The real
problems are narrower and more fixable: **(A) internal dev/demo language shown to
customers**, and **(B) the demo is logged into a junk tenant (company 1), not the clean
MERIDIAN-DEMO tenant** — which is why data looks fake/empty. Below is the evidence.

---

## FINDING A — Internal/dev language leaking into the customer UI  (REAL BUG, every page)

Rendered on **every page** by `AppShell.tsx` → `components/WorkspaceExperience.tsx`
(one central place; the per-route copy lives in AppShell ~lines 223–278).

| What the customer sees | Source | Verdict |
|---|---|---|
| **"DEV TEAM LENS"** card + copy ("Shared UI primitives…", "extend the dispatch spine without redoing every screen", "control-room pattern… configuration instead of one-off page work") | `WorkspaceExperience.tsx:46` + AppShell `maintenanceOutcome` strings | ⛔ internal eng commentary — remove the whole card |
| **"EXPERIENCE RAIL"** eyebrow label | `WorkspaceExperience.tsx:35` | 🟡 dev jargon — relabel or drop the eyebrow (keep the page title + blurb) |
| **"…no dead-end demo surfaces"** in the client blurb | AppShell:230 | ⛔ "demo surfaces" is dev language |
| **"Plan not surfaced"** chip (sidebar session card) | AppShell:161 | ⛔ leaks internal plan/entitlement wording; show plan name or hide |
| **"1 permissions"** chip | AppShell:465 | 🟡 not customer-meaningful; hide or make it a real role label |
| **"LOCAL BUILD ACTIVE"**, **"Updated locally for localhost:10000"** | CommandCenterPage / FleetCompliancePage | ⛔ dev/build status — must never ship to a customer |
| **"SESSION LENS"** label | AppShell sidebar | 🟡 jargon; "Signed in as" reads better |

**Fix:** delete the "Dev team lens" card entirely from `WorkspaceExperience`; rename
"Experience rail" → the module name (or remove the eyebrow); strip "demo surfaces",
"LOCAL BUILD ACTIVE", "Updated locally", "Plan not surfaced", "SESSION LENS". One-time
edits in 2 files (`WorkspaceExperience.tsx`, `AppShell.tsx`) + 2 pages fix all screens.

---

## FINDING B — The screenshots are the WRONG tenant (junk data), not a broken UI

The logged-in user is **Mason Lee, Super Admin, company_id = 1 ("OpsTrax Demo
Logistics")**. That company is a **pollution sink**: its drivers are literally
`Stage 7 Driver 1..N` — leftovers from `stage7-*` integration-test runs that all landed in
company 1. This — not broken pages — explains the screenshots:

| Screenshot symptom | Real cause |
|---|---|
| Driver Scorecards all named **"Stage 7 Driver N"**, every metric **0** | company 1 has stage7 test-seed drivers with no safety data |
| Fleet Health **"VEHICLE RISKS (20)"** cards all **blank** (no id/label) | company 1 vehicles have no readiness/risk fields populated |
| POD rows show **"Placeholder"** type, driver **"Stage 7 Driver 5"** | POD `proof_type` defaults to 'Placeholder' (backend EndpointMappings:3398/7662) + stage7 drivers |
| Proof Center **"No data" / "blocked"** for job 1201 | job 1201 isn't company 1's / has no proof chain |

**The clean, seeded `MERIDIAN-DEMO` tenant (that I built this session) has real names,
finance that reconciles, maintenance/coaching/DVIR rows, etc.** The demo is simply pointed
at the wrong login.

**Fix (two parts):**
1. **Log the demo in as `admin@meridian.demo` (MERIDIAN-DEMO)** — not Mason Lee/company 1.
   The pilot walkthrough must use the curated tenant.
2. **Purge company 1's stage7/test pollution** (or stop tests seeding into it). Company 1
   should either be a clean tenant or not be a login target.

---

## FINDING C — Genuine content/data-quality issues (independent of tenant)

| Issue | Where | Fix |
|---|---|---|
| **On-Time ETA KPI shows "%"** with no number | Active Shipments / dashboards | compute + render the value, or hide the card when null |
| **POD "Placeholder" type** surfaces as an ugly column value | backend POD query (EndpointMappings:3398/7662/7701) | render "Awaiting capture" instead of the internal 'Placeholder' token |
| **Boilerplate "Recommended: Monitor vehicle condition" ×20** on empty risk cards | fleet-health risk generator | suppress cards with no real risk, or generate specific recommendations |
| **Headline counts don't reconcile** (Dashboard 36 active vs Active Shipments 43/50) | different endpoints count differently | align the definitions (active vs total vs in-flight) |
| **Empty risk cards render at all** (blank id, "LOW #0") | fleet-health page renders rows even with no data | hide zero/empty entries; show a real empty-state |

---

## Recommended fix order
1. **A — strip dev/demo language** (fast, every screen, biggest trust win).
2. **B1 — switch the demo login to MERIDIAN-DEMO** (instantly makes data look real).
3. **C — POD 'Placeholder' label + On-Time ETA number + empty-card suppression** (polish).
4. **B2 — purge company-1 stage7 pollution / stop tests seeding into it** (hygiene).

Net: the product is closer to demo-ready than the screenshots suggest — the shell is good;
it's mostly **wrong tenant + dev copy**, not a rebuild.
