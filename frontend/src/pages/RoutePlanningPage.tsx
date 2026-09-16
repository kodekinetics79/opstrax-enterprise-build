import { FormEvent, type ReactNode, useEffect, useState } from "react";
import { Link } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Download, Pencil, Plus, Route, Sparkles, Trash2, UserCheck, X } from "lucide-react";
import { AiInsightCard, KpiCard, DataTable, EmptyState, ErrorState, LoadingState, PageHeader, RiskBadge, StatusBadge, labelize } from "@/components/ui";

import { usePermissions } from "@/hooks/usePermission";
import { useTenantCountry } from "@/hooks/useTenantRegion";
import { useRouteDetail, useRoutes, useRouteSummary } from "@/hooks/useBatch2";
import { jobsApi } from "@/services/jobsApi";
import { routesApi } from "@/services/routesApi";
import { downloadServerExport } from "@/services/fleetDomainApi";
import type { AnyRecord } from "@/types";
import { apiErrorMessage } from "@/utils/apiErrorMessage";
import { prepareRouteForm, routeFormForDisplay } from "@/utils/routeForm";
import { formatTenantDistanceFromMiles } from "@/utils/tenantMeasurements";

const routeFields = [["routeCode","Route Code"],["routeName","Route Name"],["region","Region / Zone"],["plannedStart","Planned Start"],["plannedEnd","Planned End"],["routeType","Route Type"],["optimizationMode","Optimization Mode"],["costEstimate","Cost Estimate"],["status","Status"],["notes","Notes"]];
const stopFields = [["stopSequence","Sequence"],["stopType","Stop Type"],["address","Address"],["latitude","Latitude"],["longitude","Longitude"],["timeWindowStart","Window Start"],["timeWindowEnd","Window End"],["eta","ETA"],["status","Status"],["notes","Notes"]];
const PAGE_SIZE = 50;
function routeDateTime(value: unknown): string {
  if (!value) return "Not scheduled";
  const date = new Date(String(value));
  return Number.isFinite(date.getTime()) ? new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric", year: "numeric", hour: "2-digit", minute: "2-digit", hour12: false, timeZone: "UTC", timeZoneName: "short" }).format(date) : "Time unavailable";
}

function routeEstimate(value: unknown, unit: string): string {
  return value != null && Number.isFinite(Number(value)) && Number(value) > 0 ? `${Number(value).toLocaleString()} ${unit}` : "Not calculated";
}

type Toast = { kind: "success" | "error" | "info"; message: string };

