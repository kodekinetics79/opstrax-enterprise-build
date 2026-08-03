# Platform Control-Plane Pre-Demo Rehearsal

Use this release gate before every client pilot demonstration that includes Platform Admin package or entitlement controls. It exercises a disposable tenant through real Platform endpoint methods and cleans up in a `finally` block. It must never target production.

## Entry conditions

1. Select a disposable non-production PostgreSQL database. The test identity needs schema/test privileges. Do not reuse the client pilot tenant.
2. Set `OPSTRAX_TEST_DB` for that database, unless the documented local test database on port 5433 is the intended disposable target.
3. Confirm the candidate commit and record any worktree changes. A dirty worktree is reported in evidence and needs release-owner explanation.
4. Export the explicit safety acknowledgement:

   ```bash
   export OPSTRAX_CONTROL_REHEARSAL_ACK=DISPOSABLE_NON_PRODUCTION
   ```

The runner never records the connection string or browser/session secrets.

## Automated rehearsal

From the repository root:

```bash
tools/rehearse-platform-control-plane.sh --output /tmp/opstrax-control-plane-evidence
```

The output directory must be new or empty. A successful run proves:

1. A new allowlist tenant denies governed Safety and Maintenance capabilities without enabled rows.
2. Assigning the Safety package enables Safety but leaves Maintenance denied.
3. A Platform override enables Integrations.
4. Assigning Maintenance removes the package-sourced Safety grant, enables Maintenance and preserves the override.
5. Restoring Safety and explicitly disabling the override returns the tenant to the intended baseline.
6. The Platform audit contains the expected actor-bound package and entitlement actions in order.
7. Tenant, subscription and entitlement rehearsal rows are removed even when an assertion fails.
8. Static contracts bind hidden navigation and deep-link messaging to the authenticated entitlement snapshot, while the API independently returns a module-disabled 403.

Keep `manifest.md`, the TRX, `test-output.log` and `git-status.txt` together. A PASS is invalid if the database acknowledgement was false, the evidence bundle was altered, or cleanup assertions did not execute.

## Rendered-browser supplement

The automated contract test is not a substitute for visible UAT. On the disposable pilot tenant, capture these four observations in the evidence template:

1. Before assignment, the governed navigation item is absent.
2. A pasted deep link renders “Not included in your plan.”
3. The associated API request returns HTTP 403 with `Module disabled` rather than data.
4. After package assignment and session refresh, the item and API become available; after restoration they return to the baseline state.

Capture browser screenshots and a redacted network record. Do not capture tokens, cookies, passwords, tenant personal data or database URLs.

## Stop and restore

- If the target might be production, stop immediately; do not set the acknowledgement.
- If automated cleanup fails, quarantine the disposable database and remove the `opstrax-control-rehearsal-*` tenant, subscriptions, entitlements and associated test audit rows before reuse.
- If package state changes but UI state does not, refresh authentication so the authoritative entitlement snapshot is reissued; API denial remains the source of truth.
- Any FAIL, missing browser evidence or unexplained dirty-worktree change is a no-go for a controls-focused client demonstration.

Use [PLATFORM_CONTROL_PLANE_REHEARSAL_EVIDENCE_TEMPLATE.md](PLATFORM_CONTROL_PLANE_REHEARSAL_EVIDENCE_TEMPLATE.md) for sign-off and [PLATFORM_ADMIN_ENTERPRISE_CONTROL_MAP.md](PLATFORM_ADMIN_ENTERPRISE_CONTROL_MAP.md) for authoritative ownership boundaries.
The independent disposition of remaining demo limitations is in [PLATFORM_CONTROL_PLANE_DEMO_GAP_REVIEW.md](PLATFORM_CONTROL_PLANE_DEMO_GAP_REVIEW.md).
