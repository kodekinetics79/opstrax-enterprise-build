# G3B — Dual-Facing Camera Integration Execution Ledger

Parent: #110  
Gate: #129  
Activation: `CR-2026-09-03-01`  
Entry baseline: `main@aba2636c543c6f77cb47597383d4c2c8c32e61c8`  
Commercial truth at start: Dual-facing camera **ROADMAP**; Video Safety **ROADMAP**.

## Stage 1 provider-path decision

**Primary evidence-acquisition candidate:** Samsara CM34 + Samsara Safety & Cameras API  
**Contingency #1:** Motive dual-facing AI Dashcam path (DC54 / current DC64 family as applicable)  
**Decision type:** architecture/provider-path selection only — not certification, commercial approval or production support.

### Current official-source evidence supporting Samsara primary

- Samsara identifies CM34 as the current dual-facing road + driver camera model; CM33 is road-facing only and older CM31/CM32 models are previous-generation/end-of-sale.
- Safety Events Stream exposes provider safety events and can include video media when the account/token has the applicable camera-media permission.
- Camera Media Retrieval API supports asynchronous image and high/low-resolution video retrieval for `dashcamRoadFacing` and `dashcamDriverFacing` inputs.
- Offline devices may upload requested media after reconnect; retrieval quotas and rate limits apply.
- Media URLs returned by retrieval are temporary, so OpsTrax must treat provider URLs as short-lived access references rather than durable evidence objects.
- CM34 privacy/recording modes distinguish full dual-facing recording, driver privacy mode and complete privacy mode, giving G3B an explicit privacy-state concept to preserve.

Official sources:
- https://kb.samsara.com/hc/en-us/articles/31096594646925-Visually-Identify-your-Dash-Cam-Model
- https://developers.samsara.com/me/reference/getsafetyeventsv2stream
- https://developers.samsara.com/reference/postmediaretrieval
- https://developers.samsara.com/reference/getmediaretrieval
- https://kb.samsara.com/hc/en-us/articles/35669901967373-Dash-Cam-Recording-Status-Visibility-for-Drivers-CM33-CM34

### Motive contingency evidence

- Motive documents DC54 as dual-facing and its newer AI Dashcam Plus DC64 family as dual-facing.
- Driver Performance Events API documents events including hard acceleration/brake/corner, crash, seat-belt, tailgating, cell-phone, distraction, unsafe lane change, drowsiness, camera obstruction and forward-collision warning, with downloadable event-video references where available.
- Camera-control endpoints allow authorized fleets to turn camera recording on/off and expose a privacy-control integration surface.
- Image-capture API supports front-facing and driver-facing positions when the required feature is enabled.

Official sources:
- https://developer-docs.gomotive.com/docs/step-2-extract-the-driver-performance-events
- https://developer-docs.gomotive.com/reference/invoke-camera-control-job
- https://developer-docs.gomotive.com/reference/poll-the-status-of-the-camera-control
- https://developer-docs.gomotive.com/reference/begin-image-capture

## G3B first engineering package

Proceed without fabricating provider data:

1. canonical `VideoSafetyEvent` envelope with provider, provider-event ID, occurred-at, received-at, vehicle/driver/trip/location identities, event type, severity, confidence/quality metadata and source provenance;
2. idempotent provider-event ingestion contract and replay key;
3. `CameraMediaReference` abstraction that stores provider/retrieval identity, camera role, media type, request/status/expiry metadata and access policy — never a fake playable URL;
4. tenant/branch/vehicle/driver authorization and audit for event/media reads;
5. retention/privacy policy model including recording mode, driver-facing permission, retention class, legal hold and deletion state;
6. provider-pending UI states that render metadata without pretending a clip exists;
7. exact event -> vehicle -> driver -> trip -> time -> location reconciliation rules;
8. observability for webhook/poll lag, duplicate events, media-retrieval queue age, provider errors and expired URLs;
9. SDET contract cases for duplicate/reordered events, missing driver, late assignment, cross-tenant ID collision, offline retrieval, expired media URL and revoked access;
10. visible Chrome responsive/overflow/accessibility acceptance after a real provider event is available.

## 2026-09-07 BUILD increment — provider intake spine

Status: **BUILD COMPLETE / INTEGRATE PARTIAL / CERTIFY EXTERNAL HOLD**

Stage 112 and `CameraProviderIngestService` now provide a provider-neutral intake boundary for exact authenticated payload bytes. The service calculates its own SHA-256 fingerprint, scopes the idempotency identity by tenant + provider + provider account + provider event, serializes concurrent duplicates, and quarantines a reused identity carrying different bytes. It retains opaque media identifiers, expiry, camera role, recording mode, retention and privacy metadata without storing a URL, signed query string, media bytes, AI conclusion or playable-media claim.

