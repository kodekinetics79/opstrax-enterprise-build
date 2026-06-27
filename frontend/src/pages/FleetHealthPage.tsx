import React, { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Activity, AlertTriangle, CheckCircle, ChevronRight,
  Clock, RefreshCw, Shield, Truck, User, Wrench, X,
  XCircle, Zap,
} from "lucide-react";
import { fleetHealthApi } from "@/services/fleetHealthApi";
import { maintenanceApi } from "@/services/maintenanceApi";
import { safetyApi } from "@/services/safetyApi";
import { coachingApi } from "@/services/coachingApi";
import { useHasPermission } from "@/hooks/usePermission";
import { PageHeader } from "@/components/ui";
import type { AnyRecord } from "@/types";

// ── Severity helpers ────────────────────────────────────────────────────────

type Sev = "critical" | "high" | "medium" | "low" | "positive";

function sevColor(sev: Sev | string): string {
  switch (sev) {
    case "critical": return "text-red-600";
    case "high":     return "text-orange-500";
    case "medium":   return "text-amber-500";
    case "positive": return "text-emerald-600";
    default:         return "text-slate-500";
  }
}

function sevBg(sev: Sev | string): string {
  switch (sev) {
    case "critical": return "bg-red-50 border-red-200";
    case "high":     return "bg-orange-50 border-orange-200";
    case "medium":   return "bg-amber-50 border-amber-200";
    case "positive": return "bg-emerald-50 border-emerald-200";
    default:         return "bg-slate-50 border-slate-200";
  }
}

function sevDot(sev: Sev | string): string {
  switch (sev) {
    case "critical": return "bg-red-500";
    case "high":     return "bg-orange-500";
    case "medium":   return "bg-amber-400";
    case "positive": return "bg-emerald-500";
    default:         return "bg-slate-400";
  }
}

function sevLabel(sev: Sev | string): string {
  return String(sev).charAt(0).toUpperCase() + String(sev).slice(1);
}

function num(v: unknown, fallback = 0): number {
  const n = Number(v);
  return isNaN(n) ? fallback : n;
}

function scoreColor(score: number): string {
  if (score >= 85) return "text-emerald-600";
  if (score >= 70) return "text-amber-500";
  if (score >= 50) return "text-orange-500";
  return "text-red-600";
}

// ── Readiness gauge strip ───────────────────────────────────────────────────

function ReadinessStrip({ summary }: { summary: AnyRecord }) {
  const score     = num(summary.fleetHealthScore, 50);
  const total     = num(summary.totalVehicles);
  const ready     = num(summary.dispatchReadyVehicles);
  const oos       = num(summary.oosVehicles);
  const blockers  = num(summary.criticalDefectVehicles);
  const avgSafety = num(summary.avgSafetyScore, 100);

  return (
    <div className="bg-white border border-slate-200 rounded-xl p-5 flex flex-wrap gap-6 items-center shadow-sm">
      {/* Fleet health score */}
      <div className="flex items-center gap-4 pr-6 border-r border-slate-200">
        <div className="relative h-16 w-16 shrink-0">
          <svg viewBox="0 0 36 36" className="h-16 w-16 -rotate-90">
            <circle cx="18" cy="18" r="14" fill="none" stroke="#e2e8f0" strokeWidth="3" />
            <circle
              cx="18" cy="18" r="14" fill="none"
              stroke={score >= 80 ? "#10b981" : score >= 60 ? "#f59e0b" : "#ef4444"}
              strokeWidth="3"
              strokeDasharray={`${(score / 100) * 87.96} 87.96`}
              strokeLinecap="round"
            />
          </svg>
          <span className={`absolute inset-0 flex items-center justify-center text-sm font-bold ${scoreColor(score)}`}>
            {score}%
          </span>
        </div>
        <div>
          <p className="text-[11px] font-bold uppercase tracking-widest text-slate-400">Fleet Health</p>
          <p className={`text-2xl font-bold ${scoreColor(score)}`}>{score}%</p>
          <p className="text-xs text-slate-500 mt-0.5">
            {score >= 85 ? "Good standing" : score >= 65 ? "Needs attention" : "Action required"}
          </p>
        </div>
      </div>

      {/* KPIs */}
      <div className="flex flex-wrap gap-8">
        <div className="text-center">
          <p className="text-[10px] font-bold uppercase tracking-widest text-slate-400 mb-1">Dispatch Ready</p>
          <p className="text-xl font-bold text-emerald-600">{ready}<span className="text-slate-400 font-normal text-sm">/{total}</span></p>
          <p className="text-xs text-slate-500">vehicles</p>
        </div>
        <div className="text-center">
          <p className="text-[10px] font-bold uppercase tracking-widest text-slate-400 mb-1">Out of Service</p>
          <p className={`text-xl font-bold ${oos > 0 ? "text-red-600" : "text-slate-400"}`}>{oos}</p>
          <p className="text-xs text-slate-500">vehicles</p>
        </div>
        <div className="text-center">
          <p className="text-[10px] font-bold uppercase tracking-widest text-slate-400 mb-1">Dispatch Blockers</p>
          <p className={`text-xl font-bold ${blockers > 0 ? "text-red-600" : "text-slate-400"}`}>{blockers}</p>
          <p className="text-xs text-slate-500">critical defects</p>
        </div>
        <div className="text-center">
          <p className="text-[10px] font-bold uppercase tracking-widest text-slate-400 mb-1">Safety Score</p>
          <p className={`text-xl font-bold ${scoreColor(avgSafety)}`}>{avgSafety}%</p>
          <p className="text-xs text-slate-500">fleet avg</p>
        </div>
        <div className="text-center">
          <p className="text-[10px] font-bold uppercase tracking-widest text-slate-400 mb-1">Overdue PM</p>
          <p className={`text-xl font-bold ${num(summary.overduePmVehicles) > 0 ? "text-amber-500" : "text-slate-400"}`}>
            {num(summary.overduePmVehicles)}
          </p>
          <p className="text-xs text-slate-500">vehicles</p>
        </div>
      </div>
    </div>
  );
}

// ── System Fleet Insight panel ──────────────────────────────────────────────

function InsightPanel({ insights }: { insights: AnyRecord[] }) {
  if (!insights.length) return null;
  return (
    <div className="bg-white border border-slate-200 rounded-xl shadow-sm overflow-hidden">
      <div className="px-5 py-3 border-b border-slate-100 flex items-center gap-2">
        <Activity className="h-4 w-4 text-slate-500" />
        <span className="text-xs font-bold uppercase tracking-widest text-slate-500">
          System Fleet Insight
        </span>
        <span className="ml-auto text-[10px] text-slate-400 font-medium">
          Rule-based · Real data
        </span>
      </div>
      <ul className="divide-y divide-slate-100">
        {insights.map((ins, i) => {
          const sev = String(ins.severity ?? "medium") as Sev;
          return (
            <li key={i} className="flex items-start gap-3 px-5 py-3">
              <span className={`mt-0.5 h-2 w-2 rounded-full shrink-0 ${sevDot(sev)}`} />
              <div className="min-w-0">
                <p className="text-sm text-slate-700 leading-snug">{String(ins.message ?? "")}</p>
                <p className="text-[10px] text-slate-400 mt-0.5">{String(ins.dataSource ?? "")}</p>
              </div>
            </li>
          );
        })}
      </ul>
    </div>
  );
}

