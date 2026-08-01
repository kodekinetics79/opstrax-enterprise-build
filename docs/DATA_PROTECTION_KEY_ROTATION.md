# Data Protection certificate rotation

The API stores its ASP.NET Core Data Protection key ring in PostgreSQL. Every API
instance must use the same current PFX and application name. Never replace the PFX
and remove the previous PFX in one deployment: old cookies and protected payloads
would become unreadable.

## Phase 1 — introduce the new certificate

1. Back up the current PFX and the `platform_data_protection_keys` table through the
   approved secrets and database backup systems. Do not print either secret.
2. Set `DATA_PROTECTION_CERTIFICATE_BASE64` and
   `DATA_PROTECTION_CERTIFICATE_PASSWORD` to the new password-protected PFX on every
   API instance.
3. Set `DATA_PROTECTION_PREVIOUS_CERTIFICATE_BASE64` and
   `DATA_PROTECTION_PREVIOUS_CERTIFICATE_PASSWORD` to the old PFX on every instance.
   The two previous-certificate variables are a pair; setting only one fails startup.
4. Deploy/restart all instances. Require Data Protection readiness to report `ready`
   and verify that an old authenticated session still works across different instances.
5. Generate or wait for a new active key, then restart a different instance and verify
   that a newly issued session works there. Confirm the new row contains an
   `encryptedSecret` payload; never log or copy the full XML.
6. Keep both certificates for at least the longest lifetime of every protected artifact
   plus clock/deployment margin. This includes authentication cookies, password/reset
   links, CSRF state, and any feature using `IDataProtector`. If the complete inventory
   is not documented, retain the previous certificate through the full Data Protection
   key lifetime (90 days by default) and the longest token lifetime, whichever is longer.

Rollback during phase 1: restore the old PFX as current, retain the new PFX as previous,
restart all instances, and validate readiness. Do not delete key rows during rollback.

## Phase 2 — retire the previous certificate

Proceed only after the retention window and after evidence shows all active protection
uses keys encrypted by the new certificate.

1. Take a final database backup. As the migration-owner identity, remove only key rows
   known to be encrypted by the retired certificate. Runtime identities intentionally
   have no `DELETE` privilege. Never truncate the table.
2. Restart one canary with only the current-certificate variables. Require readiness and
   verify a newly issued protected value survives another canary restart.
3. Remove both `DATA_PROTECTION_PREVIOUS_*` variables from every instance and roll out.
4. Verify readiness, login/logout, CSRF-protected writes, password reset, and
   cross-instance session continuity. Record row IDs/timestamps retained and the change
   approval; do not record certificate contents or passwords.

If the new-only canary cannot load the ring, stop the rollout. Restore the retired PFX as
the previous certificate and restore deleted rows from backup before investigating.
