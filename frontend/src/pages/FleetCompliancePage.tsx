import { useCallback, useEffect, useState } from "react";
import { marketPackApi } from "@/services/marketPackApi";
import { PageHeader, KpiCard, DataTable, LoadingState, ErrorState, EmptyState, StatusBadge } from "@/components/ui";

type AnyRecord = Record<string, any>;
type Tab = "canada" | "saudi";

// Fleet Compliance — regional market-pack readiness (Canada/NA + Saudi/GCC).
// Backend enforces market-pack entitlement (deny-by-default): a disabled pack
// returns 403, which is surfaced here as a "pack not enabled" notice rather than
// an error. No regional logic is hardcoded — the page is driven by API data.
export function FleetCompliancePage() {
  const [tab, setTab] = useState<Tab>("canada");
  return (
    <div className="space-y-6">
      <PageHeader title="Fleet Compliance" eyebrow="Market Packs" description="Market-pack readiness — Canada / North America and Saudi / GCC." />
      <div className="flex gap-2">
        <TabButton active={tab === "canada"} onClick={() => setTab("canada")}>Canada / North America</TabButton>
        <TabButton active={tab === "saudi"} onClick={() => setTab("saudi")}>Saudi / GCC</TabButton>
      </div>
      {tab === "canada" ? <CanadaReadiness /> : <SaudiReadiness />}
    </div>
  );
}

function TabButton({ active, onClick, children }: { active: boolean; onClick: () => void; children: React.ReactNode }) {
  return (
    <button onClick={onClick} className={`rounded-xl px-4 py-2 text-sm font-semibold transition ${active ? "bg-teal-500 text-white" : "border border-slate-300 dark:border-slate-700 text-slate-600 dark:text-slate-300"}`}>
      {children}
    </button>
  );
}

function NotEntitled({ pack }: { pack: string }) {
  return <EmptyState title={`${pack} Market Pack not enabled`} subtitle="A Platform Admin must enable this market pack for your tenant before compliance data is available." />;
}

