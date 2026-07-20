import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { AnyRecord } from "@/types";
import { platformApi } from "@/services/platformApi";
import { PHeader, PCard, PKpi, PButton, PLoading, PError, PEmpty } from "./ui";

// ── helpers ───────────────────────────────────────────────────────────────────

function tone(status: string): "good" | "warn" | "bad" | "default" {
  switch (status) {
    case "healthy":
    case "ready":
    case "connected":
    case "pass":
      return "good";
    case "degraded":
    case "at_risk":
    case "warn":
    case "warnings":
      return "warn";
    case "down":
    case "critical":
    case "breached":
    case "unhealthy":
    case "not_ready":
    case "fail":
      return "bad";
    default:
      return "default";
  }
}

function Dot({ status }: { status: string }) {
  const cls =
    tone(status) === "good" ? "bg-emerald-400"
    : tone(status) === "warn" ? "bg-amber-400"
    : tone(status) === "bad" ? "bg-red-400"
    : "bg-slate-400";
  return <span className={`inline-block h-2.5 w-2.5 rounded-full ${cls}`} />;
}

function fmtUptime(s: number): string {
  if (!s || s < 0) return "—";
  const d = Math.floor(s / 86400);
  const h = Math.floor((s % 86400) / 3600);
  const m = Math.floor((s % 3600) / 60);
  if (d > 0) return `${d}d ${h}h`;
  if (h > 0) return `${h}h ${m}m`;
  return `${m}m`;
}

function rel(v: string | null | undefined): string {
  if (!v) return "never";
  const diff = Date.now() - new Date(v).getTime();
  const mins = Math.floor(diff / 60000);
  if (mins < 1) return "just now";
  if (mins < 60) return `${mins}m ago`;
  const hrs = Math.floor(mins / 60);
  if (hrs < 24) return `${hrs}h ago`;
  return `${Math.floor(hrs / 24)}d ago`;
}

// ── page ────────────────────────────────────────────────────────────────────

