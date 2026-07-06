import { useEffect, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useLocation, useNavigate } from "react-router-dom";
import { ArrowRight, Boxes, ClipboardList, Gauge, ShieldAlert, Sparkles, Truck, Wrench } from "lucide-react";
import { LoadingState, ErrorState, KpiCard, EmptyState, DataTable, StatusBadge } from "@/components/ui";
import { EntityImportExport } from "@/components/EntityImportExport";
import { vehiclesApi } from "@/services/vehiclesApi";
import { scopeRowsForSession } from "@/auth/accessScope";
import { useAuth } from "@/hooks/useAuth";
import { useHasPermission } from "@/hooks/usePermission";
import type { AnyRecord } from "@/types";
import { VehiclesPage as VehiclesRosterPage } from "@/pages/VehiclesPage";

const VEHICLE_IMPORT_EXPORT = {
  entity: "vehicles",
  columns: ["vehicleCode", "type", "make", "model", "year", "odometerMiles", "vin", "plateNumber", "status"],
  requiredColumns: ["vehicleCode"],
  templateEndpoint: "/api/vehicles/import-template",
  exportEndpoint: "/api/vehicles/export",
  importPreview: vehiclesApi.importPreview,
  importCommit: vehiclesApi.importCommit,
  invalidateKey: "vehicles",
} as const;

type VehicleSection = "overview" | "roster" | "planning" | "health" | "records";

const SECTIONS: Array<{ key: VehicleSection; label: string; description: string }> = [
  { key: "overview", label: "Overview", description: "Fleet posture and quick links" },
  { key: "roster", label: "Roster", description: "Search, edit, assign and export" },
  { key: "planning", label: "Planning", description: "CapEx, replacement and gaps" },
  { key: "health", label: "Health", description: "Readiness, device and camera state" },
  { key: "records", label: "Records", description: "Maintenance, compliance and audit" },
];

