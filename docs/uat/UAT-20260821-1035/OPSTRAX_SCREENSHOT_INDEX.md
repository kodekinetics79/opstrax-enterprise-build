# OpsTrax Screenshot Index

| No. | Filename | Module | Action | Expected result | Actual result | Test ID | Status |
|---:|---|---|---|---|---|---|---:|
| 001 | `001_Authentication_Login_Blocked.png` | Authentication | Open public OpsTrax target in connected Chrome | Dedicated staging context is available or can be selected | Production-classified public login requires organization code and work email; no safe staging session is available | G0-003 / G0-005 | BLOCKED |
| 002 | `002_Staging_Vercel_Access_Blocked.png` | Staging access | Open isolated staging preview in connected Chrome | Staging application loads | Vercel access protection redirects to Vercel login | G0-005 | BLOCKED |
| 003 | `003_Platform_Admin_UAT_Tenant_Authenticated.png` | Platform Admin | Provision the controlled UAT tenant after CORS/database repair | Staff tenant view displays only the run-labeled disposable tenant | Authenticated tenant-management view displays `UAT UAT-20260821-1035 Logistics` (`T-C221B5BA`) | G0-005 / G1-001 | PASS |
| 004 | `004_Tenant_Admin_Fleet_Overview_Authenticated.png` | Tenant authentication | Sign in with the disposable Company Admin after Stage 83 repair | Tenant session is issued and fleet landing page renders | Login succeeded and landed on `/live-dashboard` for `UAT UAT-20260821-1035 Logistics` | G1-002 | PASS |

Screenshot 001 contains a synthetic test email already present in the connected browser. No indexed screenshot contains a password, token, session cookie, or API secret.
