# Opstrax Stage 13 Next Prompt

## Recommended Stage 13A: Full Mobile App Foundation

Stage 12A has made the telemetry/live-map foundation durable and demo-safe locally. The best next bounded slice is the first real mobile shell that consumes the same tenant-scoped contracts.

### Suggested Scope

1. React Native / Expo app shell
2. Auth login
3. RBAC route model
4. Driver / operator workflows
5. Field-worker proof capture shell
6. Offline sync contract only
7. Mobile idempotency and evidence metadata handling
8. Tests and local verification

### Why This Is the Best Next Slice

- It builds directly on the live-state and telemetry contract work completed in Stage 12A.
- It advances the field-execution story without widening into unrelated business modules.
- It keeps the user experience aligned across web and future mobile surfaces.

### Required Constraints

- Use the centralized backend authorization model.
- Reuse the telemetry/live-state and operational contracts already in place.
- Preserve tenant isolation and idempotency.
- Keep AI recommendation-only.
- Keep the mobile scope shell-level only.

### Do Not

- Do not rebuild the business spine.
- Do not build full CRM.
- Do not build full finance continuation.
- Do not build full IoT ingestion.
- Do not push, deploy, or touch production.

## Secondary Option

- Stage 13B Telemetry / IoT Operational Expansion if the product priority shifts toward deeper device, sensor, and live-operations intelligence before mobile shell delivery.
