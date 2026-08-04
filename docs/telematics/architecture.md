# OpsTrax Telematics — Target Architecture (Full Cloud-Native)

**Status:** Target topology (proposed) · **Date:** 2026-07-12
**ADRs:** [ADR-001 plane split](./adr/ADR-001-data-plane-control-plane-split.md) · [ADR-002 event backbone](./adr/ADR-002-event-backbone-redpanda-kafka.md) · [ADR-003 storage tiers](./adr/ADR-003-storage-tiers.md) · [ADR-004 gateway hosting](./adr/ADR-004-gateway-hosting.md)

---

## 0. Verified current state (what we are moving away from)

This document is grounded in the **verified** production reality, not an aspiration:

| # | Verified fact | Source | Target ADR |
|---|---|---|---|
| 1 | Production is a **single Render HTTP `web_service`** (plus a Node `web` side service). | `render.yaml` — all services are `type: web` | ADR-004 |
| 2 | **No raw TCP listener exists anywhere.** Real GPS/ELD hardware physically cannot connect. | `render.yaml`; ingest is an HTTP `POST` only | ADR-004 |
| 3 | **One global shared gateway secret** authenticates all devices of all tenants. | `EndpointMappings.cs::GpsTrackerIngest` → `config["Telemetry:GatewaySecret"]` | ADR-004, ADR-002 (topic 13) |
| 4 | The **transactional business DB is the only store** — live state, breadcrumbs, and alerts all sit in the OLTP Neon Postgres, written synchronously in the request thread. There is **no event bus and no raw retention/replay.** | `stage12a_telemetry_live_state.sql`, `location_events`, inline ingest write | ADR-002, ADR-003 |
| 5 | **`latest_vehicle_positions` has NO provenance column.** A simulator point, a real fix, and a stale/interpolated point are indistinguishable on the live map. | `stage12a` columns (`source_channel`, `source_event_id` only); `TelemetrySimulatorBackgroundService` | ADR-003 §provenance, §4 below |

**Target posture: full cloud-native** — device-facing data plane split from the transactional control plane, Redpanda as the backbone, purpose-fit hot/warm/cold/media tiers, TCP-capable regional gateways, and provenance carried end-to-end.

---

## 1. C4 Level 1 — System Context

```mermaid
graph TB
    subgraph People
        DR(["Driver<br/>mobile app"])
        DISP(["Dispatcher / Fleet Ops<br/>live map, dispatch board"])
        CUST(["Customer<br/>portal, tracking link"])
        ADMIN(["Platform Admin<br/>tenant + device provisioning"])
    end

    OTX["<b>OpsTrax Telematics Platform</b><br/>Multi-tenant fleet / dispatch / logistics SaaS.<br/>Ingests device telemetry, tracks assets in real time,<br/>runs dispatch, safety scoring and billing."]

    subgraph Devices["Telematics hardware & feeds"]
        HW(["GPS / ELD devices<br/>Teltonika, Queclink, Concox<br/><i>raw TCP / UDP / MQTT</i>"])
        CAM(["Dashcams / snapshots<br/><i>media upload</i>"])
    end

    subgraph External["External systems"]
        SAM(["Samsara / Motive<br/>managed telematics APIs"])
        GEO(["Geocoding / map-matching<br/>provider"])
        MAIL(["SMTP / notifications"])
    end

    HW -->|"position, status, events"| OTX
    CAM -->|"clips via pre-signed upload"| OTX
    DR -->|"job status, POD, ETA"| OTX
    DISP -->|"assign, monitor, intervene"| OTX
    CUST -->|"track shipment"| OTX
    ADMIN -->|"provision tenants, devices, rules"| OTX
    OTX -->|"pulls fleet data"| SAM
    OTX -->|"reverse-geocode, snap-to-road"| GEO
    OTX -->|"alerts, digests"| MAIL

    classDef sys fill:#0b2a4a,stroke:#4dabf7,color:#e7f1ff,stroke-width:2px;
    classDef ppl fill:#0b3d2e,stroke:#12b886,color:#e6fff5;
    classDef ext fill:#2b2b2b,stroke:#868e96,color:#f1f3f5;
    class OTX sys;
    class DR,DISP,CUST,ADMIN ppl;
    class SAM,GEO,MAIL,HW,CAM ext;
```

---

## 2. C4 Level 2 — Container Diagram (Data Plane + Control Plane)

The **only** coupling between the planes is the event backbone. Neither plane reads the other's database.

