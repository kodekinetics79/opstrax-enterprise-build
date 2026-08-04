# Telematics Ingestion — Metric Catalog

**Audience:** SRE / on-call building Grafana dashboards and Prometheus alerts.
**Stack:** **OpenTelemetry .NET Metrics API** (`System.Diagnostics.Metrics.Meter`)
exported via the OTLP/Prometheus exporter and scraped by **Prometheus**, visualized in
**Grafana**. Every counter/histogram here is emitted with **exemplars** so a spiking
series links straight to a `trace_id` (see [`tracing.md`](./tracing.md)); the SLIs in
[`slos.md`](./slos.md) are all derived from these series.

## Conventions

- **Meter name:** `Opstrax.Telematics` is the system-wide family; each emitting component
  registers a component-scoped meter under it. The **Edge Gateway** registers
  `Opstrax.Telematics.Gateway` (see `TelematicsInstrumentation` in
  `telematics/src/Opstrax.Telematics.Gateway/Observability/`). Instrument names use
  `opstrax_telematics_*` regardless of which component meter emits them, so they aggregate
  cleanly in Prometheus (the exporter appends `_total` to counters and `_bucket/_sum/_count`
  to histograms). **Casing note:** earlier drafts of this doc spelled the product
  `OpsTrax.*`; the shipped assemblies are `Opstrax.*` (lowercase `t`) and the registered
  meter/source names follow the assembly. See §Reconciliation.
- **Units:** durations are milliseconds (histograms), sizes are bytes, everything else is a
  raw count/gauge. Units are declared on the instrument.
- **Instrument types:** `Counter` (monotonic), `UpDownCounter` (can decrease),
  `ObservableGauge` (sampled), `Histogram` (latency/size distribution).
- **Label cardinality discipline:** `device_id`, `imei`, `vehicle_id`, `correlation_id`,
  `trace_id` are **NEVER** metric labels (unbounded) — they live on spans/exemplars only.
  Metric labels are bounded dimensions (`company_id`, `protocol`, `adapter`, `reason`,
  `gateway`). `company_id` is bounded (4 real tenants) and is intentionally a label so
  every series is tenant-sliceable.
- **Standard label set** (applied where meaningful): `company_id`, `protocol`
  (`pt40|gt06|j1939|obd2|mqtt`), `adapter` (name), `adapter_version`, `gateway`
  (gateway instance/pod).

---

## 1. Connection & auth

| Metric | Type | Labels | Notes |
|--------|------|--------|-------|
| `opstrax_telematics_active_connections` | UpDownCounter | `gateway`, `protocol` | Currently-open device sockets. Gauge-like. |
| `opstrax_telematics_connection_attempts_total` | Counter | `gateway`, `protocol`, `result` (`accepted\|refused`) | New inbound connection attempts. |
| `opstrax_telematics_connection_disconnects_total` | Counter | `gateway`, `protocol`, `reason` (`client_close\|timeout\|error\|shutdown`) | Feeds disconnect-rate SLO. |
| `opstrax_telematics_auth_failures_total` | Counter | `gateway`, `protocol`, `reason` (`bad_hmac\|expired_ts\|bad_nonce\|unknown_key`) | HMAC / gateway-signature rejects. |
| `opstrax_telematics_unknown_devices_total` | Counter | `gateway`, `protocol` | Well-formed, authenticated-ish packet whose IMEI/key resolves to no provisioned device. Spikes = scan/misconfig. |

## 2. Packet processing & decode

| Metric | Type | Labels | Notes |
|--------|------|--------|-------|
| `opstrax_telematics_packets_accepted_total` | Counter | `company_id`, `protocol`, `adapter` | Packets that passed all validation. |
| `opstrax_telematics_packets_rejected_total` | Counter | `protocol`, `adapter`, `reason` | `reason` matches span `rejection_reason` enum. `company_id` only when resolved. |
| `opstrax_telematics_packets_received_total` | Counter | `gateway`, `protocol` | Raw packets off the wire (pre-decode). accepted+rejected+unknown reconcile against this. |
| `opstrax_telematics_decode_latency_ms` | Histogram | `protocol`, `adapter`, `adapter_version` | Decode span duration distribution. Buckets: 1,2,5,10,25,50,100,250,500,1000. |
| `opstrax_telematics_e2e_latency_ms` | Histogram | `protocol`, `company_id` | **Gateway-emitted** receive→publish wall time — the end-to-end span the gateway can actually observe (it never sees the DB commit). Buckets to 5000. Emitted by `TelematicsInstrumentation`. |
| `opstrax_telematics_processing_latency_ms` | Histogram | `protocol`, `company_id` | **Projection-worker-emitted** receive→committed (adds queue + projection + DB). P95/P99 SLO source (slos.md SLO 2/3). Buckets to 5000. |
| `opstrax_telematics_invalid_checksums_total` | Counter | `protocol`, `adapter` | Subset of rejects; broken out because it flags wire corruption vs firmware bugs. |
| `opstrax_telematics_duplicate_packets_total` | Counter | `protocol`, `company_id` | Same `event_id`/nonce seen again. |
| `opstrax_telematics_replay_rejections_total` | Counter | `protocol`, `company_id` | Timestamp outside freshness window / nonce reuse → security-relevant. |
| `opstrax_telematics_out_of_order_total` | Counter | `protocol`, `company_id` | Reading older than the last committed position for that vehicle. |

