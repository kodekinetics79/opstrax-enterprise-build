import { FormEvent, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Download, Plus, Route, Sparkles, X } from "lucide-react";
import { AiInsightCard, DataTable, ErrorState, LoadingState, PageHeader, RiskBadge, StatusBadge, exportCsv, labelize } from "@/components/ui";
import { ClayStat } from "@/components/console";
import { useRouteDetail, useRoutes, useRouteSummary } from "@/hooks/useBatch2";
import { routesApi } from "@/services/routesApi";
import type { AnyRecord } from "@/types";

const routeFields = [["routeCode","Route Code"],["routeName","Route Name"],["region","Region / Zone"],["plannedStart","Planned Start"],["plannedEnd","Planned End"],["assignedDriverId","Assigned Driver ID"],["assignedVehicleId","Assigned Vehicle ID"],["routeType","Route Type"],["optimizationMode","Optimization Mode"],["notes","Notes"]];
const stopFields = [["stopSequence","Sequence"],["jobId","Job ID"],["customerId","Customer ID"],["stopType","Stop Type"],["address","Address"],["latitude","Latitude"],["longitude","Longitude"],["timeWindowStart","Window Start"],["timeWindowEnd","Window End"],["status","Status"],["proofStatus","Proof Status"],["notes","Notes"]];

export function RoutePlanningPage() {
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const [editing, setEditing] = useState<AnyRecord | null>(null);
  const [stopEditing, setStopEditing] = useState<AnyRecord | null>(null);
  const [query, setQuery] = useState("");
  const [status, setStatus] = useState("All");
  const routes = useRoutes();
  const summary = useRouteSummary();
  const detail = useRouteDetail(selected?.id as string | number | undefined);
  const qc = useQueryClient();

  const save = useMutation({ mutationFn: (payload: AnyRecord) => payload.id ? routesApi.update(String(payload.id), payload) : routesApi.create(payload), onSuccess: async () => { setEditing(null); await qc.invalidateQueries({ queryKey: ["routes"] }); } });
  const saveStop = useMutation({ mutationFn: (payload: AnyRecord) => routesApi.createStop(String(selected?.id), payload), onSuccess: async () => { setStopEditing(null); await qc.invalidateQueries({ queryKey: ["routes", "detail", selected?.id] }); } });
  const optimize = useMutation({ mutationFn: (id: string | number) => routesApi.optimizePreview(id), onSuccess: async () => { await qc.invalidateQueries({ queryKey: ["routes", "detail", selected?.id] }); } });

  const rows = useMemo(() => (routes.data || []).filter((row) => {
    const qLower = query.toLowerCase();
    const matchesQuery = !query || 
      String(row.routeCode || row.routeName || row.name || "").toLowerCase().includes(qLower) ||
      String(row.region || row.driverName || row.vehicleCode || "").toLowerCase().includes(qLower);
    const matchesStatus = status === "All" || String(row.status) === status;
    return matchesQuery && matchesStatus;
  }), [query, routes.data, status]);
  if (routes.isLoading) return <LoadingState />;
  if (routes.isError) return <ErrorState message={(routes.error as Error)?.message} />;
  const s = summary.data || {};

  return <div className="fleet-console space-y-3">
    <PageHeader eyebrow="Route Planning" title="Route Plans" description="Plan routes, manage stops, preview optimization and inspect SLA risk by stop." actions={<><button className="btn-primary" onClick={() => setEditing({ status: "Planned", routeType: "Delivery", optimizationMode: "Balanced" })}><Plus className="h-4 w-4" /> Create Route</button><button className="btn-ghost" onClick={() => exportRoutes(rows)}><Download className="h-4 w-4" /> Export Route Plan</button></>} />
    <div className="grid grid-cols-2 gap-3 md:grid-cols-5">
      {[["Total Routes Today","totalRoutesToday"],["Active Routes","activeRoutes"],["Planned Routes","plannedRoutes"],["Completed Routes","completedRoutes"],["Delayed Routes","delayedRoutes"],["Avg Stops","averageStopsPerRoute"],["Avg Route ETA","averageRouteEta"],["Efficiency","routeEfficiencyScore"],["High-Risk","highRiskRoutes"],["Cost Estimate","routeCostEstimate"]].map(([label,key], i) => <ClayStat key={key} Icon={Route} tone={["fc-clay-teal","fc-clay-emerald","fc-clay-sky","fc-clay-amber","fc-clay-red"][i % 5]} iconCls={["text-teal-700","text-emerald-700","text-sky-700","text-amber-700","text-rose-700"][i % 5]} label={label} value={String(s[key] ?? 0)} alert={/Risk|Delayed/.test(label)} />)}
    </div>
    <div className="panel flex flex-col gap-3 p-4 lg:flex-row"><input className="field lg:max-w-md" value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search routes, regions, driver, vehicle..." /><select className="field lg:max-w-[180px]" value={status} onChange={(e) => setStatus(e.target.value)}><option>All</option><option>Planned</option><option>Active</option><option>Completed</option><option>Delayed</option><option>At Risk</option></select></div>
    <DataTable rows={rows} columns={["routeCode", "routeName", "region", "driverName", "vehicleCode", "stops", "plannedStart", "plannedEnd", "status", "estimatedDurationMinutes", "estimatedDistance", "efficiencyScore", "slaRisk", "recommendedAction"]} onSelect={setSelected} />
    <RouteDrawer detail={detail.data} onClose={() => setSelected(null)} onEdit={(r) => setEditing(r)} onAddStop={() => setStopEditing({ stopType: "Drop-off", status: "Pending", proofStatus: "Pending" })} onOptimize={(id) => optimize.mutate(id)} optimizeResult={optimize.data} />
    {editing ? <Modal title={editing.id ? "Edit Route" : "Create Route"} fields={routeFields} initial={editing} saving={save.isPending} onClose={() => setEditing(null)} onSave={(p) => save.mutate(p)} /> : null}
    {stopEditing ? <Modal title="Add Route Stop" fields={stopFields} initial={stopEditing} saving={saveStop.isPending} onClose={() => setStopEditing(null)} onSave={(p) => saveStop.mutate(p)} /> : null}
  </div>;
}

