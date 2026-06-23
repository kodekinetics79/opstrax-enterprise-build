import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { Activity, AlertTriangle, CheckCircle2, ChevronRight, Link2, RefreshCw, Search, Settings2, X, Zap } from "lucide-react";
import { apiClient } from "@/services/apiClient";
import { withFallback } from "@/services/fleetDomainApi";
import { exportCsv, LoadingState } from "@/components/ui";
import { useHasPermission } from "@/hooks/usePermission";
import type { AnyRecord } from "@/types";

// ── Integration catalog ────────────────────────────────────────────────────────

const CATALOG: AnyRecord[] = [
  // ERP
  { id: 1,  name: "SAP S/4HANA",           cat: "ERP",            status: "Connected",    sync: "2 min ago",    logo: "SAP",    desc: "Enterprise resource planning — orders, invoices, GL posting and cost centers." },
  { id: 2,  name: "Oracle NetSuite",        cat: "ERP",            status: "Disconnected", sync: "—",            logo: "ORA",    desc: "Cloud ERP for financials, inventory, order management and multi-subsidiary." },
  { id: 3,  name: "Microsoft Dynamics 365", cat: "ERP",            status: "Pending",      sync: "—",            logo: "MS",     desc: "Integrated ERP + CRM with Power BI dashboards and Azure AD SSO." },
  { id: 4,  name: "Odoo",                   cat: "ERP",            status: "Disconnected", sync: "—",            logo: "ODO",    desc: "Open-source ERP covering accounting, inventory, fleet and project management." },
  // Finance
  { id: 5,  name: "QuickBooks Online",      cat: "Finance",        status: "Connected",    sync: "5 min ago",    logo: "QB",     desc: "SMB accounting — invoices, expenses, payments and payroll integration." },
  { id: 6,  name: "Xero",                   cat: "Finance",        status: "Error",        sync: "14h ago",      logo: "XER",    desc: "Online accounting with bank reconciliation, multi-currency and reporting." },
  { id: 7,  name: "Sage Intacct",           cat: "Finance",        status: "Disconnected", sync: "—",            logo: "SGE",    desc: "Cloud financial management with revenue recognition and project accounting." },
  { id: 8,  name: "FreshBooks",             cat: "Finance",        status: "Disconnected", sync: "—",            logo: "FB",     desc: "Invoicing and expense tracking designed for service businesses." },
  // Telematics / ELD
  { id: 9,  name: "Samsara",                cat: "Telematics",     status: "Connected",    sync: "Real-time",    logo: "SAM",    desc: "GPS tracking, AI dashcam, ELD compliance and driver safety events." },
  { id: 10, name: "Geotab",                 cat: "Telematics",     status: "Disconnected", sync: "—",            logo: "GEO",    desc: "Fleet telematics hardware and MyGeotab data platform with open SDK." },
  { id: 11, name: "Verizon Connect",        cat: "Telematics",     status: "Disconnected", sync: "—",            logo: "VZN",    desc: "Fleet tracking, dispatching, driver log and maintenance scheduling." },
  { id: 12, name: "Motive (ELD)",           cat: "Telematics",     status: "Pending",      sync: "—",            logo: "MOT",    desc: "ELD mandate compliance, DVIR, IFTA and driver coaching on one device." },
  { id: 13, name: "Platform Science",       cat: "Telematics",     status: "Disconnected", sync: "—",            logo: "PS",     desc: "Open carrier platform for driver apps, dispatch and compliance workflows." },
  // Fuel Cards
  { id: 14, name: "WEX Fuel Card",          cat: "Fuel Cards",     status: "Connected",    sync: "1 min ago",    logo: "WEX",    desc: "Fleet fuel card transactions, odometer capture and anomaly detection." },
  { id: 15, name: "Fleetcor / Corpay",      cat: "Fuel Cards",     status: "Pending",      sync: "—",            logo: "FLC",    desc: "Corporate fuel and payments network with cost controls per vehicle." },
  { id: 16, name: "Comdata",                cat: "Fuel Cards",     status: "Disconnected", sync: "—",            logo: "CMD",    desc: "Fuel, cash and tolls card with real-time transaction alerts." },
  { id: 17, name: "Shell Fleet",            cat: "Fuel Cards",     status: "Disconnected", sync: "—",            logo: "SHL",    desc: "Global fuel card with MENA, Europe and North America coverage." },
  // Maps & Routing
  { id: 18, name: "Google Maps Platform",   cat: "Maps & Routing", status: "Connected",    sync: "Active",       logo: "GGL",    desc: "Routing, geocoding, distance matrix and live traffic layer." },
  { id: 19, name: "HERE Maps",              cat: "Maps & Routing", status: "Disconnected", sync: "—",            logo: "HRE",    desc: "Offline tile support, truck routing and MENA regional coverage." },
  { id: 20, name: "Mapbox",                 cat: "Maps & Routing", status: "Disconnected", sync: "—",            logo: "MPB",    desc: "Customizable vector maps with real-time data overlays and high performance." },
  { id: 21, name: "PTV Route Optimiser",    cat: "Maps & Routing", status: "Disconnected", sync: "—",            logo: "PTV",    desc: "Multi-stop route optimization with time windows, vehicle constraints and CO₂." },
  // Messaging
  { id: 22, name: "WhatsApp Business API",  cat: "Messaging",      status: "Connected",    sync: "Just now",     logo: "WA",     desc: "Customer notifications — ETA, POD confirmation and delay alerts." },
  { id: 23, name: "Twilio SMS",             cat: "Messaging",      status: "Connected",    sync: "8 min ago",    logo: "TWL",    desc: "SMS delivery for driver and customer communications with delivery receipts." },
  { id: 24, name: "Slack",                  cat: "Messaging",      status: "Connected",    sync: "3 min ago",    logo: "SLK",    desc: "Team alerting — critical events, escalations and dispatch notifications." },
  { id: 25, name: "Microsoft Teams",        cat: "Messaging",      status: "Disconnected", sync: "—",            logo: "MST",    desc: "Enterprise chat and channel alerts for operations and compliance teams." },
  { id: 26, name: "SendGrid Email",         cat: "Messaging",      status: "Connected",    sync: "12 min ago",   logo: "SG",     desc: "Transactional email for invoices, reports, alerts and compliance notices." },
  // HR & Payroll
  { id: 27, name: "ADP Workforce Now",      cat: "HR & Payroll",   status: "Disconnected", sync: "—",            logo: "ADP",    desc: "Payroll processing, benefits, time tracking and HR compliance." },
  { id: 28, name: "BambooHR",               cat: "HR & Payroll",   status: "Pending",      sync: "—",            logo: "BBR",    desc: "Employee records, onboarding, performance reviews and driver documents." },
  { id: 29, name: "Workday",                cat: "HR & Payroll",   status: "Disconnected", sync: "—",            logo: "WD",     desc: "Enterprise HCM with workforce planning, payroll and analytics." },
  // WMS & E-Commerce
  { id: 30, name: "SAP Extended WMS",       cat: "WMS",            status: "Connected",    sync: "4 min ago",    logo: "SWMS",   desc: "Warehouse management with slot, wave picking and carrier booking." },
  { id: 31, name: "Manhattan WMS",          cat: "WMS",            status: "Disconnected", sync: "—",            logo: "MWM",    desc: "Enterprise WMS for distribution centers with labor management." },
  { id: 32, name: "Shopify",                cat: "E-Commerce",     status: "Pending",      sync: "—",            logo: "SHO",    desc: "Order import, shipment status push and POD confirmation back to storefront." },
  { id: 33, name: "WooCommerce",            cat: "E-Commerce",     status: "Disconnected", sync: "—",            logo: "WOO",    desc: "WordPress e-commerce plugin with order-to-dispatch automation." },
  // IoT & Sensors
  { id: 34, name: "AWS IoT Core",           cat: "IoT & Sensors",  status: "Connected",    sync: "Real-time",    logo: "AWS",    desc: "Cloud IoT message broker for sensor telemetry and device shadow state." },
  { id: 35, name: "Azure IoT Hub",          cat: "IoT & Sensors",  status: "Disconnected", sync: "—",            logo: "AZR",    desc: "Enterprise IoT gateway with device management and stream analytics." },
  { id: 36, name: "Trimble TMT",            cat: "IoT & Sensors",  status: "Disconnected", sync: "—",            logo: "TMB",    desc: "Fleet maintenance management with OEM warranty, recall and compliance." },
  // Insurance & Compliance
  { id: 37, name: "Marsh Risk Analytics",   cat: "Insurance",      status: "Disconnected", sync: "—",            logo: "MRS",    desc: "Fleet insurance analytics — risk scoring, incident reporting and premium management." },
  { id: 38, name: "Lytx DriveCam",          cat: "Insurance",      status: "Disconnected", sync: "—",            logo: "LYX",    desc: "Video-based safety with risky driving event detection and coaching." },
  { id: 39, name: "FMCSA Portal",           cat: "Compliance",     status: "Connected",    sync: "Daily",        logo: "FMC",    desc: "DOT compliance data — carrier authority, safety ratings and inspections." },
  { id: 40, name: "IFTA Reporting",         cat: "Compliance",     status: "Connected",    sync: "Weekly",       logo: "IFT",    desc: "Interstate fuel tax agreement quarterly filing with mileage reconciliation." },
];