// ── Metrics strip subcomponents ─────────────────────────────────────────────

function VehicleMetricsStrip({ metrics }: { metrics: AnyRecord }) {
  return (
    <div className="flex gap-4 px-4 py-2 bg-slate-50/70 text-xs text-slate-600 flex-wrap">
      {Boolean(metrics.outOfService) && <span className="text-red-600 font-semibold">Out of Service</span>}
      {num(metrics.criticalDefects) > 0 && <span className="text-red-600">{num(metrics.criticalDefects)} critical defect{num(metrics.criticalDefects) > 1 ? "s" : ""}</span>}
      {num(metrics.activeFaultCodes) > 0 && <span className="text-orange-600">{num(metrics.activeFaultCodes)} fault code{num(metrics.activeFaultCodes) > 1 ? "s" : ""}</span>}
      {num(metrics.overduePm) > 0 && <span className="text-amber-600">{num(metrics.overduePm)} PM overdue</span>}
      {num(metrics.openWorkOrders) > 0 && <span className="text-slate-600">{num(metrics.openWorkOrders)} open WO{num(metrics.openWorkOrders) > 1 ? "s" : ""}</span>}
      {Boolean(metrics.deviceOffline) && <span className="text-slate-500">Device offline</span>}
    </div>
  );
}

function DriverMetricsStrip({ metrics }: { metrics: AnyRecord }) {
  return (
    <div className="flex gap-4 px-4 py-2 bg-slate-50/70 text-xs text-slate-600 flex-wrap">
      <span className={scoreColor(num(metrics.safetyScore, 100))}>Safety {num(metrics.safetyScore, 100)}%</span>
      {num(metrics.openSafetyEvents) > 0 && <span className="text-red-600">{num(metrics.openSafetyEvents)} open event{num(metrics.openSafetyEvents) > 1 ? "s" : ""}</span>}
      {num(metrics.overdueCoaching) > 0 && <span className="text-orange-600">{num(metrics.overdueCoaching)} coaching overdue</span>}
      {num(metrics.openCoachingTasks) > num(metrics.overdueCoaching) && (
        <span className="text-amber-600">{num(metrics.openCoachingTasks) - num(metrics.overdueCoaching)} pending coaching</span>
      )}
    </div>
  );
}

// ── Risk item card ──────────────────────────────────────────────────────────

interface RiskCardProps {
  item: AnyRecord;
  onVehicleClick: (id: number) => void;
  onDriverClick: (id: number) => void;
  onCreateWO: (vehicleId: number) => void;
  onAckDefect: (vehicleId: number) => void;
  onAssignCoaching: (driverId: number) => void;
  canManageMaint: boolean;
  canManageSafety: boolean;
  isMutating: boolean;
}

function RiskCard({
  item, onVehicleClick, onDriverClick, onCreateWO,
  onAckDefect, onAssignCoaching, canManageMaint, canManageSafety, isMutating,
}: RiskCardProps) {
  // Boolean() breaks TS 5.9 aliased-condition narrowing that leaks into JSX sibling types
  const isVehicle = Boolean(item.entityType === "vehicle");
  const isDriver  = Boolean(!isVehicle);
  const sev       = String(item.severity ?? "low") as Sev;
  const id        = num(item.entityId);
  const reasons   = (item.reasons as string[]) ?? [];
  const metrics   = (item.metrics as AnyRecord) ?? {} as AnyRecord;
  const score     = num(item.priorityScore);
  const blocking  = Boolean(item.blockingDispatch);


  const body = (
    <>
      {/* Header */}
      <div className="flex items-center gap-3 px-4 py-3 border-b border-slate-100">
        <span className={`h-2.5 w-2.5 rounded-full shrink-0 ${sevDot(sev)}`} />
        <div className="flex items-center gap-2 min-w-0 flex-1">
          {isVehicle
            ? <Truck className="h-4 w-4 text-slate-400 shrink-0" />
            : <User className="h-4 w-4 text-slate-400 shrink-0" />}
          <span className="font-bold text-slate-800 text-sm truncate">
            {String(item.displayName ?? "")}
          </span>
          {isVehicle && !!item.vehicleType && (
            <span className="text-xs text-slate-500 shrink-0">· {String(item.vehicleType)}</span>
          )}
          {isDriver && !!item.driverCode && (
            <span className="text-xs text-slate-500 shrink-0">· {String(item.driverCode)}</span>
          )}
        </div>
        <div className="flex items-center gap-2 shrink-0">
          {blocking && (
            <span className="inline-flex items-center gap-1 text-[10px] font-bold bg-red-100 text-red-700 border border-red-200 rounded-full px-2 py-0.5 uppercase tracking-wide">
              <XCircle className="h-3 w-3" />Blocked
            </span>
          )}
          <span className={`text-[10px] font-bold uppercase tracking-wide px-2 py-0.5 rounded-full border ${
            sev === "critical" ? "bg-red-100 text-red-700 border-red-200" :
            sev === "high"     ? "bg-orange-100 text-orange-700 border-orange-200" :
            sev === "medium"   ? "bg-amber-100 text-amber-700 border-amber-200" :
                                 "bg-slate-100 text-slate-600 border-slate-200"}`}>
            {sevLabel(sev)}
          </span>
          <span className="text-[10px] text-slate-400 font-mono">#{Math.round(score)}</span>
        </div>
      </div>

      {/* Metrics strip */}
      <div className="flex gap-4 px-4 py-2 bg-slate-50/70 text-xs text-slate-600 flex-wrap">
        {isVehicle && Boolean(metrics.outOfService) && <span className="text-red-600 font-semibold">Out of Service</span>}
        {isVehicle && num(metrics.criticalDefects) > 0 && <span className="text-red-600">{num(metrics.criticalDefects)} critical defect{num(metrics.criticalDefects) > 1 ? "s" : ""}</span>}
        {isVehicle && num(metrics.activeFaultCodes) > 0 && <span className="text-orange-600">{num(metrics.activeFaultCodes)} fault code{num(metrics.activeFaultCodes) > 1 ? "s" : ""}</span>}
        {isVehicle && num(metrics.overduePm) > 0 && <span className="text-amber-600">{num(metrics.overduePm)} PM overdue</span>}
        {isVehicle && num(metrics.openWorkOrders) > 0 && <span className="text-slate-600">{num(metrics.openWorkOrders)} open WO{num(metrics.openWorkOrders) > 1 ? "s" : ""}</span>}
        {isVehicle && Boolean(metrics.deviceOffline) && <span className="text-slate-500">Device offline</span>}
        {isDriver && <span className={scoreColor(num(metrics.safetyScore, 100))}>Safety {num(metrics.safetyScore, 100)}%</span>}
        {isDriver && num(metrics.openSafetyEvents) > 0 && <span className="text-red-600">{num(metrics.openSafetyEvents)} open event{num(metrics.openSafetyEvents) > 1 ? "s" : ""}</span>}
        {isDriver && num(metrics.overdueCoaching) > 0 && <span className="text-orange-600">{num(metrics.overdueCoaching)} coaching overdue</span>}
        {isDriver && num(metrics.openCoachingTasks) > num(metrics.overdueCoaching) && <span className="text-amber-600">{num(metrics.openCoachingTasks) - num(metrics.overdueCoaching)} pending coaching</span>}
      </div>

      {/* Reasons */}
      {reasons.length > 0 && (
        <ul className="px-4 py-2 space-y-0.5">
          {reasons.slice(0, 3).map((r, i) => (
            <li key={i} className="text-xs text-slate-600 flex items-center gap-1.5">
              <span className="h-1 w-1 rounded-full bg-slate-400 shrink-0" />{r}
            </li>
          ))}
        </ul>
      )}

      {/* Recommended action */}
      {item.recommendedAction && (
        <div className="px-4 py-2 bg-blue-50/50 border-t border-blue-100">
          <p className="text-xs text-blue-700">
            <span className="font-semibold">Recommended:</span> {String(item.recommendedAction)}
          </p>
        </div>
      )}

      {/* Actions */}
      <div className="flex gap-2 px-4 py-3 border-t border-slate-100 mt-auto flex-wrap">
        <button
          type="button"
          onClick={() => isVehicle ? onVehicleClick(id) : onDriverClick(id)}
          className="flex items-center gap-1.5 text-xs font-semibold text-slate-700 bg-white border border-slate-200 rounded-lg px-3 py-1.5 hover:bg-slate-50 hover:border-slate-300 transition-colors"
        >
          <ChevronRight className="h-3.5 w-3.5" />View Detail
        </button>
        {isVehicle && canManageMaint && (
          <>
            <button
              type="button"
              disabled={isMutating}
              onClick={() => onCreateWO(id)}
              className="flex items-center gap-1.5 text-xs font-semibold text-white bg-slate-800 border border-slate-800 rounded-lg px-3 py-1.5 hover:bg-slate-700 transition-colors disabled:opacity-50"
            >
              <Wrench className="h-3.5 w-3.5" />Create WO
            </button>
            {num(metrics.openDefects) > 0 && (
              <button
                type="button"
                disabled={isMutating}
                onClick={() => onAckDefect(id)}
                className="flex items-center gap-1.5 text-xs font-semibold text-amber-700 bg-amber-50 border border-amber-200 rounded-lg px-3 py-1.5 hover:bg-amber-100 transition-colors disabled:opacity-50"
              >
                <CheckCircle className="h-3.5 w-3.5" />Ack Defect
              </button>
            )}
          </>
        )}
        {isDriver && canManageSafety && (
          <button
            type="button"
            disabled={isMutating}
            onClick={() => onAssignCoaching(id)}
            className="flex items-center gap-1.5 text-xs font-semibold text-white bg-slate-800 border border-slate-800 rounded-lg px-3 py-1.5 hover:bg-slate-700 transition-colors disabled:opacity-50"
          >
            <Shield className="h-3.5 w-3.5" />Assign Coaching
          </button>
        )}
      </div>
    </>
  );
  return (
    <div className={`border rounded-xl bg-white shadow-sm flex flex-col overflow-hidden ${sevBg(sev)}`}>
      {body}
    </div>
  );
}

