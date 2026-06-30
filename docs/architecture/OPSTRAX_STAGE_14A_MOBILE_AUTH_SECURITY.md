# Opstrax Stage 14A Mobile Auth Security

| Area | Decision | Implementation | Risk | Future Hardening |
|---|---|---|---|---|
| Authentication | Use the existing backend login/session contract. | `POST /api/auth/login`, `GET /api/auth/me`, `POST /api/auth/refresh`, `POST /api/auth/logout`. | Low | Add token expiry UX and refresh telemetry. |
| Session storage | Store only the session in secure storage. | `expo-secure-store` holds the raw session snapshot. | Medium | Add secure biometric reauth if the product later requires it. |
| Tenant context | Do not invent a mobile tenant shortcut. | The backend resolves company/tenant context from the authenticated session. | Low | Add stronger tenant hints only if the backend contract grows them. |
| Authorization | Fail closed and use backend permissions only. | The app hides or disables actions unless the backend-granted permission exists. | Low | Add finer-grained role-state UX if the backend adds more mobile-specific scopes. |
| Refresh | Retry once on 401 when possible. | The mobile API client can refresh a session and retry a request once. | Medium | Add refresh telemetry and a clearer expired-session path. |
| Logging | Do not log secrets or sensitive session payloads. | The app does not print tokens or credentials. | Low | Add structured client telemetry without credential leakage. |