const ALL_CATS = ["All", ...Array.from(new Set(CATALOG.map((i) => String(i.cat))))];

const CAT_COLOR: Record<string, string> = {
  "ERP":            "bg-blue-50 border-blue-200 text-blue-700",
  "Finance":        "bg-teal-50 border-teal-200 text-teal-700",
  "Fuel Cards":     "bg-amber-50 border-amber-200 text-amber-700",
  "Messaging":      "bg-violet-50 border-violet-200 text-violet-700",
  "Maps & Routing": "bg-green-50 border-green-200 text-green-700",
  "Telematics":     "bg-orange-50 border-orange-200 text-orange-700",
  "WMS":            "bg-indigo-50 border-indigo-200 text-indigo-700",
  "E-Commerce":     "bg-pink-50 border-pink-200 text-pink-700",
  "HR & Payroll":   "bg-cyan-50 border-cyan-200 text-cyan-700",
  "IoT & Sensors":  "bg-red-50 border-red-200 text-red-700",
  "Insurance":      "bg-slate-100 border-slate-300 text-slate-700",
  "Compliance":     "bg-emerald-50 border-emerald-200 text-emerald-700",
};

const SEED_ACTIVITY: AnyRecord[] = [
  { id: 1, integration: "SAP S/4HANA",          event: "Invoice batch synced",              ts: "2m ago",  status: "Success", records: 34 },
  { id: 2, integration: "WEX Fuel Card",          event: "Transaction import completed",      ts: "4m ago",  status: "Success", records: 12 },
  { id: 3, integration: "Google Maps Platform",   event: "Distance matrix refreshed",         ts: "5m ago",  status: "Success", records: 1 },
  { id: 4, integration: "WhatsApp Business API",  event: "ETA notifications delivered",       ts: "8m ago",  status: "Success", records: 7 },
  { id: 5, integration: "Twilio SMS",             event: "Driver alert sent",                 ts: "9m ago",  status: "Success", records: 3 },
  { id: 6, integration: "Slack",                  event: "Critical alert pushed to #ops",     ts: "11m ago", status: "Success", records: 1 },
  { id: 7, integration: "Xero",                   event: "OAuth token refresh failed",        ts: "14h ago", status: "Error",   records: 0 },
  { id: 8, integration: "AWS IoT Core",           event: "Sensor telemetry batch ingested",   ts: "1m ago",  status: "Success", records: 420 },
  { id: 9, integration: "FMCSA Portal",           event: "Daily compliance data pulled",      ts: "6h ago",  status: "Success", records: 1 },
  { id: 10,integration: "IFTA Reporting",         event: "Weekly mileage export prepared",    ts: "2d ago",  status: "Success", records: 12 },
  { id: 11,integration: "SendGrid Email",         event: "Report dispatch emails sent",       ts: "22m ago", status: "Success", records: 5 },
  { id: 12,integration: "SAP Extended WMS",       event: "Pick list sync completed",          ts: "4m ago",  status: "Success", records: 18 },
];

