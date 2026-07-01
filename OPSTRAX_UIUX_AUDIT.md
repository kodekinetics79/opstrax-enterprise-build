# OpsTrax — UI/UX Consistency Audit

**Scope:** `frontend/src/pages/*.tsx` (73 page components) + shared UI + global stylesheet
**Mode:** Measurement only. No code changed. No commits made.
**Date:** 2026-06-30
**Auditor role:** Senior UI/UX auditor

---

## 0. Repo verification & working-tree state

`git remote -v` confirms this working directory is a clone of the target repo:

```
origin  https://github.com/kodekinetics79/opstrax-enterprise-build.git (fetch/push)
```

(There is also a second remote `zayra → kodekinetics79/zayra-ai-workforce.git`, the
sibling product. `origin` is correct, so this audit proceeds against the right repo.)

**Uncommitted changes from prior sessions (Phase 0 security work) — listed, NOT touched:**

```
 M .gitignore
 M api-dotnet/Infrastructure/Database.cs
 M api-dotnet/appsettings.json
 M backend-dotnet/Controllers/EndpointMappings.cs
 M frontend/src/pages/DriverMessagingPage.tsx
 M frontend/src/pages/ExecutivePage.tsx
 M frontend/src/pages/OperatingModulePage.tsx
 M frontend/src/pages/SlaKpiPage.tsx
?? OPSTRAX_PHASE0_REMEDIATION_REPORT.md
?? OPSTRAX_PHASE0_VERIFICATION.md
?? OPSTRAX_REALITY_AUDIT.md
?? OPSTRAX_REALITY_AUDIT_V2.md
?? api-dotnet/appsettings.example.json
?? database/migrations/2026_06_30_stage19_row_level_security.sql
?? docs/qa/
```

None of the above were reverted, staged, or modified. This audit adds exactly one new
untracked file: `OPSTRAX_UIUX_AUDIT.md`.

---

## ⚠️ Headline finding — TWO design systems are in conflict

The reference spec you supplied describes a **dark, dense, 13px terminal-style enterprise
console**:

| Spec token | Spec value | What the repo actually ships |
|---|---|---|
| `--surface-base` | `#0B0F14` (near-black) | `--bg: #eef3f9` (light blue-grey) |
| `--surface-raised` | `#131922` | `--surface: #ffffff` |
| `--text-primary` | `#E8ECF1` (light on dark) | `--text-primary: #0f172a` (dark on light) |
| `--accent-primary` | `#2E7CF6` | `--blue: #2563eb` / `--teal: #0d9488` |
| `--accent-critical` | `#E5484D` | `#ef4444` |
| Body type | 13px / 400 | PageHeader H1 is **31–40px**; body 14px |
| Shell | Top command bar + global KPI strip + right drawer | **Left sidebar** + top breadcrumb bar, no global KPI strip |

