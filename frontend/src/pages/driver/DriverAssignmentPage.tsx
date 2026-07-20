import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  AlertTriangle, Camera, CheckCircle, ChevronRight,
  MapPin, Package, X, XCircle,
} from "lucide-react";
import { driverApi } from "@/services/driverApi";
import { Select } from "@/components/ui";
import type { AnyRecord } from "@/types";

const STATUS_LABELS: Record<string, string> = {
  assigned:         "Accept Assignment",
  accepted:         "Start Pre-trip DVIR → Mark En Route",
  en_route_pickup:  "Mark Arrived at Pickup",
  arrived_pickup:   "Mark Loaded",
  loaded:           "Mark In Transit",
  in_transit:       "Mark Arrived at Delivery",
  arrived_delivery: "Submit Delivery Proof",
  exception:        "Resume or Cancel",
};

const EXCEPTION_TYPES = [
  { value: "vehicle_breakdown",   label: "Vehicle Breakdown" },
  { value: "traffic_delay",       label: "Traffic / Delay" },
  { value: "customer_not_ready",  label: "Customer Not Ready" },
  { value: "route_blocked",       label: "Route Blocked" },
  { value: "hos_hold",            label: "HOS / Hours Limit" },
  { value: "maintenance_issue",   label: "Maintenance Issue" },
  { value: "safety_issue",        label: "Safety Concern" },
  { value: "weather_delay",       label: "Weather Delay" },
  { value: "general",             label: "Other" },
];

function ActionButton({
  label, onClick, disabled, variant = "primary",
}: { label: string; onClick: () => void; disabled?: boolean; variant?: "primary" | "danger" | "ghost" }) {
  const cls =
    variant === "primary" ? "bg-teal-600 active:bg-teal-700 text-white" :
    variant === "danger"  ? "bg-red-600 active:bg-red-700 text-white" :
    "bg-slate-100 active:bg-slate-200 text-slate-700";
  return (
    <button
      type="button"
      disabled={disabled}
      onClick={onClick}
      className={`flex w-full items-center justify-between rounded-2xl px-5 py-4 text-sm font-bold shadow-sm disabled:opacity-40 transition ${cls}`}
    >
      <span>{label}</span>
      <ChevronRight className="h-5 w-5 opacity-70" />
    </button>
  );
}

