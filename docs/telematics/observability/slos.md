# Telematics Ingestion — SLOs, Error Budgets & Alerts

**Audience:** SRE / on-call.
**Design target:** any pipeline failure is **localized to a stage in under 60 seconds**.
That is achieved by (a) every alert below naming the offending stage in its annotation,
(b) exemplar links from the firing Prometheus series to the failing `trace_id`
([`metrics.md`](./metrics.md) → [`tracing.md`](./tracing.md)), and (c) Grafana panels
ordered along the span chain so the first red panel *is* the failing stage.

Stack: **Prometheus** recording + alerting rules, **Grafana** SLO dashboard +
**Alertmanager** routing. SLIs are computed from the metrics in
[`metrics.md`](./metrics.md).

## Conventions

- **Window:** 28-day rolling for budgets; alerts use multi-window multi-burn-rate
  (fast 5m/1h + slow 30m/6h) per the Google SRE workbook to cut false pages.
- **Severity:** `page` (wake someone) vs `ticket` (business hours).
- Error budget = `(1 − SLO) × valid_events` over the window.

---

## 1. SLO catalog

| # | SLO | SLI (from metrics.md) | Objective | Budget @28d |
|---|-----|-----------------------|-----------|-------------|
| 1 | **Ingestion availability** | `accepted / (accepted + rejected_infra)` where `rejected_infra` = rejects with `reason` ∈ {internal, rate_limited, publish drop} (device-fault rejects excluded) | **99.9%** | 0.1% of packets |
| 2 | **Processing latency P95** | `histogram_quantile(0.95, processing_latency_ms)` | **< 1000 ms** | 5% of minutes over budget |
| 3 | **Processing latency P99** | `histogram_quantile(0.99, processing_latency_ms)` | **< 2500 ms** | 1% of minutes over budget |
| 4 | **Projection freshness** | `histogram_quantile(0.95, projection_lag_ms)` | **< 3000 ms** | 5% |
| 5 | **Event durability** | `1 − dropped_events / events_published` | **99.99%** | 0.01% |
| 6 | **Gateway disconnect rate** | `rate(connection_disconnects{reason!="client_close"}) / active_connections` | **< 1% / 5m** | — (rate alert) |
| 7 | **DLQ growth** | `delta(dead_letter_total[30m])` and `dead_letter_depth` | **≈ 0** (no sustained growth) | — |
| 8 | **Unknown-device spikes** | `rate(unknown_devices_total[5m])` | within 3× trailing-24h baseline | — |
| 9 | **Cross-tenant violations** | count of `db`/`projection` spans missing/mismatched `tenant.id` (recorded to `tenant_mismatch` reject + trace) | **0** (hard) | 0 — any is a page |
| 10 | **Stale-fleet %** | `fleet_stale_count + fleet_offline_count) / total` per `company_id` (excluding known-parked) | **< 10%** stale+offline | 10% |
| 11 | **Command failure rate** | `command_failure_total / command_sent_total` | **< 2%** | 2% |

Notes:
- SLO 1 deliberately excludes **device-fault** rejects (bad checksum from a flaky
  tracker, replays) — those are counted separately (SLOs 8 and the security signals) so a
  fleet of bad firmware does not burn the platform's availability budget. It *does* count
  infra faults (publish drops, rate limiting, internal errors).
- SLO 9 is a **hard invariant**, not a budgeted SLO: the multi-tenant contract (4 real
  tenants, reads scoped by `company_id`) means a single cross-tenant leak is a Sev-1.
- **Latency source (Increment-1 reconciliation).** SLOs 2/3 read
  `opstrax_telematics_processing_latency_ms` — the **full** receive→committed histogram emitted by
  the projection worker. The Edge Gateway can only observe up to publish, so it emits the
  narrower `opstrax_telematics_e2e_latency_ms` (receive→publish); it is a leading indicator, not
  the SLO series. See the reconciliation note in [`metrics.md`](./metrics.md). Names are
  `Opstrax.*` (lowercase `t`), matching the shipped assemblies.

---

## 2. Prometheus recording rules (SLI shorthand)

