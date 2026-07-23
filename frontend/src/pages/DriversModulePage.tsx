import { useEffect, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useLocation, useNavigate } from "react-router-dom";
import { AlertTriangle, ArrowRight, BadgeCheck, ClipboardCheck, Radio, ShieldAlert, UserCheck, Users } from "lucide-react";
import { LoadingState, ErrorState, KpiCard, EmptyState, DataTable, StatusBadge } from "@/components/ui";
import { EntityImportExport } from "@/components/EntityImportExport";
import { driversApi } from "@/services/driversApi";
import { scopeRowsForSession } from "@/auth/accessScope";
import { useAuth } from "@/hooks/useAuth";
import { useHasPermission } from "@/hooks/usePermission";
import type { AnyRecord } from "@/types";
import { EntityListPage } from "@/pages/EntityListPage";

const DRIVER_IMPORT_EXPORT = {
  entity: "drivers",
  columns: ["driverCode", "fullName", "phone", "email", "licenseNumber", "status"],
  requiredColumns: ["driverCode", "fullName"],
  templateEndpoint: "/api/drivers/import-template",
  exportEndpoint: "/api/drivers/export",
  importPreview: driversApi.importPreview,
  importCommit: driversApi.importCommit,
  invalidateKey: "drivers",
} as const;

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

function findMatching(rows: AnyRecord[] | undefined, ...patterns: RegExp[]) {
  const list = rows || [];
  return list.find((row) => {
    const hay = [
      row.documentType,
      row.document_type,
      row.documentName,
      row.document_name,
      row.certificationType,
      row.certification_type,
      row.title,
      row.category,
    ]
      .filter(Boolean)
      .join(" ");
    return patterns.some((pattern) => pattern.test(String(hay)));
  }) || null;
}

