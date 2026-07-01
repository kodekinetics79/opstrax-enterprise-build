import { useEffect, useRef, useState } from "react";
import { tokens } from "@/styles/tokens";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useSearchParams } from "react-router-dom";
import { apiClient, unwrap } from "@/services/apiClient";
import { exportCsv, LoadingState, ErrorState, EmptyState, StatusBadge } from "@/components/ui";
import { useHasPermission } from "@/hooks/usePermission";
import { jobsApi } from "@/services/jobsApi";
import type { AnyRecord } from "@/types";

// ── API ───────────────────────────────────────────────────────────────────────

const podApi = {
  list: () => unwrap<AnyRecord[]>(apiClient.get("/api/proof-of-delivery")),
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/proof-of-delivery/summary")),
  capture: (jobId: string | number, payload: AnyRecord) => jobsApi.captureProof(jobId, payload),
};

// ── Signature Canvas ─────────────────────────────────────────────────────────

function SignatureCanvas({ onCapture }: { onCapture: (dataUrl: string) => void }) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const drawing = useRef(false);
  const [hasStrokes, setHasStrokes] = useState(false);

  function getPos(e: React.MouseEvent | React.TouchEvent): { x: number; y: number } {
    const rect = canvasRef.current!.getBoundingClientRect();
    if ("touches" in e) {
      return { x: e.touches[0].clientX - rect.left, y: e.touches[0].clientY - rect.top };
    }
    return { x: (e as React.MouseEvent).clientX - rect.left, y: (e as React.MouseEvent).clientY - rect.top };
  }

  function startDraw(e: React.MouseEvent | React.TouchEvent) {
    e.preventDefault();
    drawing.current = true;
    const ctx = canvasRef.current!.getContext("2d")!;
    const { x, y } = getPos(e);
    ctx.beginPath();
    ctx.moveTo(x, y);
  }

  function draw(e: React.MouseEvent | React.TouchEvent) {
    e.preventDefault();
    if (!drawing.current) return;
    const ctx = canvasRef.current!.getContext("2d")!;
    ctx.lineWidth = 2;
    ctx.lineCap = "round";
    ctx.strokeStyle = tokens.textPrimary;
    const { x, y } = getPos(e);
    ctx.lineTo(x, y);
    ctx.stroke();
    setHasStrokes(true);
  }

  function endDraw(e: React.MouseEvent | React.TouchEvent) {
    e.preventDefault();
    drawing.current = false;
  }

  function clear() {
    const ctx = canvasRef.current!.getContext("2d")!;
    ctx.clearRect(0, 0, canvasRef.current!.width, canvasRef.current!.height);
    setHasStrokes(false);
  }

  function capture() {
    if (!canvasRef.current) return;
    onCapture(canvasRef.current.toDataURL("image/png"));
  }

  return (
    <div className="flex flex-col gap-2">
      <label className="text-xs font-medium text-slate-700">Digital Signature</label>
      <canvas
        ref={canvasRef}
        width={400}
        height={120}
        className="w-full rounded-lg border-2 border-dashed border-slate-300 bg-slate-50 touch-none cursor-crosshair"
        onMouseDown={startDraw}
        onMouseMove={draw}
        onMouseUp={endDraw}
        onMouseLeave={endDraw}
        onTouchStart={startDraw}
        onTouchMove={draw}
        onTouchEnd={endDraw}
      />
      <div className="flex gap-2">
        <button type="button" className="btn-secondary text-xs" onClick={clear}>Clear</button>
        <button
          type="button"
          className="btn-primary text-xs"
          disabled={!hasStrokes}
          onClick={capture}
        >
          Use Signature
        </button>
      </div>
    </div>
  );
}

// ── Capture Modal ─────────────────────────────────────────────────────────────