// ── Vehicle drawer ──────────────────────────────────────────────────────────

function VehicleDrawer({
  vehicleId, onClose, canManageMaint, onWOCreated,
}: {
  vehicleId: number | null;
  onClose: () => void;
  canManageMaint: boolean;
  onWOCreated: () => void;
}) {
  const { data, isLoading, isError } = useQuery<AnyRecord>({
    queryKey: ["fleet-health", "vehicle", vehicleId],
    queryFn: () => fleetHealthApi.vehicleDetail(vehicleId!),
    enabled: vehicleId !== null,
  });

  const qc = useQueryClient();

  const ackDefect = useMutation({
    mutationFn: (defectId: number) => maintenanceApi.acknowledgeDefect(defectId),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["fleet-health"] }),
  });

  const resolveDefect = useMutation({
    mutationFn: (defectId: number) => maintenanceApi.resolveDefect(defectId),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["fleet-health"] }),
  });

  const createWO = useMutation({
    mutationFn: (vehicleId: number) =>
      maintenanceApi.createWorkOrder({ vehicleId, title: "Fleet health review", priority: "High" }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["fleet-health"] });
      onWOCreated();
    },
  });

  if (vehicleId === null) return null;

  const veh       = (data?.vehicle as AnyRecord) ?? {};
  const defects   = (data?.openDefects  as AnyRecord[]) ?? [];
  const faults    = (data?.activeFaultCodes as AnyRecord[]) ?? [];
  const workOrders = (data?.openWorkOrders as AnyRecord[]) ?? [];
  const pmItems   = (data?.overduePmItems as AnyRecord[]) ?? [];
  const inspections = (data?.recentInspections as AnyRecord[]) ?? [];
  const assignment = data?.currentAssignment as AnyRecord | null;
  const oos       = Boolean(veh.out_of_service);
  const vehicleInfoRows: [string, string][] = [
    ["Type",            String(veh.type ?? "—")],
    ["Status",          String(veh.status ?? "—")],
    ["Readiness",       `${num(veh.readiness_score, 50)}%`],
    ["Device",          String(veh.device_status ?? "—")],
    ["Assigned Driver", String(veh.assigned_driver_name ?? "Unassigned")],
    ["Odometer",        veh.odometer_miles ? `${num(veh.odometer_miles).toLocaleString()} mi` : "—"],
  ];

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-end">
      <div className="absolute inset-0 bg-slate-900/30 backdrop-blur-sm" onClick={onClose} />
      <div className="relative z-10 h-full w-full max-w-lg bg-white shadow-2xl flex flex-col overflow-hidden">
        {/* Drawer header */}
        <div className="flex items-center gap-3 px-6 py-4 border-b border-slate-200 bg-white shrink-0">
          <Truck className="h-5 w-5 text-slate-500" />
          <div className="flex-1 min-w-0">
            <p className="text-[10px] font-bold uppercase tracking-widest text-slate-400">Vehicle Detail</p>
            <h2 className="font-bold text-slate-900 text-base truncate">
              {String(veh.vehicle_code ?? `Vehicle #${vehicleId}`)}
            </h2>
          </div>
          <div className="flex items-center gap-2 shrink-0">
            {oos && (
              <span className="text-[10px] font-bold bg-red-100 text-red-700 border border-red-200 rounded-full px-2 py-0.5 uppercase">
                Out of Service
              </span>
            )}
          </div>
          <button type="button" onClick={onClose} aria-label="Close vehicle detail" className="p-1.5 rounded-lg hover:bg-slate-100 text-slate-500">
            <X className="h-4 w-4" aria-hidden="true" />
          </button>
        </div>

        {isLoading && (
          <div className="flex-1 flex items-center justify-center">
            <RefreshCw className="h-6 w-6 text-slate-400 animate-spin" />
          </div>
        )}
        {isError && (
          <div className="flex-1 flex items-center justify-center p-6">
            <p className="text-sm text-red-600">Failed to load vehicle detail.</p>
          </div>
        )}

        {data && !isLoading && (
          <div className="flex-1 overflow-y-auto p-6 space-y-6">
            {/* Vehicle overview */}
            <div className="grid grid-cols-2 gap-3">
              {vehicleInfoRows.map(([label, value]) => (
                <div key={label} className="bg-slate-50 rounded-lg px-3 py-2">
                  <p className="text-[10px] text-slate-400 font-semibold uppercase tracking-wide">{label}</p>
                  <p className="text-sm font-semibold text-slate-800 mt-0.5">{value}</p>
                </div>
              ))}
            </div>

            {/* Dispatch status */}
            {assignment ? (
              <Section title="Active Assignment" icon={<Activity className="h-4 w-4" />}>
                <div className="bg-blue-50 border border-blue-200 rounded-lg p-3 text-sm space-y-1">
                  <p className="text-blue-800 font-semibold">{String(assignment.status ?? "")}</p>
                  {!!assignment.pickup_address && <p className="text-blue-700 text-xs">From: {String(assignment.pickup_address)}</p>}
                  {!!assignment.delivery_address && <p className="text-blue-700 text-xs">To: {String(assignment.delivery_address)}</p>}
                  {!!assignment.driver_name && <p className="text-blue-700 text-xs">Driver: {String(assignment.driver_name)}</p>}
                </div>
              </Section>
            ) : (
              <Section title="Dispatch Status" icon={<Activity className="h-4 w-4" />}>
                <p className="text-sm text-slate-500 italic">
                  {oos ? "Vehicle cannot be dispatched — out of service." : "No active assignment."}
                </p>
              </Section>
            )}

            {/* Open defects */}
            <Section
              title={`Open Defects (${defects.length})`}
              icon={<AlertTriangle className="h-4 w-4" />}
              badge={defects.length > 0 ? "critical" : undefined}
            >
              {defects.length === 0
                ? <p className="text-sm text-slate-500 italic">No open defects.</p>
                : defects.map((d, i) => {
                    const isCrit = Boolean(d.out_of_service);
                    return (
                      <div key={i} className={`flex items-start justify-between gap-2 rounded-lg px-3 py-2 text-sm border ${isCrit ? "bg-red-50 border-red-200" : "bg-slate-50 border-slate-200"}`}>
                        <div className="flex-1 min-w-0">
                          <p className={`font-semibold ${isCrit ? "text-red-700" : "text-slate-700"}`}>
                            {isCrit ? "⚠ " : ""}{String(d.defect_description ?? d.id)}
                          </p>
                          <p className="text-xs text-slate-500 mt-0.5">{String(d.severity ?? "")} · {String(d.status ?? "")}</p>
                        </div>
                        {canManageMaint && (
                          <div className="flex gap-1 shrink-0">
                            <button
                              type="button"
                              disabled={ackDefect.isPending}
                              onClick={() => ackDefect.mutate(num(d.id))}
                              className="text-[10px] font-bold text-amber-700 bg-amber-50 border border-amber-200 rounded px-2 py-0.5 hover:bg-amber-100 disabled:opacity-50"
                            >Ack</button>
                            <button
                              type="button"
                              disabled={resolveDefect.isPending}
                              onClick={() => resolveDefect.mutate(num(d.id))}
                              className="text-[10px] font-bold text-emerald-700 bg-emerald-50 border border-emerald-200 rounded px-2 py-0.5 hover:bg-emerald-100 disabled:opacity-50"
                            >Resolve</button>
                          </div>
                        )}
                      </div>
                    );
                  })}
            </Section>

            {/* Fault codes */}
            <Section
              title={`Active Fault Codes (${faults.length})`}
              icon={<Zap className="h-4 w-4" />}
            >
              {faults.length === 0
                ? <p className="text-sm text-slate-500 italic">No active fault codes.</p>
                : faults.map((f, i) => (
                    <div key={i} className="bg-orange-50 border border-orange-200 rounded-lg px-3 py-2 text-sm">
                      <p className="font-semibold text-orange-800">{String(f.code ?? "")} — {String(f.component ?? "")}</p>
                      {!!f.description && <p className="text-xs text-orange-600 mt-0.5">{String(f.description)}</p>}
                      {num(f.recurrence_count) > 1 && (
                        <p className="text-xs text-orange-500 mt-0.5">Recurred {num(f.recurrence_count)} times</p>
                      )}
                    </div>
                  ))}
            </Section>

            {/* Work orders */}
            <Section
              title={`Open Work Orders (${workOrders.length})`}
              icon={<Wrench className="h-4 w-4" />}
            >
              {workOrders.length === 0
                ? <p className="text-sm text-slate-500 italic">No open work orders.</p>
                : workOrders.map((wo, i) => (
                    <div key={i} className="bg-slate-50 border border-slate-200 rounded-lg px-3 py-2 text-sm">
                      <p className="font-semibold text-slate-800">{String(wo.work_order_number ?? "")} — {String(wo.title ?? wo.service_type ?? "")}</p>
                      <div className="flex gap-3 text-xs text-slate-500 mt-0.5">
                        <span>Priority: {String(wo.priority ?? "Normal")}</span>
                        <span>Status: {String(wo.status ?? "")}</span>
                        {!!wo.due_date && <span>Due: {String(wo.due_date)}</span>}
                      </div>
                    </div>
                  ))}
            </Section>

            {/* Overdue PM */}
            <Section
              title={`Overdue PM (${pmItems.length})`}
              icon={<Clock className="h-4 w-4" />}
              badge={pmItems.length > 0 ? "medium" : undefined}
            >
              {pmItems.length === 0
                ? <p className="text-sm text-slate-500 italic">No overdue PM items.</p>
                : pmItems.map((pm, i) => (
                    <div key={i} className="bg-amber-50 border border-amber-200 rounded-lg px-3 py-2 text-sm">
                      <p className="font-semibold text-amber-800">{String(pm.service_type ?? "")}</p>
                      <p className="text-xs text-amber-600 mt-0.5">
                        Due: {pm.due_date ? String(pm.due_date) : "—"} · Status: {String(pm.status ?? "")}
                      </p>
                    </div>
                  ))}
            </Section>

            {/* Recent inspections */}
            <Section title={`Recent Inspections (${inspections.length})`} icon={<CheckCircle className="h-4 w-4" />}>
              {inspections.length === 0
                ? <p className="text-sm text-slate-500 italic">No recent inspections.</p>
                : inspections.map((ins, i) => (
                    <div key={i} className="bg-slate-50 border border-slate-200 rounded-lg px-3 py-2 text-sm flex items-center justify-between">
                      <div>
                        <p className="font-semibold text-slate-800">{String(ins.inspection_type ?? "Inspection")}</p>
                        <p className="text-xs text-slate-500">{String(ins.submitted_at ?? "")}</p>
                      </div>
                      <div className="text-right shrink-0">
                        <span className={`text-xs font-bold ${Boolean(ins.safe_to_operate) ? "text-emerald-600" : "text-red-600"}`}>
                          {Boolean(ins.safe_to_operate) ? "Safe" : "Unsafe"}
                        </span>
                        {num(ins.critical_defect_count) > 0 && (
                          <p className="text-[10px] text-red-500">{num(ins.critical_defect_count)} critical</p>
                        )}
                      </div>
                    </div>
                  ))}
            </Section>
          </div>
        )}

        {/* Drawer footer */}
        {data && canManageMaint && (
          <div className="px-6 py-4 border-t border-slate-200 bg-slate-50 shrink-0">
            <button
              type="button"
              disabled={createWO.isPending}
              onClick={() => createWO.mutate(vehicleId)}
              className="w-full flex items-center justify-center gap-2 text-sm font-bold text-white bg-slate-800 rounded-xl px-4 py-2.5 hover:bg-slate-700 transition-colors disabled:opacity-50"
            >
              <Wrench className="h-4 w-4" />
              {createWO.isPending ? "Creating…" : "Create Work Order"}
            </button>
          </div>
        )}
      </div>
    </div>
  );
}

