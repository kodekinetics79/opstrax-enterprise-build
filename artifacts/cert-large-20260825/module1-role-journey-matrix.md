# Module 1 role and journey matrix

Evidence cut: 2026-08-26
Tenant: `CERT-LARGE-20260825`
Latest deployed candidate exercised: `7e98a39d66d67dbd5bb5419602532d7ec1aa23d1`

Status terms: **Observed** means directly visible in preserved Chrome evidence; **Pending** means no adequate browser evidence exists; **Blocked** means a documented entitlement or external constraint prevents the journey.

## Account population

| Population | Target | Current evidence | Status |
|---|---:|---:|---|
| Tenant/Fleet Administrator | 1 | Existing company administrator account used for setup | Observed |
| Fleet Manager | 5, one per branch | Five active accounts; CL-HQ scope and fresh login observed | Observed |
| Dispatcher | 5, one per branch | 24-account aggregate plus fresh CL-HQ login | Observed |
| Maintenance Manager | 5, one per branch | Custom role, CL-HQ scope, and fresh login observed | Observed |
| Driver | 5, one per branch | 24-account aggregate plus fresh restricted-portal login | Observed |
| Executive | 3, tenant-wide | Custom role plus fresh tenant-wide login | Observed |
| Customer | 1 | Customer/CRM surface is not included in the authorized plan | Blocked pending explicit entitlement authorization |
| **Total** | **25** | **24 active accounts shown** | One Customer account remains blocked |

## Journey matrix

| Journey | Tenant/Fleet Admin | Fleet Manager | Dispatcher | Maintenance Manager | Driver | Executive | Customer |
|---|---|---|---|---|---|---|---|
| Authenticate, refresh, logout/login persistence | Admin login and repeat login observed; device transfer persisted through refresh/relogin | Fresh CL-HQ login observed | Fresh CL-HQ login observed | Fresh CL-HQ login observed | Fresh CL-HQ login observed | Fresh tenant-wide login observed | Blocked |
| View exact frontend/API SHA | Observed on authenticated tenant screen | Exact `75eda29` / Live observed | Exact `75eda29` / Live observed | Exact `75eda29` / Live observed | Driver shell omits badges; provenance inherited from exact-gated origin immediately before switch | Exact `75eda29` / Live observed | Blocked |
| View only permitted branches | All five branches observed | CL-HQ showed 250 drivers and 200 vehicles; NE-HUB driver search returned no result | CL-HQ showed 250 drivers/200 vehicles; NE-HUB driver search returned no result | CL-HQ showed 220 devices; NE-HUB device search returned no result | Restricted portal showed no linked profile or fleet data | Tenant-wide 1,000 vehicles and 1,250 drivers observed | Blocked |
| Direct URL outside branch scope is safely denied | Pending | Direct `/user-management` cleanly denied for missing `users:view`; record-level direct URL remains pending | Direct `/user-management` cleanly denied | Direct `/user-management` cleanly denied | `/drivers/records` and `/user-management` redirected safely to `/driver` | Direct `/user-management` cleanly denied | Blocked |
| Download vehicle template | Observed | Pending/should depend on permission | Pending | Pending | Deny expected; pending | Read-only/deny mutation expected; pending | Blocked |
| Download driver template | Observed | Pending | Pending | Pending | Deny expected; pending | Read-only/deny mutation expected; pending | Blocked |
| Download device template | Observed | Pending | Pending | Pending | Deny expected; pending | Read-only/deny mutation expected; pending | Blocked |
| Download trailer/asset template | Observed | Pending | Pending | Pending | Deny expected; pending | Read-only/deny mutation expected; pending | Blocked |
| Import valid/invalid/duplicate rows | All 1,000 vehicles, 1,250 drivers, 1,100 devices, and 300 assets persisted through UI imports; controlled device/asset invalid and duplicate rejection observed; controlled vehicle/driver correction remains open | Pending per granted rights | Deny expected unless explicitly granted | Deny expected | Deny expected | Deny expected | Blocked |
| Search/filter/sort/page/export full dataset | 1,000 vehicles, 1,250 drivers, 1,100 devices and 300 assets persisted; full-volume asset/device search/page/export observed | Own-branch counts/search observed; complete sort/page/export cycle pending | Own-branch counts/search observed | 220-device own-branch page observed; complete sort/export cycle pending | Restricted portal/self scope observed | Tenant-wide 1,000 vehicles and 1,250 drivers observed read-only; export available | Blocked |
| Assign/reassign driver, vehicle, device, trailer | Vehicle/driver reassignment, device install/transfer, and trailer custody reassignment observed | Pending per granted rights | Existing branch-scoped assignment history observed | Device branch scope/read access observed; mutation control intentionally omitted | Deny expected | Read-only expected | Blocked |
| View effective-dated assignment history | Vehicle/driver, device, and trailer history survived refresh and logout/login | Pending, branch-scoped | Branch-scoped history observed | Device branch-scoped history readable | Restricted portal observed | Tenant-wide read-only expected | Blocked |
| Archive/reactivate records | CLHQ-D-0003 archived, persisted through refresh/logout-login, then reactivated with audit/timeline rows | Pending per granted rights | Deny expected unless explicitly granted | Mutation controls omitted on driver roster | Deny expected and restricted portal observed | Mutation controls absent on driver roster | Blocked |
| View readiness/document/expiry state | Driver and vehicle readiness inspected; certification data lacks linked expiry/documents and vehicle readiness is Unknown | Pending | Assignment history observed | Maintenance Center package-blocked; device health available | No linked driver profile | Pending/read-only | Blocked |
| Cross-branch export leakage test | Pending | Pending | Pending | Pending | Pending | Not applicable if tenant-wide by design; verify read-only | Blocked |
| Responsive layouts: 1440×900, 1280×800, 768×1024, 390×844 | Externally blocked: installed visible-Chrome controller exposes no viewport API and native resize attempts remained at 1728×851/940 | Blocked | Blocked | Blocked | Blocked | Blocked | Blocked |

