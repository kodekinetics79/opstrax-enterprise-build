import { useEffect, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useLocation, useNavigate } from "react-router-dom";
import { ArrowRight, Boxes, ClipboardList, Gauge, ShieldAlert, Sparkles, Truck, Wrench } from "lucide-react";
import { LoadingState, ErrorState, KpiCard, EmptyState, exportCsv, labelize } from "@/components/ui";
import { vehiclesApi } from "@/services/vehiclesApi";
import { scopeRowsForSession } from "@/auth/accessScope";
import { useAuth } from "@/hooks/useAuth";
import type { AnyRecord } from "@/types";
import { VehiclesPage as VehiclesRosterPage } from "@/pages/VehiclesPage";

type VehicleSection = "overview" | "roster" | "planning" | "health" | "records";

const SECTIONS: Array<{ key: VehicleSection; label: string; description: string }> = [
  { key: "overview", label: "Overview", description: "Fleet posture and quick links" },
  { key: "roster", label: "Roster", description: "Search, edit, assign and export" },
  { key: "planning", label: "Planning", description: "CapEx, replacement and gaps" },
  { key: "health", label: "Health", description: "Readiness, device and camera state" },
  { key: "records", label: "Records", description: "Maintenance, compliance and audit" },
];

function readSection(pathname: string): VehicleSection {
  const section = pathname.split("/").filter(Boolean)[1];
  if (section === "roster" || section === "planning" || section === "health" || section === "records") return section;
  return "overview";
}

const g = (row: AnyRecord, ...keys: string[]) => {
  for (const key of keys) if (row?.[key] != null && row[key] !== "") return row[key];
  return undefined;
};

const rowId = (row: AnyRecord) => String(row?.id ?? "");

const num = (v: unknown) => (Number.isFinite(Number(v)) ? Number(v) : 0);

function riskTier(row: AnyRecord): "High" | "Medium" | "Low" {
  const heat = String(g(row, "riskHeatScore", "risk_heat_score") ?? "");
  if (/high|critical/i.test(heat)) return "High";
  if (/medium|warning/i.test(heat)) return "Medium";
  if (/low/i.test(heat)) return "Low";
  const n = num(g(row, "riskScore", "risk_score"));
  return n >= 70 ? "High" : n >= 40 ? "Medium" : "Low";
}