export function DriverAssignmentPage() {
  const qc = useQueryClient();
  const [showException, setShowException] = useState(false);
  const [showProof, setShowProof] = useState<"pickup" | "delivery" | null>(null);
  const [exceptionType, setExceptionType] = useState("general");
  const [exceptionNotes, setExceptionNotes] = useState("");
  const [proofNotes, setProofNotes] = useState("");
  const [proofHash, setProofHash] = useState("");

  const current = useQuery<AnyRecord>({
    queryKey: ["driver", "current"],
    queryFn: driverApi.currentAssignment,
    refetchInterval: 15_000,
  });

  const acceptMut = useMutation({
    mutationFn: (id: number) => driverApi.acceptAssignment(id),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ["driver"] }),
  });

  const statusMut = useMutation({
    mutationFn: ({ id, status }: { id: number; status: string }) =>
      driverApi.updateStatus(id, status),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ["driver"] }),
  });

  const exceptionMut = useMutation({
    mutationFn: ({ id, type, notes }: { id: number; type: string; notes: string }) =>
      driverApi.reportException(id, { exceptionType: type, notes }),
    onSuccess: () => { setShowException(false); void qc.invalidateQueries({ queryKey: ["driver"] }); },
  });

  const proofMut = useMutation({
    mutationFn: ({ id, type, notes, hash }: { id: number; type: "pickup" | "delivery"; notes: string; hash: string }) =>
      driverApi.submitProof(id, { proofType: type, notes: notes || undefined, evidenceHash: hash || undefined }),
    onSuccess: () => { setShowProof(null); void qc.invalidateQueries({ queryKey: ["driver"] }); },
  });

  if (current.isLoading) {
    return (
      <div className="flex flex-col items-center justify-center gap-4 p-12">
        <Package className="h-12 w-12 animate-pulse text-slate-300" />
        <p className="text-sm text-slate-500">Loading assignment…</p>
      </div>
    );
  }

  if (current.isError) {
    return (
      <div className="flex flex-col items-center justify-center gap-4 p-12">
        <XCircle className="h-10 w-10 text-red-400" />
        <p className="text-sm font-medium text-red-700">{(current.error as Error)?.message}</p>
      </div>
    );
  }

  const payload = current.data as AnyRecord ?? {};

  // Handle "no active assignment" response
  if (!payload["assignment"]) {
    return (
      <div className="flex flex-col items-center justify-center gap-4 p-12 text-center">
        <CheckCircle className="h-12 w-12 text-teal-400" />
        <p className="text-base font-semibold text-slate-700">No active assignment</p>
        <p className="text-sm text-slate-400">Contact your dispatcher for your next load.</p>
      </div>
    );
  }

  const assignment = (payload["assignment"] as AnyRecord) ?? {};
  const nextStatuses = (payload["driverNextStatuses"] as string[]) ?? [];
  const id = Number(assignment["id"]);
  const status = String(assignment["assignmentStatus"] ?? "");
  const openExceptions = Number(assignment["openExceptions"] ?? 0);

  return (
    <div className="space-y-4 p-4 pb-8">
      <div className="pt-2">
        <p className="text-xs font-bold uppercase tracking-widest text-slate-400">Active Load</p>
        <h1 className="mt-1 text-xl font-bold text-slate-900">
          {String(assignment["shipmentNumber"] ?? "Load")}
        </h1>
        {assignment["customerName"] != null ? (
          <p className="text-sm text-slate-500">{String(assignment["customerName"])}</p>
        ) : null}
      </div>

      {/* Route */}
      <div className="rounded-2xl border border-slate-200 bg-white p-4 space-y-3">
        <div className="flex items-start gap-3">
          <div className="mt-0.5 flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-slate-200 text-xs font-bold text-slate-600">P</div>
          <div>
            <p className="text-xs text-slate-400 font-medium">Pickup</p>
            <p className="text-sm font-semibold text-slate-800">{String(assignment["pickupAddress"] ?? "—")}</p>
            {assignment["plannedPickupAt"] != null && (
              <p className="text-xs text-slate-400">{new Date(String(assignment["plannedPickupAt"])).toLocaleString()}</p>
            )}
          </div>
        </div>
        <div className="flex items-start gap-3">
          <div className="mt-0.5 flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-teal-600 text-xs font-bold text-white">D</div>
          <div>
            <p className="text-xs text-slate-400 font-medium">Delivery</p>
            <p className="text-sm font-semibold text-slate-800">{String(assignment["dropoffAddress"] ?? "—")}</p>
            {assignment["plannedDeliveryAt"] != null && (
              <p className="text-xs text-slate-400">{new Date(String(assignment["plannedDeliveryAt"])).toLocaleString()}</p>
            )}
          </div>
        </div>
      </div>

      {/* Exception banner */}
      {openExceptions > 0 && (
        <div className="flex items-center gap-2 rounded-2xl border border-amber-300 bg-amber-50 p-4">
          <AlertTriangle className="h-5 w-5 text-amber-600 shrink-0" />
          <p className="text-sm font-semibold text-amber-800">{openExceptions} open exception(s). Await dispatcher instructions.</p>
        </div>
      )}

      {/* Primary action */}
      {status === "assigned" && (
        <ActionButton
          label="Accept Assignment"
          onClick={() => acceptMut.mutate(id)}
          disabled={acceptMut.isPending}
        />
      )}

      {/* Status advance buttons */}
      {nextStatuses.filter(s => s !== "exception" && s !== "cancelled").map((s) => {
        const isProofAction = s === "delivered" || (s === "in_transit" && status === "arrived_pickup");
        const label = STATUS_LABELS[s] ?? `Mark ${s.replace(/_/g, " ")}`;

        if (s === "delivered") {
          return (
            <ActionButton
              key={s}
              label="Submit Delivery Proof"
              onClick={() => setShowProof("delivery")}
            />
          );
        }
        if (s === "in_transit" && status === "loaded") {
          return (
            <ActionButton
              key={s}
              label="Mark In Transit"
              onClick={() => statusMut.mutate({ id, status: s })}
              disabled={statusMut.isPending}
            />
          );
        }

        return (
          <ActionButton
            key={s}
            label={label}
            onClick={() => statusMut.mutate({ id, status: s })}
            disabled={statusMut.isPending}
          />
        );
      })}

      {/* Proof pickup button (when arrived) */}
      {status === "arrived_pickup" && (
        <ActionButton
          variant="ghost"
          label="Record Pickup Proof"
          onClick={() => setShowProof("pickup")}
        />
      )}

      {/* Report exception */}
      {status !== "delivered" && status !== "cancelled" && (
        <ActionButton
          variant="ghost"
          label="Report Problem / Exception"
          onClick={() => setShowException(true)}
        />
      )}

      {/* Error feedback */}
      {(statusMut.isError || acceptMut.isError || exceptionMut.isError || proofMut.isError) && (
        <div className="rounded-2xl border border-red-200 bg-red-50 p-4">
          <p className="text-sm font-medium text-red-700">
            {((statusMut.error || acceptMut.error || exceptionMut.error || proofMut.error) as Error)?.message ?? "Action failed"}
          </p>
        </div>
      )}

      {/* Exception modal */}
      {showException && (
        <div className="fixed inset-0 z-50 flex items-end bg-black/40">
          <div className="w-full rounded-t-3xl bg-white p-6 space-y-4">
            <div className="flex items-center justify-between">
              <p className="text-base font-bold text-slate-900">Report Problem</p>
              <button type="button" onClick={() => setShowException(false)}><X className="h-5 w-5 text-slate-500" /></button>
            </div>
            <div>
              <label className="text-xs font-bold text-slate-500 block mb-1">Type</label>
              <Select
                className="w-full"
                value={exceptionType}
                onChange={(e) => setExceptionType(e.target.value)}
              >
                {EXCEPTION_TYPES.map(t => (
                  <option key={t.value} value={t.value}>{t.label}</option>
                ))}
              </Select>
            </div>
            <div>
              <label className="text-xs font-bold text-slate-500 block mb-1">Notes</label>
              <textarea
                className="w-full rounded-xl border border-slate-200 px-4 py-3 text-sm"
                rows={3}
                placeholder="Describe the issue…"
                value={exceptionNotes}
                onChange={(e) => setExceptionNotes(e.target.value)}
              />
            </div>
            <button
              type="button"
              className="w-full rounded-2xl bg-amber-500 py-4 text-sm font-bold text-white active:bg-amber-600"
              disabled={exceptionMut.isPending}
              onClick={() => exceptionMut.mutate({ id, type: exceptionType, notes: exceptionNotes })}
            >
              {exceptionMut.isPending ? "Submitting…" : "Submit Exception Report"}
            </button>
          </div>
        </div>
      )}

      {/* Proof modal */}
      {showProof && (
        <div className="fixed inset-0 z-50 flex items-end bg-black/40">
          <div className="w-full rounded-t-3xl bg-white p-6 space-y-4">
            <div className="flex items-center justify-between">
              <p className="text-base font-bold text-slate-900">
                {showProof === "delivery" ? "Delivery Confirmation" : "Pickup Confirmation"}
              </p>
              <button type="button" onClick={() => setShowProof(null)}><X className="h-5 w-5 text-slate-500" /></button>
            </div>
            <div>
              <label className="text-xs font-bold text-slate-500 block mb-1">Notes (optional)</label>
              <textarea
                className="w-full rounded-xl border border-slate-200 px-4 py-3 text-sm"
                rows={3}
                placeholder="Recipient name, gate code, or other delivery notes…"
                value={proofNotes}
                onChange={(e) => setProofNotes(e.target.value)}
              />
            </div>
            <div>
              <label className="text-xs font-bold text-slate-500 block mb-1">Evidence Reference (optional)</label>
              <input
                type="text"
                className="w-full rounded-xl border border-slate-200 px-4 py-3 text-sm"
                placeholder="e.g. photo ID or reference number"
                value={proofHash}
                onChange={(e) => setProofHash(e.target.value)}
              />
            </div>
            <p className="text-xs text-slate-400 flex items-center gap-1">
              <Camera className="h-3 w-3" />
              Photo/signature upload requires storage integration — reference hash records intent
            </p>
            <button
              type="button"
              className="w-full rounded-2xl bg-teal-600 py-4 text-sm font-bold text-white active:bg-teal-700"
              disabled={proofMut.isPending}
              onClick={() => proofMut.mutate({ id, type: showProof, notes: proofNotes, hash: proofHash })}
            >
              {proofMut.isPending ? "Submitting…" : `Confirm ${showProof === "delivery" ? "Delivery" : "Pickup"}`}
            </button>
            {showProof === "delivery" && (
              <p className="text-xs text-center text-slate-500">Confirming delivery will close this assignment.</p>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
