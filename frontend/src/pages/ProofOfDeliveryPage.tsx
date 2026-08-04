import { useEffect, useRef, useState } from "react";
import { tokens } from "@/styles/tokens";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useSearchParams } from "react-router";
import { LoadingState, ErrorState, EmptyState, StatusBadge, KpiCard, ProgressBar } from "@/components/ui";
import { useHasPermission } from "@/hooks/usePermission";
import { shipmentsApi } from "@/services/shipmentsApi";
import type { AnyRecord } from "@/types";

// ── API ───────────────────────────────────────────────────────────────────────

const podApi = {
  list: shipmentsApi.proofOfDelivery,
  summary: shipmentsApi.proofOfDeliverySummary,
  capture: shipmentsApi.submitProofOfDelivery,
  verify: shipmentsApi.verifyProofOfDelivery,
  reject: shipmentsApi.rejectProofOfDelivery,
};

// ── Signature Canvas ─────────────────────────────────────────────────────────

function SignatureCanvas({ onCapture, onClear }: { onCapture: (blob: Blob) => void; onClear: () => void }) {
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
    onClear();
  }

  function capture() {
    if (!canvasRef.current) return;
    canvasRef.current.toBlob((blob) => {
      if (blob) onCapture(blob);
    }, "image/png");
  }

  return (
    <div className="flex flex-col gap-2">
      <label className="text-xs font-medium text-slate-700">Digital Signature</label>
      <canvas
        aria-label="Draw the recipient signature"
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
  error,
}: {
  job: AnyRecord;
  onClose: () => void;
  onSubmit: (payload: AnyRecord) => void;
  submitting: boolean;
  error?: string;
}) {
  const [receivedBy, setReceivedBy] = useState("");
  const [receiverPhone, setReceiverPhone] = useState("");
  const [notes, setNotes] = useState("");
  const [signatureBlob, setSignatureBlob] = useState<Blob | null>(null);
  const [deliveryPhoto, setDeliveryPhoto] = useState<File | null>(null);
  const [captureMode, setCaptureMode] = useState<"signature" | "photo">("signature");
  const [coordinates, setCoordinates] = useState<{ latitude: number; longitude: number } | null>(null);
  const [locationError, setLocationError] = useState<string | null>(null);
  const idempotencyKey = useRef(crypto.randomUUID());

  useEffect(() => {
    const closeOnEscape = (event: KeyboardEvent) => { if (event.key === "Escape") onClose(); };
    window.addEventListener("keydown", closeOnEscape);
    return () => window.removeEventListener("keydown", closeOnEscape);
  }, [onClose]);

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (captureMode === "signature" && !signatureBlob) return;
    if (captureMode === "photo" && !deliveryPhoto) return;
    onSubmit({
      receivedBy,
      receiverPhone,
      notes,
      signatureBlob,
      deliveryPhoto,
      capturedLatitude: coordinates?.latitude,
      capturedLongitude: coordinates?.longitude,
      capturedAt: new Date().toISOString(),
      idempotencyKey: idempotencyKey.current,
    });
  }

  function captureLocation() {
    setLocationError(null);
    if (!navigator.geolocation) {
      setLocationError("Location capture is not supported on this device.");
      return;
    }
    navigator.geolocation.getCurrentPosition(
      ({ coords }) => setCoordinates({ latitude: coords.latitude, longitude: coords.longitude }),
      () => setLocationError("Location was not captured. You can still submit verified media evidence."),
      { enableHighAccuracy: true, timeout: 10_000, maximumAge: 30_000 },
    );
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/30 backdrop-blur-sm"
      onClick={onClose}
      role="dialog"
      aria-modal="true"
      aria-labelledby="pod-capture-title"
    >
      <form
        className="panel w-full max-w-lg mx-4 flex flex-col gap-4"
        onClick={(e) => e.stopPropagation()}
        onSubmit={handleSubmit}
      >
        <div className="flex items-center justify-between">
          <h3 id="pod-capture-title" className="text-base font-semibold text-slate-900">Submit Proof of Delivery</h3>
          <button type="button" aria-label="Close proof submission" className="text-slate-400 hover:text-slate-600" onClick={onClose}>✕</button>
        </div>

        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-slate-700" htmlFor="pod-receiver-phone">Receiver phone <span className="font-normal text-slate-400">(optional)</span></label>
          <input
            id="pod-receiver-phone"
            maxLength={40}
            inputMode="tel"
            className="border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-900 focus:outline-none focus:ring-2 focus:ring-teal-400"
            value={receiverPhone}
            onChange={(e) => setReceiverPhone(e.target.value)}
          />
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
          {(["signature", "photo"] as const).map((mode) => (
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
              {mode === "signature" ? "Digital Signature" : "Delivery Photo"}
            </button>
          ))}
        </div>

        {captureMode === "signature" && (
          <SignatureCanvas
            onCapture={setSignatureBlob}
            onClear={() => setSignatureBlob(null)}
          />
        )}
        {signatureBlob && captureMode === "signature" && (
          <div className="rounded-lg border border-teal-200 bg-teal-50 px-3 py-2 text-xs text-teal-700 font-medium">
            Signature captured and ready for private upload
          </div>
        )}
        {captureMode === "photo" && (
          <div className="flex flex-col gap-1">
            <label className="text-xs font-medium text-slate-700" htmlFor="pod-delivery-photo">Delivery photo <span className="text-red-500">*</span></label>
            <input
              id="pod-delivery-photo"
              required
              type="file"
              accept="image/jpeg,image/png,image/webp,image/heic,image/heif"
              className="block w-full text-sm text-slate-600 file:mr-3 file:rounded-md file:border-0 file:bg-teal-50 file:px-3 file:py-2 file:text-xs file:font-semibold file:text-teal-700"
              onChange={(event) => setDeliveryPhoto(event.target.files?.[0] ?? null)}
            />
          </div>
        )}

        <div className="rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 text-xs text-slate-600">
          <div className="flex items-center justify-between gap-3">
            <span>{coordinates ? `${coordinates.latitude.toFixed(5)}, ${coordinates.longitude.toFixed(5)}` : "GPS not captured"}</span>
            <button type="button" className="btn-secondary text-xs" onClick={captureLocation}>Capture GPS</button>
          </div>
          {locationError && <p className="mt-1 text-amber-700" role="status">{locationError}</p>}
        </div>

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

        {error && <div role="alert" className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">{error}</div>}

        <div className="flex justify-end gap-2 pt-1">
          <button type="button" className="btn-secondary text-sm" onClick={onClose}>Cancel</button>
          <button
            type="submit"
            disabled={submitting || !receivedBy.trim() || (captureMode === "signature" ? !signatureBlob : !deliveryPhoto)}
            className="bg-teal-600 hover:bg-teal-700 disabled:opacity-50 text-white text-sm font-medium px-4 py-2 rounded-lg transition-colors"
          >
            {submitting ? "Uploading and submitting…" : "Submit for verification"}
          </button>
        </div>
      </form>
    </div>
  );
}

