# G7A — Enterprise Control Plane & Globalization Execution

Issue: #153  
Entry baseline: `main@6674f52f5fb8902af0cb777f2e0a893a14173b4b`

## Current-build baseline
- Existing `SsoConnectionService` and `sso_connections` provide tenant SSO configuration/readiness.
- Identifier-first SSO discovery exists in the login flow.
- MFA, RBAC, RLS, access reviews, audit and security-event foundations already exist.
- SCIM remains unimplemented/documented as a gap.
- Enterprise architecture states the application is single-region today.

## First implementation slices
1. Truth-contract tests around existing SSO lifecycle: no enabled SSO without validated configuration; disable/revoke fails closed.
2. SCIM domain contract: external identity, group mapping, immutable source identity, deprovisioning state, idempotency key and audit event model.
3. Bulk provisioning/import contract with resumable job/error ledger.
4. Enterprise hierarchy contract and policy inheritance boundaries.
5. Conditional-access/IP-policy design and tests without altering shared auth authority until its integration slot is approved.
6. Regional/data-residency contract and multi-region ADR; no multi-region production claim until deployed/evidenced.

## Conflict domain
- Shared auth/RBAC/session authority: REQUIRED for final integration; serialized.
- Production migration authority: REQUIRED for SCIM/hierarchy persistence; serialized.
- Module-local admin UI can progress independently.

## Acceptance truth
Engineering may reach `ENGINEERING COMPLETE / EXTERNAL EVIDENCE HOLD`; production SSO/SCIM/multi-region claims still require real IdP/SCIM/regional evidence and independent qualified acceptance.