export function RoutePlanningPage() {
  const tenantCountry = useTenantCountry();
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
    <div className="panel flex flex-wrap divide-x divide-slate-100" aria-label="Route summary">
      {[["Total Routes Today","totalRoutesToday"],["Active Routes","activeRoutes"],["Planned Routes","plannedRoutes"],["Completed Routes","completedRoutes"],["Delayed Routes","delayedRoutes"],["Avg Stops","averageStopsPerRoute"],["Avg Route ETA","averageRouteEta"],["Efficiency","routeEfficiencyScore"],["High-Risk","highRiskRoutes"],["Cost Estimate","routeCostEstimate"]].map(([label,key]) => <KpiCard compact key={key} label={label} value={key === "averageRouteEta" && !/\d/.test(String(s[key] ?? "")) ? "—" : String(s[key] ?? "—")} />)}
    </div>
    <div className="panel flex flex-col gap-3 p-4 lg:flex-row lg:items-center">
      <input aria-label="Search routes" className="field lg:max-w-md" value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search routes, regions, driver, vehicle..." />
      <select aria-label="Route status" className="field lg:max-w-[180px]" value={status} onChange={(e) => setStatus(e.target.value)}><option>All</option><option>Planned</option><option>Active</option><option>Completed</option><option>Delayed</option><option>At Risk</option><option>Cancelled</option></select>
      <span className="text-xs font-semibold text-slate-500">{routeRows.length} shown · {routeTotal.toLocaleString()} total</span>
      {routeTotal > PAGE_SIZE ? <span className="ml-auto flex items-center gap-2 text-xs text-slate-500"><button type="button" className="btn-ghost h-8" disabled={offset === 0 || routes.isFetching} onClick={() => setOffset(Math.max(0, offset - PAGE_SIZE))}>← Prev</button><span>Page {Math.floor(offset / PAGE_SIZE) + 1} of {Math.max(1, Math.ceil(routeTotal / PAGE_SIZE))}</span><button type="button" className="btn-ghost h-8" disabled={offset + PAGE_SIZE >= routeTotal || routes.isFetching} onClick={() => setOffset(offset + PAGE_SIZE)}>Next →</button></span> : null}
    </div>
    {routeRows.length ? <DataTable rows={routeRows} columns={["routeCode", "routeName", "region", "driverName", "vehicleCode", "stops", "plannedStart", "plannedEnd", "status", "estimatedDurationMinutes", "estimatedDistance", "slaRisk", "recommendedAction"]} showToolbar={false} cellRenderers={{ plannedStart: (row) => routeDateTime(row.plannedStart), plannedEnd: (row) => routeDateTime(row.plannedEnd), estimatedDistance: (row) => formatTenantDistanceFromMiles(row.estimatedDistance, tenantCountry), estimatedDurationMinutes: (row) => routeEstimate(row.estimatedDurationMinutes, "min") }} onSelect={setSelected} /> : <EmptyState title="No routes match these filters" subtitle="Adjust the status or search criteria." />}
    <RouteDrawer detail={detail.data} loading={detail.isLoading} canManage={canManage} canAssign={canAssign} optimizeResult={optimize.data} tenantCountry={tenantCountry}
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
    {assigning ? <AssignmentModal initial={assigning} saving={assign.isPending} serverError={assign.error ? apiErrorMessage(assign.error, "Route assignment was rejected.") : null} onClearError={assign.reset} onClose={() => { assign.reset(); setAssigning(null); }} onSave={(payload) => assign.mutate({ id: String(assigning.id), payload })} /> : null}
    {stopEditing ? <StopModal title={stopEditing.id ? "Edit Route Stop" : "Add Route Stop"} route={(detail.data?.record as AnyRecord) ?? selected ?? {}} initial={stopEditing} saving={saveStop.isPending} serverError={saveStop.error ? apiErrorMessage(saveStop.error, "Route stop was rejected.") : null} onClearError={saveStop.reset} onClose={() => { saveStop.reset(); setStopEditing(null); }} onSave={(p) => saveStop.mutate(p)} /> : null}
  </div>;
}

