# Opstrax Stage 11 Next Prompt

## Recommended Stage 11A: Full Mobile App Foundation

Build the first real mobile shell on top of the Stage 10 contract work.

### Scope
1. React Native / Expo app shell
2. Auth login
3. RBAC route model
4. Driver / field-worker workflows
5. Proof capture shell
6. Offline sync contract only
7. Tests and local verification

### Required Constraints
- Use the same centralized backend authorization model.
- Reuse the Stage 10 execution summary and workflow contracts.
- Preserve tenant isolation and idempotency.
- Keep AI recommendation-only.
- Do not build a full offline engine yet.

### Do Not
- Do not rebuild the business spine.
- Do not build full CRM.
- Do not build full finance continuation.
- Do not build full IoT ingestion.
- Do not push, deploy, or touch production.

## Secondary Option
- Stage 11E Pre-Push Release Hardening if release packaging becomes the priority before mobile expansion.
