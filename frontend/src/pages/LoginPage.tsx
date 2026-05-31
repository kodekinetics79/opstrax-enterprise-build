import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import {
  Activity, Bot, ChevronRight, Eye, EyeOff, Globe,
  Lock, Shield, ShieldCheck, Sparkles,
  Truck, Users, Wrench, Zap,
} from "lucide-react";
import { useNavigate } from "react-router-dom";
import { demoUsers } from "@/auth/demoUsers";
import { authApi } from "@/services/authApi";
import { useAuth } from "@/hooks/useAuth";

/* ── Demo roles ── */
const ROLE_STYLE = {
  platform_super_admin: { icon: <Shield className="h-5 w-5" />, gradient: "from-cyan-400/20 to-blue-500/10", border: "border-cyan-400/22", iconBg: "bg-cyan-400/12 border-cyan-400/20 text-cyan-300", tag: "Platform", tagCls: "border-cyan-400/25 bg-cyan-400/8 text-cyan-300", description: "Global access across all tenants and modules" },
  company_admin: { icon: <Shield className="h-5 w-5" />, gradient: "from-teal-400/20 to-blue-500/10", border: "border-teal-400/22", iconBg: "bg-teal-400/12 border-teal-400/20 text-teal-300", tag: "Full Access", tagCls: "border-teal-400/25 bg-teal-400/8 text-teal-300", description: "Full company-level visibility and control" },
  operations_manager: { icon: <Activity className="h-5 w-5" />, gradient: "from-sky-500/18 to-blue-400/8", border: "border-sky-400/22", iconBg: "bg-sky-400/12 border-sky-400/20 text-sky-300", tag: "Operations", tagCls: "border-sky-400/25 bg-sky-400/8 text-sky-300", description: "Cross-functional operations command and execution" },
  dispatcher: { icon: <Activity className="h-5 w-5" />, gradient: "from-blue-500/18 to-sky-400/8", border: "border-blue-400/22", iconBg: "bg-blue-400/12 border-blue-400/20 text-blue-300", tag: "Dispatch", tagCls: "border-blue-400/25 bg-blue-400/8 text-blue-300", description: "Dispatch board, loads, assignments and ETA flow" },
  fleet_manager: { icon: <Truck className="h-5 w-5" />, gradient: "from-emerald-500/16 to-green-400/6", border: "border-emerald-400/22", iconBg: "bg-emerald-400/12 border-emerald-400/20 text-emerald-300", tag: "Fleet", tagCls: "border-emerald-400/25 bg-emerald-400/8 text-emerald-300", description: "Fleet assets, drivers, fuel and uptime oversight" },
  driver: { icon: <Truck className="h-5 w-5" />, gradient: "from-green-500/16 to-lime-400/6", border: "border-green-400/22", iconBg: "bg-green-400/12 border-green-400/20 text-green-300", tag: "Field", tagCls: "border-green-400/25 bg-green-400/8 text-green-300", description: "Daily jobs, shipments, proof and compliance checks" },
  safety_compliance_manager: { icon: <Shield className="h-5 w-5" />, gradient: "from-red-500/16 to-rose-400/6", border: "border-red-400/22", iconBg: "bg-red-400/12 border-red-400/20 text-red-300", tag: "Safety", tagCls: "border-red-400/25 bg-red-400/8 text-red-300", description: "Safety events, dashcam workflows and compliance controls" },
  maintenance_manager: { icon: <Wrench className="h-5 w-5" />, gradient: "from-amber-500/16 to-yellow-400/6", border: "border-amber-400/22", iconBg: "bg-amber-400/12 border-amber-400/20 text-amber-300", tag: "Maintenance", tagCls: "border-amber-400/25 bg-amber-400/8 text-amber-300", description: "Work orders, service history and maintenance planning" },
  finance_billing_manager: { icon: <Globe className="h-5 w-5" />, gradient: "from-yellow-500/16 to-amber-400/6", border: "border-yellow-400/22", iconBg: "bg-yellow-400/12 border-yellow-400/20 text-yellow-300", tag: "Finance", tagCls: "border-yellow-400/25 bg-yellow-400/8 text-yellow-300", description: "Finance, billing, fuel cost and margin performance" },
  crm_sales_manager: { icon: <Users className="h-5 w-5" />, gradient: "from-violet-500/16 to-purple-400/6", border: "border-violet-400/22", iconBg: "bg-violet-400/12 border-violet-400/20 text-violet-300", tag: "CRM", tagCls: "border-violet-400/25 bg-violet-400/8 text-violet-300", description: "CRM pipeline, customer growth and campaign execution" },
  customer_portal_user: { icon: <Users className="h-5 w-5" />, gradient: "from-fuchsia-500/16 to-pink-400/6", border: "border-fuchsia-400/22", iconBg: "bg-fuchsia-400/12 border-fuchsia-400/20 text-fuchsia-300", tag: "Customer", tagCls: "border-fuchsia-400/25 bg-fuchsia-400/8 text-fuchsia-300", description: "Customer portal access for shipment and delivery visibility" },
  vendor_service_provider: { icon: <Wrench className="h-5 w-5" />, gradient: "from-orange-500/16 to-amber-400/6", border: "border-orange-400/22", iconBg: "bg-orange-400/12 border-orange-400/20 text-orange-300", tag: "Vendor", tagCls: "border-orange-400/25 bg-orange-400/8 text-orange-300", description: "Service-provider access for partner workflows" },
} as const;