```mermaid
graph TB
    HW(["GPS / ELD devices<br/>raw TCP · UDP · MQTT"])
    SAM(["Samsara / Motive API"])
    BROWSER(["Operator browser / Driver app"])

    subgraph DATA["🟢 DATA PLANE — untrusted · high-volume · stateless · TCP-capable host (ADR-004)"]
        GW["<b>Edge Gateway</b><br/>k8s Service type=LoadBalancer / NLB<br/>or Fly.io · static regional endpoints<br/>protocol decode · <b>per-device auth</b> · dedupe<br/>stamps provenance at the trust boundary"]
        CONN["<b>Connector Workers</b><br/>Samsara/Motive poll → same envelope<br/>origin=connector_*"]

        subgraph BB["<b>Event Backbone — Redpanda / Kafka (ADR-002)</b>"]
            T_RAW["otx.dp.telemetry.raw.v1"]
            T_POS["otx.dp.telemetry.position.v1 · status · enriched"]
            T_DOM["otx.dp.geofence · safety · trip · eta · alert"]
            T_LS["otx.dp.livestate.changed.v1 (compacted)"]
            T_CP["otx.cp.device.provisioned · geofence · rule · tenant (compacted)"]
        end

        NORM["<b>Normalizer</b><br/>decode → canonical envelope"]
        ENR["<b>Enricher</b><br/>reverse-geocode · map-match · geofence"]
        RULES["<b>Rules / Scoring</b><br/>safety events · alerts · trips · ETA"]
        PROJ["<b>Live-state Projector</b>"]
    end

    subgraph STORE["🟠 STORAGE TIERS (ADR-003)"]
        HOT[("<b>HOT</b> — Redis / KV<br/>latest state per asset<br/><b>+ provenance · trust_tier</b>")]
        WARM[("<b>WARM</b> — Timescale / ClickHouse<br/>partitioned time-series<br/>breadcrumbs · trips")]
        COLD[("<b>COLD</b> — Object storage<br/>raw Parquet · replay · audit")]
        MEDIA[("<b>MEDIA</b> — Object storage<br/>dashcam · POD · <b>signed URLs</b>")]
    end

    subgraph CTRL["🔵 CONTROL PLANE — trusted · transactional · Render web (HTTP)"]
        API["<b>.NET Opstrax.Api</b><br/>dispatch · jobs · finance · RBAC/RLS<br/>device provisioning · rules · tenant config"]
        OBX["<b>Outbox Relay / CDC</b><br/>atomic publish of config + credentials"]
        PG[("<b>Business DB</b> — Neon Postgres<br/>transactional state + outbox_events<br/><i>NOT a message bus</i>")]
        SPA["<b>Frontend SPA</b> — Vite/React on Vercel<br/>live map · dispatch board"]
        SSE["<b>Live-map read API / SSE</b><br/>serves from HOT, not from ingest"]
    end

    HW -->|raw L4| GW
    SAM --> CONN
    GW --> T_RAW
    CONN --> T_POS
    T_RAW --> NORM --> T_POS
    T_POS --> ENR --> T_DOM
    ENR --> PROJ --> T_LS
    T_DOM --> RULES

    T_LS --> HOT
    T_POS --> WARM
    T_RAW --> COLD
    COLD -.->|replay onto backbone| T_RAW

    API <--> PG
    PG --> OBX --> T_CP
    T_CP -->|per-device creds · rules · geofences| GW
    T_CP --> ENR & RULES

    HOT --> SSE --> SPA
    WARM --> API
    T_DOM -->|sink: safety_events · alerts| PG
    MEDIA -.->|signed URL| SPA
    BROWSER --> SPA
    BROWSER --> API

    classDef dp fill:#0b3d2e,stroke:#12b886,color:#e6fff5;
    classDef cp fill:#0b2a4a,stroke:#4dabf7,color:#e7f1ff;
    classDef st fill:#3d1f0b,stroke:#ff922b,color:#fff4e6;
    classDef bus fill:#2d0b3d,stroke:#cc5de8,color:#f8f0fc;
    class GW,CONN,NORM,ENR,RULES,PROJ dp;
    class API,OBX,PG,SPA,SSE cp;
    class HOT,WARM,COLD,MEDIA st;
    class T_RAW,T_POS,T_DOM,T_LS,T_CP bus;
```

### Container responsibilities

