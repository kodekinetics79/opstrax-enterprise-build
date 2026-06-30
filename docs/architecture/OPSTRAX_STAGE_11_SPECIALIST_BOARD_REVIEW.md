# Opstrax Stage 11 Specialist Board Review

This review compares the remaining enterprise candidates and selects the most defensible local build slice.

| Candidate | Enterprise Value | Scope Risk | Existing Maturity | Gap Size | Decision |
|---|---|---|---|---|---|
| Platform commercial operations cockpit | Very high. It supports SaaS packaging, tenant lifecycle, billing, entitlement, health, and audit decisions. | Moderate. It can be built by composing existing endpoints and adding one coherent summary surface. | High. Backend and frontend platform surfaces already exist. | Medium. The missing piece is a single operator-grade cockpit. | Selected |
| Tenant RBAC / access review expansion | High. It matters for governance and compliance. | Medium. It is already partially built and could become sprawling. | Medium-high. Admin pages and security tables already exist. | Medium. The surface is useful but not the clearest completion win. | Not selected |
| Telemetry / IoT / live map hardening | High. It is a product differentiator. | High. It touches many services and workflows. | Medium. The subsystem already has breadth. | Large. Too much surface area for one controlled completion slice. | Not selected |
| CRM / sales completion | High. It impacts conversion and customer lifecycle. | High. The pages and services are fragmented across multiple modules. | Medium. Many CRM pages already exist, but they are distributed. | Large. A proper CRM completion would be a broader program. | Not selected |
| Customer portal / client visibility completion | Medium-high. It is valuable for buyers and customers. | Medium. It is mostly a customer-facing read surface. | Medium. Existing page and endpoint scaffolding are present. | Medium. Useful, but less central to the SaaS control plane. | Not selected |
| Mobile shell / offline contract | High in the long term. | High. It requires new app infrastructure. | Low. No mobile shell exists in this repo. | Large. It is a program, not a slice. | Not selected |

## Board Conclusion

Stage 11 should be treated as a specialist completion pass on the platform commercial control plane.

Why this slice:

1. It closes a real SaaS-operator gap without rebuilding a vertical from scratch.
2. It is bounded enough to implement locally with low regression risk.
3. It makes the product feel more like a complete enterprise SaaS business, not just an operations app.
4. It uses the strongest existing foundation in the repo: platform auth, tenant lifecycle, packages, usage, billing, health, and audit.

## Risk Notes

- This is not a replacement for later telemetry, CRM, or mobile work.
- The cockpit must stay honest: no fake data, no hidden backend assumptions, and no production touch.
- The page should summarize what already exists rather than inventing new commercial workflows.

