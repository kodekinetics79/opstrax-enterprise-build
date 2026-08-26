import { FormEvent, type ReactNode, useEffect, useState } from "react";
import { Link } from "react-router";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Download, Pencil, Plus, Route, Sparkles, Trash2, UserCheck, X } from "lucide-react";
import { AiInsightCard, DataTable, EmptyState, ErrorState, LoadingState, PageHeader, RiskBadge, StatusBadge, labelize } from "@/components/ui";
import { ClayStat } from "@/components/console";
import { usePermissions } from "@/hooks/usePermission";
import { useRouteDetail, useRoutes, useRouteSummary } from "@/hooks/useBatch2";
import { routesApi } from "@/services/routesApi";
import { downloadServerExport } from "@/services/fleetDomainApi";
import type { AnyRecord } from "@/types";
import { apiErrorMessage } from "@/utils/apiErrorMessage";
import { prepareRouteForm, routeFormForDisplay } from "@/utils/routeForm";

const routeFields = [["routeCode","Route Code"],["routeName","Route Name"],["region","Region / Zone"],["plannedStart","Planned Start"],["plannedEnd","Planned End"],["routeType","Route Type"],["optimizationMode","Optimization Mode"],["costEstimate","Cost Estimate"],["status","Status"],["notes","Notes"]];
const stopFields = [["stopSequence","Sequence"],["jobId","Job ID"],["customerId","Customer ID"],["stopType","Stop Type"],["address","Address"],["latitude","Latitude"],["longitude","Longitude"],["timeWindowStart","Window Start"],["timeWindowEnd","Window End"],["eta","ETA"],["status","Status"],["proofStatus","Proof Status"],["notes","Notes"]];
const PAGE_SIZE = 50;
type Toast = { kind: "success" | "error" | "info"; message: string };

