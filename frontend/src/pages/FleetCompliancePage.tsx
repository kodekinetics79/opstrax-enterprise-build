import { useCallback, useEffect, useState } from "react";
import { ShieldAlert } from "lucide-react";
import { marketPackApi } from "@/services/marketPackApi";
import { KpiCard, DataTable, LoadingState, ErrorState, EmptyState, StatusBadge } from "@/components/ui";

type AnyRecord = Record<string, any>;
type Tab = "canada" | "saudi";

// Fleet Compliance — regional market-pack readiness (Canada/NA + Saudi/GCC).
// Backend enforces market-pack entitlement (deny-by-default): a disabled pack
// returns 403, which is surfaced here as a "pack not enabled" notice rather than
// an error. No regional logic is hardcoded — the page is driven by API data.
export function FleetCompliancePage() {
  const [tab, setTab] = useState<Tab>("canada");
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
                  <ShieldAlert className="h-3 w-3" /> Market Packs
                </span>
                <span className="text-[11px] font-semibold text-slate-500">Regional compliance readiness</span>
              </div>
              <h1 className="text-[32px] font-black tracking-tight leading-none cc-gradient-text sm:text-[36px]">
                Fleet Compliance
              </h1>
              <p className="mt-1 text-[13px] font-medium text-slate-400 tracking-wide">
                Canada / North America and Saudi / GCC market-pack readiness
              </p>
            </div>
          </div>
        </div>
      </header>

      <nav className="sticky top-4 z-20 rounded-2xl border border-slate-200 bg-white/95 p-2 shadow-sm backdrop-blur">
        <div className="grid gap-1 sm:grid-cols-2">
          <button
            type="button"
            onClick={() => setTab("canada")}
            className={`rounded-xl px-3 py-2.5 text-left transition ${
              tab === "canada" ? "bg-slate-900 text-white shadow-sm" : "bg-slate-50/40 hover:bg-slate-100"
            }`}
          >
            <div className="text-xs font-bold uppercase tracking-[0.14em]">Canada / North America</div>
            <div className={`mt-0.5 text-[11px] ${tab === "canada" ? "text-slate-300" : "text-slate-500"}`}>Regional compliance posture</div>
          </button>
          <button
            type="button"
            onClick={() => setTab("saudi")}
            className={`rounded-xl px-3 py-2.5 text-left transition ${
              tab === "saudi" ? "bg-slate-900 text-white shadow-sm" : "bg-slate-50/40 hover:bg-slate-100"
            }`}
          >
            <div className="text-xs font-bold uppercase tracking-[0.14em]">Saudi / GCC</div>
            <div className={`mt-0.5 text-[11px] ${tab === "saudi" ? "text-slate-300" : "text-slate-500"}`}>Regional compliance posture</div>
          </button>
        </div>
      </nav>

      {tab === "canada" ? <CanadaReadiness /> : <SaudiReadiness />}
    </div>
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
    <div className="space-y-6">
      <div className="grid gap-4 sm:grid-cols-4">
        <KpiCard label="Driver/Vehicle docs" value={docs.length} />
        <KpiCard label="Inspections" value={inspections.length} />
        <KpiCard label="Expiry alerts" value={expiries.length} />
        <KpiCard label="ELD" value={(hos?.eldDevices ?? []).length ? "Registered" : "None"} />
      </div>

      <section className="panel p-5">
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-lg font-semibold text-slate-900">Driver Qualification & Documents</h2>
            <p className="text-sm text-slate-500">Manage driver documentation and qualification records</p>
          </div>
        </div>
        <div className="mt-4 flex flex-wrap gap-3">
          <input className="w-full rounded-xl border border-slate-200 bg-white px-3 py-2.5 text-sm shadow-sm transition focus:border-teal-400 focus:ring-2 focus:ring-teal-100 sm:w-auto" placeholder="Driver name" value={form.subjectName} onChange={(e) => setForm({ ...form, subjectName: e.target.value })} />
          <select title="Document type" className="rounded-xl border border-slate-200 bg-white px-3 py-2.5 text-sm shadow-sm transition focus:border-teal-400 focus:ring-2 focus:ring-teal-100" value={form.docKey} onChange={(e) => setForm({ ...form, docKey: e.target.value })}>
            <option value="drivers_license">Driver's License</option>
            <option value="medical_certificate">Medical Certificate</option>
            <option value="endorsement">Endorsement</option>
          </select>
          <input type="date" title="Expiry date" placeholder="Expiry date" className="rounded-xl border border-slate-200 bg-white px-3 py-2.5 text-sm shadow-sm transition focus:border-teal-400 focus:ring-2 focus:ring-teal-100" value={form.expiryDate} onChange={(e) => setForm({ ...form, expiryDate: e.target.value })} />
          <button onClick={addDoc} className="fh-btn-primary">Add document</button>
        </div>
        <div className="mt-4">{docs.length === 0 ? <EmptyState /> : <DataTable rows={docs} columns={["subjectName", "docKey", "documentNo", "issuingRegion", "expiryDate", "documentStatus"]} />}</div>
      </section>

      <section className="panel p-5">
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-lg font-semibold text-slate-900">Vehicle Inspections / DVIR Readiness</h2>
            <p className="text-sm text-slate-500">Track vehicle inspection status and DVIR compliance</p>
          </div>
          <button onClick={addInspection} className="fh-btn-primary">Add inspection</button>
        </div>
        <div className="mt-4">{inspections.length === 0 ? <EmptyState /> : <DataTable rows={inspections} columns={["vehicleLabel", "inspectionType", "status", "inspectorName", "inspectedAt"]} />}</div>
      </section>

      <section className="panel p-5">
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-lg font-semibold text-slate-900">IFTA Fuel-Tax Readiness</h2>
            <p className="text-sm text-slate-500">Monitor jurisdiction mileage and fuel tax compliance</p>
          </div>
          <button onClick={addMileage} className="fh-btn-primary">Add mileage</button>
        </div>
        <p className="mt-3 text-xs text-slate-500">{ifta?.note}</p>
        <div className="mt-4">{(ifta?.mileageByJurisdiction ?? []).length === 0 ? <EmptyState title="No jurisdiction records" /> : <DataTable rows={ifta?.mileageByJurisdiction ?? []} columns={["provinceState", "country", "distance", "distanceUnit", "taxPeriod"]} />}</div>
      </section>

      <section className="panel p-5">
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-lg font-semibold text-slate-900">HOS / ELD Readiness Foundation</h2>
            <p className="text-sm text-slate-500">Track hours of service and ELD device compliance</p>
          </div>
        </div>
        <p className="mt-3 text-sm text-amber-600">{hos?.note}</p>
        <div className="mt-4">{(hos?.dutyStatusRecords ?? []).length === 0 ? <EmptyState title="No duty-status records" /> : <DataTable rows={hos?.dutyStatusRecords ?? []} columns={["driverName", "dutyStatus", "hosCycle", "logCertificationStatus", "recordedAt"]} />}</div>
      </section>

      <section className="panel p-5">
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-lg font-semibold text-slate-900">Expiry Dashboard</h2>
            <p className="text-sm text-slate-500">Monitor upcoming document and certification expiries</p>
          </div>
        </div>
        <div className="mt-4">{expiries.length === 0 ? <EmptyState title="No upcoming expiries" /> : <DataTable rows={expiries} columns={["subjectName", "docKey", "severity", "message", "expiryDate"]} />}</div>
      </section>
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
    <div className="space-y-6">
      <div className="grid gap-4 sm:grid-cols-3">
        <KpiCard label="Transport documents" value={docs.length} />
        <KpiCard label="Expiry alerts" value={expiries.length} />
        <KpiCard label="e-Invoice readiness" value={(r?.eInvoiceReadinessStatus ?? "not_ready").replace("_", " ")} />
      </div>

      <section className="panel p-5">
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-lg font-semibold text-slate-900">VAT / e-Invoice Readiness</h2>
            <p className="text-sm text-slate-500">Monitor VAT registration and e-invoice compliance status</p>
          </div>
        </div>
        <p className="mt-3 text-sm text-slate-500">{vat?.note}</p>
        <div className="mt-4 flex flex-wrap items-center gap-3">
          <div className="rounded-xl border border-slate-200 bg-slate-50 px-4 py-3">
            <p className="text-[11px] font-semibold uppercase tracking-[0.14em] text-slate-400">VAT Number</p>
            <p className="mt-1 font-semibold text-slate-900">{r?.vatNumber ?? "—"}</p>
          </div>
          <div className="rounded-xl border border-slate-200 bg-slate-50 px-4 py-3">
            <p className="text-[11px] font-semibold uppercase tracking-[0.14em] text-slate-400">Commercial Registration</p>
            <p className="mt-1 font-semibold text-slate-900">{r?.commercialRegistrationNo ?? "—"}</p>
          </div>
          <StatusBadge status={r?.eInvoiceReadinessStatus ?? "not_ready"} />
          <div className="flex gap-2">
            {["not_ready", "in_progress", "ready"].map((s) => (
              <button key={s} onClick={() => setReadiness(s)} className="rounded-xl border border-slate-200 bg-white px-3 py-2 text-xs font-semibold text-slate-700 shadow-sm transition hover:border-teal-300 hover:bg-teal-50">{s.replace("_", " ")}</button>
            ))}
          </div>
        </div>
      </section>

      <section className="panel p-5">
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-lg font-semibold text-slate-900">Transport / Compliance Documents (Hijri & Gregorian)</h2>
            <p className="text-sm text-slate-500">Manage transport permits and compliance documentation</p>
          </div>
        </div>
        <div className="mt-4 flex flex-wrap gap-3">
          <input className="w-full rounded-xl border border-slate-200 bg-white px-3 py-2.5 text-sm shadow-sm transition focus:border-teal-400 focus:ring-2 focus:ring-teal-100 sm:w-auto" placeholder="Subject / vehicle" value={form.subjectName} onChange={(e) => setForm({ ...form, subjectName: e.target.value })} />
          <select title="Document type" className="rounded-xl border border-slate-200 bg-white px-3 py-2.5 text-sm shadow-sm transition focus:border-teal-400 focus:ring-2 focus:ring-teal-100" value={form.documentType} onChange={(e) => setForm({ ...form, documentType: e.target.value })}>
            <option value="transport_permit">Transport Permit</option>
            <option value="operating_card">Operating Card</option>
            <option value="istimara">Istimara</option>
          </select>
          <input type="date" title="Gregorian expiry" className="rounded-xl border border-slate-200 bg-white px-3 py-2.5 text-sm shadow-sm transition focus:border-teal-400 focus:ring-2 focus:ring-teal-100" value={form.gregorianExpiryDate} onChange={(e) => setForm({ ...form, gregorianExpiryDate: e.target.value })} />
          <input placeholder="Hijri expiry (1447-..)" className="rounded-xl border border-slate-200 bg-white px-3 py-2.5 text-sm shadow-sm transition focus:border-teal-400 focus:ring-2 focus:ring-teal-100" value={form.hijriExpiryDate} onChange={(e) => setForm({ ...form, hijriExpiryDate: e.target.value })} />
          <button onClick={addDoc} className="fh-btn-primary">Add document</button>
        </div>
        <div className="mt-4">{docs.length === 0 ? <EmptyState /> : <DataTable rows={docs} columns={["subjectName", "docKey", "documentNo", "documentStatus", "hijriExpiryDate", "expiryDate"]} />}</div>
      </section>

      <section className="panel p-5">
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-lg font-semibold text-slate-900">Expiry Dashboard</h2>
            <p className="text-sm text-slate-500">Monitor upcoming document and certification expiries</p>
          </div>
        </div>
        <div className="mt-4">{expiries.length === 0 ? <EmptyState title="No upcoming expiries" /> : <DataTable rows={expiries} columns={["subjectName", "docKey", "severity", "message", "expiryDate"]} />}</div>
      </section>
    </div>
  );
}