export function PlatformReliabilityPage() {
  const qc = useQueryClient();
  const { data, isLoading, error } = useQuery({
    queryKey: ["platform", "reliability"],
    queryFn: platformApi.reliability,
    refetchInterval: 15000, // live: refresh every 15s
  });

  const ack = useMutation({
    mutationFn: (id: number) => platformApi.ackIncident(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["platform", "reliability"] }),
  });
  const resolve = useMutation({
    mutationFn: (id: number) => platformApi.resolveIncident(id, {}),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["platform", "reliability"] }),
  });

  const [showRules, setShowRules] = useState(false);

  if (isLoading) return <PLoading />;
  if (error) return <PError message={(error as Error)?.message} />;

  const d = (data ?? {}) as AnyRecord;
  const api = (d.api ?? {}) as AnyRecord;
  const slo = (d.slo ?? {}) as AnyRecord;
  const components = (d.components ?? []) as AnyRecord[];
  const slos = (slo.slos ?? []) as AnyRecord[];
  const topFailing = (d.topFailingEndpoints ?? []) as AnyRecord[];
  const openIncidents = (d.openIncidents ?? []) as AnyRecord[];
  const tenants = (d.affectedTenants ?? []) as AnyRecord[];
  const alertRules = (d.alertRules ?? []) as AnyRecord[];
  const overall = String(d.status ?? "unknown");

  return (
    <div className="space-y-7">
      <PHeader
        eyebrow="Site Reliability"
        title="Reliability Center"
        description="Live platform health, SLO error-budget burn, top failing endpoints, incidents, and per-tenant reliability. Auto-refreshes every 15s."
        actions={
          <div className="flex items-center gap-3">
            <span className="flex items-center gap-2 text-sm font-semibold text-slate-700">
              <Dot status={overall} /> {overall.toUpperCase()}
            </span>
            <PButton variant="ghost" onClick={() => setShowRules((s) => !s)}>
              {showRules ? "Hide alert rules" : "Alert rules"}
            </PButton>
          </div>
        }
      />

      {/* Deploy + uptime + config KPIs */}
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <PKpi label="Deploy version" value={String(d.deploymentVersion ?? "—")} sub={String(d.environment ?? "")} />
        <PKpi label="Uptime" value={fmtUptime(Number(d.uptimeSeconds))} sub={`started ${rel(String(d.startedAtUtc))}`} />
        <PKpi
          label="Config"
          value={String(d.configStatus ?? "—")}
          sub={`${d.configFailures ?? 0} failures · ${d.configWarnings ?? 0} warnings`}
          tone={Number(d.configFailures) > 0 ? "bad" : Number(d.configWarnings) > 0 ? "warn" : "good"}
        />
        <PKpi label="Last health check" value={rel(String(d.lastHealthCheckUtc))} sub="reliability snapshot" />
      </div>

      {/* Component health */}
      <PCard className="p-5">
        <h3 className="mb-4 text-sm font-black uppercase tracking-[0.2em] text-slate-500">Component health</h3>
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {components.map((c) => (
            <div key={String(c.id)} className="rounded-[14px] border border-slate-200 bg-white/70 p-4">
              <div className="flex items-center justify-between">
                <span className="font-semibold text-slate-800">{String(c.name)}</span>
                <span className="flex items-center gap-1.5 text-xs font-bold uppercase text-slate-500">
                  <Dot status={String(c.status)} /> {String(c.status).replace(/_/g, " ")}
                </span>
              </div>
              <p className="mt-2 text-xs text-slate-500">{String(c.detail ?? "")}</p>
            </div>
          ))}
        </div>
      </PCard>

      {/* Live API metrics */}
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <PKpi label="Requests / min" value={String(api.requestsPerMin ?? 0)} sub={`${api.requestCount ?? 0} in ${api.windowMinutes ?? 15}m`} />
        <PKpi label="p95 latency" value={`${api.latencyP95Ms ?? 0} ms`} sub={`p99 ${api.latencyP99Ms ?? 0} ms · avg ${api.latencyAvgMs ?? 0} ms`}
          tone={Number(api.latencyP95Ms) > 1000 ? "bad" : Number(api.latencyP95Ms) > 500 ? "warn" : "good"} />
        <PKpi label="5xx rate" value={`${api.rate5xxPct ?? 0}%`} sub={`${api.count5xx ?? 0} errors · 4xx ${api.rate4xxPct ?? 0}%`}
          tone={Number(api.rate5xxPct) > 1 ? "bad" : Number(api.rate5xxPct) > 0.5 ? "warn" : "good"} />
        <PKpi label="DB latency" value={`${api.dbAvgLatencyMs ?? 0} ms`} sub={`${api.dbFailures ?? 0} failures · ${api.authFailures ?? 0} auth fails`}
          tone={Number(api.dbFailures) > 0 ? "bad" : "good"} />
      </div>

      {/* SLOs + error budget */}
      <PCard className="p-5">
        <h3 className="mb-4 text-sm font-black uppercase tracking-[0.2em] text-slate-500">
          SLOs · error-budget burn — <span className={tone(String(slo.overallStatus)) === "bad" ? "text-red-500" : tone(String(slo.overallStatus)) === "warn" ? "text-amber-500" : "text-emerald-500"}>{String(slo.overallStatus ?? "—")}</span>
        </h3>
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-[11px] uppercase tracking-wider text-slate-400">
                <th className="pb-2 pr-4">Objective</th>
                <th className="pb-2 pr-4">Target</th>
                <th className="pb-2 pr-4">Actual</th>
                <th className="pb-2 pr-4">Budget burn</th>
                <th className="pb-2">Status</th>
              </tr>
            </thead>
            <tbody className="text-slate-700">
              {slos.map((s) => (
                <tr key={String(s.id)} className="border-t border-slate-100">
                  <td className="py-2 pr-4 font-medium">{String(s.name)}</td>
                  <td className="py-2 pr-4">{String(s.target)} {String(s.unit)}</td>
                  <td className="py-2 pr-4">{s.actual === null || s.actual === undefined ? "—" : `${s.actual} ${s.unit}`}</td>
                  <td className="py-2 pr-4">
                    {s.status === "unknown" || s.status === "defined" ? "—" : (
                      <span className="inline-flex items-center gap-2">
                        <span className="h-1.5 w-24 overflow-hidden rounded-full bg-slate-200">
                          <span
                            className={`block h-full ${Number(s.errorBudgetBurnPct) > 100 ? "bg-red-400" : Number(s.errorBudgetBurnPct) > 75 ? "bg-amber-400" : "bg-emerald-400"}`}
                            style={{ width: `${Math.min(Number(s.errorBudgetBurnPct), 100)}%` }}
                          />
                        </span>
                        {String(s.errorBudgetBurnPct)}%
                      </span>
                    )}
                  </td>
                  <td className="py-2">
                    <span className="flex items-center gap-1.5 text-xs font-bold uppercase text-slate-500">
                      <Dot status={String(s.status)} /> {String(s.status).replace(/_/g, " ")}
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </PCard>

      {/* Top failing endpoints */}
      <PCard className="p-5">
        <h3 className="mb-4 text-sm font-black uppercase tracking-[0.2em] text-slate-500">Top failing endpoints (15m)</h3>
        {topFailing.length === 0 ? (
          <p className="text-sm text-slate-500">No failing endpoints in the current window. 🎉</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="text-left text-[11px] uppercase tracking-wider text-slate-400">
                  <th className="pb-2 pr-4">Endpoint</th>
                  <th className="pb-2 pr-4">Requests</th>
                  <th className="pb-2 pr-4">5xx</th>
                  <th className="pb-2 pr-4">Error rate</th>
                  <th className="pb-2">p95</th>
                </tr>
              </thead>
              <tbody className="text-slate-700">
                {topFailing.map((e, i) => (
                  <tr key={i} className="border-t border-slate-100">
                    <td className="py-2 pr-4 font-mono text-xs">{String(e.endpoint)}</td>
                    <td className="py-2 pr-4">{String(e.count)}</td>
                    <td className="py-2 pr-4 font-semibold text-red-500">{String(e.errorCount)}</td>
                    <td className="py-2 pr-4">{String(e.errorRatePct)}%</td>
                    <td className="py-2">{String(e.p95Ms)} ms</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </PCard>

      {/* Incidents */}
      <PCard className="p-5">
        <h3 className="mb-4 text-sm font-black uppercase tracking-[0.2em] text-slate-500">Open incidents</h3>
        {openIncidents.length === 0 ? (
          <PEmpty title="No open incidents" subtitle="Auto-created when a service reports repeated failures." />
        ) : (
          <div className="space-y-3">
            {openIncidents.map((inc) => (
              <div key={String(inc.id)} className="rounded-[14px] border border-slate-200 bg-white/70 p-4">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div>
                    <div className="flex items-center gap-2">
                      <Dot status={String(inc.severity) === "critical" || String(inc.severity) === "high" ? "down" : "degraded"} />
                      <span className="font-semibold text-slate-800">{String(inc.title)}</span>
                    </div>
                    <p className="mt-1 text-xs text-slate-500">
                      {String(inc.sourceService)} · opened {rel(String(inc.openedAt))}
                      {inc.acknowledgedAt ? ` · ack'd ${rel(String(inc.acknowledgedAt))}` : ""}
                      {inc.traceId ? ` · trace ${String(inc.traceId).slice(0, 12)}…` : ""}
                    </p>
                    {inc.safeDescription ? <p className="mt-1 text-xs text-slate-500">{String(inc.safeDescription)}</p> : null}
                  </div>
                  <div className="flex gap-2">
                    {!inc.acknowledgedAt && (
                      <PButton variant="ghost" disabled={ack.isPending} onClick={() => ack.mutate(Number(inc.id))}>Acknowledge</PButton>
                    )}
                    <PButton variant="ghost" disabled={resolve.isPending} onClick={() => resolve.mutate(Number(inc.id))}>Resolve</PButton>
                  </div>
                </div>
              </div>
            ))}
          </div>
        )}
      </PCard>

      {/* Per-tenant reliability */}
      <PCard className="p-5">
        <h3 className="mb-4 text-sm font-black uppercase tracking-[0.2em] text-slate-500">Tenant reliability</h3>
        {tenants.length === 0 ? (
          <p className="text-sm text-slate-500">No tenant reliability signals available.</p>
        ) : (
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {tenants.map((t) => (
              <div key={String(t.companyId)} className="flex items-center justify-between rounded-[14px] border border-slate-200 bg-white/70 p-4">
                <div>
                  <p className="font-semibold text-slate-800">{String(t.companyName)}</p>
                  <p className="text-xs text-slate-500">{String(t.openIncidents)} incidents · {String(t.criticalAlerts)} critical alerts</p>
                </div>
                <Dot status={String(t.status)} />
              </div>
            ))}
          </div>
        )}
      </PCard>

      {/* Alert rules (toggle) */}
      {showRules && (
        <PCard className="p-5">
          <h3 className="mb-4 text-sm font-black uppercase tracking-[0.2em] text-slate-500">Alert rules</h3>
          <div className="space-y-2">
            {alertRules.map((r) => (
              <div key={String(r.id)} className="flex items-start justify-between gap-4 rounded-[12px] border border-slate-200 bg-white/70 p-3">
                <div>
                  <p className="font-medium text-slate-800">{String(r.name)}</p>
                  <p className="font-mono text-[11px] text-slate-500">{String(r.condition)}</p>
                </div>
                <span className="flex items-center gap-1.5 text-xs font-bold uppercase text-slate-500">
                  <Dot status={String(r.severity) === "critical" ? "down" : "degraded"} /> {String(r.severity)}
                </span>
              </div>
            ))}
          </div>
        </PCard>
      )}
    </div>
  );
}
