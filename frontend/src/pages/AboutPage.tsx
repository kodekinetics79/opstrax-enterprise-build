import { useQuery } from "@tanstack/react-query";
import {
  Activity, BarChart3, Bot, Building2, CheckCircle2, Cloud,
  Code2, Cpu, Database, ExternalLink, Globe, Mail,
  Phone, Server, Shield, ShieldCheck, Truck, Zap,
} from "lucide-react";
import { aboutApi } from "@/services/aboutApi";

type AnyRecord = Record<string, unknown>;

const KK_CAPABILITIES = [
  { icon: <Code2 className="h-5 w-5" />,     label: "Custom SaaS Platforms",                 color: "text-teal-400",   bg: "bg-teal-400/10 border-teal-400/20" },
  { icon: <Bot className="h-5 w-5" />,        label: "AI Automation",                         color: "text-violet-400", bg: "bg-violet-400/10 border-violet-400/20" },
  { icon: <Globe className="h-5 w-5" />,      label: "Web & Mobile Applications",             color: "text-sky-400",    bg: "bg-sky-400/10 border-sky-400/20" },
  { icon: <Database className="h-5 w-5" />,   label: "ERP / CRM Integrations",                color: "text-blue-400",   bg: "bg-blue-400/10 border-blue-400/20" },
  { icon: <Cloud className="h-5 w-5" />,      label: "Cloud Solutions",                       color: "text-sky-400",    bg: "bg-sky-400/10 border-sky-400/20" },
  { icon: <BarChart3 className="h-5 w-5" />,  label: "Data Dashboards & Reporting",           color: "text-amber-400",  bg: "bg-amber-400/10 border-amber-400/20" },
  { icon: <Cpu className="h-5 w-5" />,        label: "Workflow Automation",                   color: "text-emerald-400",bg: "bg-emerald-400/10 border-emerald-400/20" },
  { icon: <Server className="h-5 w-5" />,     label: "Enterprise System Architecture",        color: "text-slate-500",  bg: "bg-slate-100 border-slate-200" },
  { icon: <ShieldCheck className="h-5 w-5" />,label: "Public-Sector / Compliance-Ready",      color: "text-green-400",  bg: "bg-green-400/10 border-green-400/20" },
  { icon: <Activity className="h-5 w-5" />,   label: "API Development & Integration",         color: "text-red-400",    bg: "bg-red-400/10 border-red-400/20" },
];

const PLATFORM_MODULES = [
  { icon: <Zap className="h-4 w-4" />,          label: "Fleet Command Center",             color: "text-teal-400" },
  { icon: <Globe className="h-4 w-4" />,         label: "Live Control Tower",              color: "text-sky-400" },
  { icon: <Activity className="h-4 w-4" />,      label: "Dispatch Board",                  color: "text-blue-400" },
  { icon: <Building2 className="h-4 w-4" />,     label: "Jobs & Orders",                   color: "text-indigo-400" },
  { icon: <Truck className="h-4 w-4" />,         label: "Route Planning",                  color: "text-violet-400" },
  { icon: <Truck className="h-4 w-4" />,         label: "Driver & Vehicle Management",     color: "text-purple-400" },
  { icon: <Cpu className="h-4 w-4" />,           label: "Maintenance & Work Orders",       color: "text-amber-400" },
  { icon: <CheckCircle2 className="h-4 w-4" />,  label: "DVIR / Inspections",              color: "text-yellow-400" },
  { icon: <Shield className="h-4 w-4" />,        label: "Safety & AI Dashcam",             color: "text-red-400" },
  { icon: <ShieldCheck className="h-4 w-4" />,   label: "Compliance & HOS/ELD Framework",  color: "text-emerald-400" },
  { icon: <BarChart3 className="h-4 w-4" />,     label: "Fuel, Expenses & Cost Intelligence", color: "text-green-400" },
  { icon: <BarChart3 className="h-4 w-4" />,     label: "Reports & Analytics",             color: "text-teal-400" },
  { icon: <Database className="h-4 w-4" />,      label: "Integrations & API Readiness",    color: "text-sky-400" },
  { icon: <Bot className="h-4 w-4" />,           label: "AI Copilot",                      color: "text-violet-400" },
];

function StatusDot({ ok }: { ok: boolean }) {
  return <span className={`inline-block h-2 w-2 rounded-full ${ok ? "bg-emerald-400" : "bg-amber-400"}`} />;
}