const RELATED_ENTITIES = [
  { label: "Drivers", route: "/drivers", note: "Open the assigned operator record" },
  { label: "Jobs", route: "/jobs", note: "See live work tied to the unit" },
  { label: "Maintenance", route: "/maintenance", note: "Inspect service and repair work" },
  { label: "Documents", route: "/documents", note: "Review compliance and asset docs" },
  { label: "Work Orders", route: "/work-orders", note: "Jump to open repair actions" },
  { label: "Audit Logs", route: "/audit-logs", note: "Trace changes and events" },
] as const;

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
  const hasPermission = useHasPermission();

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
    <header className="fc-rail relative px-6 py-4">
      <div className="relative flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
        <div className="min-w-0">
          <span className="section-title inline-flex items-center gap-2">
            <Truck className="h-3.5 w-3.5 text-teal-700" /> Fleet · Master Data
          </span>
          <h1 className="mt-1 text-[26px] font-black leading-none tracking-tight text-slate-950">Vehicles</h1>
          <p className="mt-1.5 text-[12.5px] font-medium text-slate-500">
            <span className="font-bold text-slate-700 tabular-nums">{rows.length}</span> units in the live registry ·{" "}
            <span className="font-bold text-emerald-600 tabular-nums">{available}</span> available ·{" "}
            <span className="font-bold text-rose-600 tabular-nums">{atRisk}</span> need attention
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <EntityImportExport
            config={VEHICLE_IMPORT_EXPORT}
            canImport={hasPermission("fleet:manage")}
            canExport={hasPermission("vehicles:view")}
          />
          <button type="button" onClick={() => navigate("/vehicles/roster")} className="btn-primary h-10">
            Open roster <ArrowRight className="h-4 w-4" />
          </button>
        </div>
      </div>
    </header>
  );

  return (
    <div className="fleet-console space-y-3 pb-6">
      {shellBanner}

      <nav className="fc-neumo sticky top-4 z-20 p-2">
        <div className="grid gap-1 sm:grid-cols-5">
          {SECTIONS.map((item) => (
            <button
              key={item.key}
              type="button"
              onClick={() => navigate(`/vehicles/${item.key}`)}
              className={`rounded-xl px-3 py-2.5 text-left transition ${
                section === item.key ? "fc-seg-btn-active rounded-xl" : "hover:bg-white/60"
              }`}
            >
              <div className={`text-xs font-bold uppercase tracking-[0.14em] ${section === item.key ? "text-teal-800" : "text-slate-700"}`}>{item.label}</div>
              <div className="mt-0.5 text-[11px] text-slate-500">{item.description}</div>
            </button>
          ))}
        </div>
      </nav>

      {section === "overview" && (
        <div className="space-y-3">
          <div className="grid grid-cols-2 gap-3 xl:grid-cols-4">
            <OverviewClay Icon={Gauge}       tone="fc-clay-teal"    iconCls="text-teal-700"    label="Fleet readiness" value={`${readiness}%`} caption={`${rows.length} live units`} />
            <OverviewClay Icon={Truck}       tone="fc-clay-emerald" iconCls="text-emerald-700" label="Available now"   value={available}       caption="Ready for dispatch" />
            <OverviewClay Icon={ShieldAlert} tone="fc-clay-red"     iconCls="text-rose-700"    label="At risk"         value={atRisk}          caption="High risk or down" alert={atRisk > 0} />
            <OverviewClay Icon={Boxes}       tone="fc-clay-amber"   iconCls="text-amber-700"   label="Device gaps"     value={deviceEx}        caption="Telematics blind spots" alert={deviceEx > 0} />
          </div>
          <div className="grid gap-3 lg:grid-cols-3">
            <ModuleCard title="Roster" body="Browse, search, edit, assign and export live fleet records." action="Open roster" onClick={() => navigate("/vehicles/roster")} icon={<ClipboardList className="h-5 w-5" />} />
            <ModuleCard title="Planning" body="Replacement pressure, operational gaps and CapEx priority computed from the live fleet." action="Open planning" onClick={() => navigate("/vehicles/planning")} icon={<Sparkles className="h-5 w-5" />} />
            <ModuleCard title="Health" body="Device, camera, maintenance and compliance posture per unit." action="Open health" onClick={() => navigate("/vehicles/health")} icon={<Wrench className="h-5 w-5" />} />
          </div>
          <div className="grid gap-3 xl:grid-cols-[1.1fr_0.9fr]">
            <section className="fc-neumo p-4">
              <div className="flex items-center justify-between">
                <div>
                  <h2 className="text-base font-black text-slate-900">Latest units</h2>
                  <p className="text-xs font-medium text-slate-500">Most recent records from the live registry.</p>
                </div>
                <button type="button" className="btn-ghost h-9" onClick={() => navigate("/vehicles/roster")}>Go to roster</button>
              </div>
              <div className="mt-3 grid gap-2.5 md:grid-cols-2">
                {rows.slice(0, 4).map((row) => (
                  <div key={String(row.id)} className="deck-inset rounded-xl p-3.5">
                    <p className="text-base font-bold text-slate-900">{String(g(row, "vehicleCode", "vehicle_code") ?? `Vehicle ${row.id}`)}</p>
                    <p className="mt-0.5 text-xs text-slate-500">{[g(row, "make"), g(row, "model")].filter(Boolean).join(" ") || String(g(row, "type") ?? "—")}</p>
                    <div className="mt-2.5 flex items-center justify-between text-xs text-slate-500">
                      <StatusBadge status={String(g(row, "status") ?? "--")} />
                      <span className="truncate pl-2">{String(g(row, "assignedDriver", "assigned_driver") ?? "Unassigned")}</span>
                    </div>
                  </div>
                ))}
                {rows.length === 0 && <p className="deck-inset col-span-full rounded-xl px-3 py-4 text-sm text-slate-400">No vehicles in the registry yet — import a CSV or add one from the roster.</p>}
              </div>
            </section>
            <section className="fc-neumo p-4">
              <div className="flex flex-wrap items-start justify-between gap-2">
                <div>
                  <h2 className="text-base font-black text-slate-900">Connected records</h2>
                  <p className="text-xs font-medium text-slate-500">Jump to the records tied to your fleet.</p>
                </div>
              </div>
              <div className="mt-3 grid gap-2 sm:grid-cols-2">
                {RELATED_ENTITIES.map((item) => (
                  <button
                    key={item.label}
                    type="button"
                    onClick={() => navigate(item.route)}
                    className="deck-alert group flex items-center justify-between px-3.5 py-2.5 text-left"
                  >
                    <span>
                      <span className="block text-[13px] font-bold text-slate-800">{item.label}</span>
                      <span className="block text-[10.5px] font-medium text-slate-400">{item.note}</span>
                    </span>
                    <ArrowRight className="h-3.5 w-3.5 shrink-0 text-slate-300 transition group-hover:translate-x-0.5 group-hover:text-teal-600" />
                  </button>
                ))}
              </div>
            </section>
          </div>
        </div>
      )}

      {section === "roster" && <VehiclesRosterPage />}

      {section === "planning" && <PlanningView rows={rows} planning={planning.data as AnyRecord} onNavigate={navigate} />}
      {section === "health" && <HealthView rows={rows} onNavigate={navigate} />}
      {section === "records" && <RecordsView rows={rows} onNavigate={navigate} />}
    </div>
  );
}