// ── API ────────────────────────────────────────────────────────────────────────

const integrationsApi = {
  list: () => withFallback(
    apiClient.get("/api/integrations").then((r) => {
      const rows = (r.data as AnyRecord[] | undefined) ?? [];
      return rows.length ? rows : CATALOG;
    }),
    () => CATALOG
  ),
  connect:    (id: number) => apiClient.post(`/api/integrations/${id}/connect`, {}),
  disconnect: (id: number) => apiClient.delete(`/api/integrations/${id}`),
};

// ── Helpers ────────────────────────────────────────────────────────────────────

function CatBadge({ cat }: { cat: string }) {
  const cls = CAT_COLOR[cat] ?? "bg-slate-100 border-slate-200 text-slate-600";
  return <span className={`inline-flex text-[10px] px-2 py-0.5 rounded-full border font-semibold uppercase tracking-wide ${cls}`}>{cat}</span>;
}

function StatusPill({ status }: { status: string }) {
  const [dot, txt] =
    status === "Connected"    ? ["bg-teal-400",   "text-teal-700"]  :
    status === "Error"        ? ["bg-red-400",    "text-red-700"]   :
    status === "Pending"      ? ["bg-amber-400",  "text-amber-700"] :
    ["bg-slate-300", "text-slate-400"];
  return (
    <span className="flex items-center gap-1.5">
      <span className={`h-2 w-2 rounded-full shrink-0 ${dot}`} />
      <span className={`text-xs font-medium ${txt}`}>{status}</span>
    </span>
  );
}