// ── Driver drawer ───────────────────────────────────────────────────────────

function DriverDrawer({
  driverId, onClose, canManageSafety, onActionTaken,
}: {
  driverId: number | null;
  onClose: () => void;
  canManageSafety: boolean;
  onActionTaken: () => void;
}) {
  const { data, isLoading, isError } = useQuery<AnyRecord>({
    queryKey: ["fleet-health", "driver", driverId],
    queryFn:  () => fleetHealthApi.driverDetail(driverId!),
    enabled:  driverId !== null,
  });

  const qc = useQueryClient();

  const ackEvent = useMutation({
    mutationFn: (eventId: number) => safetyApi.review(eventId),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["fleet-health"] });
      onActionTaken();
    },
  });

  const completeCoaching = useMutation({
    mutationFn: (taskId: number) => safetyApi.completeCoaching(taskId),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["fleet-health"] });
      onActionTaken();
    },
  });

  if (driverId === null) return null;

  const drv          = (data?.driver as AnyRecord) ?? {};
  const events       = (data?.openSafetyEvents  as AnyRecord[]) ?? [];
  const coachingTasks = (data?.coachingTasks as AnyRecord[]) ?? [];
  const hos          = data?.hosStatus as AnyRecord | null;
  const assignment   = data?.currentAssignment as AnyRecord | null;
  const safetyScore  = num(drv.safety_score, 100);
  const driverInfoRows: [string, string][] = [
    ["Driver Code",   String(drv.driver_code ?? "—")],
    ["Status",        String(drv.status ?? "—")],
    ["Safety Score",  `${safetyScore}%`],
    ["Risk Score",    `${num(drv.risk_score)}%`],
    ["Vehicle",       String(drv.assigned_vehicle_code ?? "Unassigned")],
    ["License",       String(drv.license_number ?? "—")],
  ];

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-end">
      <div className="absolute inset-0 bg-slate-900/30 backdrop-blur-sm" onClick={onClose} />
      <div className="relative z-10 h-full w-full max-w-lg bg-white shadow-2xl flex flex-col overflow-hidden">
        {/* Header */}
        <div className="flex items-center gap-3 px-6 py-4 border-b border-slate-200 bg-white shrink-0">
          <User className="h-5 w-5 text-slate-500" />
          <div className="flex-1 min-w-0">
            <p className="text-[10px] font-bold uppercase tracking-widest text-slate-400">Driver Risk Detail</p>
            <h2 className="font-bold text-slate-900 text-base truncate">
              {String(drv.full_name ?? `Driver #${driverId}`)}
            </h2>
          </div>
          <div className={`text-sm font-bold ${scoreColor(safetyScore)} shrink-0`}>
            Safety {safetyScore}%
          </div>
          <button type="button" onClick={onClose} aria-label="Close driver detail" className="p-1.5 rounded-lg hover:bg-slate-100 text-slate-500">
            <X className="h-4 w-4" aria-hidden="true" />
          </button>
        </div>

        {isLoading && (
          <div className="flex-1 flex items-center justify-center">
            <RefreshCw className="h-6 w-6 text-slate-400 animate-spin" />
          </div>
        )}
        {isError && (
          <div className="flex-1 flex items-center justify-center p-6">
            <p className="text-sm text-red-600">Failed to load driver detail.</p>
          </div>
        )}

        {data && !isLoading && (
          <div className="flex-1 overflow-y-auto p-6 space-y-6">
            {/* Driver overview */}
            <div className="grid grid-cols-2 gap-3">
              {driverInfoRows.map(([label, value]) => (
                <div key={label} className="bg-slate-50 rounded-lg px-3 py-2">
                  <p className="text-[10px] text-slate-400 font-semibold uppercase tracking-wide">{label}</p>
                  <p className={`text-sm font-semibold mt-0.5 ${label === "Safety Score" ? scoreColor(safetyScore) : "text-slate-800"}`}>
                    {value}
                  </p>
                </div>
              ))}
            </div>

            {/* Current assignment */}
            {assignment ? (
              <Section title="Current Assignment" icon={<Activity className="h-4 w-4" />}>
                <div className="bg-blue-50 border border-blue-200 rounded-lg p-3 text-sm">
                  <p className="text-blue-800 font-semibold">{String(assignment.status ?? "")}</p>
                  {!!assignment.pickup_address && <p className="text-blue-700 text-xs mt-0.5">From: {String(assignment.pickup_address)}</p>}
                  {!!assignment.delivery_address && <p className="text-blue-700 text-xs">To: {String(assignment.delivery_address)}</p>}
                  {!!assignment.vehicle_code && <p className="text-blue-700 text-xs">Vehicle: {String(assignment.vehicle_code)}</p>}
                </div>
              </Section>
            ) : null}

            {/* HOS */}
            <Section title="Hours of Service" icon={<Clock className="h-4 w-4" />}>
              {hos ? (
                <div className={`border rounded-lg p-3 text-sm ${String(hos.hos_status) === "Violation" ? "bg-red-50 border-red-200" : "bg-slate-50 border-slate-200"}`}>
                  <div className="flex gap-4">
                    <div>
                      <p className="text-[10px] text-slate-400 uppercase font-bold">Drive Remaining</p>
                      <p className="font-bold text-slate-800">
                        {Math.floor(num(hos.drive_time_remaining_minutes) / 60)}h {num(hos.drive_time_remaining_minutes) % 60}m
                      </p>
                    </div>
                    <div>
                      <p className="text-[10px] text-slate-400 uppercase font-bold">HOS Status</p>
                      <p className={`font-bold ${String(hos.hos_status) === "Violation" ? "text-red-600" : String(hos.hos_status) === "Warning" ? "text-amber-600" : "text-emerald-600"}`}>
                        {String(hos.hos_status ?? "OK")}
                      </p>
                    </div>
                  </div>
                </div>
              ) : (
                <p className="text-sm text-slate-400 italic">
                  HOS data unavailable for this driver. ELD integration required.
                </p>
              )}
            </Section>

            {/* Open safety events */}
            <Section
              title={`Open Safety Events (${events.length})`}
              icon={<AlertTriangle className="h-4 w-4" />}
              badge={events.length > 0 ? "high" : undefined}
            >
              {events.length === 0
                ? <p className="text-sm text-slate-500 italic">No open safety events.</p>
                : events.map((ev, i) => (
                    <div key={i} className="bg-orange-50 border border-orange-200 rounded-lg px-3 py-2 text-sm flex items-start justify-between gap-2">
                      <div className="flex-1 min-w-0">
                        <p className="font-semibold text-orange-800">{String(ev.event_type ?? "")}</p>
                        <p className="text-xs text-orange-600 mt-0.5">{String(ev.severity ?? "")} · {String(ev.review_status ?? "")}</p>
                        {!!ev.description && <p className="text-xs text-orange-600 truncate">{String(ev.description)}</p>}
                      </div>
                      {canManageSafety && (
                        <button
                          type="button"
                          disabled={ackEvent.isPending}
                          onClick={() => ackEvent.mutate(num(ev.id))}
                          className="text-[10px] font-bold text-amber-700 bg-amber-50 border border-amber-200 rounded px-2 py-0.5 hover:bg-amber-100 shrink-0 disabled:opacity-50"
                        >Acknowledge</button>
                      )}
                    </div>
                  ))}
            </Section>

            {/* Coaching tasks */}
            <Section
              title={`Coaching Tasks (${coachingTasks.length})`}
              icon={<Shield className="h-4 w-4" />}
              badge={coachingTasks.some((t) => t.due_at && new Date(String(t.due_at)) < new Date()) ? "medium" : undefined}
            >
              {coachingTasks.length === 0
                ? <p className="text-sm text-slate-500 italic">No pending coaching tasks.</p>
                : coachingTasks.map((ct, i) => {
                    const overdue = ct.due_at && new Date(String(ct.due_at)) < new Date();
                    return (
                      <div key={i} className={`border rounded-lg px-3 py-2 text-sm flex items-start justify-between gap-2 ${overdue ? "bg-amber-50 border-amber-200" : "bg-slate-50 border-slate-200"}`}>
                        <div className="flex-1 min-w-0">
                          <p className={`font-semibold ${overdue ? "text-amber-800" : "text-slate-800"}`}>
                            {overdue ? "⚠ " : ""}{String(ct.title ?? ct.coaching_type ?? "")}
                          </p>
                          <div className="flex gap-2 text-xs mt-0.5">
                            <span className={overdue ? "text-amber-600" : "text-slate-500"}>
                              {overdue ? "OVERDUE" : "Due"}: {ct.due_at ? String(ct.due_at).split("T")[0] : "—"}
                            </span>
                            <span className="text-slate-400">· {String(ct.priority ?? "")}</span>
                          </div>
                        </div>
                        {canManageSafety && (
                          <button
                            type="button"
                            disabled={completeCoaching.isPending}
                            onClick={() => completeCoaching.mutate(num(ct.id))}
                            className="text-[10px] font-bold text-emerald-700 bg-emerald-50 border border-emerald-200 rounded px-2 py-0.5 hover:bg-emerald-100 shrink-0 disabled:opacity-50"
                          >Complete</button>
                        )}
                      </div>
                    );
                  })}
            </Section>
          </div>
        )}
      </div>
    </div>
  );
}

