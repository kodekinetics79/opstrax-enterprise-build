import { useMutation } from "@tanstack/react-query";
import {
  Activity, Bot, ChevronRight, Globe,
  Lock, Shield, ShieldCheck, Sparkles,
  Truck, Users, Wrench, Zap,
} from "lucide-react";
import { useNavigate } from "react-router-dom";
import { authApi } from "@/services/authApi";
import { useAuth } from "@/hooks/useAuth";

/* ── Demo roles ── */
const ROLES = [
  {
    role: "Company Admin",
    email: "admin@opstrax.com",
    description: "Full fleet visibility and executive command",
    icon: <Shield className="h-5 w-5" />,
    gradient: "from-teal-400/20 to-blue-500/10",
    border:   "border-teal-400/22",
    iconBg:   "bg-teal-400/12 border-teal-400/20 text-teal-300",
    tag:      "Full Access",
    tagCls:   "border-teal-400/25 bg-teal-400/8 text-teal-300",
  },
  {
    role: "Dispatcher",
    email: "dispatcher@opstrax.com",
    description: "Dispatch board, job assignment and driver coordination",
    icon: <Activity className="h-5 w-5" />,
    gradient: "from-blue-500/18 to-sky-400/8",
    border:   "border-blue-400/22",
    iconBg:   "bg-blue-400/12 border-blue-400/20 text-blue-300",
    tag:      "Operations",
    tagCls:   "border-blue-400/25 bg-blue-400/8 text-blue-300",
  },
  {
    role: "Driver",
    email: "driver@opstrax.com",
    description: "Route info, jobs, HOS, DVIR and coaching",
    icon: <Truck className="h-5 w-5" />,
    gradient: "from-emerald-500/16 to-green-400/6",
    border:   "border-emerald-400/22",
    iconBg:   "bg-emerald-400/12 border-emerald-400/20 text-emerald-300",
    tag:      "Field",
    tagCls:   "border-emerald-400/25 bg-emerald-400/8 text-emerald-300",
  },
  {
    role: "Mechanic",
    email: "mechanic@opstrax.com",
    description: "Maintenance queue, work orders and DVIR reviews",
    icon: <Wrench className="h-5 w-5" />,
    gradient: "from-amber-500/16 to-yellow-400/6",
    border:   "border-amber-400/22",
    iconBg:   "bg-amber-400/12 border-amber-400/20 text-amber-300",
    tag:      "Maintenance",
    tagCls:   "border-amber-400/25 bg-amber-400/8 text-amber-300",
  },
  {
    role: "Customer",
    email: "customer@opstrax.com",
    description: "ETA portal, job status and delivery proof",
    icon: <Users className="h-5 w-5" />,
    gradient: "from-violet-500/16 to-purple-400/6",
    border:   "border-violet-400/22",
    iconBg:   "bg-violet-400/12 border-violet-400/20 text-violet-300",
    tag:      "Portal",
    tagCls:   "border-violet-400/25 bg-violet-400/8 text-violet-300",
  },
] as const;

/* ── Feature highlights ── */
const FEATURES = [
  { icon: <Activity className="h-4 w-4" />, label: "Live fleet control",          color: "text-teal-400" },
  { icon: <Sparkles  className="h-4 w-4" />, label: "AI action intelligence",      color: "text-violet-400" },
  { icon: <Shield    className="h-4 w-4" />, label: "Safety & compliance",         color: "text-red-400" },
  { icon: <Truck     className="h-4 w-4" />, label: "Dispatch & route planning",   color: "text-sky-400" },
  { icon: <Globe     className="h-4 w-4" />, label: "Customer ETA transparency",   color: "text-emerald-400" },
  { icon: <Zap       className="h-4 w-4" />, label: "Cost & margin intelligence",  color: "text-amber-400" },
] as const;

/* ── Stats ── */
const STATS = [
  { value: "35+", label: "Enterprise Modules" },
  { value: "100%", label: "API Coverage" },
  { value: "Zero", label: "Config Required" },
] as const;

