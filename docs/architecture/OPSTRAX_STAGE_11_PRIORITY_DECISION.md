# Opstrax Stage 11 Priority Decision

## Selected Slice

**Platform Commercial Control Center**

## Why This Slice

The repo already contains real platform admin pieces, but they are spread across:

- command center
- tenants
- packages
- revenue and usage
- billing
- health
- audit

Stage 11 should unify those capabilities into one operator-grade cockpit so the SaaS business feels complete and presentable.

## Why Not the Alternatives

- Telemetry is valuable but broader and riskier.
- CRM is important but fragmented across many pages and services.
- Mobile is strategically important but not the smallest honest Stage 11 completion slice.
- Governance is already functional enough that it should not consume this stage.

## Success Criteria

The Stage 11 slice is successful if:

1. The commercial control plane is easier to operate and demo.
2. The UI makes tenant health, package posture, billing exposure, and audit state obvious.
3. The backend remains tenant-safe and permission-gated.
4. Builds and tests still pass locally.
5. No production or deployment activity occurs.