function RouteDrawer({ detail, loading, canManage, canAssign, onClose, onEdit, onAssign, onAddStop, onEditStop, onDeleteStop, onOptimize, onArchive, optimizeResult, tenantCountry }: {
  detail?: AnyRecord; loading: boolean; canManage: boolean; canAssign: boolean; onClose: () => void; onEdit: (record: AnyRecord) => void;
  onAssign: (record: AnyRecord) => void; onAddStop: () => void; onEditStop: (record: AnyRecord) => void; onDeleteStop: (id: string | number) => void;
  onOptimize: (id: string | number) => void; onArchive: (id: string | number) => void; optimizeResult?: AnyRecord; tenantCountry: string | null;
}) {
  if (loading) return null;
  const record = detail?.record as AnyRecord | undefined;
  if (!record) return null;
  const stops = (detail?.stops as AnyRecord[]) || [];
  const terminal = /completed|cancelled/i.test(String(record.status ?? ""));
  const active = /active|delayed|at risk/i.test(String(record.status ?? ""));
  return <div className="fixed inset-0 z-50 flex justify-end bg-slate-900/30 backdrop-blur-sm"><aside className="fleet-console h-full w-full max-w-4xl overflow-y-auto border-l border-slate-200 p-6"><button type="button" className="float-right icon-btn" onClick={onClose} aria-label="Close"><X /></button><p className="section-title">Route Detail</p><h2 className="mt-3 text-2xl font-black text-slate-950">{String(record.routeName || record.name)}</h2><div className="mt-4 flex flex-wrap gap-2"><StatusBadge status={record.status} /><RiskBadge risk={record.slaRisk} /></div>
    <div className="mt-5 flex flex-wrap gap-3"><button type="button" className="btn-primary" disabled={!canManage || terminal} onClick={() => onEdit(record)}>Edit Route</button><button type="button" className="btn-ghost" disabled={!canAssign || terminal} onClick={() => onAssign(record)}><UserCheck className="h-4 w-4" /> Assign</button><button type="button" className="btn-ghost" disabled={!canManage || terminal} onClick={onAddStop}><Plus className="h-4 w-4" /> Add Stop</button><button type="button" className="btn-ghost" onClick={() => onOptimize(String(record.id))}><Sparkles className="h-4 w-4" /> Optimize Preview</button><button type="button" className="btn-ghost text-rose-700" disabled={!canManage || active} title={active ? "Complete or cancel this active route before archiving" : undefined} onClick={() => onArchive(String(record.id))}><Trash2 className="h-4 w-4" /> Archive</button><Link to="/trips" className="btn-ghost">Trips</Link><Link to="/jobs" className="btn-ghost">Jobs board</Link></div>
    <div className="mt-6 grid gap-4 lg:grid-cols-[1fr_320px]"><section className="deck-inset min-h-[300px] rounded-2xl p-4"><h3 className="section-title">Stop sequence</h3>{stops.length === 0 ? <p className="mt-3 text-sm text-slate-500">No stops added yet — add at least two geocoded stops before optimization.</p> : <ol className="mt-3 space-y-2">{stops.map((stop, i) => <li key={String(stop.id ?? i)} className="deck-alert flex items-center gap-3 px-3 py-2.5"><span className="grid h-6 w-6 shrink-0 place-items-center rounded-full bg-teal-100 text-[11px] font-black text-teal-800">{String(stop.stopSequence ?? i + 1)}</span><span className="min-w-0 flex-1"><span className="block truncate text-[12.5px] font-bold text-slate-800">{String(stop.customerName ?? stop.address ?? "Stop")}</span><span className="block truncate text-[10.5px] font-medium text-slate-400">{String(stop.stopType ?? "")}{stop.eta ? ` · ETA ${routeDateTime(stop.eta)}` : ""}</span></span><StatusBadge status={stop.status} />{canManage && !terminal ? <><button type="button" className="icon-btn" aria-label="Edit stop" onClick={() => onEditStop(stop)}><Pencil className="h-4 w-4" /></button><button type="button" className="icon-btn text-rose-600" aria-label="Delete stop" onClick={() => onDeleteStop(String(stop.id))}><Trash2 className="h-4 w-4" /></button></> : null}</li>)}</ol>}</section>
      <section className="panel p-4"><h3 className="section-title">Route Cost / ETA Summary</h3>{["region","plannedStart","plannedEnd","estimatedDistance","estimatedDurationMinutes","costEstimate","optimizationMode"].map((key) => <p key={key} className="mt-2 text-sm text-slate-700"><span className="text-slate-500">{labelize(key)}:</span> {key === "plannedStart" || key === "plannedEnd" ? routeDateTime(record[key]) : key === "estimatedDistance" ? formatTenantDistanceFromMiles(record[key], tenantCountry) : key === "estimatedDurationMinutes" ? routeEstimate(record[key], "min") : String(record[key] ?? "--")}</p>)}{optimizeResult ? <OptimizationResult result={optimizeResult} tenantCountry={tenantCountry} /> : null}</section></div>
    <Grid title="Route Recommendations" rows={(detail?.recommendations as AnyRecord[]) || []} columns={["title","body","score","status"]} /><Grid title="Audit Trail" rows={(detail?.auditTrail as AnyRecord[]) || []} columns={["actionName","actorName","createdAt"]} /></aside></div>;
}

