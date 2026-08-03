# Independent Client-Demo Control Gap Review

**Review date:** 2026-08-02
**Scope:** the Platform Admin commercial control plane, not general functional completeness of every OpsTrax workflow.

## Decision

The control plane's functional transition has passed locally, but the client-demo release remains NO-GO until the evidence is repeated and retained against the frozen candidate and the wider release gates pass. It is not defensible to claim that every tenant-visible module is individually package-controlled. The authoritative catalog is 91 modules: 45 commercially governed modules and 46 included core/open modules. Here “included core/open” means not selectively gated by the current Platform entitlement mechanism; it does not independently promise contractual inclusion, workflow completeness, integrations or unlimited usage.

## Gaps and required treatment

| Priority | Gap | Client-demo impact | Required treatment |
|---|---|---|---|
| Gate | The completed local rendered nav/deep-link/API transition is not an immutable exact-candidate artifact | A mutable Development observation cannot prove the later deployed browser bundle or proxy | Repeat and retain the observations in the rehearsal evidence template against the frozen demo release |
| P1 | 46 tenant modules have no Platform commercial entitlement | Sales could imply controls that the product does not provide | Present these as included core; do not demonstrate or promise package-level disablement |
| P1 | Existing tenant sessions hold an entitlement snapshot | Platform changes are enforced immediately by the API, but navigation can remain stale until session refresh | Refresh/re-authenticate after every control change in the demo script; treat API denial as authoritative |
| P1 | API-to-entitlement ownership is a manually maintained prefix map | A newly added endpoint could escape the intended commercial boundary | Run catalog/contract drift tests on the release candidate and require control-map review for new endpoints |
| P1 | Composite pages consume multiple product domains | Live Map and HOS can show partial/403 subfeatures under mixed packages | Use a rehearsed package combination; explain degraded states rather than claiming atomic page ownership |
| P1 | Saudi Readiness UI and API boundaries are split | A country-qualified deep link can render before Compliance/market-pack API denial | Do not use it as the primary entitlement demo until a single explicit UI entitlement boundary exists |
| P1 | Only seat limits are broadly operationally enforced | Vehicle, driver, device, AI and usage quotas could be mistaken for active contract limits | Do not claim these quotas; restrict the demo to seats and module access |
| P2 | Test runs use an owner-capable disposable integration database | This proves endpoint transitions, not production restricted-role/RLS topology | Pair with the production-shaped release rehearsal when making production-readiness claims |
| P2 | Automated audit rows are intentionally deleted with the disposable tenant | The test log proves assertions but does not provide a persistent UI audit record | Capture a redacted Platform audit view/export during rendered UAT before restoring the named demo tenant |
| Gate | Stage 75 now provides a default-off, uniquely bound read-only support grant, but it is not part of the Safety pilot candidate evidence | Enabling an adjacent privileged workflow would expand demo scope and requires target-environment/migration/operations approval | Keep `PlatformImpersonation:Enabled=false`; do not grant or demonstrate it. A future support rollout must approve each read route and retain Stage 75, dual-audit, banner, rate-ceiling and exact-revocation evidence |

## Demo owner checklist

- Use a dedicated disposable demo tenant and a written baseline/restore manifest.
- Freeze the release candidate before evidence capture; explain any dirty-worktree changes.
- Demonstrate one package transition and one explicit override, then restore both.
- Show tenant UI behavior and the API 403; never use a Platform token as evidence of tenant access.
- Keep market-pack controls separate from general packages because their deny-by-default overlay is independent.
- Assign a named restoration owner and verify the post-demo entitlement snapshot and audit record.

Any failed automated gate, missing browser/API capture, unexplained residual tenant state or unsupported sales claim is a control-plane demo no-go.
