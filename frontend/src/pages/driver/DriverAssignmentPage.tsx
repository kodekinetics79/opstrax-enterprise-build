import { useState, useRef, type PointerEvent as ReactPointerEvent } from "react";
import { useNavigate } from "react-router-dom";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  AlertTriangle, Camera, CheckCircle, ChevronRight,
  MapPin, MessageSquare, Package, X, XCircle,
} from "lucide-react";
import { driverApi } from "@/services/driverApi";
import { messagesApi } from "@/services/messagesApi";
import { useFlag } from "@/hooks/useFeatureFlags";
import type { AnyRecord } from "@/types";

// Touch/mouse signature capture. Exports the drawn ink as a PNG Blob for upload.
function SignaturePad({ onCapture, disabled }: { onCapture: (blob: Blob) => void; disabled?: boolean }) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const drawing = useRef(false);
  const [hasInk, setHasInk] = useState(false);
  const point = (e: ReactPointerEvent<HTMLCanvasElement>) => {
    const c = canvasRef.current!; const r = c.getBoundingClientRect();
    return { x: (e.clientX - r.left) * (c.width / r.width), y: (e.clientY - r.top) * (c.height / r.height) };
  };
  const down = (e: ReactPointerEvent<HTMLCanvasElement>) => { drawing.current = true; const ctx = canvasRef.current!.getContext("2d")!; const p = point(e); ctx.beginPath(); ctx.moveTo(p.x, p.y); };
  const moveP = (e: ReactPointerEvent<HTMLCanvasElement>) => {
    if (!drawing.current) return;
    const ctx = canvasRef.current!.getContext("2d")!; const p = point(e);
    ctx.strokeStyle = "#0f172a"; ctx.lineWidth = 2; ctx.lineCap = "round";
    ctx.lineTo(p.x, p.y); ctx.stroke(); setHasInk(true);
  };
  const up = () => { drawing.current = false; };
  const clear = () => { const c = canvasRef.current; if (c) c.getContext("2d")!.clearRect(0, 0, c.width, c.height); setHasInk(false); };
  const attach = () => { canvasRef.current?.toBlob((b) => { if (b) { onCapture(b); clear(); } }, "image/png"); };
  return (
    <div>
      <canvas ref={canvasRef} width={480} height={150}
        onPointerDown={down} onPointerMove={moveP} onPointerUp={up} onPointerLeave={up}
        className="w-full h-36 rounded-xl border border-slate-300 bg-white" style={{ touchAction: "none" }} />
      <div className="mt-2 flex gap-2">
        <button type="button" onClick={clear} className="rounded-lg border border-slate-200 px-3 py-1.5 text-xs font-semibold text-slate-600">Clear</button>
        <button type="button" onClick={attach} disabled={!hasInk || disabled} className="rounded-lg bg-slate-900 px-3 py-1.5 text-xs font-semibold text-white disabled:opacity-40">Attach signature</button>
      </div>
    </div>
  );
}

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
  const navigate = useNavigate();
  const [messaging, setMessaging] = useState(false);

  // Open (or create, once) the load-scoped conversation with dispatch and jump into it. Dedupes
  // against an existing open thread for this assignment so repeat taps never spawn duplicate threads.
  const messageDispatch = async (assignmentId: number, shipment: string) => {
    if (messaging) return;
    setMessaging(true);
    try {
      const convs = (await messagesApi.listConversations()) as AnyRecord[];
      const existing = convs.find(
        (c) => Number(c.dispatchAssignmentId) === assignmentId && String(c.status) !== "closed"
      );
      const convId = existing
        ? existing.id
        : ((await messagesApi.createConversation({ dispatchAssignmentId: assignmentId, subject: `Load ${shipment}` })) as AnyRecord)["id"];
      navigate("/driver/messages", { state: { convId } });
    } finally {
      setMessaging(false);
    }
  };
  const [showException, setShowException] = useState(false);
  const [showProof, setShowProof] = useState<"pickup" | "delivery" | null>(null);
  const [exceptionType, setExceptionType] = useState("general");
  const [exceptionNotes, setExceptionNotes] = useState("");
  const [proofNotes, setProofNotes] = useState("");
  const [proofHash, setProofHash] = useState("");
  const [artifacts, setArtifacts] = useState<AnyRecord[]>([]);
  const [uploading, setUploading] = useState(false);
  const [uploadErr, setUploadErr] = useState<string | null>(null);

  // Feature flag: POD media capture. The server gate on the upload endpoint is the real
  // enforcement — this just keeps the UI honest so a driver isn't shown a camera whose
  // upload would 403. fallback=true: never hide an already-shipped capability while flags
  // load. When off, the text "Evidence Reference" field below is the fallback, so the
  // driver can still confirm the delivery.
  const podMediaOn = useFlag("pod_media_capture", true);

  const uploadArtifact = async (assignmentId: number, file: Blob, kind: string, filename: string) => {
    setUploading(true); setUploadErr(null);
    try {
      const res = await driverApi.uploadProofArtifact(assignmentId, file, kind, filename);
      setArtifacts((a) => [...a, res]);
    } catch (e) {
      setUploadErr(e instanceof Error ? e.message : "Upload failed");
    } finally {
      setUploading(false);
    }
  };

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
    mutationFn: ({ id, type, notes, hash, media }: {
      id: number;
      type: "pickup" | "delivery";
      notes: string;
      hash: string;
      media: Array<{ kind: "photo" | "signature"; reference: string; contentType?: string; size?: number }>;
    }) =>
      driverApi.submitProof(id, {
        proofType: type,
        notes: notes || undefined,
        evidenceHash: hash || undefined,
        artifacts: media.length ? media : undefined,
      }),
    onSuccess: () => { setShowProof(null); setArtifacts([]); setProofNotes(""); setProofHash(""); setUploadErr(null); void qc.invalidateQueries({ queryKey: ["driver"] }); },
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
        <button
          type="button"
          onClick={() => void messageDispatch(id, String(assignment["shipmentNumber"] ?? id))}
          disabled={messaging}
          className="mt-3 flex items-center gap-2 rounded-xl border border-teal-200 bg-teal-50 px-3.5 py-2 text-sm font-semibold text-teal-700 active:bg-teal-100 disabled:opacity-50"
        >
          <MessageSquare className="h-4 w-4" />
          {messaging ? "Opening…" : "Message dispatch"}
        </button>
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
              <select
                className="w-full rounded-xl border border-slate-200 px-4 py-3 text-sm"
                value={exceptionType}
                onChange={(e) => setExceptionType(e.target.value)}
              >
                {EXCEPTION_TYPES.map(t => (
                  <option key={t.value} value={t.value}>{t.label}</option>
                ))}
              </select>
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
            {podMediaOn && (
              <div className="rounded-2xl border border-slate-200 p-3 space-y-3">
                <p className="text-xs font-bold text-slate-500 flex items-center gap-1"><Camera className="h-3.5 w-3.5" /> Photo & signature</p>
                <input
                  type="file" accept="image/*" capture="environment" disabled={uploading}
                  onChange={(e) => { const f = e.target.files?.[0]; if (f) void uploadArtifact(id, f, "photo", f.name); e.target.value = ""; }}
                  className="block w-full text-xs text-slate-600 file:mr-3 file:rounded-lg file:border-0 file:bg-teal-600 file:px-3 file:py-2 file:text-xs file:font-bold file:text-white"
                />
                <div>
                  <p className="text-[11px] font-semibold text-slate-500 mb-1">Recipient signature</p>
                  <SignaturePad disabled={uploading} onCapture={(blob) => void uploadArtifact(id, blob, "signature", "signature.png")} />
                </div>
                {artifacts.length > 0 && (
                  <div className="flex flex-wrap gap-2">
                    {artifacts.map((a, i) => (
                      <div key={i} className="relative">
                        {a.url
                          ? <img src={String(a.url)} alt={String(a.kind)} className="h-14 w-14 rounded-lg border border-slate-200 object-cover" />
                          : <span className="inline-flex h-14 w-14 items-center justify-center rounded-lg border border-slate-200 text-[10px] text-slate-500">{String(a.kind)}</span>}
                        <span className="absolute -top-1 -right-1 rounded-full bg-teal-600 px-1 text-[9px] font-bold text-white capitalize">{String(a.kind)}</span>
                      </div>
                    ))}
                  </div>
                )}
                {uploading && <p className="text-xs text-slate-400">Uploading…</p>}
                {uploadErr && <p className="text-xs text-red-600">{uploadErr}</p>}
              </div>
            )}
            <button
              type="button"
              className="w-full rounded-2xl bg-teal-600 py-4 text-sm font-bold text-white active:bg-teal-700 disabled:opacity-50"
              disabled={proofMut.isPending || uploading}
              onClick={() => {
                // Media travels in `artifacts`, the reference/hash stays a hash. Packing the
                // media manifest into evidenceHash (a VARCHAR(128)) is what made every POD
                // with a photo fail to submit at all.
                const media = artifacts
                  .filter((a) => a.reference && (a.kind === "photo" || a.kind === "signature"))
                  .map((a) => ({
                    kind: a.kind as "photo" | "signature",
                    reference: String(a.reference),
                    contentType: a.contentType ? String(a.contentType) : undefined,
                    size: typeof a.size === "number" ? a.size : undefined,
                  }));
                proofMut.mutate({ id, type: showProof, notes: proofNotes, hash: proofHash, media });
              }}
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
