# OpsTrax Design System (ODS) — v5.0

Single source of truth: `src/styles/index.css` (CSS custom properties + classes) mirrored by
`src/styles/tokens.ts` (for Recharts/SVG only). React primitives: `src/components/ui.tsx`.
**Rule zero: pages compose shared primitives; they do not invent surfaces, badges, buttons, or tables.**

## 1. Visual language

Light enterprise. One page background (the body gradient), three surface tiers:

| Surface | Class / component | Use for | Never for |
|---|---|---|---|
| **Panel** | `.panel` | Default card, tables, lists, drawers | — |
| **Clay** | `.clay-card` / `<ClayCard>` | KPI/stat cards, featured metrics | Long text bodies |
| **Liquid glass** | `.liquid-glass` / `<GlassPanel>` | Page headers, hero strips, small overlays | Large scrolling lists |
| **Glass nav** | `.glass-nav`, `.glass-nav-dark` | Fixed chrome only (sidebar, app header) | In-flow content |

Clay is fully opaque (text-safe by construction). Glass surfaces have `@supports` solid
fallbacks and sit only where the backdrop is a known light (or, for `-dark`, dark) gradient.

## 2. Typography scale

Inter. Weights: 500 body-medium, 600 semibold, 700 bold, 800/900 display only.

| Role | Spec |
|---|---|
| Page title | 31–40px, font-black, tracking-tight, `text-slate-950` |
| Section/card title | `.section-title` — 0.7rem, 700, uppercase, tracking .22em, `#64748b` |
| KPI value | 30px font-black tabular-nums |
| Body | 13–14px, `text-slate-600/700` |
| Caption/meta | 11–12px, `text-slate-500` |
| Eyebrow/badge label | 10px, 700–900, uppercase, tracking ≥ .14em |

Minimum text color on white/clay: `slate-500` (#64748b). Anything lighter is decoration, not text.

## 3. Spacing & radius

Tailwind scale; page rhythm is a 4px grid.
- Card padding: `p-5` (dense: `p-4`, hero: `p-6`)
- Grid gaps: KPI rows `gap-3`, page sections `gap-4`/`space-y-4`
- Radii tokens only: `--r-card` 18px, `--r-clay` 20px, `--r-btn`/`--r-field` 12px, pills `999px`.

## 4. Elevation & shadows

Tokens only — never hand-rolled `box-shadow` in pages:
`--shadow-card` (resting) → `--shadow-card-hover` (raised) · `--clay-shadow`/`--clay-shadow-hover`
· `--lg-shadow` · `--shadow-modal` · `--shadow-header`. Interactive cards add `.card-hover`
(2px lift). Elevation implies interactivity: don't put hover-lift on non-clickable cards.

## 5. Blur policy (performance)

`--blur-xs/sm/md/lg` = 4/8/14/22px are the only sanctioned values.
- `md` — fixed chrome (`.glass-nav*`) and `.liquid-glass` heroes (≤1 per viewport)
- `lg` — small overlays only (`.glass` dropdowns)
- **Never** backdrop-blur on `.panel`, tables, or any large scrolling region.

## 6. Motion

Subtle and fast: transitions 150–220ms ease; entrances `.anim-fade-up/.anim-fade-in`
(≤320ms); stagger via `.stagger` (max 8 children). Transform + opacity only — no
animating layout properties. Everything must collapse under `prefers-reduced-motion`
(global guard exists in index.css; don't add inline animations that bypass it).
No layout jumps: skeletons must match the loaded component's surface and footprint
(`SkeletonCard` ↔ `KpiCard`).

## 7. Accessibility

- WCAG AA contrast: status text uses the 600/700-on-50 ramps (`--status-*` tokens).
- Keyboard: global `:focus-visible` teal outline — never `outline: none` without replacement.
- No meaningful text over blur without a solid fallback (`@supports` blocks in index.css).
- Forms: use `<FormField>` — it wires `htmlFor`, `aria-describedby`, `aria-invalid`, `role="alert"`.
- Overlays: `role="dialog"`, `aria-modal`, Escape to close, focus the dismiss control.
- Icon-only buttons need `aria-label`. Clickable rows need `tabIndex` + Enter/Space handling
  (DataTable does this).

## 8. Status colors

One vocabulary, from `--status-*` tokens (TS: `status` in tokens.ts):
**danger** #dc2626 · **warning** #d97706 · **success** #059669 · **info** #2563eb ·
**ai** #7c3aed · **muted** #64748b — each with `-bg` (50-tint) and `-border` (200-tint).
Pages use `<StatusBadge>` / `<RiskBadge>` or `.badge-*`; never invent new tone hexes.

## 9. Tables

Use `<DataTable rows columns onSelect>`. Rules it enforces (keep them if you must go bespoke):
search + count in the toolbar; sortable headers with visible sort state; sticky header on
scroll; zebra-free rows with hover tint; row affordance (cursor + keyboard) only when
`onSelect` exists; built-in empty row state; footer count. Numbers right-align and use
`tabular-nums`. Cells auto-render StatusBadge/RiskBadge for status-ish columns.

## 10. AI recommendation panels

`<AiInsightCard>` is the only AI surface: violet `--status-ai` ramp, Sparkles glyph,
"insight" eyebrow, optional confidence %. AI content is always visually distinct from
factual data (violet ramp is reserved for AI), never presented as a metric, and shows a
confidence signal when the backend provides one. Recommendations phrase an action, not a fact.

## 11. Responsive behavior

Breakpoints: base (mobile) → `md` 768 → `lg` 1024 → `xl` 1280 (sidebar appears at `xl` in
AppShell, `lg` in PlatformShell). KPI grids: `sm:grid-cols-2 md:grid-cols-4`. Tables scroll
inside `overflow-x-auto` with a `min-w` — the page never scrolls horizontally. Header
collapses breadcrumb (lg) and clock (md) first. Touch targets ≥ 36px on mobile.

## 12. Loading / error / empty

Every data region renders exactly one of: `<LoadingState>` (or `SkeletonCard` grid) while
fetching, `<ErrorState message onRetry>` on failure, `<EmptyState title subtitle action>`
when zero rows, or the content. Never render a blank panel and never leave seed/fallback
data looking live (see CLAUDE.md: fix the API, don't lean on the seed).
