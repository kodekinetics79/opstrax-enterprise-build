import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { BarChart, Bar, LineChart, Line, XAxis, YAxis, Tooltip, ResponsiveContainer, CartesianGrid } from "recharts";
import { apiClient, unwrap } from "@/services/apiClient";
import { withFallback } from "@/services/fleetDomainApi";
import { exportCsv, LoadingState, ErrorState, EmptyState } from "@/components/ui";
import { useHasPermission } from "@/hooks/usePermission";
import type { AnyRecord } from "@/types";

// ── Seed fallback data ────────────────────────────────────────────────────────

const SEED_DRIVERS: AnyRecord[] = Array.from({ length: 12 }, (_, i) => ({
  id: i + 1,
  driverId: i + 1,
  driverName: ["Marcus Johnson","Sofia Reyes","Liam Patel","Aisha Williams","Ethan Kim","Priya Sharma","Jordan Mitchell","Elena Vasquez","Noah Anderson","Fatima Hassan","Tyler Brooks","Carmen Lopez"][i],
  driverCode: `DRV-${String(i + 1).padStart(3, "0")}`,
  safetyScore: [94, 91, 88, 85, 82, 79, 76, 73, 70, 67, 64, 58][i],
  riskScore: [8, 12, 18, 22, 28, 34, 40, 45, 52, 58, 64, 72][i],
  harshBrakingCount: [1, 2, 3, 3, 4, 5, 6, 7, 8, 9, 10, 13][i],
  harshAccelerationCount: [0, 1, 2, 2, 3, 4, 5, 6, 6, 8, 9, 11][i],
  speedingCount: [0, 1, 1, 2, 3, 4, 4, 6, 7, 8, 10, 14][i],
  dashcamEventCount: [0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 7][i],
  coachingOpenCount: [0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 6][i],
  coachingCompletedCount: [3, 2, 2, 1, 1, 1, 0, 0, 0, 0, 0, 0][i],
  incidentCount: [0, 0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3][i],
}));

const SEED_TRENDS: AnyRecord[] = Array.from({ length: 8 }, (_, i) => {
  const d = new Date();
  d.setDate(d.getDate() - (7 - i) * 7);
  return {
    trendDate: d.toISOString().split("T")[0],
    harshBrakingCount: 22 + Math.round(Math.sin(i) * 5),
    harshAccelerationCount: 15 + Math.round(Math.cos(i) * 4),
    speedingCount: 18 + Math.round(Math.sin(i + 1) * 6),
    dashcamEventCount: 10 + Math.round(Math.cos(i + 2) * 3),
    incidentCount: i < 5 ? 2 : 1,
    fleetSafetyScore: 82 + i * 0.8,
  };
});

const SEED_SUMMARY: AnyRecord = {
  fleetSafetyScore: 84,
  safetyEventsToday: 7,
  criticalEvents: 2,
  harshBraking: 18,
  harshAcceleration: 12,
  speedingEvents: 14,
  coachingNeeded: 9,
  openIncidents: 3,
  reviewedEvents: 34,
  preventableRiskScore: 41,
};

// ── API ───────────────────────────────────────────────────────────────────────

const safetyApi = {
  summary: () => withFallback(unwrap<AnyRecord>(apiClient.get("/api/safety/summary")), () => SEED_SUMMARY),
  driverScorecards: () => withFallback(unwrap<AnyRecord[]>(apiClient.get("/api/safety/drivers/scorecards")), () => SEED_DRIVERS),
  vehicleScorecards: () => withFallback(unwrap<AnyRecord[]>(apiClient.get("/api/safety/vehicles/scorecards")), () => []),
  trends: () => withFallback(unwrap<AnyRecord[]>(apiClient.get("/api/safety/trends")), () => SEED_TRENDS),
  createCoachingTask: (driverId: string | number, payload: AnyRecord) =>
    withFallback(
      unwrap<AnyRecord>(apiClient.post(`/api/safety/events/${driverId}/create-coaching-task`, payload)),
      () => ({ taskId: `TASK-${Date.now()}`, driverId, success: true })
    ),
};

