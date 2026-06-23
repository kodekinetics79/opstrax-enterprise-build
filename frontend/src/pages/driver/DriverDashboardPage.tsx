import { useQuery } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import {
  AlertTriangle, BookOpen, CheckCircle, ChevronRight, Clock,
  MapPin, Package, ShieldAlert, Truck, XCircle,
} from "lucide-react";
import { driverApi } from "@/services/driverApi";
import type { AnyRecord } from "@/types";

const NEXT_ACTION: Record<string, { label: string; sublabel: string }> = {
  assigned:         { label: "Accept your assignment",     sublabel: "Dispatcher is waiting for confirmation" },
  accepted:         { label: "Complete pre-trip DVIR",     sublabel: "Required before departure" },
  en_route_pickup:  { label: "Mark arrived at pickup",     sublabel: "Update status when at location" },
  arrived_pickup:   { label: "Confirm load & depart",      sublabel: "Record pickup and begin transit" },
  loaded:           { label: "Depart for delivery",        sublabel: "Mark in transit when ready" },
  in_transit:       { label: "Mark arrived at delivery",   sublabel: "Update on arrival" },
  arrived_delivery: { label: "Submit delivery proof",      sublabel: "Required to close assignment" },
  exception:        { label: "Await dispatcher update",    sublabel: "Exception reported — stand by" },
};

function GuidanceBanner({ guidance }: { guidance: AnyRecord }) {
  const level = String(guidance["level"] ?? "info");
  const cfg: Record<string, string> = {
    critical: "bg-red-50 border-red-300 text-red-800",
    warning:  "bg-amber-50 border-amber-300 text-amber-800",
    action:   "bg-teal-50 border-teal-300 text-teal-800",
    reminder: "bg-blue-50 border-blue-300 text-blue-800",
    ok:       "bg-green-50 border-green-300 text-green-800",
    info:     "bg-slate-50 border-slate-200 text-slate-700",
  };
  const icons: Record<string, typeof AlertTriangle> = {
    critical: XCircle, warning: AlertTriangle, action: CheckCircle, ok: CheckCircle,
  };
  const Icon = icons[level] ?? ShieldAlert;
  return (
    <div className={`flex items-start gap-3 rounded-2xl border p-4 ${cfg[level] ?? cfg["info"]}`}>
      <Icon className="mt-0.5 h-5 w-5 shrink-0" />
      <p className="text-sm font-medium">{String(guidance["message"])}</p>
    </div>
  );
}

function StatusChip({ status }: { status?: unknown }) {
  const s = String(status ?? "").toLowerCase().replace(/_/g, " ");
  const color =
    s.includes("delivered") ? "bg-teal-100 text-teal-800" :
    s.includes("exception") ? "bg-red-100 text-red-800" :
    s.includes("transit")   ? "bg-blue-100 text-blue-800" :
    "bg-slate-100 text-slate-700";
  return (
    <span className={`inline-block rounded-full px-3 py-1 text-xs font-bold uppercase tracking-wide ${color}`}>
      {s || "—"}
    </span>
  );
}