function OverviewClay({ Icon, tone, iconCls, label, value, caption, alert }:
  { Icon: React.ElementType; tone: string; iconCls: string; label: string; value: React.ReactNode; caption?: string; alert?: boolean }) {
  const n = Number(value);
  const valueColor = alert && Number.isFinite(n) && n > 0 ? (tone.includes("red") ? "text-rose-600" : "text-amber-600") : "text-slate-900";
  return (
    <div className={`fc-clay ${tone} p-4`}>
      <div className="flex items-center justify-between">
        <span className="text-[12px] font-bold text-slate-600">{label}</span>
        <span className="fc-blob"><Icon className={`h-4 w-4 ${iconCls}`} /></span>
      </div>
      <div className={`mt-2 text-[30px] font-black leading-none tracking-tight tabular-nums ${valueColor}`}>{value}</div>
      {caption ? <p className="mt-2 text-[11px] font-medium text-slate-500">{caption}</p> : null}
    </div>
  );
}

function ModuleCard({ title, body, action, onClick, icon }: { title: string; body: string; action: string; onClick: () => void; icon: React.ReactNode }) {
  return (
    <button type="button" onClick={onClick} className="fc-neumo group p-5 text-left transition hover:-translate-y-0.5">
      <div className="flex items-center justify-between">
        <div className="fc-blob h-10 w-10 text-slate-500">{icon}</div>
        <ArrowRight className="h-4 w-4 text-slate-300 transition group-hover:translate-x-0.5 group-hover:text-teal-600" />
      </div>
      <h3 className="mt-4 text-base font-black text-slate-900">{title}</h3>
      <p className="mt-1.5 text-sm text-slate-500">{body}</p>
      <p className="mt-3 text-xs font-bold uppercase tracking-[0.14em] text-teal-700">{action}</p>
    </button>
  );
}

function PlanningView({ rows, planning, onNavigate }: { rows: AnyRecord[]; planning?: AnyRecord; onNavigate: (route: string) => void }) {
  const forecast = ((planning?.replacementForecast as AnyRecord[]) || []).slice(0, 6);
  const gaps = ((planning?.operationalGaps as AnyRecord[]) || []).slice(0, 6);
  const customerBusiness = ((planning?.customerBusiness as AnyRecord[]) || []).slice(0, 4);
  const routeBusiness = ((planning?.routeBusiness as AnyRecord[]) || []).slice(0, 4);

  return (
    <div className="space-y-5">
      <section className="panel p-5">
        <h2 className="text-lg font-black text-slate-900">Planning</h2>
        <p className="mt-1 text-sm text-slate-500">Replacement priority, CapEx pressure and operational gaps computed from the live fleet, customer and route data.</p>
      </section>
      <div className="grid gap-4 lg:grid-cols-[1.2fr_0.8fr]">
        <SimpleListCard title="Replacement priority" rows={forecast} fields={["vehicleCode", "replacementWindow", "capexPriorityScore"]} />
        <SimpleListCard title="Operational gaps" rows={gaps} fields={["gapName", "affectedRecords", "visibility"]} />
      </div>
      <div className="grid gap-4 lg:grid-cols-2">
        <SimpleListCard title="Customer business pressure" rows={customerBusiness} fields={["customerName", "planningSignal", "activeJobs"]} />
        <SimpleListCard title="Route pressure" rows={routeBusiness} fields={["routeCode", "planningSignal", "activeJobs"]} />
      </div>
      <RelatedJumpRow onNavigate={onNavigate} />
      {!forecast.length && !gaps.length && !customerBusiness.length && !routeBusiness.length ? (
        <EmptyState title="No planning data" subtitle="The backend returned no planning insight rows." />
      ) : null}
      <p className="text-xs text-slate-500">{rows.length} live vehicles feed this view.</p>
    </div>
  );
}

function HealthView({ rows, onNavigate }: { rows: AnyRecord[]; onNavigate: (route: string) => void }) {
  const risky = rows.filter((row) => riskTier(row) === "High" || !/online/i.test(String(g(row, "deviceStatus", "device_status") ?? "Online")) || !/online/i.test(String(g(row, "cameraStatus", "camera_status") ?? "Online")));
  return (
    <div className="space-y-5">
      <section className="panel p-5">
        <h2 className="text-lg font-black text-slate-900">Health</h2>
        <p className="mt-1 text-sm text-slate-500">Readiness, device and camera exceptions across the fleet, updated from live vehicle telemetry.</p>
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
      <RelatedJumpRow onNavigate={onNavigate} />
    </div>
  );
}

