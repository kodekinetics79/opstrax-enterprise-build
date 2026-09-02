# AUD-004 / AUD-029 Negative-Access Matrix

| Principal / attempt | Expected | Verified control |
|---|---|---|
| Portal role with null customer binding, password login | 403 before MFA challenge/session | live same-tenant binding validation before MFA branch |
| Portal role with null binding, MFA verification | no session | binding revalidated before session issuance |
| Portal role with null binding, SSO | no session | binding revalidated before session issuance |
| Portal role bound to deleted/dangling customer | 403 | active customer lookup in caller tenant |
| Tenant admin creates portal user without customer | 400 | create invariant |
| Tenant admin binds portal user to another tenant's customer | 400 | same-tenant customer predicate |
| Tenant admin clears binding while role remains portal | 400; binding unchanged | update invariant + PostgreSQL test |
| Tenant admin changes portal user to internal role | 200; stale binding cleared and audited | transition invariant |
| Platform admin creates/changes to portal role without binding-capable form | 400 | platform path fails closed |
| Bound customer calls internal route directly with overlapping grant | 403 and auditable decision | server-side portal boundary |
| Customer A reads customer B records | denied/empty by customer-scoped portal query | existing portal DB tests + binding gate |
| Branch-bound user calls any of 8 `/api/analytics/*` handlers | 403 | explicit server-side branch-scope gate |
| Tenant-wide authorized internal role calls analytics | allowed | positive focused oracle |
| Cross-tenant signed-ticket database read | denied | production-shaped restricted-role isolation suite |

AUD-029 uses an explicit 403 for branch-bound analytics because the eight aggregates
currently combine tables with incompatible branch ownership. Returning a partial or
silently tenant-wide metric would be misleading. Tenant-wide internal principals
retain their intentional access.