function regionalRequirements(countryCode: string) {
  if (countryCode === "SA") {
    return [
      { title: "Saudi / GCC license", type: "license", matcher: /license|cdl|driving permit/i },
      { title: "Medical fitness certificate", type: "medical", matcher: /medical|fitness|med cert|dot/i },
      { title: "Iqama / work authorization", type: "workAuth", matcher: /iqama|residence|work permit|visa|residency/i },
      { title: "Health insurance / GOSI", type: "insurance", matcher: /insurance|health|gosi/i },
      { title: "Cold chain / hazmat endorsement", type: "endorsement", matcher: /cold chain|hazmat|dangerous goods|reefer/i },
    ] as const;
  }
  if (countryCode === "AE") {
    return [
      { title: "UAE heavy vehicle license", type: "license", matcher: /license|cdl|driving permit/i },
      { title: "Medical fitness certificate", type: "medical", matcher: /medical|fitness|med cert/i },
      { title: "Emirates ID / visa", type: "workAuth", matcher: /emirates id|visa|work permit|residence/i },
      { title: "Health insurance", type: "insurance", matcher: /insurance|health/i },
      { title: "Dangerous goods / reefer endorsement", type: "endorsement", matcher: /dangerous goods|hazmat|cold chain|reefer/i },
    ] as const;
  }
  if (countryCode === "CA") {
    return [
      { title: "Commercial license", type: "license", matcher: /license|cdl|class/i },
      { title: "Medical certificate", type: "medical", matcher: /medical|med cert/i },
      { title: "Drug / alcohol compliance", type: "drug", matcher: /drug|alcohol|consortium/i },
      { title: "Work authorization / SIN support", type: "workAuth", matcher: /work permit|visa|sin|residence/i },
      { title: "Cross-border / hazmat endorsement", type: "endorsement", matcher: /hazmat|dangerous goods|cross border|fast/i },
    ] as const;
  }
  return [
    { title: "CDL / commercial license", type: "license", matcher: /license|cdl|class/i },
    { title: "Medical card", type: "medical", matcher: /medical|med cert|dot/i },
    { title: "Drug / alcohol program", type: "drug", matcher: /drug|alcohol|consortium/i },
    { title: "Health insurance / benefits", type: "insurance", matcher: /insurance|health/i },
    { title: "Hazmat / specialty endorsement", type: "endorsement", matcher: /hazmat|tank|reefer|cold chain|endorsement/i },
  ] as const;
}

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
  const hasPermission = useHasPermission();

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
    <div className="fleet-console space-y-3 pb-6">
      <header className="fc-rail relative px-6 py-4">
        <div className="relative flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
          <div className="min-w-0">
            <span className="section-title inline-flex items-center gap-2">
              <Users className="h-3.5 w-3.5 text-teal-700" /> Workforce · Master Data
            </span>
            <h1 className="mt-1 text-[26px] font-black leading-none tracking-tight text-slate-950">Drivers</h1>
            <p className="mt-1.5 text-[12.5px] font-medium text-slate-500">
              <span className="font-bold text-slate-700 tabular-nums">{rows.length}</span> operators in the live registry ·{" "}
              <span className="font-bold text-emerald-600 tabular-nums">{ready}</span> available ·{" "}
              <span className="font-bold text-rose-600 tabular-nums">{atRisk}</span> need attention
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <EntityImportExport
              config={DRIVER_IMPORT_EXPORT}
              canImport={hasPermission("fleet:manage")}
              canExport={hasPermission("drivers:view")}
            />
            <button type="button" onClick={() => navigate("/drivers/roster")} className="btn-primary h-10">
              Open roster <ArrowRight className="h-4 w-4" />
            </button>
          </div>
        </div>
      </header>

      <nav className="fc-neumo sticky top-4 z-20 p-2">
        <div className="grid gap-1 sm:grid-cols-5">
          {SECTIONS.map((item) => (
            <button
              key={item.key}
              type="button"
              onClick={() => navigate(`/drivers/${item.key}`)}
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
            <OverviewClay Icon={UserCheck}     tone="fc-clay-teal"    iconCls="text-teal-700"    label="Driver readiness" value={`${readiness}%`} caption={`${rows.length} live operators`} />
            <OverviewClay Icon={ShieldAlert}   tone="fc-clay-emerald" iconCls="text-emerald-700" label="Safety average"   value={safetyAvg}       caption="Fleet-wide behavior score" />
            <OverviewClay Icon={Users}         tone="fc-clay-sky"     iconCls="text-sky-700"     label="Available now"    value={ready}           caption="Ready for dispatch" />
            <OverviewClay Icon={AlertTriangle} tone="fc-clay-red"     iconCls="text-rose-700"    label="At risk"          value={atRisk}          caption="Compliance or safety watch" alert={atRisk > 0} />
          </div>
          <div className="grid gap-3 lg:grid-cols-3">
            <ModuleCard title="Roster" body="Browse, search, edit, assign and export live driver records." action="Open roster" onClick={() => navigate("/drivers/roster")} icon={<Users className="h-5 w-5" />} />
            <ModuleCard title="Readiness" body="Driver availability, HOS pressure and compliance gaps from live records." action="Open readiness" onClick={() => navigate("/drivers/readiness")} icon={<BadgeCheck className="h-5 w-5" />} />
            <ModuleCard title="Safety" body="Coaching load, risk posture and communication touchpoints per driver." action="Open safety" onClick={() => navigate("/drivers/safety")} icon={<ShieldAlert className="h-5 w-5" />} />
          </div>
          <div className="grid gap-3 xl:grid-cols-[1.1fr_0.9fr]">
            <section className="fc-neumo p-4">
              <div className="flex items-center justify-between">
                <div>
                  <h2 className="text-base font-black text-slate-900">Latest operators</h2>
                  <p className="text-xs font-medium text-slate-500">Most recent records from the live registry.</p>
                </div>
                <button type="button" className="btn-ghost h-9" onClick={() => navigate("/drivers/roster")}>Go to roster</button>
              </div>
              <div className="mt-3 grid gap-2.5 md:grid-cols-2">
                {rows.slice(0, 4).map((row) => (
                  <div key={rowId(row)} className="deck-inset rounded-xl p-3.5">
                    <p className="text-base font-bold text-slate-900">{String(g(row, "fullName", "driverName", "full_name") ?? `Driver ${rowId(row)}`)}</p>
                    <p className="mt-0.5 text-xs text-slate-500">{String(g(row, "driverCode", "driver_code") ?? "")}</p>
                    <div className="mt-2.5 flex items-center justify-between text-xs text-slate-500">
                      <StatusBadge status={String(g(row, "status") ?? "--")} />
                      <span className="truncate pl-2">{String(g(row, "assignedVehicle", "assigned_vehicle") ?? "Unassigned")}</span>
                    </div>
                  </div>
                ))}
                {rows.length === 0 && <p className="deck-inset col-span-full rounded-xl px-3 py-4 text-sm text-slate-400">No drivers in the registry yet — import a CSV or add one from the roster.</p>}
              </div>
            </section>
            <section className="fc-neumo p-4">
              <div>
                <h2 className="text-base font-black text-slate-900">Related workflows</h2>
                <p className="text-xs font-medium text-slate-500">HOS, compliance, coaching and messaging for this workforce.</p>
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

      {section === "roster" && <EntityListPage kind="drivers" />}
      {section === "readiness" && <ReadinessView rows={rows} complianceGap={complianceGap} hosWatch={hosWatch} assigned={assigned} onNavigate={navigate} />}
      {section === "safety" && <SafetyView rows={rows} onNavigate={navigate} />}
      {section === "records" && <RecordsView rows={rows} onNavigate={navigate} />}
    </div>
  );
}

function OverviewClay({ Icon, tone, iconCls, label, value, caption, alert }:
  { Icon: React.ElementType; tone: string; iconCls: string; label: string; value: React.ReactNode; caption?: string; alert?: boolean }) {
  const n = Number(value);
  const valueColor = alert && Number.isFinite(n) && n > 0 ? "text-rose-600" : "text-slate-900";
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
        <h2 className="text-lg font-black text-slate-900">Readiness</h2>
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
        <h2 className="text-lg font-black text-slate-900">Safety</h2>
        <p className="mt-1 text-sm text-slate-500">Coaching load, risk posture and communication touchpoints across the driver pool.</p>
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

  const detailPayload = (detail.data as AnyRecord) || {};
  const selectedRow = rows.find((row) => rowId(row) === selectedId) || null;
  const record = (detailPayload.record as AnyRecord) || selectedRow;
  const documents = (detailPayload.documents as AnyRecord[]) || [];
  const certifications = (detailPayload.certifications as AnyRecord[]) || [];
  const complianceStatus = (detailPayload.complianceStatus as AnyRecord) || null;
  const countryCode = String(
    g(complianceStatus || {}, "countryCode", "country_code", "profileCountryCode", "profile_country_code")
      ?? g(record || {}, "countryCode", "country_code")
      ?? "US"
  );
  const licenseDoc = findMatching(documents, /license|cdl/i) || findMatching(certifications, /license|cdl/i);
  const medicalDoc = findMatching(documents, /medical|med cert|dot/i) || findMatching(certifications, /medical|dot/i);
  const insuranceDoc = findMatching(documents, /insurance|health/i);
  const workAuthDoc = findMatching(documents, /iqama|emirates id|residence|work permit|visa|sin/i);
  const drugDoc = findMatching(documents, /drug|alcohol|consortium/i) || findMatching(certifications, /drug|alcohol/i);
  const endorsementDoc = findMatching(documents, /hazmat|dangerous goods|cold chain|reefer|endorsement/i) || findMatching(certifications, /hazmat|dangerous goods|cold chain|endorsement/i);
  const requirementDocs: Record<string, AnyRecord | null> = {
    license: licenseDoc,
    medical: medicalDoc,
    insurance: insuranceDoc,
    workAuth: workAuthDoc,
    drug: drugDoc,
    endorsement: endorsementDoc,
  };
  const requirementCards = regionalRequirements(countryCode).map((item) => ({
    ...item,
    source: requirementDocs[item.type] || null,
  }));
  const profileItems = record ? [
    ["Driver code", String(g(record, "driverCode", "driver_code") ?? "—")],
    ["Phone", String(g(record, "phone") ?? "—")],
    ["Email", String(g(record, "email") ?? "—")],
    ["Assigned vehicle", String(g(record, "assignedVehicle", "assigned_vehicle") ?? "Unassigned")],
    ["License number", String(g(record, "licenseNumber", "license_number") ?? g(licenseDoc || {}, "certificationNumber", "certification_number") ?? "—")],
    ["License expiry", fmt(g(record, "licenseExpiry", "license_expiry") ?? g(licenseDoc || {}, "expiryDate", "expiry_date"))],
    ["License class", String(g(record, "licenseClass", "license_class") ?? "—")],
    ["Compliance score", String(num(g(record, "complianceScore", "compliance_score")) || "—")],
    ["Compliance profile", String(g(complianceStatus || {}, "profileName", "profile_name", "rulesetName", "ruleset_name") ?? "—")],
    ["Authority", String(g(complianceStatus || {}, "authority") ?? "—")],
    ["Country", countryCode],
    ["Overall compliance", String(g(complianceStatus || {}, "overallStatus", "overall_status") ?? "—")],
  ] : [];
  const sections = [
    { title: "Certifications", rows: detailPayload.certifications as AnyRecord[] | undefined, fields: ["certificationType", "status", "expiryDate"] },
    { title: "Documents", rows: detailPayload.documents as AnyRecord[] | undefined, fields: ["documentName", "documentType", "status", "expiryDate"] },
    { title: "HOS", rows: detailPayload.hos as AnyRecord[] | undefined, fields: ["logDate", "drivingHours", "onDutyHours", "cycleHoursLeft", "status"] },
    { title: "DVIR / Inspections", rows: detailPayload.inspections as AnyRecord[] | undefined, fields: ["inspectionType", "result", "createdAt"] },
    { title: "Safety events", rows: detailPayload.safetyEvents as AnyRecord[] | undefined, fields: ["eventType", "severity", "reviewStatus", "eventTime"] },
    { title: "Audit trail", rows: detailPayload.auditTrail as AnyRecord[] | undefined, fields: ["actionName", "actorName", "createdAt"] },
  ];

  return (
    <div className="space-y-5">
      <section className="panel p-5">
        <h2 className="text-lg font-black text-slate-900">Records</h2>
        <p className="mt-1 text-sm text-slate-500">Certifications, HOS, inspections and audit history for the selected driver.</p>
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
                  <StatusBadge status={String(g(row, "status") ?? "--")} />
                </div>
                <div className="mt-1 text-xs text-slate-500">{String(g(row, "driverCode", "driver_code") ?? "")} · {String(g(row, "assignedVehicle", "assigned_vehicle") ?? "Unassigned")}</div>
              </button>
            ))}
          </div>
        </div>
        <div className="space-y-4">
          {record ? (
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
              <KpiCard label="Driver" value={String(g(record, "fullName", "driverName", "full_name") ?? `Driver ${record.id}`)} />
              <KpiCard label="Status" value={String(g(record, "status") ?? "--")} />
              <KpiCard label="Vehicle" value={String(g(record, "assignedVehicle", "assigned_vehicle") ?? "Unassigned")} />
              <KpiCard label="Safety score" value={String(num(g(record, "safetyScore", "safety_score")))} />
            </div>
          ) : null}
          {record ? (
            <div className="grid gap-4 xl:grid-cols-[0.95fr_1.05fr]">
              <section className="panel p-4">
                <div className="flex items-center justify-between">
                  <div>
                    <h3 className="text-sm font-semibold text-slate-800">Driver profile</h3>
                    <p className="text-xs text-slate-500">Core personnel and operator identity details from the live record.</p>
                  </div>
                </div>
                <div className="mt-3 grid gap-3 sm:grid-cols-2">
                  {profileItems.map(([label, value]) => (
                    <div key={label} className="rounded-xl border border-slate-200 bg-slate-50 px-3 py-3">
                      <div className="text-[10px] font-semibold uppercase tracking-[0.14em] text-slate-400">{label}</div>
                      <div className="mt-1 text-sm font-semibold text-slate-900">{value}</div>
                    </div>
                  ))}
                </div>
              </section>
              <section className="panel p-4">
                <div className="flex items-center justify-between">
                  <div>
                    <h3 className="text-sm font-semibold text-slate-800">Credentials and coverage</h3>
                    <p className="text-xs text-slate-500">License, medical and insurance evidence surfaced from live documents and certifications.</p>
                  </div>
                </div>
                <div className="mt-3 grid gap-3 sm:grid-cols-3">
                  <CredentialCard
                    title="Driver license / CDL"
                    status={String(g(licenseDoc || record, "status") ?? "Missing")}
                    name={String(g(licenseDoc || {}, "documentName", "document_name", "certificationType", "certification_type") ?? "License record")}
                    expiry={fmt(g(record, "licenseExpiry", "license_expiry") ?? g(licenseDoc || {}, "expiryDate", "expiry_date"))}
                  />
                  <CredentialCard
                    title="Medical certificate"
                    status={String(g(medicalDoc || {}, "status") ?? "Watch")}
                    name={String(g(medicalDoc || {}, "documentName", "document_name", "certificationType", "certification_type") ?? "Medical certificate")}
                    expiry={fmt(g(medicalDoc || {}, "expiryDate", "expiry_date"))}
                  />
                  <CredentialCard
                    title="Health insurance / coverage"
                    status={String(g(insuranceDoc || {}, "status") ?? "Not captured")}
                    name={String(g(insuranceDoc || {}, "documentName", "document_name", "documentType", "document_type") ?? "No insurance document on file")}
                    expiry={fmt(g(insuranceDoc || {}, "expiryDate", "expiry_date"))}
                  />
                </div>
              </section>
            </div>
          ) : null}
          {record ? (
            <section className="panel p-4">
              <div className="flex items-center justify-between">
                <div>
                  <h3 className="text-sm font-semibold text-slate-800">Market requirements</h3>
                  <p className="text-xs text-slate-500">Region-aware driver requirements for {countryCode}, so Saudi/GCC and North American operator records are assessed against the right evidence set.</p>
                </div>
              </div>
              <div className="mt-3 grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
                {requirementCards.map((item) => (
                  <MarketRequirementCard
                    key={item.title}
                    title={item.title}
                    source={item.source}
                    fallbackStatus={item.type === "medical" ? String(g(complianceStatus || {}, "medicalCertValid") === false ? "Review" : "Watch") : item.type === "drug" ? String(g(complianceStatus || {}, "drugTestValid") === false ? "Review" : "Watch") : item.type === "license" ? String(g(complianceStatus || {}, "licenseValid") === false ? "Review" : "Watch") : "Not captured"}
                    fallbackExpiry={item.type === "medical" ? fmt(g(complianceStatus || {}, "medicalCertExpiry", "medical_cert_expiry")) : item.type === "drug" ? fmt(g(complianceStatus || {}, "drugTestExpiry", "drug_test_expiry")) : item.type === "license" ? fmt(g(complianceStatus || {}, "licenseExpiry", "license_expiry")) : "—"}
                  />
                ))}
              </div>
            </section>
          ) : null}
          <PortalAccessCard record={record} onChanged={() => { void detail.refetch(); }} />
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
          {detail.isLoading && !record ? <LoadingState /> : null}
    </div>
  );
}