function LogoBubble({ logo }: { logo: string }) {
  return (
    <div className="h-9 w-9 shrink-0 rounded-lg bg-slate-100 border border-slate-200 flex items-center justify-center">
      <span className="text-[10px] font-bold text-slate-600 tracking-tight">{String(logo).slice(0, 3)}</span>
    </div>
  );
}

// ── Config drawer ──────────────────────────────────────────────────────────────

function ConfigDrawer({ intg, onClose, canManage }: { intg: AnyRecord; onClose: () => void; canManage: boolean }) {
  const [apiKey, setApiKey] = useState("");
  const [webhookUrl, setWebhookUrl] = useState("");
  const [syncInterval, setSyncInterval] = useState("5");
  const [saved, setSaved] = useState(false);

  function save(e: React.FormEvent) {
    e.preventDefault();
    setSaved(true);
    setTimeout(() => setSaved(false), 2500);
  }

  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-black/50 backdrop-blur-sm">
      <aside className="h-full w-full max-w-lg overflow-y-auto bg-white border-l border-slate-200 p-6 flex flex-col gap-5">
        <div className="flex items-start justify-between">
          <div className="flex items-center gap-3">
            <LogoBubble logo={String(intg.logo ?? "")} />
            <div>
              <h2 className="text-lg font-bold text-slate-900">{String(intg.name)}</h2>
              <CatBadge cat={String(intg.cat)} />
            </div>
          </div>
          <button type="button" aria-label="Close" className="icon-btn" onClick={onClose}><X className="h-5 w-5" /></button>
        </div>

        <p className="text-sm text-slate-500">{String(intg.desc ?? "")}</p>

        <div className="rounded-lg border border-slate-200 bg-slate-50 p-3 flex items-center justify-between">
          <StatusPill status={String(intg.status)} />
          <span className="text-xs text-slate-400">Last sync: {String(intg.sync ?? "—")}</span>
        </div>

        <form onSubmit={save} className="flex flex-col gap-4">
          <div>
            <label className="field-label">API Key / Client Secret</label>
            <input
              type="password"
              className="field mt-1 w-full font-mono text-sm"
              value={apiKey}
              onChange={(e) => setApiKey(e.target.value)}
              placeholder="sk-••••••••••••••••"
              disabled={!canManage}
              aria-label="API key"
            />
          </div>
          <div>
            <label className="field-label">Webhook Callback URL (optional)</label>
            <input
              type="url"
              className="field mt-1 w-full text-sm"
              value={webhookUrl}
              onChange={(e) => setWebhookUrl(e.target.value)}
              placeholder="https://your-server.com/webhook"
              disabled={!canManage}
              aria-label="Webhook URL"
            />
          </div>
          <div>
            <label className="field-label">Sync Interval (minutes)</label>
            <select
              aria-label="Sync interval"
              className="field mt-1 w-full"
              value={syncInterval}
              onChange={(e) => setSyncInterval(e.target.value)}
              disabled={!canManage}
            >
              {["1","5","15","30","60","1440"].map((v) => (
                <option key={v} value={v}>{v === "1440" ? "Daily (1440 min)" : `Every ${v} min`}</option>
              ))}
            </select>
          </div>

          <div className="border-t border-slate-100 pt-2">
            <p className="text-xs font-semibold text-slate-500 uppercase tracking-wide mb-2">Data Mappings</p>
            <div className="space-y-1.5 text-xs text-slate-600">
              {[
                ["OpsTrax Job →", `${String(intg.name)} Order`],
                ["OpsTrax Driver →", `${String(intg.name)} Employee`],
                ["OpsTrax Invoice →", `${String(intg.name)} Bill`],
              ].map(([src, dst]) => (
                <div key={src} className="flex items-center gap-2">
                  <span className="text-slate-500 w-36 shrink-0">{src}</span>
                  <ChevronRight className="h-3 w-3 text-slate-300 shrink-0" />
                  <span className="text-slate-700 font-medium">{dst}</span>
                </div>
              ))}
            </div>
          </div>

          {canManage && (
            <div className="flex items-center gap-3 pt-2 border-t border-slate-100">
              <button type="submit" className="btn-primary flex-1">Save Configuration</button>
              <button type="button" className="btn-ghost" onClick={onClose}>Cancel</button>
              {saved && <span className="flex items-center gap-1 text-xs text-teal-600 font-semibold"><CheckCircle2 className="h-3.5 w-3.5" />Saved</span>}
            </div>
          )}
        </form>
      </aside>
    </div>
  );
}