function RouteDrawer({ detail, onClose, onEdit, onAddStop, onOptimize, optimizeResult }: { detail?: AnyRecord; onClose: () => void; onEdit: (record: AnyRecord) => void; onAddStop: () => void; onOptimize: (id: string | number) => void; optimizeResult?: AnyRecord }) {
  const record = detail?.record as AnyRecord | undefined;
  if (!record) return null;
  const stops = (detail?.stops as AnyRecord[]) || [];
  return <div className="fixed inset-0 z-50 flex justify-end bg-slate-900/30 backdrop-blur-sm"><aside className="fleet-console h-full w-full max-w-4xl overflow-y-auto border-l border-slate-200 p-6"><button className="float-right icon-btn" onClick={onClose}><X /></button><p className="section-title">Route Detail</p><h2 className="mt-3 text-2xl font-black text-slate-950">{String(record.routeName || record.name)}</h2><div className="mt-4 flex flex-wrap gap-2"><StatusBadge status={record.status} /><RiskBadge risk={record.slaRisk} /><span className="badge">Efficiency {String(record.efficiencyScore)}%</span></div><div className="mt-5 flex flex-wrap gap-3"><button className="btn-primary" onClick={() => onEdit(record)}>Edit Route</button><button className="btn-ghost" onClick={onAddStop}><Plus className="h-4 w-4" /> Add Stop</button><button className="btn-ghost" onClick={() => onOptimize(String(record.id))}><Sparkles className="h-4 w-4" /> Optimize Preview</button><Link to="/trips" className="btn-ghost">Trips</Link><Link to="/jobs" className="btn-ghost">Jobs board</Link></div><div className="mt-6 grid gap-4 lg:grid-cols-[1fr_320px]"><section className="deck-inset min-h-[300px] rounded-2xl p-4"><h3 className="section-title">Stop sequence</h3>{stops.length === 0 ? <p className="mt-3 text-sm text-slate-500">No stops added yet — add the first stop to sequence this route.</p> : <ol className="mt-3 space-y-2">{stops.slice(0, 8).map((stop, i) => <li key={String(stop.id ?? i)} className="deck-alert flex items-center gap-3 px-3 py-2.5"><span className="grid h-6 w-6 shrink-0 place-items-center rounded-full bg-teal-100 text-[11px] font-black text-teal-800 tabular-nums">{String(stop.stopSequence ?? i + 1)}</span><span className="min-w-0 flex-1"><span className="block truncate text-[12.5px] font-bold text-slate-800">{String(stop.customerName ?? stop.address ?? "Stop")}</span><span className="block truncate text-[10.5px] font-medium text-slate-400">{String(stop.stopType ?? "")}{stop.eta ? ` · ETA ${String(stop.eta)}` : ""}</span></span><StatusBadge status={stop.status} /></li>)}</ol>}</section><section className="panel p-4"><h3 className="section-title">Route Cost / ETA Summary</h3>{["region","plannedStart","plannedEnd","estimatedDistance","estimatedDurationMinutes","costEstimate","optimizationMode"].map(k=><p key={k} className="mt-2 text-sm text-slate-700"><span className="text-slate-500">{labelize(k)}:</span> {String(record[k] ?? "--")}</p>)}{optimizeResult ? <AiInsightCard insight={{title:"Optimization Preview",body:`Efficiency ${optimizeResult.efficiencyScore}%, saves ${optimizeResult.estimatedSavingsMinutes} minutes, reduces leakage ${optimizeResult.costLeakageReduction}.`}} /> : null}</section></div><Grid title="Stops / SLA Risk by Stop" rows={stops} columns={["stopSequence","stopType","customerName","jobCode","address","timeWindowStart","timeWindowEnd","status","proofStatus","eta"]} /><Grid title="Route Recommendations" rows={(detail?.recommendations as AnyRecord[]) || []} columns={["title","body","score","status"]} /><Grid title="Audit Trail" rows={(detail?.auditTrail as AnyRecord[]) || []} columns={["actionName","actorName","createdAt"]} /></aside></div>;
}