export function LoginPage() {
  const { setSession } = useAuth();
  const navigate = useNavigate();

  const login = useMutation({
    mutationFn: ({ email }: { email: string }) => authApi.login(email, "Admin@12345"),
    onSuccess: (session) => {
      setSession(session);
      navigate("/command-center", { replace: true });
    },
  });

  const activeEmail = login.variables?.email;

  return (
    <div className="relative min-h-screen overflow-hidden bg-[#f8fafc] text-slate-900">

      {/* ── Background ── */}
      <div className="login-bg absolute inset-0" />
      <div className="login-grid absolute inset-0" />

      {/* Floating orbs */}
      <div
        className="pointer-events-none absolute left-[8%] top-[18%] h-64 w-64 rounded-full anim-float"
        style={{ background: "radial-gradient(circle, rgba(45,212,191,.09) 0%, transparent 70%)" }}
      />
      <div
        className="pointer-events-none absolute right-[10%] top-[35%] h-80 w-80 rounded-full anim-float"
        style={{ animationDelay: "2s", background: "radial-gradient(circle, rgba(59,130,246,.07) 0%, transparent 70%)" }}
      />
      <div
        className="pointer-events-none absolute bottom-[15%] left-[30%] h-56 w-56 rounded-full anim-float"
        style={{ animationDelay: "4s", background: "radial-gradient(circle, rgba(139,92,246,.07) 0%, transparent 70%)" }}
      />

      {/* ── Main Layout ── */}
      <div className="relative mx-auto grid min-h-screen max-w-[1320px] items-center gap-10 px-6 py-14 lg:grid-cols-[1.15fr_0.9fr] xl:gap-16">

        {/* ── Left: Hero ── */}
        <section className="anim-fade-up flex flex-col gap-8">

          {/* Brand mark */}
          <div className="flex items-center gap-3">
            <div className="flex h-11 w-11 items-center justify-center rounded-2xl bg-gradient-to-br from-teal-400 to-blue-500 shadow-2xl shadow-teal-400/30">
              <Zap className="h-6 w-6 text-slate-950" />
            </div>
            <div>
              <p className="text-xl font-extrabold tracking-tight">OpsTrax</p>
              <p className="text-[10px] font-bold uppercase tracking-[0.28em] text-teal-600">Enterprise TMS</p>
            </div>
            <div className="ml-4 flex items-center gap-1.5 rounded-full border border-emerald-500/30 bg-emerald-50 px-3 py-1.5 text-[11px] font-bold text-emerald-700">
              <span className="live-dot h-[6px] w-[6px]" />
              Live Demo
            </div>
          </div>

          {/* Headline */}
          <div>
            <h1 className="text-5xl font-extrabold leading-[1.08] tracking-tight md:text-6xl xl:text-7xl">
              Connected transport.{" "}
              <span className="gradient-text">Intelligent control.</span>
            </h1>
            <p className="mt-5 max-w-xl text-lg leading-relaxed text-slate-600">
              A premium command center for fleets of every scale — dispatch, safety,
              compliance, cost intelligence and AI-assisted operations in one place.
            </p>
          </div>

          {/* Stats */}
          <div className="flex items-center gap-8">
            {STATS.map(({ value, label }) => (
              <div key={label}>
                <p className="text-2xl font-extrabold text-slate-900">{value}</p>
                <p className="text-xs text-slate-500 mt-0.5">{label}</p>
              </div>
            ))}
          </div>

          {/* Feature grid */}
          <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
            {FEATURES.map(({ icon, label, color }) => (
              <div
                key={label}
                className="flex items-center gap-2.5 rounded-xl border border-slate-200 bg-white/80 px-3.5 py-3 transition hover:border-slate-300 hover:bg-white"
              >
                <span className={`flex-shrink-0 ${color}`}>{icon}</span>
                <span className="text-sm font-medium text-slate-600">{label}</span>
              </div>
            ))}
          </div>
        </section>

        {/* ── Right: Login Panel ── */}
        <section className="panel panel-glow anim-fade-up p-0 overflow-hidden" style={{ animationDelay: ".08s" }}>

          {/* Panel header */}
          <div className="border-b border-white/[0.08] px-6 pt-6 pb-5">
            <div className="flex items-center gap-3">
              <div className="flex h-10 w-10 items-center justify-center rounded-xl border border-violet-400/22 bg-violet-400/10">
                <Bot className="h-5 w-5 text-violet-300" />
              </div>
              <div>
                <h2 className="text-xl font-extrabold">Sign in to OpsTrax</h2>
                <p className="text-xs text-slate-500">Select a demo role to explore the platform</p>
              </div>
            </div>
          </div>

          {/* Role cards */}
          <div className="stagger space-y-2 p-5">
            {ROLES.map(({ role, email, description, icon, gradient, border, iconBg, tag, tagCls }) => {
              const isLoading = login.isPending && activeEmail === email;
              return (
                <button
                  key={email}
                  className={`role-card w-full bg-gradient-to-r ${gradient} ${border} disabled:opacity-60 disabled:cursor-not-allowed`}
                  onClick={() => login.mutate({ email })}
                  disabled={login.isPending}
                >
                  {/* Icon */}
                  <div className={`flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-xl border ${iconBg}`}>
                    {isLoading
                      ? <span className="h-4 w-4 animate-spin rounded-full border-2 border-current border-t-transparent" />
                      : icon}
                  </div>

                  {/* Text */}
                  <div className="flex-1 min-w-0 text-left">
                    <div className="flex items-center gap-2">
                      <p className="font-bold text-white">{role}</p>
                      <span className={`rounded-full border px-2 py-px text-[10px] font-bold ${tagCls}`}>{tag}</span>
                    </div>
                    <p className="mt-0.5 text-xs text-slate-400 truncate">{description}</p>
                  </div>

                  {/* Arrow */}
                  <ChevronRight className="h-4 w-4 flex-shrink-0 text-slate-500 transition group-hover:translate-x-0.5" />
                </button>
              );
            })}
          </div>

          {/* Security footer */}
          <div className="mx-5 mb-5 flex items-start gap-3 rounded-2xl border border-emerald-300/40 bg-emerald-50 p-4">
            <ShieldCheck className="mt-0.5 h-4 w-4 flex-shrink-0 text-emerald-400" />
            <div>
              <p className="text-xs font-semibold text-emerald-300">Enterprise security active</p>
              <p className="mt-0.5 text-xs text-slate-500">
                Demo password:{" "}
                <span className="rounded border border-slate-200 bg-slate-100 px-1.5 py-px font-mono text-slate-700">
                  Admin@12345
                </span>
                {" "}· Seeded operational data · RBAC metadata
              </p>
            </div>
          </div>

          {login.isError && (
            <div className="mx-5 mb-5 flex items-center gap-2 rounded-xl border border-red-300/50 bg-red-50 p-3 text-sm text-red-700">
              <Lock className="h-4 w-4 flex-shrink-0" />
              Login failed — use one of the demo role buttons or check the backend service.
            </div>
          )}

          {/* Kode Kinetics attribution */}
          <div className="border-t border-white/[0.06] px-6 py-3 text-center">
            <p className="text-[11px] text-slate-600">
              An enterprise transport intelligence platform by{" "}
              <span className="font-semibold text-slate-500">Kode Kinetics</span>
            </p>
          </div>
        </section>
      </div>
    </div>
  );
}
