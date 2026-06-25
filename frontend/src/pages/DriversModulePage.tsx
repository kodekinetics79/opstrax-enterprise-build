import { useEffect, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useLocation, useNavigate } from "react-router-dom";
import { AlertTriangle, ArrowRight, BadgeCheck, ClipboardCheck, Radio, ShieldAlert, Sparkles, UserCheck, Users } from "lucide-react";
import { LoadingState, ErrorState, KpiCard, EmptyState, exportCsv, labelize } from "@/components/ui";
import { driversApi } from "@/services/driversApi";
import { scopeRowsForSession } from "@/auth/accessScope";
import { useAuth } from "@/hooks/useAuth";
import type { AnyRecord } from "@/types";
import { EntityListPage } from "@/pages/EntityListPage";

type DriverSection = "overview" | "roster" | "readiness" | "safety" | "records";

const SECTIONS: Array<{ key: DriverSection; label: string; description: string }> = [
  { key: "overview", label: "Overview", description: "Readiness and workflow entry points" },
  { key: "roster", label: "Roster", description: "Search, edit, assign and export" },
  { key: "readiness", label: "Readiness", description: "Availability, HOS and compliance posture" },
  { key: "safety", label: "Safety", description: "Coaching, risk and communication flow" },
  { key: "records", label: "Records", description: "Certifications, HOS, DVIR and audit" },
];

const RELATED_ENTITIES = [
  { label: "Vehicles", route: "/vehicles", note: "Jump to assigned units and availability" },
  { label: "Assignments", route: "/assignments", note: "Open driver and vehicle pairing history" },
  { label: "HOS / ELD", route: "/hos-eld", note: "Inspect clocks, logs and device issues" },
  { label: "Compliance", route: "/compliance", note: "Review certs, med cards and violations" },
  { label: "Coaching", route: "/coaching", note: "Work the safety intervention queue" },
  { label: "Driver Messaging", route: "/driver-messaging", note: "Coordinate with drivers in real time" },
] as const;

function readSection(pathname: string): DriverSection {
  const section = pathname.split("/").filter(Boolean)[1];
  if (section === "roster" || section === "readiness" || section === "safety" || section === "records") return section;
  return "overview";
}

const g = (row: AnyRecord, ...keys: string[]) => {
  for (const key of keys) if (row?.[key] != null && row[key] !== "") return row[key];
  return undefined;
};

const num = (v: unknown) => (Number.isFinite(Number(v)) ? Number(v) : 0);
const rowId = (row: AnyRecord) => String(row?.id ?? "");

function riskTier(row: AnyRecord): "High" | "Medium" | "Low" {
  const heat = String(g(row, "riskHeatScore", "risk_heat_score") ?? "");
  if (/high|critical/i.test(heat)) return "High";
  if (/medium|warning/i.test(heat)) return "Medium";
  if (/low/i.test(heat)) return "Low";
  const n = num(g(row, "safetyScore", "riskScore", "risk_score"));
  return n <= 65 ? "High" : n <= 80 ? "Medium" : "Low";
}

