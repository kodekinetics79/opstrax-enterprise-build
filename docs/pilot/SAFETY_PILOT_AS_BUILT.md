# Safety pilot as-built architecture and capability boundary

Status date: 2026-08-02. This describes current repository behavior; it is not a production-readiness claim.

## Runtime shape

```text
Client browser
  -> frontend container (Nginx, static React application)
  -> .NET API over HTTPS at the deployment edge
       -> authentication/session, CSRF, permission, entitlement and scope middleware
       -> tenant-facing handlers use the restricted opstrax_app identity
       -> background/control-plane work uses the distinct opstrax_system identity
       -> PostgreSQL forced RLS and non-forgeable tenant-ticket policies
       -> durable application, audit, key-ring, safety and telemetry records
       -> optional generic document/POD object storage where separately configured
       -> provider APIs / SMTP where explicitly configured

Owner/migration identity
  -> applies additive migrations out of band
  -> is never an API runtime identity

Operations
  <- /health/live (process), /health/ready (DB + config + fleet contract),
     /health/deep (expanded readiness), /metrics (Prometheus text)
```

The repository has production-shaped definitions in `render.yaml`, `docker-compose.yml`, the API and frontend Dockerfiles, and the owner migration runner. Those definitions are design evidence only until a deployed environment proves the effective identities, secrets, image digests, readiness output, alert delivery and data-store settings.

## Trust boundaries and enforcement

| Boundary | Primary enforcement | Required runtime proof |
|---|---|---|
| Anonymous to authenticated | JWT/session validation, login controls, rate limiting and CSRF for browser mutations | unauthenticated and invalid-session tests; CSRF rejection and successful same-session mutation |
| Platform operator to tenant user | Separate Platform routes/permissions and tenant context | tenant token denied on Platform APIs; Platform activity audited |
| Tenant to tenant | handler predicates plus forced PostgreSQL RLS | two restricted sessions, adversarial IDOR, no public/legacy RLS policies |
| Branch to branch | authoritative user/driver/vehicle branch binding plus handler predicates | two-branch positive and negative workflow tests |
| Driver self-service to manager | server-derived driver identity and narrow Driver permissions | Driver can mutate only their own allowed records; guessed IDs denied |
| Commercial plan to feature | Platform entitlement middleware and market-pack controls | explicit pilot snapshot and navigation/deep-link/API consistency |
| Runtime to schema owner | separate `opstrax_app`, `opstrax_system`, and owner/migration identities | database role/privilege evidence with values redacted |
| External provider to tenant data | tenant-bound connector/gateway/device credentials and provenance | credential isolation, replay/idempotency and degraded-provider evidence |

Stage 58 replaces forgeable tenant GUC policy behavior with security-definer ticket issuance restricted to the system identity. Production configuration validation expects exact `opstrax_app` and `opstrax_system` usernames, distinct credentials, forced tenant context, shared Data Protection material and encrypted per-device credentials. The final migration reconciliation must run after schema materialization.

## Pilot capability boundary

| Client capability | Commercial gate | Tenant/persona gate | Pilot promise |
|---|---|---|---|
| Incidents, evidence and insurance report | `safety` | Safety permissions and branch scope | Include only after create-to-close, evidence integrity and cross-branch denial pass |
| Coaching tasks/notes/acknowledgement | `safety` | Manager Safety grants; Driver self scope | Include only after full state machine and effectiveness provenance pass |
| Driver/vehicle scorecards and trends | `safety` | `safety:view`, branch scope | Decision support with visible source/formula/time; never present unknown as healthy |
| DVIR and defect repair | `maintenance` | Maintenance permissions; narrow Driver self/create | Include only after defect, out-of-service, repair certification and driver acknowledgement pass |
| HOS logs/clocks/certification | `compliance` | Compliance permissions and Driver self scope | Demonstration workflow, not a certified ELD claim; certification invalidation must pass |
| ELD device/malfunction operations | `telematics` | Compliance/telematics grants and branch scope | Include only configured provider/device states; partial HOS-without-ELD state must be explicit |
| Connectors | `integrations` plus provider-specific readiness | Provider-management permissions | Only exercised provider paths may be called supported |

These gates are independent. Enabling `safety` does not enable DVIR, HOS, ELD or integrations. Tenants newly provisioned through the Platform API use `package_allowlist`, so omitted governed modules fail closed; migrated tenants intentionally remain `legacy_allow` until reviewed. The database compatibility default is legacy mode, so out-of-band provisioning must never rely on that default. The signed pilot contract and control snapshot must identify the policy mode and effective state of every module above.

## Data and evidence invariants

- Every tenant-owned record must retain tenant scope; branch-owned Safety records must retain authoritative branch scope.
- Incident, coaching and DVIR create operations use idempotency and request-hash contracts; optimistic concurrency must reject stale updates.
- Incident evidence in this pilot stores metadata for an external HTTPS reference and a caller-supplied hash only; it does not upload, fetch, retrieve, verify, retain or manage the referenced object. Any generic document/POD object-store capability is a separate subsystem and requires its own target evidence.
- Fixture v7's seeded incident row is only synthetic telemetry metadata. Its hash
  identifies a deterministic fixture pointer; there is no object URL, retrieval,
  independent verification or managed chain of custody. It must not be used as
  proof of any uploaded/managed-evidence claim or described as “verified evidence.”
- Scorecards must expose formula/version, observation window and evaluated time sufficient to explain the score.
- HOS certification uses a source revision/snapshot and must be invalidated or protected when underlying certified logs materially change.
- Device/connector events retain observed, received and normalized/provenance time separately; stale or unavailable remains unknown/degraded, never synthetic green.
- Audit and release evidence exclude secrets and sensitive raw payloads.

## Operational boundary

Present in source:

- distinct liveness, readiness and deep-health endpoints;
- in-process Prometheus-format API/DB metrics and structured correlation data;
- background service heartbeat/incident tracking;
- production fail-closed configuration checks;
- a Neon PITR branch restore drill;
- additive migration runner, clean-chain regression and terminal RLS reconciliation.

Not proven merely by source:

- an external monitor scraping the effective deployment;
- alert delivery to the named on-call person;
- paid backup/PITR retention and a successful restore of the pilot store;
- tested production rollback and forward-fix timings;
- evidence object-store durability, retention enforcement and deletion/export procedures;
- multiple-instance behavior and shared Data Protection in the target environment.

## Explicit exclusions from the Safety pilot claim

- regulatory certification of OpsTrax as an ELD;
- legal advice or automatic compliance adjudication;
- native camera/video telematics unless a real provider path, retention and evidence controls are exercised;
- replacement of the client’s system of record or telematics provider;
- unsupported provider/hardware feeds, fabricated device health, or demo data represented as live;
- autonomous disciplinary, insurance, employment or safety decisions.
