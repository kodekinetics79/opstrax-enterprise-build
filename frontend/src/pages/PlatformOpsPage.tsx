import React, { useState } from "react";
import {
  useDeepHealth,
  useOpsMetrics,
  useServiceRuns,
  useServiceRunHistory,
  useIncidents,
  useUpdateIncidentStatus,
  useConfigCheck,
} from "@/hooks/useOps";
import type {
  ServiceRunEntry,
  PlatformIncident,
  ConfigIssue,
} from "@/services/opsApi";

// ── Status helpers ────────────────────────────────────────────────────────────

function StatusBadge({ status }: { status: string }) {
  const base = "inline-block px-2 py-0.5 rounded text-xs font-semibold uppercase";
  const cls =
    status === "healthy" || status === "succeeded" || status === "connected" || status === "pass"
      ? `${base} bg-green-100 text-green-800`
    : status === "warning" || status === "degraded" || status === "warn"
      ? `${base} bg-yellow-100 text-yellow-800`
    : status === "failed" || status === "unhealthy" || status === "fail" || status === "unavailable"
      ? `${base} bg-red-100 text-red-800`
    : status === "running"
      ? `${base} bg-blue-100 text-blue-800`
    : `${base} bg-gray-100 text-gray-700`;
  return <span className={cls}>{status}</span>;
}

function SeverityBadge({ severity }: { severity: string }) {
  const base = "inline-block px-2 py-0.5 rounded text-xs font-semibold uppercase";
  const cls =
    severity === "critical" ? `${base} bg-red-100 text-red-800`
    : severity === "high"   ? `${base} bg-orange-100 text-orange-800`
    : severity === "medium" ? `${base} bg-yellow-100 text-yellow-800`
    : severity === "info"   ? `${base} bg-blue-100 text-blue-800`
    : `${base} bg-gray-100 text-gray-700`;
  return <span className={cls}>{severity}</span>;
}

function LevelBadge({ level }: { level: string }) {
  const base = "inline-block px-2 py-0.5 rounded text-xs font-semibold";
  const cls =
    level === "pass" ? `${base} bg-green-100 text-green-800`
    : level === "warn" ? `${base} bg-yellow-100 text-yellow-800`
    : level === "fail" ? `${base} bg-red-100 text-red-800`
    : `${base} bg-blue-100 text-blue-800`;
  return <span className={cls}>{level}</span>;
}

function ts(v: string | null | undefined) {
  if (!v) return "—";
  return new Date(v).toLocaleString();
}

function relTime(v: string | null | undefined) {
  if (!v) return "never";
  const diff = Date.now() - new Date(v).getTime();
  const mins = Math.floor(diff / 60_000);
  if (mins < 1) return "just now";
  if (mins < 60) return `${mins}m ago`;
  const hrs = Math.floor(mins / 60);
  if (hrs < 24) return `${hrs}h ago`;
  return `${Math.floor(hrs / 24)}d ago`;
}

// ── Section wrapper ───────────────────────────────────────────────────────────

function Section({
  title,
  children,
  badge,
}: {
  title: string;
  children: React.ReactNode;
  badge?: React.ReactNode;
}) {
  return (
    <div className="bg-white rounded-lg border border-gray-200 p-4 mb-4">
      <div className="flex items-center justify-between mb-3">
        <h2 className="text-sm font-semibold text-gray-700 uppercase tracking-wide">{title}</h2>
        {badge}
      </div>
      {children}
    </div>
  );
}

// ── Loading / Error states ────────────────────────────────────────────────────

function Loading() {
  return <div className="text-xs text-gray-400 py-2">Loading…</div>;
}

function ErrorState({ msg }: { msg?: string }) {
  return (
    <div className="text-xs text-red-500 py-2">
      {msg ?? "Failed to load. Check ops:view permission or API connectivity."}
    </div>
  );
}

// ── Health banner ─────────────────────────────────────────────────────────────

