import { FormEvent, useMemo, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Download, MapPinned, Plus, Route, Sparkles, X } from "lucide-react";
import { AiInsightCard, DataTable, KpiCard, LoadingState, PageHeader, RiskBadge, StatusBadge, labelize } from "@/components/ui";
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
  const s = summary.data || {};

  return <div className="space-y-6">
    <PageHeader eyebrow="Route Planning" title="Route intelligence and stop orchestration" description="Plan routes, manage stops, preview optimization, inspect SLA risk by stop and export route plans without paid map APIs." actions={<><button className="btn-primary" onClick={() => setEditing({ status: "Planned", routeType: "Delivery", optimizationMode: "Balanced" })}><Plus className="h-4 w-4" /> Create Route</button><button className="btn-ghost"><Download className="h-4 w-4" /> Export Route Plan</button></>} />
    <div className="grid gap-4 md:grid-cols-5">
      {[["Total Routes Today","totalRoutesToday"],["Active Routes","activeRoutes"],["Planned Routes","plannedRoutes"],["Completed Routes","completedRoutes"],["Delayed Routes","delayedRoutes"],["Avg Stops","averageStopsPerRoute"],["Avg Route ETA","averageRouteEta"],["Efficiency","routeEfficiencyScore"],["High-Risk","highRiskRoutes"],["Cost Estimate","routeCostEstimate"]].map(([label,key]) => <KpiCard key={key} label={label} value={String(s[key] ?? 0)} icon={<Route />} status={/Risk|Delayed/.test(label) ? "Review" : "Active"} />)}
    </div>
    <div className="panel flex flex-col gap-3 p-4 lg:flex-row"><input className="field lg:max-w-md" value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search routes, regions, driver, vehicle..." /><select className="field lg:max-w-[180px]" value={status} onChange={(e) => setStatus(e.target.value)}><option>All</option><option>Planned</option><option>Active</option><option>Completed</option><option>Delayed</option><option>At Risk</option></select><span className="badge">Route replay placeholder</span><span className="badge">Cost leakage by route</span></div>
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
  return <div className="fixed inset-0 z-50 flex justify-end bg-black/50 backdrop-blur-sm"><aside className="h-full w-full max-w-4xl overflow-y-auto border-l border-white/10 bg-slate-950 p-6"><button className="float-right icon-btn" onClick={onClose}><X /></button><p className="section-title text-teal-300">Route Detail</p><h2 className="mt-3 text-2xl font-semibold text-white">{String(record.routeName || record.name)}</h2><div className="mt-4 flex flex-wrap gap-2"><StatusBadge status={record.status} /><RiskBadge risk={record.slaRisk} /><span className="badge">Efficiency {String(record.efficiencyScore)}%</span></div><div className="mt-5 flex flex-wrap gap-3"><button className="btn-primary" onClick={() => onEdit(record)}>Edit Route</button><button className="btn-ghost" onClick={onAddStop}><Plus className="h-4 w-4" /> Add Stop</button><button className="btn-ghost" onClick={() => onOptimize(String(record.id))}><Sparkles className="h-4 w-4" /> Optimize Preview</button></div><div className="mt-6 grid gap-4 lg:grid-cols-[1fr_320px]"><section className="map-surface min-h-[300px]"><svg className="absolute inset-0 h-full w-full"><polyline points="60,220 180,120 300,180 440,80 570,190" fill="none" stroke="rgba(45,212,191,.8)" strokeWidth="3" strokeDasharray="8 8" /></svg>{stops.slice(0,6).map((_,i)=><div key={i} className="map-pin" style={{left:`${12+i*15}%`,top:`${70-(i%3)*20}%`}}><span /></div>)}</section><section className="panel p-4"><h3 className="section-title">Route Cost / ETA Summary</h3>{["region","plannedStart","plannedEnd","estimatedDistance","estimatedDurationMinutes","costEstimate","optimizationMode"].map(k=><p key={k} className="mt-2 text-sm text-slate-300"><span className="text-slate-500">{labelize(k)}:</span> {String(record[k] ?? "--")}</p>)}{optimizeResult ? <AiInsightCard insight={{title:"Optimization Preview",body:`Efficiency ${optimizeResult.efficiencyScore}%, saves ${optimizeResult.estimatedSavingsMinutes} minutes, reduces leakage ${optimizeResult.costLeakageReduction}.`}} /> : null}</section></div><Grid title="Stops / SLA Risk by Stop" rows={stops} columns={["stopSequence","stopType","customerName","jobCode","address","timeWindowStart","timeWindowEnd","status","proofStatus","eta"]} /><Grid title="AI Route Recommendations" rows={(detail?.recommendations as AnyRecord[]) || []} columns={["title","body","score","status"]} /><Grid title="Audit Trail" rows={(detail?.auditTrail as AnyRecord[]) || []} columns={["actionName","actorName","createdAt"]} /></aside></div>;
}

function Modal({ title, fields, initial, saving, onClose, onSave }: { title: string; fields: string[][]; initial: AnyRecord; saving: boolean; onClose: () => void; onSave: (payload: AnyRecord) => void }) {
  const [form, setForm] = useState<AnyRecord>(initial);
  const submit = (e: FormEvent) => { e.preventDefault(); onSave(form); };
  return <div className="fixed inset-0 z-[60] grid place-items-center bg-black/60 p-4"><form className="panel max-h-[90vh] w-full max-w-3xl overflow-y-auto p-6" onSubmit={submit}><div className="flex justify-between"><h2 className="text-2xl font-semibold text-white">{title}</h2><button type="button" className="icon-btn" onClick={onClose}><X /></button></div><div className="mt-6 grid gap-4 md:grid-cols-2">{fields.map(([key,label])=><label key={key}><span className="mb-2 block text-xs font-bold uppercase tracking-[0.16em] text-slate-500">{label}</span><input className="field" value={String(form[key] ?? "")} onChange={(e)=>setForm(x=>({...x,[key]:e.target.value}))} required={["routeCode","routeName","address","stopSequence"].includes(key)} /></label>)}</div><div className="mt-6 flex justify-end gap-3"><button type="button" className="btn-ghost" onClick={onClose}>Cancel</button><button className="btn-primary" disabled={saving}>Save</button></div></form></div>;
}

function Grid({ title, rows, columns }: { title: string; rows: AnyRecord[]; columns: string[] }) {
  return <section className="mt-6 rounded-2xl border border-white/10 bg-white/[0.03] p-4"><h3 className="section-title">{title}</h3>{!rows.length ? <p className="mt-3 text-sm text-slate-500">No records yet.</p> : <div className="mt-3 overflow-x-auto"><table className="w-full min-w-[680px] text-left text-sm"><thead className="text-xs uppercase tracking-[0.16em] text-slate-500"><tr>{columns.map(c=><th key={c} className="px-3 py-2">{labelize(c)}</th>)}</tr></thead><tbody className="divide-y divide-white/10">{rows.slice(0,10).map((r,i)=><tr key={String(r.id||i)}>{columns.map(c=><td key={c} className="px-3 py-2 text-slate-300">{String(r[c] ?? "--")}</td>)}</tr>)}</tbody></table></div>}</section>;
}