/**
 * Driver-portal access.
 *
 * This is the only place in the product that can give a driver a login. The link it writes
 * (drivers.user_id) is what every /api/driver/* endpoint uses to identify the caller — before
 * it existed, the column was read by five call sites and written by none, so the entire driver
 * app 403'd for every driver in every tenant.
 *
 * Deliberately per-driver rather than automatic on driver-create: the create path is shared
 * with the CSV bulk importer, and silently minting hundreds of credentialed accounts from a
 * spreadsheet paste is a security incident, not a convenience.
 */
function PortalAccessCard({ record, onChanged }: { record: AnyRecord | null; onChanged: () => void }) {
  const hasPermission = useHasPermission();
  const canManage = hasPermission("fleet:manage");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [tempPassword, setTempPassword] = useState<string | null>(null);

  if (!record) return null;

  const driverId = g(record, "id");
  const status = String(g(record, "portalStatus", "portal_status") ?? "none");
  const email = String(g(record, "portalEmail", "portal_email") ?? g(record, "email") ?? "");
  const hasAccess = status === "active" || status === "disabled";

  const run = async (fn: () => Promise<unknown>) => {
    setBusy(true);
    setError(null);
    setTempPassword(null);
    try {
      const result = (await fn()) as AnyRecord;
      const temp = result?.temporaryPassword;
      if (temp) setTempPassword(String(temp));
      onChanged();
    } catch (err) {
      setError((err as Error)?.message ?? "Action failed");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="panel p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h3 className="text-sm font-semibold text-slate-800">Driver portal access</h3>
          <p className="text-xs text-slate-500">
            {hasAccess
              ? `Signs in as ${email || "—"}`
              : "This driver has no login and cannot use the driver app."}
          </p>
        </div>
        <StatusBadge status={status === "active" ? "Active" : status === "disabled" ? "Disabled" : "Not provisioned"} />
      </div>

      {canManage && (
        <div className="mt-3 flex flex-wrap gap-2">
          {!hasAccess && (
            <button
              type="button"
              disabled={busy}
              onClick={() => void run(() => driversApi.portalInvite(String(driverId)))}
              className="rounded-lg bg-teal-600 px-3 py-2 text-xs font-semibold text-white disabled:opacity-50"
            >
              {busy ? "Granting…" : "Grant portal access"}
            </button>
          )}
          {hasAccess && (
            <button
              type="button"
              disabled={busy}
              onClick={() => void run(() => driversApi.portalRevoke(String(driverId)))}
              className="rounded-lg border border-rose-200 px-3 py-2 text-xs font-semibold text-rose-600 disabled:opacity-50"
            >
              {busy ? "Revoking…" : "Revoke access"}
            </button>
          )}
        </div>
      )}

      {tempPassword && (
        <div className="mt-3 rounded-lg border border-amber-200 bg-amber-50 p-3">
          <p className="text-xs font-semibold text-amber-900">Temporary password — shown once</p>
          <p className="mt-1 font-mono text-sm text-amber-900">{tempPassword}</p>
          <p className="mt-1 text-[11px] text-amber-800">
            Email delivery is not configured, so this is not sent automatically. Give it to the driver directly;
            they should change it on first sign-in.
          </p>
        </div>
      )}
      {error && <p className="mt-2 text-xs text-rose-600">{error}</p>}
    </div>
  );
}

function CredentialCard({ title, status, name, expiry }: { title: string; status: string; name: string; expiry: string }) {
  const tone = /valid|active|current/i.test(status)
    ? "border-emerald-200 bg-emerald-50/60"
    : /review|expiring|watch/i.test(status)
      ? "border-amber-200 bg-amber-50/60"
      : "border-rose-200 bg-rose-50/60";
  return (
    <div className={`rounded-2xl border p-4 ${tone}`}>
      <div className="text-[10px] font-semibold uppercase tracking-[0.14em] text-slate-500">{title}</div>
      <div className="mt-2 text-sm font-semibold text-slate-900">{name}</div>
      <div className="mt-2 flex items-center justify-between text-xs">
        <span className="font-medium text-slate-600">{status}</span>
        <span className="text-slate-500">Expiry {expiry}</span>
      </div>
    </div>
  );
}

function MarketRequirementCard({
  title,
  source,
  fallbackStatus,
  fallbackExpiry,
}: {
  title: string;
  source: AnyRecord | null;
  fallbackStatus: string;
  fallbackExpiry: string;
}) {
  const status = String(g(source || {}, "status") ?? fallbackStatus ?? "Not captured");
  const name = String(g(source || {}, "documentName", "document_name", "documentType", "document_type", "certificationType", "certification_type") ?? "No evidence linked");
  const expiry = fmt(g(source || {}, "expiryDate", "expiry_date") ?? fallbackExpiry);
  const tone = /valid|active|current|compliant/i.test(status)
    ? "border-emerald-200 bg-emerald-50/60"
    : /review|expiring|watch|warning/i.test(status)
      ? "border-amber-200 bg-amber-50/60"
      : "border-rose-200 bg-rose-50/60";
  return (
    <div className={`rounded-2xl border p-4 ${tone}`}>
      <div className="text-[10px] font-semibold uppercase tracking-[0.14em] text-slate-500">{title}</div>
      <div className="mt-2 text-sm font-semibold text-slate-900">{name}</div>
      <div className="mt-2 text-xs text-slate-600">{status}</div>
      <div className="mt-1 text-xs text-slate-500">Expiry {expiry}</div>
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
