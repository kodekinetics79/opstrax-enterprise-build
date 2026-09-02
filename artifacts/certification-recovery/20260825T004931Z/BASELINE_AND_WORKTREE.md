# Phase 1 Baseline and Worktree Preservation

- Governing audit run: `20260824T212623Z`
- Recovery run opened: `2026-08-25T00:49:31Z`
- Required source: `origin/main`
- Recorded start SHA: `b982ef8b7020b490cdf7968364f6c15421fcf83f`
- Recovery branch: `fix/certification-recovery-phase1`
- Isolated worktree: `/Users/zackkhan/Downloads/opstrax-certification-recovery-phase1`
- Original user worktree: `/Users/zackkhan/Downloads/opstrax-enterprise-build-fixed-nginx`

At isolation time the original worktree was on
`remediation/retest-20260821-1035-r1` at
`9dcbbeadac8fe0a280e6380693f0e001a0513644`. It was inspected read-only and
was not reset, cleaned, stashed, committed, reformatted, overwritten, or edited.

Its recorded user-owned changes were:

```text
 M backend-dotnet.Tests/CoreFleetVehiclesRegressionTests.cs
 M backend-dotnet/Controllers/EndpointMappings.cs
 M backend-dotnet/Services/TelemetryLiveStateService.cs
 M frontend/scripts/test-rbac-contract.mjs
 M frontend/src/auth/rbacConfig.ts
 M frontend/src/components/ui.tsx
 M frontend/src/pages/LiveMapPage.tsx
 M frontend/src/pages/VehiclesModulePage.tsx
 M frontend/src/pages/VehiclesPage.tsx
 M frontend/src/services/vehiclesApi.ts
 M telematics/fly.toml
 M tools/apply-neon-predeploy-migrations.sh
 M tools/launch/launch_plan.mjs
?? artifacts/
?? backend-dotnet/Services/VehicleFieldPolicy.cs
?? database/migrations/2026_08_23_stage89_vehicle_record_expansion.sql
?? tools/cleanup/
?? tools/pt40/11-commission-production.sh
```

The recovery worktree was clean immediately after creation and its `HEAD` was
verified equal to the required start SHA. Only selectively reimplemented Phase 1
changes are permitted here.