export function RoutePlanningPage() {
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const [editing, setEditing] = useState<AnyRecord | null>(null);
  const [assigning, setAssigning] = useState<AnyRecord | null>(null);
  const [stopEditing, setStopEditing] = useState<AnyRecord | null>(null);
  const [query, setQuery] = useState("");
  const [status, setStatus] = useState("All");
  const [offset, setOffset] = useState(0);
  const [toast, setToast] = useState<Toast | null>(null);
  const permissions = usePermissions().map((p) => p.trim().toLowerCase().replace(".", ":"));
  const directlyHas = (permission: string) => permissions.includes("*") || permissions.includes(permission);
  const canManage = directlyHas("dispatch:manage");
  const canAssign = canManage || directlyHas("dispatch:assign");
  const canExport = directlyHas("dispatch:view") || canManage;
  const routes = useRoutes({ limit: PAGE_SIZE, offset, search: query, status });
  const routeRows = routes.data?.rows ?? [];
  const routeTotal = routes.data?.total ?? routeRows.length;
  const summary = useRouteSummary();
  const detail = useRouteDetail(selected?.id as string | number | undefined);
  const qc = useQueryClient();
  const notify = (kind: Toast["kind"], message: string) => setToast({ kind, message });
  useEffect(() => { setOffset(0); }, [query, status]);
  useEffect(() => {
    if (!toast) return;
    const timer = setTimeout(() => setToast(null), 3500);
    return () => clearTimeout(timer);
  }, [toast]);

  const refresh = async () => {
    await qc.invalidateQueries({ queryKey: ["routes"] });
    if (selected?.id) await qc.invalidateQueries({ queryKey: ["routes", "detail", selected.id] });
  };
  const save = useMutation({
    mutationFn: (payload: AnyRecord) => payload.id ? routesApi.update(String(payload.id), payload) : routesApi.create(payload),
    onSuccess: async () => { setEditing(null); await refresh(); notify("success", "Route saved"); },
    onError: (error: unknown) => notify("error", apiErrorMessage(error, "Route save was rejected. Review the highlighted fields and try again.")),
  });
  const remove = useMutation({
    mutationFn: (id: string | number) => routesApi.remove(id),
    onSuccess: async () => { setSelected(null); await refresh(); notify("success", "Route archived"); },
    onError: (error: Error) => notify("error", error.message || "Route archive was rejected"),
  });
  const assign = useMutation({
    mutationFn: ({ id, payload }: { id: string | number; payload: AnyRecord }) => routesApi.assign(id, payload),
    onSuccess: async () => { setAssigning(null); await refresh(); notify("success", "Route resources assigned"); },
    onError: (error: Error) => notify("error", error.message || "Assignment failed HOS, availability, branch, or conflict validation"),
  });
  const saveStop = useMutation({
    mutationFn: (payload: AnyRecord) => payload.id
      ? routesApi.updateStop(String(selected?.id), String(payload.id), payload)
      : routesApi.createStop(String(selected?.id), payload),
    onSuccess: async () => { setStopEditing(null); await refresh(); notify("success", "Route stop saved"); },
    onError: (error: Error) => notify("error", error.message || "Route stop validation failed"),
  });
  const deleteStop = useMutation({
    mutationFn: (stopId: string | number) => routesApi.deleteStop(String(selected?.id), stopId),
    onSuccess: async () => { await refresh(); notify("success", "Route stop removed"); },
    onError: (error: Error) => notify("error", error.message || "Route stop could not be removed"),
  });
  const optimize = useMutation({
    mutationFn: (id: string | number) => routesApi.optimizePreview(id),
    onSuccess: (result) => notify(result.optimizationAvailable ? "success" : "info", result.optimizationAvailable ? "Optimization preview generated" : "More route inputs are required"),
    onError: (error: Error) => notify("error", error.message || "Optimization preview failed"),
  });
  const exportRoutes = async () => {
    if (!canExport) return;
    try { await downloadServerExport("/api/routes/export", "route-plans.csv"); notify("success", "Full route plan exported"); }
    catch { notify("error", "Route export failed"); }
  };

  if (routes.isLoading) return <LoadingState />;
  if (routes.isError) return <ErrorState message={(routes.error as Error)?.message} />;
  const s = summary.data || {};
  return <div className="fleet-console space-y-3">
    {toast ? <div role="status" className={`fixed right-5 top-20 z-[90] rounded-xl border px-4 py-3 text-sm font-semibold shadow-xl ${toast.kind === "error" ? "border-rose-200 bg-rose-50 text-rose-800" : toast.kind === "success" ? "border-emerald-200 bg-emerald-50 text-emerald-800" : "border-sky-200 bg-sky-50 text-sky-800"}`}>{toast.message}</div> : null}
    <PageHeader eyebrow="Route Planning" title="Route Plans" description="Plan branch-owned routes, validate resources and HOS, manage stops, and preview deterministic sequencing." actions={<>
      <button type="button" className="btn-primary" disabled={!canManage} title={!canManage ? "dispatch:manage is required" : undefined} onClick={() => { if (canManage) { save.reset(); setEditing({ status: "Planned", routeType: "Delivery", optimizationMode: "Balanced" }); } }}><Plus className="h-4 w-4" /> Create Route</button>
      <button type="button" className="btn-ghost" disabled={!canExport} onClick={() => void exportRoutes()}><Download className="h-4 w-4" /> Export Route Plan</button>
    </>} />
    <div className="grid grid-cols-2 gap-3 md:grid-cols-5">
      {[["Total Routes Today","totalRoutesToday"],["Active Routes","activeRoutes"],["Planned Routes","plannedRoutes"],["Completed Routes","completedRoutes"],["Delayed Routes","delayedRoutes"],["Avg Stops","averageStopsPerRoute"],["Avg Route ETA","averageRouteEta"],["Efficiency","routeEfficiencyScore"],["High-Risk","highRiskRoutes"],["Cost Estimate","routeCostEstimate"]].map(([label,key], i) => <ClayStat key={key} Icon={Route} tone={["fc-clay-teal","fc-clay-emerald","fc-clay-sky","fc-clay-amber","fc-clay-red"][i % 5]} iconCls={["text-teal-700","text-emerald-700","text-sky-700","text-amber-700","text-rose-700"][i % 5]} label={label} value={String(s[key] ?? 0)} alert={/Risk|Delayed/.test(label)} />)}
    </div>
    <div className="panel flex flex-col gap-3 p-4 lg:flex-row lg:items-center">
      <input className="field lg:max-w-md" value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search routes, regions, driver, vehicle..." />
      <select className="field lg:max-w-[180px]" value={status} onChange={(e) => setStatus(e.target.value)}><option>All</option><option>Planned</option><option>Active</option><option>Completed</option><option>Delayed</option><option>At Risk</option><option>Cancelled</option></select>
      <span className="text-xs font-semibold text-slate-500">{routeRows.length} shown · {routeTotal.toLocaleString()} total</span>
      {routeTotal > PAGE_SIZE ? <span className="ml-auto flex items-center gap-2 text-xs text-slate-500"><button type="button" className="btn-ghost h-8" disabled={offset === 0 || routes.isFetching} onClick={() => setOffset(Math.max(0, offset - PAGE_SIZE))}>← Prev</button><span>Page {Math.floor(offset / PAGE_SIZE) + 1} of {Math.max(1, Math.ceil(routeTotal / PAGE_SIZE))}</span><button type="button" className="btn-ghost h-8" disabled={offset + PAGE_SIZE >= routeTotal || routes.isFetching} onClick={() => setOffset(offset + PAGE_SIZE)}>Next →</button></span> : null}
    </div>
    {routeRows.length ? <DataTable rows={routeRows} columns={["routeCode", "routeName", "region", "driverName", "vehicleCode", "stops", "plannedStart", "plannedEnd", "status", "estimatedDurationMinutes", "estimatedDistance", "efficiencyScore", "slaRisk", "recommendedAction"]} onSelect={setSelected} /> : <EmptyState title="No routes match these filters" subtitle="Adjust the status or search criteria." />}
    <RouteDrawer detail={detail.data} loading={detail.isLoading} canManage={canManage} canAssign={canAssign} optimizeResult={optimize.data}
      onClose={() => setSelected(null)} onEdit={(record) => { save.reset(); setEditing(record); }} onAssign={setAssigning}
      onAddStop={() => setStopEditing({ stopType: "Drop-off", status: "Pending", proofStatus: "Pending" })}
      onEditStop={setStopEditing} onDeleteStop={(stopId) => { if (window.confirm("Remove this route stop?")) deleteStop.mutate(stopId); }}
      onOptimize={(id) => optimize.mutate(id)} onArchive={(id) => remove.mutate(id)} />
    {editing ? <RouteModal
      title={editing.id ? "Edit Route" : "Create Route"}
      initial={editing}
      saving={save.isPending}
      serverError={save.error ? apiErrorMessage(save.error, "Route save was rejected. Review the fields and try again.") : null}
      onClearError={save.reset}
      onClose={() => { save.reset(); setEditing(null); }}
      onSave={(payload) => save.mutate(payload)}
    /> : null}
    {assigning ? <AssignmentModal initial={assigning} saving={assign.isPending} onClose={() => setAssigning(null)} onSave={(payload) => assign.mutate({ id: String(assigning.id), payload })} /> : null}
    {stopEditing ? <Modal title={stopEditing.id ? "Edit Route Stop" : "Add Route Stop"} fields={stopFields} initial={stopEditing} saving={saveStop.isPending} onClose={() => setStopEditing(null)} onSave={(p) => saveStop.mutate(p)} /> : null}
  </div>;
}

