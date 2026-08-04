# Telematics Ingestion — Distributed Tracing

**Audience:** SRE / platform / backend engineers debugging a stuck, dropped, or
mis-attributed telematics packet.
**Goal:** one `trace_id` follows a device packet from the TCP/UDP socket all the way
to the pixel on the dispatch map, so that "why is vehicle X offline?" is answered by
reading a single trace — not by grepping seven services.

Stack: **OpenTelemetry .NET** (`System.Diagnostics.Activity` + the OTel SDK) exporting
OTLP to the collector; spans and exemplars link to the Prometheus metrics in
[`metrics.md`](./metrics.md). Trace-based SLIs feed the alerts in [`slos.md`](./slos.md).

---

## 1. The span chain

Each stage is one span (`Activity`). Parent/child links are propagated in-process via
`Activity.Current` and across the queue boundary via W3C `traceparent` carried on the
event envelope (see §3). The `ActivitySource` names are stable and are what you filter on
in Grafana Tempo / Jaeger.

```
device-connection        (ActivitySource: Opstrax.Telematics.Gateway)
  └─ packet-receive       (Opstrax.Telematics.Gateway)
       └─ decode          (Opstrax.Telematics.Gateway)   ┐  in-process stages: emitted from the
            └─ identity    (Opstrax.Telematics.Gateway)  │  single gateway source, since decode/
                 └─ validation        (Opstrax.Telematics.Gateway) │ identity/validation/publish run
                      └─ event-publish (Opstrax.Telematics.Gateway) ┘ in one component (see PipelineTrace)
                                                                   ── async queue boundary ──▶
                           └─ projection (Opstrax.Telematics.Projection)  [new trace linked by span link]
                                └─ db     (Opstrax.Telematics.Projection)
                                     └─ sse-fanout / api-read (Opstrax.Telematics.Delivery)
                                          └─ frontend-render (browser OTel web SDK)
```

> **Source naming (implemented).** The gateway emits `packet-receive → decode → identity →
> validation → event-publish` from the single `Opstrax.Telematics.Gateway` source
> (`PipelineTrace` in `telematics/src/Opstrax.Telematics.Gateway/Observability/`), because those
> stages run in-process in one component. The per-stage `Opstrax.Telematics.Decode/.Identity/…`
> sources are the *target* once stages split into separate services; register whichever sources
> exist. Casing is `Opstrax.*` (see the reconciliation note in [`metrics.md`](./metrics.md)).

| # | Span name | Source component | Span kind | What it wraps |
|---|-----------|------------------|-----------|---------------|
| 1 | `device-connection` | Protocol gateway | `SERVER` | Lifetime of the TCP/UDP connection from a PT40/GT06-class tracker. Long-lived; child spans nest under it per packet. |
| 2 | `packet-receive` | Protocol gateway | `INTERNAL` | Reading one framed packet off the socket + forwarding to `POST /api/telemetry/gps-ingest`. |
| 3 | `decode` | Decode/adapter | `INTERNAL` | Adapter parses raw bytes → normalized reading (lat/lon, speed, engine, DTC). Records `adapter.name`/`adapter.version`. |
| 4 | `identity` | Identity resolver | `INTERNAL` | IMEI/device-key → `device_id` → `vehicle_id` → tenant/`company_id`. HMAC verification happens here. |
| 5 | `validation` | Validation | `INTERNAL` | Checksum, freshness/replay window, nonce uniqueness, ordering, tenant-bind check, timestamp bounds. |
| 6 | `event-publish` | Ingest | `PRODUCER` | Writes the accepted reading as an event onto the internal queue/stream. Injects `traceparent` into the envelope. |
| 7 | `projection` | Projection worker | `CONSUMER` | Consumes the event, computes `latest_vehicle_positions` / `telemetry_live_asset_states` deltas. New trace, linked to #6. |
| 8 | `db` | Projection worker | `CLIENT` | The actual Postgres upsert into `location_events` + `latest_vehicle_positions`. |
| 9 | `sse-fanout` / `api-read` | Delivery | `SERVER` | Push over `/api/telemetry/stream` (SSE) and/or serve a positions-snapshot GET. |
| 10 | `frontend-render` | Browser (web OTel) | `CLIENT` | Time from SSE receipt to map marker paint on `DispatchCommandPage`. |

