# OpsTrax Mobile Premium Glass — Preview Notes

Working branch: `feat/mobile-premium-glass-ui`

This branch is an active visual/product hardening stream for the existing React Native / Expo mobile application.

## Completed in this stream

- Premium glass design tokens and shared surfaces
- Native iOS blur with controlled Android translucent fallbacks
- Floating glass navigation treatment
- Secure loading/splash treatment
- Glass hero surfaces
- Rich KPI cards
- Haptic shared actions
- Focused glass form inputs
- Improved offline, loading, empty, and error states
- Driver Today premium home treatment
- Fleet/Operations dashboard premium treatment
- Customer Home premium treatment
- Customer Shipments premium treatment
- Customer role routing remains server-permission aware
- Design-system governance documentation

## Non-negotiable product rules

- No dead buttons or decorative controls masquerading as features
- No hard-coded operational demo records in release builds
- Driver safety/compliance state must outrank decorative effects
- Customer data must remain both tenant-scoped and customer-account-scoped
- Mobile role hiding never replaces backend authorization
- Uncertified HOS/ELD capability must be labeled truthfully

## Next implementation surfaces

1. Customer billing and proof visibility
2. Driver trip execution
3. Driver proof/POD capture
4. Driver compliance/DVIR
5. Fleet telemetry/live state
6. Workflow/action surfaces for dispatch
7. Profile/security/settings
8. Store-ready onboarding and permission education
9. Accessibility and cross-platform rendering sweep
10. Internal iOS/Android build verification