| Container | Plane | Responsibility | Never does |
|---|---|---|---|
| Edge Gateway | Data | Terminate raw TCP/UDP/MQTT on static regional endpoints; authenticate **per device**; decode; dedupe; stamp provenance; produce to backbone | Write the business DB |
| Connector Workers | Data | Poll Samsara/Motive, emit the same canonical envelope with `origin=connector_*` | Bypass the backbone |
| Normalizer / Enricher / Rules / Projector | Data | Stateless stream processing; sole writers into the storage tiers | Read the control-plane DB |
| Redpanda backbone | Seam | Durable, replayable, ordered log; the only cross-plane coupling | Be replaced by the business DB |
| Hot / Warm / Cold / Media | Storage | Purpose-fit serving, history, replay, blobs | Serve ingest writes directly |
| .NET `Opstrax.Api` | Control | Transactional business domain; device identity & credential issuance; rules/geofence/tenant config; publishes via outbox | Sit on the device ingest hot path |
| Outbox Relay | Control | Atomically publish config/credential changes to `otx.cp.*` | Dual-write |

---

## 3. Sequence — device ➜ map

End-to-end, from a raw socket frame to a dot rendering on the dispatcher's live map, with provenance carried the whole way.

```mermaid
sequenceDiagram
    autonumber
    participant DEV as GPS Device<br/>(raw TCP)
    participant NLB as NLB / static<br/>regional endpoint
    participant GW as Edge Gateway
    participant K as Redpanda
    participant NRM as Normalizer
    participant ENR as Enricher
    participant PRJ as Live-state<br/>Projector
    participant HOT as HOT store<br/>(Redis)
    participant SSE as Live-map API<br/>(SSE)
    participant UI as Dispatcher<br/>Live Map
    participant CP as .NET Control Plane
    participant COLD as COLD / WARM

    Note over CP,GW: Bootstrap — per-device credentials, not a global secret
    CP->>K: outbox → otx.cp.device.provisioned.v1 (IMEI, company_id, credential)
    K-->>GW: consume compacted config → device credential table

    DEV->>NLB: TCP connect :5027 (firmware-pinned endpoint)
    NLB->>GW: L4 forward (sticky socket)
    DEV->>GW: binary frame (IMEI + fix + status)
    GW->>GW: authenticate THIS device · bind company_id
    alt auth fails
        GW-->>DEV: close socket
        GW->>K: otx.dp.telemetry.deadletter.v1
    else authenticated
        GW->>GW: decode · dedupe (idempotency_key)<br/>stamp origin=device_native, trust_tier=verified
        GW->>K: produce otx.dp.telemetry.raw.v1 (key=device_id)
        GW-->>DEV: ACK
    end

    K->>COLD: tiered-storage sink → raw Parquet (replay/audit)
    K->>NRM: consume raw
    NRM->>K: otx.dp.telemetry.position.v1 (canonical envelope)
    K->>ENR: consume position
    ENR->>ENR: reverse-geocode · map-match · geofence eval
    ENR->>K: otx.dp.telemetry.enriched.v1
    ENR->>K: otx.dp.geofence.transition / safety.event (if triggered)
    K->>COLD: sink enriched → WARM time-series (breadcrumbs/trips)

    K->>PRJ: consume enriched
    PRJ->>PRJ: compute freshness (stale_seconds), risk, next_action
    PRJ->>K: otx.dp.livestate.changed.v1 (compacted, key=vehicle_id)
    K->>HOT: upsert latest state + provenance + trust_tier

    UI->>SSE: subscribe (tenant-scoped)
    HOT-->>SSE: state change
    SSE-->>UI: push {lat,lng,heading,origin,trust_tier,freshness}
    UI->>UI: render dot styled by PROVENANCE (see §4)

    Note over UI: Operator sees WHY to trust the dot —<br/>verified/live vs stale vs simulated vs interpolated
```

**Latency budget (target):** device frame → gateway ACK < 50 ms · gateway → backbone < 20 ms · backbone → hot store < 300 ms · hot → browser (SSE) < 200 ms. **End-to-end p95 device → map: < 1 s.**

---

## 4. Honest live-map provenance-state legend

> **This is the gap being closed.** Today `latest_vehicle_positions` has **no provenance column** — the live map paints every dot identically, so a `TelemetrySimulatorBackgroundService` point, a real device fix, and a 20-minute-old stale position all look like equally-trustworthy live truth. That is a *lying UI*. The target carries `origin` + `trust_tier` + `freshness` (ADR-003) from the trust boundary all the way to the pixel, and the map **must** render them distinguishably.

### 4.1 Provenance dimensions

