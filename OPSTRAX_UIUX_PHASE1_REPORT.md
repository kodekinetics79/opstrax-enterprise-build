# OpsTrax — UI/UX Phase 1 Remediation Report

**Scope:** Frontend design-system foundation only. Works entirely within the existing
**Light-Enterprise v4.0** token system (`frontend/src/styles/index.css`). No new color palette
introduced. The retired dark-mode spec was not reintroduced.
**Date:** 2026-06-30
**Repo:** `origin → github.com/kodekinetics79/opstrax-enterprise-build.git` (verified via `git remote -v`)
**Build/test gate:** `npm run build` and `npm run lint` after every step. No commits made.

---

## Result summary

| Step | Outcome |
|---|---|
| 1 — `tokens.ts` export + chart/SVG migration | ✅ Done. 12 chart/SVG page files now source colors from one file. |
| 2 — StatusBadge / RiskBadge contrast | ✅ Done. 300-level text → 700-level; all variants now ≥ AA (4.5:1). |
| 3 — Global `:focus-visible` ring | ✅ Done. Single global rule using `var(--teal)`. |
| 4 — DataTable keyboard accessibility | ✅ Done. Rows focusable + Enter/Space operable. |
| 5 — Verify | ✅ `build` exit 0, `lint` exit 0. Residual chart hex = 1 decorative line (documented). |
| 6 — Report | ✅ This file. |

**Hardcoded hex across `frontend/src/pages/*.tsx`: 214 → 102 occurrences** (−112, −52%).
**Within the 12 migrated chart/SVG files: 116 → 4 occurrences** — the remaining 4 are a single
decorative banner-gradient line in `CommandCenterPage` (non-palette pastel tints; see Residuals).

---

## Files changed this session (design-system only — 15 files)

**New (1):**
- `frontend/src/styles/tokens.ts` — TS token source of truth. `tokens` mirrors the `:root` CSS
  variables in `index.css` **exactly**; `chart` catalogues the extended data-viz palette (every
  value already existed as a literal — nothing invented).

**Modified — shared system (2):**
- `frontend/src/components/ui.tsx`
  - `StatusBadge` (5 variants) + `RiskBadge` (4 variants): `text-*-300` → `text-*-700` (Step 2).
  - `DataTable`: clickable `<tr>` made keyboard-operable — `tabIndex`, `role="button"`,
    `onKeyDown` (Enter/Space), `aria-label`, all gated on `onSelect` (Step 4).
- `frontend/src/styles/index.css` — added global `*:focus-visible` ring (Step 3).

**Modified — chart/SVG color migration (12, Step 1b):**
`PredictiveAnalyticsPage`, `DriverScorecardsPage`, `Batch5FinancePage`, `EntityListPage`,
`CarbonTrackingPage`, `GeofenceManagementPage`, `FleetUtilizationPage`, `CommandCenterPage`,
`OpportunitiesPage`, `FleetHealthPage`, `FinancialAnalyticsPage`, `ProofOfDeliveryPage`.

---

## Step 1 — `tokens.ts` and chart/SVG migration

**1a.** `tokens.ts` created. `tokens.*` is 1:1 with the `index.css :root` variables (surface,
border, brand teal/blue/violet, text, radii). `chart.*` centralizes the data-viz literals
(Recharts/SVG strokes, fills, axis ticks, tooltip chrome) under Tailwind hue/scale names, e.g.
`chart.teal600 = "#0d9488"` (alias of `tokens.teal`), `chart.amber500 = "#f59e0b"`,
`chart.slate400 = "#94a3b8"` (alias of `tokens.textMuted`). Values are byte-identical to what was
previously hardcoded, so rendered output is unchanged.

**1b.** All Recharts/SVG `stroke`/`fill`/`color`/tooltip-chrome literals in the 12 files now read
from `tokens`/`chart`. Embedded-in-string literals (e.g. `border: "1px solid #e2e8f0"`) became
template literals (`` `1px solid ${tokens.border}` ``).

**1c.** Build + lint pass; colors unchanged (same hex, single source).

