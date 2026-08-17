import { useMemo, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, CheckCircle2, ClipboardCheck, Plus, Search, ShieldAlert, Wrench, X } from "lucide-react";
import { dvirApi } from "@/services/dvirApi";
import { driversApi } from "@/services/driversApi";
import { vehiclesApi } from "@/services/vehiclesApi";
import { useHasDirectPermission } from "@/hooks/usePermission";
import { useDialogFocus } from "@/hooks/useDialogFocus";
import { useSingleFlight } from "@/hooks/useSingleFlight";
import { EmptyState, ErrorState, LoadingState } from "@/components/ui";
import { formatDateTime } from "@/utils/formatters";
import type { AnyRecord } from "@/types";

type DetailEnvelope = { record?: AnyRecord; defects?: AnyRecord[]; workOrders?: AnyRecord[] };

function value(row: AnyRecord | undefined, camel: string, snake: string = camel) {
  return row?.[camel] ?? row?.[snake];
}

function bool(valueToParse: unknown) {
  return valueToParse === true || valueToParse === 1 || valueToParse === "1" || String(valueToParse).toLowerCase() === "true";
}

function statusClass(status: string) {
  if (/certified|reviewed|signed|resolved/i.test(status)) return "border-emerald-200 bg-emerald-50 text-emerald-700";
  if (/repair|required|critical|unsafe|deleted/i.test(status)) return "border-red-200 bg-red-50 text-red-700";
  if (/pending|submitted|open/i.test(status)) return "border-amber-200 bg-amber-50 text-amber-700";
  return "border-slate-200 bg-slate-50 text-slate-600";
}

function StatusBadge({ status }: { status: unknown }) {
  const text = String(status ?? "Unknown");
  return <span className={`inline-flex rounded-full border px-2 py-0.5 text-[10px] font-bold ${statusClass(text)}`}>{text}</span>;
}

