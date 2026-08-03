# Platform Control-Plane Rehearsal Evidence

## Candidate and custody

- Candidate commit:
- Branch / build identifier:
- Operator and UTC window:
- Database classification (never paste URI): disposable non-production / other:
- Worktree clean: yes / no; approved deviations:
- Evidence directory and immutable archive reference:

## Automated gate

- Exact command:
- Result: PASS / FAIL
- Passed / failed / skipped counts:
- TRX hash or archive hash:
- Cleanup assertions passed: yes / no
- Deviations or retries:

## Deterministic transitions

| Phase | Package | Safety | Maintenance | Integrations override | Expected result | Observed |
|---|---|---:|---:|---:|---|---|
| Baseline | none | deny | deny | absent | allowlist fails closed | |
| Assign 1 | Safety | allow | deny | absent | package grant only | |
| Override | Safety | allow | deny | allow | override enabled | |
| Assign 2 | Maintenance | deny | allow | allow | override survives reassignment | |
| Restore | Safety | allow | deny | deny | intended baseline restored | |

## Rendered-browser and API evidence

| Control | Required observation | Evidence reference | PASS / FAIL |
|---|---|---|---|
| Navigation | Denied governed module is absent | | |
| Deep link | “Not included in your plan” is rendered | | |
| API boundary | Governed endpoint returns HTTP 403 / `Module disabled` | | |
| Re-enable | Refreshed session exposes assigned module and API | | |
| Restoration | UI and API return to approved baseline | | |

Redaction check: no tokens, cookies, passwords, connection strings, personal data or secret headers captured: yes / no.

## Audit evidence

- Package actions observed: three `tenant.package.assigned`
- Override actions observed: one `entitlement.enabled`, one `entitlement.disabled`
- Actor identity bound on every action: yes / no
- Before/after details sufficient to reconstruct transition: yes / no
- Audit export/screenshot reference (redacted):

## Approval

- Product/control owner: GO / NO-GO; name/date:
- Security/engineering reviewer: GO / NO-GO; name/date:
- Demo owner confirms the 46 core/open modules are represented as included core, not selectively controllable: yes / no
- Open risks accepted for this demo:
- Restoration owner and post-demo verification time:
