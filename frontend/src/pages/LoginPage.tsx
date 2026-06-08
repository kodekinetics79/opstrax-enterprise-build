import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import {
  AlertCircle,
  ArrowRight,
  BadgeCheck,
  CheckCircle2,
  Globe,
  Layers3,
  LogIn,
  MapPinned,
  ShieldCheck,
  Sparkles,
  Truck,
  Users,
  Wrench,
} from "lucide-react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "@/hooks/useAuth";
import { authApi } from "@/services/authApi";
import { demoUsers } from "@/auth/demoUsers";

const heroStats = [
  { value: "24/7", label: "operations visibility" },
  { value: "1 tenant", label: "secure login surface" },
  { value: "8 modules", label: "connected on load" },
];

const trustPoints = [
  { icon: Truck, title: "Fleet operations", body: "Vehicles, drivers, routes, and jobs in one control plane." },
  { icon: MapPinned, title: "Live telemetry", body: "Location, ETA, and event signals surfaced with tenant scoping." },
  { icon: Wrench, title: "Maintenance readiness", body: "Service, DVIR, and work-order workflows tied to real entities." },
  { icon: ShieldCheck, title: "Audit and access", body: "RBAC, CSRF, and session validation built for operator confidence." },
];

const accessShortcuts = demoUsers.slice(0, 4);