export function DvirInspectionsPage() {
  const qc = useQueryClient();
  const hasPermission = useHasDirectPermission();
  const canCreate = hasPermission("maintenance:create") || hasPermission("maintenance:manage");
  const canReview = hasPermission("maintenance:update") || hasPermission("maintenance:manage");
  const canClose = hasPermission("maintenance:close") || hasPermission("maintenance:manage");
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [showCreate, setShowCreate] = useState(false);
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState("All");
  const [resolveNotes, setResolveNotes] = useState<Record<string, string>>({});
  const [notice, setNotice] = useState<{ kind: "success" | "error"; text: string } | null>(null);
  const detailDialogRef = useDialogFocus<HTMLDivElement>(selectedId != null, () => setSelectedId(null));

  const listQ = useQuery({ queryKey: ["dvir", "reports"], queryFn: dvirApi.list, staleTime: 15_000 });
  const summaryQ = useQuery({ queryKey: ["dvir", "summary"], queryFn: dvirApi.summary, staleTime: 15_000 });
  const detailQ = useQuery({
    queryKey: ["dvir", "detail", selectedId],
    queryFn: () => dvirApi.detail(selectedId!),
    enabled: selectedId != null,
  });
  const timelineQ = useQuery({
    queryKey: ["dvir", "timeline", selectedId],
    queryFn: () => dvirApi.timeline(selectedId!),
    enabled: selectedId != null,
  });
  const driversQ = useQuery({ queryKey: ["dvir", "drivers"], queryFn: driversApi.list, enabled: showCreate });
  const vehiclesQ = useQuery({ queryKey: ["dvir", "vehicles"], queryFn: vehiclesApi.list, enabled: showCreate });
  const actionSingleFlight = useSingleFlight();

  const refresh = async () => {
    await Promise.all([
      qc.invalidateQueries({ queryKey: ["dvir"] }),
      qc.invalidateQueries({ queryKey: ["fleet-intel", "dvir-summary"] }),
    ]);
  };
  const action = useMutation({
    mutationFn: async ({ kind, id, rowVersion, notes }: { kind: "review" | "certify" | "resolve"; id: number; rowVersion: number; notes?: string }) => {
      if (kind === "review") return dvirApi.mechanicReview(id, rowVersion);
      if (kind === "certify") return dvirApi.certifyRepair(id, rowVersion);
      return dvirApi.resolveDefect(id, rowVersion, notes?.trim() ?? "");
    },
    onSuccess: async (_data, variables) => {
      setNotice({ kind: "success", text: variables.kind === "review" ? "Mechanic review recorded." : variables.kind === "certify" ? "Repair certification recorded; driver acknowledgment is required before release." : "Defect resolved; repair certification and driver acknowledgment remain required." });
      await refresh();
    },
    onError: (error) => setNotice({ kind: "error", text: error instanceof Error ? error.message : "DVIR action failed." }),
  });

  const rows = useMemo(() => {
    const term = search.trim().toLowerCase();
    return ((listQ.data as AnyRecord[] | undefined) ?? []).filter((row) => {
      const status = String(value(row, "inspectionStatus", "inspection_status") ?? "");
      const matchesStatus = statusFilter === "All" || status.toLowerCase().includes(statusFilter.toLowerCase());
      const haystack = [value(row, "reportNumber", "report_number"), value(row, "vehicleCode", "vehicle_code"), value(row, "driverName", "driver_name"), value(row, "inspectionType", "inspection_type"), status].join(" ").toLowerCase();
      return matchesStatus && (!term || haystack.includes(term));
    });
  }, [listQ.data, search, statusFilter]);

  if (listQ.isLoading || summaryQ.isLoading) return <LoadingState />;
  if (listQ.isError || summaryQ.isError) {
    const error = (listQ.error ?? summaryQ.error) as Error | null;
    return <ErrorState message={error?.message} onRetry={() => { void listQ.refetch(); void summaryQ.refetch(); }} />;
  }

  const summary = (summaryQ.data ?? {}) as AnyRecord;
  return (
    <div className="fleet-console flex h-full flex-col gap-4 overflow-y-auto">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="text-[11px] font-bold uppercase tracking-[0.18em] text-teal-700">Safety &amp; Compliance</p>
          <h1 className="mt-1 flex items-center gap-2 text-2xl font-black text-slate-900"><ClipboardCheck className="h-6 w-6 text-teal-600" />DVIR Inspections</h1>
          <p className="mt-1 text-sm text-slate-500">Persisted inspection reports, defect evidence, mechanic review and repair certification.</p>
        </div>
        <button type="button" className="btn-primary h-10 gap-1.5" disabled={!canCreate} title={!canCreate ? "Requires maintenance create permission" : "Create a no-defect administrative report"} onClick={() => setShowCreate(true)}>
          <Plus className="h-4 w-4" /> New report
        </button>
      </div>

      <div className="rounded-xl border border-blue-200 bg-blue-50 p-3 text-xs leading-5 text-blue-800">
        DVIR is an operational record, not a substitute for the carrier&apos;s applicable inspection program. Defects must originate with checklist evidence; only the authenticated driver can certify the driver signature.
      </div>
      {!canReview && !canClose && <div className="rounded-xl border border-slate-200 bg-slate-50 p-3 text-xs text-slate-600" role="status">Read-only access. Lifecycle actions require maintenance update or close permission.</div>}
      {notice && <div className={`rounded-xl border p-3 text-xs ${notice.kind === "error" ? "border-red-300 bg-red-50 text-red-700" : "border-emerald-300 bg-emerald-50 text-emerald-700"}`} role={notice.kind === "error" ? "alert" : "status"}>{notice.text}</div>}

      <div className="grid grid-cols-2 gap-3 lg:grid-cols-5">
        {[
          ["Inspections today", value(summary, "inspectionsToday", "inspections_today") ?? 0, ClipboardCheck, "text-teal-700"],
          ["Reports with defects", value(summary, "reportsWithDefects", "reports_with_defects") ?? 0, AlertTriangle, "text-amber-700"],
          ["Unsafe vehicles", value(summary, "unsafeVehicles", "unsafe_vehicles") ?? 0, ShieldAlert, "text-red-700"],
          ["Pending review", value(summary, "pendingMechanicReview", "pending_mechanic_review") ?? 0, Wrench, "text-amber-700"],
          ["Open OOS defects", value(summary, "openOutOfServiceDefects", "open_out_of_service_defects") ?? 0, ShieldAlert, "text-red-700"],
        ].map(([label, count, Icon, color]) => {
          const KpiIcon = Icon as typeof ClipboardCheck;
          return <div key={String(label)} className="panel p-4"><div className="flex items-center justify-between"><p className="text-[10px] font-bold uppercase tracking-wide text-slate-500">{String(label)}</p><KpiIcon className={`h-4 w-4 ${String(color)}`} /></div><p className={`mt-2 text-2xl font-black tabular-nums ${String(color)}`}>{String(count)}</p></div>;
        })}
      </div>

      <div className="panel flex flex-wrap gap-3 p-3">
        <label className="relative min-w-[240px] flex-1"><span className="sr-only">Search DVIR reports</span><Search className="absolute left-3 top-2.5 h-4 w-4 text-slate-400" /><input className="field h-9 pl-9!" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search report, vehicle, driver…" /></label>
        <label><span className="sr-only">Filter by status</span><select className="field h-9 min-w-40 py-0" value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)}>{["All", "Submitted", "Repair Required", "Reviewed", "Certified"].map((item) => <option key={item}>{item}</option>)}</select></label>
      </div>

      {rows.length === 0 ? <EmptyState title="No DVIR reports found" subtitle={search || statusFilter !== "All" ? "Clear the filters to view other authorized reports." : "No persisted inspection reports are available for your branch."} /> : (
        <div className="panel overflow-x-auto">
          <table className="w-full min-w-[900px] text-left text-sm">
            <thead><tr className="border-b border-slate-200">{["Report", "Submitted", "Vehicle", "Driver", "Inspection", "Defects", "Safe", "Status", "Signature"].map((heading) => <th key={heading} className="px-3 py-3 text-[10px] font-bold uppercase tracking-wide text-slate-500">{heading}</th>)}</tr></thead>
            <tbody className="divide-y divide-slate-100">{rows.map((row) => {
              const id = Number(row.id);
              return <tr key={id} className="cursor-pointer hover:bg-slate-50" tabIndex={0} onClick={() => setSelectedId(id)} onKeyDown={(event) => { if (event.key === "Enter") setSelectedId(id); }}>
                <td className="px-3 py-3 font-mono text-xs font-semibold text-teal-700">{String(value(row, "reportNumber", "report_number") ?? id)}</td>
                <td className="px-3 py-3 text-xs text-slate-600">{formatDateTime(String(value(row, "submittedAt", "submitted_at") ?? ""))}</td>
                <td className="px-3 py-3 font-medium text-slate-800">{String(value(row, "vehicleCode", "vehicle_code") ?? "—")}</td>
                <td className="px-3 py-3 text-slate-700">{String(value(row, "driverName", "driver_name") ?? "—")}</td>
                <td className="px-3 py-3 text-slate-600">{String(value(row, "inspectionType", "inspection_type") ?? "—")}</td>
                <td className="px-3 py-3 tabular-nums">{String(value(row, "defectsFound", "defects_found") ?? 0)}</td>
                <td className="px-3 py-3">{bool(value(row, "safeToOperate", "safe_to_operate")) ? <span className="text-emerald-700">Yes</span> : <span className="font-bold text-red-700">No</span>}</td>
                <td className="px-3 py-3"><StatusBadge status={value(row, "inspectionStatus", "inspection_status")} /></td>
                <td className="px-3 py-3"><StatusBadge status={value(row, "driverSignatureStatus", "driver_signature_status")} /></td>
              </tr>;
            })}</tbody>
          </table>
        </div>
      )}

      {selectedId != null && (
        <div ref={detailDialogRef} className="fixed inset-0 z-50 flex justify-end" role="dialog" aria-modal="true" aria-label="DVIR report detail">
          <button type="button" className="absolute inset-0 bg-black/55" aria-label="Close detail" onClick={() => setSelectedId(null)} />
          <aside className="relative z-10 h-full w-full max-w-2xl overflow-y-auto bg-white p-5 shadow-2xl">
            <div className="flex items-center justify-between"><h2 className="text-lg font-black text-slate-900">DVIR Detail</h2><button type="button" className="icon-btn" aria-label="Close" onClick={() => setSelectedId(null)}><X className="h-4 w-4" /></button></div>
            {detailQ.isLoading ? <div className="mt-5"><LoadingState /></div> : detailQ.isError ? <div className="mt-5"><ErrorState message={(detailQ.error as Error)?.message} onRetry={() => void detailQ.refetch()} /></div> : <DvirDetail
              detail={(detailQ.data ?? {}) as DetailEnvelope}
              canReview={canReview}
              canClose={canClose}
              busy={action.isPending}
              timeline={(timelineQ.data as AnyRecord[] | undefined) ?? []}
              resolveNotes={resolveNotes}
              onResolveNotes={(id, notes) => setResolveNotes((current) => ({ ...current, [id]: notes }))}
              onAction={(kind, id, rowVersion, notes) => { void actionSingleFlight(() => action.mutateAsync({ kind, id, rowVersion, notes })); }}
            />}
          </aside>
        </div>
      )}

      {showCreate && <CreateDvirDialog drivers={(driversQ.data as AnyRecord[] | undefined) ?? []} vehicles={(vehiclesQ.data as AnyRecord[] | undefined) ?? []} loading={driversQ.isLoading || vehiclesQ.isLoading} onClose={() => setShowCreate(false)} onCreated={async () => { setShowCreate(false); setNotice({ kind: "success", text: "DVIR report created." }); await refresh(); }} />}
    </div>
  );
}