> **Trace continuity across the queue.** Spans 1–6 share one `trace_id`. At the async
> boundary the projection worker starts a **new** root span (#7) and attaches a **span
> link** back to the `event-publish` span, so end-to-end latency stays queryable without
> creating unbounded long-lived producer spans. `correlation_id` (see below) is the join
> key that survives the boundary as a plain attribute for humans and log correlation.

---

## 2. Propagated fields (span attributes)

Every span in the chain carries the fields below where they are known. Names follow OTel
semantic-convention style (`namespace.key`, snake within the last segment). Fields absent
at a given stage are simply not set (never faked — mirrors the honest-`—` rule the rest of
the telematics stack follows).

| Attribute | Type | First set at | Meaning / use |
|-----------|------|--------------|---------------|
| `trace_id` | id (W3C) | `device-connection` | Root correlation for spans 1–6; the primary search key. |
| `telematics.correlation_id` | string (uuid) | `packet-receive` | Stable per-packet id that **crosses the queue boundary** (spans 1–10) and is stamped on every log line. Join key when `trace_id` splits at #7. |
| `telematics.connection_id` | string | `device-connection` | Gateway socket/session id. Groups all packets from one physical connection; use to spot a flapping device. |
| `telematics.event_id` | string (uuid) | `event-publish` | Identity of the published event; primary key echoed into `location_events`. De-dup key. |
| `tenant.id` / `company.id` | string | `identity` | The resolved tenant (`company_id`). **Required** on spans 4–9. A span reaching `db` without it is a cross-tenant defect (alert in slos.md). |
| `device.id` | string | `identity` | Resolved `eld_devices` / tracker id. |
| `device.imei` | string | `packet-receive` | Lookup key only, never a credential; useful pre-identity. |
| `vehicle.id` | string | `identity` | Resolved OpsTrax vehicle. |
| `adapter.name` | string | `decode` | e.g. `pt40`, `gt06`, `samsara-webhook`. |
| `adapter.version` | string | `decode` | Adapter/codec version — pins a decode regression to a release. |
| `telematics.protocol` | string | `packet-receive` | Wire protocol (`pt40`, `gt06`, `j1939`, `obd2`, `mqtt`). |
| `telematics.processing_latency_ms` | double | each span (as duration) | Per-stage wall time; also emitted as a metric. Span duration is authoritative. |
| `telematics.rejection_reason` | string (enum) | `validation` / `identity` | Set only on rejection: `bad_checksum`, `replay`, `stale`, `out_of_order`, `duplicate`, `unknown_device`, `auth_failed`, `tenant_mismatch`, `malformed`, `rate_limited`. Drives `otel.status_code=ERROR`. |
| `telematics.queue_lag_ms` | double | `projection` | Now − event enqueue time. How far behind the projection worker is running. |
| `telematics.projection_lag_ms` | double | `projection` / `db` | Event timestamp → position committed. Feeds projection-freshness SLO. |
| `messaging.destination` | string | `event-publish`/`projection` | Queue/stream/topic name. |
| `db.system` / `db.statement` | string | `db` | `postgresql`; statement is parameterized (no PII/coords in the statement text). |
| `http.route` | string | `sse-fanout`/`api-read` | `/api/telemetry/gps-ingest`, `/api/telemetry/stream`, positions snapshot. |
| `otel.status_code` | enum | any | `ERROR` whenever `telematics.rejection_reason` is set or an exception is recorded. |

**Rejections are first-class spans, not dropped work.** A packet failing checksum still
produces spans 1–5 with `otel.status_code=ERROR` and `telematics.rejection_reason` set, so
"packets that vanish" are visible in Tempo. The corresponding counter increments carry an
**exemplar** pointing at the failing `trace_id`.

---

## 3. Context propagation mechanics

- **In-process (spans 1–6, 7–10):** ambient `Activity.Current`. Register each
  `ActivitySource` with `AddSource(...)` in the OTel `TracerProviderBuilder`.
- **Across the queue (span 6 → 7):** the ingest publisher injects W3C
  `traceparent` + `tracestate` into the event envelope headers using
  `TextMapPropagator` (`Propagators.DefaultTextMapPropagator`). The projection worker
  extracts them and starts span 7 with a **span link** (not a parent) to keep the
  consumer trace independent but navigable.
- **Server → browser (span 9 → 10):** the SSE event payload includes `trace_id` +
  `correlation_id` as data fields; the browser OTel web SDK starts `frontend-render` as a
  linked span. This closes the "device → pixel" loop for the map on `DispatchCommandPage`.
- **HMAC / gateway auth** (`X-Gateway-Signature`, `X-Device-Key`) is verified inside the
  `identity` span; on failure the span is `ERROR` with `rejection_reason=auth_failed` and
  **no** device/vehicle/tenant attributes are set (nothing to attribute yet).

---

## 4. .NET wiring (reference)

The gateway registers its single source through the raw SDK builder (minimal package set — see
`TelematicsObservabilityExtensions.AddTelematicsObservability`); a service that also runs
ASP.NET Core / Npgsql adds those instrumentations and the remaining sources:

```csharp
// Gateway (implemented): raw SDK builder, one source, OTLP only when an endpoint is configured.
Sdk.CreateTracerProviderBuilder()
    .SetResourceBuilder(ResourceBuilder.CreateDefault()
        .AddService("opstrax.telematics.gateway", serviceVersion: TelematicsInstrumentation.Version))
    .AddSource("Opstrax.Telematics.Gateway")
    .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(1.0)))  // head-sample tuning lives in the collector
    .AddOtlpExporter()                 // attached only when the endpoint is set; no-op otherwise
    .Build();

// Downstream services add the remaining (target) sources + framework instrumentation:
//   .AddSource("Opstrax.Telematics.Projection", "Opstrax.Telematics.Delivery")
//   .AddAspNetCoreInstrumentation().AddNpgsql()
```

```csharp
// A stage span via the implemented PipelineTrace helper (Gateway/Observability/PipelineTrace.cs):
using var trace = PipelineTrace.StartPacketReceive(correlationId, connectionId, "gt06");
trace.SetAdapter(adapter.Name, adapter.Version);

using (var decode = trace.StartDecode()) { /* … */ }

trace.ResolveOwnership(owner.TenantId, owner.CompanyId, owner.DeviceId, owner.VehicleId);

using (var validation = trace.StartValidation())
{
    if (!checksumOk)
    {
        trace.MarkRejected(validation, "bad_checksum", "checksum mismatch");   // reason + otel.status=ERROR on span & parent
        TelematicsInstrumentation.PacketsRejected.Add(1, trace.MetricLabels("bad_checksum")); // metric + exemplar(trace_id)
        return Reject.BadChecksum;
    }
}
```

**Sampling policy.** Head sample healthy traffic at ~10% (`TraceIdRatioBasedSampler`), but
**tail-sample keep-100%** for any trace where `otel.status_code=ERROR`,
`rejection_reason` is set, `queue_lag_ms` > threshold, or a cross-tenant mismatch appears.
Configure this in the OTel Collector `tail_sampling` processor so every failure is
retained even when the overall rate is sampled down.

---

## 5. How to debug in <60s (trace path)

1. Symptom: "vehicle 42 is stale on the map." Grafana Tempo → search
   `vehicle.id=42` (or `device.imei=...`), last 15 min.
2. Look at the most recent trace's **last span**:
   - stops at `validation` with `rejection_reason` → packet arriving but rejected (why is on the span).
   - stops at `identity` with `auth_failed` → device key/HMAC or provisioning issue.
   - stops at `event-publish`, never a linked `projection` → queue/worker stall
     (confirm with `queue_lag_ms` metric and DLQ, slos.md).
   - reaches `db` but `sse-fanout` missing → delivery/SSE problem, positions are fresh in DB.
   - full chain green but old `event.timestamp` → device silent; not a platform fault.
3. Pivot from the metric alert to the trace via the **exemplar** on the firing series.

See [`metrics.md`](./metrics.md) for the counters/histograms referenced above and
[`slos.md`](./slos.md) for the alert rules that page on these conditions.
