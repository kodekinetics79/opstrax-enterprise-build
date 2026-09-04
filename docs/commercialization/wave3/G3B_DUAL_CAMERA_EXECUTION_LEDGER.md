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

## External evidence still required

- authorized Samsara organization/account/token with Safety & Cameras scopes and written commercial integration rights;
- exact CM34 hardware/firmware identity and intended-market installation evidence;
- authentic safety events and authentic road/driver video retrieval;
- real privacy/retention/access behavior;
- failure/recovery and provider latency/availability evidence;
- independent Privacy + Security + Principal SDET + Driver Safety Product acceptance.

Until those pass, Dual-facing camera remains **ROADMAP** and no customer-facing video capability is certified.
