# Stage 14A Naming and Route Compatibility

| Area | Finding | Evidence | Risk | Decision |
| --- | --- | --- | --- | --- |
| Main label | `Dashboard` is the correct visible label for the main app. | `frontend/src/layouts/AppShell.tsx` fallback title and `CommandCenterPage` heading. | Low | Keep Dashboard. |
| Legacy route | `/command-center` is still the compatibility route. | `frontend/src/App.tsx` and `frontend/src/modules/moduleConfig.ts`. | Low | Preserve route path. |
| Cockpit rename request | The app should not switch the main surface to `Cockpit`. | Current shell already uses Dashboard. | Low | No Cockpit rename. |
| Route behavior | Visible UI name and route path can differ safely. | Dashboard label with `/command-center` route. | Low | Keep as-is for compatibility. |