The intake relations are FORCE-RLS control-plane tables. `opstrax_system` receives the minimum read/insert/update path; `opstrax_app` receives no direct provider-identity access. Internal branch/vehicle/driver/trip references are checked against the owning tenant before persistence. A later replay may add a valid driver assignment, while a cross-tenant identity is quarantined and not retained as a linked vehicle or driver.

Independent test coverage currently proves:

- exact payload hashing and bounded payload admission;
- UTC/future-time rejection;
- URL and signed-link rejection at the media-identity boundary;
- expired-media state without access promotion;
- one stored identity and exact replay count under eight simultaneous deliveries;
- same-payload replay and late driver assignment;
- different-payload reuse quarantine without overwriting the first fingerprint;
- provider-account separation for reused provider event IDs;
- cross-tenant reference quarantine;
- repeat-safe migration application, FORCE RLS, system-only grants and permanent `ExternalHold` constraints.

This increment does **not** establish provider authenticity, ingest a real Samsara event, retrieve or display media, create an authoritative `dashcam_events` record, or satisfy Chrome/customer acceptance. The next INTEGRATE package must connect an authenticated provider adapter, add the safe provider-pending customer projection and reconciliation observability, and exercise it with authentic account data. Certification remains blocked on the external evidence listed below.

The customer camera page now reads a separate tenant/branch-scoped operational status projection from the system-only intake ledger. It shows whether the scope has received any provider intake records, how many are matched, unmatched or quarantined, how many opaque media references remain pending or expired, and the last provider receipt time. It always reports provider verification, media availability and certification as unavailable/`ExternalHold`. Provider accounts, event IDs, payload hashes and media identifiers are never returned by this endpoint. An account with no intake records now says that it has no provider-backed camera evidence instead of implying an empty-but-connected camera service.

This completes the safe status/observability slice only. A provider-pending camera event will not appear in the customer event table until a real authenticated adapter has parsed an authentic provider response and the customer projection is proven against that provider account.

## 2026-09-07 INTEGRATE increment — Samsara safety-event intake adapter

Status: **INTEGRATE CODE COMPLETE / PROVIDER EXECUTION EXTERNAL HOLD / CERTIFY EXTERNAL HOLD**

The registered Samsara connector now has a separate bounded camera-safety lane using the Safety Events Stream with an `updatedAtTime` cursor. It parses exact provider event bytes, rejects ambiguous asset identities, malformed timestamps, duplicate IDs and broken pagination, then sends the accepted envelope to the Stage 112 protected ledger. Every provider write revalidates the exact connector generation and lease inside the same transaction, so a credential change or disconnect during a remote request leaves no stale-generation ledger row. The camera cursor, completion time and result are stored separately from GPS health, so a missing camera permission cannot mark working telematics as failed. Credential replacement clears both provider cursors, and the intake account reference includes the connector generation so evidence from different credential generations cannot collide.

The Samsara configuration journey now exposes a distinct **Intake camera safety events** action and refreshes the customer camera status projection after it runs. The result shows observed, accepted, replayed and quarantined counts while continuing to state that media, provider verification, privacy acceptance and certification are on External hold. The adapter does not ingest signed media URLs and does not manufacture a playable clip or authoritative safety event.

The camera page also exposes a separate, read-only provider-intake queue for the current tenant and branch. It shows the event classification, occurrence time, safe matched vehicle label and reconciliation state while suppressing provider accounts, provider event IDs, external asset identities, payload hashes and media identifiers. Quarantined classifications are shown as unavailable. The queue has no review, coaching, export or evidence action and remains visibly marked External hold.

Verification for this increment uses controlled provider-response fixtures plus the isolated Stage 112 PostgreSQL role. Those fixtures prove parser, retry, cursor, quarantine, independent GPS/camera state and customer-copy behavior; they are software evidence only. No authorized Samsara account was contacted and no real camera event, media object, device or customer journey is claimed by this increment.

## External evidence still required

- authorized Samsara organization/account/token with Safety & Cameras scopes and written commercial integration rights;
- exact CM34 hardware/firmware identity and intended-market installation evidence;
- authentic safety events and authentic road/driver video retrieval;
- real privacy/retention/access behavior;
- failure/recovery and provider latency/availability evidence;
- independent Privacy + Security + Principal SDET + Driver Safety Product acceptance.

Until those pass, Dual-facing camera remains **ROADMAP** and no customer-facing video capability is certified.