// ── Section helper ──────────────────────────────────────────────────────────

function Section({
  title, icon, badge, children,
}: {
  title: string;
  icon?: React.ReactNode;
  badge?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-2">
      <div className="flex items-center gap-2">
        {icon && <span className="text-slate-400">{icon}</span>}
        <h3 className="text-xs font-bold uppercase tracking-widest text-slate-500">{title}</h3>
        {badge && (
          <span className={`text-[10px] font-bold uppercase px-1.5 py-0.5 rounded ${
            badge === "critical" ? "bg-red-100 text-red-700" :
            badge === "high"     ? "bg-orange-100 text-orange-700" :
            badge === "medium"   ? "bg-amber-100 text-amber-700" :
                                   "bg-slate-100 text-slate-600"}`}>
            {badge}
          </span>
        )}
      </div>
      <div className="space-y-2">{children}</div>
    </div>
  );
}

// ── Filter bar ──────────────────────────────────────────────────────────────

type CategoryFilter = "all" | "vehicle" | "driver";
type SeverityFilter = "all" | "critical" | "high" | "medium" | "low";

function FilterBar({
  category, severity, onCategory, onSeverity,
}: {
  category: CategoryFilter;
  severity: SeverityFilter;
  onCategory: (c: CategoryFilter) => void;
  onSeverity: (s: SeverityFilter) => void;
}) {
  const catOpts: { label: string; value: CategoryFilter }[] = [
    { label: "All", value: "all" },
    { label: "Vehicles", value: "vehicle" },
    { label: "Drivers", value: "driver" },
  ];
  const sevOpts: { label: string; value: SeverityFilter }[] = [
    { label: "All", value: "all" },
    { label: "Critical", value: "critical" },
    { label: "High", value: "high" },
    { label: "Medium", value: "medium" },
  ];

  const chipBase = "px-3 py-1.5 rounded-lg text-xs font-semibold border transition-colors cursor-pointer";
  const chipActive = "bg-slate-900 text-white border-slate-900";
  const chipInactive = "bg-white text-slate-600 border-slate-200 hover:border-slate-400";

  return (
    <div className="flex flex-wrap gap-4 items-center">
      <div className="flex gap-1.5">
        {catOpts.map((o) => (
          <button key={o.value} type="button"
            className={`${chipBase} ${category === o.value ? chipActive : chipInactive}`}
            onClick={() => onCategory(o.value)}>
            {o.label}
          </button>
        ))}
      </div>
      <div className="h-4 w-px bg-slate-200" />
      <div className="flex gap-1.5">
        {sevOpts.map((o) => (
          <button key={o.value} type="button"
            className={`${chipBase} ${severity === o.value ? chipActive : chipInactive}`}
            onClick={() => onSeverity(o.value)}>
            {o.label}
          </button>
        ))}
      </div>
    </div>
  );
}

