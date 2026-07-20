# Stage 14A Source Search Findings

| Area | Files / Findings | Risk | Decision |
| --- | --- | --- | --- |
| Dashboard naming | `frontend/src/layouts/AppShell.tsx`, `frontend/src/modules/moduleConfig.ts`, `frontend/src/pages/CommandCenterPage.tsx` all show `Dashboard` as the visible name. | Low | Keep visible naming as Dashboard. |
| Route compatibility | `/command-center` still exists for navigation compatibility. | Low | Preserve route; do not rename the path in this stage. |
| Live API client pattern | `frontend/src/services/apiClient.ts` uses bearer token, tenant headers, and 401 handling. | Low | Keep as the canonical client. |
| Legacy fallback residue | `frontend/src/services/fuelApi.ts` still imports seed helpers; `frontend/src/services/safetyApi.ts` still fabricates a success object on create failure. | High | Remove fallback masking from live main-app services. |
| Operational surfaces | `CommandCenterPage`, `LiveMapPage`, `OperationsProofCenterPage`, and customer/finance/safety/maintenance pages are real app surfaces, not empty shells. | Low | Maintain existing architecture and harden only where needed. |