function HealthBanner() {
  const { data, isLoading, isError } = useDeepHealth();

  if (isLoading) return <div className="h-10 bg-gray-100 rounded animate-pulse mb-4" />;
  if (isError || !data)
    return (
      <div className="mb-4 rounded bg-red-50 border border-red-200 px-4 py-2 text-sm text-red-700">
        Deep health check unavailable
      </div>
    );

  const color =
    data.status === "healthy" ? "bg-green-50 border-green-200 text-green-800"
    : data.status === "degraded" ? "bg-yellow-50 border-yellow-200 text-yellow-800"
    : "bg-red-50 border-red-200 text-red-800";

  return (
    <div className={`mb-4 rounded border px-4 py-2 text-sm font-medium ${color}`}>
      System Status: <strong className="uppercase">{data.status}</strong>
      <span className="ml-4 font-normal opacity-70">
        DB: {data.checks.database.status} ({data.checks.database.latency_ms}ms) ·
        Config: {data.checks.config.status}
        {data.checks.config.failures > 0 && ` · ${data.checks.config.failures} config failures`}
        {data.checks.config.warnings > 0 && ` · ${data.checks.config.warnings} warnings`}
      </span>
    </div>
  );
}

// ── Metrics panel ─────────────────────────────────────────────────────────────

function MetricsPanel() {
  const { data, isLoading, isError } = useOpsMetrics();

  if (isLoading) return <Loading />;
  if (isError || !data) return <ErrorState />;

  const cards = [
    {
      label: "Telemetry (24h)",
      value: data.telemetry.accepted.toLocaleString(),
      sub: `${data.telemetry.rejected} rejected · ${data.telemetry.authFailed} auth failed`,
      alert: data.telemetry.rejected > 100,
    },
    {
      label: "Open Alerts",
      value: data.alerts.openCount.toLocaleString(),
      sub: `${data.alerts.criticalCount} critical · ${data.alerts.total24h} generated 24h`,
      alert: data.alerts.criticalCount > 0,
    },
    {
      label: "Active Dispatches",
      value: data.dispatch.active.toLocaleString(),
      sub: `${data.dispatch.openExceptions} open exceptions`,
      alert: data.dispatch.openExceptions > 10,
    },
    {
      label: "Notifications",
      value: data.notifications.pending.toLocaleString(),
      sub: `${data.notifications.failed} failed · ${data.notifications.notConfigured} not_configured`,
      alert: data.notifications.failed > 0,
    },
    {
      label: "Scheduled Reports",
      value: data.reports.activeSchedules.toLocaleString(),
      sub: `${data.reports.succeeded} succeeded · ${data.reports.failed} failed`,
      alert: data.reports.failed > 0,
    },
    {
      label: "Open Incidents",
      value: data.incidents.openCount.toLocaleString(),
      sub: `${data.incidents.criticalOpen} critical`,
      alert: data.incidents.criticalOpen > 0,
    },
    {
      label: "Database",
      value: data.database.connected ? "Connected" : "Down",
      sub: `Latency: ${data.database.latencyMs}ms`,
      alert: !data.database.connected,
    },
    {
      label: "Safety Events (24h)",
      value: data.safety.generated24h.toLocaleString(),
      sub: `${data.safety.openReview} pending review`,
      alert: data.safety.openReview > 50,
    },
  ];

  return (
    <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
      {cards.map((c) => (
        <div
          key={c.label}
          className={`rounded border p-3 ${c.alert ? "border-red-300 bg-red-50" : "border-gray-200 bg-white"}`}
        >
          <div className="text-xs text-gray-500 mb-1">{c.label}</div>
          <div className={`text-xl font-bold ${c.alert ? "text-red-700" : "text-gray-900"}`}>
            {c.value}
          </div>
          <div className="text-xs text-gray-400 mt-1">{c.sub}</div>
        </div>
      ))}
    </div>
  );
}