## Existing browser evidence mapped to matrix

- `chrome/stable-tenant-admin-login-exact-sha.png`: authenticated administrator and exact SHA surface.
- `chrome/tenant-admin-second-login-success.png`: repeat administrator authentication.
- `chrome/module1-branches-five-persisted-71fd868.png`: five branches persisted after refresh.
- `chrome/module1-add-user-branch-scope-five-71fd868.png`: all five branch scopes available in user workflow.
- `chrome/module1-vehicles-template-import-surface.png` and `chrome/module1-vehicles-import-modal-500-row-limit.png`: vehicle template/import surface and batch limit.
- `chrome/module1-drivers-template-import-surface.png`: driver template/import surface.
- `chrome/module1-device-template-import-modal-71fd868.png`: device template/import surface.
- `chrome/module1-returnable-asset-type-template-import-71fd868.png`: trailer/asset type and import surface.
- `chrome/module1-6d068938-24-active-role-accounts.png`: aggregate active-account count.
- `chrome/module1-6d068938-five-fleet-managers-active.png`: five Fleet Manager accounts.
- `chrome/module1-6d068938-maintenance-manager-persisted-exact.png`: custom Maintenance Manager role persisted.
- `chrome/module1-6d068938-executive-persisted-exact.png`: custom Executive role persisted.
- `chrome/module1-6d068938-maintenance-manager-cl-hq-scope.png`: branch-bound maintenance account configuration.
- `chrome/module1-customers-not-in-plan-71fd868.png`: Customer journey entitlement blocker.
- `chrome/m1-021-post-vehicle-d0002-history-045c8a4.png` and `chrome/m1-021-after-logout-login-assignments-045c8a4.png`: governed vehicle/driver reassignment and effective-dated history persistence.
- `chrome/75eda29-fleet-manager-clhq-fresh-login.png`: final-SHA Fleet Manager authentication and Live provenance.
- `chrome/75eda29-fleet-manager-clhq-own-branch-drivers.png`: CL-HQ branch-scoped roster population.
- `chrome/75eda29-fleet-manager-clhq-cross-branch-driver-search-denied.png`: safe negative cross-branch search.
- `chrome/75eda29-fleet-manager-clhq-direct-admin-denied-verified.png`: direct administrative URL denial.
- `chrome/75eda29-dispatcher-clhq-fresh-login.png`, `chrome/75eda29-dispatcher-clhq-assignment-history.png`, `chrome/75eda29-dispatcher-clhq-cross-branch-driver-denied.png`, and `chrome/75eda29-dispatcher-clhq-direct-admin-denied-verified.png`: Dispatcher authentication, branch scope, effective-dated history and denial evidence.
- `chrome/75eda29-maintenance-manager-clhq-fresh-login.png`, `chrome/75eda29-maintenance-manager-clhq-devices-220.png`, `chrome/75eda29-maintenance-manager-clhq-cross-branch-device-denied.png`, `chrome/75eda29-maintenance-manager-clhq-driver-mutation-controls-omitted-verified.png`, and `chrome/75eda29-maintenance-manager-clhq-direct-admin-denied.png`: Maintenance Manager Module 1 positive and negative authorization evidence.
- `chrome/75eda29-driver-clhq-fresh-login-safe-setup.png`, `chrome/75eda29-driver-clhq-fleet-registry-redirected-safe.png`, and `chrome/75eda29-driver-clhq-direct-admin-redirected-safe.png`: Driver restricted-portal and direct-URL negative evidence.
- `chrome/75eda29-executive01-fresh-login.png`, `chrome/75eda29-executive01-vehicles-1000-readonly.png`, `chrome/75eda29-executive01-drivers-1250-readonly.png`, and `chrome/75eda29-executive01-direct-admin-denied.png`: Executive tenant-wide read-only and denial evidence.
- `chrome/75eda29-admin-driver-D0003-reactivated-history-verified.png`: reversible driver lifecycle with persisted archive/reactivate audit history.
- `chrome/75eda29-admin-driver-D0003-readiness-documents-prearchive.png` and `chrome/75eda29-admin-vehicle-CLHQ-V-0001-readiness.png`: readiness/document/expiry inspection and remaining data gaps.
- `chrome/7e98a39-exact-frontend-api-live-gate.png`: final matching frontend/API SHA, Live status, tenant identity, and all-systems-operational gate.
- `chrome/7e98a39-device-transfer-review.png`, `chrome/7e98a39-device-transfer-success-history.png`, `chrome/7e98a39-device-transfer-refresh-persisted.png`, and `chrome/7e98a39-device-transfer-relogin-persisted.png`: one controlled device transfer, complete old/new effective-dated history, and refresh/logout-login persistence.
- `input/drivers_server_export_b1313ed5.csv`: complete 500-driver server export at that evidence cut.

The b131 Fleet Manager cross-branch denial, archive/reactivate lifecycle, 500-driver pagination, and document-modal observations were not preserved as screenshots because returned screenshot bytes were not written. They therefore remain Pending under this matrix's evidence definition and must be repeated on the corrective SHA.

## Closure rule

Account creation does not satisfy a role row. Each role requires a fresh Chrome login, navigation and direct-URL checks, positive actions within its grant, negative actions outside its grant, branch filtering, refresh persistence, logout/login persistence, and preserved screenshots. Until that is complete, the role matrix remains open.
