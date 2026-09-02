# Wave 2 Node dependency hardening — 2026-09-02

Scope: repair the two Node production dependency audits on PR #118. This is code
hardening evidence, not a deployment, provider certification, or gate closure.

## Observation and root cause

[CI run 33658502003](https://github.com/kodekinetics79/opstrax-enterprise-build/actions/runs/33658502003)
tested candidate `f6593e00113dabd9aa451876de1474e659bb8b0f`. The Node backend and
demo-events jobs failed `npm audit --omit=dev --audit-level=low`; aggregate release
evidence correctly failed because these prerequisites failed. The other eight
jobs passed. Both lockfiles resolved Express/body-parser to `qs` 6.15.2.

The upstream advisories
[GHSA-x5fp-wj9c-mxmx](https://github.com/advisories/GHSA-x5fp-wj9c-mxmx) and
[GHSA-4mjr-xmp4-gh2g](https://github.com/advisories/GHSA-4mjr-xmp4-gh2g) identify
6.16.0 as patched. These describe parser array-limit bypass and unsafe
constructor/isBuffer handling. We do not claim either was exploited in OpsTrax.

## Fix and local verification

- Pin a transitive `qs: 6.16.0` override in `backend` and `services/node-events`,
  with regenerated integrity-locked dependencies. This intentionally exceeds
  Express/body-parser's `~6.15.1` range without upgrading Express major versions.
  Revisit the override when upstream dependency ranges admit the fixed release.
- Both production audits report zero vulnerabilities at verification time.
- Fourteen parser regressions pass: effective dependency resolution for both
  consumers, ordinary nested query/array compatibility, comma array-limit
  enforcement, safe constructor/isBuffer serialization, and loopback HTTP
  extended-form parsing for both packages.
- Backend TypeScript build, demo-events syntax check, and six existing Node
  authentication/tenant/TLS regressions pass.
- CI now runs parser regression cases in each affected job and the existing
  backend boundary regressions after its build. The audit threshold remains low.

Commands: `node tools/security/test_query_parser_security.mjs`,
`npm run build --prefix backend`, `npm run check --prefix services/node-events`,
and `node --test backend/tests/security-hardening.test.js`. Audit commands run in
each package directory. Fixtures and HTTP traffic are local, not customer data.

An independent AI security reviewer reproduced the fourteen parser tests and
both clean audits, checked effective resolution through Express and body-parser,
and found no merge-blocking issue in this dependency change. A low-severity test
cleanup diagnostic was corrected so a failed local listener is not masked by a
second shutdown error. This is independent AI review, not human certification.

Hosted run `33659573606` confirmed both repaired Node jobs pass, but exposed a
test-placement mistake: the zero-install launch-tooling glob also discovered
the new suite without its package dependencies. The suite was moved to
`tools/security`, retaining mandatory execution after each Node package install.
No audit or test was disabled; the zero-install launch suite remains separate.

## Release boundary

The repair must pass hosted CI at its own exact commit before merging. No old
green run is acceptance for a new head. No staging or production deployment is
part of this repair; the G1A frontend/API remains frozen at
`e2230425a8e14249d2c0f477a7ec7b713a6ab27e`. The master plan and capability truth
statuses are unchanged. Any later deployment requires its own exact-SHA
deployment and same-journey verification.
