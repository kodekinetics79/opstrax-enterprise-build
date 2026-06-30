# Opstrax Stage 14A Mobile Workspace Discovery

| Path Checked | Exists | Framework Evidence | Reuse Decision | Risk | Stage 14A Action |
|---|---|---|---|---|---|
| `mobile/` | Yes | Expo scaffold with `package.json`, `app.json`, `index.ts`, `App.tsx`, `tsconfig.json` | Reuse as the Stage 14A mobile app root. | Low | Build the mobile shell here instead of creating a second workspace. |
| `mobile/App.tsx` | Yes | Default Expo placeholder content | Replace immediately. | High | Implement the authenticated app shell and route structure. |
| `mobile/index.ts` | Yes | Standard `registerRootComponent(App)` entrypoint | Reuse unchanged unless app bootstrap needs a tiny wrapper. | Low | Keep the Expo entrypoint simple. |
| `mobile/app.json` | Yes | Default Expo app config | Reuse and extend only where needed. | Low | Keep branding/config local and non-production. |
| `mobile/package.json` | Yes | Expo 56 / React 19 / React Native 0.85 scaffold | Reuse; add only the libraries needed for navigation, secure storage, and API wiring. | Medium | Install the minimal dependencies for a real mobile shell. |
| `mobile/tsconfig.json` | Yes | Strict TypeScript extends `expo/tsconfig.base` | Reuse. | Low | Keep strict typing for contract-driven UI. |
| `frontend/src/services/apiClient.ts` | Yes | Axios client with bearer token, tenant header, CSRF handling, and session parsing | Reuse the contract shape as guidance for the mobile API client. | Medium | Mirror the same auth and tenant semantics in mobile fetch helpers. |
| `frontend/src/auth/rbacConfig.ts` | Yes | Permission taxonomy already includes mobile-relevant operational permissions | Reuse the same permission names and role intent. | Medium | Hide or disable mobile actions based on backend session permissions. |
| `backend-dotnet/Controllers/EndpointMappings.cs` | Yes | Auth, telemetry, safety, maintenance, and operational routes are available | Reuse as the mobile contract source. | Medium | Wire mobile screens to existing endpoints only. |