export function LoginPage() {
  const { setSession } = useAuth();
  const navigate = useNavigate();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);

  const login = useMutation({
    mutationFn: async ({ email: e, password: p }: { email: string; password: string }) => authApi.login(e, p),
    onSuccess: (session) => {
      setSession(session);
      navigate("/command-center", { replace: true });
    },
  });

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!email.trim() || !password) return;
    login.mutate({ email: email.trim(), password });
  };

  const fillAccess = (accessEmail: string, accessPassword: string) => {
    setEmail(accessEmail);
    setPassword(accessPassword);
  };

  return (
    <div className="relative min-h-screen overflow-hidden bg-[#07111f] text-white">
      <div className="absolute inset-0 bg-[radial-gradient(circle_at_top_left,_rgba(45,212,191,0.24),_transparent_26%),radial-gradient(circle_at_right,_rgba(59,130,246,0.18),_transparent_22%),linear-gradient(135deg,_#06111d_0%,_#0a1a2c_52%,_#07111f_100%)]" />
      <div className="absolute left-[-5rem] top-24 h-72 w-72 rounded-full bg-teal-400/15 blur-3xl" />
      <div className="absolute right-[-6rem] top-44 h-96 w-96 rounded-full bg-blue-500/15 blur-3xl" />
      <div className="absolute bottom-[-8rem] left-1/3 h-96 w-96 rounded-full bg-sky-400/10 blur-3xl" />

      <div className="relative mx-auto grid min-h-screen max-w-7xl items-center gap-10 px-6 py-8 lg:grid-cols-[1.1fr_0.9fr] lg:px-8 xl:px-12">
        <section className="flex flex-col justify-center">
          <div className="mb-6 inline-flex w-fit items-center gap-2 rounded-full border border-white/15 bg-white/5 px-4 py-2 text-sm text-white/80 shadow-[0_0_0_1px_rgba(255,255,255,0.04)] backdrop-blur">
            <Sparkles className="h-4 w-4 text-teal-300" />
            Enterprise fleet intelligence for dispatch, safety, and operations
          </div>

          <div className="max-w-2xl">
            <h1 className="text-4xl font-semibold tracking-tight text-white sm:text-5xl lg:text-6xl">
              OpsTrax
              <span className="block bg-gradient-to-r from-teal-200 via-cyan-200 to-blue-200 bg-clip-text text-transparent">
                Command center for live fleet operations.
              </span>
            </h1>
            <p className="mt-6 max-w-xl text-base leading-7 text-slate-300 sm:text-lg">
              Track vehicles, dispatches, compliance, maintenance, and customer commitments from one secure platform
              built to feel like a real enterprise operations suite on first load.
            </p>
          </div>

          <div className="mt-8 grid gap-4 sm:grid-cols-3">
            {heroStats.map((stat) => (
              <div key={stat.label} className="rounded-2xl border border-white/10 bg-white/6 p-4 backdrop-blur">
                <div className="text-2xl font-semibold text-white">{stat.value}</div>
                <div className="mt-1 text-sm text-slate-300">{stat.label}</div>
              </div>
            ))}
          </div>

          <div className="mt-8 grid gap-4 md:grid-cols-2">
            {trustPoints.map((item) => {
              const Icon = item.icon;
              return (
                <div
                  key={item.title}
                  className="rounded-2xl border border-white/10 bg-slate-950/35 p-5 shadow-2xl shadow-black/15 backdrop-blur"
                >
                  <div className="flex items-start gap-3">
                    <div className="rounded-xl border border-teal-400/20 bg-teal-400/10 p-2 text-teal-200">
                      <Icon className="h-5 w-5" />
                    </div>
                    <div>
                      <h3 className="text-sm font-semibold text-white">{item.title}</h3>
                      <p className="mt-1 text-sm leading-6 text-slate-300">{item.body}</p>
                    </div>
                  </div>
                </div>
              );
            })}
          </div>

          <div className="mt-8 flex flex-wrap items-center gap-3 text-sm text-slate-300">
            <div className="flex items-center gap-2 rounded-full border border-white/10 bg-white/5 px-4 py-2">
              <Layers3 className="h-4 w-4 text-cyan-300" />
              Multi-tenant access
            </div>
            <div className="flex items-center gap-2 rounded-full border border-white/10 bg-white/5 px-4 py-2">
              <Users className="h-4 w-4 text-cyan-300" />
              Role-based entry
            </div>
            <div className="flex items-center gap-2 rounded-full border border-white/10 bg-white/5 px-4 py-2">
              <Globe className="h-4 w-4 text-cyan-300" />
              Browser-ready deployment
            </div>
          </div>
        </section>

        <section className="relative">
          <div className="absolute -inset-3 rounded-[2rem] bg-gradient-to-br from-white/10 to-white/5 blur-xl" />
          <div className="relative overflow-hidden rounded-[2rem] border border-white/12 bg-slate-950/70 shadow-[0_30px_80px_rgba(0,0,0,0.45)] backdrop-blur-xl">
            <div className="border-b border-white/10 px-8 py-6">
              <div className="flex items-center gap-3">
                <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-gradient-to-br from-teal-400 to-blue-500 shadow-lg shadow-cyan-500/20">
                  <LogIn className="h-6 w-6 text-white" />
                </div>
                <div>
                  <p className="text-xs font-semibold uppercase tracking-[0.28em] text-cyan-200/80">Secure access</p>
                  <h2 className="text-2xl font-semibold text-white">Sign in to OpsTrax</h2>
                </div>
              </div>
              <p className="mt-3 max-w-md text-sm leading-6 text-slate-300">
                Enter a tenant account to open the live command center. The same backend-auth path is used in
                production mode and in the local seeded environment.
              </p>
            </div>

            <div className="grid gap-6 px-8 py-8">
              {login.isError && (
                <div className="flex items-start gap-3 rounded-2xl border border-red-500/30 bg-red-500/10 p-4">
                  <AlertCircle className="mt-0.5 h-5 w-5 flex-shrink-0 text-red-300" />
                  <div className="flex-1">
                    <p className="font-semibold text-red-100">Login failed</p>
                    <p className="text-sm text-red-200/80">Invalid email or password. Try one of the accounts below.</p>
                  </div>
                </div>
              )}

              <form onSubmit={handleSubmit} className="space-y-5">
                <div>
                  <label className="mb-2 block text-sm font-medium text-slate-200">Email</label>
                  <input
                    type="email"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    placeholder="e.g. admin@northshore-fleet.com"
                    autoComplete="email"
                    className="w-full rounded-2xl border border-white/10 bg-white/5 px-4 py-3 text-white outline-none transition placeholder:text-slate-500 focus:border-cyan-300/50 focus:bg-white/8 focus:ring-2 focus:ring-cyan-400/20"
                  />
                </div>

                <div>
                  <label className="mb-2 block text-sm font-medium text-slate-200">Password</label>
                  <div className="relative">
                    <input
                      type={showPassword ? "text" : "password"}
                      value={password}
                      onChange={(e) => setPassword(e.target.value)}
                      placeholder="••••••••"
                      autoComplete="current-password"
                      className="w-full rounded-2xl border border-white/10 bg-white/5 px-4 py-3 pr-16 text-white outline-none transition placeholder:text-slate-500 focus:border-cyan-300/50 focus:bg-white/8 focus:ring-2 focus:ring-cyan-400/20"
                    />
                    <button
                      type="button"
                      onClick={() => setShowPassword(!showPassword)}
                      className="absolute right-3 top-1/2 -translate-y-1/2 rounded-full px-2 py-1 text-xs font-medium text-slate-300 transition hover:bg-white/10 hover:text-white"
                    >
                      {showPassword ? "Hide" : "Show"}
                    </button>
                  </div>
                </div>

                <button
                  type="submit"
                  disabled={login.isPending || !email.trim() || !password}
                  className="inline-flex w-full items-center justify-center gap-2 rounded-2xl bg-gradient-to-r from-teal-400 via-cyan-400 to-blue-500 px-4 py-3.5 font-semibold text-slate-950 shadow-lg shadow-cyan-500/25 transition hover:brightness-110 disabled:cursor-not-allowed disabled:opacity-60"
                >
                  {login.isPending ? (
                    <>
                      <span className="h-4 w-4 animate-spin rounded-full border-2 border-slate-950/30 border-t-slate-950" />
                      Signing in
                    </>
                  ) : (
                    <>
                      Enter command center
                      <ArrowRight className="h-4 w-4" />
                    </>
                  )}
                </button>
              </form>

              <div className="rounded-2xl border border-white/10 bg-white/5 p-4">
                <div className="mb-3 flex items-center justify-between gap-3">
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-400">Seeded access</p>
                    <p className="text-sm text-slate-200">Quick-fill a live account</p>
                  </div>
                  <BadgeCheck className="h-5 w-5 text-teal-300" />
                </div>
                <div className="grid gap-2 sm:grid-cols-2">
                  {accessShortcuts.map((user) => (
                    <button
                      key={user.email}
                      type="button"
                    onClick={() => fillAccess(user.email, user.password)}
                      className="rounded-xl border border-white/10 bg-slate-950/40 p-3 text-left transition hover:border-cyan-300/40 hover:bg-slate-900/70"
                    >
                    <p className="text-sm font-semibold text-white">{user.roleLabel}</p>
                    <p className="mt-1 truncate text-xs text-slate-400">{user.email}</p>
                    </button>
                  ))}
                </div>
              </div>

              <div className="flex items-start gap-3 rounded-2xl border border-white/10 bg-slate-950/35 p-4 text-sm text-slate-300">
                <CheckCircle2 className="mt-0.5 h-4 w-4 flex-shrink-0 text-teal-300" />
                <p>
                  CSRF protected, RBAC-aware, and backed by the same session path that powers the live command
                  center.
                </p>
              </div>
            </div>
          </div>
        </section>
      </div>
    </div>
  );
}