function RejectModal({
  proof,
  submitting,
  error,
  onClose,
  onReject,
}: {
  proof: AnyRecord;
  submitting: boolean;
  error?: string;
  onClose: () => void;
  onReject: (reason: string) => void;
}) {
  const [reason, setReason] = useState("");
  useEffect(() => {
    const closeOnEscape = (event: KeyboardEvent) => { if (event.key === "Escape") onClose(); };
    window.addEventListener("keydown", closeOnEscape);
    return () => window.removeEventListener("keydown", closeOnEscape);
  }, [onClose]);
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/30 px-4 backdrop-blur-sm" role="dialog" aria-modal="true" aria-labelledby="pod-reject-title">
      <form
        className="panel w-full max-w-md space-y-4"
        onSubmit={(event) => {
          event.preventDefault();
          if (reason.trim().length >= 3) onReject(reason.trim());
        }}
      >
        <div className="flex items-center justify-between gap-3">
          <h2 id="pod-reject-title" className="text-base font-semibold text-slate-900">Reject submitted proof</h2>
          <button type="button" aria-label="Close rejection dialog" className="text-slate-400 hover:text-slate-600" onClick={onClose}>✕</button>
        </div>
        <p className="text-sm text-slate-600">{String(proof.jobNumber ?? `Job ${proof.jobId}`)} will remain undelivered and blocked from billing until corrected evidence is submitted.</p>
        <div className="space-y-1">
          <label htmlFor="pod-rejection-reason" className="text-xs font-medium text-slate-700">Correction required <span className="text-red-500">*</span></label>
          <textarea
            id="pod-rejection-reason"
            required
            minLength={3}
            maxLength={1000}
            autoFocus
            rows={4}
            className="w-full resize-none rounded-lg border border-slate-200 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-red-300"
            value={reason}
            onChange={(event) => setReason(event.target.value)}
          />
        </div>
        {error && <div role="alert" className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">{error}</div>}
        <div className="flex justify-end gap-2">
          <button type="button" className="btn-secondary text-sm" onClick={onClose}>Cancel</button>
          <button type="submit" disabled={submitting || reason.trim().length < 3} className="rounded-lg bg-red-600 px-4 py-2 text-sm font-semibold text-white hover:bg-red-700 disabled:opacity-50">
            {submitting ? "Rejecting…" : "Reject proof"}
          </button>
        </div>
      </form>
    </div>
  );
}

