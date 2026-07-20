# OPSTRAX Product Experience and Maintainability Playbook

This playbook defines how we keep the product easy for clients to use and easy for the dev team to maintain.

## Principles

| Area | Client Need | Dev-Team Need | Pattern We Use |
|---|---|---|---|
| Navigation | One obvious place to go next | One nav system to maintain | Shared shell, grouped modules, route-aware shortcuts |
| Actions | Real buttons that do something useful | No fake CTA drift | Buttons must navigate, export, open a drawer, or call an API |
| Visual system | Professional, consistent, trustworthy | Fewer per-page style fixes | Shared `panel`, `surface`, `btn-*`, `field`, `badge`, `PageHeader` primitives |
| Feedback | Loading, empty, error, success states must be clear | Predictable UX state handling | Common loading/error/empty components and honest “no data yet” states |
| Task flow | See what to do next, not every database field | Reusable flow patterns | Workspace experience rail with client outcome, dev note, and quick actions |
| Module growth | New pages should feel familiar on first load | New pages should be cheap to build | Config-driven module metadata and shell-level experience rails |
| Safety | No misleading demo behavior | Fewer brittle one-off conditions | Fail-closed RBAC, honest placeholders, no disabled fake CTAs |
| Evidence | Users need confidence the system is real | Team needs traceability | Live DB-backed pages, shared exports, and workflow summaries |

## Client-First Rules

1. Show the next best action, not the entire database.
2. Keep the top of every page visually calm and immediately understandable.
3. Prefer one premium shell over many page-specific designs.
4. Replace dead demo buttons with live routes, real exports, or explicit honest text.
5. Keep “no data yet” visible when the workflow is not configured.

## Dev-First Rules

1. Shared primitives first, page-specific styling second.
2. Route-aware experience hints should come from the shell, not duplicated in every page.
3. Any new module must reuse the same page header, button language, and empty-state language.
4. Any new CTA must prove it is connected to navigation, state mutation, or a backend endpoint.
5. If a page cannot complete its workflow yet, the UI must say so directly.

## What Was Implemented

| Area | Status | Notes |
|---|---|---|
| Shared theme | Done | Stronger global surfaces, buttons, and page framing |
| Page header system | Done | More premium, more reusable, better action strip |
| Dead CTA cleanup | Done | Module workspace export/navigation CTAs now real |
| Workspace experience rail | Done | Route-aware strip with client outcome, dev note, and quick actions |
| Major workspace shells | Done | Dashboard, map, fleet, dispatch, proof, health, utilization, vehicles, drivers use the unified frame |

## Remaining Work

1. Normalize the remaining secondary pages that still use older visual patterns.
2. Convert page-by-page CTA logic into shared route metadata where it makes sense.
3. Add component-level tests for the shared experience rail and shell shortcuts.
4. Keep replacing any “disabled because not configured” CTA with a clear route or a clearer honest state.

## Maintenance Checklist For New Work

- Is the page using the shared shell and page header?
- Are the primary actions real?
- Does the page show loading, empty, and error states?
- Is the module grouped and routed in the same way as the rest of the app?
- Does the UX tell the user what to do next in one glance?
- Can the next developer extend this without rewriting the page?