// ── Main page ──────────────────────────────────────────────────────────────────

type Tab = "Marketplace" | "Activity Log";

export function IntegrationsPage() {
  const hasPermission = useHasPermission();
  const canManage = hasPermission("telematics:providers:manage");
  const qc = useQueryClient();

  const [tab, setTab] = useState<Tab>("Marketplace");
  const [catFilter, setCatFilter] = useState("All");
  const [statusFilter, setStatusFilter] = useState("All");
  const [query, setQuery] = useState("");
  const [configTarget, setConfigTarget] = useState<AnyRecord | null>(null);

  const q = useQuery({ queryKey: ["integrations"], queryFn: integrationsApi.list });

  const connectMut = useMutation({
    mutationFn: (id: number) => integrationsApi.connect(id),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ["integrations"] }); },
    onError: () => { void qc.invalidateQueries({ queryKey: ["integrations"] }); },
  });

  const disconnectMut = useMutation({
    mutationFn: (id: number) => integrationsApi.disconnect(id),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ["integrations"] }); },
    onError: () => { void qc.invalidateQueries({ queryKey: ["integrations"] }); },
  });

  const integrations = ((q.data ?? CATALOG) as AnyRecord[]);

  const connected    = integrations.filter((i) => i.status === "Connected").length;
  const errored      = integrations.filter((i) => i.status === "Error").length;
  const pending      = integrations.filter((i) => i.status === "Pending").length;

  const filtered = integrations.filter((intg) => {
    if (catFilter !== "All" && intg.cat !== catFilter) return false;
    if (statusFilter !== "All" && intg.status !== statusFilter) return false;
    if (query && !String(intg.name ?? "").toLowerCase().includes(query.toLowerCase())) return false;
    return true;
  });

  if (q.isLoading) return <LoadingState />;

  return (
    <div className="flex flex-col gap-6 py-6">
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-bold text-slate-900">Integrations</h1>
          <p className="text-sm text-slate-500 mt-0.5">ERP, telematics, finance, messaging, maps, IoT and compliance connections</p>
        </div>
        <button type="button" className="btn-ghost text-sm" onClick={() => exportCsv("integrations", integrations)}>
          Export CSV
        </button>
      </div>

      {/* KPIs */}
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Catalogue",   val: integrations.length,                        accent: "text-slate-900" },
          { label: "Connected",   val: connected,  accent: "text-teal-600" },
          { label: "Errors",      val: errored,    accent: errored  > 0 ? "text-red-600"   : "text-slate-400" },
          { label: "Pending Auth",val: pending,    accent: pending  > 0 ? "text-amber-600" : "text-slate-400" },
          { label: "Categories",  val: ALL_CATS.length - 1, accent: "text-slate-600" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-24">
            <span className={`text-xl font-bold ${accent}`}>{val}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>

      {/* Tabs */}
      <div className="flex gap-1 rounded-xl border border-slate-200 bg-slate-50 p-1 self-start">
        {(["Marketplace", "Activity Log"] as Tab[]).map((t) => (
          <button type="button" key={t} onClick={() => setTab(t)}
            className={`flex items-center gap-1.5 rounded-lg px-4 py-1.5 text-sm font-medium transition ${tab === t ? "bg-white text-slate-900 shadow-sm" : "text-slate-500 hover:text-slate-700"}`}>
            {t === "Marketplace" ? <Link2 className="h-3.5 w-3.5" /> : <Activity className="h-3.5 w-3.5" />}
            {t}
          </button>
        ))}
      </div>

      {/* ── Marketplace ─────────────────────────────────────────────────────── */}
      {tab === "Marketplace" && (
        <>
          {/* Filter bar */}
          <div className="panel flex flex-wrap gap-3 items-center">
            <div className="relative">
              <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-slate-400" />
              <input className="field pl-8 w-52 text-sm" value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search integrations…" />
            </div>
            <div className="flex flex-wrap gap-1.5">
              {ALL_CATS.map((cat) => (
                <button type="button" key={cat} onClick={() => setCatFilter(cat)}
                  className={`px-2.5 py-1 rounded-lg text-xs font-medium border transition-colors ${catFilter === cat ? "bg-teal-50 border-teal-300 text-teal-700" : "bg-slate-50 border-slate-200 text-slate-600 hover:bg-slate-100"}`}>
                  {cat}
                </button>
              ))}
            </div>
            <select aria-label="Status filter" value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)}
              className="field ml-auto text-sm w-40">
              <option value="All">All Statuses</option>
              <option value="Connected">Connected</option>
              <option value="Error">Error</option>
              <option value="Pending">Pending</option>
              <option value="Disconnected">Disconnected</option>
            </select>
          </div>

          {errored > 0 && (
            <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 flex items-center gap-3">
              <AlertTriangle className="h-4 w-4 text-red-500 shrink-0" />
              <p className="text-sm text-red-700 font-medium">{errored} integration{errored > 1 ? "s" : ""} in error state — click Configure to re-authenticate.</p>
            </div>
          )}

          {/* Cards grid */}
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
            {filtered.map((intg) => {
              const id = Number(intg.id ?? 0);
              const isConnected = intg.status === "Connected";
              const isLoading = connectMut.isPending || disconnectMut.isPending;
              return (
                <div key={String(intg.id)} className="panel flex flex-col gap-3 hover:border-slate-300 transition">
                  <div className="flex items-start gap-3">
                    <LogoBubble logo={String(intg.logo ?? "")} />
                    <div className="flex-1 min-w-0">
                      <p className="font-semibold text-slate-900 text-sm leading-tight truncate">{String(intg.name)}</p>
                      <div className="mt-1"><CatBadge cat={String(intg.cat ?? "—")} /></div>
                    </div>
                  </div>

                  <p className="text-xs text-slate-500 leading-relaxed flex-1">{String(intg.desc ?? "")}</p>

                  <div className="flex items-center justify-between pt-2 border-t border-slate-100">
                    <StatusPill status={String(intg.status)} />
                    <span className="text-[10px] text-slate-400 truncate max-w-20">{String(intg.sync ?? "—")}</span>
                  </div>

                  <div className="flex gap-1.5">
                    {isConnected ? (
                      <button type="button" disabled={isLoading} onClick={() => disconnectMut.mutate(id)}
                        className="flex-1 rounded-lg border border-red-200 text-red-600 bg-red-50 hover:bg-red-100 text-xs py-1.5 font-medium transition disabled:opacity-50">
                        Disconnect
                      </button>
                    ) : intg.status === "Error" ? (
                      <button type="button" disabled={isLoading} onClick={() => connectMut.mutate(id)}
                        className="flex-1 rounded-lg border border-amber-300 text-amber-700 bg-amber-50 hover:bg-amber-100 text-xs py-1.5 font-medium transition disabled:opacity-50">
                        Reconnect
                      </button>
                    ) : (
                      <button type="button" disabled={isLoading} onClick={() => connectMut.mutate(id)}
                        className="flex-1 rounded-lg border border-teal-300 text-teal-700 bg-teal-50 hover:bg-teal-100 text-xs py-1.5 font-medium transition disabled:opacity-50">
                        {intg.status === "Pending" ? "Authorize" : "Connect"}
                      </button>
                    )}
                    <button type="button" aria-label="Configure" title="Configure" onClick={() => setConfigTarget(intg)}
                      className="rounded-lg border border-slate-200 text-slate-500 bg-slate-50 hover:bg-slate-100 text-xs p-1.5 transition">
                      <Settings2 className="h-3.5 w-3.5" />
                    </button>
                  </div>
                </div>
              );
            })}
          </div>

          {filtered.length === 0 && (
            <div className="panel text-center py-12 text-slate-400">
              <Zap className="h-8 w-8 mx-auto mb-2 opacity-30" />
              <p className="text-sm font-medium">No integrations match your filters</p>
              <button type="button" className="btn-ghost mt-3 text-sm" onClick={() => { setCatFilter("All"); setStatusFilter("All"); setQuery(""); }}>Clear filters</button>
            </div>
          )}
        </>
      )}

      {/* ── Activity Log ─────────────────────────────────────────────────────── */}
      {tab === "Activity Log" && (
        <div className="panel overflow-hidden">
          <div className="flex items-center justify-between mb-4">
            <p className="text-sm font-semibold text-slate-700">Recent Sync Activity</p>
            <button type="button" className="flex items-center gap-1.5 text-xs text-slate-500 hover:text-slate-700 transition">
              <RefreshCw className="h-3.5 w-3.5" />Refresh
            </button>
          </div>
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200">
                  {["Integration","Event","Records","Timestamp","Status"].map((h) => (
                    <th key={h} className="text-left text-xs font-semibold text-slate-500 uppercase tracking-wide pb-2 pr-4">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {SEED_ACTIVITY.map((row) => (
                  <tr key={String(row.id)} className="hover:bg-slate-50 transition">
                    <td className="py-2.5 pr-4 font-medium text-slate-800 text-xs whitespace-nowrap">{String(row.integration)}</td>
                    <td className="py-2.5 pr-4 text-slate-600 text-xs">{String(row.event)}</td>
                    <td className="py-2.5 pr-4 text-slate-500 text-xs font-mono">{Number(row.records) > 0 ? Number(row.records).toLocaleString() : "—"}</td>
                    <td className="py-2.5 pr-4 text-slate-400 text-xs whitespace-nowrap">{String(row.ts)}</td>
                    <td className="py-2.5">
                      {row.status === "Success"
                        ? <span className="flex items-center gap-1 text-xs text-teal-700"><CheckCircle2 className="h-3.5 w-3.5" />Success</span>
                        : <span className="flex items-center gap-1 text-xs text-red-600"><AlertTriangle className="h-3.5 w-3.5" />Error</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Config drawer */}
      {configTarget && (
        <ConfigDrawer intg={configTarget} onClose={() => setConfigTarget(null)} canManage={canManage} />
      )}
    </div>
  );
}