// ── Score ring ────────────────────────────────────────────────────────────────

function ScoreRing({ score, size = 56 }: { score: number; size?: number }) {
  const r = (size - 8) / 2;
  const circ = 2 * Math.PI * r;
  const dash = (score / 100) * circ;
  const color = score >= 85 ? "#14b8a6" : score >= 70 ? "#f59e0b" : "#ef4444";
  return (
    <svg width={size} height={size} className="shrink-0">
      <circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke="#e2e8f0" strokeWidth={6} />
      <circle
        cx={size / 2} cy={size / 2} r={r} fill="none"
        stroke={color} strokeWidth={6}
        strokeDasharray={`${dash} ${circ - dash}`}
        strokeLinecap="round"
        transform={`rotate(-90 ${size / 2} ${size / 2})`}
      />
      <text x={size / 2} y={size / 2 + 5} textAnchor="middle" fontSize={13} fontWeight={700} fill={color}>
        {score}
      </text>
    </svg>
  );
}

// ── Behavior bar ─────────────────────────────────────────────────────────────

function BehaviorBar({ label, count, max, color }: { label: string; count: number; max: number; color: string }) {
  const pct = Math.min(100, max > 0 ? (count / max) * 100 : 0);
  return (
    <div className="flex items-center gap-2 text-xs">
      <span className="w-28 text-slate-500 shrink-0">{label}</span>
      <div className="flex-1 h-1.5 rounded-full bg-slate-100 overflow-hidden">
        <div className={`h-full rounded-full ${color}`} style={{ width: `${pct}%` }} />
      </div>
      <span className="w-5 text-right font-medium text-slate-700">{count}</span>
    </div>
  );
}

// ── Detail Drawer ─────────────────────────────────────────────────────────────

function DriverDrawer({
  driver,
  onClose,
  canCoach,
  onCoach,
}: {
  driver: AnyRecord | null;
  onClose: () => void;
  canCoach: boolean;
  onCoach: (driver: AnyRecord) => void;
}) {
  if (!driver) return null;

  const score = Number(driver.safetyScore ?? 0);
  const risk = Number(driver.riskScore ?? 0);
  const scoreLabel = score >= 85 ? "Good standing" : score >= 70 ? "Needs monitoring" : "High risk — action required";

  const maxCount = 15;

  return (
    <div className="fixed inset-0 z-40 flex justify-end" onClick={onClose}>
      <div
        className="bg-slate-950 w-full max-w-sm h-full flex flex-col overflow-y-auto shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between px-5 py-4 border-b border-white/8">
          <span className="text-sm font-semibold text-white">Driver Scorecard</span>
          <button type="button" className="text-slate-400 hover:text-white" onClick={onClose}>✕</button>
        </div>

        <div className="px-5 pt-5 pb-4 flex items-center gap-4 border-b border-white/6">
          <ScoreRing score={score} size={64} />
          <div>
            <p className="text-base font-semibold text-white">{String(driver.driverName ?? "Driver")}</p>
            <p className="text-xs text-slate-400 mt-0.5">{String(driver.driverCode ?? "")}</p>
            <p className={`text-xs mt-1 font-medium ${score >= 85 ? "text-teal-400" : score >= 70 ? "text-amber-400" : "text-red-400"}`}>
              {scoreLabel}
            </p>
          </div>
        </div>

        <div className="px-5 py-4 grid grid-cols-2 gap-3 border-b border-white/6">
          {[
            ["Safety Score", `${score}/100`],
            ["Risk Score", `${risk}`],
            ["Coaching Open", driver.coachingOpenCount],
            ["Incidents", driver.incidentCount],
          ].map(([k, v]) => (
            <div key={String(k)}>
              <p className="text-xs text-slate-400">{String(k)}</p>
              <p className="text-sm font-semibold text-white mt-0.5">{String(v ?? "--")}</p>
            </div>
          ))}
        </div>

        <div className="px-5 py-4 flex flex-col gap-2.5 border-b border-white/6">
          <p className="text-xs font-semibold text-slate-400 uppercase tracking-wide mb-1">Behavior Breakdown</p>
          <BehaviorBar label="Harsh Braking" count={Number(driver.harshBrakingCount ?? 0)} max={maxCount} color="bg-red-400" />
          <BehaviorBar label="Harsh Accel." count={Number(driver.harshAccelerationCount ?? 0)} max={maxCount} color="bg-orange-400" />
          <BehaviorBar label="Speeding" count={Number(driver.speedingCount ?? 0)} max={maxCount} color="bg-amber-400" />
          <BehaviorBar label="Dashcam Events" count={Number(driver.dashcamEventCount ?? 0)} max={maxCount} color="bg-violet-400" />
          <BehaviorBar label="Coaching Completed" count={Number(driver.coachingCompletedCount ?? 0)} max={maxCount} color="bg-teal-400" />
        </div>

        <div className="px-5 py-4 border-b border-white/6">
          <p className="text-xs font-semibold text-teal-400 uppercase tracking-wide mb-1.5">AI Recommendation</p>
          <p className="text-sm text-slate-300 leading-relaxed">
            {risk >= 60
              ? "Immediate coaching intervention recommended. Schedule a mandatory session focusing on following distance and speed compliance before next dispatch."
              : risk >= 35
              ? "Monitor closely over the next 14 days. Assign a targeted coaching task for the highest-frequency behavior category."
              : "Driver is performing well. Maintain positive reinforcement — no corrective action required at this time."}
          </p>
        </div>

        {canCoach && (
          <div className="px-5 py-4">
            <button
              type="button"
              className="w-full bg-violet-600 hover:bg-violet-700 text-white text-sm font-medium py-2.5 rounded-lg transition-colors"
              onClick={() => onCoach(driver)}
            >
              Create Coaching Task
            </button>
          </div>
        )}
      </div>
    </div>
  );
}