export function AboutPage() {
  const { data: platformRaw }     = useQuery({ queryKey: ["about-platform"],      queryFn: aboutApi.platform,      staleTime: 300_000 });
  const { data: healthRaw }       = useQuery({ queryKey: ["about-health"],        queryFn: aboutApi.healthSummary, staleTime: 30_000 });

  const platform = platformRaw as AnyRecord | undefined;
  const health   = healthRaw   as AnyRecord | undefined;

  const support   = platform?.support as AnyRecord | undefined;

  return (
    <div className="space-y-8 pb-8">

      {/* ── Hero ── */}
      <div className="relative overflow-hidden rounded-2xl border border-white/[0.08] bg-gradient-to-br from-slate-900 via-slate-900 to-teal-950/30 p-8">
        <div className="pointer-events-none absolute -right-12 -top-12 h-48 w-48 rounded-full bg-teal-400/8 blur-3xl" />
        <div className="pointer-events-none absolute -bottom-8 left-1/3 h-32 w-64 rounded-full bg-blue-500/6 blur-3xl" />
        <div className="relative">
          <div className="flex flex-wrap items-center gap-3 mb-4">
            <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-gradient-to-br from-teal-400 to-blue-500 shadow-xl shadow-teal-400/25">
              <Zap className="h-6 w-6 text-slate-950" />
            </div>
            <div>
              <h1 className="text-2xl font-extrabold text-white">About OpsTrax</h1>
              <p className="text-xs font-bold uppercase tracking-[0.22em] text-teal-300/70">Transport Management Solution</p>
            </div>
            <div className="ml-auto hidden sm:flex items-center gap-2 rounded-full border border-teal-400/20 bg-teal-400/7 px-3 py-1.5">
              <span className="live-dot h-[6px] w-[6px]" />
              <span className="text-xs font-bold text-teal-300">Live Tenant Active</span>
            </div>
          </div>
          <p className="max-w-3xl text-base text-slate-300 leading-relaxed">
            OpsTrax Transport Management Solution is an enterprise-grade connected operations platform for fleets, transport teams, drivers, vehicles, assets, dispatch, maintenance, safety, compliance, cost intelligence, and AI-powered decision support.
          </p>
          <p className="mt-3 text-sm font-semibold text-teal-300/80 italic">
            Connected transport. Intelligent control. Enterprise execution.
          </p>
        </div>
      </div>

      {/* ── Build / Health Status ── */}
      {health && (
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-4 lg:grid-cols-6">
          {[
            { label: "API",          ok: true,  value: String(health.apiStatus ?? "Connected") },
            { label: "Database",     ok: true,  value: String(health.databaseStatus ?? "Connected") },
            { label: "Node Events",  ok: true,  value: String(health.nodeEventsStatus ?? "Connected") },
            { label: "Modules",      ok: true,  value: String(health.moduleCount ?? "35+") },
            { label: "Version",      ok: true,  value: String(health.version ?? "Enterprise Build") },
            { label: "Environment",  ok: false, value: String(health.environment ?? "Local / Seeded") },
          ].map((item) => (
            <div key={item.label} className="panel p-3 text-center">
              <div className="flex items-center justify-center gap-1.5 mb-1">
                <StatusDot ok={item.ok} />
                <p className="text-[10px] font-bold uppercase tracking-widest text-slate-500">{item.label}</p>
              </div>
              <p className="text-sm font-bold text-slate-900 truncate">{item.value}</p>
            </div>
          ))}
        </div>
      )}

      <div className="grid gap-8 lg:grid-cols-2">

        {/* ── Developed by Kode Kinetics ── */}
        <div className="panel p-6 space-y-4">
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-xl border border-teal-400/25 bg-teal-400/10">
              <Code2 className="h-5 w-5 text-teal-300" />
            </div>
            <div>
              <h2 className="font-extrabold text-slate-900">Developed by Kode Kinetics</h2>
              <p className="text-xs text-slate-500">Technology company · Custom software · AI systems</p>
            </div>
          </div>
          <p className="text-sm text-slate-600 leading-relaxed">
            {String(platform?.companyDescription ?? "Kode Kinetics is a technology company specializing in custom software development, AI automation, SaaS platforms, enterprise integrations, cloud solutions, web/mobile applications, and digital transformation systems for modern organizations.")}
          </p>
          <p className="text-sm font-semibold text-slate-700">
            Built by Kode Kinetics. Designed for connected transport operations.
          </p>

          {/* Contact */}
          <div className="space-y-2 pt-2 border-t border-slate-200">
            <p className="text-[10px] font-bold uppercase tracking-widest text-slate-500">Contact &amp; Support</p>
            <a href="https://www.kodekinetics.com" target="_blank" rel="noopener noreferrer" className="flex items-center gap-2 text-sm text-teal-600 hover:text-teal-700 transition">
              <Globe className="h-3.5 w-3.5" />
              {String(support?.website ?? "www.kodekinetics.com")}
              <ExternalLink className="h-3 w-3 opacity-60" />
            </a>
            <a href="mailto:info@kodekinetics.com" className="flex items-center gap-2 text-sm text-slate-500 hover:text-slate-700 transition">
              <Mail className="h-3.5 w-3.5" />
              {String(support?.email ?? "info@kodekinetics.com")}
            </a>
            <a href="tel:+15714305333" className="flex items-center gap-2 text-sm text-slate-500 hover:text-slate-700 transition">
              <Phone className="h-3.5 w-3.5" />
              {String(support?.phone ?? "+1 571 430 5333")}
            </a>
          </div>
        </div>

        {/* ── OpsTrax Platform Capabilities ── */}
        <div className="panel p-6 space-y-4">
          <h2 className="font-extrabold text-slate-900">OpsTrax Platform Modules</h2>
          <div className="grid grid-cols-2 gap-2">
            {PLATFORM_MODULES.map((m) => (
              <div key={m.label} className="flex items-center gap-2 rounded-lg border border-slate-200 bg-slate-50 px-2.5 py-2">
                <span className={`shrink-0 ${m.color}`}>{m.icon}</span>
                <span className="text-xs font-medium text-slate-700 leading-tight">{m.label}</span>
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* ── Kode Kinetics Capabilities ── */}
      <div className="panel p-6 space-y-4">
        <div className="flex items-center gap-2 mb-2">
          <Server className="h-5 w-5 text-teal-400" />
          <h2 className="font-extrabold text-slate-900">Kode Kinetics Capabilities</h2>
        </div>
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
          {KK_CAPABILITIES.map((cap) => (
            <div key={cap.label} className={`flex flex-col items-start gap-2 rounded-xl border p-3 ${cap.bg}`}>
              <span className={cap.color}>{cap.icon}</span>
              <p className="text-xs font-semibold text-slate-700 leading-snug">{cap.label}</p>
            </div>
          ))}
        </div>
      </div>

      {/* ── Compliance Disclaimer ── */}
      <div className="rounded-xl border border-amber-400/15 bg-amber-400/[0.04] p-4">
        <div className="flex items-start gap-3">
          <ShieldCheck className="h-5 w-5 shrink-0 text-amber-600 mt-0.5" />
          <div>
            <p className="text-xs font-bold uppercase tracking-widest text-amber-700 mb-1">Compliance Disclaimer</p>
            <p className="text-sm text-amber-700 leading-relaxed">
              {String(platform?.disclaimer ?? "OpsTrax provides operational, compliance management, and audit-readiness tools. Final regulatory compliance remains the responsibility of the carrier/operator. ELD certification and regulatory approval depend on the connected device/provider and applicable country requirements.")}
            </p>
          </div>
        </div>
      </div>

      {/* ── Build Info ── */}
      <div className="panel p-4">
        <p className="text-[10px] font-bold uppercase tracking-widest text-slate-500 mb-3">Platform Build Info</p>
        <div className="grid grid-cols-2 gap-x-8 gap-y-2 text-sm sm:grid-cols-4">
          {[
            ["Product",     String(platform?.fullProductName ?? "OpsTrax Transport Management Solution")],
            ["Developer",   String(platform?.developer       ?? "Kode Kinetics")],
            ["Version",     String(platform?.version         ?? "Enterprise Build")],
            ["Environment", String(platform?.environment     ?? "Local / Seeded")],
          ].map(([k, v]) => (
            <div key={k}>
              <p className="text-[10px] text-slate-500 uppercase tracking-widest">{k}</p>
              <p className="font-semibold text-slate-700 mt-0.5">{v}</p>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