export function DriversModulePage() {
  const navigate = useNavigate();
  const location = useLocation();
  const section = readSection(location.pathname);
  const { session } = useAuth();

  const list = useQuery({ queryKey: ["drivers"], queryFn: driversApi.list });
  const summary = useQuery({ queryKey: ["drivers", "summary"], queryFn: driversApi.summary });

  const rows = useMemo(() => scopeRowsForSession("drivers", list.data || [], session), [list.data, session]);
  const visibleSummary = (summary.data as AnyRecord) || {};
  const loading = list.isLoading || summary.isLoading;

  if (section === "overview" && loading) return <LoadingState />;
  if (list.isError) return <ErrorState message={list.error instanceof Error ? list.error.message : "Unable to load drivers."} />;
  if (summary.isError) return <ErrorState message={summary.error instanceof Error ? summary.error.message : "Unable to load driver summary."} />;

  const ready = rows.filter((row) => /available|ready/i.test(String(g(row, "status") ?? ""))).length;
  const atRisk = num(visibleSummary.atRisk) || rows.filter((row) => riskTier(row) === "High").length;
  const safetyAvg = Math.round(num(visibleSummary.safetyScore) || avg(rows, "safetyScore"));
  const readiness = Math.round(num(visibleSummary.driverReadinessScore ?? visibleSummary.driver_readiness_score) || avg(rows, "driverReadinessScore"));
  const complianceGap = rows.filter((row) => num(g(row, "complianceScore", "compliance_score")) < 85).length;
  const hosWatch = rows.filter((row) => /warning|violation|review/i.test(String(g(row, "hosStatus", "hos_status", "status") ?? ""))).length;
  const assigned = rows.filter((row) => g(row, "assignedVehicle", "assigned_vehicle")).length;

  return (
    <div className="space-y-6 pb-10">
      <header className="relative overflow-hidden rounded-[26px] border border-slate-200 bg-white/80 px-6 py-5 text-slate-900 shadow-[0_24px_80px_-36px_rgba(15,23,42,0.45)] backdrop-blur-xl">
        <div className="pointer-events-none absolute inset-x-0 top-0 h-24 bg-[linear-gradient(90deg,rgba(16,185,129,0.14),rgba(14,165,233,0.12),rgba(251,191,36,0.12))]" />
        <div className="pointer-events-none absolute -right-10 -top-10 h-32 w-32 rounded-full bg-emerald-200/35 blur-3xl" />
        <div className="relative flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
          <div className="min-w-0">
            <div className="inline-flex items-center gap-2 rounded-full border border-emerald-200 bg-white/85 px-3 py-1 text-[11px] font-semibold uppercase tracking-[0.18em] text-emerald-700 shadow-sm">
              <span className="h-1.5 w-1.5 rounded-full bg-emerald-500" /> Driver module
            </div>
            <h1 className="mt-3 text-3xl font-black tracking-tight text-slate-900">Drivers</h1>
            <p className="mt-2 max-w-3xl text-sm text-slate-600">
              Organized around the real driver workflow so readiness, coaching, HOS, compliance and records are easier to find than in one oversized list view.
            </p>
          </div>
          <div className="flex flex-wrap gap-2">
            <button type="button" onClick={() => exportCsv("drivers", rows)} className="btn-ghost h-10 border-slate-200 bg-white/90 text-slate-700 hover:bg-slate-50">
              Export drivers
            </button>
            <button type="button" onClick={() => navigate("/drivers/roster")} className="btn-primary h-10 bg-gradient-to-r from-emerald-600 to-sky-600 shadow-md shadow-sky-200/60 hover:from-emerald-500 hover:to-sky-500">
              Open roster <ArrowRight className="h-4 w-4" />
            </button>
          </div>
        </div>
      </header>

      <nav className="sticky top-4 z-20 rounded-2xl border border-slate-200 bg-white/95 p-2 shadow-sm backdrop-blur">
        <div className="grid gap-1 sm:grid-cols-5">
          {SECTIONS.map((item) => (
            <button
              key={item.key}
              type="button"
              onClick={() => navigate(`/drivers/${item.key}`)}
              className={`rounded-xl px-3 py-2.5 text-left transition ${
                section === item.key ? "bg-slate-900 text-white shadow-sm" : "bg-slate-50/40 hover:bg-slate-100"
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
            <KpiCard label="Driver readiness" value={`${readiness}%`} icon={<UserCheck className="h-4 w-4" />} />
            <KpiCard label="Safety average" value={`${safetyAvg}`} icon={<ShieldAlert className="h-4 w-4" />} />
            <KpiCard label="Available now" value={String(ready)} icon={<Users className="h-4 w-4" />} />
            <KpiCard label="At risk" value={String(atRisk)} status="Review" icon={<AlertTriangle className="h-4 w-4" />} />
          </div>
          <div className="grid gap-4 lg:grid-cols-3">
            <ModuleCard title="Roster" body="Browse, search, edit, assign and export live driver records." action="Open roster" onClick={() => navigate("/drivers/roster")} icon={<Users className="h-5 w-5" />} />
            <ModuleCard title="Readiness" body="Review driver availability, HOS pressure and compliance gaps." action="Open readiness" onClick={() => navigate("/drivers/readiness")} icon={<BadgeCheck className="h-5 w-5" />} />
            <ModuleCard title="Safety" body="Track coaching needs, safety posture and communication touchpoints." action="Open safety" onClick={() => navigate("/drivers/safety")} icon={<ShieldAlert className="h-5 w-5" />} />
          </div>
          <section className="panel p-5">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <h2 className="text-lg font-semibold text-slate-900">Important surfaces we were missing from the main entry</h2>
                <p className="text-sm text-slate-500">The driver module now calls out the linked operational areas that were previously buried in separate pages.</p>
              </div>
              <span className="rounded-full border border-slate-200 bg-slate-50 px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.14em] text-slate-500">
                Related workflows
              </span>
            </div>
            <div className="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-3">
              {RELATED_ENTITIES.map((item) => (
                <button
                  key={item.label}
                  type="button"
                  onClick={() => navigate(item.route)}
                  className="group rounded-2xl border border-slate-200 bg-white p-4 text-left shadow-sm transition hover:-translate-y-0.5 hover:border-emerald-200 hover:shadow-md"
                >
                  <div className="flex items-center justify-between">
                    <span className="text-sm font-semibold text-slate-900">{item.label}</span>
                    <ArrowRight className="h-4 w-4 text-slate-300 transition group-hover:translate-x-0.5 group-hover:text-emerald-500" />
                  </div>
                  <p className="mt-2 text-sm text-slate-500">{item.note}</p>
                </button>
              ))}
            </div>
          </section>
          <section className="panel p-5">
            <div className="flex items-center justify-between">
              <div>
                <h2 className="text-lg font-semibold text-slate-900">Live driver snapshot</h2>
                <p className="text-sm text-slate-500">A fast, readable cross-section of the drivers currently feeding the module.</p>
              </div>
              <button type="button" className="btn-ghost h-9" onClick={() => navigate("/drivers/roster")}>Go to roster</button>
            </div>
            <div className="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
              {rows.slice(0, 4).map((row) => (
                <div key={rowId(row)} className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
                  <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Driver</p>
                  <p className="mt-1 text-base font-semibold text-slate-900">{String(g(row, "fullName", "driverName", "full_name") ?? `Driver ${rowId(row)}`)}</p>
                  <p className="mt-1 text-sm text-slate-500">{String(g(row, "driverCode", "driver_code") ?? "")}</p>
                  <div className="mt-3 flex items-center justify-between text-xs text-slate-500">
                    <span>{String(g(row, "status") ?? "--")}</span>
                    <span>{String(g(row, "assignedVehicle", "assigned_vehicle") ?? "Unassigned")}</span>
                  </div>
                </div>
              ))}
            </div>
          </section>
        </div>
      )}

      {section === "roster" && <EntityListPage kind="drivers" />}
      {section === "readiness" && <ReadinessView rows={rows} complianceGap={complianceGap} hosWatch={hosWatch} assigned={assigned} onNavigate={navigate} />}
      {section === "safety" && <SafetyView rows={rows} onNavigate={navigate} />}
      {section === "records" && <RecordsView rows={rows} onNavigate={navigate} />}
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
      <p className="mt-4 text-xs font-bold uppercase tracking-[0.14em] text-emerald-600">{action}</p>
    </button>
  );
}

function ReadinessView({
  rows,
  complianceGap,
  hosWatch,
  assigned,
  onNavigate,
}: {
  rows: AnyRecord[];
  complianceGap: number;
  hosWatch: number;
  assigned: number;
  onNavigate: (route: string) => void;
}) {
  const watch = rows.filter((row) =>
    /warning|violation|review/i.test(String(g(row, "hosStatus", "hos_status", "status") ?? "")) ||
    num(g(row, "complianceScore", "compliance_score")) < 85 ||
    riskTier(row) === "High"
  );

  return (
    <div className="space-y-5">
      <section className="panel p-5">
        <h2 className="text-lg font-semibold text-slate-900">Readiness view</h2>
        <p className="mt-1 text-sm text-slate-500">A focused view for who is dispatchable, who is nearing HOS trouble, and where compliance is weak.</p>
      </section>
      <div className="grid gap-4 xl:grid-cols-4">
        <KpiCard label="Ready for work" value={String(rows.filter((row) => /available|ready/i.test(String(g(row, "status") ?? ""))).length)} />
        <KpiCard label="Assigned" value={String(assigned)} />
        <KpiCard label="Compliance gaps" value={String(complianceGap)} status={complianceGap > 0 ? "Review" : undefined} />
        <KpiCard label="HOS watch" value={String(hosWatch)} status={hosWatch > 0 ? "Review" : undefined} />
      </div>
      <SimpleListCard title="Drivers needing readiness review" rows={watch.slice(0, 8)} fields={["driverCode", "fullName", "status", "assignedVehicle", "complianceScore"]} />
      <RelatedJumpRow onNavigate={onNavigate} />
    </div>
  );
}

function SafetyView({ rows, onNavigate }: { rows: AnyRecord[]; onNavigate: (route: string) => void }) {
  const risky = rows.filter((row) => riskTier(row) === "High" || num(g(row, "safetyScore", "safety_score")) < 75);
  const coachingOpen = rows.filter((row) => num(g(row, "coachingOpenCount", "openCoachingTasks", "coaching_open_count")) > 0).length;
  const openIncidents = rows.filter((row) => num(g(row, "incidentCount", "incident_count")) > 0).length;

  return (
    <div className="space-y-5">
      <section className="panel p-5">
        <h2 className="text-lg font-semibold text-slate-900">Safety view</h2>
        <p className="mt-1 text-sm text-slate-500">Brings coaching, risk and driver communications into one place instead of scattering them across unrelated screens.</p>
      </section>
      <div className="grid gap-4 xl:grid-cols-4">
        <KpiCard label="Fleet safety avg" value={`${Math.round(avg(rows, "safetyScore"))}`} />
        <KpiCard label="Coaching open" value={String(coachingOpen)} status={coachingOpen > 0 ? "Review" : undefined} />
        <KpiCard label="Incident watch" value={String(openIncidents)} status={openIncidents > 0 ? "Review" : undefined} />
        <KpiCard label="High risk drivers" value={String(risky.length)} status={risky.length > 0 ? "Review" : undefined} />
      </div>
      <div className="grid gap-4 lg:grid-cols-3">
        <ModuleCard title="Driver Scorecards" body="Fleet-wide driver behavior scores, events and trends." action="Open scorecards" onClick={() => onNavigate("/driver-scorecards")} icon={<ShieldAlert className="h-5 w-5" />} />
        <ModuleCard title="Coaching Queue" body="Open structured coaching work for at-risk drivers." action="Open coaching" onClick={() => onNavigate("/coaching")} icon={<ClipboardCheck className="h-5 w-5" />} />
        <ModuleCard title="Driver Messaging" body="Send safety updates and operational instructions quickly." action="Open messaging" onClick={() => onNavigate("/driver-messaging")} icon={<Radio className="h-5 w-5" />} />
      </div>
      <SimpleListCard title="Drivers needing safety attention" rows={risky.slice(0, 8)} fields={["driverCode", "fullName", "safetyScore", "riskHeatScore", "assignedVehicle"]} />
      <RelatedJumpRow onNavigate={onNavigate} />
    </div>
  );
}

function RecordsView({ rows, onNavigate }: { rows: AnyRecord[]; onNavigate: (route: string) => void }) {
  const [selectedId, setSelectedId] = useState<string | null>(() => (rows[0] ? rowId(rows[0]) : null));
  const detail = useQuery({
    queryKey: ["drivers", "detail", selectedId],
    queryFn: () => driversApi.detail(String(selectedId)),
    enabled: selectedId != null,
  });

  useEffect(() => {
    if (rows.length && selectedId == null) setSelectedId(rowId(rows[0]));
  }, [rows, selectedId]);

  const record = (detail.data?.record as AnyRecord) || null;
  const sections = [
    { title: "Certifications", rows: detail.data?.certifications as AnyRecord[] | undefined, fields: ["certificationType", "status", "expiryDate"] },
    { title: "Documents", rows: detail.data?.documents as AnyRecord[] | undefined, fields: ["documentName", "documentType", "status", "expiryDate"] },
    { title: "HOS", rows: detail.data?.hos as AnyRecord[] | undefined, fields: ["logDate", "drivingHours", "onDutyHours", "cycleHoursLeft", "status"] },
    { title: "DVIR / Inspections", rows: detail.data?.inspections as AnyRecord[] | undefined, fields: ["inspectionType", "result", "createdAt"] },
    { title: "Safety events", rows: detail.data?.safetyEvents as AnyRecord[] | undefined, fields: ["eventType", "severity", "reviewStatus", "eventTime"] },
    { title: "Audit trail", rows: detail.data?.auditTrail as AnyRecord[] | undefined, fields: ["actionName", "actorName", "createdAt"] },
  ];

  return (
    <div className="space-y-5">
      <section className="panel p-5">
        <h2 className="text-lg font-semibold text-slate-900">Records view</h2>
        <p className="mt-1 text-sm text-slate-500">Keeps certifications, HOS, inspections and audit history in a focused record workspace instead of burying them under the roster.</p>
      </section>
      <RelatedJumpRow onNavigate={onNavigate} />
      <div className="grid gap-5 xl:grid-cols-[0.9fr_1.1fr]">
        <div className="panel p-4">
          <h3 className="text-sm font-semibold text-slate-800">Driver selector</h3>
          <div className="mt-3 space-y-2">
            {rows.slice(0, 12).map((row) => (
              <button
                key={rowId(row)}
                type="button"
                onClick={() => setSelectedId(rowId(row))}
                className={`w-full rounded-xl border px-3 py-2.5 text-left transition ${selectedId === rowId(row) ? "border-emerald-300 bg-emerald-50" : "border-slate-200 bg-white hover:bg-slate-50"}`}
              >
                <div className="flex items-center justify-between">
                  <span className="font-semibold text-slate-900">{String(g(row, "fullName", "driverName", "full_name") ?? `Driver ${rowId(row)}`)}</span>
                  <span className="text-xs text-slate-500">{String(g(row, "status") ?? "--")}</span>
                </div>
                <div className="mt-1 text-xs text-slate-500">{String(g(row, "driverCode", "driver_code") ?? "")} · {String(g(row, "assignedVehicle", "assigned_vehicle") ?? "Unassigned")}</div>
              </button>
            ))}
          </div>
        </div>
        <div className="space-y-4">
          {record ? (
            <div className="grid gap-4 md:grid-cols-2">
              <KpiCard label="Driver" value={String(g(record, "fullName", "driverName", "full_name") ?? `Driver ${record.id}`)} />
              <KpiCard label="Status" value={String(g(record, "status") ?? "--")} />
              <KpiCard label="Vehicle" value={String(g(record, "assignedVehicle", "assigned_vehicle") ?? "Unassigned")} />
              <KpiCard label="Safety score" value={String(num(g(record, "safetyScore", "safety_score")))} />
            </div>
          ) : null}
          <div className="panel p-4">
            <div className="flex items-center justify-between">
              <div>
                <h3 className="text-sm font-semibold text-slate-800">Record connections</h3>
                <p className="text-xs text-slate-500">Jump directly to the workflows this driver record depends on.</p>
              </div>
            </div>
            <div className="mt-3 grid gap-2 sm:grid-cols-2">
              <RelatedChip label="Open vehicles" onClick={() => onNavigate("/vehicles")} />
              <RelatedChip label="Open assignments" onClick={() => onNavigate("/assignments")} />
              <RelatedChip label="Open HOS / ELD" onClick={() => onNavigate("/hos-eld")} />
              <RelatedChip label="Open compliance" onClick={() => onNavigate("/compliance")} />
              <RelatedChip label="Open coaching" onClick={() => onNavigate("/coaching")} />
              <RelatedChip label="Open messaging" onClick={() => onNavigate("/driver-messaging")} />
            </div>
          </div>
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

function RelatedJumpRow({ onNavigate }: { onNavigate: (route: string) => void }) {
  return (
    <div className="grid gap-3 md:grid-cols-3 xl:grid-cols-6">
      {RELATED_ENTITIES.map((item) => (
        <button
          key={item.label}
          type="button"
          onClick={() => onNavigate(item.route)}
          className="rounded-2xl border border-slate-200 bg-white px-3 py-3 text-left text-sm shadow-sm transition hover:border-emerald-200 hover:bg-emerald-50/50"
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
      className="rounded-xl border border-slate-200 bg-slate-50 px-3 py-2 text-left text-sm font-medium text-slate-700 transition hover:border-emerald-200 hover:bg-white"
    >
      {label}
    </button>
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