## 3. Queue, events & durability

| Metric | Type | Labels | Notes |
|--------|------|--------|-------|
| `opstrax_telematics_queue_lag_ms` | ObservableGauge | `queue`, `partition` | Now − oldest un-consumed enqueue time. Projection backlog signal. |
| `opstrax_telematics_queue_depth` | ObservableGauge | `queue`, `partition` | Un-consumed event count. |
| `opstrax_telematics_events_published_total` | Counter | `company_id`, `protocol` | Accepted readings published to the stream. |
| `opstrax_telematics_dropped_events_total` | Counter | `stage` (`publish\|projection\|delivery`), `reason` | Events lost/shed anywhere in the pipeline. Durability SLO source — target ~0. |
| `opstrax_telematics_dead_letter_total` | Counter | `stage`, `reason` | Events routed to DLQ after retry exhaustion. |
| `opstrax_telematics_dead_letter_depth` | ObservableGauge | `queue` | Current DLQ backlog size. |

## 4. Projection & fleet freshness

| Metric | Type | Labels | Notes |
|--------|------|--------|-------|
| `opstrax_telematics_projection_lag_ms` | Histogram | `company_id` | Event timestamp → `latest_vehicle_positions` committed. Freshness SLO. |
| `opstrax_telematics_latest_position_age_seconds` | ObservableGauge | `company_id` | Age of newest position per tenant (max/quantile). Sampled from `latest_vehicle_positions`. |
| `opstrax_telematics_fleet_online_count` | ObservableGauge | `company_id` | Vehicles reporting within online window. |
| `opstrax_telematics_fleet_stale_count` | ObservableGauge | `company_id` | Reporting within stale window but past online (e.g. 5–15 min). |
| `opstrax_telematics_fleet_offline_count` | ObservableGauge | `company_id` | No fix past stale window (~15 min, matches decay behavior). |

## 5. Commands (device → server control path)

| Metric | Type | Labels | Notes |
|--------|------|--------|-------|
| `opstrax_telematics_command_sent_total` | Counter | `company_id`, `protocol`, `command` | Outbound commands (locate, immobilize, config push). |
| `opstrax_telematics_command_success_total` | Counter | `company_id`, `protocol`, `command` | ACKed by device. |
| `opstrax_telematics_command_failure_total` | Counter | `company_id`, `protocol`, `command`, `reason` (`timeout\|nack\|offline\|error`) | Failure-rate SLO source. |
| `opstrax_telematics_command_latency_ms` | Histogram | `protocol`, `command` | Send → ACK round trip. |

## 6. Connector / gateway health & platform

| Metric | Type | Labels | Notes |
|--------|------|--------|-------|
| `opstrax_telematics_connector_health` | ObservableGauge | `connector`, `company_id` | 1=healthy, 0=down per upstream connector (Samsara/Motive/Geotab/gateway). |
| `opstrax_telematics_connector_poll_errors_total` | Counter | `connector`, `reason` | Failed provider polls/webhook rejects. |
| `opstrax_telematics_gateway_cpu_ratio` | ObservableGauge | `gateway` | 0–1 CPU utilization of the gateway process/pod. |
| `opstrax_telematics_gateway_memory_bytes` | ObservableGauge | `gateway` | RSS of the gateway process. |
| `opstrax_telematics_gateway_net_bytes_total` | Counter | `gateway`, `direction` (`rx\|tx`) | Network throughput on the gateway. |

## 7. Tenant usage & storage

| Metric | Type | Labels | Notes |
|--------|------|--------|-------|
| `opstrax_telematics_tenant_ingest_rate` | ObservableGauge | `company_id` | Accepted packets/sec per tenant. Billing/abuse + capacity. |
| `opstrax_telematics_tenant_active_devices` | ObservableGauge | `company_id` | Devices that reported in the trailing window. |
| `opstrax_telematics_storage_bytes` | ObservableGauge | `company_id`, `table` (`location_events\|latest_vehicle_positions\|telemetry_live_asset_states`) | Telemetry storage volume per tenant/table. Retention & cost signal. |

---