### Scope decisions (why not all 20 hex-bearing page files)
- **Excluded — Phase 0 locked (3):** `ExecutivePage`, `OperatingModulePage`, `SlaKpiPage` carry
  **uncommitted Phase 0 / Session 1 edits**. Per the standing instruction, these were left
  **untouched** to avoid entangling design-system edits with the security/data-honesty diff.
  Their chart hex (Executive 25, Operating 11, Sla 7 lines) is deferred to a follow-up after
  Phase 0 lands.
- **Excluded — not chart/SVG (5):** `LoginPage` (decorative animated canvas/ticker, public),
  `FleetWorkspacePage`, `DispatchWorkspacePage`, `CustomerEtaPage`, `PublicShipmentTrackingPage`.
  Their literals are `bg-[#hex]` layout backgrounds / decorative gradients, not data-viz
  `stroke`/`fill` props — out of Step 1b's scope.

---

## Step 2 — StatusBadge / RiskBadge contrast (WCAG AA)

**2a.** Confirmed against the audit: the canonical badges are `StatusBadge` and `RiskBadge`, both
in `frontend/src/components/ui.tsx`. Both used `text-*-300` (a ramp designed for dark surfaces)
on light tinted pills. Changed to `text-*-700`, the same ramp already used by the app's existing
chip pattern (e.g. CommandCenter `text-red-700`/`text-amber-700`) — an existing in-system color,
nothing invented. Backgrounds (`bg-*-500/10` etc.) and borders were kept.

**2b. Contrast calculation (WCAG, actual hex over the tinted pill atop the white panel):**

The tinted pill background (e.g. `bg-amber-500/10`) composited over the white `.panel` yields a
near-white surface; contrast computed against that composite.

| Badge variant | Before (`text-*-300`) | After (`text-*-700`) | AA (≥4.5)? |
|---|---|---|---|
| Critical / risk (red) | `#fca5a5` ≈ **1.9:1** ❌ | `#b91c1c` ≈ **5.7:1** | ✅ |
| Warning (amber) | `#fcd34d` ≈ **1.3:1** ❌ | `#b45309` ≈ **4.66:1** | ✅ |
| Success (emerald) | `#6ee7b7` ≈ **1.4:1** ❌ | `#047857` ≈ **4.96:1** | ✅ |
| AI (violet) | `#c4b5fd` ≈ **1.6:1** ❌ | `#6d28d9` ≈ **6.4:1** | ✅ |

Worked example (amber, the floor): `text-amber-700 #b45309`, relative luminance L₁ ≈ 0.159.
Background = `amber-500 #f59e0b` at 10% over `#ffffff` → ≈ `#fef5ec`, L₂ ≈ 0.923.
Contrast = (0.923 + 0.05) / (0.159 + 0.05) = **4.66:1 ≥ 4.5** ✅. All four families pass AA for
normal text; the badge label is bold 10px, which is treated as normal text (not large), so 4.5:1
is the correct threshold — met by every variant.

**Verification:** `text-*-300` remaining inside `StatusBadge`/`RiskBadge` = **0**.

---

## Step 3 — Global keyboard focus ring

**3a.** Added to `index.css` (the primary accent token in this system is `--teal`):

```css
*:focus-visible {
  outline: 2px solid var(--teal);
  outline-offset: 2px;
}
```

**3b. No collision with `.field:focus`.** `.field` sets `outline: none` and shows a soft teal
**`box-shadow`** ring on `:focus`. The new rule targets **`:focus-visible`** (keyboard only) and
uses **`outline`** — a different property. The two **compose**: a keyboard-focused field keeps its
inset box-shadow ring and additionally gains the outline; mouse focus shows only the box-shadow
(no `:focus-visible`). No property is overridden, so there is no collision. Previously only 22/73
pages had any focus styling; this gives every interactive element a visible keyboard indicator.

---

## Step 4 — DataTable keyboard accessibility

