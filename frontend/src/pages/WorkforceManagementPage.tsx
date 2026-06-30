import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  AlertTriangle, Bot, Calendar, CheckCircle2, Clock, Download, Moon,
  Plus, Sun, Users, X,
} from "lucide-react";
import { apiClient, unwrap } from "@/services/apiClient";
import { exportCsv, LoadingState } from "@/components/ui";
import type { AnyRecord } from "@/types";

// ── Constants ─────────────────────────────────────────────────────────────────

const DAYS = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
const SHIFT_TYPES = ["Morning", "Afternoon", "Night", "Off", "Rest (HOS)"] as const;
type ShiftType = typeof SHIFT_TYPES[number];

const SHIFT_META: Record<ShiftType, { label: string; bg: string; text: string; hours: string }> = {
  "Morning":    { label: "Morning",    bg: "bg-sky-100",    text: "text-sky-700",    hours: "06:00–14:00" },
  "Afternoon":  { label: "Afternoon",  bg: "bg-amber-100",  text: "text-amber-700",  hours: "14:00–22:00" },
  "Night":      { label: "Night",      bg: "bg-violet-100", text: "text-violet-700", hours: "22:00–06:00" },
  "Off":        { label: "Off",        bg: "bg-slate-100",  text: "text-slate-500",  hours: "Day off" },
  "Rest (HOS)": { label: "HOS Rest",   bg: "bg-red-50",     text: "text-red-600",    hours: "Mandatory rest" },
};

// ── Live data ─────────────────────────────────────────────────────────────────

function buildDriverPool(): AnyRecord[] {
  return [];
}

function normalizeDrivers(rows: AnyRecord[]): AnyRecord[] {
  return rows.map((d, i) => ({
    id: d.id ?? i + 1,
    driverId: d.id ?? d.driverId ?? i + 1,
    name: String(d.fullName ?? d.name ?? d.driverName ?? `Driver ${i + 1}`),
    code: String(d.driverCode ?? d.code ?? `DRV-${String(i + 1).padStart(3, "0")}`),
    licenceClass: String(d.licenceClass ?? d.licenseClass ?? "C"),
    vehicleCode: String(d.assignedVehicleCode ?? d.vehicleCode ?? d.assignedVehicle ?? ""),
    hoursThisWeek: Number(d.hoursThisWeek ?? d.hours ?? 0),
    hosLimit: Number(d.hosLimit ?? 70),
    status: String(d.status ?? "Available"),
    safetyScore: Number(d.safetyScore ?? d.score ?? 0),
  }));
}

const SCHEDULE_SHIFTS: ShiftType[] = [
  "Morning", "Afternoon", "Night", "Off",
  "Morning", "Rest (HOS)", "Off", "Morning",
  "Afternoon", "Afternoon", "Morning", "Night",
  "Off", "Morning", "Afternoon", "Off",
  "Night", "Off", "Morning", "Afternoon",
  "Morning", "Afternoon", "Night", "Off",
  "Morning", "Afternoon", "Off", "Morning",
  "Off", "Morning", "Afternoon", "Night",
  "Morning", "Off", "Morning", "Afternoon",
  "Afternoon", "Morning", "Night", "Rest (HOS)",
  "Morning", "Afternoon", "Night", "Off",
  "Morning", "Rest (HOS)", "Off", "Morning",
  "Afternoon", "Morning", "Off", "Afternoon",
  "Night", "Off", "Morning", "Afternoon",
  "Morning", "Morning", "Night", "Off",
  "Afternoon", "Morning", "Off", "Night",
  "Morning", "Afternoon", "Rest (HOS)", "Morning",
  "Night", "Off", "Morning", "Afternoon",
  "Off", "Morning", "Afternoon", "Night",
  "Morning", "Afternoon", "Off", "Morning",
];

function buildSchedule(drivers: AnyRecord[]): AnyRecord[] {
  return drivers.map((d, di) =>
    DAYS.reduce<AnyRecord>((acc, day, dayIdx) => {
      acc[day] = SCHEDULE_SHIFTS[(di * 7 + dayIdx) % SCHEDULE_SHIFTS.length];
      return acc;
    }, { driverId: d.id, driverName: String(d.name), code: String(d.code) })
  );
}

