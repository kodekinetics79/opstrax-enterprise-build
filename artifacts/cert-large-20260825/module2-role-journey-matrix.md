# Module 2 role and journey matrix

Tenant: `CERT-LARGE-20260825`

| Role | Branch scope | Control Tower | GPS | OBD/J1939 | Device Health | Direct URL / negative evidence | Final state |
|---|---|---|---|---|---|---|---|
| Dispatcher | CL-HQ | 220 managed devices; 100-row page; permission-aware actions | Allowed; 220 scoped devices; serial search and detail pass | Control absent; direct URL denied | Allowed | `/obd-j1939` rendered a safe permission denial without diagnostic data | Final-candidate retest pending |
| Maintenance Manager | CL-HQ | Allowed | Navigation depends on role grant | Navigation advertised, but page was restricted on `d0e01c9…` | Allowed | Finding M2-004 fixed in PR #94 | Final-candidate retest pending |
| Tenant / Fleet Administrator | Tenant-wide | Pending final candidate | Pending final candidate | Pending final candidate | Pending final candidate | Cross-branch and direct URL negatives pending | Pending |
| Executive / Read-only | Authorized read scope | Pending | Pending | Pending | Pending | Mutation controls and direct URL negatives pending | Pending |
| Driver | Assigned/self scope | Pending | Pending | Pending | Pending | Internal tenant-list leakage check pending | Pending |
| Customer | Portal scope only | Not expected | Not expected | Not expected | Not expected | Internal telematics direct URLs must deny before data paint | Pending |

This matrix records rendered behavior. Static role configuration and API probes are supporting evidence only.
