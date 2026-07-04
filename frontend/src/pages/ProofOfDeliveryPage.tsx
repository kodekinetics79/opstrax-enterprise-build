import { useEffect, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useSearchParams } from "react-router-dom";
import {
  Camera, CheckCircle2, ClipboardCheck, Clock, Download, Eraser,
  FileCheck2, Filter, Package, PenTool, Search, ShieldCheck,
  Signature, Truck, X,
} from "lucide-react";
import { apiClient, unwrap } from "@/services/apiClient";
import { exportCsv, LoadingState, ErrorState, EmptyState, Select, StatusBadge } from "@/components/ui";
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
    ctx.strokeStyle = "#0f172a";
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
    <div className="flex flex-col gap-2.5">
      <label className="text-xs font-bold uppercase tracking-[0.14em] text-slate-500">Digital Signature</label>
      <div className="relative rounded-xl border-2 border-dashed border-slate-200 bg-slate-50/50 overflow-hidden transition-colors hover:border-teal-300">
        <canvas
          ref={canvasRef}
          width={400}
          height={120}
          className="w-full touch-none cursor-crosshair"
          onMouseDown={startDraw}
          onMouseMove={draw}
          onMouseUp={endDraw}
          onMouseLeave={endDraw}
          onTouchStart={startDraw}
          onTouchMove={draw}
          onTouchEnd={endDraw}
        />
        {!hasStrokes && (
          <div className="pointer-events-none absolute inset-0 flex items-center justify-center">
            <p className="text-sm text-slate-300 font-medium">Sign here</p>
          </div>
        )}
      </div>
      <div className="flex gap-2">
        <button type="button" className="inline-flex items-center gap-1.5 rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-xs font-semibold text-slate-600 transition hover:bg-slate-50 cursor-pointer" onClick={clear}>
          <Eraser className="h-3 w-3" /> Clear
        </button>
        <button
          type="button"
          className="inline-flex items-center gap-1.5 rounded-lg bg-teal-600 px-3 py-1.5 text-xs font-semibold text-white transition hover:bg-teal-700 disabled:opacity-50 cursor-pointer"
          disabled={!hasStrokes}
          onClick={capture}
        >
          <CheckCircle2 className="h-3 w-3" /> Use Signature
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
      className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 backdrop-blur-sm anim-fade-in"
      onClick={onClose}
    >
      <form
        className="w-full max-w-lg mx-4 overflow-hidden rounded-2xl bg-white shadow-2xl anim-slide-up"
        onClick={(e) => e.stopPropagation()}
        onSubmit={handleSubmit}
      >
        {/* Modal header */}
        <div className="border-b border-slate-100 bg-gradient-to-r from-teal-50 to-white px-6 py-4">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-3">
              <span className="grid h-9 w-9 place-items-center rounded-xl bg-teal-100 text-teal-700">
                <FileCheck2 className="h-4.5 w-4.5" />
              </span>
              <div>
                <h3 className="text-base font-bold text-slate-900">Capture Proof of Delivery</h3>
                <p className="text-xs text-slate-500">Record delivery evidence</p>
              </div>
            </div>
            <button type="button" className="rounded-lg p-2 text-slate-400 transition hover:bg-slate-100 hover:text-slate-600 cursor-pointer" onClick={onClose}>
              <X className="h-4 w-4" />
            </button>
          </div>
        </div>

        <div className="space-y-4 px-6 py-5">
          {/* Job info */}
          <div className="rounded-xl border border-slate-100 bg-slate-50/50 px-4 py-3">
            <p className="font-bold text-slate-900">{String(job.jobNumber ?? job.shipmentId ?? `JOB-${job.id}`)}</p>
            <p className="mt-0.5 text-sm text-slate-500">{String(job.customerName ?? "Customer")} — {String(job.dropoffAddress ?? job.destination ?? "Destination")}</p>
          </div>

          {/* Receiver */}
          <div className="flex flex-col gap-1.5">
            <label className="text-xs font-bold uppercase tracking-[0.14em] text-slate-500">Received by <span className="text-rose-500">*</span></label>
            <input
              required
              className="w-full rounded-xl border border-slate-200 bg-white px-3.5 py-2.5 text-sm text-slate-900 transition placeholder:text-slate-400 focus:border-teal-400 focus:outline-none focus:ring-2 focus:ring-teal-100"
              placeholder="Receiver full name"
              value={receivedBy}
              onChange={(e) => setReceivedBy(e.target.value)}
            />
          </div>

          {/* Capture mode toggle */}
          <div className="flex gap-2">
            {(["signature", "verbal"] as const).map((mode) => (
              <button
                key={mode}
                type="button"
                onClick={() => setCaptureMode(mode)}
                className={`cursor-pointer flex items-center gap-2 rounded-xl border px-4 py-2.5 text-sm font-semibold transition ${
                  captureMode === mode
                    ? "border-teal-300 bg-teal-50 text-teal-700 shadow-sm ring-1 ring-teal-200/60"
                    : "border-slate-200 bg-white text-slate-500 hover:bg-slate-50"
                }`}
              >
                {mode === "signature" ? <Signature className="h-4 w-4" /> : <ClipboardCheck className="h-4 w-4" />}
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
            <div className="flex items-center gap-2 rounded-xl border border-teal-200 bg-teal-50 px-4 py-2.5 text-xs font-semibold text-teal-700">
              <CheckCircle2 className="h-3.5 w-3.5" /> Signature captured successfully
            </div>
          )}

          {/* Notes */}
          <div className="flex flex-col gap-1.5">
            <label className="text-xs font-bold uppercase tracking-[0.14em] text-slate-500">Notes</label>
            <textarea
              className="w-full rounded-xl border border-slate-200 bg-white px-3.5 py-2.5 text-sm text-slate-900 resize-none transition placeholder:text-slate-400 focus:border-teal-400 focus:outline-none focus:ring-2 focus:ring-teal-100"
              rows={2}
              placeholder="Delivery notes, damage, condition..."
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
            />
          </div>
        </div>

        {/* Modal footer */}
        <div className="flex justify-end gap-3 border-t border-slate-100 bg-slate-50/50 px-6 py-4">
          <button type="button" className="cursor-pointer rounded-xl border border-slate-200 bg-white px-4 py-2.5 text-sm font-semibold text-slate-600 transition hover:bg-slate-50" onClick={onClose}>Cancel</button>
          <button
            type="submit"
            disabled={submitting || !receivedBy}
            className="cursor-pointer inline-flex items-center gap-2 rounded-xl bg-teal-600 px-4 py-2.5 text-sm font-semibold text-white shadow-sm transition hover:bg-teal-700 disabled:opacity-50"
          >
            <CheckCircle2 className="h-4 w-4" />
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
      showToast("Proof of delivery captured successfully");
    },
  });

  function showToast(msg: string) {
    setToast(msg);
    setTimeout(() => setToast(null), 3500);
  }

  const s = (summary ?? {}) as AnyRecord;
  const rows = (records ?? []) as AnyRecord[];
  const focusedJobId = searchParams.get("jobId");
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

  const captured = Number(s.captured ?? rows.filter((r) => r.status === "Captured").length);
  const pending = Number(s.pending ?? rows.filter((r) => r.status === "Pending").length);
  const digitalSigs = Number(s.digitalSignatures ?? rows.filter((r) => r.proofType === "Digital Signature").length);
  const jobsPendingPod = Number(s.jobsPendingProof ?? 0);

  if (isLoading) return <LoadingState />;
  if (isError) return <ErrorState message={(error as Error)?.message} />;

  return (
    <div className="space-y-6 pb-10">
      {/* Toast */}
      {toast && (
        <div className="fixed right-5 top-5 z-[80] anim-slide-right">
          <div className="relative flex items-center gap-3 overflow-hidden rounded-xl border border-emerald-200 bg-white/95 py-3 pl-5 pr-3 shadow-2xl backdrop-blur">
            <span className="absolute left-0 top-0 h-full w-1 bg-emerald-500" />
            <CheckCircle2 className="h-5 w-5 text-emerald-600" />
            <p className="max-w-xs text-sm font-semibold text-slate-800">{toast}</p>
            <button type="button" className="icon-btn ml-1 cursor-pointer" onClick={() => setToast(null)} aria-label="Dismiss"><X className="h-4 w-4" /></button>
          </div>
        </div>
      )}

      {/* Hero Header */}
      <header className="fh-hero relative">
        <span className="fh-hero-bar" />
        <span className="fh-hero-glow-1" />
        <span className="fh-hero-glow-2" />
        <div className="relative px-7 py-6">
          <div className="flex flex-wrap items-start justify-between gap-6">
            <div className="min-w-0 flex-1">
              <div className="flex items-center gap-3 mb-3">
                <span className="inline-flex items-center gap-1.5 rounded-lg bg-white/90 px-3 py-1 text-[10px] font-bold uppercase tracking-[0.2em] text-teal-700 ring-1 ring-teal-200/50 shadow-sm">
                  <FileCheck2 className="h-3 w-3" /> Proof of Delivery
                </span>
                <span className="text-[11px] font-semibold text-slate-500">Delivery evidence & invoice readiness</span>
              </div>
              <h1 className="text-[32px] font-black tracking-tight leading-none cc-gradient-text sm:text-[36px]">
                Proof of Delivery
              </h1>
              <p className="mt-1 text-[13px] font-medium text-slate-400 tracking-wide">
                Delivery evidence surface tied directly to job status, shipment promise, and invoice readiness
              </p>
            </div>
            <div className="flex items-center gap-2">
              <button type="button" className="fh-btn-ghost cursor-pointer" onClick={() => exportCsv("proof-of-delivery", filtered)}>
                <Download className="h-4 w-4" /> Export CSV
              </button>
            </div>
          </div>
        </div>
      </header>

      {/* KPI Cards */}
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-5">
        {[
          { label: "Total Records", val: s.total ?? rows.length, icon: <Package className="h-4 w-4" />, color: "text-slate-700" },
          { label: "Captured", val: captured, icon: <CheckCircle2 className="h-4 w-4" />, color: "text-teal-600", bg: "bg-teal-50" },
          { label: "Pending", val: pending, icon: <Clock className="h-4 w-4" />, color: "text-amber-600", bg: "bg-amber-50" },
          { label: "Digital Signatures", val: digitalSigs, icon: <Signature className="h-4 w-4" />, color: "text-violet-600", bg: "bg-violet-50" },
          { label: "Jobs Awaiting POD", val: jobsPendingPod, icon: <Truck className="h-4 w-4" />, color: "text-rose-600", bg: "bg-rose-50" },
        ].map(({ label, val, icon, color, bg }) => (
          <div key={label} className="panel group flex items-center gap-4 p-4 transition hover:shadow-sm">
            <span className={`grid h-10 w-10 shrink-0 place-items-center rounded-xl ${bg ?? "bg-slate-50"} ${color}`}>
              {icon}
            </span>
            <div>
              <p className={`text-2xl font-bold tabular-nums ${color ?? "text-slate-900"}`}>{String(val)}</p>
              <p className="text-[11px] font-medium text-slate-500">{label}</p>
            </div>
          </div>
        ))}
      </div>

      {/* Lifecycle assurance bar */}
      <div className="relative flex flex-col gap-4 overflow-hidden rounded-2xl border border-slate-700/20 bg-gradient-to-br from-slate-900 via-slate-800 to-slate-900 p-5 text-white shadow-xl sm:flex-row sm:items-center sm:justify-between">
        <div className="absolute -right-8 -top-8 h-32 w-32 rounded-full bg-teal-500/10 blur-2xl" />
        <div className="absolute -bottom-6 left-1/3 h-24 w-24 rounded-full bg-indigo-500/8 blur-2xl" />
        <div className="relative flex items-center gap-4">
          <span className="grid h-11 w-11 shrink-0 place-items-center rounded-xl bg-gradient-to-br from-teal-400/20 to-teal-600/10 ring-1 ring-teal-400/20">
            <ShieldCheck className="h-5 w-5 text-teal-300" />
          </span>
          <div>
            <p className="text-[10px] font-bold uppercase tracking-[0.22em] text-teal-300/80">Lifecycle assurance</p>
            <p className="mt-1 text-sm font-medium leading-relaxed text-slate-600">
              Every POD row is anchored to a real shipment job. Pending proof, captured evidence, and job delivery status are evaluated together so dispatch, customer updates, and invoice readiness stay in sync.
            </p>
          </div>
        </div>
        {pending > 0 && canCapture && (
          <button
            type="button"
            onClick={() => setStatusFilter("Pending")}
            className="cursor-pointer inline-flex items-center gap-2 self-start rounded-xl bg-gradient-to-r from-amber-500 to-amber-600 px-4 py-2.5 text-xs font-bold text-white shadow-lg shadow-amber-500/20 transition hover:from-amber-400 hover:to-amber-500 sm:self-auto"
          >
            {pending} pending capture <Clock className="h-3.5 w-3.5" />
          </button>
        )}
      </div>

      {/* Filters */}
      <div className="panel flex flex-col gap-3 p-3.5 lg:flex-row lg:items-center">
        <div className="flex gap-1.5">
          {(["All", "Pending", "Awaiting Capture", "Captured"] as const).map((f) => (
            <button
              key={f}
              type="button"
              onClick={() => setStatusFilter(f)}
              className={`cursor-pointer rounded-xl border px-3.5 py-2 text-xs font-semibold transition ${
                statusFilter === f
                  ? "bg-teal-50 border-teal-300 text-teal-700 shadow-sm ring-1 ring-teal-200/60"
                  : "border-slate-200 bg-white text-slate-500 hover:bg-slate-50"
              }`}
            >
              {f}
            </button>
          ))}
        </div>
        <div className="relative ml-auto min-w-[220px] lg:max-w-xs">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 shrink-0 -translate-y-1/2 text-slate-400" />
          <input
            type="search"
            placeholder="Search job, customer, driver…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="field h-10 pl-9"
          />
        </div>
        <span className="rounded-full border border-slate-200 bg-slate-50 px-3 py-1.5 text-xs font-semibold text-slate-500">{filtered.length} records</span>
      </div>

      {/* Table */}
      <div className="panel overflow-hidden p-0">
        {filtered.length === 0 ? (
          <EmptyState title="No POD records match your filters" subtitle="Adjust the status filter or search to widen results." />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50/80">
                  <th className="text-left px-4 py-3.5 text-[10px] font-bold uppercase tracking-[0.12em] text-slate-500">Job</th>
                  <th className="text-left px-4 py-3.5 text-[10px] font-bold uppercase tracking-[0.12em] text-slate-500">Customer</th>
                  <th className="text-left px-4 py-3.5 text-[10px] font-bold uppercase tracking-[0.12em] text-slate-500">Shipment State</th>
                  <th className="text-left px-4 py-3.5 text-[10px] font-bold uppercase tracking-[0.12em] text-slate-500">Driver / Vehicle</th>
                  <th className="text-left px-4 py-3.5 text-[10px] font-bold uppercase tracking-[0.12em] text-slate-500">Received by</th>
                  <th className="text-left px-4 py-3.5 text-[10px] font-bold uppercase tracking-[0.12em] text-slate-500">Type</th>
                  <th className="text-left px-4 py-3.5 text-[10px] font-bold uppercase tracking-[0.12em] text-slate-500">Status</th>
                  <th className="text-left px-4 py-3.5 text-[10px] font-bold uppercase tracking-[0.12em] text-slate-500">Captured at</th>
                  <th className="text-right px-4 py-3.5 text-[10px] font-bold uppercase tracking-[0.12em] text-slate-500">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {filtered.map((row, i) => (
                  <tr key={String(row.id ?? i)} className="transition hover:bg-slate-50/80">
                    <td className="px-4 py-3.5">
                      <div className="font-semibold text-slate-900">{String(row.jobNumber ?? `JOB-${row.jobId}`)}</div>
                      <div className="mt-0.5 text-[11px] text-slate-400">{String(row.trackingCode ?? "No tracking code")}</div>
                    </td>
                    <td className="px-4 py-3.5 text-slate-700">{String(row.customerName ?? "—")}</td>
                    <td className="px-4 py-3.5">
                      <div className="font-medium text-slate-900">{String(row.jobStatus ?? "—")}</div>
                      <div className="mt-0.5 text-[11px] text-slate-400">SLA {String(row.slaStatus ?? "—")} · Update {String(row.customerUpdateStatus ?? "—")}</div>
                    </td>
                    <td className="px-4 py-3.5 text-slate-700">
                      {String(row.driverName ?? "—")}
                      {row.vehicleCode ? <span className="text-slate-400"> / {String(row.vehicleCode)}</span> : null}
                    </td>
                    <td className="px-4 py-3.5 text-slate-700">{String(row.receivedBy || "—")}</td>
                    <td className="px-4 py-3.5">
                      <span className="inline-flex items-center gap-1.5 rounded-lg border border-slate-200 bg-slate-50 px-2 py-0.5 text-[11px] font-medium text-slate-600">
                        {row.proofType === "Digital Signature" ? <Signature className="h-3 w-3" /> : <Camera className="h-3 w-3" />}
                        {String(row.proofType || "—")}
                      </span>
                    </td>
                    <td className="px-4 py-3.5">
                      <StatusBadge status={row.status} />
                    </td>
                    <td className="px-4 py-3.5 text-xs text-slate-500">
                      {row.capturedAt ? new Date(String(row.capturedAt)).toLocaleString() : "—"}
                    </td>
                    <td className="px-4 py-3.5 text-right">
                      {canCapture && ["Pending", "Awaiting Capture"].includes(String(row.status ?? "")) && (
                        <button
                          type="button"
                          className="cursor-pointer inline-flex items-center gap-1.5 rounded-lg bg-teal-50 border border-teal-200 px-3 py-1.5 text-xs font-semibold text-teal-700 transition hover:bg-teal-100"
                          onClick={() => setCaptureJob(row)}
                        >
                          <PenTool className="h-3 w-3" /> Capture POD
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
