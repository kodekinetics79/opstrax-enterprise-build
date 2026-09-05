# OpsTrax Mobile — Current Release Status

Current verdict: `NOT_READY_WITH_BLOCKERS`

The current branch is an active product hardening branch, not a public-store release candidate.

## What is already real
- Existing Expo/React Native application foundation
- Tenant-aware authenticated session model
- Driver role routing
- Customer role routing
- Fleet/operations role routing
- Real backend-backed operational screens
- Premium shared glass design system

## Why public store release remains blocked
- Remaining role surfaces still need the new visual/interaction system
- End-to-end driver trip/POD/compliance flows require full device validation
- Offline behavior must be verified on real phones and poor networks
- Cross-tenant/customer-account authorization must be regression tested after final mobile API changes
- Store privacy/permission/account-deletion disclosures must match final behavior
- Internal iOS and Android builds must pass before public submission

## Next target

`READY_FOR_INTERNAL_TESTING`

That verdict requires a buildable mobile branch with all critical role journeys functioning against real backend APIs, no dead UI, clean authorization boundaries, and device-installable iOS/Android artifacts.