function RouteDrawer({ detail, loading, canManage, canAssign, onClose, onEdit, onAssign, onAddStop, onEditStop, onDeleteStop, onOptimize, onArchive, optimizeResult }: {
  detail?: AnyRecord; loading: boolean; canManage: boolean; canAssign: boolean; onClose: () => void; onEdit: (record: AnyRecord) => void;
  onAssign: (record: AnyRecord) => void; onAddStop: () => void; onEditStop: (record: AnyRecord) => void; onDeleteStop: (id: string | number) => void;
  onOptimize: (id: string | number) => void; onArchive: (id: string | number) => void; optimizeResult?: AnyRecord;
}) {
  if (loading) return null;
  const record = detail?.record as AnyRecord | undefined;
  if (!record) return null;
  const stops = (detail?.stops as AnyRecord[]) || [];
  const terminal = /completed|cancelled/i.test(String(record.status ?? ""));
  const active = /active|delayed|at risk/i.test(String(record.status ?? ""));
  return <div className="fixed inset-0 z-50 flex justify-end bg-slate-900/30 backdrop-blur-sm"><aside className="fleet-console h-full w-full max-w-4xl overflow-y-auto border-l border-slate-200 p-6"><button type="button" className="float-right icon-btn" onClick={onClose} aria-label="Close"><X /></button><p className="section-title">Route Detail</p><h2 className="mt-3 text-2xl font-black text-slate-950">{String(record.routeName || record.name)}</h2><div className="mt-4 flex flex-wrap gap-2"><StatusBadge status={record.status} /><RiskBadge risk={record.slaRisk} /><span className="badge">Efficiency {String(record.efficiencyScore)}%</span></div>
    <div className="mt-5 flex flex-wrap gap-3"><button type="button" className="btn-primary" disabled={!canManage || terminal} onClick={() => onEdit(record)}>Edit Route</button><button type="button" className="btn-ghost" disabled={!canAssign || terminal} onClick={() => onAssign(record)}><UserCheck className="h-4 w-4" /> Assign</button><button type="button" className="btn-ghost" disabled={!canManage || terminal} onClick={onAddStop}><Plus className="h-4 w-4" /> Add Stop</button><button type="button" className="btn-ghost" onClick={() => onOptimize(String(record.id))}><Sparkles className="h-4 w-4" /> Optimize Preview</button><button type="button" className="btn-ghost text-rose-700" disabled={!canManage || active} title={active ? "Complete or cancel this active route before archiving" : undefined} onClick={() => onArchive(String(record.id))}><Trash2 className="h-4 w-4" /> Archive</button><Link to="/trips" className="btn-ghost">Trips</Link><Link to="/jobs" className="btn-ghost">Jobs board</Link></div>
    <div className="mt-6 grid gap-4 lg:grid-cols-[1fr_320px]"><section className="deck-inset min-h-[300px] rounded-2xl p-4"><h3 className="section-title">Stop sequence</h3>{stops.length === 0 ? <p className="mt-3 text-sm text-slate-500">No stops added yet — add at least two geocoded stops before optimization.</p> : <ol className="mt-3 space-y-2">{stops.map((stop, i) => <li key={String(stop.id ?? i)} className="deck-alert flex items-center gap-3 px-3 py-2.5"><span className="grid h-6 w-6 shrink-0 place-items-center rounded-full bg-teal-100 text-[11px] font-black text-teal-800">{String(stop.stopSequence ?? i + 1)}</span><span className="min-w-0 flex-1"><span className="block truncate text-[12.5px] font-bold text-slate-800">{String(stop.customerName ?? stop.address ?? "Stop")}</span><span className="block truncate text-[10.5px] font-medium text-slate-400">{String(stop.stopType ?? "")}{stop.eta ? ` · ETA ${String(stop.eta)}` : ""}</span></span><StatusBadge status={stop.status} />{canManage && !terminal ? <><button type="button" className="icon-btn" aria-label="Edit stop" onClick={() => onEditStop(stop)}><Pencil className="h-4 w-4" /></button><button type="button" className="icon-btn text-rose-600" aria-label="Delete stop" onClick={() => onDeleteStop(String(stop.id))}><Trash2 className="h-4 w-4" /></button></> : null}</li>)}</ol>}</section>
      <section className="panel p-4"><h3 className="section-title">Route Cost / ETA Summary</h3>{["region","plannedStart","plannedEnd","estimatedDistance","estimatedDurationMinutes","costEstimate","optimizationMode"].map((key) => <p key={key} className="mt-2 text-sm text-slate-700"><span className="text-slate-500">{labelize(key)}:</span> {String(record[key] ?? "--")}</p>)}{optimizeResult ? <OptimizationResult result={optimizeResult} /> : null}</section></div>
    <Grid title="Route Recommendations" rows={(detail?.recommendations as AnyRecord[]) || []} columns={["title","body","score","status"]} /><Grid title="Audit Trail" rows={(detail?.auditTrail as AnyRecord[]) || []} columns={["actionName","actorName","createdAt"]} /></aside></div>;
}

