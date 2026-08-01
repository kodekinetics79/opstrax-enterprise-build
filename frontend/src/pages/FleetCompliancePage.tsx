import { useCallback, useEffect, useState } from "react";
import { ArrowRight, RefreshCw, Sparkles } from "lucide-react";
import { marketPackApi } from "@/services/marketPackApi";
import { PageHeader, KpiCard, DataTable, LoadingState, ErrorState, EmptyState, StatusBadge } from "@/components/ui";
import { useHasPermission } from "@/hooks/usePermission";

type AnyRecord = Record<string, any>;
type Tab = "canada" | "saudi";

// Fleet Compliance — regional market-pack readiness (Canada/NA + Saudi/GCC).
// Canada remains market-pack entitlement-based. Saudi/GCC now uses the live
// Saudi readiness foundation so the tab shows real tenant data instead of a
// dead not-enabled state.
export function FleetCompliancePage({ initialTab = "canada" }: { initialTab?: Tab } = {}) {
  const [tab, setTab] = useState<Tab>(initialTab);
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
  const hasPermission = useHasPermission();
  const canManage = hasPermission("compliance:manage");
  const [docs, setDocs] = useState<AnyRecord[]>([]);
  const [inspections, setInspections] = useState<AnyRecord[]>([]);
  const [expiries, setExpiries] = useState<AnyRecord[]>([]);
  const [ifta, setIfta] = useState<AnyRecord | null>(null);
  const [hos, setHos] = useState<AnyRecord | null>(null);
  const [form, setForm] = useState({ subjectName: "", docKey: "drivers_license", expiryDate: "" });
  const [inspectionForm, setInspectionForm] = useState({ vehicleLabel: "", inspectorName: "", inspectionType: "pre_trip", status: "pass", defectDescription: "", defectSeverity: "minor" });
  const [mileageForm, setMileageForm] = useState({ provinceState: "", country: "CA", distance: "", taxPeriod: "" });
  const [fuelForm, setFuelForm] = useState({ provinceState: "", country: "CA", fuelVolume: "", taxPeriod: "" });
  const [mutationError, setMutationError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

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
    if (!canManage || !form.subjectName) return;
    await marketPackApi.createDriverDocument({ subjectType: "driver", ...form });
    setForm({ subjectName: "", docKey: "drivers_license", expiryDate: "" });
    reload();
  };
  const addInspection = async () => {
    if (!canManage) return;
    if (!inspectionForm.vehicleLabel.trim() || !inspectionForm.inspectorName.trim()) {
      setMutationError("Vehicle and inspector are required before recording an inspection.");
      return;
    }
    setSaving(true);
    setMutationError(null);
    try {
      if ((inspectionForm.status === "fail" || inspectionForm.status === "needs_repair") && !inspectionForm.defectDescription.trim()) {
        setMutationError("Failed and repair-required inspections need a defect description."); setSaving(false); return;
      }
      const { defectDescription, defectSeverity, ...inspection } = inspectionForm;
      await marketPackApi.createVehicleInspection({ ...inspection, defects: inspection.status === "pass" ? [] : [{ description: defectDescription, severity: defectSeverity, repairRequired: true }] });
      setInspectionForm({ vehicleLabel: "", inspectorName: "", inspectionType: "pre_trip", status: "pass", defectDescription: "", defectSeverity: "minor" });
      reload();
    } catch (e: any) {
      setMutationError(e?.response?.data?.message ?? e?.message ?? "Unable to save inspection.");
    } finally {
      setSaving(false);
    }
  };
  const addMileage = async () => {
    if (!canManage) return;
    if (!mileageForm.provinceState.trim() || Number(mileageForm.distance) <= 0 || !/^\d{4}-Q[1-4]$/.test(mileageForm.taxPeriod)) {
      setMutationError("Province/state, positive distance, and a tax period such as 2026-Q2 are required.");
      return;
    }
    setSaving(true);
    setMutationError(null);
    try {
      await marketPackApi.createJurisdictionMileage(mileageForm);
      setMileageForm({ provinceState: "", country: "CA", distance: "", taxPeriod: "" });
      reload();
    } catch (e: any) {
      setMutationError(e?.response?.data?.message ?? e?.message ?? "Unable to save jurisdiction mileage.");
    } finally {
      setSaving(false);
    }
  };
  const addFuel = async () => {
    if (!canManage) return;
    if (!fuelForm.provinceState.trim() || Number(fuelForm.fuelVolume) <= 0 || !/^\d{4}-Q[1-4]$/.test(fuelForm.taxPeriod)) {
      setMutationError("Province/state, positive fuel volume, and a tax period such as 2026-Q2 are required.");
      return;
    }
    setSaving(true); setMutationError(null);
    try {
      await marketPackApi.createJurisdictionFuel({ ...fuelForm, fuelUnit: "liter" });
      setFuelForm({ provinceState: "", country: "CA", fuelVolume: "", taxPeriod: "" });
      reload();
    } catch (e: any) { setMutationError(e?.response?.data?.message ?? e?.message ?? "Unable to save jurisdiction fuel."); }
    finally { setSaving(false); }
  };

  return (
    <div className="space-y-5">
      {mutationError ? <div className="rounded-xl border border-rose-200 bg-rose-50 p-3 text-sm text-rose-700">{mutationError}</div> : null}
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
          <button onClick={addDoc} disabled={!canManage} className="rounded-lg bg-teal-500 px-3 py-1.5 text-sm font-semibold text-white disabled:opacity-60">Add document</button>
        </div>
        {docs.length === 0 ? <EmptyState /> : <DataTable rows={docs} columns={["subjectName", "docKey", "documentNo", "issuingRegion", "expiryDate", "documentStatus"]} />}
      </Section>

      <Section title="Vehicle Inspections / DVIR Readiness">
        <div className="mb-3 flex flex-wrap gap-2">
          <input className="rounded-lg border border-slate-300 bg-transparent px-3 py-1.5 text-sm dark:border-slate-700" placeholder="Vehicle ID / label" value={inspectionForm.vehicleLabel} onChange={(e) => setInspectionForm({ ...inspectionForm, vehicleLabel: e.target.value })} />
          <input className="rounded-lg border border-slate-300 bg-transparent px-3 py-1.5 text-sm dark:border-slate-700" placeholder="Inspector name" value={inspectionForm.inspectorName} onChange={(e) => setInspectionForm({ ...inspectionForm, inspectorName: e.target.value })} />
          <select title="Inspection type" className="rounded-lg border border-slate-300 bg-transparent px-3 py-1.5 text-sm dark:border-slate-700" value={inspectionForm.inspectionType} onChange={(e) => setInspectionForm({ ...inspectionForm, inspectionType: e.target.value })}>
            <option value="pre_trip">Pre-trip</option><option value="post_trip">Post-trip</option><option value="annual">Annual</option>
          </select>
          <select title="Inspection result" className="rounded-lg border border-slate-300 bg-transparent px-3 py-1.5 text-sm dark:border-slate-700" value={inspectionForm.status} onChange={(e) => setInspectionForm({ ...inspectionForm, status: e.target.value })}>
            <option value="pass">Pass</option><option value="fail">Fail</option><option value="needs_repair">Needs repair</option>
          </select>
          {inspectionForm.status !== "pass" ? <>
            <input className="rounded-lg border border-slate-300 bg-transparent px-3 py-1.5 text-sm dark:border-slate-700" placeholder="Defect description" value={inspectionForm.defectDescription} onChange={(e) => setInspectionForm({ ...inspectionForm, defectDescription: e.target.value })} />
            <select title="Defect severity" className="rounded-lg border border-slate-300 bg-transparent px-3 py-1.5 text-sm dark:border-slate-700" value={inspectionForm.defectSeverity} onChange={(e) => setInspectionForm({ ...inspectionForm, defectSeverity: e.target.value })}><option value="minor">Minor</option><option value="major">Major</option><option value="critical">Critical</option></select>
          </> : null}
          <button onClick={addInspection} disabled={!canManage || saving} className="rounded-lg bg-teal-500 px-3 py-1.5 text-sm font-semibold text-white disabled:opacity-60">Record inspection</button>
        </div>
        {inspections.length === 0 ? <EmptyState /> : <DataTable rows={inspections} columns={["vehicleLabel", "inspectionType", "status", "outOfService", "repairStatus", "inspectorName", "inspectedAt"]} />}
        <div className="mt-3 flex flex-wrap gap-2">
          {inspections.filter((row) => row.repairStatus === "open").map((row) => <button key={String(row.id)} disabled={!canManage || saving} onClick={async () => {
            const repairCertifiedBy = window.prompt("Repair certifier name"); if (!repairCertifiedBy) return;
            setSaving(true); setMutationError(null);
            try { await marketPackApi.updateVehicleInspection(Number(row.id), { status: "pass", repairCertified: true, repairCertifiedBy }); reload(); }
            catch (e: any) { setMutationError(e?.response?.data?.message ?? e?.message ?? "Unable to certify repair."); }
            finally { setSaving(false); }
          }} className="rounded-lg border border-teal-500 px-3 py-1.5 text-xs font-semibold text-teal-700 disabled:opacity-60">Certify repair: {String(row.vehicleLabel ?? row.vehicleId)}</button>)}
        </div>
      </Section>

      <Section title="IFTA Fuel-Tax Preview">
        <p className="mb-2 text-xs font-semibold text-amber-600 dark:text-amber-400">{ifta?.note}</p>
        <div className="mb-3 flex flex-wrap gap-2">
          <input className="rounded-lg border border-slate-300 bg-transparent px-3 py-1.5 text-sm dark:border-slate-700" placeholder="Province / state" value={mileageForm.provinceState} onChange={(e) => setMileageForm({ ...mileageForm, provinceState: e.target.value.toUpperCase() })} />
          <select title="Country" className="rounded-lg border border-slate-300 bg-transparent px-3 py-1.5 text-sm dark:border-slate-700" value={mileageForm.country} onChange={(e) => setMileageForm({ ...mileageForm, country: e.target.value })}><option value="CA">Canada</option><option value="US">United States</option></select>
          <input type="number" min="0.01" step="0.01" className="rounded-lg border border-slate-300 bg-transparent px-3 py-1.5 text-sm dark:border-slate-700" placeholder="Distance (km)" value={mileageForm.distance} onChange={(e) => setMileageForm({ ...mileageForm, distance: e.target.value })} />
          <input className="rounded-lg border border-slate-300 bg-transparent px-3 py-1.5 text-sm dark:border-slate-700" placeholder="Tax period (2026-Q2)" value={mileageForm.taxPeriod} onChange={(e) => setMileageForm({ ...mileageForm, taxPeriod: e.target.value.toUpperCase() })} />
          <button onClick={addMileage} disabled={!canManage || saving} className="rounded-lg bg-teal-500 px-3 py-1.5 text-sm font-semibold text-white disabled:opacity-60">Record mileage</button>
        </div>
        <div className="mb-3 flex flex-wrap gap-2">
          <input className="rounded-lg border border-slate-300 bg-transparent px-3 py-1.5 text-sm dark:border-slate-700" placeholder="Province / state" value={fuelForm.provinceState} onChange={(e) => setFuelForm({ ...fuelForm, provinceState: e.target.value.toUpperCase() })} />
          <select title="Country" className="rounded-lg border border-slate-300 bg-transparent px-3 py-1.5 text-sm dark:border-slate-700" value={fuelForm.country} onChange={(e) => setFuelForm({ ...fuelForm, country: e.target.value })}><option value="CA">Canada</option><option value="US">United States</option></select>
          <input type="number" min="0.01" step="0.01" className="rounded-lg border border-slate-300 bg-transparent px-3 py-1.5 text-sm dark:border-slate-700" placeholder="Fuel (litres)" value={fuelForm.fuelVolume} onChange={(e) => setFuelForm({ ...fuelForm, fuelVolume: e.target.value })} />
          <input className="rounded-lg border border-slate-300 bg-transparent px-3 py-1.5 text-sm dark:border-slate-700" placeholder="Tax period (2026-Q2)" value={fuelForm.taxPeriod} onChange={(e) => setFuelForm({ ...fuelForm, taxPeriod: e.target.value.toUpperCase() })} />
          <button onClick={addFuel} disabled={!canManage || saving} className="rounded-lg bg-teal-500 px-3 py-1.5 text-sm font-semibold text-white disabled:opacity-60">Record fuel</button>
        </div>
        {(ifta?.mileageByJurisdiction ?? []).length === 0 ? <EmptyState title="No jurisdiction records" /> : <DataTable rows={ifta?.mileageByJurisdiction ?? []} columns={["provinceState", "country", "distance", "distanceUnit", "taxPeriod"]} />}
        {(ifta?.fuelByJurisdiction ?? []).length === 0 ? null : <div className="mt-3"><DataTable rows={ifta?.fuelByJurisdiction ?? []} columns={["provinceState", "country", "fuelVolume", "fuelUnit", "taxPeriod"]} /></div>}
      </Section>

      <Section title="HOS / ELD Preview">
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
  const hasPermission = useHasPermission();
  const canManage = hasPermission("compliance:manage");
  const [regions, setRegions] = useState<AnyRecord[]>([]);
  const [docs, setDocs] = useState<AnyRecord[]>([]);
  const [expiries, setExpiries] = useState<AnyRecord[]>([]);
  const [invoice, setInvoice] = useState<AnyRecord | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [form, setForm] = useState({ subjectName: "", documentType: "transport_permit", gregorianExpiryDate: "", hijriExpiryDate: "" });

  const load = useCallback(async () => {
    const [r, d, e, v] = await Promise.all([
      marketPackApi.saudiRegions(),
      marketPackApi.saudiDocuments(),
      marketPackApi.saudiExpiries(),
      marketPackApi.saudiVatReadiness(),
    ]);
    setRegions((r?.items as AnyRecord[]) ?? []);
    setDocs((d?.items as AnyRecord[]) ?? []);
    setExpiries((e?.items as AnyRecord[]) ?? []);
    setInvoice(v as AnyRecord);
  }, []);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    load()
      .catch((e: any) => { if (!cancelled) setError(e?.message ?? "Failed to load Saudi readiness data."); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [load]);

  const reload = async () => {
    setRefreshing(true);
    try {
      setError(null);
      await load();
    } catch (e: any) {
      setError(e?.message ?? "Failed to refresh Saudi readiness data.");
    } finally {
      setRefreshing(false);
    }
  };

  const addDoc = async () => {
    if (!canManage || !form.subjectName) return;
    setSaving(true);
    try {
      await marketPackApi.createSaudiDocument({
        subjectType: "vehicle",
        subjectName: form.subjectName,
        documentType: form.documentType,
        gregorianExpiryDate: form.gregorianExpiryDate || null,
        hijriExpiryDate: form.hijriExpiryDate || null,
      });
      setForm({ subjectName: "", documentType: "transport_permit", gregorianExpiryDate: "", hijriExpiryDate: "" });
      await reload();
    } catch (e: any) {
      setError(e?.response?.data?.message ?? e?.message ?? "Failed to save Saudi readiness document.");
    } finally {
      setSaving(false);
    }
  };

  if (loading) return <LoadingState />;
  if (error) return <ErrorState message={error} />;

  const vatProfile = invoice?.readiness as AnyRecord | undefined;
  const vatStatus = String(vatProfile?.eInvoiceReadinessStatus ?? "not_ready");
  const readinessPercent = vatStatus === "ready" ? 100 : vatStatus === "in_progress" ? 50 : 0;
  const kpis = [
    { label: "Regions", value: regions.length },
    { label: "Documents", value: docs.length },
    { label: "Expiry alerts", value: expiries.length },
    { label: "VAT / e-Invoice", value: vatStatus.replaceAll("_", " ") },
  ];

  return (
    <div className="space-y-5">
      <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/40 p-5 shadow-sm">
        <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
          <div className="max-w-3xl">
            <div className="mb-3 inline-flex items-center gap-2 rounded-full border border-amber-200 bg-amber-50 px-3 py-1 text-[11px] font-bold uppercase tracking-[0.24em] text-amber-700">
              <Sparkles className="h-3.5 w-3.5" />
              Saudi/GCC readiness foundation
            </div>
            <h2 className="text-2xl font-black text-slate-950 dark:text-white">Compliance &amp; invoice readiness</h2>
            <p className="mt-2 text-sm leading-6 text-slate-600 dark:text-slate-300">
              Track regional coverage, compliance documents, expiry alerts, and invoice readiness across your fleet in one view.
            </p>
            <div className="mt-4 flex flex-wrap gap-2">
              <StatusBadge status={readinessPercent === 100 ? "Ready" : readinessPercent > 0 ? "In progress" : "Not ready"} />
              <span className="rounded-full bg-slate-100 px-3 py-1 text-[11px] font-semibold text-slate-600 dark:bg-white/5 dark:text-slate-300">VAT profile readiness {readinessPercent}%</span>
            </div>
          </div>
          <div className="flex flex-col gap-2 md:items-end">
            <button onClick={reload} className="btn-secondary inline-flex items-center gap-2">
              <RefreshCw className={`h-4 w-4 ${refreshing ? "animate-spin" : ""}`} />
              Refresh live data
            </button>
            <a href="/fleet-saudi-readiness" className="btn-primary inline-flex items-center gap-2">
              Open full Saudi workspace
              <ArrowRight className="h-4 w-4" />
            </a>
          </div>
        </div>
        <div className="mt-5 grid gap-3 sm:grid-cols-4">
          {kpis.map((item) => (
            <div key={item.label} className="rounded-xl border border-slate-200 bg-slate-50/80 p-4 dark:border-white/10 dark:bg-white/[0.03]">
              <p className="text-xs uppercase tracking-[0.22em] text-slate-400">{item.label}</p>
              <p className="mt-1 text-2xl font-black text-slate-950 dark:text-white">{item.value}</p>
            </div>
          ))}
        </div>
        <div className="mt-5 grid gap-3 sm:grid-cols-3">
          {regions.slice(0, 3).map((region) => (
            <div key={String(region.id)} className="rounded-xl border border-slate-200 bg-slate-50/70 p-4 dark:border-white/10 dark:bg-white/[0.03]">
              <p className="text-xs uppercase tracking-[0.22em] text-slate-400">Region</p>
              <p className="mt-1 font-bold text-slate-900 dark:text-white">{region.nameEn}</p>
              <p className="text-xs text-slate-500">{Array.isArray(region.cities) ? region.cities.join(" · ") : ""}</p>
            </div>
          ))}
        </div>
      </div>

      <div className="grid gap-4 sm:grid-cols-3">
        <KpiCard label="Transport documents" value={docs.length} />
        <KpiCard label="Expiry alerts" value={expiries.length} />
        <KpiCard label="e-Invoice readiness" value={`${readinessPercent}%`} />
      </div>

      <Section title="VAT / e-Invoice Readiness">
        <p className="mb-3 text-xs text-slate-500">Canonical branch VAT evidence used by the Saudi compliance pack. This is readiness evidence, not a direct ZATCA certification.</p>
        <div className="flex flex-wrap items-center gap-3 text-sm">
          <span>VAT number: <b>{vatProfile?.vatNumber || "Missing"}</b></span>
          <span>Commercial registration: <b>{vatProfile?.commercialRegistrationNo || "Missing"}</b></span>
          <StatusBadge status={readinessPercent === 100 ? "Ready" : readinessPercent > 0 ? "In progress" : "Not ready"} />
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
          <button onClick={addDoc} disabled={!canManage || saving} className="rounded-lg bg-teal-500 px-3 py-1.5 text-sm font-semibold text-white disabled:opacity-60">Add document</button>
        </div>
        {docs.length === 0 ? <EmptyState /> : <DataTable rows={docs} columns={["subjectName", "subjectType", "docKey", "documentNo", "documentStatus", "expiryBasis", "expiryDate", "hijriExpiryDate"]} />}
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
