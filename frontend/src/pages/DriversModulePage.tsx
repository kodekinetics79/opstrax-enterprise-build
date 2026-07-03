import { useEffect, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useLocation, useNavigate } from "react-router-dom";
import { AlertTriangle, ArrowRight, BadgeCheck, ClipboardCheck, Radio, Satellite, ShieldAlert, Sparkles, UserCheck, Users } from "lucide-react";
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
      <header className="fh-hero relative">
        <span className="fh-hero-bar" />
        <span className="fh-hero-glow-1" />
        <span className="fh-hero-glow-2" />
        <div className="relative px-7 py-6">
          <div className="flex flex-wrap items-start justify-between gap-6">
            <div className="min-w-0 flex-1">
              <div className="flex items-center gap-3 mb-3">
                <span className="inline-flex items-center gap-1.5 rounded-lg bg-white/90 px-3 py-1 text-[10px] font-bold uppercase tracking-[0.2em] text-teal-700 ring-1 ring-teal-200/50 shadow-sm">
                  <Users className="h-3 w-3" /> Driver Module
                </span>
                <span className="text-[11px] font-semibold text-slate-500">Readiness, workflow and compliance</span>
              </div>
              <h1 className="text-[32px] font-black tracking-tight leading-none cc-gradient-text sm:text-[36px]">
                Drivers
              </h1>
              <p className="mt-1 text-[13px] font-medium text-slate-400 tracking-wide">
                Organized around the real driver workflow — readiness, coaching, HOS, compliance and records
              </p>
            </div>
            <div className="flex items-center gap-2">
              <button type="button" onClick={() => exportCsv("drivers", rows)} className="fh-btn-ghost">Export drivers</button>
              <button type="button" onClick={() => navigate("/drivers/roster")} className="fh-btn-primary">Open roster <ArrowRight className="h-3.5 w-3.5" /></button>
            </div>
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
              <button type="button" className="fh-btn-ghost h-9" onClick={() => navigate("/drivers/roster")}>Go to roster</button>
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

  const detailPayload = (detail.data as AnyRecord) || {};
  const record = (detailPayload.record as AnyRecord) || null;
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
