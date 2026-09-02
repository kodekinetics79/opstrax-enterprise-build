# G2 provider access and registration ledger

**Owning gates:** G2A / #115 and G2B / #116  
**Purpose:** obtain authentic provider sandboxes, organizations, API authority, commercial terms, and test devices without activating additional major workstreams or promoting capability truth

Registration is dependency acquisition, not certification evidence. A submitted form, developer account, catalog listing, provider name, sandbox, or API document does not prove production compatibility. Provider-specific evidence begins only after OpsTrax receives authorized access and records the exact organization, scopes, contract boundary, provider responses, device/firmware where applicable, and independent acceptance.

On 2026-09-02, the Motive developer-registration form accepted the program-owner details and displayed a confirmation that a verification email was sent to the registered work address. The authenticated Motive Developer Portal subsequently displayed an OpsTrax app with App ID `87892`, status `draft`, creator Zack Khan and creation time Sep 02, 2026 at 10:24:09 AM. The app exposes OAuth client credentials and a selectable read-only scope catalog, including vehicle, location, ELD and HOS domains, but no credential value is retained in this repository or evidence ledger. At inspection time the success redirect URI was empty, no requested scope was visibly selected, distribution metadata was incomplete and Submit for Review was disabled. No provider review, marketplace approval, customer authorization, sandbox/customer data, token exchange, API response, commercial right, exact ELD/device boundary or regulatory status has been verified. This remains administrative access evidence only.

| Provider | Intended evaluation | Official access path | Current status | Evidence still required |
|---|---|---|---|---|
| Samsara | G2A telematics connector; optional future marketplace submission | Developer/partner registration | SUBMITTED / UNVERIFIED | Provider confirmation, developer organization or customer tenant, scopes/token, commercial rights, real responses, exact-SHA deployment and journey |
| Motive | G2B ELD/HOS plus telematics and dual-facing camera option | https://developer.gomotive.com/portal/dashboard/app/87892 | DRAFT DEVELOPER APP PROVISIONED / OAUTH UNTESTED | Approved callback design, least-privilege scope selection, secret storage and rotation, provider/customer authorization, test organization/data, token exchange and real responses, distribution review, commercial rights, exact ELD/device/firmware, U.S./Canada status and end-to-end evidence |
| Geotab | Provider-neutral telematics, custom-device/OEM path, ELD/HOS candidate | https://my.geotab.com/registration.html | FORM READY / NOT SUBMITTED | Demo database, dedicated scoped API user, reseller/commercial terms, exact GO device/app boundary where ELD is evaluated, official status and real feed evidence |
| Platform Science | Enterprise/OEM in-cab, HOS and media ecosystem candidate | https://www.platformscience.com/developer-portal-form | FORM READY / NOT SUBMITTED | Developer-program acceptance, integration environment, SDK/API rights, marketplace/certification terms, exact app/device/provider boundary and regulatory evidence |

## Submission-build rule

The subsequent owner-requested Motive configuration/test preparation is recorded in [G2B Motive OAuth smoke-test readiness](G2B_MOTIVE_OAUTH_SMOKE_READINESS.md). A later Chrome inspection showed 57/59 portal permissions selected and the callback still blank. The narrow software harness requests nine read-only scopes, but no portal settings, secrets, live grant, or provider test have been completed. This candidate-evaluation preparation does not select a production partner, activate Wave 4, close G2B, or replace G2A Samsara.

Provider applications may describe OpsTrax as a connected-fleet platform seeking integration access. They must not state that an unverified connector, ELD/HOS product, camera pipeline, device family, or marketplace app is certified or production ready.

Before any app submission, retain:

1. architecture and data-flow diagram;
2. least-privilege scope inventory;
3. tenant-isolation and credential-lifecycle evidence;
4. connect, discover, map, validate, sync, monitor, disconnect and recovery journeys;
5. privacy, retention, deletion and support policies;
6. exact build/SHA and dependency provenance;
7. provider sandbox/real-account evidence and rate-limit/backfill behavior;
8. provider-specific test results plus qualified independent acceptance.

## Concurrency control

These registrations remain administrative dependency work inside G2A/G2B. They do not activate Geotab, Motive, Platform Science, HOS, camera, video-safety, or other future gates as additional major workstreams. Provider-specific implementation begins only after a controlled selection/activation record identifies the owning gate and evidence boundary.
