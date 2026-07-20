# OpsTrax RBAC / Authorization Engine Review

| Area | Current Support | Missing Pieces | Risk | Required Foundation | Priority | P0/P1/P2 |
|---|---|---|---|---|---|---|
| Actor model | Platform users and tenant users exist | No first-class model for portal users, driver users, AI agents, API keys, integrations, webhook actors | Authorization logic may be too narrow | Define actor types and identity sources | P0 |
| Roles / permissions | Role and permission tables plus route checks exist | No centralized authorization engine | Checks can drift across controllers | Centralize decision logic | P0 |
| Scope | Company/tenant scoping exists | No formal scope model across resource types | Cross-tenant leakage risk | Add scoped policies and ownership rules | P0 |
| Feature flag integration | Tenant entitlements exist | Not integrated into centralized authorization decisions | Feature-disabled access could leak | Evaluate subscription/entitlement in auth decisions | P0 |
| Approval requirement | Export governance exists | No general approval matrix | High-risk actions may be ungoverned | Add approval policies and request logs | P0 |
| Decision logging | Audit logs exist | No authorization decision log | Hard to explain allow/deny outcomes | Log sensitive auth decisions | P1 |
| Platform vs tenant | Separate platform schema and sessions exist | Not expressed as a formal auth model | Support access may overreach | Keep separation explicit in policy design | P0 |
| Frontend hiding | UI permissions exist | Not authorization | Security illusion | Keep enforcement in backend only | P0 |
| Resource conditions | Some backend checks exist | No resource-policy DSL or condition evaluation | Complex rules will be inconsistent | Add resource and condition-based checks | P1 |
| AI/integration actors | Integration auth exists in places | No AI-specific permission path | AI could bypass user rules | Treat AI and integrations as scoped actors | P1 |