```yaml
groups:
- name: telematics_sli
  interval: 30s
  rules:
  - record: tel:ingest_availability:ratio_rate5m
    expr: |
      sum(rate(opstrax_telematics_packets_accepted_total[5m]))
      /
      ( sum(rate(opstrax_telematics_packets_accepted_total[5m]))
        + sum(rate(opstrax_telematics_packets_rejected_total{reason=~"internal|rate_limited|publish_drop"}[5m])) )
  - record: tel:processing_latency:p95_5m
    expr: histogram_quantile(0.95, sum by (le) (rate(opstrax_telematics_processing_latency_ms_bucket[5m])))
  - record: tel:projection_lag:p95_5m
    expr: histogram_quantile(0.95, sum by (le) (rate(opstrax_telematics_projection_lag_ms_bucket[5m])))
  - record: tel:durability:ratio_rate5m
    expr: |
      1 - ( sum(rate(opstrax_telematics_dropped_events_total[5m]))
            / clamp_min(sum(rate(opstrax_telematics_events_published_total[5m])), 1) )
```

---

## 3. Alert rules

Each alert's annotations carry `stage`, the PromQL that fired, and a `trace_query`
templated link — that is the <60s localization contract.

```yaml
groups:
- name: telematics_alerts
  rules:

  # SLO 1 — availability, multi-burn-rate
  - alert: TelematicsIngestAvailabilityFastBurn
    expr: (1 - tel:ingest_availability:ratio_rate5m) > (14.4 * 0.001)
       and (1 - tel:ingest_availability:ratio_rate1h) > (14.4 * 0.001)
    for: 2m
    labels: { severity: page, stage: ingest }
    annotations:
      summary: "Ingestion availability burning 14.4x (2% budget/hr)"
      trace_query: 'rejection_reason="internal" OR "publish_drop" in Tempo, last 15m'

  - alert: TelematicsIngestAvailabilitySlowBurn
    expr: (1 - tel:ingest_availability:ratio_rate30m) > (6 * 0.001)
       and (1 - tel:ingest_availability:ratio_rate6h) > (6 * 0.001)
    for: 15m
    labels: { severity: ticket, stage: ingest }

  # SLO 2/3 — latency
  - alert: TelematicsProcessingLatencyP95High
    expr: tel:processing_latency:p95_5m > 1000
    for: 5m
    labels: { severity: page, stage: pipeline }
    annotations:
      summary: "P95 receive→commit > 1s for 5m"
      hint: "Check decode_latency_ms (adapter regression) vs queue_lag_ms (worker stall)."
  - alert: TelematicsProcessingLatencyP99High
    expr: histogram_quantile(0.99, sum by (le) (rate(opstrax_telematics_processing_latency_ms_bucket[5m]))) > 2500
    for: 5m
    labels: { severity: ticket, stage: pipeline }

  # SLO 4 — projection freshness
  - alert: TelematicsProjectionLagHigh
    expr: tel:projection_lag:p95_5m > 3000
    for: 5m
    labels: { severity: page, stage: projection }
    annotations:
      summary: "Positions landing >3s behind events (P95)"
      hint: "Correlate opstrax_telematics_queue_lag_ms + queue_depth; likely worker/db."

  # SLO 5 — durability
  - alert: TelematicsEventDropDetected
    expr: increase(opstrax_telematics_dropped_events_total[5m]) > 0
    for: 0m
    labels: { severity: page, stage: "{{ $labels.stage }}" }
    annotations:
      summary: "Telemetry events dropped at stage {{ $labels.stage }} ({{ $labels.reason }})"

  # SLO 6 — gateway disconnect storm
  - alert: TelematicsGatewayDisconnectStorm
    expr: |
      sum(rate(opstrax_telematics_connection_disconnects_total{reason!="client_close"}[5m]))
      / clamp_min(sum(opstrax_telematics_active_connections), 1) > 0.01
    for: 5m
    labels: { severity: page, stage: gateway }
    annotations:
      hint: "Check gateway_cpu_ratio / gateway_memory_bytes for the affected gateway."

  # SLO 7 — DLQ growth
  - alert: TelematicsDLQGrowing
    expr: increase(opstrax_telematics_dead_letter_total[30m]) > 0
       or opstrax_telematics_dead_letter_depth > 0
    for: 10m
    labels: { severity: page, stage: "{{ $labels.stage }}" }

  # SLO 8 — unknown-device spike (3x baseline)
  - alert: TelematicsUnknownDeviceSpike
    expr: |
      sum(rate(opstrax_telematics_unknown_devices_total[5m]))
      > 3 * sum(rate(opstrax_telematics_unknown_devices_total[24h] offset 5m))
    for: 5m
    labels: { severity: ticket, stage: identity }
    annotations:
      hint: "Scan/misconfig or un-provisioned rollout. Pivot to identity spans."

  # SLO 9 — cross-tenant violation (hard, page immediately)
  - alert: TelematicsCrossTenantViolation
    expr: increase(opstrax_telematics_packets_rejected_total{reason="tenant_mismatch"}[1m]) > 0
    for: 0m
    labels: { severity: page, stage: identity, incident: sev1 }
    annotations:
      summary: "Cross-tenant attribution attempt/leak — Sev1"
      trace_query: 'tenant.id mismatch traces in Tempo, last 5m — capture before rollover'

  # SLO 10 — stale fleet per tenant
  - alert: TelematicsFleetStaleHigh
    expr: |
      (opstrax_telematics_fleet_stale_count + opstrax_telematics_fleet_offline_count)
      / clamp_min(
          opstrax_telematics_fleet_online_count
          + opstrax_telematics_fleet_stale_count
          + opstrax_telematics_fleet_offline_count, 1) > 0.10
    for: 10m
    labels: { severity: ticket, stage: freshness }
    annotations:
      summary: ">10% of {{ $labels.company_id }} fleet stale/offline"
      hint: "If ALL tenants: producer/connector down (connector_health). If one: tenant-specific."

  # SLO 11 — command failure rate
  - alert: TelematicsCommandFailureHigh
    expr: |
      sum(rate(opstrax_telematics_command_failure_total[10m]))
      / clamp_min(sum(rate(opstrax_telematics_command_sent_total[10m])), 1) > 0.02
    for: 10m
    labels: { severity: ticket, stage: command }

  # Connector health (producer down) — catches "whole fleet stale"
  - alert: TelematicsConnectorDown
    expr: min(opstrax_telematics_connector_health) by (connector) == 0
    for: 3m
    labels: { severity: page, stage: connector }
    annotations:
      summary: "Connector {{ $labels.connector }} down — ingest producer offline"
```