function CaptureModal({
  job,
  onClose,
  onSubmit,
  submitting,
}: {
  job: AnyRecord;
  onClose: () => void;
  onSubmit: (payload: AnyRecord) => void;
  submitting: boolean;
}) {
  const [receivedBy, setReceivedBy] = useState("");
  const [notes, setNotes] = useState("");
  const [signatureDataUrl, setSignatureDataUrl] = useState<string | null>(null);
  const [captureMode, setCaptureMode] = useState<"signature" | "verbal">("signature");

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    onSubmit({ receivedBy, notes, signatureDataUrl: signatureDataUrl ?? "" });
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/30 backdrop-blur-sm"
      onClick={onClose}
    >
      <form
        className="panel w-full max-w-lg mx-4 flex flex-col gap-4"
        onClick={(e) => e.stopPropagation()}
        onSubmit={handleSubmit}
      >
        <div className="flex items-center justify-between">
          <h3 className="text-base font-semibold text-slate-900">Capture Proof of Delivery</h3>
          <button type="button" className="text-slate-400 hover:text-slate-600" onClick={onClose}>✕</button>
        </div>

        <div className="rounded-lg bg-slate-50 border border-slate-200 px-4 py-3 text-sm">
          <p className="font-medium text-slate-900">{String(job.jobNumber ?? job.shipmentId ?? `JOB-${job.id}`)}</p>
          <p className="text-slate-500 mt-0.5">{String(job.customerName ?? "Customer")} — {String(job.dropoffAddress ?? job.destination ?? "Destination")}</p>
        </div>

        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-slate-700">Received by <span className="text-red-500">*</span></label>
          <input
            required
            className="border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-900 focus:outline-none focus:ring-2 focus:ring-teal-400"
            placeholder="Receiver full name"
            value={receivedBy}
            onChange={(e) => setReceivedBy(e.target.value)}
          />
        </div>

        <div className="flex gap-2">
          {(["signature", "verbal"] as const).map((mode) => (
            <button
              key={mode}
              type="button"
              onClick={() => setCaptureMode(mode)}
              className={`px-3 py-1.5 rounded-lg text-sm font-medium border transition-colors ${
                captureMode === mode
                  ? "bg-teal-50 border-teal-300 text-teal-700"
                  : "bg-slate-50 border-slate-200 text-slate-600 hover:bg-slate-100"
              }`}
            >
              {mode === "signature" ? "Digital Signature" : "Verbal Confirmation"}
            </button>
          ))}
        </div>

        {captureMode === "signature" && (
          <SignatureCanvas
            onCapture={(dataUrl) => setSignatureDataUrl(dataUrl)}
          />
        )}
        {signatureDataUrl && captureMode === "signature" && (
          <div className="rounded-lg border border-teal-200 bg-teal-50 px-3 py-2 text-xs text-teal-700 font-medium">
            Signature captured
          </div>
        )}

        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-slate-700">Notes</label>
          <textarea
            className="border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-900 resize-none focus:outline-none focus:ring-2 focus:ring-teal-400"
            rows={2}
            placeholder="Delivery notes, damage, condition..."
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
          />
        </div>

        <div className="flex justify-end gap-2 pt-1">
          <button type="button" className="btn-secondary text-sm" onClick={onClose}>Cancel</button>
          <button
            type="submit"
            disabled={submitting || !receivedBy}
            className="bg-teal-600 hover:bg-teal-700 disabled:opacity-50 text-white text-sm font-medium px-4 py-2 rounded-lg transition-colors"
          >
            {submitting ? "Capturing…" : "Confirm Delivery"}
          </button>
        </div>
      </form>
    </div>
  );
}

// ── Main Page ─────────────────────────────────────────────────────────────────

