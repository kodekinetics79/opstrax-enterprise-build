# Stage 14B Frontend Fix Log

| Page / Component / Service | Issue | Fix | API Used | Empty / Error State | RBAC / Visibility | Remaining Gap |
| --- | --- | --- | --- | --- | --- | --- |
| `frontend/src/services/adminApi.ts` | Used a shadow seed DB for all admin reads and writes | Removed fallback logic; client now calls live backend only | `/api/admin/*` | Honest errors now bubble up | Admin permissions stay backend-controlled | None for this slice |
| `frontend/src/pages/AdminPage.tsx` | Fell back to seed permissions when the live query failed | Render live loading/error states instead of synthetic data | `adminApi.permissions()` | `LoadingState` / `ErrorState` | Tabs remain permission-gated | None for this slice |
| `frontend/src/services/incidentsApi.ts` | Returned static timeline/recommendation data | Rewired to live incident timeline and recommendation endpoints | `/api/incidents/*` | Honest backend failures only | Safety permissions remain intact | None for this slice |
| `frontend/src/services/fuelApi.ts` | Dead seed imports remained | Removed in Stage 14A | `/api/fuel/*` | Live errors only | Unchanged | None |
| `frontend/src/services/safetyApi.ts` | Fake success on create | Removed in Stage 14A | `/api/safety/*` | Live errors only | Unchanged | None |
| `frontend/src/pages/CustomersPage.tsx` | Legacy seed helper remains in compatibility path | Deferred; no runtime masking after no-op helper | `/api/customers/*` | Existing page states remain honest | Customer scope preserved | Remove residue later if needed |
| `frontend/src/pages/AccountHealthPage.tsx` | Legacy seed helper remains in compatibility path | Deferred; no runtime masking after no-op helper | `/api/customers/*` | Existing page states remain honest | Customer scope preserved | Remove residue later if needed |