const workforceApi = {
  drivers: () => unwrap<AnyRecord[]>(apiClient.get("/api/workforce/drivers")).then(normalizeDrivers),
  schedule: () => unwrap<AnyRecord[]>(apiClient.get("/api/workforce/schedule")),
  assign: (driverId: number, day: string, shift: ShiftType) =>
    apiClient.post("/api/workforce/schedule/assign", { driverId, day, shift }),
};

// ── Modal ─────────────────────────────────────────────────────────────────────

function AssignModal({
  cell, onClose, onSave,
}: {
  cell: { driverName: string; day: string; current: ShiftType } | null;
  onClose: () => void;
  onSave: (shift: ShiftType) => void;
}) {
  const [chosen, setChosen] = useState<ShiftType>(cell?.current ?? "Morning");
  if (!cell) return null;
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/30 backdrop-blur-sm">
      <div className="panel w-full max-w-sm mx-4 p-6 space-y-4">
        <div className="flex items-center justify-between">
          <h2 className="text-base font-bold text-slate-900">Assign Shift</h2>
          <button type="button" onClick={onClose} className="text-slate-400 hover:text-slate-600"><X className="h-5 w-5" /></button>
        </div>
        <p className="text-sm text-slate-600"><strong>{cell.driverName}</strong> · {cell.day}</p>
        <div className="grid grid-cols-1 gap-2">
          {SHIFT_TYPES.map((s) => {
            const meta = SHIFT_META[s];
            return (
              <button key={s} type="button" onClick={() => setChosen(s)}
                className={`flex items-center gap-3 rounded-xl border-2 px-4 py-3 text-left transition-colors ${chosen === s ? "border-teal-400 bg-teal-50" : "border-slate-200 hover:border-slate-300"}`}>
                <span className={`w-3 h-3 rounded-full ${meta.bg.replace("bg-", "bg-").replace("-100", "-400").replace("-50", "-400")}`} />
                <div>
                  <p className={`text-sm font-semibold ${chosen === s ? "text-teal-800" : "text-slate-700"}`}>{meta.label}</p>
                  <p className="text-xs text-slate-400">{meta.hours}</p>
                </div>
              </button>
            );
          })}
        </div>
        <div className="flex gap-2 pt-1">
          <button type="submit" className="btn-primary flex-1" onClick={() => onSave(chosen)}>Save</button>
          <button type="button" className="btn-secondary" onClick={onClose}>Cancel</button>
        </div>
      </div>
    </div>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

type PageTab = "Schedule" | "Roster" | "Insights";

export function WorkforceManagementPage() {
  const [tab, setTab] = useState<PageTab>("Schedule");
  const [cell, setCell] = useState<{ driverName: string; day: string; current: ShiftType; driverId: number } | null>(null);
  const [filterStatus, setFilterStatus] = useState("");
  const queryClient = useQueryClient();

  const driversQ  = useQuery({ queryKey: ["workforce-drivers"],  queryFn: workforceApi.drivers,  staleTime: 30_000 });
  const scheduleQ = useQuery({ queryKey: ["workforce-schedule"], queryFn: workforceApi.schedule, staleTime: 30_000 });

  const drivers  = (driversQ.data  ?? []) as AnyRecord[];
  const schedule = (scheduleQ.data ?? []) as AnyRecord[];

  const assign = useMutation({
    mutationFn: ({ driverId, day, shift }: { driverId: number; day: string; shift: ShiftType }) =>
      workforceApi.assign(driverId, day, shift),
    onSuccess: () => { queryClient.invalidateQueries({ queryKey: ["workforce-schedule"] }); },
  });

  const isLoading = driversQ.isLoading || scheduleQ.isLoading;

  const onShift    = drivers.filter((d) => String(d.status) === "On Shift").length;
  const available  = drivers.filter((d) => String(d.status) === "Available").length;
  const hosRest    = drivers.filter((d) => String(d.status) === "Rest (HOS)").length;
  const nearHosLimit = drivers.filter((d) => Number(d.hoursThisWeek) >= 60).length;

  const filteredDrivers = filterStatus
    ? drivers.filter((d) => String(d.status) === filterStatus)
    : drivers;

  function handleCellClick(row: AnyRecord, day: string) {
    setCell({
      driverId: Number(row.driverId),
      driverName: String(row.driverName ?? ""),
      day,
      current: (String(row[day] ?? "Off")) as ShiftType,
    });
  }

  function handleSave(shift: ShiftType) {
    if (cell) {
      assign.mutate({ driverId: cell.driverId, day: cell.day, shift });
    }
    setCell(null);
  }

  if (isLoading) return <LoadingState />;

  return (
    <div className="flex flex-col gap-6 py-6">
      <AssignModal cell={cell} onClose={() => setCell(null)} onSave={handleSave} />

      {/* Header */}
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="text-xl font-bold text-slate-900">Workforce Management</h1>
          <p className="text-sm text-slate-500 mt-0.5">Driver shift scheduling, availability, HOS compliance and roster planning</p>
        </div>
        <div className="flex items-center gap-2">
          <button type="button" className="btn-secondary flex items-center gap-2 text-sm" onClick={() => exportCsv("workforce-schedule", schedule)}>
            <Download className="h-4 w-4" />Export Schedule
          </button>
          <button type="button" className="btn-primary flex items-center gap-2 text-sm">
            <Plus className="h-4 w-4" />Add Driver
          </button>
        </div>
      </div>

      {/* KPI strip */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        <div className="panel flex items-center gap-3 p-4">
          <div className="flex h-9 w-9 items-center justify-center rounded-xl bg-teal-50 shrink-0"><Users className="h-5 w-5 text-teal-600" /></div>
          <div><p className="text-xl font-bold text-teal-700">{drivers.length}</p><p className="text-xs text-slate-500 font-medium">Total Drivers</p></div>
        </div>
        <div className="panel flex items-center gap-3 p-4">
          <div className="flex h-9 w-9 items-center justify-center rounded-xl bg-sky-50 shrink-0"><Sun className="h-5 w-5 text-sky-600" /></div>
          <div><p className="text-xl font-bold text-sky-700">{onShift}</p><p className="text-xs text-slate-500 font-medium">On Shift</p></div>
        </div>
        <div className="panel flex items-center gap-3 p-4">
          <div className="flex h-9 w-9 items-center justify-center rounded-xl bg-emerald-50 shrink-0"><CheckCircle2 className="h-5 w-5 text-emerald-600" /></div>
          <div><p className="text-xl font-bold text-emerald-700">{available}</p><p className="text-xs text-slate-500 font-medium">Available</p></div>
        </div>
        <div className="panel flex items-center gap-3 p-4">
          <div className="flex h-9 w-9 items-center justify-center rounded-xl bg-red-50 shrink-0"><AlertTriangle className="h-5 w-5 text-red-500" /></div>
          <div>
            <p className="text-xl font-bold text-red-700">{hosRest + nearHosLimit}</p>
            <p className="text-xs text-slate-500 font-medium">HOS Issues</p>
            <p className="text-[10px] text-slate-400">{hosRest} resting · {nearHosLimit} near limit</p>
          </div>
        </div>
      </div>

      {/* Tabs */}
      <div className="panel flex gap-1 p-1.5">
        {(["Schedule", "Roster", "Insights"] as const).map((t) => (
          <button key={t} type="button" onClick={() => setTab(t)}
            className={`flex items-center gap-2 px-4 py-2 rounded-lg text-sm font-medium transition-colors ${tab === t ? "bg-teal-600 text-white shadow-sm" : "text-slate-600 hover:bg-slate-100"}`}>
            {t === "Schedule" ? <Calendar className="h-3.5 w-3.5" /> : t === "Roster" ? <Users className="h-3.5 w-3.5" /> : <Bot className="h-3.5 w-3.5" />}
            {t}
          </button>
        ))}
      </div>

      {/* ── Schedule Tab ── */}
      {tab === "Schedule" && (
        <div className="panel overflow-x-auto">
          <div className="min-w-[720px]">
            <div className="p-4 pb-2 flex items-center justify-between">
              <p className="text-sm font-semibold text-slate-700">Week of 16–22 Jun 2026 <span className="text-xs text-slate-400 ml-2">Click any cell to reassign</span></p>
              <div className="flex gap-2">
                {(Object.entries(SHIFT_META) as [ShiftType, typeof SHIFT_META[ShiftType]][]).map(([key, meta]) => (
                  <span key={key} className={`text-[10px] px-2 py-0.5 rounded-full ${meta.bg} ${meta.text} font-semibold`}>{meta.label}</span>
                ))}
              </div>
            </div>
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  <th className="px-4 py-2 text-left text-xs font-bold text-slate-500 w-44">Driver</th>
                  {DAYS.map((d) => (
                    <th key={d} className="px-2 py-2 text-center text-xs font-bold text-slate-500 min-w-24">{d}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {schedule.map((row, ri) => (
                  <tr key={ri} className="hover:bg-slate-50">
                    <td className="px-4 py-2">
                      <p className="font-medium text-slate-900 text-xs">{String(row.driverName ?? "")}</p>
                      <p className="text-[10px] text-slate-400">{String(row.code ?? "")}</p>
                    </td>
                    {DAYS.map((day) => {
                      const shift = (String(row[day] ?? "Off")) as ShiftType;
                      const meta  = SHIFT_META[shift] ?? SHIFT_META["Off"];
                      return (
                        <td key={day} className="px-1 py-1.5 text-center">
                          <button
                            type="button"
                            title={`${String(row.driverName)} · ${day}: ${shift}`}
                            onClick={() => handleCellClick(row, day)}
                            className={`w-full rounded-lg px-1 py-1.5 text-[10px] font-semibold transition-all hover:ring-2 hover:ring-teal-400 ${meta.bg} ${meta.text}`}
                          >
                            {meta.label}
                          </button>
                        </td>
                      );
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* ── Roster Tab ── */}
      {tab === "Roster" && (
        <div className="space-y-4">
          <div className="flex flex-wrap gap-2">
            <button type="button" onClick={() => setFilterStatus("")} className={`rounded-full border px-3 py-1 text-xs font-bold transition ${!filterStatus ? "border-teal-300 bg-teal-50 text-teal-700" : "border-slate-200 text-slate-500"}`}>All</button>
            {["On Shift", "Available", "Off", "Rest (HOS)"].map((s) => (
              <button key={s} type="button" onClick={() => setFilterStatus(s === filterStatus ? "" : s)} className={`rounded-full border px-3 py-1 text-xs font-bold transition ${filterStatus === s ? "border-teal-300 bg-teal-50 text-teal-700" : "border-slate-200 text-slate-500"}`}>{s}</button>
            ))}
          </div>
          <div className="panel overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  {["Driver", "Code", "Licence", "Vehicle", "Status", "Hours This Week", "HOS Remaining", "Safety Score"].map((h) => (
                    <th key={h} className="px-4 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-500">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {filteredDrivers.map((d, i) => {
                  const hrs = Number(d.hoursThisWeek ?? 0);
                  const remaining = Math.max(0, Number(d.hosLimit ?? 70) - hrs);
                  const hosColor = remaining < 10 ? "text-red-600" : remaining < 20 ? "text-amber-600" : "text-emerald-600";
                  return (
                    <tr key={i} className="hover:bg-slate-50">
                      <td className="px-4 py-3 font-medium text-slate-900">{String(d.name ?? "")}</td>
                      <td className="px-4 py-3 text-xs text-slate-500 font-mono">{String(d.code ?? "")}</td>
                      <td className="px-4 py-3 text-xs font-semibold text-slate-700">{String(d.licenceClass ?? "")}</td>
                      <td className="px-4 py-3 text-xs text-slate-600">{String(d.vehicleCode ?? "—")}</td>
                      <td className="px-4 py-3">
                        <span className={`inline-flex items-center gap-1 text-xs font-semibold px-2 py-0.5 rounded-full ${
                          d.status === "On Shift" ? "bg-sky-50 text-sky-700" :
                          d.status === "Available" ? "bg-emerald-50 text-emerald-700" :
                          d.status === "Rest (HOS)" ? "bg-red-50 text-red-600" :
                          "bg-slate-100 text-slate-500"
                        }`}>
                          {d.status === "On Shift" ? <Sun className="h-3 w-3" /> : d.status === "Rest (HOS)" ? <Moon className="h-3 w-3" /> : null}
                          {String(d.status ?? "Off")}
                        </span>
                      </td>
                      <td className="px-4 py-3">
                        <div className="flex items-center gap-2">
                          <div className="w-16 h-1.5 bg-slate-100 rounded-full overflow-hidden">
                            <div className={`h-full rounded-full ${hrs >= 60 ? "bg-red-500" : hrs >= 50 ? "bg-amber-400" : "bg-teal-500"}`} style={{ width: `${Math.min(100, (hrs / 70) * 100)}%` }} />
                          </div>
                          <span className="text-xs font-mono text-slate-700">{hrs}h</span>
                        </div>
                      </td>
                      <td className={`px-4 py-3 text-xs font-bold ${hosColor}`}>{remaining}h left</td>
                      <td className="px-4 py-3">
                        <span className={`text-xs font-bold ${Number(d.safetyScore) >= 90 ? "text-emerald-700" : Number(d.safetyScore) >= 75 ? "text-amber-600" : "text-red-600"}`}>
                          {Number(d.safetyScore)}/100
                        </span>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* ── AI Insights Tab ── */}
      {tab === "Insights" && (
        <div className="flex flex-col gap-3">
          {[
            { title: `${nearHosLimit} drivers approaching HOS 70-hour weekly limit`, body: "Drivers DRV-007, DRV-005 and DRV-009 have 62h, 55h and 44h respectively. Reassign weekend runs to DRV-010 (29h) to prevent forced rest-day disruption.", priority: "High", action: "Rebalance Roster" },
            { title: "Schedule coverage gap: Saturday Night shift understaffed", body: "Current schedule leaves only 1 driver on Night shift Saturday. 3 active jobs require Night-shift coverage. Move DRV-002 from Off to Night to resolve.", priority: "High", action: "Fix Coverage" },
            { title: "Optimize Morning shift distribution for fuel efficiency", body: "Clustering Morning drivers in DXB-North routes reduces average deadhead by 18km per trip. AI routing overlay can implement automatically.", priority: "Medium", action: "Apply Routing" },
            { title: "2 drivers with C+E licence underutilized this week", body: "DRV-004 and DRV-010 hold C+E truck licences but are scheduled for van routes. Reassigning heavy loads frees up 2 C-licence drivers for multi-stop city runs.", priority: "Medium", action: "Optimize Assignment" },
            { title: "Driver DRV-007 flagged for 3 consecutive Night shifts", body: "Regulatory guidelines recommend no more than 2 consecutive Night shifts without a day break. Reschedule Thursday to Off to maintain driver health compliance.", priority: "Low", action: "Adjust Roster" },
          ].map((rec, i) => (
            <div key={i} className="panel flex items-start gap-4 p-4">
              <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-violet-50 border border-violet-200">
                <Bot className="h-4 w-4 text-violet-600" />
              </div>
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2 flex-wrap mb-1">
                  <p className="font-semibold text-slate-900 text-sm">{rec.title}</p>
                  <span className={`rounded-full border px-2 py-0.5 text-[10px] font-bold uppercase ${rec.priority === "High" ? "border-red-200 bg-red-50 text-red-700" : rec.priority === "Medium" ? "border-amber-200 bg-amber-50 text-amber-700" : "border-slate-200 bg-slate-50 text-slate-500"}`}>{rec.priority}</span>
                </div>
                <p className="text-sm text-slate-600 leading-relaxed">{rec.body}</p>
                <button type="button" className="mt-2 text-xs font-semibold text-teal-600 hover:text-teal-700 transition">{rec.action} →</button>
              </div>
              <Clock className="h-4 w-4 shrink-0 text-slate-400 mt-0.5" />
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