// ── Main page ───────────────────────────────────────────────────────────────

export function FleetHealthPage() {
  const [categoryFilter, setCategoryFilter] = useState<CategoryFilter>("all");
  const [severityFilter, setSeverityFilter] = useState<SeverityFilter>("all");
  const [vehicleDrawerId, setVehicleDrawerId] = useState<number | null>(null);
  const [driverDrawerId, setDriverDrawerId]   = useState<number | null>(null);
  const [mutatingId, setMutatingId]           = useState<number | null>(null);

  const hasPermission = useHasPermission();
  const canManageMaint  = hasPermission("maintenance:manage") || hasPermission("maintenance:create");
  const canManageSafety = hasPermission("safety:manage") || hasPermission("safety:create");

  const qc = useQueryClient();

  const summary = useQuery<AnyRecord>({
    queryKey: ["fleet-health", "summary"],
    queryFn:  fleetHealthApi.summary,
    refetchInterval: 60_000,
  });

  const risks = useQuery<AnyRecord[]>({
    queryKey: ["fleet-health", "risks"],
    queryFn:  () => fleetHealthApi.risks(),
    refetchInterval: 60_000,
  });

  const createWO = useMutation({
    mutationFn: (vehicleId: number) =>
      maintenanceApi.createWorkOrder({ vehicleId, title: "Fleet health review", priority: "High" }),
    onMutate:  (id) => setMutatingId(id),
    onSettled: () => { setMutatingId(null); qc.invalidateQueries({ queryKey: ["fleet-health"] }); },
  });

  const ackFirstDefect = useMutation({
    mutationFn: async (vehicleId: number) => {
      const detail = await fleetHealthApi.vehicleDetail(vehicleId);
      const firstDefect = ((detail?.openDefects as AnyRecord[]) ?? [])[0];
      if (!firstDefect) throw new Error("No open defect found");
      return maintenanceApi.acknowledgeDefect(num(firstDefect.id));
    },
    onMutate:  (id) => setMutatingId(id),
    onSettled: () => { setMutatingId(null); qc.invalidateQueries({ queryKey: ["fleet-health"] }); },
  });

  const assignCoaching = useMutation({
    mutationFn: (driverId: number) =>
      coachingApi.create({
        driverId,
        title:        "Safety performance review",
        coachingType: "Safety Review",
        priority:     "High",
        status:       "Open",
      } as import("@/types").AnyRecord),
    onMutate:  (id) => setMutatingId(id),
    onSettled: () => { setMutatingId(null); qc.invalidateQueries({ queryKey: ["fleet-health"] }); },
  });

  const allRisks = risks.data ?? [];

  const filtered = allRisks.filter((item) => {
    const catOk = categoryFilter === "all" || item.entityType === categoryFilter;
    const sevOk = severityFilter === "all" || item.severity === severityFilter;
    return catOk && sevOk;
  });

  const vehicleRisks = filtered.filter((r) => r.entityType === "vehicle");
  const driverRisks  = filtered.filter((r) => r.entityType === "driver");
  const blockers     = allRisks.filter((r) => Boolean(r.blockingDispatch));
  const topUrgent    = allRisks.filter((r) => r.severity === "critical" || r.severity === "high").slice(0, 6);

  const isMutating = mutatingId !== null;

  const summaryData = summary.data ?? {};
  const insights    = (summaryData.systemInsights as AnyRecord[]) ?? [];

  if (summary.isLoading) {
    return (
      <div className="flex items-center justify-center py-24">
        <div className="flex flex-col items-center gap-3 text-slate-500">
          <RefreshCw className="h-8 w-8 animate-spin" />
          <p className="text-sm font-medium">Loading fleet health data…</p>
        </div>
      </div>
    );
  }

  if (summary.isError) {
    return (
      <div className="flex items-center justify-center py-24 px-8">
        <div className="text-center max-w-sm">
          <XCircle className="h-10 w-10 text-red-400 mx-auto mb-3" />
          <p className="text-base font-semibold text-slate-700 mb-1">Failed to load fleet health data</p>
          <p className="text-sm text-slate-500">Check backend connectivity and try refreshing.</p>
          <button
            type="button"
            onClick={() => summary.refetch()}
            className="mt-4 px-4 py-2 rounded-lg bg-slate-900 text-white text-sm font-semibold hover:bg-slate-700"
          >
            Retry
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Drawers */}
      <VehicleDrawer
        vehicleId={vehicleDrawerId}
        onClose={() => setVehicleDrawerId(null)}
        canManageMaint={canManageMaint}
        onWOCreated={() => qc.invalidateQueries({ queryKey: ["fleet-health"] })}
      />
      <DriverDrawer
        driverId={driverDrawerId}
        onClose={() => setDriverDrawerId(null)}
        canManageSafety={canManageSafety}
        onActionTaken={() => qc.invalidateQueries({ queryKey: ["fleet-health"] })}
      />

      <div className="space-y-6">
        {/* Page header */}
        <PageHeader
          eyebrow="Fleet Health"
          title="Fleet Health & Safety"
          description="Dispatch readiness · Maintenance risk · Driver safety — unified operating view"
          actions={
            <button
              type="button"
              onClick={() => {
                summary.refetch();
                risks.refetch();
              }}
              className="btn-ghost"
            >
              <RefreshCw className={`h-4 w-4 ${(summary.isFetching || risks.isFetching) ? "animate-spin text-blue-500" : ""}`} />
              Refresh
            </button>
          }
        />

        {/* Readiness strip */}
        <ReadinessStrip summary={summaryData} />

        {/* Urgent actions — top critical/high risks */}
        {topUrgent.length > 0 && (
          <div className="bg-red-50 border border-red-200 rounded-xl overflow-hidden">
            <div className="flex items-center gap-2 px-5 py-3 border-b border-red-200">
              <AlertTriangle className="h-4 w-4 text-red-600" />
              <span className="text-xs font-bold uppercase tracking-widest text-red-700">
                Urgent Actions Required
              </span>
              <span className="ml-auto text-xs text-red-500">{topUrgent.length} item{topUrgent.length > 1 ? "s" : ""}</span>
            </div>
            <div className="px-5 py-3 space-y-2">
              {topUrgent.map((item, i) => {
                const isVehicle = item.entityType === "vehicle";
                const id        = num(item.entityId);
                return (
                  <div key={i} className="flex items-center gap-3 bg-white border border-red-200 rounded-lg px-4 py-2.5">
                    <span className={`h-2 w-2 rounded-full shrink-0 ${sevDot(String(item.severity))}`} />
                    {isVehicle
                      ? <Truck className="h-4 w-4 text-slate-400 shrink-0" />
                      : <User className="h-4 w-4 text-slate-400 shrink-0" />}
                    <div className="flex-1 min-w-0">
                      <span className="font-semibold text-sm text-slate-800">{String(item.displayName ?? "")}</span>
                      <span className="text-xs text-slate-500 ml-2">
                        {((item.reasons as string[]) ?? []).slice(0, 1).join("")}
                      </span>
                    </div>
                    <span className={`text-xs font-bold ${sevColor(String(item.severity))} shrink-0`}>
                      {sevLabel(String(item.severity))}
                    </span>
                    <button
                      type="button"
                      onClick={() => isVehicle ? setVehicleDrawerId(id) : setDriverDrawerId(id)}
                      className="flex items-center gap-1 text-xs font-semibold text-slate-600 hover:text-slate-900 shrink-0"
                    >
                      View <ChevronRight className="h-3.5 w-3.5" />
                    </button>
                  </div>
                );
              })}
            </div>
          </div>
        )}

        {/* Filters */}
        <FilterBar
          category={categoryFilter}
          severity={severityFilter}
          onCategory={setCategoryFilter}
          onSeverity={setSeverityFilter}
        />

        {risks.isLoading ? (
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
            {[0, 1, 2, 3].map((i) => (
              <div key={i} className="h-48 bg-white border border-slate-200 rounded-xl animate-pulse" />
            ))}
          </div>
        ) : risks.isError ? (
          <div className="bg-red-50 border border-red-200 rounded-xl p-6 text-center">
            <p className="text-sm text-red-600">Failed to load risk data. Check backend connectivity.</p>
          </div>
        ) : filtered.length === 0 ? (
          <div className="bg-white border border-slate-200 rounded-xl p-12 text-center">
            <CheckCircle className="h-10 w-10 text-emerald-400 mx-auto mb-3" />
            <p className="text-base font-semibold text-slate-700 mb-1">No risk items match the current filter</p>
            <p className="text-sm text-slate-500">
              {allRisks.length === 0
                ? "All vehicles and drivers are currently within acceptable operational parameters."
                : "Try adjusting the severity or category filters."}
            </p>
          </div>
        ) : (
          <div className="grid grid-cols-1 xl:grid-cols-2 gap-6">
            {/* Vehicle risks */}
            {(categoryFilter === "all" || categoryFilter === "vehicle") && vehicleRisks.length > 0 && (
              <div className="space-y-3">
                <div className="flex items-center gap-2">
                  <Truck className="h-4 w-4 text-slate-500" />
                  <h2 className="text-sm font-bold text-slate-700 uppercase tracking-wide">
                    Vehicle Risks ({vehicleRisks.length})
                  </h2>
                </div>
                {vehicleRisks.map((item, i) => (
                  <RiskCard
                    key={i}
                    item={item}
                    onVehicleClick={setVehicleDrawerId}
                    onDriverClick={setDriverDrawerId}
                    onCreateWO={(id) => createWO.mutate(id)}
                    onAckDefect={(id) => ackFirstDefect.mutate(id)}
                    onAssignCoaching={(id) => assignCoaching.mutate(id)}
                    canManageMaint={canManageMaint}
                    canManageSafety={canManageSafety}
                    isMutating={isMutating}
                  />
                ))}
              </div>
            )}

            {/* Driver risks */}
            {(categoryFilter === "all" || categoryFilter === "driver") && driverRisks.length > 0 && (
              <div className="space-y-3">
                <div className="flex items-center gap-2">
                  <User className="h-4 w-4 text-slate-500" />
                  <h2 className="text-sm font-bold text-slate-700 uppercase tracking-wide">
                    Driver Risks ({driverRisks.length})
                  </h2>
                </div>
                {driverRisks.map((item, i) => (
                  <RiskCard
                    key={i}
                    item={item}
                    onVehicleClick={setVehicleDrawerId}
                    onDriverClick={setDriverDrawerId}
                    onCreateWO={(id) => createWO.mutate(id)}
                    onAckDefect={(id) => ackFirstDefect.mutate(id)}
                    onAssignCoaching={(id) => assignCoaching.mutate(id)}
                    canManageMaint={canManageMaint}
                    canManageSafety={canManageSafety}
                    isMutating={isMutating}
                  />
                ))}
              </div>
            )}

            {/* Empty column placeholder when only one column is shown */}
            {categoryFilter === "all" && vehicleRisks.length > 0 && driverRisks.length === 0 && (
              <div className="space-y-3">
                <div className="flex items-center gap-2">
                  <User className="h-4 w-4 text-slate-500" />
                  <h2 className="text-sm font-bold text-slate-700 uppercase tracking-wide">Driver Risks</h2>
                </div>
                <div className="bg-white border border-slate-200 rounded-xl p-8 text-center">
                  <CheckCircle className="h-8 w-8 text-emerald-400 mx-auto mb-2" />
                  <p className="text-sm font-semibold text-slate-600 mb-1">All drivers within normal parameters</p>
                  <p className="text-xs text-slate-400">No drivers currently exceed the risk threshold.</p>
                </div>
              </div>
            )}
            {categoryFilter === "all" && driverRisks.length > 0 && vehicleRisks.length === 0 && (
              <div className="space-y-3">
                <div className="flex items-center gap-2">
                  <Truck className="h-4 w-4 text-slate-500" />
                  <h2 className="text-sm font-bold text-slate-700 uppercase tracking-wide">Vehicle Risks</h2>
                </div>
                <div className="bg-white border border-slate-200 rounded-xl p-8 text-center">
                  <CheckCircle className="h-8 w-8 text-emerald-400 mx-auto mb-2" />
                  <p className="text-sm font-semibold text-slate-600 mb-1">All vehicles within normal parameters</p>
                  <p className="text-xs text-slate-400">No vehicles currently exceed the risk threshold.</p>
                </div>
              </div>
            )}
          </div>
        )}

        {/* Dispatch blockers */}
        {blockers.length > 0 && (
          <div className="bg-white border border-slate-200 rounded-xl shadow-sm overflow-hidden">
            <div className="flex items-center gap-2 px-5 py-3 border-b border-slate-100">
              <XCircle className="h-4 w-4 text-red-500" />
              <span className="text-xs font-bold uppercase tracking-widest text-slate-600">
                Dispatch Blockers
              </span>
              <span className="ml-1 text-[10px] font-bold bg-red-100 text-red-700 px-1.5 py-0.5 rounded-full">
                {blockers.length}
              </span>
            </div>
            <div className="divide-y divide-slate-100">
              {blockers.map((item, i) => (
                <div key={i} className="flex items-center gap-3 px-5 py-3">
                  <Truck className="h-4 w-4 text-slate-400 shrink-0" />
                  <div className="flex-1 min-w-0">
                    <p className="font-semibold text-sm text-slate-800">{String(item.displayName ?? "")}</p>
                    <p className="text-xs text-red-600 mt-0.5">
                      {((item.reasons as string[]) ?? []).slice(0, 2).join(" · ")}
                    </p>
                  </div>
                  <span className="text-[10px] font-bold bg-red-100 text-red-700 border border-red-200 rounded-full px-2 py-0.5 uppercase shrink-0">
                    Blocked
                  </span>
                  <button
                    type="button"
                    onClick={() => setVehicleDrawerId(num(item.entityId))}
                    className="text-xs font-semibold text-slate-600 hover:text-slate-900 shrink-0"
                  >
                    View →
                  </button>
                </div>
              ))}
            </div>
          </div>
        )}

        {/* System fleet insight */}
        <InsightPanel insights={insights} />

        {/* Footer note */}
        <p className="text-xs text-slate-400 text-center pb-4">
          System Fleet Insight — rule-based guidance from live operational data.
          Not AI-generated. Updated every 60 seconds.
        </p>
      </div>
    </div>
  );
}