function RecordsView({ rows, onNavigate }: { rows: AnyRecord[]; onNavigate: (route: string) => void }) {
  const [selectedId, setSelectedId] = useState<string | null>(() => (rows[0] ? rowId(rows[0]) : null));
  const detail = useQuery({
    queryKey: ["vehicles", "detail", selectedId],
    queryFn: () => vehiclesApi.detail(String(selectedId)),
    enabled: selectedId != null,
  });

  useEffect(() => {
    if (rows.length && selectedId == null) setSelectedId(rowId(rows[0]));
  }, [rows, selectedId]);

  const selectedRow = rows.find((row) => rowId(row) === selectedId) || null;
  const record = (detail.data?.record as AnyRecord) || selectedRow;
  const sections = [
    { title: "Maintenance", rows: detail.data?.maintenance as AnyRecord[] | undefined ?? [], fields: ["title", "category", "status", "dueDate"] },
    { title: "Compliance", rows: detail.data?.compliance as AnyRecord[] | undefined ?? [], fields: ["documentName", "documentType", "status", "expiryDate"] },
    { title: "Documents", rows: detail.data?.documents as AnyRecord[] | undefined ?? [], fields: ["documentName", "documentType", "status", "expiryDate"] },
    { title: "Audit trail", rows: detail.data?.auditTrail as AnyRecord[] | undefined ?? [], fields: ["actionName", "actorName", "createdAt"] },
  ];

  return (
    <div className="space-y-5">
      <section className="panel p-5">
        <h2 className="text-lg font-black text-slate-900">Records</h2>
        <p className="mt-1 text-sm text-slate-500">Maintenance, compliance, documents and audit history for the selected unit.</p>
      </section>
      <RelatedJumpRow onNavigate={onNavigate} />
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
          <div className="panel p-4">
            <div className="flex items-center justify-between">
              <div>
                <h3 className="text-sm font-semibold text-slate-800">Record connections</h3>
                <p className="text-xs text-slate-500">Jump directly to the entity areas this vehicle record depends on.</p>
              </div>
            </div>
            <div className="mt-3 grid gap-2 sm:grid-cols-2">
              <RelatedChip label="Open driver" onClick={() => onNavigate("/drivers")} />
              <RelatedChip label="Open maintenance" onClick={() => onNavigate("/maintenance")} />
              <RelatedChip label="Open documents" onClick={() => onNavigate("/documents")} />
              <RelatedChip label="Open jobs" onClick={() => onNavigate("/jobs")} />
              <RelatedChip label="Open work orders" onClick={() => onNavigate("/work-orders")} />
              <RelatedChip label="Open audit logs" onClick={() => onNavigate("/audit-logs")} />
            </div>
          </div>
          {sections.map((section) => (
            <SimpleListCard key={section.title} title={section.title} rows={section.rows || []} fields={section.fields} />
          ))}
        </div>
      </div>
      {detail.isLoading && !record ? <LoadingState /> : null}
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
        <div className="mt-3">
          <DataTable rows={rows.slice(0, 6)} columns={fields} />
        </div>
      )}
    </section>
  );
}

function RelatedJumpRow({ onNavigate }: { onNavigate: (route: string) => void }) {
  return (
    <div className="grid gap-3 md:grid-cols-3 xl:grid-cols-6">
      {RELATED_ENTITIES.map((item) => (
        <button
          key={item.label}
          type="button"
          onClick={() => onNavigate(item.route)}
          className="rounded-2xl border border-slate-200 bg-white px-3 py-3 text-left text-sm shadow-sm transition hover:border-teal-200 hover:bg-teal-50/50"
        >
          <div className="font-semibold text-slate-900">{item.label}</div>
          <div className="mt-1 text-xs text-slate-500">{item.note}</div>
        </button>
      ))}
    </div>
  );
}

function RelatedChip({ label, onClick }: { label: string; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="rounded-xl border border-slate-200 bg-slate-50 px-3 py-2 text-left text-sm font-medium text-slate-700 transition hover:border-teal-200 hover:bg-white"
    >
      {label}
    </button>
  );
}

function avg(rows: AnyRecord[], key: string) {
  if (!rows.length) return 0;
  return rows.reduce((t, r) => t + num(g(r, key, key.replace(/([A-Z])/g, "_$1").toLowerCase())), 0) / rows.length;
}