| Dimension | Values | Stamped by |
|---|---|---|
| `origin` | `device_native` · `connector_samsara` · `connector_motive` · `manual` · `simulator` · `interpolated` | Edge Gateway / Connector / Projector |
| `trust_tier` | `verified` · `unverified` · `derived` · `synthetic` | Edge Gateway (at the auth boundary) |
| `freshness` | `live` (< 60 s) · `recent` (< 5 min) · `stale` (< 30 min) · `offline` (≥ 30 min / no fix) | Live-state Projector (from `stale_seconds`) |

### 4.2 The legend the map must render

| State | Origin / trust | Freshness | Map rendering | What the operator is being told |
|---|---|---|---|---|
| 🟢 **Live — verified** | `device_native` / `verified` | `live` | Solid filled dot, full colour, heading arrow, subtle pulse | Real authenticated hardware, fix < 60 s old. **Trust this.** |
| 🔵 **Live — connector** | `connector_*` / `unverified` | `live` / `recent` | Solid dot, **square/diamond** marker + provider badge | Real, but sourced from a third-party API — freshness is bounded by *their* poll interval, not ours. |
| 🟡 **Recent** | any real origin | `recent` | Solid dot, 70% opacity | Last fix 1–5 min ago. Probably fine; not this second's truth. |
| 🟠 **Stale** | any real origin | `stale` | Hollow dot, dashed border, **age label ("14 min")** | We have **not heard from this asset recently.** Position is where it *was*. |
| ⚫ **Offline** | any real origin | `offline` | Grey hollow dot, "last seen HH:MM", drop shadow removed | No contact ≥ 30 min. **Do not dispatch on this position.** |
| 🟣 **Interpolated / derived** | `interpolated` / `derived` | n/a | **Dashed outline, no heading arrow**, "estimated" chip | Computed/dead-reckoned, **not** an observed fix. |
| 🔴 **Simulated** | `simulator` / `synthetic` | any | **Hatched/striped fill + persistent "SIMULATED" banner** on the map canvas | Synthetic data (demo/test). **This is not a real vehicle.** Never silently blended with real assets. |
| ⚠️ **Unverified / quarantined** | unknown device or failed auth | any | Red-outlined marker in a separate "quarantine" layer, **off by default** | Traffic we could not attribute to a provisioned device (`stage27_iot_credential_quarantine`). |

### 4.3 Non-negotiable UI rules

1. **No dot without provenance.** If `origin`/`trust_tier` is absent, the marker renders as ⚠️ *unverified* — never as verified-live. Absence of provenance is itself a state, and it is not a trustworthy one.
2. **Synthetic data is never silently mixed with real.** If any `simulator`-origin asset is on the canvas, a **persistent map-level banner** says so.
3. **Freshness is always visible for anything past `live`.** Stale/offline assets must carry a visible age; the map may not imply currency it does not have.
4. **`withFallback` seed data is not a provenance state — it is a bug.** Per `CLAUDE.md`, a page rendering seed content means the live API call failed. The live map must render an explicit **"telemetry unavailable"** error state rather than seed dots that look like real assets.
5. **The legend is always reachable** from the map chrome — the operator can look up what any marker style means without leaving the board.

---

## 5. Migration sketch (strangler, no device left stranded)

| Phase | Move | Cut-over safety |
|---|---|---|
| 0 | Add `origin` / `trust_tier` / `freshness` columns + backfill `origin='unverified'`; ship the map legend against existing data | UI stops lying immediately, before any infra change |
| 1 | Stand up Redpanda + Schema Registry; add the **outbox** to `Opstrax.Api` (topics 13–16) | No traffic change; control plane just starts publishing |
| 2 | Deploy the Edge Gateway to the TCP-capable target (ADR-004); it produces to `otx.dp.telemetry.raw.v1`. **Existing HTTP `GpsTrackerIngest` keeps working, dual-writing to the backbone** | Both paths live; compare outputs |
| 3 | Stand up processors + hot/warm/cold sinks; live map reads from HOT | Shadow-read and diff against the old DB read |
| 4 | Retire the inline DB write in `GpsTrackerIngest`; retire the **global `Telemetry:GatewaySecret`** once all devices carry per-device credentials | The single-secret risk is finally gone |

---

## 6. Open questions

- Managed **Redpanda Cloud** vs self-hosted on the gateway cluster — cost vs. ops surface at 4 tenants today, but which at 40?
- Warm tier: **TimescaleDB** (keeps SQL/Postgres skills, easy joins) vs **ClickHouse** (far better at fleet-scale scans). Decide against a real device-count/ping-rate projection.
- Regional footprint for ADR-004 static endpoints — which regions do the first hardware tenants actually sit in (incl. the Saudi readiness track)?
- Data-residency: does per-tenant residency force per-region backbones and storage tiers, not just per-region gateways?