function DvirDetail({ detail, canReview, canClose, busy, timeline, resolveNotes, onResolveNotes, onAction }: {
  detail: DetailEnvelope; canReview: boolean; canClose: boolean; busy: boolean; timeline: AnyRecord[]; resolveNotes: Record<string, string>;
  onResolveNotes: (id: string, notes: string) => void;
  onAction: (kind: "review" | "certify" | "resolve", id: number, rowVersion: number, notes?: string) => void;
}) {
  const record = (detail.record ?? {}) as AnyRecord;
  const defects = detail.defects ?? [];
  const id = Number(record.id);
  const rowVersion = Number(value(record, "rowVersion", "row_version"));
  const reviewStatus = String(value(record, "mechanicReviewStatus", "mechanic_review_status") ?? "Pending");
  const certStatus = String(value(record, "repairCertificationStatus", "repair_certification_status") ?? "Pending");
  const unresolved = defects.filter((defect) => !/resolved|rejected/i.test(String(defect.status ?? "Open")));
  return <div className="mt-5 space-y-5">
    <div className="grid gap-3 sm:grid-cols-2">{[
      ["Report", value(record, "reportNumber", "report_number")], ["Submitted", formatDateTime(String(value(record, "submittedAt", "submitted_at") ?? ""))],
      ["Vehicle", value(record, "vehicleCode", "vehicle_code")], ["Driver", value(record, "driverName", "driver_name")],
      ["Inspection", value(record, "inspectionType", "inspection_type")], ["Safe to operate", bool(value(record, "safeToOperate", "safe_to_operate")) ? "Yes" : "No"],
      ["Mechanic review", reviewStatus], ["Repair certification", certStatus], ["Driver signature", value(record, "driverSignatureStatus", "driver_signature_status")],
    ].map(([label, data]) => <div key={String(label)} className="rounded-xl border border-slate-200 p-3"><p className="text-[10px] font-bold uppercase tracking-wide text-slate-500">{String(label)}</p><p className="mt-1 text-sm font-semibold text-slate-800">{String(data ?? "—")}</p></div>)}</div>
    <div className="flex flex-wrap gap-2">
      <button type="button" className="btn-secondary" disabled={!canReview || busy || reviewStatus === "Reviewed"} title={!canReview ? "Requires maintenance update permission" : undefined} onClick={() => onAction("review", id, rowVersion)}><Wrench className="h-4 w-4" /> Record mechanic review</button>
      <button type="button" className="btn-primary" disabled={!canClose || busy || reviewStatus !== "Reviewed" || unresolved.length > 0 || certStatus === "Certified"} title={!canClose ? "Requires maintenance close permission" : unresolved.length > 0 ? "Resolve all defects first" : reviewStatus !== "Reviewed" ? "Mechanic review is required first" : undefined} onClick={() => onAction("certify", id, rowVersion)}><CheckCircle2 className="h-4 w-4" /> Certify repair</button>
    </div>
    <section><h3 className="text-sm font-black text-slate-900">Defect evidence ({defects.length})</h3>{defects.length === 0 ? <p className="mt-2 text-sm text-slate-500">No defect rows are attached to this report.</p> : <div className="mt-3 space-y-3">{defects.map((defect) => {
      const defectId = String(defect.id);
      const open = !/resolved|rejected/i.test(String(defect.status ?? "Open"));
      return <div key={defectId} className={`rounded-xl border p-4 ${bool(value(defect, "outOfService", "out_of_service")) && open ? "border-red-300 bg-red-50" : "border-slate-200 bg-slate-50"}`}>
        <div className="flex flex-wrap items-start justify-between gap-2"><div><p className="font-semibold text-slate-900">{String(value(defect, "defectCategory", "defect_category") ?? "Defect")}</p><p className="mt-1 text-sm text-slate-600">{String(value(defect, "defectDescription", "defect_description") ?? "No description")}</p></div><div className="flex gap-1"><StatusBadge status={defect.severity} /><StatusBadge status={defect.status} /></div></div>
        {open && canClose && <div className="mt-3 flex flex-wrap gap-2"><label className="min-w-60 flex-1"><span className="sr-only">Repair resolution note</span><input className="field h-9" value={resolveNotes[defectId] ?? ""} onChange={(event) => onResolveNotes(defectId, event.target.value)} placeholder="Repair verification note (3–2000 characters)" maxLength={2000} /></label><button type="button" className="btn-secondary h-9" disabled={busy || (resolveNotes[defectId] ?? "").trim().length < 3} onClick={() => onAction("resolve", Number(defect.id), Number(value(defect, "rowVersion", "row_version")), resolveNotes[defectId])}>Resolve defect</button></div>}
      </div>;
    })}</div>}</section>
    <section><h3 className="text-sm font-black text-slate-900">Linked work orders</h3>{(detail.workOrders ?? []).length === 0 ? <p className="mt-2 text-sm text-slate-500">No linked work orders.</p> : <div className="mt-2 space-y-2">{(detail.workOrders ?? []).map((wo) => <div key={String(wo.id)} className="rounded-xl border border-slate-200 p-3 text-sm"><span className="font-semibold text-slate-800">{String(value(wo, "workOrderNumber", "work_order_number") ?? wo.id)}</span><span className="ml-2 text-slate-500">{String(wo.status ?? "")}</span></div>)}</div>}</section>
    <section><h3 className="text-sm font-black text-slate-900">Immutable activity timeline</h3>{timeline.length === 0 ? <p className="mt-2 text-sm text-slate-500">No lifecycle events recorded.</p> : <div className="mt-2 space-y-2">{timeline.map((event, index) => <div key={`${String(event.id ?? index)}-${String(value(event, "occurredAt", "occurred_at") ?? index)}`} className="rounded-xl border border-slate-200 p-3"><div className="flex items-start justify-between gap-3"><p className="text-sm font-semibold text-slate-800">{String(value(event, "eventTitle", "event_title") ?? "DVIR activity")}</p><StatusBadge status={event.severity} /></div><p className="mt-1 text-xs text-slate-500">{formatDateTime(String(value(event, "occurredAt", "occurred_at") ?? ""))}</p>{value(event, "eventDescription", "event_description") ? <p className="mt-2 text-sm text-slate-600">{String(value(event, "eventDescription", "event_description"))}</p> : null}</div>)}</div>}</section>
  </div>;
}