function EvidenceDetailModal({
  data,
  loading,
  error,
  onClose,
}: {
  data?: AnyRecord;
  loading: boolean;
  error?: string;
  onClose: () => void;
}) {
  const proof = (data?.proof ?? {}) as AnyRecord;
  const proofPackage = (data?.proofPackage ?? {}) as AnyRecord;
  const artifacts = Array.isArray(data?.artifacts) ? data.artifacts as AnyRecord[] : [];
  useEffect(() => {
    const closeOnEscape = (event: KeyboardEvent) => { if (event.key === "Escape") onClose(); };
    window.addEventListener("keydown", closeOnEscape);
    return () => window.removeEventListener("keydown", closeOnEscape);
  }, [onClose]);
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/30 px-4 backdrop-blur-sm" role="dialog" aria-modal="true" aria-labelledby="pod-detail-title">
      <div className="panel max-h-[85vh] w-full max-w-2xl overflow-y-auto space-y-4">
        <div className="flex items-center justify-between gap-3">
          <div>
            <h2 id="pod-detail-title" className="text-base font-semibold text-slate-900">POD evidence record</h2>
            <p className="text-xs text-slate-500">{String(proof.jobNumber ?? "Loading…")}</p>
          </div>
          <button type="button" aria-label="Close evidence details" className="text-slate-400 hover:text-slate-600" onClick={onClose}>✕</button>
        </div>
        {loading ? <LoadingState /> : error ? <ErrorState message={error} /> : (
          <>
            <dl className="grid grid-cols-2 gap-3 rounded-lg border border-slate-200 bg-slate-50 p-3 text-sm md:grid-cols-4">
              <div><dt className="text-xs text-slate-500">Recipient</dt><dd className="font-medium text-slate-900">{String(proof.receivedBy ?? proof.receiverName ?? "—")}</dd></div>
              <div><dt className="text-xs text-slate-500">Proof status</dt><dd><StatusBadge status={proof.status} /></dd></div>
              <div><dt className="text-xs text-slate-500">Validation</dt><dd className="font-medium text-slate-900">{String(proofPackage.validationStatus ?? "pending")}</dd></div>
              <div><dt className="text-xs text-slate-500">Billing</dt><dd className="font-medium text-slate-900">{String(proofPackage.billingStatus ?? "not ready")}</dd></div>
              <div className="col-span-2"><dt className="text-xs text-slate-500">GPS</dt><dd className="font-medium text-slate-900">{proofPackage.geoLatitude == null ? "Not captured" : `${String(proofPackage.geoLatitude)}, ${String(proofPackage.geoLongitude)}`}</dd></div>
              <div className="col-span-2"><dt className="text-xs text-slate-500">Captured</dt><dd className="font-medium text-slate-900">{proofPackage.capturedAt ? new Date(String(proofPackage.capturedAt)).toLocaleString() : "—"}</dd></div>
            </dl>
            <div>
              <h3 className="text-sm font-semibold text-slate-900">Durable evidence</h3>
              {artifacts.length === 0 ? <p className="mt-2 text-sm text-slate-500">No durable evidence is attached.</p> : (
                <ul className="mt-2 divide-y divide-slate-100 rounded-lg border border-slate-200">
                  {artifacts.map((artifact) => (
                    <li key={String(artifact.id)} className="flex items-center justify-between gap-3 px-3 py-2 text-sm">
                      <span className="font-medium text-slate-800">{String(artifact.title ?? artifact.artifactType)}</span>
                      <span className="text-xs text-slate-500">{String(artifact.artifactType)} · {String(artifact.status)}</span>
                    </li>
                  ))}
                </ul>
              )}
            </div>
            {proofPackage.validationSummary && <p className="rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-600">{String(proofPackage.validationSummary)}</p>}
          </>
        )}
      </div>
    </div>
  );
}

