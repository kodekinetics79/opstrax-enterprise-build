# Opstrax Stage 14 Next Prompt

## Recommended Stage 14A: Full Mobile App Foundation

Stage 13 hardened the dashboard and made the operational bridge honest. The best next slice is the first real mobile shell that consumes the same tenant-scoped contracts.

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

- It builds directly on the live telemetry, safety, maintenance, and fleet-health contracts already in place.
- It moves the product closer to field execution without widening scope into unrelated business modules.
- It keeps the customer story aligned between web operations and future mobile execution.

### Required Constraints

- Use the centralized backend authorization model.
- Reuse the live APIs and tenant-scoped records already in place.
- Preserve tenant isolation and idempotency.
- Keep AI recommendation-only.
- Keep the mobile scope shell-level only.

### Do Not

- Do not rebuild the business spine.
- Do not build full CRM.
- Do not build full finance continuation.
- Do not build full IoT ingestion.
- Do not push, deploy, or touch production.