function useEntitledLoader(loader: () => Promise<void>) {
  const [state, setState] = useState<"loading" | "ready" | "denied" | "error">("loading");
  const [message, setMessage] = useState<string>();
  const run = useCallback(() => {
    setState("loading");
    loader()
      .then(() => setState("ready"))
      .catch((e: any) => {
        if (e?.response?.status === 403) setState("denied");
        else { setMessage(e?.message ?? "Failed to load"); setState("error"); }
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  useEffect(() => { run(); }, [run]);
  return { state, message, reload: run };
}

// ───────────────────────────── Canada ─────────────────────────────
function CanadaReadiness() {
  const [docs, setDocs] = useState<AnyRecord[]>([]);
  const [inspections, setInspections] = useState<AnyRecord[]>([]);
  const [expiries, setExpiries] = useState<AnyRecord[]>([]);
  const [ifta, setIfta] = useState<AnyRecord | null>(null);
  const [hos, setHos] = useState<AnyRecord | null>(null);
  const [form, setForm] = useState({ subjectName: "", docKey: "drivers_license", expiryDate: "" });

  const load = useCallback(async () => {
    const [d, i, e, f, h] = await Promise.all([
      marketPackApi.driverDocuments(), marketPackApi.vehicleInspections(),
      marketPackApi.expiries(), marketPackApi.iftaReadiness(), marketPackApi.hosReadiness(),
    ]);
    setDocs((d?.items as AnyRecord[]) ?? []);
    setInspections((i?.items as AnyRecord[]) ?? []);
    setExpiries((e?.items as AnyRecord[]) ?? []);
    setIfta(f); setHos(h);
  }, []);
  const { state, message, reload } = useEntitledLoader(load);

  if (state === "loading") return <LoadingState />;
  if (state === "denied") return <NotEntitled pack="Canada / North America" />;
  if (state === "error") return <ErrorState message={message} />;

  const addDoc = async () => {
    if (!form.subjectName) return;
    await marketPackApi.createDriverDocument({ subjectType: "driver", ...form });
    setForm({ subjectName: "", docKey: "drivers_license", expiryDate: "" });
    reload();
  };
  const addInspection = async () => {
    await marketPackApi.createVehicleInspection({ vehicleLabel: "Truck", inspectionType: "pre_trip", status: "pass" });
    reload();
  };
  const addMileage = async () => { await marketPackApi.createJurisdictionMileage({ provinceState: "QC", distance: "1200", taxPeriod: "2026-Q2" }); reload(); };

  return (
    <div className="space-y-5">
      <div className="grid gap-4 sm:grid-cols-4">
        <KpiCard label="Driver/Vehicle docs" value={docs.length} />
        <KpiCard label="Inspections" value={inspections.length} />
        <KpiCard label="Expiry alerts" value={expiries.length} />
        <KpiCard label="ELD" value={(hos?.eldDevices ?? []).length ? "Registered" : "None"} />
      </div>

      <Section title="Driver Qualification & Documents">
        <div className="mb-3 flex flex-wrap gap-2">
          <input className="rounded-lg border border-slate-300 dark:border-slate-700 bg-transparent px-3 py-1.5 text-sm" placeholder="Driver name" value={form.subjectName} onChange={(e) => setForm({ ...form, subjectName: e.target.value })} />
          <select title="Document type" className="rounded-lg border border-slate-300 dark:border-slate-700 bg-transparent px-3 py-1.5 text-sm" value={form.docKey} onChange={(e) => setForm({ ...form, docKey: e.target.value })}>
            <option value="drivers_license">Driver's License</option>
            <option value="medical_certificate">Medical Certificate</option>
            <option value="endorsement">Endorsement</option>
          </select>
          <input type="date" title="Expiry date" placeholder="Expiry date" className="rounded-lg border border-slate-300 dark:border-slate-700 bg-transparent px-3 py-1.5 text-sm" value={form.expiryDate} onChange={(e) => setForm({ ...form, expiryDate: e.target.value })} />
          <button onClick={addDoc} className="rounded-lg bg-teal-500 px-3 py-1.5 text-sm font-semibold text-white">Add document</button>
        </div>
        {docs.length === 0 ? <EmptyState /> : <DataTable rows={docs} columns={["subjectName", "docKey", "documentNo", "issuingRegion", "expiryDate", "documentStatus"]} />}
      </Section>

      <Section title="Vehicle Inspections / DVIR Readiness" action={<button onClick={addInspection} className="rounded-lg bg-teal-500 px-3 py-1.5 text-sm font-semibold text-white">Add inspection</button>}>
        {inspections.length === 0 ? <EmptyState /> : <DataTable rows={inspections} columns={["vehicleLabel", "inspectionType", "status", "inspectorName", "inspectedAt"]} />}
      </Section>

      <Section title="IFTA Fuel-Tax Readiness" action={<button onClick={addMileage} className="rounded-lg bg-teal-500 px-3 py-1.5 text-sm font-semibold text-white">Add mileage</button>}>
        <p className="mb-2 text-xs text-slate-500">{ifta?.note}</p>
        {(ifta?.mileageByJurisdiction ?? []).length === 0 ? <EmptyState title="No jurisdiction records" /> : <DataTable rows={ifta?.mileageByJurisdiction ?? []} columns={["provinceState", "country", "distance", "distanceUnit", "taxPeriod"]} />}
      </Section>

      <Section title="HOS / ELD Readiness Foundation">
        <p className="mb-2 text-xs text-amber-600 dark:text-amber-400">{hos?.note}</p>
        {(hos?.dutyStatusRecords ?? []).length === 0 ? <EmptyState title="No duty-status records" /> : <DataTable rows={hos?.dutyStatusRecords ?? []} columns={["driverName", "dutyStatus", "hosCycle", "logCertificationStatus", "recordedAt"]} />}
      </Section>

      <Section title="Expiry Dashboard">
        {expiries.length === 0 ? <EmptyState title="No upcoming expiries" /> : <DataTable rows={expiries} columns={["subjectName", "docKey", "severity", "message", "expiryDate"]} />}
      </Section>
    </div>
  );
}

// ───────────────────────────── Saudi ─────────────────────────────
function SaudiReadiness() {
  const [docs, setDocs] = useState<AnyRecord[]>([]);
  const [expiries, setExpiries] = useState<AnyRecord[]>([]);
  const [vat, setVat] = useState<AnyRecord | null>(null);
  const [form, setForm] = useState({ subjectName: "", documentType: "transport_permit", gregorianExpiryDate: "", hijriExpiryDate: "" });

  const load = useCallback(async () => {
    const [d, e, v] = await Promise.all([marketPackApi.saudiDocuments(), marketPackApi.saudiExpiries(), marketPackApi.saudiVatReadiness()]);
    setDocs((d?.items as AnyRecord[]) ?? []);
    setExpiries((e?.items as AnyRecord[]) ?? []);
    setVat(v);
  }, []);
  const { state, message, reload } = useEntitledLoader(load);

  if (state === "loading") return <LoadingState />;
  if (state === "denied") return <NotEntitled pack="Saudi / GCC" />;
  if (state === "error") return <ErrorState message={message} />;

  const addDoc = async () => {
    if (!form.subjectName) return;
    await marketPackApi.createSaudiDocument(form);
    setForm({ subjectName: "", documentType: "transport_permit", gregorianExpiryDate: "", hijriExpiryDate: "" });
    reload();
  };
  const setReadiness = async (status: string) => { await marketPackApi.setSaudiVatReadiness({ eInvoiceReadinessStatus: status }); reload(); };

  const r = vat?.readiness ?? {};
  return (
    <div className="space-y-5">
      <div className="grid gap-4 sm:grid-cols-3">
        <KpiCard label="Transport documents" value={docs.length} />
        <KpiCard label="Expiry alerts" value={expiries.length} />
        <KpiCard label="e-Invoice readiness" value={(r?.eInvoiceReadinessStatus ?? "not_ready").replace("_", " ")} />
      </div>

      <Section title="VAT / e-Invoice Readiness">
        <p className="mb-2 text-xs text-slate-500">{vat?.note}</p>
        <div className="flex flex-wrap items-center gap-3 text-sm">
          <span>VAT: <b>{r?.vatNumber ?? "—"}</b></span>
          <span>CR: <b>{r?.commercialRegistrationNo ?? "—"}</b></span>
          <StatusBadge status={r?.eInvoiceReadinessStatus ?? "not_ready"} />
          <div className="flex gap-2">
            {["not_ready", "in_progress", "ready"].map((s) => (
              <button key={s} onClick={() => setReadiness(s)} className="rounded-lg border border-slate-300 dark:border-slate-700 px-2 py-1 text-xs">{s.replace("_", " ")}</button>
            ))}
          </div>
        </div>
      </Section>

      <Section title="Transport / Compliance Documents (Hijri & Gregorian)">
        <div className="mb-3 flex flex-wrap gap-2">
          <input className="rounded-lg border border-slate-300 dark:border-slate-700 bg-transparent px-3 py-1.5 text-sm" placeholder="Subject / vehicle" value={form.subjectName} onChange={(e) => setForm({ ...form, subjectName: e.target.value })} />
          <select title="Document type" className="rounded-lg border border-slate-300 dark:border-slate-700 bg-transparent px-3 py-1.5 text-sm" value={form.documentType} onChange={(e) => setForm({ ...form, documentType: e.target.value })}>
            <option value="transport_permit">Transport Permit</option>
            <option value="operating_card">Operating Card</option>
            <option value="istimara">Istimara</option>
          </select>
          <input type="date" title="Gregorian expiry" className="rounded-lg border border-slate-300 dark:border-slate-700 bg-transparent px-3 py-1.5 text-sm" value={form.gregorianExpiryDate} onChange={(e) => setForm({ ...form, gregorianExpiryDate: e.target.value })} />
          <input placeholder="Hijri expiry (1447-..)" className="rounded-lg border border-slate-300 dark:border-slate-700 bg-transparent px-3 py-1.5 text-sm" value={form.hijriExpiryDate} onChange={(e) => setForm({ ...form, hijriExpiryDate: e.target.value })} />
          <button onClick={addDoc} className="rounded-lg bg-teal-500 px-3 py-1.5 text-sm font-semibold text-white">Add document</button>
        </div>
        {docs.length === 0 ? <EmptyState /> : <DataTable rows={docs} columns={["subjectName", "docKey", "documentNo", "documentStatus", "hijriExpiryDate", "expiryDate"]} />}
      </Section>

      <Section title="Expiry Dashboard">
        {expiries.length === 0 ? <EmptyState title="No upcoming expiries" /> : <DataTable rows={expiries} columns={["subjectName", "docKey", "severity", "message", "expiryDate"]} />}
      </Section>
    </div>
  );
}

function Section({ title, action, children }: { title: string; action?: React.ReactNode; children: React.ReactNode }) {
  return (
    <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/60 dark:bg-slate-900/40 p-4">
      <div className="mb-3 flex items-center justify-between">
        <h3 className="text-sm font-semibold text-slate-700 dark:text-slate-200">{title}</h3>
        {action}
      </div>
      {children}
    </div>
  );
}