// ── Coaching modal ────────────────────────────────────────────────────────────

function CoachingModal({
  driver,
  onClose,
  onConfirm,
  pending,
}: {
  driver: AnyRecord | null;
  onClose: () => void;
  onConfirm: (payload: AnyRecord) => void;
  pending: boolean;
}) {
  const [notes, setNotes] = useState("");
  if (!driver) return null;
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/30 backdrop-blur-sm" onClick={onClose}>
      <div className="panel w-full max-w-md mx-4 flex flex-col gap-4" onClick={(e) => e.stopPropagation()}>
        <div className="flex items-center justify-between">
          <h3 className="text-base font-semibold text-slate-900">Create Coaching Task</h3>
          <button type="button" className="text-slate-400 hover:text-slate-600" onClick={onClose}>✕</button>
        </div>
        <p className="text-sm text-slate-600">
          Driver: <span className="font-medium">{String(driver.driverName)}</span> — Safety Score: <span className="font-medium">{String(driver.safetyScore)}</span>
        </p>
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-slate-700">Coaching notes</label>
          <textarea
            className="border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-900 resize-none focus:outline-none focus:ring-2 focus:ring-violet-400"
            rows={3}
            placeholder="Focus areas, session goals..."
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
          />
        </div>
        <div className="flex justify-end gap-2">
          <button type="button" className="btn-secondary text-sm" onClick={onClose}>Cancel</button>
          <button
            type="button"
            disabled={pending}
            className="bg-violet-600 hover:bg-violet-700 disabled:opacity-50 text-white text-sm font-medium px-4 py-2 rounded-lg transition-colors"
            onClick={() => onConfirm({ notes, priority: Number(driver.riskScore ?? 0) >= 50 ? "High" : "Normal" })}
          >
            {pending ? "Creating…" : "Create Task"}
          </button>
        </div>
      </div>
    </div>
  );
}

// ── Score badge ───────────────────────────────────────────────────────────────