export function DriverDashboardPage() {
  const navigate = useNavigate();
  const { data, isLoading, isError, error } = useQuery<AnyRecord>({
    queryKey: ["driver", "me"],
    queryFn: driverApi.me,
    refetchInterval: 30_000,
  });

  if (isLoading) {
    return (
      <div className="flex flex-col items-center justify-center gap-4 p-12">
        <Truck className="h-12 w-12 animate-pulse text-slate-300" />
        <p className="text-sm text-slate-500">Loading your dashboard…</p>
      </div>
    );
  }

  if (isError) {
    return (
      <div className="flex flex-col items-center justify-center gap-4 p-12">
        <XCircle className="h-10 w-10 text-red-400" />
        <p className="text-sm font-medium text-red-700">
          {(error as Error)?.message ?? "Failed to load driver dashboard"}
        </p>
        <p className="text-xs text-slate-500">Check your connection and try again.</p>
      </div>
    );
  }

  const d           = data ?? {};
  const driver      = (d["driver"] as AnyRecord) ?? {};
  const assignment  = (d["currentAssignment"] as AnyRecord) ?? null;
  const blocking    = (d["vehicleBlocking"] as AnyRecord) ?? {};
  const hos         = (d["hos"] as AnyRecord) ?? {};
  const coaching    = (d["coaching"] as AnyRecord) ?? {};
  const guidance    = (d["guidance"] as AnyRecord[]) ?? [];

  const assignmentStatus = assignment ? String(assignment["assignmentStatus"] ?? "") : "";
  const nextAction = assignmentStatus ? NEXT_ACTION[assignmentStatus] ?? null : null;

  const hosHours = hos["remainingDriveHours"] != null ? Number(hos["remainingDriveHours"]) : null;
  const hosColor =
    hosHours == null       ? "text-slate-400" :
    hosHours < 1           ? "text-red-600" :
    hosHours < 3           ? "text-amber-600" :
    "text-teal-600";

  return (
    <div className="space-y-4 p-4">
      {/* Driver header */}
      <div className="flex items-center gap-3 pt-2">
        <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-full bg-teal-600 text-lg font-bold text-white">
          {String(driver["fullName"] ?? "D").charAt(0).toUpperCase()}
        </div>
        <div>
          <p className="text-lg font-bold text-slate-900">{String(driver["fullName"] ?? "Driver")}</p>
          <div className="flex items-center gap-2">
            <Truck className="h-3.5 w-3.5 text-slate-400" />
            <p className="text-sm text-slate-500">{String(driver["vehicleCode"] ?? "No vehicle assigned")}</p>
            {driver["vehicleOos"] ? (
              <span className="rounded-full bg-red-100 px-2 py-0.5 text-xs font-bold text-red-700">OOS</span>
            ) : null}
          </div>
        </div>
      </div>

      {/* Next Required Action — hero CTA */}
      {nextAction && !blocking["blocked"] && (
        <button
          type="button"
          onClick={() => navigate("/driver/assignments")}
          className="flex w-full items-center justify-between rounded-2xl bg-teal-600 p-5 text-left text-white shadow-lg active:bg-teal-700 transition"
        >
          <div>
            <p className="text-xs font-bold uppercase tracking-widest text-teal-200">Next Required Action</p>
            <p className="mt-1 text-lg font-bold leading-snug">{nextAction.label}</p>
            <p className="mt-0.5 text-sm text-teal-200">{nextAction.sublabel}</p>
          </div>
          <ChevronRight className="h-8 w-8 shrink-0 text-teal-300" />
        </button>
      )}

      {!assignment && (
        <div className="flex flex-col items-center gap-2 rounded-2xl border border-slate-200 bg-white py-8 text-center">
          <Package className="h-10 w-10 text-slate-300" />
          <p className="text-sm font-semibold text-slate-600">No active assignment</p>
          <p className="text-xs text-slate-400">Contact your dispatcher for the next load.</p>
        </div>
      )}

      {/* System guidance (most important first) */}
      {guidance.length > 0 && (
        <div className="space-y-2">
          {guidance.map((g, i) => <GuidanceBanner key={i} guidance={g} />)}
        </div>
      )}

      {/* Vehicle blocking */}
      {(blocking["blocked"] || Number(blocking["criticalDefects"] ?? 0) > 0) && (
        <div className="rounded-2xl border border-red-300 bg-red-50 p-4">
          <div className="flex items-center gap-2 mb-2">
            <XCircle className="h-5 w-5 text-red-600" />
            <p className="font-bold text-red-800">Vehicle Blocked</p>
          </div>
          <p className="text-sm text-red-700">{String(blocking["reason"] ?? "Vehicle is out of service or has critical defects")}</p>
        </div>
      )}

      {/* Current assignment */}
      <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
        <p className="mb-3 text-xs font-bold uppercase tracking-widest text-slate-400">Current Assignment</p>
        {assignment ? (
          <div className="space-y-3">
            <div className="flex items-center justify-between">
              <p className="font-bold text-slate-900 text-lg">{String(assignment["shipmentNumber"] ?? "—")}</p>
              <StatusChip status={assignment["assignmentStatus"]} />
            </div>
            <div className="space-y-1.5 text-sm text-slate-600">
              <div className="flex items-start gap-2">
                <MapPin className="mt-0.5 h-4 w-4 shrink-0 text-slate-400" />
                <span>{String(assignment["pickupAddress"] ?? "—")}</span>
              </div>
              <div className="flex items-start gap-2">
                <MapPin className="mt-0.5 h-4 w-4 shrink-0 text-teal-500" />
                <span>{String(assignment["dropoffAddress"] ?? "—")}</span>
              </div>
            </div>
            {assignment["plannedDeliveryAt"] != null && (
              <p className="text-xs text-slate-500">
                Delivery: {new Date(String(assignment["plannedDeliveryAt"])).toLocaleString()}
              </p>
            )}
          </div>
        ) : (
          <div className="flex flex-col items-center py-4 text-slate-400">
            <Package className="h-8 w-8 mb-2" />
            <p className="text-sm">No active assignment</p>
          </div>
        )}
      </div>

      {/* Quick stats row */}
      <div className="grid grid-cols-2 gap-3">
        {/* HOS */}
        <div className="rounded-2xl border border-slate-200 bg-white p-4">
          <div className="flex items-center gap-2 mb-1">
            <Clock className="h-4 w-4 text-slate-400" />
            <p className="text-xs font-bold uppercase tracking-wider text-slate-400">HOS</p>
          </div>
          {hos["dataAvailable"] ? (
            <>
              <p className={`text-2xl font-bold ${hosColor}`}>
                {hosHours != null ? `${hosHours.toFixed(1)}h` : "—"}
              </p>
              <p className="text-xs text-slate-500">Drive time remaining</p>
            </>
          ) : (
            <>
              <p className="text-sm font-semibold text-slate-400">Unavailable</p>
              <p className="text-xs text-slate-400">ELD not synced</p>
            </>
          )}
        </div>

        {/* Coaching */}
        <div className="rounded-2xl border border-slate-200 bg-white p-4">
          <div className="flex items-center gap-2 mb-1">
            <BookOpen className="h-4 w-4 text-slate-400" />
            <p className="text-xs font-bold uppercase tracking-wider text-slate-400">Coaching</p>
          </div>
          <p className={`text-2xl font-bold ${Number(coaching["pendingCount"] ?? 0) > 0 ? "text-amber-600" : "text-teal-600"}`}>
            {String(coaching["pendingCount"] ?? 0)}
          </p>
          <p className="text-xs text-slate-500">Pending tasks</p>
        </div>
      </div>
    </div>
  );
}