// ── Main Page ─────────────────────────────────────────────────────────────────

export function ProofOfDeliveryPage() {
  const qc = useQueryClient();
  const hasPermission = useHasPermission();
  const canCapture = hasPermission("fleet.pod.manage") || hasPermission("dispatch:update") || hasPermission("shipments:update");
  const canReview = hasPermission("operations.proof.validate") || hasPermission("dispatch:override") || hasPermission("fleet:manage");
  const canExport = hasPermission("fleet.pod.export") || hasPermission("shipments:export") || hasPermission("fleet:manage");
  const [searchParams, setSearchParams] = useSearchParams();

  const [captureJob, setCaptureJob] = useState<AnyRecord | null>(null);
  const [rejectProof, setRejectProof] = useState<AnyRecord | null>(null);
  const [selectedProofId, setSelectedProofId] = useState<string | number | null>(null);
  const [statusFilter, setStatusFilter] = useState<"All" | "Pending" | "Submitted" | "Captured" | "Rejected" | "Awaiting Capture">("All");
  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");
  const [toast, setToast] = useState<string | null>(null);
  const [page, setPage] = useState(0);
  const [exporting, setExporting] = useState(false);
  const [exportError, setExportError] = useState<string | null>(null);
  const pageSize = 25;
  const focusedJobId = searchParams.get("jobId");

  const { data: records, isLoading, isError, error } = useQuery({
    queryKey: ["pod", "list", statusFilter, search, page, focusedJobId],
    queryFn: () => podApi.list({ status: statusFilter, search, jobId: focusedJobId ?? undefined, limit: pageSize, offset: page * pageSize }),
    refetchInterval: 20_000,
  });
  const { data: summary } = useQuery({ queryKey: ["pod", "summary"], queryFn: podApi.summary });
  const detailQuery = useQuery({
    queryKey: ["pod", "detail", selectedProofId],
    queryFn: () => shipmentsApi.proofOfDeliveryDetail(selectedProofId!),
    enabled: selectedProofId !== null,
  });

  const captureMutation = useMutation({
    mutationFn: async ({ jobId, payload }: { jobId: string | number; payload: AnyRecord }) => {
      const signatureBlob = payload.signatureBlob as Blob | null;
      const deliveryPhoto = payload.deliveryPhoto as File | null;
      const signature = signatureBlob
        ? await shipmentsApi.uploadProofEvidence(jobId, signatureBlob, "signature", `pod-signature-${jobId}.png`)
        : null;
      const photo = deliveryPhoto
        ? await shipmentsApi.uploadProofEvidence(jobId, deliveryPhoto, "photo", deliveryPhoto.name)
        : null;
      return podApi.capture(jobId, {
        receivedBy: payload.receivedBy,
        receiverPhone: payload.receiverPhone,
        notes: payload.notes,
        signatureFileId: signature?.fileId,
        photoFileId: photo?.fileId,
        capturedLatitude: payload.capturedLatitude,
        capturedLongitude: payload.capturedLongitude,
        capturedAt: payload.capturedAt,
        idempotencyKey: payload.idempotencyKey,
      });
    },
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
      showToast("Proof submitted for independent verification");
    },
  });

  const reviewMutation = useMutation({
    mutationFn: ({ proofId, decision, reason }: { proofId: string | number; decision: "verify" | "reject"; reason?: string }) =>
      decision === "verify" ? podApi.verify(proofId) : podApi.reject(proofId, reason ?? "Evidence requires correction"),
    onSuccess: async (_data, vars) => {
      await Promise.all([
        qc.invalidateQueries({ queryKey: ["pod"] }),
        qc.invalidateQueries({ queryKey: ["pod", "summary"] }),
        qc.invalidateQueries({ queryKey: ["jobs"] }),
      ]);
      showToast(vars.decision === "verify" ? "Proof verified; delivery is billing-ready" : "Proof rejected for correction");
      setRejectProof(null);
    },
  });

  function showToast(msg: string) {
    setToast(msg);
    setTimeout(() => setToast(null), 3500);
  }

  useEffect(() => {
    const timeout = window.setTimeout(() => { setSearch(searchInput.trim()); setPage(0); }, 300);
    return () => window.clearTimeout(timeout);
  }, [searchInput]);

  async function handleExport() {
    if (!canExport) return;
    setExportError(null);
    setExporting(true);
    try {
      await shipmentsApi.exportProofOfDelivery({ status: statusFilter, search });
      showToast("Filtered proof-of-delivery export started");
    } catch (exportFailure) {
      setExportError((exportFailure as Error)?.message ?? "Unable to export proof of delivery records.");
    } finally {
      setExporting(false);
    }
  }

  const s = (summary ?? {}) as AnyRecord;
  const rows = (records?.items ?? []) as AnyRecord[];
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

  // ── Derived KPIs (computed from live data already in scope) ──────────────────
  const totalRecords = Number(s.total ?? records?.total ?? rows.length);
  const capturedCount = Number(s.captured ?? rows.filter((r) => r.status === "Captured").length);
  const pendingCount = Number(s.pending ?? rows.filter((r) => r.status === "Pending").length);
  const submittedCount = Number(s.submitted ?? rows.filter((r) => r.status === "Submitted").length);
  const digitalSignatures = Number(
    s.digitalSignatures ?? rows.filter((r) => r.proofType === "Digital Signature").length,
  );
  const jobsPendingProof = Number(s.jobsPendingProof ?? 0);
  const captureRate = totalRecords > 0 ? Math.round((capturedCount / totalRecords) * 100) : 0;
  const signatureRate = capturedCount > 0 ? Math.round((digitalSignatures / capturedCount) * 100) : 0;

  // Proof-type breakdown across all captured records (live data).
  const proofTypeBreakdown = Object.entries(
    rows.reduce<Record<string, number>>((acc, r) => {
      if (String(r.status ?? "") !== "Captured") return acc;
      const key = String(r.proofType || "Unspecified");
      acc[key] = (acc[key] ?? 0) + 1;
      return acc;
    }, {}),
  ).sort((a, b) => b[1] - a[1]);

  // Awaiting-capture queue (records still needing a POD).
  const awaitingQueue = rows.filter((r) =>
    ["Pending", "Awaiting Capture"].includes(String(r.status ?? "")),
  );

  // Recently captured, newest first (live capturedAt timestamps).
  const recentCaptures = rows
    .filter((r) => r.capturedAt)
    .sort((a, b) => new Date(String(b.capturedAt)).getTime() - new Date(String(a.capturedAt)).getTime())
    .slice(0, 8);

  const filtered = rows;
  const pagedRows = rows;
  const filteredTotal = records?.total ?? rows.length;

  useEffect(() => {
    setPage(0);
  }, [statusFilter]);

  if (isLoading) return <LoadingState />;
  if (isError) return <ErrorState message={(error as Error)?.message} />;

  return (
    <div className="fleet-console flex flex-col gap-3">
      {toast && (
        <div role="status" aria-live="polite" className="fixed top-4 right-4 z-50 bg-teal-600 text-white text-sm font-medium px-4 py-2.5 rounded-lg shadow-lg">
          {toast}
        </div>
      )}
      {reviewMutation.isError && (
        <div role="alert" className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
          {(reviewMutation.error as Error)?.message ?? "The review action failed."}
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
          disabled={!canExport || exporting}
          title={!canExport ? "fleet.pod.export, shipments:export, or fleet:manage is required" : undefined}
          onClick={handleExport}
        >
          {exporting ? "Exporting…" : "Export CSV"}
        </button>
      </div>
      {!canExport && <div role="status" className="rounded-lg border border-blue-200 bg-blue-50 px-3 py-2 text-sm text-blue-800">Read-only POD view. A direct export permission is required to download evidence metadata.</div>}
      {exportError && <div role="alert" className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">{exportError}</div>}

      {/* KPI grid */}
      <div className="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-6">
        <KpiCard
          label="Total Records"
          value={String(totalRecords)}
          trend={`${filteredTotal} matching`}
        />
        <KpiCard
          label="Captured"
          value={String(capturedCount)}
          status="Complete"
          delta={totalRecords ? `${captureRate}% capture rate` : undefined}
        />
        <KpiCard
          label="Pending"
          value={String(pendingCount)}
          status={pendingCount > 0 ? "Pending" : undefined}
        />
        <KpiCard
          label="Awaiting Review"
          value={String(submittedCount)}
          status={submittedCount > 0 ? "Pending" : undefined}
        />
        <KpiCard
          label="Digital Signatures"
          value={String(digitalSignatures)}
          trend={capturedCount ? `${signatureRate}% of captures` : undefined}
        />
        <KpiCard
          label="Jobs Pending POD"
          value={String(jobsPendingProof)}
          status={jobsPendingProof > 0 ? "Missing" : undefined}
        />
      </div>


      {/* Filters */}
      <div className="panel flex flex-wrap gap-3 items-center">
        <div className="flex gap-1.5">
          {(["All", "Pending", "Awaiting Capture", "Submitted", "Captured", "Rejected"] as const).map((f) => (
            <button
              key={f}
              type="button"
              aria-pressed={statusFilter === f}
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
          value={searchInput}
          maxLength={120}
          aria-label="Search proof of delivery records"
          onChange={(e) => setSearchInput(e.target.value)}
          className="ml-auto border border-slate-200 rounded-lg px-3 py-1.5 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-teal-400 w-56"
        />
      </div>

      {/* Table + supporting rail */}
      <div className="grid grid-cols-1 gap-3 xl:grid-cols-[minmax(0,1fr)_340px]">
      <div className="clay-card overflow-hidden p-0">
        {rows.length === 0 ? (
          <EmptyState title="No POD records match your filters" />
        ) : (
          <>
          <div className="hidden overflow-x-auto md:block">
            <table className="w-full text-sm">
              <caption className="sr-only">Proof of delivery records, evidence status, billing readiness, and review actions</caption>
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Job</th>
                  <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Customer</th>
                  <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Shipment State</th>
                  <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Driver / Vehicle</th>
                  <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Received by</th>
                  <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Type</th>
                  <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Status</th>
                  <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Evidence / Billing</th>
                  <th className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Captured at</th>
                  <th className="text-right px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {pagedRows.map((row, i) => (
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
                    <td className="px-4 py-3 text-xs text-slate-600">
                      <div>{Number(row.evidenceCount ?? 0)} file{Number(row.evidenceCount ?? 0) === 1 ? "" : "s"}{row.hasSignature ? " · signature" : ""}{row.hasPhoto ? " · photo" : ""}</div>
                      <div className="mt-0.5 text-slate-400">Billing {String(row.billingStatus ?? "not ready")}</div>
                    </td>
                    <td className="px-4 py-3 text-slate-500 text-xs">
                      {row.capturedAt ? new Date(String(row.capturedAt)).toLocaleString() : "—"}
                    </td>
                    <td className="px-4 py-3 text-right">
                      <div className="flex justify-end gap-1.5">
                      {Boolean(row.proofId) && (
                        <button
                          type="button"
                          className="rounded-md border border-slate-200 bg-white px-2.5 py-1 text-xs font-semibold text-slate-600 hover:bg-slate-50"
                          onClick={() => setSelectedProofId((row.proofId ?? row.id) as string | number)}
                        >
                          View
                        </button>
                      )}
                      {canCapture && ["Pending", "Awaiting Capture", "Rejected"].includes(String(row.status ?? "")) && (
                        <button
                          type="button"
                          className="text-xs px-3 py-1 rounded-md bg-teal-50 border border-teal-200 text-teal-700 hover:bg-teal-100 transition-colors"
                          onClick={() => setCaptureJob(row)}
                        >
                          Capture POD
                        </button>
                      )}
                      {canReview && String(row.status ?? "") === "Submitted" && (
                        <>
                          <button
                            type="button"
                            className="rounded-md border border-emerald-200 bg-emerald-50 px-2.5 py-1 text-xs font-semibold text-emerald-700 hover:bg-emerald-100 disabled:opacity-50"
                            disabled={reviewMutation.isPending}
                            onClick={() => reviewMutation.mutate({ proofId: (row.proofId ?? row.id) as string | number, decision: "verify" })}
                          >
                            Verify
                          </button>
                          <button
                            type="button"
                            className="rounded-md border border-red-200 bg-red-50 px-2.5 py-1 text-xs font-semibold text-red-700 hover:bg-red-100 disabled:opacity-50"
                            disabled={reviewMutation.isPending}
                            onClick={() => setRejectProof(row)}
                          >
                            Reject
                          </button>
                        </>
                      )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className="space-y-3 p-3 md:hidden">
            {pagedRows.map((row, index) => (
              <article key={String(row.id ?? row.jobId ?? index)} className="rounded-xl border border-slate-200 bg-white p-4">
                <div className="flex items-start justify-between gap-3">
                  <div><h2 className="font-semibold text-slate-900">{String(row.jobNumber ?? `JOB-${row.jobId}`)}</h2><p className="text-xs text-slate-500">{String(row.customerName ?? "—")}</p></div>
                  <StatusBadge status={row.status} />
                </div>
                <dl className="mt-3 grid grid-cols-2 gap-2 text-xs">
                  <div><dt className="text-slate-400">Driver / vehicle</dt><dd className="font-medium text-slate-700">{String(row.driverName ?? "—")} / {String(row.vehicleCode ?? "—")}</dd></div>
                  <div><dt className="text-slate-400">Evidence</dt><dd className="font-medium text-slate-700">{Number(row.evidenceCount ?? 0)} file(s) · Billing {String(row.billingStatus ?? "not ready")}</dd></div>
                  <div className="col-span-2"><dt className="text-slate-400">Received by</dt><dd className="font-medium text-slate-700">{String(row.receivedBy || "—")}</dd></div>
                </dl>
                <div className="mt-4 flex flex-wrap gap-2">
                  {Boolean(row.proofId) && <button type="button" className="btn-secondary text-xs" onClick={() => setSelectedProofId((row.proofId ?? row.id) as string | number)}>View evidence</button>}
                  {canCapture && ["Pending", "Awaiting Capture", "Rejected"].includes(String(row.status ?? "")) && <button type="button" className="btn-primary text-xs" onClick={() => setCaptureJob(row)}>Capture POD</button>}
                  {canReview && String(row.status ?? "") === "Submitted" && <button type="button" className="rounded-md bg-emerald-600 px-3 py-1.5 text-xs font-semibold text-white disabled:opacity-50" disabled={reviewMutation.isPending} onClick={() => reviewMutation.mutate({ proofId: (row.proofId ?? row.id) as string | number, decision: "verify" })}>Verify</button>}
                  {canReview && String(row.status ?? "") === "Submitted" && <button type="button" className="rounded-md bg-red-600 px-3 py-1.5 text-xs font-semibold text-white disabled:opacity-50" disabled={reviewMutation.isPending} onClick={() => setRejectProof(row)}>Reject</button>}
                </div>
              </article>
            ))}
          </div>
          </>
        )}
      </div>

      {filteredTotal > pageSize && (
        <nav aria-label="Proof of delivery pages" className="flex items-center justify-end gap-2">
          <span className="text-xs text-slate-500" aria-live="polite">{page * pageSize + 1}–{Math.min((page + 1) * pageSize, filteredTotal)} of {filteredTotal}</span>
          <button type="button" className="btn-secondary text-xs" disabled={page === 0} onClick={() => setPage((value) => Math.max(0, value - 1))}>Previous</button>
          <button type="button" className="btn-secondary text-xs" disabled={(page + 1) * pageSize >= filteredTotal} onClick={() => setPage((value) => value + 1)}>Next</button>
        </nav>
      )}

      {/* Supporting rail — recent captures, awaiting queue, proof-type mix */}
      <div className="flex flex-col gap-3">
        {/* Awaiting capture queue */}
        <div className="clay-card p-4">
          <div className="mb-3 flex items-center justify-between">
            <h2 className="section-title">Awaiting Capture</h2>
            <span className="badge badge-warning tabular-nums">{awaitingQueue.length}</span>
          </div>
          {awaitingQueue.length === 0 ? (
            <p className="text-xs text-slate-500">Every delivery has proof on file. Nothing pending capture.</p>
          ) : (
            <ul className="flex flex-col gap-1.5">
              {awaitingQueue.slice(0, 6).map((r, i) => (
                <li
                  key={String(r.id ?? i)}
                  className="deck-inset flex items-center justify-between gap-3 rounded-xl px-3 py-2"
                >
                  <div className="min-w-0">
                    <div className="truncate text-sm font-semibold text-slate-900">
                      {String(r.jobNumber ?? `JOB-${r.jobId}`)}
                    </div>
                    <div className="truncate text-[11px] text-slate-500">
                      {String(r.customerName ?? "—")}
                    </div>
                  </div>
                  {canCapture ? (
                    <button
                      type="button"
                      className="shrink-0 rounded-md border border-teal-200 bg-teal-50 px-2.5 py-1 text-[11px] font-semibold text-teal-700 transition-colors hover:bg-teal-100"
                      onClick={() => setCaptureJob(r)}
                    >
                      Capture
                    </button>
                  ) : (
                    <StatusBadge status={r.status} />
                  )}
                </li>
              ))}
            </ul>
          )}
        </div>

        {/* Proof-type mix */}
        <div className="clay-card p-4">
          <h2 className="section-title mb-3">Proof-Type Mix</h2>
          {proofTypeBreakdown.length === 0 ? (
            <p className="text-xs text-slate-500">No captured proofs yet.</p>
          ) : (
            <div className="flex flex-col gap-3">
              {proofTypeBreakdown.map(([type, count]) => (
                <ProgressBar
                  key={type}
                  value={count}
                  max={capturedCount || 1}
                  label={`${type} · ${count}`}
                  color={/signature/i.test(type) ? "var(--teal)" : "#8b5cf6"}
                />
              ))}
            </div>
          )}
        </div>

        {/* Recent captures timeline */}
        <div className="clay-card p-4">
          <h2 className="section-title mb-3">Recent Captures</h2>
          {recentCaptures.length === 0 ? (
            <p className="text-xs text-slate-500">No proofs captured yet.</p>
          ) : (
            <div className="space-y-0">
              {recentCaptures.map((r, i) => {
                const isLast = i === recentCaptures.length - 1;
                return (
                  <div key={String(r.id ?? i)} className="flex gap-3">
                    <div className="flex flex-col items-center">
                      <div className="mt-1 h-2.5 w-2.5 shrink-0 rounded-full bg-emerald-500 ring-2 ring-white" />
                      {!isLast && <div className="mt-1 min-h-[18px] w-px flex-1 bg-slate-200" />}
                    </div>
                    <div className="min-w-0 pb-3">
                      <p className="truncate text-sm font-semibold text-slate-800">
                        {String(r.jobNumber ?? `JOB-${r.jobId}`)}
                        <span className="font-normal text-slate-500"> · {String(r.receivedBy || "signed")}</span>
                      </p>
                      <p className="text-[11px] text-slate-500">
                        {new Date(String(r.capturedAt)).toLocaleString()}
                      </p>
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </div>
      </div>

      {captureJob && (
        <CaptureModal
          job={captureJob}
          onClose={() => setCaptureJob(null)}
          submitting={captureMutation.isPending}
          error={captureMutation.isError ? (captureMutation.error as Error)?.message : undefined}
          onSubmit={(payload) =>
            captureMutation.mutate({ jobId: (captureJob.jobId ?? captureJob.id) as string | number, payload })
          }
        />
      )}
      {rejectProof && (
        <RejectModal
          proof={rejectProof}
          submitting={reviewMutation.isPending}
          error={reviewMutation.isError ? (reviewMutation.error as Error)?.message : undefined}
          onClose={() => setRejectProof(null)}
          onReject={(reason) => reviewMutation.mutate({ proofId: (rejectProof.proofId ?? rejectProof.id) as string | number, decision: "reject", reason })}
        />
      )}
      {selectedProofId !== null && (
        <EvidenceDetailModal
          data={detailQuery.data}
          loading={detailQuery.isLoading}
          error={detailQuery.isError ? (detailQuery.error as Error)?.message : undefined}
          onClose={() => setSelectedProofId(null)}
        />
      )}
    </div>
  );
}
