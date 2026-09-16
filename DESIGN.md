---
version: alpha
name: "OpsTrax"
description: "A dense, tactile operations interface for fleet, commercial, and finance teams."
colors:
  primary: "#0d9488"
  secondary: "#2563eb"
  accent: "#7c3aed"
  background: "#eef3f9"
  background-deep: "#e6edf6"
  surface: "#ffffff"
  surface-sunken: "#eff4f9"
  border: "#e2e8f0"
  text-primary: "#0f172a"
  text-secondary: "#475569"
  text-muted: "#64748b"
  success: "#059669"
  warning: "#d97706"
  danger: "#dc2626"
typography:
  sans:
    fontFamily: "Inter, ui-sans-serif, system-ui, -apple-system, Segoe UI, sans-serif"
  mono:
    fontFamily: "ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace"
rounded:
  DEFAULT: "0.75rem"
  control: "0.5rem"
  field: "0.5rem"
  pill: "999px"
spacing:
  unit: "0.25rem"
  page-stack: "0.75rem"
  panel: "1rem"
components:
  page-header: {}
  button: {}
  field: {}
  panel: {}
  metric-rail: {}
  revenue-spine: {}
  table: {}
  dialog: {}
---

# OpsTrax Design System

## Overview

### Creative North Star

The interface should feel like a well-organized transport control desk: status strips, ledgers, manifests, and clearly depressed physical controls. It should help an operator scan exceptions and act without reading a presentation.

- **Audience and primary job:** Fleet operators, dispatch supervisors, account teams, and finance staff reviewing many related records under time pressure.
- **Target markets and evidence:** The product supports multiple regions and has Saudi/GCC packs. The current POC targets Saudi Arabia, so examples and defaults should use Saudi locations and SAR when the tenant or record supplies it.
- **Locales and language policy:** The maintained locale set includes English, Arabic (Saudi and UAE), French, and Urdu. Components must survive longer translated labels and right-to-left layout without encoding layout meaning into word order.
- **Usage scene:** Desktop-first command work with dense tables; narrow and touch-primary devices retain 44px targets and horizontal rails.
- **Register:** Product utility throughout authenticated workspaces. Brand expression belongs in the shell and compact page headers.
- **Memorable signature:** A permission-aware lifecycle rail connects records across the customer-to-cash journey.
- **Restraint:** Tables, forms, financial approval, and destructive actions stay familiar and quiet.
- **Anti-references:** Avoid slide-deck explainer panels, a wall of equal-weight cards, decorative gradients behind long lists, and unsupported “live” or “healthy” claims.
- **Token ownership/runtime mapping:** `frontend/src/styles/index.css` is the canonical runtime source. This file mirrors accepted values and rationale. Shared behavior lives in `frontend/src/components/ui.tsx`, `frontend/src/components/clay.tsx`, and `frontend/src/components/CommercialWorkspace.tsx`.

## Colors

Teal is the primary action and selected-state color. Blue identifies active or informational work; violet is a limited analytical accent. The canvas is cool gray-blue and record surfaces remain white. Red is reserved for failed, overdue, expired, or blocking states; amber means review or an approaching deadline; green means recorded success or healthy status. Missing evidence stays neutral. Focus uses the primary teal with a visible outline and soft ring.

## Typography

Inter with system fallbacks is canonical. Titles are compact, sentence case, and semibold or bold. Dense labels may use uppercase at 10–11px with modest tracking. Body copy remains 12–14px. Identifiers, dates, quantities, and money use tabular alignment where scanning benefits. Arabic and Urdu use the locale-capable system fallback and must not rely on italics for meaning.

## Layout

`AppShell` owns navigation and outer workspace spacing. `PageStack` uses the documented 12px rhythm. `PageHeader` contains a compact title, one-line purpose, actions, and an optional contextual footer. The primary queue or register belongs in the first viewport. Summary metrics use one 58px rail and filters use one compact toolbar. Lists and tables are the default presentation for operational records. Horizontal lifecycle and filter rails scroll on narrow screens instead of wrapping into tall headers.

## Elevation & Depth

Depth communicates interaction. Static record surfaces use the subtle border and card shadow from `index.css`. Buttons and linked lifecycle steps rise slightly on hover and press inward on activation. Liquid glass is limited to compact headers and small overlays; large scrolling regions use opaque surfaces. Focus, selection, and disabled states must remain distinct without depending on shadow alone.

## Shapes

Primary panels use a 12px radius. Buttons, fields, tabs, and lifecycle steps use an 8px radius. Status badges may use a pill radius. Dividers are one-pixel slate borders. Lucide icons use consistent strokes at 13–18px in dense workspaces. Text labels remain mandatory for primary and financial actions.

## Components

### Foundational visual states

Default, hover, focus-visible, pressed, selected, disabled, busy, success, warning, and error states are visibly distinct. Busy actions keep their original width and replace the icon or label with a spinner and progress verb. Skeletons preserve final geometry. Missing measurements display an em dash or an explicit unavailable label.

### Buttons and actions

Use one primary button for the main page action. Secondary actions use neutral raised controls. Destructive actions use red and explicit confirmation. Financial actions are permission-gated and show progress, success, and actionable failure. Icon-only buttons require an accessible name.

### Navigation and data display

The shell owns global navigation. `RevenueWorkspaceHeader` shows only permitted stages of Accounts → Leads → Deals → Quotes → Contracts → Delivery → Invoices → Payments. `CommercialMetricRail` contains current, decision-relevant measures and may filter the list when the relationship is clear. `CommercialToolbar` combines filters, result context, and search. Rows open a useful inspector or workflow; tables scroll horizontally while essential actions remain available.

### Forms and overlays

Fields use persistent labels for business data and visible validation. Dialogs trap focus, support Escape where safe, and return focus to the opener. Drawers use a clear heading, close action, and grouped facts. `CommercialDisclosure` holds evidence and workflow boundaries without displacing the work queue.

### Iconography

Use Lucide outline icons with the inherited text color. Dense rails use 13–14px icons; standard controls use 16px. Icons support recognition and do not replace action labels for business-critical operations.

### Motion

Motion communicates state through 100–180ms hover and press feedback. Avoid decorative looping motion. Honor `prefers-reduced-motion` by removing transforms and nonessential transitions.

### Content and data visualization

Use direct operator language: review, assign, approve, issue, record, retry. Never combine money across currencies. Prefer the tenant currency only when recorded. Charts supplement a table and never hide the underlying values. Persisted records are not automatically real-time, provider-verified, legally certified, or settled.

## Do's and Don'ts

- **Do:** Put the actionable list and its exceptions in the first viewport.
- **Do:** Derive metric and filter counts from the same authorized record set or an explicitly scoped summary.
- **Do:** Use the lifecycle rail to connect existing workflows and respect route permissions.
- **Don't:** Use three-column prose blocks or oversized summary cards above a record queue.
- **Don't:** Add buttons for future behavior or present missing evidence as zero, healthy, current, connected, compliant, verified, or settled.
