# Opstrax Stage 14A Mobile Architecture

## Objective

Stage 14A turns the backend contract into a real mobile foundation without creating a fake app or a second auth model.

## Architecture Summary

| Layer | Implementation | Notes |
|---|---|---|
| App shell | Expo React Native TypeScript app under `mobile/` | Uses a real navigation container and role-aware tabs. |
| Session | Backend login, session rehydrate, refresh, and logout | Session is stored in `expo-secure-store`. |
| Authorization | Centralized backend RBAC only | The mobile client renders based on the granted permissions. |
| Workflow state | Selected job id persisted locally | Drives proof/workflow/telemetry tabs. |
| Data access | Existing Opstrax REST contracts | No mobile-only auth shortcut and no fake business data. |

## Runtime Flow

1. App starts and restores the secure session.
2. If a session exists, the app rehydrates through `/api/auth/me`.
3. The role model selects the correct route family preview and screen emphasis.
4. The dashboard lets the user choose a live job id.
5. Workflow, proof, and telemetry tabs load real backend data for that job.

## Security Invariants

- No hardcoded tenant, company, user, or token values.
- No production URL is embedded.
- No business action executes automatically from the client.
- No separate mobile authorization model exists.
- Backend RBAC remains the source of truth.

## Current Limitations

- Offline sync is a contract only.
- Push notifications are only mapped as a future contract.
- The mobile shell is operational, but not yet a fully distributed field-execution system.

