# OpsTrax Mobile Premium Glass — QA Focus

## Functional truth
- No visual change may alter tenant, customer-account, driver-assignment, role, or permission enforcement.
- No control may be added unless it performs a real action or is intentionally non-interactive.
- Existing API-backed values must remain API-backed after redesign.

## Visual regression
- Login
- Driver Today
- Customer Home
- Customer Shipments
- Fleet/Operations Dashboard
- Bottom navigation
- Offline banner
- Loading, empty, and error states

## Cross-platform
- iOS native blur rendering
- Android translucent fallback rendering
- keyboard avoidance on login
- bottom navigation safe-area behavior
- small phone width
- large phone width
- tablet sanity check

## Accessibility
- button labels
- selected state on work items
- disabled state on actions
- alert/live-region behavior
- text contrast
- status labels not dependent on color alone

## Field-use checks
- primary actions readable in bright light
- driver touch targets usable one-handed
- blocked/safety state visually outranks decorative glass
- offline state remains obvious