function OptimizationResult({ result, tenantCountry }: { result: AnyRecord; tenantCountry: string | null }) {
  if (!result.optimizationAvailable) return <AiInsightCard insight={{ title: "Optimization unavailable", body: `${String((result.missingInputs as string[] | undefined)?.join("; ") ?? "Complete the required inputs")}. ${String(result.disclaimer ?? "")}` }} />;
  const sequence = (result.recommendedSequence as AnyRecord[] | undefined) ?? [];
  return <div className="mt-4"><AiInsightCard insight={{ title: "Optimization Preview", body: `${formatTenantDistanceFromMiles(result.distanceSavingsMiles ?? 0, tenantCountry)} straight-line distance potentially saved. ${String(result.disclaimer ?? "")}` }} /><ol className="mt-3 space-y-1 text-xs text-slate-600">{sequence.map((item) => <li key={String(item.stopId)}>#{String(item.sequence)} · Stop {String(item.stopId)} · {String(item.reason)}</li>)}</ol></div>;
}

function AssignmentModal({ initial, saving, serverError, onClearError, onClose, onSave }: { initial: AnyRecord; saving: boolean; serverError: string | null; onClearError: () => void; onClose: () => void; onSave: (payload: AnyRecord) => void }) {
  const [form, setForm] = useState<AnyRecord>({ driverId: initial.assignedDriverId ?? "", vehicleId: initial.assignedVehicleId ?? "", override: false });
  const optionsQ = useQuery({ queryKey: ["route-assignment-options", initial.id], queryFn: () => routesApi.assignmentOptions(String(initial.id)) });
  const drivers = (optionsQ.data?.drivers ?? []) as AnyRecord[];
  const vehicles = (optionsQ.data?.vehicles ?? []) as AnyRecord[];
  const driver = drivers.find((row) => String(row.id) === String(form.driverId));
  const vehicle = vehicles.find((row) => String(row.id) === String(form.vehicleId));
  const change = (key: string, value: unknown) => { onClearError(); setForm((current) => ({ ...current, [key]: value })); };
  const submit = (event: FormEvent) => { event.preventDefault(); if (!driver || !vehicle || driver.hosBlockReason) return; onSave({ ...form, driverId: Number(form.driverId), vehicleId: Number(form.vehicleId) }); };
  return <div className="fixed inset-0 z-[70] grid place-items-center bg-black/60 p-4"><form role="dialog" aria-modal="true" aria-labelledby="route-assignment-title" className="panel max-h-[90vh] w-full max-w-lg overflow-y-auto p-5" onSubmit={submit}>
    <div className="flex justify-between"><h2 id="route-assignment-title" className="text-xl font-bold">Assign Route Resources</h2><button type="button" className="icon-btn" onClick={onClose} aria-label="Close route assignment"><X /></button></div>
    <p className="mt-2 text-xs text-slate-500">Choose resources from this route's branch. Current HOS, maintenance, availability and conflicts are checked again before activation.</p>
    {optionsQ.isLoading ? <LoadingState /> : optionsQ.isError ? <ErrorState message={apiErrorMessage(optionsQ.error, "Unable to load authorized route resources. Check dispatch assignment access.")} onRetry={() => { void optionsQ.refetch(); }} /> : <>
      <div className="mt-4 grid gap-3"><Field label="Driver"><select className="field w-full" required value={String(form.driverId)} disabled={saving} onChange={(event) => change("driverId", event.target.value)}><option value="">Select driver</option>{drivers.map((row) => <option key={String(row.id)} value={String(row.id)}>{String(row.driverCode ?? "")} · {String(row.fullName ?? "Driver")} · {row.hosBlockReason ? "HOS blocked" : String(row.status ?? "Unknown")}</option>)}</select></Field><Field label="Vehicle"><select className="field w-full" required value={String(form.vehicleId)} disabled={saving} onChange={(event) => change("vehicleId", event.target.value)}><option value="">Select vehicle</option>{vehicles.map((row) => <option key={String(row.id)} value={String(row.id)}>{String(row.vehicleCode ?? "Vehicle")} · {String(row.type ?? "")} · {String(row.status ?? "Unknown")}</option>)}</select></Field></div>
      {Boolean(driver?.hosBlockReason) && <p role="alert" className="mt-3 rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">{String(driver?.hosBlockReason)}. Review a fresh authoritative HOS clock before activating the route.</p>}
      {(!drivers.length || !vehicles.length) && <p className="mt-3 text-sm text-slate-600">No driver or vehicle choices are available in this route's branch. Add or correct the fleet resources before assignment.</p>}
      <label className="mt-3 flex gap-2 text-sm"><input type="checkbox" checked={Boolean(form.override)} disabled={saving} onChange={(event) => change("override", event.target.checked)} /> Request authorized availability override</label><p className="mt-1 text-xs text-slate-500">Availability overrides cannot replace current authoritative HOS or maintenance eligibility.</p>
    </>}
    {serverError && <p role="alert" className="mt-3 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">{serverError}</p>}
    <div className="mt-4 flex justify-end gap-2"><button type="button" className="btn-ghost" onClick={onClose} disabled={saving}>Cancel</button><button type="submit" className="btn-primary" disabled={saving || optionsQ.isLoading || optionsQ.isError || !driver || !vehicle || Boolean(driver.hosBlockReason)}>{saving ? "Assigning…" : "Assign & Activate"}</button></div>
  </form></div>;
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
  const [plainDateTimeEntry, setPlainDateTimeEntry] = useState(false);
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
  const typeFor = (key: string) => /start|end/i.test(key) ? (plainDateTimeEntry ? "text" : "datetime-local") : /cost/i.test(key) ? "number" : "text";

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
      <label className="mt-5 flex items-start gap-3 rounded-xl border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-700">
        <input type="checkbox" className="mt-1" checked={plainDateTimeEntry} onChange={(event) => setPlainDateTimeEntry(event.target.checked)} />
        <span><strong>Use plain date/time entry</strong><span className="mt-0.5 block text-xs text-slate-500">Accessible fallback for browsers or assistive tools that cannot operate the native picker. Enter YYYY-MM-DDTHH:MM; the same validation and UTC conversion apply.</span></span>
      </label>
      <div className="mt-6 grid gap-4 md:grid-cols-2">
        {routeFields.map(([key, label]) => <Field key={key} label={label}>
          <input
            id={`route-${key}`}
            className="field"
            type={typeFor(key)}
            inputMode={plainDateTimeEntry && /start|end/i.test(key) ? "text" : undefined}
            placeholder={plainDateTimeEntry && /start|end/i.test(key) ? "2026-08-26T09:00" : undefined}
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

function stopLocalDateTime(value: unknown): string {
  if (!value) return "";
  const date = new Date(String(value));
  if (!Number.isFinite(date.getTime())) return "";
  const pad = (number: number) => String(number).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function StopModal({ title, route, initial, saving, serverError, onClearError, onClose, onSave }: { title: string; route: AnyRecord; initial: AnyRecord; saving: boolean; serverError: string | null; onClearError: () => void; onClose: () => void; onSave: (payload: AnyRecord) => void }) {
  const [form, setForm] = useState<AnyRecord>(() => ({ ...initial, timeWindowStart: stopLocalDateTime(initial.timeWindowStart), timeWindowEnd: stopLocalDateTime(initial.timeWindowEnd), eta: stopLocalDateTime(initial.eta) }));
  const [search, setSearch] = useState("");
  const [plainDateTimeEntry, setPlainDateTimeEntry] = useState(false);
  const [errors, setErrors] = useState<string[]>([]);
  const jobsQ = useQuery({ queryKey: ["route-stop-job-options", route.id, search], queryFn: () => jobsApi.listPaged({ limit: 200, search }) });
  const jobId = String(form.jobId ?? "");
  const selectedQ = useQuery({ queryKey: ["route-stop-selected-job", jobId], queryFn: () => jobsApi.detail(jobId), enabled: Boolean(jobId) });
  const selectedJob = selectedQ.data?.record;
  const sameBranch = (job: AnyRecord) => String(job.branchId ?? "") === String(route.branchId ?? "");
  const choices = (jobsQ.data?.rows ?? []).filter((job) => sameBranch(job) && (job.routeId == null || String(job.routeId) === String(route.id)) && !/cancelled|deleted/i.test(String(job.status ?? "")));
  const validJob = !jobId || Boolean(selectedJob && sameBranch(selectedJob) && (selectedJob.routeId == null || String(selectedJob.routeId) === String(route.id)));
  const change = (key: string, value: string) => { onClearError(); setErrors([]); setForm((current) => ({ ...current, [key]: value })); };
  const submit = (event: FormEvent) => {
    event.preventDefault();
    const nextErrors: string[] = [];
    const payload: AnyRecord = { ...form, jobId: jobId ? Number(jobId) : null, customerId: selectedJob?.customerId ?? selectedJob?.customer_id ?? null };
    if (!Number.isInteger(Number(form.stopSequence)) || Number(form.stopSequence) < 1) nextErrors.push("Enter a positive stop sequence.");
    if (!String(form.address ?? "").trim()) nextErrors.push("Enter the stop address.");
    if (!validJob) nextErrors.push("Select a job from this route's branch that is not linked to another route.");
    const lat = String(form.latitude ?? "").trim(), lng = String(form.longitude ?? "").trim();
    if (Boolean(lat) !== Boolean(lng) || (lat && (!Number.isFinite(Number(lat)) || Number(lat) < -90 || Number(lat) > 90 || !Number.isFinite(Number(lng)) || Number(lng) < -180 || Number(lng) > 180))) nextErrors.push("Provide a valid latitude and longitude together, or leave both blank.");
    for (const key of ["timeWindowStart", "timeWindowEnd", "eta"]) {
      const value = String(form[key] ?? "").trim();
      if (!value) { delete payload[key]; continue; }
      const date = new Date(value);
      if (!Number.isFinite(date.getTime())) nextErrors.push(`Enter a valid ${labelize(key)} date and time.`);
      else payload[key] = date.toISOString();
    }
    if (payload.timeWindowStart && payload.timeWindowEnd && new Date(String(payload.timeWindowEnd)) <= new Date(String(payload.timeWindowStart))) nextErrors.push("The stop window must end after it starts.");
    setErrors(nextErrors);
    if (!nextErrors.length) onSave(payload);
  };
  const visibleErrors = errors.length ? errors : serverError ? [serverError] : [];
  return <div className="fixed inset-0 z-[60] grid place-items-center bg-black/60 p-4"><form role="dialog" aria-modal="true" aria-labelledby="route-stop-title" className="panel max-h-[90vh] w-full max-w-3xl overflow-y-auto p-5" onSubmit={submit} noValidate>
    <div className="flex justify-between"><h2 id="route-stop-title" className="text-xl font-semibold text-slate-900">{title}</h2><button type="button" className="icon-btn" onClick={onClose} aria-label="Close route stop"><X /></button></div>
    {visibleErrors.length > 0 && <div role="alert" className="mt-3 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">{visibleErrors.map((error) => <p key={error}>{error}</p>)}</div>}
    <div className="mt-4 grid gap-3 md:grid-cols-2">
      <Field label="Find job"><input className="field w-full" value={search} disabled={saving} onChange={(event) => setSearch(event.target.value)} placeholder="Job code or customer name" /></Field>
      <Field label="Linked job"><select className="field w-full" value={jobId} disabled={saving || jobsQ.isLoading} onChange={(event) => change("jobId", event.target.value)}><option value="" disabled={Boolean(initial.jobId)}>No linked job</option>{jobId && !choices.some((job) => String(job.id) === jobId) && <option value={jobId}>{String(selectedJob?.jobCode ?? selectedJob?.jobNumber ?? initial.jobCode ?? "Loading selected job…")}</option>}{choices.map((job) => <option key={String(job.id)} value={String(job.id)}>{String(job.jobCode ?? job.jobNumber ?? job.id)} · {String(job.customerName ?? "No customer")} · {String(job.status ?? "Unknown")}</option>)}</select></Field>
      <Field label="Customer"><div className="field w-full bg-slate-50">{jobId ? selectedQ.isLoading ? "Loading customer…" : String(selectedJob?.customerName ?? "No customer linked") : "Derived from the linked job"}</div></Field>
      <div className="flex items-end"><button type="button" className="btn-secondary text-xs" disabled={saving || !selectedJob || !validJob} onClick={() => { const pickup = /pickup|pick-up/i.test(String(form.stopType)); change("address", String((pickup ? selectedJob?.pickupAddress : selectedJob?.dropoffAddress) ?? "")); }}>Use job stop address</button></div>
      {stopFields.map(([key, label]) => <Field key={key} label={label}>{key === "stopType" ? <select className="field w-full" value={String(form[key] ?? "Drop-off")} disabled={saving} onChange={(event) => change(key, event.target.value)}><option>Pickup</option><option>Drop-off</option><option>Warehouse</option><option>Break</option></select> : key === "status" ? <select className="field w-full" value={String(form[key] ?? "Pending")} disabled={saving} onChange={(event) => change(key, event.target.value)}><option>Pending</option><option>Arrived</option><option>Completed</option></select> : <input className="field w-full" type={/start|end|eta/i.test(key) ? plainDateTimeEntry ? "text" : "datetime-local" : /sequence|latitude|longitude/i.test(key) ? "number" : "text"} step={/latitude|longitude/i.test(key) ? "any" : undefined} value={String(form[key] ?? "")} disabled={saving} onChange={(event) => change(key, event.target.value)} />}</Field>)}
    </div>
    <label className="mt-3 flex items-center gap-2 text-xs text-slate-600"><input type="checkbox" checked={plainDateTimeEntry} disabled={saving} onChange={(event) => setPlainDateTimeEntry(event.target.checked)} /> Type stop dates and times as text</label><p className="mt-1 text-xs text-slate-500">Times use your browser's timezone. Text entry also accepts an explicit offset, for example 2026-09-15T09:00:00-05:00. Coordinates are optional unless you request optimization.</p>
    {jobsQ.isError && <ErrorState message={apiErrorMessage(jobsQ.error, "Unable to load authorized jobs. Check shipment read access.")} onRetry={() => { void jobsQ.refetch(); }} />}
    {jobId && selectedQ.isError && <ErrorState message={apiErrorMessage(selectedQ.error, "Unable to load the linked job.")} onRetry={() => { void selectedQ.refetch(); }} />}
    {!jobsQ.isLoading && !jobsQ.isError && choices.length === 0 && <p className="mt-2 text-xs text-slate-500">No matching jobs are available in this route's branch. Clear the search or review job and route ownership.</p>}
    <p className="mt-2 text-xs text-slate-500">Delivery proof is reviewed in Proof of Delivery; editing a route stop does not verify evidence.</p>
    <div className="mt-4 flex justify-end gap-2"><button type="button" className="btn-ghost" onClick={onClose} disabled={saving}>Cancel</button><button type="submit" className="btn-primary" disabled={saving || !validJob || (Boolean(jobId) && (selectedQ.isLoading || selectedQ.isError))}>{saving ? "Saving…" : "Save stop"}</button></div>
  </form></div>;
}

function Field({ label, children }: { label: string; children: ReactNode }) { return <label><span className="mb-2 block text-xs font-bold uppercase tracking-[0.16em] text-slate-500">{label}</span>{children}</label>; }
function Grid({ title, rows, columns }: { title: string; rows: AnyRecord[]; columns: string[] }) { return <section className="fc-neumo mt-6 p-4"><h3 className="section-title">{title}</h3>{!rows.length ? <p className="mt-3 text-sm text-slate-500">No records yet.</p> : <div className="mt-3 overflow-x-auto"><table className="w-full min-w-[680px] text-left text-sm"><thead className="text-xs uppercase tracking-[0.16em] text-slate-500"><tr>{columns.map((column) => <th key={column} className="px-3 py-2">{labelize(column)}</th>)}</tr></thead><tbody className="divide-y divide-slate-100">{rows.slice(0, 10).map((row, index) => <tr key={String(row.id || index)}>{columns.map((column) => <td key={column} className="px-3 py-2 text-slate-600">{String(row[column] ?? "--")}</td>)}</tr>)}</tbody></table></div>}</section>; }