// ── Services table ────────────────────────────────────────────────────────────

function ServicesTable() {
  const { data, isLoading, isError } = useServiceRuns();
  const [selected, setSelected] = useState<string | null>(null);
  const { data: history } = useServiceRunHistory(selected);

  if (isLoading) return <Loading />;
  if (isError || !data) return <ErrorState />;
  if (data.length === 0)
    return (
      <div className="text-xs text-gray-400 py-2">
        No service heartbeats yet. Background services will appear here after first cycle.
      </div>
    );

  return (
    <>
      <div className="overflow-x-auto">
        <table className="min-w-full text-xs">
          <thead>
            <tr className="border-b text-gray-500 text-left">
              <th className="py-1 pr-3">Service</th>
              <th className="py-1 pr-3">Status</th>
              <th className="py-1 pr-3">Last Run</th>
              <th className="py-1 pr-3">Consecutive Failures</th>
              <th className="py-1 pr-3">Last Error</th>
              <th className="py-1"></th>
            </tr>
          </thead>
          <tbody>
            {data.map((svc: ServiceRunEntry) => {
              const failures = svc.consecutiveFailures;
              const statusStr =
                failures >= 3 ? "degraded" : failures > 0 ? "warning" : "healthy";
              return (
                <tr key={svc.serviceName} className="border-b hover:bg-gray-50">
                  <td className="py-1 pr-3 font-mono">{svc.serviceName}</td>
                  <td className="py-1 pr-3">
                    <StatusBadge status={svc.lastRunStatus ?? statusStr} />
                  </td>
                  <td className="py-1 pr-3 text-gray-500">{relTime(svc.lastRunAt)}</td>
                  <td className="py-1 pr-3">
                    {failures > 0 ? (
                      <span className="text-red-600 font-semibold">{failures}</span>
                    ) : (
                      <span className="text-gray-400">0</span>
                    )}
                  </td>
                  <td className="py-1 pr-3 text-gray-400 max-w-xs truncate">
                    {svc.lastErrorSafe ?? "—"}
                  </td>
                  <td className="py-1">
                    <button
                      type="button"
                      onClick={() => setSelected(selected === svc.serviceName ? null : svc.serviceName)}
                      className="text-blue-600 hover:underline text-xs"
                    >
                      {selected === svc.serviceName ? "Hide" : "History"}
                    </button>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      {selected && history && history.length > 0 && (
        <div className="mt-3 border-t pt-3">
          <div className="text-xs font-semibold text-gray-600 mb-2">
            Recent runs — {selected}
          </div>
          <div className="overflow-x-auto">
            <table className="min-w-full text-xs">
              <thead>
                <tr className="border-b text-gray-500">
                  <th className="py-1 pr-3 text-left">Started</th>
                  <th className="py-1 pr-3 text-left">Status</th>
                  <th className="py-1 pr-3 text-left">Duration</th>
                  <th className="py-1 pr-3 text-left">Error</th>
                </tr>
              </thead>
              <tbody>
                {history.slice(0, 20).map((r) => (
                  <tr key={r.id} className="border-b hover:bg-gray-50">
                    <td className="py-1 pr-3 text-gray-500">{ts(r.startedAt)}</td>
                    <td className="py-1 pr-3">
                      <StatusBadge status={r.status} />
                    </td>
                    <td className="py-1 pr-3">
                      {r.durationMs != null ? `${r.durationMs}ms` : "—"}
                    </td>
                    <td className="py-1 pr-3 text-gray-400 max-w-xs truncate">
                      {r.errorMessageSafe ?? "—"}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </>
  );
}

// ── Incidents panel ───────────────────────────────────────────────────────────

function IncidentsPanel() {
  const { data, isLoading, isError } = useIncidents();
  const updateStatus = useUpdateIncidentStatus();

  if (isLoading) return <Loading />;
  if (isError || !data) return <ErrorState />;

  const incidents = data.open ?? [];

  if (incidents.length === 0)
    return <div className="text-xs text-gray-400 py-2">No open incidents. All services operating normally.</div>;

  function handleResolve(inc: PlatformIncident) {
    if (!window.confirm(`Resolve incident "${inc.title}"?`)) return;
    updateStatus.mutate({ id: inc.id, status: "resolved" });
  }

  function handleInvestigate(inc: PlatformIncident) {
    updateStatus.mutate({ id: inc.id, status: "investigating" });
  }

  return (
    <div className="overflow-x-auto">
      <table className="min-w-full text-xs">
        <thead>
          <tr className="border-b text-gray-500 text-left">
            <th className="py-1 pr-3">Severity</th>
            <th className="py-1 pr-3">Service</th>
            <th className="py-1 pr-3">Title</th>
            <th className="py-1 pr-3">Status</th>
            <th className="py-1 pr-3">Opened</th>
            <th className="py-1"></th>
          </tr>
        </thead>
        <tbody>
          {incidents.map((inc: PlatformIncident) => (
            <tr key={inc.id} className="border-b hover:bg-gray-50">
              <td className="py-1 pr-3">
                <SeverityBadge severity={inc.severity} />
              </td>
              <td className="py-1 pr-3 font-mono text-gray-600">{inc.sourceService}</td>
              <td className="py-1 pr-3 max-w-xs">
                <div className="font-medium text-gray-800">{inc.title}</div>
                {inc.safeDescription && (
                  <div className="text-gray-400 truncate">{inc.safeDescription}</div>
                )}
              </td>
              <td className="py-1 pr-3">
                <StatusBadge status={inc.status} />
              </td>
              <td className="py-1 pr-3 text-gray-500">{relTime(inc.openedAt)}</td>
              <td className="py-1 flex gap-2">
                {inc.status === "open" && (
                  <button
                    type="button"
                    onClick={() => handleInvestigate(inc)}
                    className="text-blue-600 hover:underline text-xs"
                  >
                    Investigate
                  </button>
                )}
                <button
                  type="button"
                  onClick={() => handleResolve(inc)}
                  className="text-green-600 hover:underline text-xs"
                >
                  Resolve
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

// ── Config validation panel ───────────────────────────────────────────────────

function ConfigPanel() {
  const { data, isLoading, isError } = useConfigCheck();

  if (isLoading) return <Loading />;
  if (isError || !data) return <ErrorState />;

  return (
    <>
      <div className="mb-2 flex items-center gap-2">
        <StatusBadge
          status={
            data.status === "valid" ? "pass" : data.status === "warnings" ? "warn" : "fail"
          }
        />
        <span className="text-xs text-gray-500">
          {data.failCount} failures · {data.warnCount} warnings
        </span>
      </div>
      <div className="space-y-1">
        {data.issues.map((issue: ConfigIssue) => (
          <div key={issue.check} className="flex items-start gap-2 text-xs">
            <LevelBadge level={issue.level} />
            <span className="font-mono text-gray-500 shrink-0">{issue.check}</span>
            <span className="text-gray-700">{issue.message}</span>
          </div>
        ))}
      </div>
    </>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

export default function PlatformOpsPage() {
  return (
    <div className="p-4 max-w-7xl mx-auto">
      <div className="mb-4">
        <h1 className="text-lg font-bold text-gray-900">Platform Operations</h1>
        <p className="text-xs text-gray-500 mt-0.5">
          Live observability · Background services · Incidents · Config validation
        </p>
      </div>

      <HealthBanner />

      <Section title="Operational Metrics (Live)">
        <MetricsPanel />
      </Section>

      <Section title="Background Services">
        <ServicesTable />
      </Section>

      <Section title="Open Incidents">
        <IncidentsPanel />
      </Section>

      <Section title="Config Validation">
        <ConfigPanel />
      </Section>
    </div>
  );
}
