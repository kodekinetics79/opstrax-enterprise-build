# G7C — Developer Platform & Certified Ecosystem Execution

Issue: #155  
Entry baseline: `main@6674f52f5fb8902af0cb777f2e0a893a14173b4b`

## Current-build baseline
- Authenticated APIs, tenant API-key/webhook foundations and integration catalog/lifecycle code exist.
- DeviceOps and hardware/provider certification foundations already produce capability/evidence boundaries.
- The goal is to productize and unify those foundations, not create a second API or connector stack.

## First implementation slices
1. Inventory current API-key/webhook schemas/endpoints and identify contract/security gaps.
2. Define versioned public API surface, credential scopes and deprecation policy.
3. Signed webhook delivery contract: event identity, signature version, timestamp/replay window, retry/backoff, delivery attempt ledger, replay and dead-letter visibility.
4. Developer portal information architecture and API/webhook evidence model.
5. Compatibility catalog contract shared with DeviceOps and provider gates; catalog status cannot outrun owning evidence.
6. Installer/partner registry contract and support/RMA ownership model.
7. API/webhook usage meters integrate with G6B's billing-idempotency work rather than adding a parallel meter path.

## Conflict domain
- API auth/scopes and credentials require shared security-authority coordination.
- Webhook persistence may require serialized schema authority.
- Docs, contracts, SDK/reference-client skeletons and module-local portal UI may proceed independently.

## Acceptance truth
No marketplace, partner, provider or device item is certified by being listed. Each claim remains linked to its owning gate/evidence. External sample integration and real webhook delivery/recovery evidence are required for production-platform acceptance.