function Modal({ title, fields, initial, saving, onClose, onSave }: { title: string; fields: string[][]; initial: AnyRecord; saving: boolean; onClose: () => void; onSave: (payload: AnyRecord) => void }) {
  const [form, setForm] = useState<AnyRecord>(initial);
  const submit = (e: FormEvent) => { e.preventDefault(); onSave(form); };
  return <div className="fixed inset-0 z-[60] grid place-items-center bg-black/60 p-4"><form className="panel max-h-[90vh] w-full max-w-3xl overflow-y-auto p-6" onSubmit={submit}><div className="flex justify-between"><h2 className="text-2xl font-semibold text-slate-900">{title}</h2><button type="button" className="icon-btn" onClick={onClose}><X /></button></div><div className="mt-6 grid gap-4 md:grid-cols-2">{fields.map(([key,label])=><label key={key}><span className="mb-2 block text-xs font-bold uppercase tracking-[0.16em] text-slate-500">{label}</span><input className="field" value={String(form[key] ?? "")} onChange={(e)=>setForm(x=>({...x,[key]:e.target.value}))} required={["routeCode","routeName","address","stopSequence"].includes(key)} /></label>)}</div><div className="mt-6 flex justify-end gap-3"><button type="button" className="btn-ghost" onClick={onClose}>Cancel</button><button type="submit" className="btn-primary" disabled={saving}>Save</button></div></form></div>;
}

function Grid({ title, rows, columns }: { title: string; rows: AnyRecord[]; columns: string[] }) {
  return <section className="fc-neumo mt-6 p-4"><h3 className="section-title">{title}</h3>{!rows.length ? <p className="mt-3 text-sm text-slate-500">No records yet.</p> : <div className="mt-3 overflow-x-auto"><table className="w-full min-w-[680px] text-left text-sm"><thead className="text-xs uppercase tracking-[0.16em] text-slate-500"><tr>{columns.map(c=><th key={c} className="px-3 py-2">{labelize(c)}</th>)}</tr></thead><tbody className="divide-y divide-slate-100">{rows.slice(0,10).map((r,i)=><tr key={String(r.id||i)}>{columns.map(c=><td key={c} className="px-3 py-2 text-slate-600">{String(r[c] ?? "--")}</td>)}</tr>)}</tbody></table></div>}</section>;
}

function exportRoutes(rows: AnyRecord[]) {
  exportCsv("route-plans", rows);
}
