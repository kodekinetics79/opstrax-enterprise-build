import { useState, useMemo, useRef } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, CheckCircle, ClipboardList, XCircle } from "lucide-react";
import { driverApi } from "@/services/driverApi";
import { ErrorState } from "@/components/ui";
import { useSingleFlight } from "@/hooks/useSingleFlight";
import { useDialogFocus } from "@/hooks/useDialogFocus";
import type { AnyRecord } from "@/types";

type ChecklistResult = "pass" | "fail" | "na";
const DVIR_ATTESTATION = "I certify that this DVIR is true and correct and that I completed this inspection.";

interface ChecklistItemState {
  templateId: number;
  itemName: string;
  category: string;
  isRequired: boolean;
  result: ChecklistResult | null;
  notes: string;
  severity: string;
}

function ResultButton({ label, active, color, onClick }: {
  label: string; active: boolean; color: string; onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`flex-1 rounded-xl py-3 text-sm font-bold transition ${
        active ? color : "bg-slate-100 text-slate-400"
      }`}
    >
      {label}
    </button>
  );
}

export function DriverDvirPage() {
  const qc = useQueryClient();
  const [selectedTemplateId, setSelectedTemplateId] = useState<number | null>(null);
  const [vehicleId, setVehicleId] = useState("");
  const [inspectionType, setInspectionType] = useState("pre_trip");
  const [odometer, setOdometer] = useState("");
  const [notes, setNotes] = useState("");
  const [items, setItems] = useState<ChecklistItemState[]>([]);
  const [submitted, setSubmitted] = useState(false);
  const [attestationAccepted, setAttestationAccepted] = useState(false);
  const idempotencyKey = useRef(crypto.randomUUID());
  const submitSingleFlight = useSingleFlight();

  const templates = useQuery<AnyRecord[]>({
    queryKey: ["driver", "dvir-templates"],
    queryFn: driverApi.dvirTemplates,
  });

  // The driver's own vehicles — a real picker instead of asking a driver to type a numeric
  // vehicle id (a primary key they have no way of knowing).
  //
  // Sourced from BOTH the driver's permanently-assigned vehicle (/driver/me) and any vehicle
  // on an active assignment. Assignments alone are not enough: a pre-trip inspection happens
  // BEFORE accepting a load, which is the single most common DVIR in a real fleet, and a
  // driver with no active assignment was dropped to a free-text "Vehicle ID" box — a dead end.
  const meQ = useQuery<AnyRecord>({ queryKey: ["driver", "me"], queryFn: driverApi.me });
  const assignmentsQ = useQuery<AnyRecord[]>({ queryKey: ["driver", "assignments"], queryFn: driverApi.assignments });
  const vehicles = useMemo(() => {
    const map = new Map<string, string>();

    const me = meQ.data as AnyRecord | undefined;
    const driver = (me?.["driver"] ?? me) as AnyRecord | undefined;
    const myVid = driver?.["vehicleId"] ?? driver?.["vehicle_id"];
    if (myVid != null && myVid !== "") {
      map.set(String(myVid), String(driver?.["vehicleCode"] ?? driver?.["vehicle_code"] ?? myVid));
    }

    for (const a of assignmentsQ.data ?? []) {
      const vid = a["vehicleId"] ?? a["vehicle_id"];
      if (vid != null && vid !== "") map.set(String(vid), String(a["vehicleCode"] ?? a["vehicle_code"] ?? vid));
    }
    return [...map.entries()].map(([id, code]) => ({ id, code }));
  }, [meQ.data, assignmentsQ.data]);

  const submitMut = useMutation({
    mutationFn: driverApi.submitDvir,
    onSuccess: () => {
      setSubmitted(true);
      setAttestationAccepted(false);
      idempotencyKey.current = crypto.randomUUID();
      void qc.invalidateQueries({ queryKey: ["driver"] });
    },
  });

  function selectTemplate(id: number) {
    setSelectedTemplateId(id);
    const tmpl = (templates.data ?? []).find(t => Number(t["id"]) === id);
    if (!tmpl) return;
    const checklistItems = ((tmpl["items"] as AnyRecord[]) ?? []).map(ci => ({
      templateId: id,
      itemName:   String(ci["itemName"] ?? ""),
      category:   String(ci["itemCategory"] ?? "General"),
      isRequired: Boolean(ci["isRequired"]),
      result:     null as ChecklistResult | null,
      notes:      "",
      severity:   "Low",
    }));
    setItems(checklistItems);
  }

  function setResult(idx: number, result: ChecklistResult) {
    setItems(prev => prev.map((item, i) => i === idx ? { ...item, result } : item));
  }

  function setItemNotes(idx: number, n: string) {
    setItems(prev => prev.map((item, i) => i === idx ? { ...item, notes: n } : item));
  }

  function setItemSeverity(idx: number, s: string) {
    setItems(prev => prev.map((item, i) => i === idx ? { ...item, severity: s } : item));
  }

  const hasFail   = items.some(i => i.result === "fail");
  const allDone   = items.length > 0 && items.every(i => i.result !== null);
  const incompleteRequired = items.some(i => i.isRequired && i.result === null);

  if (submitted) {
    return (
      <div className="flex flex-col items-center justify-center gap-4 p-12 text-center">
        <CheckCircle className="h-14 w-14 text-teal-500" />
        <p className="text-lg font-bold text-slate-900">DVIR Submitted</p>
        {hasFail && (
          <p className="text-sm text-amber-700 font-medium">
            Defects recorded for maintenance review.
          </p>
        )}
        <div className="w-full max-w-lg"><RepairAcknowledgments /></div>
        <div className="w-full max-w-lg"><PendingDvirSignatures /></div>
        <button
          type="button"
          className="mt-4 rounded-2xl bg-teal-600 px-6 py-3 text-sm font-bold text-white"
          onClick={() => { setSubmitted(false); setSelectedTemplateId(null); setItems([]); }}
        >
          Start Another Inspection
        </button>
      </div>
    );
  }

  if (templates.isLoading) {
    return (
      <div className="flex flex-col items-center justify-center gap-4 p-12">
        <ClipboardList className="h-12 w-12 animate-pulse text-slate-300" />
        <p className="text-sm text-slate-500">Loading DVIR templates…</p>
      </div>
    );
  }

  if (templates.isError) return <ErrorState message={(templates.error as Error)?.message} onRetry={() => void templates.refetch()} />;
  if (meQ.isError || assignmentsQ.isError) return <ErrorState message={`Authorized vehicle assignments are unavailable. No unassigned state has been inferred: ${((meQ.error ?? assignmentsQ.error) as Error)?.message ?? "request failed"}`} onRetry={() => { void meQ.refetch(); void assignmentsQ.refetch(); }} />;

  return (
    <div className="space-y-4 p-4 pb-10">
      <div className="pt-2">
        <p className="text-xs font-bold uppercase tracking-widest text-slate-400">Vehicle Inspection</p>
        <h1 className="mt-1 text-xl font-bold text-slate-900">DVIR</h1>
      </div>

      <RepairAcknowledgments />
      <PendingDvirSignatures />

      {/* Setup */}
      {!selectedTemplateId ? (
        <div className="space-y-4">
          <div>
            <label className="text-xs font-bold text-slate-500 block mb-1">Vehicle</label>
            {vehicles.length > 0 ? (
              <select
                className="w-full rounded-xl border border-slate-200 px-4 py-3 text-sm"
                value={vehicleId}
                onChange={e => setVehicleId(e.target.value)}
              >
                <option value="">Select vehicle…</option>
                {vehicles.map(v => <option key={v.id} value={v.id}>{v.code}</option>)}
              </select>
            ) : <div className="flex items-start gap-2 rounded-xl border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800"><AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />{assignmentsQ.isLoading || meQ.isLoading ? "Loading your authorized vehicles…" : "No authorized vehicle assignment is available. Contact dispatch before starting an inspection."}</div>}
          </div>
          <div>
            <label className="text-xs font-bold text-slate-500 block mb-1">Inspection Type</label>
            <select
              className="w-full rounded-xl border border-slate-200 px-4 py-3 text-sm"
              value={inspectionType}
              onChange={e => setInspectionType(e.target.value)}
            >
              <option value="pre_trip">Pre-trip</option>
              <option value="post_trip">Post-trip</option>
              <option value="en_route">En Route</option>
            </select>
          </div>
          <div>
            <label className="text-xs font-bold text-slate-500 block mb-1">Odometer (mi)</label>
            <input
              type="number"
              className="w-full rounded-xl border border-slate-200 px-4 py-3 text-sm"
              placeholder="Optional"
              value={odometer}
              onChange={e => setOdometer(e.target.value)}
            />
          </div>

          {/* Template selection */}
          <div>
            <p className="text-xs font-bold text-slate-500 mb-2">Select Checklist</p>
            {!vehicleId && <p className="mb-2 text-xs font-medium text-amber-600">Select a vehicle above to start an inspection.</p>}
            {(templates.data ?? []).map(t => (
              <button
                key={String(t["id"])}
                type="button"
                disabled={!vehicleId}
                className="mb-2 flex w-full items-center justify-between rounded-2xl border border-slate-200 bg-white px-5 py-4 text-left text-sm font-semibold text-slate-800 shadow-sm active:bg-slate-50 disabled:opacity-40 disabled:cursor-not-allowed"
                onClick={() => selectTemplate(Number(t["id"]))}
              >
                <span>{String(t["templateName"] ?? t["id"])}</span>
                <span className="text-xs text-slate-400">{String(t["inspectionType"] ?? "")}</span>
              </button>
            ))}
            {(templates.data ?? []).length === 0 && (
              <p className="text-sm text-slate-400">No inspection templates available. Contact your fleet admin.</p>
            )}
          </div>
        </div>
      ) : (
        /* Checklist form */
        <div className="space-y-4">
          {/* Group by category */}
          {Array.from(new Set(items.map(i => i.category))).map(cat => (
            <div key={cat} className="rounded-2xl border border-slate-200 bg-white overflow-hidden">
              <div className="bg-slate-50 px-4 py-2 border-b border-slate-200">
                <p className="text-xs font-bold uppercase tracking-wider text-slate-500">{cat}</p>
              </div>
              {items.filter(i => i.category === cat).map((item, _) => {
                const globalIdx = items.indexOf(item);
                return (
                  <div key={globalIdx} className="border-b border-slate-100 last:border-none px-4 py-4 space-y-2">
                    <p className="text-sm font-semibold text-slate-800">
                      {item.itemName}
                      {item.isRequired && <span className="ml-1 text-red-500">*</span>}
                    </p>
                    <div className="flex gap-2">
                      <ResultButton
                        label="Pass"
                        active={item.result === "pass"}
                        color="bg-teal-500 text-white"
                        onClick={() => setResult(globalIdx, "pass")}
                      />
                      <ResultButton
                        label="Fail"
                        active={item.result === "fail"}
                        color="bg-red-500 text-white"
                        onClick={() => setResult(globalIdx, "fail")}
                      />
                      <ResultButton
                        label="N/A"
                        active={item.result === "na"}
                        color="bg-slate-500 text-white"
                        onClick={() => setResult(globalIdx, "na")}
                      />
                    </div>
                    {item.result === "fail" && (
                      <div className="space-y-2">
                        <select
                          className="w-full rounded-xl border border-red-200 px-3 py-2 text-sm"
                          value={item.severity}
                          onChange={e => setItemSeverity(globalIdx, e.target.value)}
                        >
                          <option value="Low">Low — advisory, can continue</option>
                          <option value="Medium">Medium — monitor closely</option>
                          <option value="High">High — fix at next stop</option>
                          <option value="Critical">Critical — out of service immediately</option>
                        </select>
                        <textarea
                          className="w-full rounded-xl border border-red-200 px-3 py-2 text-sm"
                          rows={2}
                          placeholder="Describe the defect…"
                          value={item.notes}
                          onChange={e => setItemNotes(globalIdx, e.target.value)}
                        />
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          ))}

          {/* General notes */}
          <div>
            <label className="text-xs font-bold text-slate-500 block mb-1">Additional Notes</label>
            <textarea
              className="w-full rounded-xl border border-slate-200 px-4 py-3 text-sm"
              rows={3}
              placeholder="Optional inspection notes…"
              value={notes}
              onChange={e => setNotes(e.target.value)}
            />
          </div>

          {hasFail && (
            <div className="rounded-2xl border border-red-300 bg-red-50 p-4">
              <div className="flex items-center gap-2">
                <XCircle className="h-5 w-5 text-red-600 shrink-0" />
                <p className="text-sm font-bold text-red-800">Defects recorded</p>
              </div>
              {items.some(i => i.result === "fail" && i.severity === "Critical") && (
                <p className="mt-1 text-xs text-red-700">One or more Critical defects will place the vehicle out of service.</p>
              )}
            </div>
          )}

          {incompleteRequired && (
            <p className="text-xs text-amber-600 font-medium">Complete all required items (*) before submitting.</p>
          )}

          <label className="flex items-start gap-3 rounded-2xl border border-teal-200 bg-teal-50 p-4">
            <input type="checkbox" className="mt-1 h-4 w-4" checked={attestationAccepted} onChange={(event) => setAttestationAccepted(event.target.checked)} />
            <span className="text-sm font-medium text-teal-950">{DVIR_ATTESTATION}</span>
          </label>

          <div className="flex gap-3">
            <button
              type="button"
              className="flex-1 rounded-2xl border border-slate-200 bg-white py-4 text-sm font-bold text-slate-600"
              onClick={() => setSelectedTemplateId(null)}
            >
              Back
            </button>
            <button
              type="button"
              disabled={submitMut.isPending || incompleteRequired || !allDone || !attestationAccepted}
              className="flex-1 rounded-2xl bg-teal-600 py-4 text-sm font-bold text-white disabled:opacity-40"
              onClick={() => {
                void submitSingleFlight(() => submitMut.mutateAsync({
                  vehicleId: Number(vehicleId),
                  inspectionType,
                  odometerMiles: odometer ? Number(odometer) : undefined,
                  notes: notes || undefined,
                  idempotencyKey: idempotencyKey.current,
                  checklistItems: items.map(i => ({
                    category:  i.category,
                    itemName:  i.itemName,
                    result:    i.result as "pass" | "fail" | "na",
                    severity:  i.result === "fail" ? i.severity : undefined,
                    notes:     i.result === "fail" ? i.notes : undefined,
                  })),
                  attestationAccepted,
                  attestation: DVIR_ATTESTATION,
                }));
              }}
            >
              {submitMut.isPending ? "Submitting…" : "Submit DVIR"}
            </button>
          </div>

          {submitMut.isError && (
            <p className="text-sm text-red-600" role="alert">{(submitMut.error as Error)?.message}</p>
          )}
        </div>
      )}
    </div>
  );
}

function PendingDvirSignatures() {
  const qc = useQueryClient();
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const [accepted, setAccepted] = useState(false);
  const signRequest = useRef<Promise<AnyRecord> | null>(null);
  useDialogFocus<HTMLDivElement>(selected != null, () => setSelected(null));
  const reportsQ = useQuery<AnyRecord[]>({ queryKey: ["driver", "dvir-reports"], queryFn: driverApi.dvirReports });
  const sign = useMutation({
    mutationFn: async (report: AnyRecord) => {
      if (signRequest.current) return signRequest.current;
      const request = driverApi.signDvir(Number(report.id), Number(report["rowVersion"] ?? report["row_version"]));
      signRequest.current = request;
      try { return await request; } finally { signRequest.current = null; }
    },
    onSuccess: async () => { setSelected(null); setAccepted(false); await qc.invalidateQueries({ queryKey: ["driver", "dvir-reports"] }); },
  });
  if (reportsQ.isError) return <div className="rounded-xl border border-red-200 bg-red-50 p-3 text-sm text-red-700" role="alert">DVIR certifications are unavailable. No pending state has been inferred: {(reportsQ.error as Error)?.message}<button type="button" className="mt-2 block rounded-lg border border-red-200 bg-white px-3 py-2 text-xs font-bold disabled:opacity-40" disabled={reportsQ.isFetching} onClick={() => void reportsQ.refetch()}>{reportsQ.isFetching ? "Retrying…" : "Retry certifications"}</button></div>;
  const pending = (reportsQ.data ?? []).filter((report) => String(report["driverSignatureStatus"] ?? report["driver_signature_status"] ?? "Pending").toLowerCase() !== "signed");
  if (reportsQ.isLoading || pending.length === 0) return null;
  return <section className="rounded-2xl border border-teal-300 bg-teal-50 p-4 space-y-3"><div><p className="font-bold text-teal-950">DVIRs awaiting your certification</p><p className="text-xs text-teal-800">Review each submitted inspection before accepting the exact driver attestation.</p></div>{pending.map((report) => <div key={String(report.id)} className="flex items-center justify-between gap-3 rounded-xl border border-teal-200 bg-white p-3"><div><p className="text-sm font-semibold text-slate-900">{String(report["reportNumber"] ?? report["report_number"] ?? report.id)}</p><p className="text-xs text-slate-500">{String(report["vehicleCode"] ?? report["vehicle_code"] ?? "Vehicle")} · {String(report["inspectionType"] ?? report["inspection_type"] ?? "Inspection")}</p></div><button type="button" className="rounded-xl bg-teal-600 px-3 py-2 text-xs font-bold text-white" onClick={() => { setAccepted(false); setSelected(report); }}>Review &amp; certify</button></div>)}{sign.isError && <p className="text-sm text-red-700" role="alert">{(sign.error as Error)?.message}</p>}{selected && <div className="fixed inset-0 z-50 grid place-items-center bg-black/60 p-4" role="dialog" aria-modal="true" aria-label="Certify DVIR"><div className="w-full max-w-md rounded-2xl bg-white p-5 shadow-2xl space-y-4"><div><h2 className="font-bold text-slate-900">Certify DVIR</h2><p className="mt-1 text-sm text-slate-600">Review {String(selected["reportNumber"] ?? selected["report_number"] ?? selected.id)} before certifying.</p></div><label className="flex items-start gap-3 rounded-xl border border-slate-200 p-3"><input type="checkbox" className="mt-1 h-4 w-4" checked={accepted} onChange={(event) => setAccepted(event.target.checked)} /><span className="text-sm font-medium text-slate-800">{DVIR_ATTESTATION}</span></label><div className="flex gap-2"><button type="button" className="flex-1 rounded-xl border border-slate-200 py-3 text-sm font-bold text-slate-600" onClick={() => setSelected(null)}>Cancel</button><button type="button" className="flex-1 rounded-xl bg-teal-600 py-3 text-sm font-bold text-white disabled:opacity-40" disabled={!accepted || sign.isPending} onClick={() => sign.mutate(selected)}>{sign.isPending ? "Certifying…" : "Certify DVIR"}</button></div></div></div>}</section>;
}

const REPAIR_ATTESTATION = "I acknowledge that I reviewed the certified repairs for this DVIR.";

function RepairAcknowledgments() {
  const qc = useQueryClient();
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const [accepted, setAccepted] = useState(false);
  const acknowledgeRequest = useRef<Promise<AnyRecord> | null>(null);
  useDialogFocus<HTMLDivElement>(selected != null, () => setSelected(null));
  const reportsQ = useQuery<AnyRecord[]>({ queryKey: ["driver", "dvir-reports"], queryFn: driverApi.dvirReports });
  const acknowledge = useMutation({
    mutationFn: async (report: AnyRecord) => {
      if (acknowledgeRequest.current) return acknowledgeRequest.current;
      const request = driverApi.acknowledgeDvirRepairs(Number(report.id), Number(report["rowVersion"] ?? report["row_version"]));
      acknowledgeRequest.current = request;
      try { return await request; } finally { acknowledgeRequest.current = null; }
    },
    onSuccess: async () => { setSelected(null); setAccepted(false); await qc.invalidateQueries({ queryKey: ["driver", "dvir-reports"] }); },
  });
  if (reportsQ.isError) return <div className="rounded-xl border border-red-200 bg-red-50 p-3 text-sm text-red-700" role="alert">Repaired DVIRs are unavailable. No acknowledgment state has been inferred: {(reportsQ.error as Error)?.message}<button type="button" className="ml-2 underline disabled:opacity-40" disabled={reportsQ.isFetching} onClick={() => void reportsQ.refetch()}>{reportsQ.isFetching ? "Retrying…" : "Retry"}</button></div>;
  const pending = (reportsQ.data ?? []).filter((report) => String(report["repairCertificationStatus"] ?? report["repair_certification_status"]).toLowerCase() === "certified" && !(report["driverRepairAcknowledgedAt"] ?? report["driver_repair_acknowledged_at"]));
  if (reportsQ.isLoading || pending.length === 0) return null;
  return <section className="rounded-2xl border border-amber-300 bg-amber-50 p-4 space-y-3"><div className="flex items-start gap-2"><AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-amber-700" /><div><p className="font-bold text-amber-900">Certified repairs need your review</p><p className="text-xs text-amber-800">The vehicle remains unavailable until you review and acknowledge the certified repairs.</p></div></div>{pending.map((report) => <div key={String(report.id)} className="flex items-center justify-between gap-3 rounded-xl border border-amber-200 bg-white p-3"><div><p className="text-sm font-semibold text-slate-900">{String(report["reportNumber"] ?? report["report_number"] ?? report.id)}</p><p className="text-xs text-slate-500">{String(report["vehicleCode"] ?? report["vehicle_code"] ?? "Vehicle")} · {String(report["inspectionType"] ?? report["inspection_type"] ?? "Inspection")}</p></div><button type="button" className="rounded-xl bg-amber-600 px-3 py-2 text-xs font-bold text-white" onClick={() => { setAccepted(false); setSelected(report); }}>Review repairs</button></div>)}{acknowledge.isError && <p className="text-sm text-red-700" role="alert">{(acknowledge.error as Error)?.message}</p>}{selected && <div className="fixed inset-0 z-50 grid place-items-center bg-black/60 p-4" role="dialog" aria-modal="true" aria-label="Acknowledge certified DVIR repairs"><div className="w-full max-w-md rounded-2xl bg-white p-5 shadow-2xl space-y-4"><div><h2 className="font-bold text-slate-900">Review certified repairs</h2><p className="mt-1 text-sm text-slate-600">Confirm only after you have reviewed the repairs for {String(selected["reportNumber"] ?? selected["report_number"] ?? selected.id)}.</p></div><label className="flex items-start gap-3 rounded-xl border border-slate-200 p-3"><input type="checkbox" className="mt-1 h-4 w-4" checked={accepted} onChange={(event) => setAccepted(event.target.checked)} /><span className="text-sm font-medium text-slate-800">{REPAIR_ATTESTATION}</span></label><div className="flex gap-2"><button type="button" className="flex-1 rounded-xl border border-slate-200 py-3 text-sm font-bold text-slate-600" onClick={() => setSelected(null)}>Cancel</button><button type="button" className="flex-1 rounded-xl bg-teal-600 py-3 text-sm font-bold text-white disabled:opacity-40" disabled={!accepted || acknowledge.isPending} onClick={() => acknowledge.mutate(selected)}>{acknowledge.isPending ? "Recording…" : "Acknowledge repairs"}</button></div></div></div>}</section>;
}