export function ProofOfDeliveryPage() {
  const qc = useQueryClient();
  const hasPermission = useHasPermission();
  const canCapture = hasPermission("dispatch:update") || hasPermission("shipments:update");
  const [searchParams, setSearchParams] = useSearchParams();

  const [captureJob, setCaptureJob] = useState<AnyRecord | null>(null);
  const [statusFilter, setStatusFilter] = useState<"All" | "Pending" | "Captured" | "Awaiting Capture">("All");
  const [search, setSearch] = useState("");
  const [toast, setToast] = useState<string | null>(null);

  const { data: records, isLoading, isError, error } = useQuery({
    queryKey: ["pod"],
    queryFn: podApi.list,
    refetchInterval: 20_000,
  });
  const { data: summary } = useQuery({ queryKey: ["pod", "summary"], queryFn: podApi.summary });

  const captureMutation = useMutation({
    mutationFn: ({ jobId, payload }: { jobId: string | number; payload: AnyRecord }) =>
      podApi.capture(jobId, payload),
    onSuccess: async (_data, vars) => {
      qc.invalidateQueries({ queryKey: ["pod"] });
      qc.invalidateQueries({ queryKey: ["pod", "summary"] });
      qc.invalidateQueries({ queryKey: ["jobs"] });
      qc.invalidateQueries({ queryKey: ["jobs", "summary"] });
      qc.invalidateQueries({ queryKey: ["jobs", "detail", vars.jobId] });
      setCaptureJob(null);
      if (searchParams.get("jobId")) {
        const next = new URLSearchParams(searchParams);
        next.delete("jobId");
        setSearchParams(next, { replace: true });
      }
      showToast("Proof of delivery captured");
    },
  });

  function showToast(msg: string) {
    setToast(msg);
    setTimeout(() => setToast(null), 3500);
  }

  const s = (summary ?? {}) as AnyRecord;
  const rows = (records ?? []) as AnyRecord[];
  const focusedJobId = searchParams.get("jobId");
  // Track which jobId has already been auto-opened so closing the modal while
  // the query param is still present doesn't immediately re-open it.
  const autoOpenedFor = useRef<string | null>(null);

  useEffect(() => {
    if (!focusedJobId || !rows.length || !canCapture) return;
    if (autoOpenedFor.current === focusedJobId) return;
    const match = rows.find((row) => String(row.jobId ?? row.id) === focusedJobId);
    if (match && ["Pending", "Awaiting Capture"].includes(String(match.status ?? ""))) {
      autoOpenedFor.current = focusedJobId;
      setCaptureJob(match);
    }
  }, [focusedJobId, rows, canCapture]);

  const filtered = rows.filter((r) => {
    if (statusFilter !== "All" && r.status !== statusFilter) return false;
    if (search) {
      const q = search.toLowerCase();
      return (
        String(r.jobNumber ?? "").toLowerCase().includes(q) ||
        String(r.customerName ?? "").toLowerCase().includes(q) ||
        String(r.driverName ?? "").toLowerCase().includes(q) ||
        String(r.receivedBy ?? "").toLowerCase().includes(q) ||
        String(r.trackingCode ?? "").toLowerCase().includes(q)
      );
    }
    return true;
  });

  if (isLoading) return <LoadingState />;
  if (isError) return <ErrorState message={(error as Error)?.message} />;

  return (
    <div className="flex flex-col gap-6 py-6">
      {toast && (
        <div className="fixed top-4 right-4 z-50 bg-teal-600 text-white text-sm font-medium px-4 py-2.5 rounded-lg shadow-lg">
          {toast}
        </div>
      )}

      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-bold text-slate-900">Proof of Delivery</h1>
          <p className="text-sm text-slate-500 mt-0.5">Delivery evidence surface tied directly to job status, shipment promise, and invoice readiness</p>
        </div>
        <button
          type="button"
          className="btn-secondary text-sm"
          onClick={() => exportCsv("proof-of-delivery", filtered)}
        >
          Export CSV
        </button>
      </div>

      {/* KPI strip */}
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Total Records", val: s.total ?? rows.length },
          { label: "Captured", val: s.captured ?? rows.filter((r) => r.status === "Captured").length, accent: "text-teal-600" },
          { label: "Pending", val: s.pending ?? rows.filter((r) => r.status === "Pending").length, accent: "text-amber-600" },
          { label: "Digital Signatures", val: s.digitalSignatures ?? rows.filter((r) => r.proofType === "Digital Signature").length, accent: "text-violet-600" },
          { label: "Jobs Pending POD", val: s.jobsPendingProof ?? 0, accent: "text-red-600" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-30">
            <span className={`text-2xl font-bold ${accent ?? "text-slate-900"}`}>{String(val)}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>

      <div className="rounded-2xl border border-slate-200 bg-gradient-to-r from-slate-900 to-slate-800 px-4 py-3 text-white">
        <p className="text-[11px] font-bold uppercase tracking-[0.18em] text-teal-300">Lifecycle assurance</p>
        <p className="mt-1 text-sm text-slate-100">
          Every POD row is now anchored to a real shipment job. Pending proof, captured evidence, and job delivery status are evaluated together so dispatch, customer updates, and invoice readiness stay in sync.
        </p>
      </div>

      {/* Filters */}
      <div className="panel flex flex-wrap gap-3 items-center">
        <div className="flex gap-1.5">
          {(["All", "Pending", "Awaiting Capture", "Captured"] as const).map((f) => (
            <button
              key={f}
              type="button"
              onClick={() => setStatusFilter(f)}
              className={`px-3 py-1.5 rounded-lg text-sm font-medium border transition-colors ${
                statusFilter === f
                  ? "bg-teal-50 border-teal-300 text-teal-700"
                  : "bg-slate-50 border-slate-200 text-slate-600 hover:bg-slate-100"
              }`}
            >
              {f}
            </button>
          ))}
        </div>
        <input
          type="search"
          placeholder="Search job, tracking code, customer, driver…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="ml-auto border border-slate-200 rounded-lg px-3 py-1.5 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-teal-400 w-56"
        />
      </div>

      {/* Table */}
      <div className="panel overflow-hidden p-0">
        {filtered.length === 0 ? (
          <EmptyState title="No POD records match your filters" />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Job</th>
                  <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Customer</th>
                  <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Shipment State</th>
                  <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Driver / Vehicle</th>
                  <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Received by</th>
                  <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Type</th>
                  <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Status</th>
                  <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Captured at</th>
                  <th className="text-right px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {filtered.map((row, i) => (
                  <tr key={String(row.id ?? i)} className="hover:bg-slate-50">
                    <td className="px-4 py-3 font-medium text-slate-900">
                      <div>{String(row.jobNumber ?? `JOB-${row.jobId}`)}</div>
                      <div className="mt-0.5 text-[11px] text-slate-400">{String(row.trackingCode ?? "No tracking code")}</div>
                    </td>
                    <td className="px-4 py-3 text-slate-700">{String(row.customerName ?? "—")}</td>
                    <td className="px-4 py-3 text-slate-700">
                      <div className="font-medium text-slate-900">{String(row.jobStatus ?? "—")}</div>
                      <div className="mt-0.5 text-[11px] text-slate-400">SLA {String(row.slaStatus ?? "—")} · Update {String(row.customerUpdateStatus ?? "—")}</div>
                    </td>
                    <td className="px-4 py-3 text-slate-700">
                      {String(row.driverName ?? "—")}
                      {row.vehicleCode ? <span className="text-slate-400"> / {String(row.vehicleCode)}</span> : null}
                    </td>
                    <td className="px-4 py-3 text-slate-700">{String(row.receivedBy || "—")}</td>
                    <td className="px-4 py-3 text-slate-700">{String(row.proofType || "—")}</td>
                    <td className="px-4 py-3">
                      <StatusBadge status={row.status} />
                    </td>
                    <td className="px-4 py-3 text-slate-500 text-xs">
                      {row.capturedAt ? new Date(String(row.capturedAt)).toLocaleString() : "—"}
                    </td>
                    <td className="px-4 py-3 text-right">
                      {canCapture && ["Pending", "Awaiting Capture"].includes(String(row.status ?? "")) && (
                        <button
                          type="button"
                          className="text-xs px-3 py-1 rounded-md bg-teal-50 border border-teal-200 text-teal-700 hover:bg-teal-100 transition-colors"
                          onClick={() => setCaptureJob(row)}
                        >
                          Capture POD
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {captureJob && (
        <CaptureModal
          job={captureJob}
          onClose={() => setCaptureJob(null)}
          submitting={captureMutation.isPending}
          onSubmit={(payload) =>
            captureMutation.mutate({ jobId: (captureJob.jobId ?? captureJob.id) as string | number, payload })
          }
        />
      )}
    </div>
  );
}