function ScoreBadge({ score }: { score: number }) {
  const cls =
    score >= 85 ? "bg-teal-50 border-teal-200 text-teal-700" :
    score >= 70 ? "bg-amber-50 border-amber-200 text-amber-700" :
    "bg-red-50 border-red-200 text-red-700";
  return (
    <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full border text-xs font-semibold ${cls}`}>
      {score}
    </span>
  );
}

// ── Main Page ─────────────────────────────────────────────────────────────────

type Tab = "drivers" | "vehicles" | "trends";

export function DriverScorecardsPage() {
  const qc = useQueryClient();
  const hasPermission = useHasPermission();
  const canCoach = hasPermission("safety:create") || hasPermission("safety:review");

  const [tab, setTab] = useState<Tab>("drivers");
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const [coachTarget, setCoachTarget] = useState<AnyRecord | null>(null);
  const [search, setSearch] = useState("");
  const [toast, setToast] = useState<string | null>(null);

  const summaryQ = useQuery({ queryKey: ["safety", "summary"], queryFn: safetyApi.summary });
  const driversQ = useQuery({ queryKey: ["safety", "driver-scorecards"], queryFn: safetyApi.driverScorecards, refetchInterval: 30_000 });
  const vehiclesQ = useQuery({ queryKey: ["safety", "vehicle-scorecards"], queryFn: safetyApi.vehicleScorecards });
  const trendsQ = useQuery({ queryKey: ["safety", "trends"], queryFn: safetyApi.trends });

  const coachMutation = useMutation({
    mutationFn: ({ driverId, payload }: { driverId: string | number; payload: AnyRecord }) =>
      safetyApi.createCoachingTask(driverId, payload),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["safety"] });
      setCoachTarget(null);
      setSelected(null);
      showToast("Coaching task created");
    },
  });

  function showToast(msg: string) {
    setToast(msg);
    setTimeout(() => setToast(null), 3500);
  }

  const s = (summaryQ.data ?? {}) as AnyRecord;
  const drivers = (driversQ.data ?? []) as AnyRecord[];
  const vehicles = (vehiclesQ.data ?? []) as AnyRecord[];
  const trends = (trendsQ.data ?? []) as AnyRecord[];

  const filteredDrivers = drivers.filter((d) => {
    if (!search) return true;
    const q = search.toLowerCase();
    return String(d.driverName ?? "").toLowerCase().includes(q) || String(d.driverCode ?? "").toLowerCase().includes(q);
  });

  const fleetScore = Number(s.fleetSafetyScore ?? (drivers.length ? (drivers.reduce((a, d) => a + Number(d.safetyScore ?? 0), 0) / drivers.length).toFixed(1) : 0));

  if (driversQ.isLoading) return <LoadingState />;
  if (driversQ.isError) return <ErrorState message={(driversQ.error as Error)?.message} />;

  return (
    <div className="flex flex-col gap-6 py-6">
      {toast && (
        <div className="fixed top-4 right-4 z-50 bg-teal-600 text-white text-sm font-medium px-4 py-2.5 rounded-lg shadow-lg">
          {toast}
        </div>
      )}

      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-bold text-slate-900">Driver Safety Scorecards</h1>
          <p className="text-sm text-slate-500 mt-0.5">Fleet-wide behavior scoring — harsh braking, acceleration, speeding, dashcam events &amp; coaching</p>
        </div>
        <button
          type="button"
          className="btn-secondary text-sm"
          onClick={() => exportCsv("driver-scorecards", drivers)}
        >
          Export CSV
        </button>
      </div>

      {/* KPI strip */}
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Fleet Safety Score", val: `${fleetScore}`, accent: fleetScore >= 85 ? "text-teal-600" : fleetScore >= 70 ? "text-amber-600" : "text-red-600" },
          { label: "Critical Events", val: s.criticalEvents ?? 0, accent: "text-red-600" },
          { label: "Harsh Braking", val: s.harshBraking ?? 0 },
          { label: "Speeding Events", val: s.speedingEvents ?? 0, accent: "text-amber-600" },
          { label: "Coaching Needed", val: s.coachingNeeded ?? 0, accent: "text-violet-600" },
          { label: "Open Incidents", val: s.openIncidents ?? 0, accent: "text-red-600" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-30">
            <span className={`text-2xl font-bold ${accent ?? "text-slate-900"}`}>{String(val)}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>

      {/* Tab bar */}
      <div className="panel flex gap-1.5 p-2">
        {(["drivers", "vehicles", "trends"] as Tab[]).map((t) => (
          <button
            key={t}
            type="button"
            onClick={() => setTab(t)}
            className={`px-4 py-1.5 rounded-lg text-sm font-medium border transition-colors capitalize ${
              tab === t
                ? "bg-teal-50 border-teal-300 text-teal-700"
                : "bg-slate-50 border-slate-200 text-slate-600 hover:bg-slate-100"
            }`}
          >
            {t === "trends" ? "Fleet Trends" : `${t.charAt(0).toUpperCase() + t.slice(1)} Scorecards`}
          </button>
        ))}
        {tab === "drivers" && (
          <input
            type="search"
            placeholder="Search drivers…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="ml-auto border border-slate-200 rounded-lg px-3 py-1.5 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-teal-400 w-48"
          />
        )}
      </div>

      {/* Driver scorecards table */}
      {tab === "drivers" && (
        <div className="panel overflow-hidden p-0">
          {filteredDrivers.length === 0 ? (
            <EmptyState title="No drivers found" />
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-slate-200 bg-slate-50">
                    <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Rank</th>
                    <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Driver</th>
                    <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Safety Score</th>
                    <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Harsh Braking</th>
                    <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Harsh Accel.</th>
                    <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Speeding</th>
                    <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Dashcam</th>
                    <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Coaching Open</th>
                    <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Incidents</th>
                    <th className="text-right px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Actions</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {filteredDrivers.map((driver, i) => {
                    const score = Number(driver.safetyScore ?? 0);
                    return (
                      <tr
                        key={String(driver.id ?? i)}
                        className="hover:bg-slate-50 cursor-pointer"
                        onClick={() => setSelected(driver)}
                      >
                        <td className="px-4 py-3 text-slate-500 text-xs font-medium">#{i + 1}</td>
                        <td className="px-4 py-3">
                          <p className="font-medium text-slate-900">{String(driver.driverName ?? "--")}</p>
                          <p className="text-xs text-slate-400">{String(driver.driverCode ?? "")}</p>
                        </td>
                        <td className="px-4 py-3">
                          <div className="flex items-center gap-2">
                            <ScoreRing score={score} size={36} />
                            <ScoreBadge score={score} />
                          </div>
                        </td>
                        <td className="px-4 py-3 text-slate-700">{String(driver.harshBrakingCount ?? 0)}</td>
                        <td className="px-4 py-3 text-slate-700">{String(driver.harshAccelerationCount ?? 0)}</td>
                        <td className="px-4 py-3 text-slate-700">{String(driver.speedingCount ?? 0)}</td>
                        <td className="px-4 py-3 text-slate-700">{String(driver.dashcamEventCount ?? 0)}</td>
                        <td className="px-4 py-3">
                          {Number(driver.coachingOpenCount ?? 0) > 0 ? (
                            <span className="text-xs px-2 py-0.5 rounded-full bg-violet-50 border border-violet-200 text-violet-700 font-semibold">
                              {String(driver.coachingOpenCount)}
                            </span>
                          ) : (
                            <span className="text-slate-400">—</span>
                          )}
                        </td>
                        <td className="px-4 py-3 text-slate-700">{String(driver.incidentCount ?? 0)}</td>
                        <td className="px-4 py-3 text-right" onClick={(e) => e.stopPropagation()}>
                          {canCoach && (
                            <button
                              type="button"
                              className="text-xs px-2.5 py-1 rounded-md bg-violet-50 border border-violet-200 text-violet-700 hover:bg-violet-100 transition-colors"
                              onClick={() => setCoachTarget(driver)}
                            >
                              Coach
                            </button>
                          )}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {/* Vehicle scorecards */}
      {tab === "vehicles" && (
        <div className="panel overflow-hidden p-0">
          {vehicles.length === 0 ? (
            <EmptyState title="No vehicle scorecard data" subtitle="Scorecards are generated from safety events linked to vehicles" />
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-slate-200 bg-slate-50">
                    <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Vehicle</th>
                    <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Safety Score</th>
                    <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Safety Events</th>
                    <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Dashcam Events</th>
                    <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Incidents</th>
                    <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Route Deviations</th>
                    <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Risk Score</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {vehicles.map((v, i) => (
                    <tr key={String(v.id ?? i)} className="hover:bg-slate-50">
                      <td className="px-4 py-3">
                        <p className="font-medium text-slate-900">{String(v.vehicleCode ?? "--")}</p>
                        <p className="text-xs text-slate-400">{String(v.type ?? "")}</p>
                      </td>
                      <td className="px-4 py-3"><ScoreBadge score={Number(v.safetyScore ?? 0)} /></td>
                      <td className="px-4 py-3 text-slate-700">{String(v.safetyEventCount ?? 0)}</td>
                      <td className="px-4 py-3 text-slate-700">{String(v.dashcamEventCount ?? 0)}</td>
                      <td className="px-4 py-3 text-slate-700">{String(v.incidentCount ?? 0)}</td>
                      <td className="px-4 py-3 text-slate-700">{String(v.routeDeviationCount ?? 0)}</td>
                      <td className="px-4 py-3 text-slate-700">{String(v.riskScore ?? 0)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {/* Fleet trends chart */}
      {tab === "trends" && (
        <div className="grid gap-6 xl:grid-cols-2">
          <div className="panel p-5">
            <h2 className="text-sm font-semibold text-slate-900 mb-4">Fleet Safety Score Trend</h2>
            <ResponsiveContainer width="100%" height={220}>
              <LineChart data={trends}>
                <CartesianGrid stroke="rgba(0,0,0,0.06)" strokeDasharray="3 3" />
                <XAxis dataKey="trendDate" tick={{ fontSize: 11 }} tickFormatter={(v: string) => v.slice(5)} />
                <YAxis domain={[60, 100]} tick={{ fontSize: 11 }} />
                <Tooltip
                  contentStyle={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: 8, fontSize: 12 }}
                  labelStyle={{ color: "#475569" }}
                />
                <Line type="monotone" dataKey="fleetSafetyScore" stroke="#14b8a6" strokeWidth={2} dot={false} name="Safety Score" />
              </LineChart>
            </ResponsiveContainer>
          </div>
          <div className="panel p-5">
            <h2 className="text-sm font-semibold text-slate-900 mb-4">Behavior Events by Week</h2>
            <ResponsiveContainer width="100%" height={220}>
              <BarChart data={trends}>
                <CartesianGrid stroke="rgba(0,0,0,0.06)" strokeDasharray="3 3" />
                <XAxis dataKey="trendDate" tick={{ fontSize: 11 }} tickFormatter={(v: string) => v.slice(5)} />
                <YAxis tick={{ fontSize: 11 }} />
                <Tooltip
                  contentStyle={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: 8, fontSize: 12 }}
                  labelStyle={{ color: "#475569" }}
                />
                <Bar dataKey="harshBrakingCount" fill="#ef4444" name="Harsh Braking" radius={[2, 2, 0, 0]} />
                <Bar dataKey="harshAccelerationCount" fill="#f97316" name="Harsh Accel." radius={[2, 2, 0, 0]} />
                <Bar dataKey="speedingCount" fill="#f59e0b" name="Speeding" radius={[2, 2, 0, 0]} />
                <Bar dataKey="dashcamEventCount" fill="#8b5cf6" name="Dashcam" radius={[2, 2, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </div>
        </div>
      )}

      <DriverDrawer
        driver={selected}
        onClose={() => setSelected(null)}
        canCoach={canCoach}
        onCoach={(d) => { setSelected(null); setCoachTarget(d); }}
      />

      <CoachingModal
        driver={coachTarget}
        onClose={() => setCoachTarget(null)}
        pending={coachMutation.isPending}
        onConfirm={(payload) =>
          coachMutation.mutate({ driverId: (coachTarget!.driverId ?? coachTarget!.id) as string | number, payload })
        }
      />
    </div>
  );
}