function OptimizationResult({ result }: { result: AnyRecord }) {
  if (!result.optimizationAvailable) return <AiInsightCard insight={{ title: "Optimization unavailable", body: `${String((result.missingInputs as string[] | undefined)?.join("; ") ?? "Complete the required inputs")}. ${String(result.disclaimer ?? "")}` }} />;
  const sequence = (result.recommendedSequence as AnyRecord[] | undefined) ?? [];
  return <div className="mt-4"><AiInsightCard insight={{ title: "Optimization Preview", body: `${String(result.distanceSavingsMiles ?? 0)} straight-line miles potentially saved. ${String(result.disclaimer ?? "")}` }} /><ol className="mt-3 space-y-1 text-xs text-slate-600">{sequence.map((item) => <li key={String(item.stopId)}>#{String(item.sequence)} · Stop {String(item.stopId)} · {String(item.reason)}</li>)}</ol></div>;
}

function AssignmentModal({ initial, saving, onClose, onSave }: { initial: AnyRecord; saving: boolean; onClose: () => void; onSave: (payload: AnyRecord) => void }) {
  const [form, setForm] = useState<AnyRecord>({ driverId: initial.assignedDriverId ?? "", vehicleId: initial.assignedVehicleId ?? "", override: false });
  const submit = (event: FormEvent) => { event.preventDefault(); onSave({ ...form, driverId: Number(form.driverId), vehicleId: Number(form.vehicleId) }); };
  return <div className="fixed inset-0 z-[70] grid place-items-center bg-black/60 p-4"><form className="panel w-full max-w-lg p-6" onSubmit={submit}><div className="flex justify-between"><h2 className="text-xl font-bold">Assign Route Resources</h2><button type="button" className="icon-btn" onClick={onClose}><X /></button></div><div className="mt-5 grid gap-4 sm:grid-cols-2"><Field label="Driver ID"><input className="field" type="number" min="1" required value={String(form.driverId)} onChange={(e) => setForm((x) => ({ ...x, driverId: e.target.value }))} /></Field><Field label="Vehicle ID"><input className="field" type="number" min="1" required value={String(form.vehicleId)} onChange={(e) => setForm((x) => ({ ...x, vehicleId: e.target.value }))} /></Field></div><label className="mt-4 flex gap-2 text-sm"><input type="checkbox" checked={Boolean(form.override)} onChange={(e) => setForm((x) => ({ ...x, override: e.target.checked }))} /> Request authorized availability override</label><p className="mt-3 text-xs text-slate-500">The API validates tenant, branch, current HOS, maintenance state, availability, and active-route conflicts.</p><div className="mt-6 flex justify-end gap-3"><button type="button" className="btn-ghost" onClick={onClose}>Cancel</button><button type="submit" className="btn-primary" disabled={saving}>{saving ? "Assigning…" : "Assign & Activate"}</button></div></form></div>;
}

function RouteModal({ title, initial, saving, serverError, onClearError, onClose, onSave }: {
  title: string;
  initial: AnyRecord;
  saving: boolean;
  serverError: string | null;
  onClearError: () => void;
  onClose: () => void;
  onSave: (payload: AnyRecord) => void;
}) {
  const [form, setForm] = useState<AnyRecord>(() => routeFormForDisplay(initial));
  const [validationErrors, setValidationErrors] = useState<string[]>([]);
  const submit = (event: FormEvent) => {
    event.preventDefault();
    const prepared = prepareRouteForm(form);
    setValidationErrors(prepared.errors);
    if (prepared.errors.length > 0) return;
    onSave(prepared.payload);
  };
  const change = (key: string, value: string) => {
    setValidationErrors([]);
    onClearError();
    setForm((current) => ({ ...current, [key]: value }));
  };
  const errors = validationErrors.length > 0 ? validationErrors : serverError ? [serverError] : [];
  const typeFor = (key: string) => /start|end/i.test(key) ? "datetime-local" : /cost/i.test(key) ? "number" : "text";

  return <div className="fixed inset-0 z-[60] grid place-items-center bg-black/60 p-4">
    <form role="dialog" aria-modal="true" aria-labelledby="route-modal-title" aria-describedby={errors.length ? "route-form-errors" : undefined} className="panel max-h-[90vh] w-full max-w-3xl overflow-y-auto p-6" onSubmit={submit} noValidate>
      <div className="flex justify-between">
        <h2 id="route-modal-title" className="text-2xl font-semibold text-slate-900">{title}</h2>
        <button type="button" className="icon-btn" onClick={onClose} aria-label="Close route form"><X /></button>
      </div>
      {errors.length > 0 ? (
        <div id="route-form-errors" role="alert" className="mt-4 rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm font-semibold text-rose-800">
          <p>The route was not saved.</p>
          <ul className="mt-1 list-disc space-y-1 pl-5">{errors.map((error) => <li key={error}>{error}</li>)}</ul>
        </div>
      ) : null}
      <div className="mt-6 grid gap-4 md:grid-cols-2">
        {routeFields.map(([key, label]) => <Field key={key} label={label}>
          <input
            className="field"
            type={typeFor(key)}
            step={key === "costEstimate" ? "any" : /start|end/i.test(key) ? "1" : undefined}
            min={key === "costEstimate" ? "0" : undefined}
            value={String(form[key] ?? "")}
            onChange={(event) => change(key, event.target.value)}
            required={["routeCode", "routeName", "plannedStart", "plannedEnd"].includes(key)}
          />
        </Field>)}
      </div>
      <div className="mt-6 flex justify-end gap-3">
        <button type="button" className="btn-ghost" onClick={onClose} disabled={saving}>Cancel</button>
        <button type="submit" className="btn-primary" disabled={saving}>{saving ? "Saving…" : "Save Route"}</button>
      </div>
    </form>
  </div>;
}

function Modal({ title, fields, initial, saving, onClose, onSave }: { title: string; fields: string[][]; initial: AnyRecord; saving: boolean; onClose: () => void; onSave: (payload: AnyRecord) => void }) {
  const [form, setForm] = useState<AnyRecord>(initial);
  const submit = (event: FormEvent) => { event.preventDefault(); onSave(form); };
  const typeFor = (key: string) => /start|end|eta/i.test(key) ? "datetime-local" : /sequence|id$|latitude|longitude|cost/i.test(key) ? "number" : "text";
  return <div className="fixed inset-0 z-[60] grid place-items-center bg-black/60 p-4"><form className="panel max-h-[90vh] w-full max-w-3xl overflow-y-auto p-6" onSubmit={submit}><div className="flex justify-between"><h2 className="text-2xl font-semibold text-slate-900">{title}</h2><button type="button" className="icon-btn" onClick={onClose}><X /></button></div><div className="mt-6 grid gap-4 md:grid-cols-2">{fields.map(([key,label]) => <Field key={key} label={label}><input className="field" type={typeFor(key)} step={/latitude|longitude|cost/i.test(key) ? "any" : undefined} min={/sequence|id$|cost/i.test(key) ? "0" : undefined} value={String(form[key] ?? "")} onChange={(e) => setForm((x) => ({ ...x, [key]: e.target.value }))} required={["routeCode","routeName","plannedStart","plannedEnd","address","stopSequence"].includes(key)} /></Field>)}</div><div className="mt-6 flex justify-end gap-3"><button type="button" className="btn-ghost" onClick={onClose}>Cancel</button><button type="submit" className="btn-primary" disabled={saving}>{saving ? "Saving…" : "Save"}</button></div></form></div>;
}

function Field({ label, children }: { label: string; children: ReactNode }) { return <label><span className="mb-2 block text-xs font-bold uppercase tracking-[0.16em] text-slate-500">{label}</span>{children}</label>; }
function Grid({ title, rows, columns }: { title: string; rows: AnyRecord[]; columns: string[] }) { return <section className="fc-neumo mt-6 p-4"><h3 className="section-title">{title}</h3>{!rows.length ? <p className="mt-3 text-sm text-slate-500">No records yet.</p> : <div className="mt-3 overflow-x-auto"><table className="w-full min-w-[680px] text-left text-sm"><thead className="text-xs uppercase tracking-[0.16em] text-slate-500"><tr>{columns.map((column) => <th key={column} className="px-3 py-2">{labelize(column)}</th>)}</tr></thead><tbody className="divide-y divide-slate-100">{rows.slice(0, 10).map((row, index) => <tr key={String(row.id || index)}>{columns.map((column) => <td key={column} className="px-3 py-2 text-slate-600">{String(row[column] ?? "--")}</td>)}</tr>)}</tbody></table></div>}</section>; }