---

## 4. <60s failure-localization runbook

The alert `stage` label maps 1:1 to a span in [`tracing.md`](./tracing.md). On page:

1. **Read the `stage` label** — it names the failing span immediately.
2. **Open the Grafana "Telematics Pipeline" dashboard** (panels laid out along the span
   chain: connection → receive → decode → identity → validation → publish → queue →
   projection → db → delivery). The first red panel confirms the stage.
3. **Click the exemplar** on the firing series → jumps to a representative failing
   `trace_id` in Tempo. The trace's last span + `rejection_reason` gives root cause.
4. Stage → likely cause cheat-sheet:
   - `gateway` → disconnect storm; check `gateway_cpu_ratio` / `gateway_memory_bytes`.
   - `identity` → auth/HMAC (`auth_failures_total`), unknown devices, or tenant mismatch.
   - `validation` → checksum/replay/out-of-order counters (device firmware vs corruption).
   - `pipeline`/`projection` → `queue_lag_ms` + `queue_depth` (worker stall) vs
     `decode_latency_ms` (adapter regression pinned by `adapter_version`).
   - `connector` → producer/provider down = fleet-wide staleness.
   - `command` → `command_failure_total{reason}` split (timeout vs offline vs nack).

Because the alert already carries the stage and a trace link, step 1 alone usually
localizes the failure; steps 2–4 confirm and find root cause — comfortably inside the 60s
target.

## 5. Dashboards (Grafana)

- **SLO Overview:** burn-rate gauges for SLOs 1–5 + 11, remaining error budget bars.
- **Pipeline (stage-ordered):** one row per span; latency histograms + accept/reject rates.
- **Security/Integrity:** auth failures, unknown devices, replays, duplicates,
  out-of-order, and the cross-tenant violation counter (should be flat zero).
- **Fleet freshness (per company_id):** online/stale/offline counts + `latest_position_age`.
- **Capacity/cost:** `tenant_ingest_rate`, `storage_bytes`, gateway cpu/mem/net.
