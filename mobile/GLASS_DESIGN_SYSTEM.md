# OpsTrax Mobile — Premium Glass Design System

## Product family

The shared mobile design system serves three role-aware products on the same OpsTrax platform:

- OpsTrax Driver
- OpsTrax Fleet
- OpsTrax Customer

The apps may ship as separate binaries over time, but should share visual tokens, components, interaction patterns, accessibility requirements, and backend security contracts.

## Visual direction

Use a dark premium glass foundation with restrained translucency, crisp typography, subtle teal/blue/violet illumination, layered depth, and high-contrast operational states.

Glass is a hierarchy tool, not decoration. Use it for navigation, hero surfaces, map overlays, summary cards, fleet status, shipment cards, alerts, and decision panels.

Do not use low-contrast glass for safety-critical actions, destructive actions, compliance blockers, authentication errors, or dense operational forms.

## Core principles

1. **Operational clarity first** — important state must remain readable in sunlight, motion, warehouses, cabs, and low-connectivity environments.
2. **One glance before one tap** — driver and dispatcher screens should expose the next meaningful action without hunting through menus.
3. **Role-aware simplicity** — Driver, Fleet, and Customer receive different information density and permissions.
4. **No dead UI** — every visible control must be wired, permission-disabled with a truthful explanation, or removed.
5. **Real data only** — no hard-coded demo operational records in release builds.
6. **Server authority** — visual role restrictions never replace tenant, customer-account, assignment, RBAC, or ABAC checks on the backend.
7. **Progressive disclosure** — summaries first, details on demand.
8. **Accessible motion** — animation and haptics support state recognition but never become necessary to understand the UI.

## Glass hierarchy

### Hero glass

Use for the first operational surface on a screen: driver status, fleet operations summary, customer shipment summary, or authentication identity.

Characteristics:
- strongest ambient gradient
- subtle highlight edge
- maximum one hero per screen
- high-contrast title
- only the most important KPIs

### Elevated glass

Use for primary work panels, active shipment cards, trip details, proof workflows, and fleet exception panels.

### Quiet glass

Use for secondary information, privacy explanations, metadata, account context, and less-frequent supporting content.

### Solid safety surface

Use for:
- blocked vehicle states
- severe HOS/compliance warnings
- destructive confirmations
- security failures
- irreversible workflow actions

## Color semantics

- Teal — active movement, primary action, in-transit
- Blue — information, neutral operational context
- Violet — management/command context
- Green — complete, healthy, delivered, validated
- Amber — warning, pending, delayed, attention needed
- Red — critical, blocked, failed, violation, destructive

Never use color as the only carrier of meaning. Pair with text, iconography, or state labels.

## Driver-specific rules

- Minimum primary touch target: 52 px high; prefer larger for trip actions.
- Avoid dense card grids while the driver is on an active trip.
- Keep the next stop and next permitted action visually dominant.
- Safety/compliance warnings outrank aesthetic glass treatment.
- Do not expose uncertified ELD/HOS claims. When certified data is unavailable, state that truthfully.
- Offline state must remain obvious without blocking read-only information unnecessarily.

## Fleet-specific rules

- Prioritize exception-driven operations over decorative dashboards.
- Surface critical exceptions, delayed jobs, unavailable vehicles, and driver risks before low-value KPIs.
- Make selection state explicit and accessible.
- Desktop remains the deep planning/control plane; mobile optimizes see → decide → approve → act.

## Customer-specific rules

- The customer landing experience must answer: Where is it? When will it arrive? Do I need to do anything?
- Never expose another customer account inside the same tenant.
- Prefer shipment timeline, ETA, proof, invoice state, and support actions over fleet-internal terminology.
- Customer-safe tracking must not leak driver information beyond tenant policy.

## Accessibility and readability

- Preserve strong text/background contrast.
- Support system font scaling where practical.
- Do not communicate status through blur, opacity, or color alone.
- Maintain accessibility labels and selected/disabled state on interactive controls.
- Test dark UI in high ambient light and low brightness.

## Performance

- Native blur is preferred on iOS where practical.
- Android uses controlled translucent fallbacks when blur cost or compatibility is unacceptable.
- Avoid stacking multiple blur layers on large scrolling lists.
- Use gradients and borders to simulate depth where full blur is unnecessary.

## Store-release acceptance

A screen is not release-ready until:

- it uses shared tokens/components where applicable;
- all controls are wired or truthfully disabled;
- loading, empty, error, offline, and permission states exist;
- tenant/customer/assignment boundaries are enforced server-side;
- critical text remains readable without blur effects;
- iOS and Android rendering is checked;
- accessibility labels/states are present for interactive controls;
- no placeholder/demo operational content remains.