const ROLES = demoUsers.map((user) => ({
  role: user.roleLabel,
  username: user.email,
  password: user.password,
  ...ROLE_STYLE[user.roleKey],
}));

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
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);

  const login = useMutation({
    mutationFn: ({ user, pass }: { user: string; pass: string }) =>
      authApi.login(user, pass),
    onSuccess: (session) => {
      setSession(session);
      navigate("/command-center", { replace: true });
    },
  });

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!username.trim() || !password) return;
    login.mutate({ user: username.trim(), pass: password });
  };

  const fillCredentials = (roleUsername: string, rolePassword: string) => {
    setUsername(roleUsername);
    setPassword(rolePassword);
  };

  return (
    <div className="relative min-h-screen overflow-hidden bg-[#f8fafc] text-slate-900">

      {/* ── Background ── */}
      <div className="login-bg absolute inset-0" />
      <div className="login-grid absolute inset-0" />

      {/* Floating orbs */}
      <div className="orb-teal pointer-events-none absolute left-[8%] top-[18%] h-64 w-64 rounded-full anim-float" />
      <div className="orb-blue pointer-events-none absolute right-[10%] top-[35%] h-80 w-80 rounded-full anim-float anim-delay-2s" />
      <div className="orb-violet pointer-events-none absolute bottom-[15%] left-[30%] h-56 w-56 rounded-full anim-float anim-delay-4s" />

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
        <section className="panel panel-glow anim-fade-up anim-delay-08 p-0 overflow-hidden">

          {/* Panel header */}
          <div className="border-b border-white/[0.08] px-6 pt-6 pb-5">
            <div className="flex items-center gap-3">
              <div className="flex h-10 w-10 items-center justify-center rounded-xl border border-violet-400/22 bg-violet-400/10">
                <Bot className="h-5 w-5 text-violet-300" />
              </div>
              <div>
                <h2 className="text-xl font-extrabold">Sign in to OpsTrax</h2>
                <p className="text-xs text-slate-500">Enter your credentials or pick a demo role below</p>
              </div>
            </div>
          </div>

          {/* ── Login Form ── */}
          <form onSubmit={handleSubmit} className="px-5 pt-5 pb-4 space-y-3">
            {/* Username field */}
            <div className="space-y-1">
              <label className="text-xs font-semibold text-slate-400 uppercase tracking-wide">Username</label>
              <input
                type="text"
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                placeholder="e.g. superadmin@opstrax.com"
                autoComplete="username"
                className="w-full rounded-xl border border-white/10 bg-white/5 px-4 py-2.5 text-sm text-white placeholder:text-slate-500 focus:border-teal-400/50 focus:outline-none focus:ring-1 focus:ring-teal-400/30 transition"
              />
            </div>

            {/* Password field */}
            <div className="space-y-1">
              <label className="text-xs font-semibold text-slate-400 uppercase tracking-wide">Password</label>
              <div className="relative">
                <input
                  type={showPassword ? "text" : "password"}
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  placeholder="Enter password"
                  autoComplete="current-password"
                  className="w-full rounded-xl border border-white/10 bg-white/5 px-4 py-2.5 pr-11 text-sm text-white placeholder:text-slate-500 focus:border-teal-400/50 focus:outline-none focus:ring-1 focus:ring-teal-400/30 transition"
                />
                <button
                  type="button"
                  onClick={() => setShowPassword((v) => !v)}
                  className="absolute right-3 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-200 transition"
                >
                  {showPassword
                    ? <EyeOff className="h-4 w-4" />
                    : <Eye className="h-4 w-4" />}
                </button>
              </div>
            </div>

            {/* Submit */}
            <button
              type="submit"
              disabled={login.isPending || !username.trim() || !password}
              className="w-full rounded-xl bg-gradient-to-r from-teal-500 to-blue-500 py-2.5 text-sm font-bold text-white shadow-lg shadow-teal-500/20 transition hover:opacity-90 disabled:opacity-50 disabled:cursor-not-allowed flex items-center justify-center gap-2"
            >
              {login.isPending
                ? <span className="h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" />
                : <Lock className="h-4 w-4" />}
              Sign In
            </button>
          </form>

          {/* Divider */}
          <div className="flex items-center gap-3 px-5 pb-3">
            <div className="h-px flex-1 bg-white/[0.07]" />
            <span className="text-[11px] font-semibold text-slate-500 uppercase tracking-wider">or quick access</span>
            <div className="h-px flex-1 bg-white/[0.07]" />
          </div>

          {/* Role cards */}
          <div className="stagger space-y-2 px-5 pb-4">
            {ROLES.map(({ role, username: roleUsername, password: rolePassword, description, icon, gradient, border, iconBg, tag, tagCls }) => {
              const isFilled = username === roleUsername && password === rolePassword;
              return (
                <button
                  key={roleUsername}
                  type="button"
                  className={`role-card w-full bg-gradient-to-r ${gradient} ${border} ${isFilled ? "ring-2 ring-teal-400/40" : ""}`}
                  onClick={() => fillCredentials(roleUsername, rolePassword)}
                >
                  {/* Icon */}
                  <div className={`flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-xl border ${iconBg}`}>
                    {icon}
                  </div>

                  {/* Text */}
                  <div className="flex-1 min-w-0 text-left">
                    <div className="flex items-center gap-2">
                      <p className="font-bold text-white">{role}</p>
                      <span className={`rounded-full border px-2 py-px text-[10px] font-bold ${tagCls}`}>{tag}</span>
                    </div>
                    <div className="flex items-center gap-2 mt-0.5">
                      <p className="text-xs text-slate-400 truncate">{description}</p>
                    </div>
                    <p className="text-[11px] text-slate-500 mt-0.5 font-mono">
                      user: <span className="text-slate-300">{roleUsername}</span>
                      {" · "}pass: <span className="text-slate-300">{rolePassword}</span>
                    </p>
                  </div>

                  {/* Arrow / filled indicator */}
                  {isFilled
                    ? <span className="text-[10px] font-bold text-teal-400 flex-shrink-0">Filled ✓</span>
                    : <ChevronRight className="h-4 w-4 flex-shrink-0 text-slate-500 transition group-hover:translate-x-0.5" />}
                </button>
              );
            })}
          </div>

          {/* ── Client Demo Credentials ── */}
          <div className="mx-5 mb-4 rounded-2xl border border-violet-400/30 bg-violet-500/8 p-4">
            <div className="flex items-start gap-3">
              <Sparkles className="mt-0.5 h-4 w-4 flex-shrink-0 text-violet-400" />
              <div className="flex-1">
                <p className="text-xs font-bold text-violet-300">Demo Access</p>
                <p className="mt-0.5 text-xs text-slate-400">All listed demo users use the same password:</p>
                <div className="mt-2 flex flex-wrap gap-2">
                  <span className="flex items-center gap-1.5 rounded-lg border border-white/10 bg-white/5 px-2.5 py-1 text-xs font-mono">
                    <span className="text-slate-500">pass:</span>
                    <span className="text-white font-semibold">demo123</span>
                  </span>
                </div>
                <button
                  type="button"
                  onClick={() => fillCredentials("superadmin@opstrax.com", "demo123")}
                  className="mt-2.5 text-[11px] font-bold text-violet-400 hover:text-violet-300 transition"
                >
                  {username === "superadmin@opstrax.com" && password === "demo123"
                    ? "Credentials filled — click Sign In ✓"
                    : "Fill super admin credentials →"}
                </button>
              </div>
            </div>
          </div>

          {/* Security footer */}
          <div className="mx-5 mb-5 flex items-start gap-3 rounded-2xl border border-emerald-300/40 bg-emerald-50 p-4">
            <ShieldCheck className="mt-0.5 h-4 w-4 flex-shrink-0 text-emerald-400" />
            <div>
              <p className="text-xs font-semibold text-emerald-300">Enterprise security active</p>
              <p className="mt-0.5 text-xs text-slate-500">
                Seeded operational data · RBAC role isolation · Demo environment
              </p>
            </div>
          </div>

          {login.isError && (
            <div className="mx-5 mb-5 flex items-center gap-2 rounded-xl border border-red-300/50 bg-red-50 p-3 text-sm text-red-700">
              <Lock className="h-4 w-4 flex-shrink-0" />
              Invalid credentials — check the username and password or use a quick access role.
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
