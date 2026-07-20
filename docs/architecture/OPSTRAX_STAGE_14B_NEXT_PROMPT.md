# Opstrax Stage 14B Next Prompt

## Recommended Stage 14B: Mobile Field Execution Hardening

Stage 14A established the real mobile foundation. The best next slice is the operational hardening that makes field work safer under weak networks and more useful for real operators.

### Suggested Scope

1. Offline capture queue for proof and evidence metadata
2. Retry-safe client-generated ids and idempotency keys
3. Better job search / job assignment pickers
4. Role-specific deep links into workflow surfaces
5. Push notification contract wiring for future delivery
6. Field-ready evidence capture forms
7. Permission-aware action buttons for submit/validate/accept flows
8. Mobile test coverage and release hardening

### Why This Is the Best Next Slice

- It turns the foundation into an actual field tool without rebuilding the business spine.
- It strengthens the weakest part of the mobile story: offline/retry behavior.
- It keeps the backend contract stable while making the mobile UX more operational.

### Do Not

- Do not build the full mobile app store release pipeline yet.
- Do not build external push delivery.
- Do not rebuild CRM, finance, or IoT from the mobile side.
- Do not bypass backend RBAC or tenant isolation.