function CreateDvirDialog({ drivers, vehicles, loading, onClose, onCreated }: { drivers: AnyRecord[]; vehicles: AnyRecord[]; loading: boolean; onClose: () => void; onCreated: () => Promise<void> }) {
  const createDialogRef = useDialogFocus<HTMLDivElement>(true, onClose);
  const [form, setForm] = useState({ driverId: "", vehicleId: "", inspectionType: "Pre-Trip", countryCode: "US", notes: "" });
  const idempotencyKey = useRef(crypto.randomUUID());
  const reportNumber = useRef(`DVIR-${Date.now()}`);
  const createSingleFlight = useSingleFlight();
  const mutation = useMutation({ mutationFn: () => dvirApi.create({ ...form, driverId: Number(form.driverId), vehicleId: Number(form.vehicleId), reportNumber: reportNumber.current, idempotencyKey: idempotencyKey.current }), onSuccess: onCreated });
  return <div ref={createDialogRef} className="fixed inset-0 z-[60] grid place-items-center bg-black/60 p-4" role="dialog" aria-modal="true" aria-label="Create DVIR report"><form className="panel w-full max-w-lg p-5" onSubmit={(event) => { event.preventDefault(); void createSingleFlight(() => mutation.mutateAsync()); }}>
    <div className="flex items-center justify-between"><div><h2 className="text-lg font-black text-slate-900">New DVIR report</h2><p className="mt-1 text-xs text-slate-500">Administrative zero-defect report. Driver checklist defects must use the driver inspection workflow.</p></div><button type="button" className="icon-btn" aria-label="Close" onClick={onClose}><X className="h-4 w-4" /></button></div>
    {loading ? <div className="mt-5"><LoadingState /></div> : <div className="mt-5 grid gap-4">
      <label><span className="field-label">Driver</span><select className="field mt-1" required value={form.driverId} onChange={(event) => setForm({ ...form, driverId: event.target.value })}><option value="">Select driver…</option>{drivers.map((driver) => <option key={String(driver.id)} value={String(driver.id)}>{String(value(driver, "fullName", "full_name") ?? value(driver, "driverName", "driver_name") ?? driver.id)}</option>)}</select></label>
      <label><span className="field-label">Vehicle</span><select className="field mt-1" required value={form.vehicleId} onChange={(event) => setForm({ ...form, vehicleId: event.target.value })}><option value="">Select vehicle…</option>{vehicles.map((vehicle) => <option key={String(vehicle.id)} value={String(vehicle.id)}>{String(value(vehicle, "vehicleCode", "vehicle_code") ?? vehicle.id)}</option>)}</select></label>
      <div className="grid grid-cols-2 gap-3"><label><span className="field-label">Inspection type</span><select className="field mt-1" value={form.inspectionType} onChange={(event) => setForm({ ...form, inspectionType: event.target.value })}><option>Pre-Trip</option><option>Post-Trip</option><option>En Route</option></select></label><label><span className="field-label">Country</span><input className="field mt-1" maxLength={12} value={form.countryCode} onChange={(event) => setForm({ ...form, countryCode: event.target.value.toUpperCase() })} /></label></div>
      <label><span className="field-label">Notes</span><textarea className="field mt-1 min-h-24" maxLength={4000} value={form.notes} onChange={(event) => setForm({ ...form, notes: event.target.value })} /></label>
      {mutation.isError && <p className="text-sm text-red-700" role="alert">{(mutation.error as Error)?.message}</p>}
      <div className="flex justify-end gap-2"><button type="button" className="btn-ghost" onClick={onClose}>Cancel</button><button type="submit" className="btn-primary" disabled={mutation.isPending || !form.driverId || !form.vehicleId}>{mutation.isPending ? "Creating…" : "Create report"}</button></div>
    </div>}
  </form></div>;
}