## .NET wiring (reference)

This is the **implemented** gateway meter (`telematics/src/Opstrax.Telematics.Gateway/Observability/TelematicsInstrumentation.cs`):

```csharp
public static class TelematicsInstrumentation
{
    public const string MeterName = "Opstrax.Telematics.Gateway";   // component-scoped, under the Opstrax.Telematics family
    public static readonly Meter Meter = new(MeterName, Version);

    public static readonly Counter<long> PacketsAccepted =
        Meter.CreateCounter<long>("opstrax_telematics_packets_accepted", unit: "{packet}");
    public static readonly Counter<long> PacketsRejected =
        Meter.CreateCounter<long>("opstrax_telematics_packets_rejected", unit: "{packet}");
    public static readonly Histogram<double> DecodeLatencyMs =
        Meter.CreateHistogram<double>("opstrax_telematics_decode_latency_ms", unit: "ms");
    public static readonly Histogram<double> E2eLatencyMs =      // gateway receive→publish
        Meter.CreateHistogram<double>("opstrax_telematics_e2e_latency_ms", unit: "ms");
    public static readonly UpDownCounter<long> ActiveConnections =
        Meter.CreateUpDownCounter<long>("opstrax_telematics_active_connections", unit: "{connection}");
    // + unknown_devices, auth_failures, replay_rejections, duplicate_packets, out_of_order
}
```

```csharp
// DI — the gateway uses the raw SDK builder (minimal package set), not the hosting shim.
// See TelematicsObservabilityExtensions.AddTelematicsObservability(IServiceCollection).
Sdk.CreateMeterProviderBuilder()
    .AddMeter("Opstrax.Telematics.Gateway")
    .SetExemplarFilter(ExemplarFilterType.TraceBased)   // link series → trace_id
    .AddView("opstrax_telematics_decode_latency_ms",
        new ExplicitBucketHistogramConfiguration { Boundaries = new double[]{1,2,5,10,25,50,100,250,500,1000} })
    .AddOtlpExporter()                // attached only when an OTLP endpoint is configured
    .Build();
```

> `AddRuntimeInstrumentation()` / `AddAspNetCoreInstrumentation()` and the `ObservableGauge`
> samplers (DLQ depth, position age, fleet counts) are added by the hosting worker in a later
> increment; they need additional packages and live state and are out of scope for the
> isolated gateway primitives.

Enable exemplars (`ExemplarFilterType.TraceBased`) so every rejection/latency series
carries a sampled `trace_id`, giving the one-click metric→trace pivot that makes the
<60s localization target in [`slos.md`](./slos.md) achievable.

---

## Reconciliation — doc vs implemented contracts (Increment-1 review)

The distributed-systems reviewer flagged that this doc (and ADR-002) predated the shipped
`Opstrax.Telematics.*` assemblies and diverged from them in vocabulary. Corrected here so the
prose matches the code that emits the series:

1. **Product casing.** Docs said `OpsTrax.*` (capital `T`); the assemblies are `Opstrax.*`
   (lowercase `t`). The registered meter/source names follow the assembly — `Opstrax.Telematics`
   (family) and `Opstrax.Telematics.Gateway` (the gateway's component meter/source).
2. **Meter scope.** The single flat `OpsTrax.Telematics` meter is now a *family*; the gateway
   emits from `Opstrax.Telematics.Gateway`. Instrument names (`opstrax_telematics_*`) are
   unchanged, so dashboards/alerts keying on instrument names need no edit.
3. **Gateway end-to-end histogram.** The gateway cannot observe the DB commit, so it emits
   `opstrax_telematics_e2e_latency_ms` (receive→publish). The full-pipeline
   `opstrax_telematics_processing_latency_ms` (receive→committed) that SLOs 2/3 read is emitted
   by the projection worker. Both are documented in §2 above.
4. **ADR-002 topic vocabulary.** ADR-002's "16 topic contracts" use physical, versioned names
   (`otx.dp.telemetry.position.v1`, per-domain `otx.dp.telemetry.deadletter.v1`, key =
   `device_id`). The implemented contract (`Contracts/Eventing/TelematicsTopics.cs`) uses
   **broker-neutral logical** names (`telemetry.raw|decoded|rejected|normalized`,
   `vehicle.position.validated`, …), a **single** cross-cutting `integration.deadletter`, and a
   **composite** partition key `tenant+company+device` (`TelematicsEventKey.ForDevice`). Per that
   file's own docstring, the physical `otx.*.v1` mapping and any `prod.`/`dev.` prefix is a
   deployment concern applied by the backbone implementation, **not** encoded in the contract —
   so ADR-002's names describe the physical topology, not the code-level topic constants. Treat
   `TelematicsTopics` as authoritative for producer/consumer code.
