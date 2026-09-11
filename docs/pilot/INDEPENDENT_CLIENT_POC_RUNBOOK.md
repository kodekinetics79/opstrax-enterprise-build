# OpsTrax Independent Client POC Runbook

**Purpose:** prepare a client-controlled proof of concept on the production customer URL without exposing the legacy demo tenant or presenting unsupported provider, device, camera, or regulatory capabilities as complete.

## POC boundary

The client POC is an operational software evaluation. It is not provider, hardware, ELD, camera, privacy, or regulatory certification. Those claims remain on External hold until their named evidence is captured from the real account, device, jurisdiction, and frozen release candidate.

The POC uses a new tenant created for the client. Never rename or reuse `OpsTrax Demo Logistics`, and never copy its seeded users, vehicles, camera metadata, or telemetry into the client tenant.

## Prepare the account

1. In Platform Console → Packages, create a client POC package containing only the modules agreed for the client journey. Keep unsupported provider, hardware, camera, HOS/ELD, and compliance modules outside the package unless the POC explicitly needs their honest empty or external-hold states.
2. In Platform Console → Tenants, create a new client tenant. Select the client country, the POC package, a real client contact, and a client administrator email. New tenants use `package_allowlist`, so governed modules outside the package fail closed.
3. Open the tenant and clear every item in **Independent client POC preflight**. The account must have a client identity, allowlist access, an assigned package, an operating region, and an active client administrator.
4. Create only the client roles and users needed for the agreed journeys. Grant the least permissions needed for each role. Do not add QA, synthetic, or internal demonstration users.
5. Configure outbound mail in Platform Console → Email & SMTP, save the public tenant URL, and send a real test message. Issue the administrator invitation and confirm delivery and successful password setup in the client's browser.

## Prepare the data and journeys

1. Import or create client-approved data through the product UI or supported APIs. Record the source and the client's permission to use it. Synthetic seed data is not accepted.
2. Reconcile imported counts for each included entity, such as vehicles, drivers, assignments, jobs, trips, customers, and documents. Resolve rejected rows before handover.
3. Configure only provider adapters that are in scope. **Connected** requires the backend's successful provider handshake. Entries under **Evaluation catalog** are reference-only and must remain disconnected.
4. Walk each agreed vertical journey from its starting screen through its final operational result. Confirm that role, branch, customer, tenant, and package boundaries hold on every transition.
5. Confirm truthful empty states for excluded external evidence. Camera media, device signals, provider records, and regulatory outcomes must not be simulated to make the POC look complete.

## Freeze and release

1. Merge a green candidate to `main` and record its full 40-character SHA.
2. Run the guarded production release for that exact SHA. The public frontend deployment manifest and API readiness endpoint must report the same SHA and production environment.
3. Open a fresh private browser session at the customer production URL. Sign in as the client administrator using the delivered invitation. A mismatch banner, stale state, disconnected runtime, or demo-data badge is a NO-GO.
4. Repeat the agreed client journeys with the client's role and data. Record visible results, timestamps, user role, tenant, module path, and exact release SHA.
5. Capture the tenant's audited control snapshot immediately before handover and retain it with the POC evidence.

## Handover gate

The client may run the POC independently only when all conditions below are true:

- production frontend and API report the same approved SHA;
- the client tenant is separate, named for the client, and contains no seeded demo records;
- package allowlist and least-privilege roles expose only the agreed modules;
- the administrator invitation was delivered and the client completed sign-in and password setup;
- client-approved data counts reconcile with their source;
- every agreed journey passes in a fresh signed-in browser session;
- all provider, hardware, camera, ELD, privacy, and regulatory gaps are shown as unavailable, evaluation-only, or External hold;
- the audited tenant control snapshot is captured and stored with the release evidence.

Any failed condition is a POC NO-GO for independent handover. Engineering work may continue on other modules while external evidence remains on hold.