export function VehiclesModulePage() {
  const navigate = useNavigate();
  const location = useLocation();
  const section = readSection(location.pathname);
  const { session } = useAuth();

  const list = useQuery({ queryKey: ["vehicles"], queryFn: vehiclesApi.list });
  const summary = useQuery({ queryKey: ["vehicles", "summary"], queryFn: vehiclesApi.summary });
  const planning = useQuery({ queryKey: ["vehicles", "planning-insights"], queryFn: vehiclesApi.planningInsights });

  const rows = useMemo(() => scopeRowsForSession("vehicles", list.data || [], session), [list.data, session]);
  const visibleSummary = (summary.data as AnyRecord) || {};
  const loading = list.isLoading || summary.isLoading || planning.isLoading;

  if (section === "overview" && loading) return <LoadingState />;
  if (list.isError) return <ErrorState message={list.error instanceof Error ? list.error.message : "Unable to load vehicles."} />;
  if (summary.isError) return <ErrorState message={summary.error instanceof Error ? summary.error.message : "Unable to load vehicle summary."} />;

  const available = rows.filter((row) => /available/i.test(String(g(row, "status")))).length;
  const atRisk = num(visibleSummary.atRisk ?? visibleSummary.at_risk) || rows.filter((row) => riskTier(row) === "High").length;
  const deviceEx = num(visibleSummary.deviceExceptions ?? visibleSummary.device_exceptions) ||
    rows.filter((row) => !/online/i.test(String(g(row, "deviceStatus", "device_status") ?? "Online")) || !/online/i.test(String(g(row, "cameraStatus", "camera_status") ?? "Online"))).length;
  const readiness = Math.round(num(visibleSummary.fleetReadinessScore ?? visibleSummary.fleet_readiness_score) || avg(rows, "fleetReadinessScore"));

  const shellBanner = (
    <header className="rounded-[22px] border border-slate-800 bg-gradient-to-br from-slate-950 via-slate-900 to-teal-950 px-6 py-5 text-white shadow-xl">
      <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
        <div className="min-w-0">
          <div className="inline-flex items-center gap-2 rounded-full border border-white/10 bg-white/10 px-3 py-1 text-[11px] font-semibold uppercase tracking-[0.18em] text-teal-200">
            <span className="h-1.5 w-1.5 rounded-full bg-teal-300" /> Fleet module
          </div>
          <h1 className="mt-3 text-3xl font-black tracking-tight text-white">Vehicles</h1>
          <p className="mt-2 max-w-3xl text-sm text-slate-300">
            Split into focused subviews so users can get to the right fleet task without scrolling through one oversized page.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <button type="button" onClick={() => exportCsv("vehicles", rows)} className="btn-ghost h-10 border-white/15 bg-white/5 text-white hover:bg-white/10">
            Export fleet
          </button>
          <button type="button" onClick={() => navigate("/vehicles/roster")} className="btn-primary h-10">
            Open roster <ArrowRight className="h-4 w-4" />
          </button>
        </div>
      </div>
    </header>
  );

  return (
    <div className="space-y-6 pb-10">
      {shellBanner}

      <nav className="sticky top-4 z-20 rounded-2xl border border-slate-200 bg-white/95 p-2 shadow-sm backdrop-blur">
        <div className="grid gap-1 sm:grid-cols-5">
          {SECTIONS.map((item) => (
            <button
              key={item.key}
              type="button"
              onClick={() => navigate(`/vehicles/${item.key}`)}
              className={`rounded-xl px-3 py-2.5 text-left transition ${
                section === item.key ? "bg-slate-900 text-white shadow-sm" : "hover:bg-slate-50"
              }`}
            >
              <div className="text-xs font-bold uppercase tracking-[0.14em]">{item.label}</div>
              <div className={`mt-0.5 text-[11px] ${section === item.key ? "text-slate-300" : "text-slate-500"}`}>{item.description}</div>
            </button>
          ))}
        </div>
      </nav>

      {section === "overview" && (
        <div className="space-y-6">
          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <KpiCard label="Fleet readiness" value={`${readiness}%`} icon={<Gauge className="h-4 w-4" />} />
            <KpiCard label="Available now" value={String(available)} icon={<Truck className="h-4 w-4" />} />
            <KpiCard label="At risk" value={String(atRisk)} status="Review" icon={<ShieldAlert className="h-4 w-4" />} />
            <KpiCard label="Device gaps" value={String(deviceEx)} status="Review" icon={<Boxes className="h-4 w-4" />} />
          </div>
          <div className="grid gap-4 lg:grid-cols-3">
            <ModuleCard title="Roster" body="Browse, search, edit, assign and export live fleet records." action="Open roster" onClick={() => navigate("/vehicles/roster")} icon={<ClipboardList className="h-5 w-5" />} />
            <ModuleCard title="Planning" body="See replacement pressure, operational gaps and CapEx priority." action="Open planning" onClick={() => navigate("/vehicles/planning")} icon={<Sparkles className="h-5 w-5" />} />
            <ModuleCard title="Health" body="Inspect device, camera, maintenance and compliance posture." action="Open health" onClick={() => navigate("/vehicles/health")} icon={<Wrench className="h-5 w-5" />} />
          </div>
          <div className="panel p-5">
            <div className="flex items-center justify-between">
              <div>
                <h2 className="text-lg font-semibold text-slate-900">Live fleet snapshot</h2>
                <p className="text-sm text-slate-500">A compact summary of the live data now driving the module.</p>
              </div>
              <button type="button" className="btn-ghost h-9" onClick={() => navigate("/vehicles/roster")}>Go to roster</button>
            </div>
            <div className="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
              {rows.slice(0, 4).map((row) => (
                <div key={String(row.id)} className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
                  <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Vehicle</p>
                  <p className="mt-1 text-base font-semibold text-slate-900">{String(g(row, "vehicleCode", "vehicle_code") ?? `Vehicle ${row.id}`)}</p>
                  <p className="mt-1 text-sm text-slate-500">{String(g(row, "make") ?? "")} {String(g(row, "model") ?? "")}</p>
                  <div className="mt-3 flex items-center justify-between text-xs text-slate-500">
                    <span>{String(g(row, "status") ?? "--")}</span>
                    <span>{String(g(row, "assignedDriver", "assigned_driver") ?? "Unassigned")}</span>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>
      )}

      {section === "roster" && <VehiclesRosterPage />}

      {section === "planning" && <PlanningView rows={rows} planning={planning.data as AnyRecord} />}
      {section === "health" && <HealthView rows={rows} />}
      {section === "records" && <RecordsView rows={rows} />}
    </div>
  );
}

function ModuleCard({ title, body, action, onClick, icon }: { title: string; body: string; action: string; onClick: () => void; icon: React.ReactNode }) {
  return (
    <button type="button" onClick={onClick} className="group rounded-2xl border border-slate-200 bg-white p-5 text-left shadow-sm transition hover:-translate-y-0.5 hover:border-slate-300 hover:shadow-md">
      <div className="flex items-center justify-between">
        <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-slate-50 text-slate-500">{icon}</div>
        <ArrowRight className="h-4 w-4 text-slate-300 transition group-hover:translate-x-0.5" />
      </div>
      <h3 className="mt-4 text-base font-semibold text-slate-900">{title}</h3>
      <p className="mt-2 text-sm text-slate-500">{body}</p>
      <p className="mt-4 text-xs font-bold uppercase tracking-[0.14em] text-teal-600">{action}</p>
    </button>
  );
}

function PlanningView({ rows, planning }: { rows: AnyRecord[]; planning?: AnyRecord }) {
  const forecast = ((planning?.replacementForecast as AnyRecord[]) || []).slice(0, 6);
  const gaps = ((planning?.operationalGaps as AnyRecord[]) || []).slice(0, 6);
  const customerBusiness = ((planning?.customerBusiness as AnyRecord[]) || []).slice(0, 4);
  const routeBusiness = ((planning?.routeBusiness as AnyRecord[]) || []).slice(0, 4);

  return (
    <div className="space-y-5">
      <section className="panel p-5">
        <h2 className="text-lg font-semibold text-slate-900">Planning view</h2>
        <p className="mt-1 text-sm text-slate-500">Replacement pressure and operational gaps, without the rest of the roster crowding the screen.</p>
      </section>
      <div className="grid gap-4 lg:grid-cols-[1.2fr_0.8fr]">
        <SimpleListCard title="Replacement priority" rows={forecast} fields={["vehicleCode", "replacementWindow", "capexPriorityScore"]} />
        <SimpleListCard title="Operational gaps" rows={gaps} fields={["gapName", "affectedRecords", "visibility"]} />
      </div>
      <div className="grid gap-4 lg:grid-cols-2">
        <SimpleListCard title="Customer business pressure" rows={customerBusiness} fields={["customerName", "planningSignal", "activeJobs"]} />
        <SimpleListCard title="Route pressure" rows={routeBusiness} fields={["routeCode", "planningSignal", "activeJobs"]} />
      </div>
      {!forecast.length && !gaps.length && !customerBusiness.length && !routeBusiness.length ? (
        <EmptyState title="No planning data" subtitle="The backend returned no planning insight rows." />
      ) : null}
      <p className="text-xs text-slate-500">{rows.length} live vehicles feed this view.</p>
    </div>
  );
}

function HealthView({ rows }: { rows: AnyRecord[] }) {
  const risky = rows.filter((row) => riskTier(row) === "High" || !/online/i.test(String(g(row, "deviceStatus", "device_status") ?? "Online")) || !/online/i.test(String(g(row, "cameraStatus", "camera_status") ?? "Online")));
  return (
    <div className="space-y-5">
      <section className="panel p-5">
        <h2 className="text-lg font-semibold text-slate-900">Health view</h2>
        <p className="mt-1 text-sm text-slate-500">A tighter operational screen for readiness, device and camera exceptions.</p>
      </section>
      <div className="grid gap-4 xl:grid-cols-4">
        {[
          { label: "Ready", value: rows.filter((row) => /available/i.test(String(g(row, "status")))).length },
          { label: "Risk", value: rows.filter((row) => riskTier(row) === "High").length },
          { label: "Device gaps", value: rows.filter((row) => !/online/i.test(String(g(row, "deviceStatus", "device_status") ?? "Online"))).length },
          { label: "Camera gaps", value: rows.filter((row) => !/online/i.test(String(g(row, "cameraStatus", "camera_status") ?? "Online"))).length },
        ].map((k) => <KpiCard key={k.label} label={k.label} value={String(k.value)} />)}
      </div>
      <SimpleListCard title="Vehicles needing attention" rows={risky.slice(0, 8)} fields={["vehicleCode", "status", "deviceStatus", "cameraStatus", "riskHeatScore"]} />
    </div>
  );
}

function RecordsView({ rows }: { rows: AnyRecord[] }) {
  const [selectedId, setSelectedId] = useState<string | null>(() => (rows[0] ? rowId(rows[0]) : null));
  const detail = useQuery({
    queryKey: ["vehicles", "detail", selectedId],
    queryFn: () => vehiclesApi.detail(String(selectedId)),
    enabled: selectedId != null,
  });

  useEffect(() => {
    if (rows.length && selectedId == null) setSelectedId(rowId(rows[0]));
  }, [rows, selectedId]);

  const record = (detail.data?.record as AnyRecord) || null;
  const sections = [
    { title: "Maintenance", rows: detail.data?.maintenance as AnyRecord[] | undefined, fields: ["title", "category", "status", "dueDate"] },
    { title: "Compliance", rows: detail.data?.compliance as AnyRecord[] | undefined, fields: ["documentName", "documentType", "status", "expiryDate"] },
    { title: "Documents", rows: detail.data?.documents as AnyRecord[] | undefined, fields: ["documentName", "documentType", "status", "expiryDate"] },
    { title: "Audit trail", rows: detail.data?.auditTrail as AnyRecord[] | undefined, fields: ["actionName", "actorName", "createdAt"] },
  ];

  return (
    <div className="space-y-5">
      <section className="panel p-5">
        <h2 className="text-lg font-semibold text-slate-900">Records view</h2>
        <p className="mt-1 text-sm text-slate-500">Vehicle records are split out from the roster so the user can inspect one record at a time.</p>
      </section>
      <div className="grid gap-5 xl:grid-cols-[0.9fr_1.1fr]">
        <div className="panel p-4">
          <h3 className="text-sm font-semibold text-slate-800">Vehicle selector</h3>
          <div className="mt-3 space-y-2">
            {rows.slice(0, 12).map((row) => (
              <button
                key={rowId(row)}
                type="button"
                onClick={() => setSelectedId(rowId(row))}
                className={`w-full rounded-xl border px-3 py-2.5 text-left transition ${selectedId === rowId(row) ? "border-teal-300 bg-teal-50" : "border-slate-200 bg-white hover:bg-slate-50"}`}
              >
                <div className="flex items-center justify-between">
                  <span className="font-semibold text-slate-900">{String(g(row, "vehicleCode", "vehicle_code") ?? `Vehicle ${rowId(row)}`)}</span>
                  <span className="text-xs text-slate-500">{String(g(row, "status") ?? "--")}</span>
                </div>
                <div className="mt-1 text-xs text-slate-500">{String(g(row, "make") ?? "")} {String(g(row, "model") ?? "")}</div>
              </button>
            ))}
          </div>
        </div>
        <div className="space-y-4">
          {record ? (
            <div className="grid gap-4 md:grid-cols-2">
              <KpiCard label="Vehicle" value={String(g(record, "vehicleCode", "vehicle_code") ?? `Vehicle ${record.id}`)} />
              <KpiCard label="Status" value={String(g(record, "status") ?? "--")} />
              <KpiCard label="Driver" value={String(g(record, "assignedDriver", "assigned_driver") ?? "Unassigned")} />
              <KpiCard label="Odometer" value={`${num(g(record, "odometerMiles", "odometer_miles")).toLocaleString()} mi`} />
            </div>
          ) : null}
          {sections.map((section) => (
            <SimpleListCard key={section.title} title={section.title} rows={section.rows || []} fields={section.fields} />
          ))}
        </div>
      </div>
      {detail.isLoading ? <LoadingState /> : null}
    </div>
  );
}

function SimpleListCard({ title, rows, fields }: { title: string; rows: AnyRecord[]; fields: string[] }) {
  return (
    <section className="panel p-4">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold text-slate-800">{title}</h3>
        <span className="text-xs text-slate-500">{rows.length} rows</span>
      </div>
      {!rows.length ? (
        <p className="mt-3 rounded-xl border border-dashed border-slate-200 px-3 py-3 text-sm text-slate-400">No linked records.</p>
      ) : (
        <div className="mt-3 overflow-hidden rounded-xl border border-slate-200">
          <table className="w-full text-left text-xs">
            <thead className="bg-slate-50 text-[10px] uppercase tracking-wide text-slate-400">
              <tr>{fields.map((field) => <th key={field} className="px-3 py-2 font-semibold">{labelize(field)}</th>)}</tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {rows.slice(0, 6).map((row, index) => (
                <tr key={String(row.id ?? index)} className="text-slate-600">
                  {fields.map((field) => <td key={field} className="px-3 py-2">{fmt(row[field])}</td>)}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

function fmt(v: unknown) {
  if (v == null || v === "") return "—";
  const s = String(v);
  if (/^\d{4}-\d{2}-\d{2}T/.test(s)) return s.slice(0, 10);
  return s;
}

function avg(rows: AnyRecord[], key: string) {
  if (!rows.length) return 0;
  return rows.reduce((t, r) => t + num(g(r, key, key.replace(/([A-Z])/g, "_$1").toLowerCase())), 0) / rows.length;
}
