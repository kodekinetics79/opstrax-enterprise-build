# G6B Support / Billing / Packaging / Commercial Release Ops — Current-Build Execution Baseline

Parent: #146 / #110  
Entry: `main@1f3b5de029b33e9315fb96c80988e610665c41b0`  
State: ACTIVE under `CR-2026-09-03-04` when v2.5 merges.

## Existing product foundation to preserve

- The Platform control plane already has `packages`, `tenant_subscriptions`, `tenant_entitlements` and `platform_invoices`.
- `backend-dotnet/Services/RevenueSchemaService.cs` already extends this with `module_packages`, `usage_meters`, append-only `usage_events`, rolled-up `usage_counters`, `pricing_rules` and `tenant_contract_overrides`.
- Current meter seeds already include vehicle, driver, shipment, POD, tracking-link, asset, temperature, fuel, integration, API-call, user and market-pack dimensions.
- Invoice previews are intentionally computed from subscription + usage + pricing + overrides rather than persisted as drifting preview state.
- Revenue/platform endpoints and entitlement guards already exist; this lane must validate and complete them rather than creating a second billing vocabulary.

## Gap statement

The product has substantial billing/control-plane primitives, but commercial release requires proof that usage events are emitted from authoritative business events, counters/invoice previews reconcile, entitlements match packages and invoices, customer-visible usage is trustworthy, and support/onboarding/SLA/RMA/offboarding are executable end to end.

## Atomic billing/packaging program

1. Inventory every current usage meter and identify its authoritative emitting business event or gauge source.
2. Prove append-only/idempotent event semantics and counter reconciliation; prevent double counting on retries/replays.
3. Define package catalog by current Capability Truth Matrix state; uncertified capability cannot be sold as certified.
4. Reconcile `module_packages`, pricing `packages`, tenant subscriptions, entitlements and in-code route/module guards to one vocabulary.
5. Complete invoice-preview/source-usage reconciliation and persisted invoice lifecycle where applicable.
6. Add tenant-visible usage/limits/overage explanation and auditability.
7. Define hardware/device/provider/video/ELD add-on charging only where the owning product gate supports sale.
8. Exercise credits/adjustments/cancellation/offboarding/data-retention boundaries.

## Atomic customer/support program

1. Tenant provisioning -> package/entitlement -> admin setup -> large-fleet import -> provider/device onboarding checklist.
2. Training/readiness paths for admin, dispatcher/fleet, maintenance, driver and customer roles as applicable.
3. Support tiers, severity definitions, escalation/on-call ownership, incident communications and customer status workflow.
4. Hardware/provider RMA/escalation ownership tied to products actually sold.
5. Renewal/expansion health and pilot-to-paid conversion workflow.
6. Release notes, known limitations, go-live/handover and rollback responsibilities.

## First implementation slice

Start with a source-of-truth meter audit against `RevenueSchemaService` and `RevenueEndpoints`: for each seeded meter, prove whether an authoritative emitter exists, whether retry/idempotency is defined, how the counter is reconciled, and whether the invoice preview consumes the same canonical value. Missing emitters become focused defects; do not create synthetic usage merely to make dashboards non-empty.

In parallel, build the package-claim map from the current Capability Truth Matrix so pricing/entitlements cannot silently expose a ROADMAP capability as a certified paid add-on.

## Stop conditions

RED if usage can be double-counted on replay, if invoice and entitlement vocabularies diverge, if one tenant can view/change another tenant's commercial state, if a package sells a capability above its evidence status, or if support/SLA/RMA commitments exist only in marketing copy and cannot be executed.