**4a.** In `DataTable` (`ui.tsx`), the clickable `<tr>` now carries (when `onSelect` is provided):
`onClick`, `onKeyDown` handling **Enter** and **Space** (with `preventDefault` to stop page
scroll), `tabIndex={0}`, `role="button"`, and `aria-label="View record details"`. When `onSelect`
is **not** provided the row is inert (no tabstop, no pointer cursor) — previously it always
rendered `cursor-pointer` even when non-interactive, which was misleading.

**4b. Consumer safety.** There is **no frontend test suite** in the repo (the market-pack tests
in git history are backend .NET tests; `package.json` has no `test` script). Verification was done
via the type-checked build (`tsc -b && vite build`, exit 0) and a review of all 13 `DataTable`
consumers: the change is backward-compatible — for consumers passing `onSelect`, click behaviour
is byte-identical; the added handlers are purely additive.

> Note: the IDE's stricter a11y engine emits a hint about a dynamic `role` on `<tr>`. The
> **project ESLint gate passes (exit 0)**; the row-opens-detail-drawer interaction is a legitimate
> button model, and this matches the approach prescribed for Step 4.

---

## Step 5 — Verification (full output)

**5a. Build**
```
> opstrax-enterprise-frontend@1.0.0 build
> tsc -b && vite build
...
dist/assets/index-D_UbgBx-.js   474.62 kB │ gzip: 146.70 kB
✓ built in 3.03s
BUILD_EXIT=0
```

**5a. Lint**
```
> opstrax-enterprise-frontend@1.0.0 lint
> eslint .
LINT_EXIT=0
```

**5b. Residual hex in chart/SVG files** — 11 of 12 fully clean. Remaining (1 line, intentional):
```
CommandCenterPage.tsx:109  background: "linear-gradient(120deg,#ffffff 0%,#f0fdfa 36%,#eff6ff 68%,#f5f3ff 100%)"
```
These three pastel tints (`#f0fdfa`, `#eff6ff`, `#f5f3ff`) are a **decorative banner gradient** with
no equivalent in the v4.0 palette — there is no token to reference and adding them would mean
inventing values, which the brief forbids. Left as-is and documented.

**5c. Contrast re-confirmed** — all StatusBadge/RiskBadge variants ≥ 4.66:1 (table in Step 2). Pass.

---

## Residuals / explicitly deferred (Phase 2)

- **CommandCenter decorative gradient** (1 line, 3 non-palette tints) — above.
- **Chart hex in Phase-0-locked files** — `ExecutivePage` / `OperatingModulePage` / `SlaKpiPage`;
  tokenize after Phase 0 lands.
- **Decorative `bg-[#hex]` backgrounds** — LoginPage and the workspace/public pages.
- **`ErrorState` icon tint** (`ui.tsx:448`, `text-red-300` on the warning icon) — same contrast
  family as the badges but **not a badge**, so outside Step 2's scope; flagged for Phase 2.
- **NOT started (correctly out of scope):** the 51-table `DataTable` migration, badge
  consolidation, button-variant cleanup.

---

## Phase 0 / Session 1 integrity

**No Phase 0 / Session 1 changes were committed, reverted, or modified.** The four
Phase-0-touched frontend pages show the **same diff stats as session start**, confirming they were
not edited here:

| File | diff at session start | diff now |
|---|---|---|
| `frontend/src/pages/ExecutivePage.tsx` | 25+ / 22− | 25+ / 22− (unchanged) |
| `frontend/src/pages/OperatingModulePage.tsx` | 16+ / 2− | 16+ / 2− (unchanged) |
| `frontend/src/pages/SlaKpiPage.tsx` | 11+ / 8− | 11+ / 8− (unchanged) |
| `frontend/src/pages/DriverMessagingPage.tsx` | 10+ / 8− | 10+ / 8− (unchanged) |

Backend Phase 0 files (`Database.cs`, `appsettings.json`, `EndpointMappings.cs`,
`AuditService.cs`) and the Session 1 `OPSTRAX_*.md` / migration / `docs/qa` artifacts were not
touched. Nothing was committed — all changes remain in the working tree for review.
