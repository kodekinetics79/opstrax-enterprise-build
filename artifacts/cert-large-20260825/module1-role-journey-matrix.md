# Module 1 role and journey matrix

Evidence cut: 2026-08-25
Tenant: `CERT-LARGE-20260825`
Candidate represented by latest role evidence: `6d06893881f3fd58482681549735892a201f2b39`

Status terms: **Observed** means directly visible in preserved Chrome evidence; **Pending** means no adequate browser evidence exists; **Blocked** means a documented entitlement or external constraint prevents the journey.

## Account population

| Population | Target | Current evidence | Status |
|---|---:|---:|---|
| Tenant/Fleet Administrator | 1 | Existing company administrator account used for setup | Observed |
| Fleet Manager | 5, one per branch | Screenshot shows five active Fleet Managers; CL-HQ scope separately shown | Observed for creation/scope; login pending |
| Dispatcher | 5, one per branch | Included in 24 active role-account total | Creation evidenced in aggregate; login pending |
| Maintenance Manager | 5, one per branch | Custom role persisted; CL-HQ scope shown | Observed for role/scope; login pending |
| Driver | 5, one per branch | Included in 24 active role-account total | Creation evidenced in aggregate; login pending |
| Executive | 3, tenant-wide | Custom role persisted | Observed for role creation; login pending |
| Customer | 1 | Customer/CRM surface is not included in the authorized plan | Blocked pending explicit entitlement authorization |
| **Total** | **25** | **24 active accounts shown** | One Customer account remains blocked |

## Journey matrix

| Journey | Tenant/Fleet Admin | Fleet Manager | Dispatcher | Maintenance Manager | Driver | Executive | Customer |
|---|---|---|---|---|---|---|---|
| Authenticate, refresh, logout/login persistence | Admin login and repeat login observed | Pending | Pending | Pending | Pending | Pending | Blocked |
| View exact frontend/API SHA | Observed on authenticated tenant screen | Pending | Pending | Pending | Pending | Pending | Blocked |
| View only permitted branches | All five branches observed | Pending | Pending | Pending | Pending | Tenant-wide intended; pending | Blocked |
| Direct URL outside branch scope is safely denied | Pending | Pending | Pending | Pending | Pending | Pending | Blocked |
| Download vehicle template | Observed | Pending/should depend on permission | Pending | Pending | Deny expected; pending | Read-only/deny mutation expected; pending | Blocked |
| Download driver template | Observed | Pending | Pending | Pending | Deny expected; pending | Read-only/deny mutation expected; pending | Blocked |
| Download device template | Observed | Pending | Pending | Pending | Deny expected; pending | Read-only/deny mutation expected; pending | Blocked |
| Download trailer/asset template | Observed | Pending | Pending | Pending | Deny expected; pending | Read-only/deny mutation expected; pending | Blocked |
| Import valid/invalid/duplicate rows | Pending | Pending per granted rights | Deny expected unless explicitly granted | Deny expected | Deny expected | Deny expected | Blocked |
| Search/filter/sort/page/export full dataset | Pending | Pending, branch-scoped | Pending, branch-scoped | Pending, branch-scoped | Pending, self/minimal scope | Pending, tenant-wide read-only | Blocked |
| Assign/reassign driver, vehicle, device, trailer | Pending | Pending per granted rights | Pending; expected positive for dispatch assignments | Pending; device/maintenance boundary to verify | Deny expected | Deny expected | Blocked |
| View effective-dated assignment history | Pending | Pending, branch-scoped | Pending, branch-scoped | Pending, branch-scoped | Pending/self scope | Pending, tenant-wide read-only | Blocked |
| Archive/reactivate records | Pending | Pending per granted rights | Deny expected unless explicitly granted | Pending for maintenance-owned lifecycle only | Deny expected | Deny expected | Blocked |
| View readiness/document/expiry state | Pending | Pending | Pending | Pending | Pending/self scope | Pending/read-only | Blocked |
| Cross-branch export leakage test | Pending | Pending | Pending | Pending | Pending | Not applicable if tenant-wide by design; verify read-only | Blocked |
| Responsive layouts: 1440×900, 1280×800, 768×1024, 390×844 | Pending | Pending | Pending | Pending | Pending | Pending | Blocked |

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

## Closure rule

Account creation does not satisfy a role row. Each role requires a fresh Chrome login, navigation and direct-URL checks, positive actions within its grant, negative actions outside its grant, branch filtering, refresh persistence, logout/login persistence, and preserved screenshots. Until that is complete, the role matrix remains open.