The repo has a fully realized, internally-documented design system — **"OPSTRAX
ENTERPRISE DESIGN SYSTEM v4.0 · Light-Enterprise"** (header comment in
[index.css](frontend/src/styles/index.css#L3)) — but it is a **different system** from the
one in the brief. The `:root` token block ([index.css:8-72](frontend/src/styles/index.css#L8))
is dark→light inverted, the type scale is larger, and the shell is sidebar-based.

**Consequence for this audit:** "off-system" is ambiguous, so every finding below is graded
on **two axes**:

- **Axis A — Internal consistency:** does the page conform to the repo's *own* v4.0 system
  (tokens in `index.css`, components in [ui.tsx](frontend/src/components/ui.tsx))? This is the
  actionable uniformity metric today.
- **Axis B — Spec conformance:** does it match the supplied dark/13px spec? Answer is
  effectively **"no" for all 73 pages** — adopting the spec is a full re-skin, not a cleanup.

The rest of the report measures primarily against **Axis A** (the system the code claims to
follow), and flags Axis-B gaps where structural (shell, type scale, tabular-nums).

---

## 1. Shell consistency audit

**Standard shell:** [AppShell.tsx](frontend/src/layouts/AppShell.tsx) (803 lines) wraps all
authenticated routes via `<ProtectedShell>` in [App.tsx:124](frontend/src/App.tsx#L124).

Its structure ([AppShell.tsx:570](frontend/src/layouts/AppShell.tsx#L570) onward):
- **Left sidebar** (`<aside>`, fixed, 300px, collapsible) — brand + search + nav.
- **Top bar** with breadcrumb (`<nav aria-label="Breadcrumb">`,
  [AppShell.tsx:622](frontend/src/layouts/AppShell.tsx#L622)).
- **Main canvas** = `<Outlet/>` where the page renders.

**Deviation from the *spec* shell (applies to ALL pages):**
- Spec wants a **top command bar** (logo│tenant switcher│search│alerts│user). Repo puts brand
  + search in the **left sidebar**; there is no horizontal command bar.
- Spec wants a **global KPI strip** owned by the shell. Repo has **none** — KPI tiles are
  rendered ad-hoc per page, so KPI presence/shape is inconsistent (see §3 KPI tiles).
- Spec's **right contextual drawer** exists only as an opt-in per-page component
  (`DetailDrawer` in ui.tsx, used by 1 page; `ShipmentLifecycleDrawer` in fleet/), not a shell slot.

**Pages that render OUTSIDE AppShell entirely (by routing design):**

| Page | Route | Why it deviates |
|---|---|---|
| [LoginPage.tsx](frontend/src/pages/LoginPage.tsx) | `/login` | Full custom marketing/login canvas. 30 hardcoded hex, custom spinner, `.login-*` CSS namespace ([index.css:504-622](frontend/src/styles/index.css#L504)). No shell — expected. |
| [PublicShipmentTrackingPage.tsx](frontend/src/pages/PublicShipmentTrackingPage.tsx) | `/track/:token` | Public, unauthenticated. Custom layout, 5 inline badges, 10 rgba. No shell — expected. |
| `PublicEtaTrackingPage` / [CustomerEtaPage.tsx](frontend/src/pages/CustomerEtaPage.tsx) | `/eta/:trackingCode` | Public ETA page, custom layout. |
| `/driver/*` pages | under [DriverLayout.tsx](frontend/src/pages/driver/DriverLayout.tsx) | Separate driver shell, not AppShell — intentional, distinct persona. |
| `/platform/*` | [PlatformShell.tsx](frontend/src/layouts/PlatformShell.tsx) | Separate platform-admin shell. |

**In-shell pages that fight the shell with custom full-bleed layouts** (render inside AppShell
but bypass the standard page composition — own headers/grids instead of `PageHeader` + tiles +
canvas):

- [FleetWorkspacePage.tsx](frontend/src/pages/FleetWorkspacePage.tsx) — 31 rgba, 15 inline
  badges, no `ui.tsx` import. Fully bespoke.
- [DispatchWorkspacePage.tsx](frontend/src/pages/DispatchWorkspacePage.tsx) — 30 rgba, 14
  inline badges, no `ui.tsx` import. Fully bespoke.
- [CommandCenterPage.tsx](frontend/src/pages/CommandCenterPage.tsx) — 24 hex local color maps,
  custom spinner.
- [FleetColdChainPage.tsx](frontend/src/pages/FleetColdChainPage.tsx),
  [FleetAssetManagementPage.tsx](frontend/src/pages/FleetAssetManagementPage.tsx),
  [FleetHealthPage.tsx](frontend/src/pages/FleetHealthPage.tsx) — no `ui.tsx` import, custom
  surfaces/badges.

**Pages NOT importing the shared component library at all (15):** AboutPage, AuditLogsPage,
DispatchWorkspacePage, FleetAssetManagementPage, FleetColdChainPage, FleetHealthPage,
FleetSaudiReadinessPage, FleetWorkspacePage, HosEldPage, LoginPage, MessageCenterPage,
NotificationCenterPage, PlatformOpsPage, PublicShipmentTrackingPage, SettingsPage.

---

## 2. Color / token audit

### Does a central theme/tokens file exist?

**Yes — partially.** This is Tailwind **v4** (`@import "tailwindcss"` at
[index.css:1](frontend/src/styles/index.css#L1)); there is **no `tailwind.config.js/ts`** and
**no `theme.ts` / `tokens.css`**. Tokens live CSS-first in the `:root` block of
[index.css:8-72](frontend/src/styles/index.css#L8) (`--bg`, `--surface`, `--teal`, `--blue`,
`--text-primary`, shadows, radii, sidebar tokens). So a single source of truth **exists**, but
it is **not enforced** — pages and components bypass it with literal hex/rgba and Tailwind
palette utilities (`text-red-300`, `bg-amber-50`, etc.) instead of token references.

### Hardcoded colors found

**Pages: 214 hex literals across 20 files; rgba() across 18 files. Non-central components add
more.** Counts per file:

**Hex (top offenders):**
LoginPage **30**, ExecutivePage **25**, CommandCenterPage **24**, PredictiveAnalyticsPage **16**,
OperatingModulePage **11**, DriverScorecardsPage **11**, Batch5FinancePage 9, EntityListPage 8,
SlaKpiPage 7, CarbonTrackingPage 6, GeofenceManagementPage 3, FleetUtilizationPage 3,
OpportunitiesPage 2, FleetWorkspacePage 2, FleetHealthPage 2, CustomerEtaPage 2, +5 with 1 each.

**rgba (top offenders):**
FleetWorkspacePage **31**, DispatchWorkspacePage **30**, PublicShipmentTrackingPage 10,
FleetColdChainPage 9, FleetAssetManagementPage 8, Batch5FinancePage 6, +12 files.

**Non-central components with hardcoded color:**
[OpsTraxLogo.tsx](frontend/src/components/OpsTraxLogo.tsx) **22**,
[DriverIntelligenceBoard.tsx](frontend/src/components/DriverIntelligenceBoard.tsx) **13**,
[LiveMap.tsx](frontend/src/components/LiveMap.tsx) **13**,
[fleet/ShipmentLifecycleDrawer.tsx](frontend/src/components/fleet/ShipmentLifecycleDrawer.tsx) 9,
[WorkspaceExperience.tsx](frontend/src/components/WorkspaceExperience.tsx) 1,
[ui.tsx](frontend/src/components/ui.tsx) 3 (the canonical lib itself hardcodes `#2dd4bf` defaults).

### Distinct hex values + what they SHOULD map to

The same brand colors are re-typed as literals dozens of times instead of using the existing
token. Top values and their token mapping:

| Hex (count) | Should be | Token already exists? |
|---|---|---|
| `#0d9488` ×24 | `--teal` | ✅ [index.css:25](frontend/src/styles/index.css#L25) |
| `#e2e8f0` ×22 | `--border` | ✅ [index.css:21](frontend/src/styles/index.css#L21) |
| `#f59e0b` ×19 | warning accent (`.kpi-warning`) | partial — no semantic token, value duplicated |
| `#2dd4bf` ×18 | teal-400 / brand light | no token (literal everywhere incl. ui.tsx) |
| `#94a3b8` ×15 | `--text-muted` | ✅ [index.css:35](frontend/src/styles/index.css#L35) |
| `#64748b` ×11 | secondary text | near `--text-secondary #475569`, drifted |
| `#ef4444` ×10 | critical accent | `.kpi-danger` uses it, no shared token |
| `#2563eb` ×8 | `--blue` | ✅ [index.css:28](frontend/src/styles/index.css#L28) |
| `#0f172a` ×2 | `--text-primary` | ✅ [index.css:33](frontend/src/styles/index.css#L33) |

**Sample file:line evidence:**
- [ExecutivePage.tsx:60](frontend/src/pages/ExecutivePage.tsx#L60) `stroke="#e2e8f0"` → `--border`
- [ExecutivePage.tsx:64](frontend/src/pages/ExecutivePage.tsx#L64) `fill="#0f172a"` → `--text-primary`
- [ExecutivePage.tsx:182-186](frontend/src/pages/ExecutivePage.tsx#L182) ScoreRing colors `#2dd4bf #38bdf8 #f87171 #34d399 #f59e0b` — five one-off literals
- [ExecutivePage.tsx:226](frontend/src/pages/ExecutivePage.tsx#L226) Recharts tooltip `background:"#fff"; border:"1px solid #e2e8f0"`
- [CommandCenterPage.tsx:21-32](frontend/src/pages/CommandCenterPage.tsx#L21) severity/accent color maps `#ef4444 #f59e0b #3b82f6 #0d9488 #d97706 #e11d48 #2563eb #4f46e5`
- [OperatingModulePage.tsx:564-704](frontend/src/pages/OperatingModulePage.tsx#L564) decorative SVG strokes/fills `#2563eb #0d9488 #f59e0b #ef4444 #7c3aed`

> Note: a meaningful share of these literals are **Recharts/SVG props** (`stroke`, `fill`,
> `tick={{fill}}`), which cannot consume CSS variables directly without a JS token export. That
> is exactly the gap — there is no `tokens.ts` to import chart colors from, so every chart
> re-hardcodes the palette. This is the single highest-leverage fix.

### Spec-token gap (Axis B)
None of the spec tokens (`#0B0F14`, `#2E7CF6`, `#E5484D` …) appear anywhere in the codebase
(grep = 0 hits). The repo's palette is a different, lighter palette. Conforming to the spec
colors = a re-theme, not a token-mapping cleanup.

---

## 3. Component duplication audit — the core uniformity metric

The repo **does** ship a canonical component library
([ui.tsx](frontend/src/components/ui.tsx)) — the problem is **partial adoption**: ~58 pages
import it, but most still hand-roll the table, and 15 pages don't import it at all.

### Status badges — **≥4 distinct implementations + ~25 page-local one-offs**
1. `StatusBadge` — canonical, semantic regex→color ([ui.tsx:131](frontend/src/components/ui.tsx#L131)). Imported by 25 pages.
2. `RiskBadge` — second canonical badge ([ui.tsx:166](frontend/src/components/ui.tsx#L166)). Imported by 15 pages.
3. KPI internal badge — third badge style baked into `KpiCard` ([ui.tsx:93](frontend/src/components/ui.tsx#L93)).
4. **Inline page-local badge spans** (`rounded-full … uppercase`) in **25 page files**, e.g.
   FleetWorkspacePage **15**, DispatchWorkspacePage **14**, PublicShipmentTrackingPage 5,
   FleetUtilizationPage 4, FleetHealthPage 4, FleetAssignmentsPage 4, AlertsCenterPage 4,
   CommandCenterPage 3, AuditLogsPage 2, DriversModulePage 2, NotificationCenterPage 2,
   SlaKpiPage 2 … (full list in scorecard).

> Extra subtlety: canonical `StatusBadge`/`RiskBadge` use **300-level text colors**
> (`text-red-300`, `text-amber-300`, `text-emerald-300` — [ui.tsx:142-150](frontend/src/components/ui.tsx#L142))
> that were designed for a **dark** surface, but they now render on the **light** v4.0 panels.
> See §5 — this is a contrast defect baked into the "canonical" component.

### Empty states — **≥3 distinct implementations**
1. `EmptyState` — canonical ([ui.tsx:441](frontend/src/components/ui.tsx#L441)). Imported by 34 pages.
2. `DataTable`'s own inline empty row — "No records found…" ([ui.tsx:324](frontend/src/components/ui.tsx#L324)).
3. Per-page ad-hoc empties in the 15 no-`ui.tsx` pages (raw `<table>` with no rows, custom messaging).

### Loading states — **≥3 distinct implementations (spec violation present)**
1. `LoadingState` (skeleton rows) + `SkeletonCard` — canonical ([ui.tsx:400](frontend/src/components/ui.tsx#L400), [:112](frontend/src/components/ui.tsx#L112)). Imported by 51 pages. ✅ matches spec intent (skeleton, not spinner).
2. **Generic spinners** (`animate-spin` / `Loader2`) — **spec explicitly forbids** "a generic
   spinner over structured content" — in **6 pages**: [AlertsCenterPage](frontend/src/pages/AlertsCenterPage.tsx),
   [CommandCenterPage](frontend/src/pages/CommandCenterPage.tsx),
   [FleetCompliancePage](frontend/src/pages/FleetCompliancePage.tsx),
   [FleetSaudiReadinessPage](frontend/src/pages/FleetSaudiReadinessPage.tsx),
   [FleetHealthPage](frontend/src/pages/FleetHealthPage.tsx), [LoginPage](frontend/src/pages/LoginPage.tsx).
3. `.skeleton` raw CSS class used directly in custom markup (the no-`ui` pages).

### Data tables — **2 patterns, badly skewed — THE biggest uniformity gap**
1. `DataTable` — canonical, sortable, search, count badge, hover, empty row ([ui.tsx:241](frontend/src/components/ui.tsx#L241)). Imported by 11 pages; actually *rendered* as `<DataTable>` in only **~4** (FleetCompliancePage, ModulePage, OperatingModulePage, TripsPage).
2. **Raw hand-rolled `<table>`** in **~51 pages** — each with its own header styling, sort logic
   (or none), hover, pagination (or none). Examples: AccountHealth, Admin, AlertRules, AuditLogs,
   Batch3/4/5, Campaigns, Compliance, Contracts, ControlTower, Customers, DispatchCommand,
   Executive, JobsPage (uses *both* a raw table and DataTable), Reports, Settings, Vehicles,
   Workforce …

> **~51 distinct table implementations vs 1 shared component.** This is the headline
> "lack of uniformity" number: **sortable headers, sticky header, pagination, and row-hover are
> reinvented per page** and therefore inconsistent. The spec's "one shared table component, used
> everywhere" is ~8% adopted.

### KPI tiles — **≥3 distinct implementations + no shell-owned strip**
1. `KpiCard` — canonical ([ui.tsx:69](frontend/src/components/ui.tsx#L69)). Used by ~23 pages
   (e.g. VehiclesModule ×10, TripsPage ×5, FleetUtilization ×5).
2. `LiveCount` — second metric-display component ([ui.tsx:573](frontend/src/components/ui.tsx#L573)).
3. `ScoreRing` — third numeric-display pattern (SVG donut, [ui.tsx:185](frontend/src/components/ui.tsx#L185)).
4. **Custom metric grids** in pages that show KPIs without `KpiCard` (FleetOverviewPage,
   ReportsPage, CommandCenterPage, the workspace pages).
- **No global KPI strip** (spec gap). KPI presence is page-by-page; some primary pages
  (FleetOverview, Reports) render KPIs with **0** `KpiCard` usage.
- `KpiCard` value text uses `text-[30px]` but **not** `tabular-nums`
  ([ui.tsx:90](frontend/src/components/ui.tsx#L90)) — spec requires `font-variant-numeric:
  tabular-nums` on numeric values. Only `LiveCount`/`ScoreRing` use it.

### Buttons — **4 variants in use vs spec's 2**
Spec allows exactly **primary (filled)** + **secondary (outline)**. Repo ships **four** button
classes plus raw buttons:
1. `.btn-primary` ([index.css:122](frontend/src/styles/index.css#L122)) — used **118×** in pages.
2. `.btn-secondary` ([index.css:171](frontend/src/styles/index.css#L171)) — **43×**.
3. `.btn-ghost` ([index.css:147](frontend/src/styles/index.css#L147)) — **177×** (the *most-used* button, and it's the non-spec third variant).
4. `.icon-btn` ([index.css:198](frontend/src/styles/index.css#L198)) — **35×**.
5. **Raw `<button>` with ad-hoc `bg-…` utility colors** (off-system) — **~30×** across pages.

---

## 4. Typography audit

**Spec scale:** Inter · H1 20/600 · H2 15/600 · Body 13/400 · Label 11/500 (0.02em) ·
numerics `tabular-nums`.

**Reality:** font is Inter ✅ ([index.css:9](frontend/src/styles/index.css#L9)), but sizes are a
sprawl of **arbitrary `text-[Npx]`** values, almost none matching the spec scale. Occurrence
counts across pages:

| Arbitrary size | Count | vs spec |
|---|---|---|
| `text-[10px]` | **232** | label is 11px; 10px is below floor |
| `text-[11px]` | **183** | ✅ matches label size (but should be a token) |
| `text-[12px]` | 40 | off-scale |
| `text-[9px]` | 20 | below floor (legibility risk) |
| `text-[28px]` | 5 | off-scale |
| `text-[18px]` | 5 | off-scale |
| `text-[13px]` | 5 | ✅ body size (but arbitrary, not tokenized) |
| `text-[15px]` | 3 | ✅ H2 size |
| `text-[14px]` `text-[16px]` `text-[24px]` `text-[26px]` `text-[34px]` `text-[40px]` `text-[54px]` `text-[30px]` | 2–3 each | all off-scale |

Plus the canonical `PageHeader` H1 is `text-[31px] … md:text-[40px]`
([ui.tsx:53](frontend/src/components/ui.tsx#L53)) — i.e. the **shared header itself is off the
spec's 20px H1** by 11–20px.

**Inline `style={{ fontSize / fontWeight }}` in pages: 48 occurrences** — mostly Recharts
tick/legend/tooltip props (e.g. [ExecutivePage.tsx:224-228](frontend/src/pages/ExecutivePage.tsx#L224)
`fontSize: 11/12`), which also re-specify size per chart rather than from a token.

**Verdict:** there is no enforced type scale. `text-[10px]`/`text-[11px]` dominate (415
combined), so the *de facto* scale is "tiny label text everywhere," with one-off display sizes
sprinkled in. Spec body=13/H1=20 is not the operative scale anywhere.

---

## 5. Accessibility spot-check (Dashboard, Fleet, Jobs, Trips, Reports)

Representative routes: **Dashboard** = [FleetOverviewPage](frontend/src/pages/FleetOverviewPage.tsx)
(`/live-dashboard`); **Fleet** = [VehiclesModulePage](frontend/src/pages/VehiclesModulePage.tsx) /
[FleetUtilizationPage](frontend/src/pages/FleetUtilizationPage.tsx); **Jobs** =
[JobsPage](frontend/src/pages/JobsPage.tsx); **Trips** = [TripsPage](frontend/src/pages/TripsPage.tsx);
**Reports** = [ReportsPage](frontend/src/pages/ReportsPage.tsx).

### Keyboard focus visibility — **FAIL (systemic)**
- Global stylesheet defines **only `.field:focus`** ([index.css:247](frontend/src/styles/index.css#L247)).
  There is **no global `:focus-visible` ring** using an accent color. Spec requires "visible
  keyboard focus rings using `--accent-primary`."
- Only **22 / 73** pages contain any `focus-visible`/`focus:ring`/`focus:outline` utility.
- Of the 5 spot pages, **only FleetUtilizationPage** has a single focus utility; FleetOverview,
  Jobs, Trips, Reports have **0**. Clickable table rows (`onClick` on `<tr>` in `DataTable`,
  [ui.tsx:330](frontend/src/components/ui.tsx#L330)) are **`<tr>` not `<button>`** → not keyboard
  focusable/operable at all.

### Color-only status signaling — **PARTIAL**
- `StatusBadge`/`RiskBadge` always render a **text label** alongside color ✅ ([ui.tsx:158](frontend/src/components/ui.tsx#L158)) — spec-compliant where used.
- But **color-only dots** appear in custom code: `ActionQueue` priority dots
  ([ui.tsx:493-515](frontend/src/components/ui.tsx#L493)), `Timeline` type dots
  ([ui.tsx:532](frontend/src/components/ui.tsx#L532)), and CommandCenter severity dots
  ([CommandCenterPage.tsx:21-23](frontend/src/pages/CommandCenterPage.tsx#L21)) — a 2px colored
  dot is the *only* differentiator in some rows. `.live-dot` pulse is also color/motion only.

### Contrast (estimated from actual hex on the light v4.0 surfaces)

| Pair | Approx ratio | AA normal (4.5) | Notes |
|---|---|---|---|
| `--text-primary #0f172a` on `--surface #ffffff` | ~16.5:1 | ✅ PASS | body/headings fine |
| `--text-secondary #475569` on `#ffffff` | ~7.4:1 | ✅ PASS | |
| `#64748b` on `#ffffff` | ~4.9:1 | ✅ borderline | used for many labels |
| **`--text-muted #94a3b8` on `#ffffff`** | **~2.8:1** | ❌ **FAIL** | used for `text-[10px]`/`text-[11px]` meta everywhere → tiny + low contrast, double hit |
| **`StatusBadge text-*-300` on `bg-*-500/10`** | **~2–3:1** | ❌ **FAIL** | 300-level text (e.g. `#fca5a5`) on a near-white tinted pill — the canonical badge is low-contrast on light theme |
| `text-teal-700 #0f766e` on white (eyebrow) | ~5.3:1 | ✅ PASS | |

**Worst offenders:** the two failing rows above are *pervasive* — muted 10px meta text and the
canonical status badge colors. Because the badge defect lives in the shared component, fixing
`StatusBadge`/`RiskBadge` to use 600/700-level text remediates it everywhere at once.

### Spot-page summary

| Page | Focus rings | Color-only signal | Contrast |
|---|---|---|---|
| FleetOverview (Dashboard) | ❌ none | dots in custom widgets | muted-meta fails |
| VehiclesModule / FleetUtilization (Fleet) | ❌ 0 / 1 | inline badges (have labels) | badge + muted fail |
| Jobs | ❌ none; rows not focusable | badges OK | muted fail |
| Trips | ❌ none; DataTable rows not focusable | badges OK | badge fail |
| Reports | ❌ none | n/a (charts) | chart axis `#94a3b8` low-contrast |

---

## 6. Scorecard

**Verdict key:** CONSISTENT = uses shell + shared lib, no hardcoded color, no custom variants ·
PARTIAL = mostly shared but ≥1 deviation (raw table / inline badge / few literals) · OFF-SYSTEM
= bypasses shared lib and/or heavy custom layout/color.
"Std shell?" = renders inside AppShell (public/login/driver/platform = **No, by design**).
"A11y issues" = focus/contrast/color-only (✱ = systemic floor applies to every in-shell page:
no global focus ring + muted-meta contrast).

| Page | Std shell? | Hardcoded colors | Custom variants | A11y issues | Verdict |
|---|---|---|---|---|---|
| AboutPage | Yes | none | no-ui import | ✱ | PARTIAL |
| AccountHealthPage | Yes | none | raw table | ✱ | PARTIAL |
| AdminPage | Yes | none | raw table | ✱ | PARTIAL |
| AiCopilotPage | Yes | none | — | ✱ | CONSISTENT |
| AlertRulesPage | Yes | none | raw table, inline badge | ✱ | PARTIAL |
| AlertsCenterPage | Yes | 2 rgba | spinner, 4 inline badges | ✱ spinner | OFF-SYSTEM |
| AnalyticsDashboardPage | Yes | none | raw table, inline badge | ✱ | PARTIAL |
| AuditLogsPage | Yes | none | no-ui, raw table, 2 badges | ✱ | OFF-SYSTEM |
| Batch3OperationsPage | Yes | none | raw table | ✱ | PARTIAL |
| Batch4SafetyPage | Yes | none | raw table | ✱ | PARTIAL |
| Batch5FinancePage | Yes | 9 hex, 6 rgba | raw table | ✱ | OFF-SYSTEM |
| CampaignsPage | Yes | none | raw table | ✱ | PARTIAL |
| CarbonTrackingPage | Yes | 6 hex | raw table | ✱ | PARTIAL |
| CommandCenterPage | Yes (custom layout) | 24 hex, 2 rgba | spinner, color maps, 3 badges | ✱ spinner, dots | OFF-SYSTEM |
| CompliancePage | Yes | none | raw table | ✱ | PARTIAL |
| ContractsPage | Yes | none | raw table | ✱ | PARTIAL |
| ControlTowerPage | Yes | none | raw table | ✱ | PARTIAL |
| CustomerEtaPage | No (public) | 2 hex | raw table | ✱ | PARTIAL |
| CustomerVisibilityPage | Yes | none | raw table | ✱ | PARTIAL |
| CustomersPage | Yes | none | raw table | ✱ | PARTIAL |
| DigitalFormsPage | Yes | none | raw table | ✱ | PARTIAL |
| DispatchCommandPage | Yes | none | raw table | ✱ | PARTIAL |
| DispatchPage | Yes | none | — | ✱ | CONSISTENT |
| DispatchWorkspacePage | Yes (custom layout) | 1 hex, 30 rgba | no-ui, 14 inline badges | ✱ | OFF-SYSTEM |
| DriverMessagingPage | Yes | none | raw table, inline badge | ✱ | PARTIAL |
| DriverScorecardsPage | Yes | 11 hex, 2 rgba | raw table | ✱ | OFF-SYSTEM |
| DriversModulePage | Yes | 2 rgba | raw table, 2 badges | ✱ | PARTIAL |
| EntityListPage | Yes | 8 hex, 2 rgba | raw table | ✱ | OFF-SYSTEM |
| ExecutivePage | Yes | 25 hex, 1 rgba | raw table, inline badge | ✱ | OFF-SYSTEM |
| FeatureFlagsPage | Yes | none | — | ✱ | CONSISTENT |
| FinancialAnalyticsPage | Yes | 1 hex | raw table | ✱ | PARTIAL |
| FleetAssetManagementPage | Yes (custom layout) | 8 rgba | no-ui, inline badge | ✱ | OFF-SYSTEM |
| FleetAssignmentsPage | Yes | 2 rgba | raw table, 4 badges | ✱ | PARTIAL |
| FleetColdChainPage | Yes (custom layout) | 9 rgba | no-ui, inline badge | ✱ | OFF-SYSTEM |
| FleetCompliancePage | Yes | none | shared table ✅, spinner, badge | ✱ spinner | PARTIAL |
| FleetHealthPage | Yes (custom layout) | 2 hex | no-ui, spinner, 4 badges | ✱ spinner | OFF-SYSTEM |
| FleetOverviewPage | Yes | none | raw table, custom KPIs (0 KpiCard) | ✱ | PARTIAL |
| FleetSaudiReadinessPage | Yes | none | no-ui, raw table, spinner | ✱ spinner | OFF-SYSTEM |
| FleetUtilizationPage | Yes | 3 hex, 3 rgba | raw table, 4 badges | ✱ (1 focus util) | OFF-SYSTEM |
| FleetWorkspacePage | Yes (custom layout) | 2 hex, 31 rgba | no-ui, 15 inline badges | ✱ | OFF-SYSTEM |
| GeofenceManagementPage | Yes | 3 hex | — | ✱ | PARTIAL |
| HosEldPage | Yes | none | no-ui, raw table | ✱ | OFF-SYSTEM |
| IntegrationsPage | Yes | none | raw table, inline badge | ✱ | PARTIAL |
| IotDevicesPage | Yes | none | raw table | ✱ | PARTIAL |
| JobsPage | Yes | none | raw table **and** DataTable (both) | ✱ | PARTIAL |
| LastMileDeliveryPage | Yes | none | — | ✱ | CONSISTENT |
| LeadsPage | Yes | none | raw table | ✱ | PARTIAL |
| LiveMapPage | Yes | none | raw table | ✱ | PARTIAL |
| LoginPage | No (login) | 30 hex, 3 rgba | no-ui, spinner, full custom | n/a auth | OFF-SYSTEM (by design) |
| MaintenanceCommandPage | Yes | none | raw table | ✱ | PARTIAL |
| MaintenancePlanningPage | Yes | none | raw table | ✱ | PARTIAL |
| MessageCenterPage | Yes | none | no-ui, inline badge | ✱ | OFF-SYSTEM |
| ModulePage | Yes | none | shared table ✅ | ✱ | CONSISTENT |
| NotificationCenterPage | Yes | none | no-ui, raw table, 2 badges | ✱ | OFF-SYSTEM |
| OperatingModulePage | Yes | 11 hex | shared table ✅ (decorative SVG literals) | ✱ | PARTIAL |
| OperationsProofCenterPage | Yes | none | raw table | ✱ | PARTIAL |
| OpportunitiesPage | Yes | 2 hex, 1 rgba | raw table | ✱ | PARTIAL |
| PlatformOpsPage | Yes | none | no-ui, raw table | ✱ | OFF-SYSTEM |
| PredictiveAnalyticsPage | Yes | 16 hex | inline badge | ✱ | OFF-SYSTEM |
| ProofOfDeliveryPage | Yes | 1 hex | raw table | ✱ | PARTIAL |
| PublicShipmentTrackingPage | No (public) | 1 hex, 10 rgba | no-ui, 5 inline badges | ✱ | OFF-SYSTEM (by design) |
| QuotationsPage | Yes | none | raw table | ✱ | PARTIAL |
| RateCardsPage | Yes | none | raw table | ✱ | PARTIAL |
| ReportsPage | Yes | none | raw table, 0 KpiCard, no PageHeader | ✱ | PARTIAL |
| RoutePlanningPage | Yes | 1 rgba | raw table | ✱ | PARTIAL |
| SettingsPage | Yes | none | no-ui, raw table | ✱ | OFF-SYSTEM |
| SlaKpiPage | Yes | 7 hex | raw table, 2 badges | ✱ | OFF-SYSTEM |
| TelematicsCommandPage | Yes | none | raw table | ✱ | PARTIAL |
| TrafficViolationsPage | Yes | none | raw table | ✱ | PARTIAL |
| TripsPage | Yes | none | shared table ✅, 5 KpiCard | ✱ | CONSISTENT |
| VehiclesModulePage | Yes | 2 rgba | raw table, 2 badges, 10 KpiCard | ✱ | PARTIAL |
| VehiclesPage | Yes | none | raw table, inline badge | ✱ | PARTIAL |
| WorkforceManagementPage | Yes | none | raw table, inline badge | ✱ | PARTIAL |

### Tally
- **CONSISTENT:** 6 (AiCopilot, Dispatch, FeatureFlags, LastMileDelivery, ModulePage, Trips)
- **PARTIAL:** 43
- **OFF-SYSTEM:** 24 (3 of which — LoginPage, PublicShipmentTracking, CustomerEta — are
  off-system *by design* as public/auth pages)
- Against **Axis B (the supplied dark/13px spec):** **0 / 73 conform** — the implemented system
  is a different (light, large-type, sidebar) system.

---

## Summary — the uniformity verdict in one paragraph

OpsTrax is **not lacking** a design system — it has a thorough one (`index.css` v4.0 tokens +
`ui.tsx` component library). It is lacking **enforcement**. The single shared `DataTable` is
used by ~4 pages while **~51 pages hand-roll their own table**; **25 pages** ship inline status
badges beside the 3 canonical badge components; **6 pages** use forbidden spinners; buttons come
in **4 variants** instead of 2; **214 hardcoded hex** literals re-type colors that already exist
as tokens; and the type scale is a sprawl of arbitrary `text-[Npx]` values dominated by 10–11px.
Accessibility has a **systemic floor failure**: no global keyboard focus ring, clickable rows
that aren't focusable, and a canonical `StatusBadge` whose 300-level colors fail AA contrast on
the light surfaces. **Separately and more fundamentally,** the supplied reference spec describes
a *dark, 13px, top-command-bar* system that the repo does not implement at all — so "adopt the
spec" is a re-theme decision, while "make the repo internally consistent" (collapse to the one
`DataTable`, one badge, one button pair, tokenize colors/type, add focus rings) is the
measurable cleanup this audit recommends as the first pass.

---

*Measurement-only pass. No source files were modified; no commits were made. The four modified
`frontend/src/pages/*.tsx` files in the working tree are pre-existing Phase 0 changes, left
untouched.*
