import { useEffect, useRef, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import axios from "axios";
import {
  AlertCircle, ArrowRight, BarChart3, CheckCircle2, Clock,
  Eye, EyeOff, Globe, Lock, Mail, Map, Shield, Sparkles,
  Truck, Zap,
} from "lucide-react";
import { flushSync } from "react-dom";
import { useNavigate } from "react-router-dom";
import { getLandingRouteForSession } from "@/auth/sessionRouting";
import { useAuth } from "@/hooks/useAuth";
import { authApi } from "@/services/authApi";
import { OpsTraxLogo } from "@/components/OpsTraxLogo";

function getLoginErrorMessage(error: unknown): string {
  if (!axios.isAxiosError(error)) {
    return "We could not complete sign-in. Please try again.";
  }
  if (error.code === "ECONNABORTED") {
    return "OpsTrax is taking too long to respond. The backend may be waking up, so please try again in a few seconds.";
  }
  const status = error.response?.status;
  if (status === 401) {
    return "The email or password was not recognized. Please verify your credentials and try again.";
  }
  if (status === 403) {
    return "Security verification did not complete. Refresh the page and try signing in again.";
  }
  if (status === 429) {
    return "Too many sign-in attempts were detected. Wait a moment, then try again.";
  }
  if (!error.response) {
    return "We could not reach the OpsTrax API. Check the connection or retry once the service is fully awake.";
  }
  return String(error.response?.data?.message ?? "We could not complete sign-in. Please try again.");
}

/* ── Animated gradient mesh background ─────────────────────────────────── */
function MeshBackground() {
  const ref = useRef<HTMLCanvasElement>(null);
  useEffect(() => {
    const canvas = ref.current;
    if (!canvas) return;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;
    let raf = 0;
    const W = canvas.width = canvas.offsetWidth;
    const H = canvas.height = canvas.offsetHeight;

    type Orb = { x: number; y: number; r: number; vx: number; vy: number; color: string };
    const orbs: Orb[] = [
      { x: W * 0.2, y: H * 0.3, r: 400, vx: 0.3, vy: 0.2, color: "rgba(20,184,166,0.12)" },
      { x: W * 0.7, y: H * 0.6, r: 350, vx: -0.25, vy: 0.18, color: "rgba(99,102,241,0.08)" },
      { x: W * 0.5, y: H * 0.15, r: 320, vx: 0.2, vy: -0.25, color: "rgba(56,189,248,0.1)" },
      { x: W * 0.85, y: H * 0.2, r: 280, vx: -0.22, vy: 0.28, color: "rgba(20,184,166,0.09)" },
      { x: W * 0.3, y: H * 0.8, r: 300, vx: 0.28, vy: -0.2, color: "rgba(168,85,247,0.07)" },
    ];

    function draw() {
      ctx!.clearRect(0, 0, W, H);
      for (const o of orbs) {
        const g = ctx!.createRadialGradient(o.x, o.y, 0, o.x, o.y, o.r);
        g.addColorStop(0, o.color);
        g.addColorStop(0.5, o.color.replace(/[\d.]+\)$/, "0.04)"));
        g.addColorStop(1, "transparent");
        ctx!.fillStyle = g;
        ctx!.fillRect(0, 0, W, H);
        o.x += o.vx;
        o.y += o.vy;
        if (o.x < -o.r * 0.5 || o.x > W + o.r * 0.5) o.vx *= -1;
        if (o.y < -o.r * 0.5 || o.y > H + o.r * 0.5) o.vy *= -1;
      }
      raf = requestAnimationFrame(draw);
    }
    draw();
    return () => cancelAnimationFrame(raf);
  }, []);
  return <canvas ref={ref} className="absolute inset-0 h-full w-full" aria-hidden="true" />;
}

/* ── Floating particle field ───────────────────────────────────────────── */
function ParticleField() {
  const ref = useRef<HTMLCanvasElement>(null);
  useEffect(() => {
    const canvas = ref.current;
    if (!canvas) return;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;
    let raf = 0;
    const W = canvas.width = canvas.offsetWidth;
    const H = canvas.height = canvas.offsetHeight;

    type P = { x: number; y: number; s: number; o: number; vx: number; vy: number; pulse: number };
    const pts: P[] = Array.from({ length: 50 }, () => ({
      x: Math.random() * W, y: Math.random() * H,
      s: 1.5 + Math.random() * 2,
      o: 0.2 + Math.random() * 0.3,
      vx: (Math.random() - 0.5) * 0.4,
      vy: (Math.random() - 0.5) * 0.4,
      pulse: Math.random() * Math.PI * 2,
    }));

    function draw() {
      ctx!.clearRect(0, 0, W, H);
      for (const p of pts) {
        const pulseFactor = 0.75 + 0.25 * Math.sin(p.pulse);
        ctx!.beginPath();
        ctx!.arc(p.x, p.y, p.s * pulseFactor, 0, Math.PI * 2);
        ctx!.fillStyle = `rgba(20,184,166,${p.o * pulseFactor})`;
        ctx!.fill();
        p.x += p.vx;
        p.y += p.vy;
        p.pulse += 0.025;
        if (p.x < 0) p.x = W;
        if (p.x > W) p.x = 0;
        if (p.y < 0) p.y = H;
        if (p.y > H) p.y = 0;
      }
      // connecting lines
      for (let i = 0; i < pts.length; i++) {
        for (let j = i + 1; j < pts.length; j++) {
          const d = Math.hypot(pts[i].x - pts[j].x, pts[i].y - pts[j].y);
          if (d < 140) {
            const opacity = 0.08 * (1 - d / 140);
            ctx!.beginPath();
            ctx!.moveTo(pts[i].x, pts[i].y);
            ctx!.lineTo(pts[j].x, pts[j].y);
            ctx!.strokeStyle = `rgba(20,184,166,${opacity})`;
            ctx!.lineWidth = 0.7;
            ctx!.stroke();
          }
        }
      }
      raf = requestAnimationFrame(draw);
    }
    draw();
    return () => cancelAnimationFrame(raf);
  }, []);
  return <canvas ref={ref} className="absolute inset-0 h-full w-full" aria-hidden="true" />;
}

/* ── Feature pills ─────────────────────────────────────────────────────── */
const features = [
  { icon: <Truck className="h-4 w-4" />, label: "Live Fleet Tracking", color: "text-teal-600", border: "border-teal-200", bg: "bg-teal-50" },
  { icon: <Shield className="h-4 w-4" />, label: "HOS / ELD Compliance", color: "text-amber-600", border: "border-amber-200", bg: "bg-amber-50" },
  { icon: <BarChart3 className="h-4 w-4" />, label: "Safety Scorecards", color: "text-violet-600", border: "border-violet-200", bg: "bg-violet-50" },
  { icon: <Zap className="h-4 w-4" />, label: "AI Dispatch Copilot", color: "text-sky-600", border: "border-sky-200", bg: "bg-sky-50" },
  { icon: <Map className="h-4 w-4" />, label: "Route Optimization", color: "text-emerald-600", border: "border-emerald-200", bg: "bg-emerald-50" },
  { icon: <Clock className="h-4 w-4" />, label: "Maintenance Commands", color: "text-rose-600", border: "border-rose-200", bg: "bg-rose-50" },
];

/* ── Trust metrics ─────────────────────────────────────────────────────── */
const trustMetrics = [
  { value: "99.9%", label: "Uptime SLA" },
  { value: "SOC 2", label: "Compliant" },
  { value: "150+", label: "Fleets Managed" },
  { value: "12M+", label: "Miles Tracked" },
];

/* ── Main component ────────────────────────────────────────────────────── */
export function LoginPage() {
  const { setSession } = useAuth();
  const navigate = useNavigate();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPass] = useState(false);

  const login = useMutation({
    mutationFn: async ({ email: e, password: p }: { email: string; password: string }) => {
      await authApi.bootstrap();
      return authApi.login(e, p);
    },
    onSuccess: (session) => {
      flushSync(() => { setSession(session); });
      navigate(getLandingRouteForSession(session), { replace: true });
    },
  });

  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    if (email.trim() && password) login.mutate({ email: email.trim(), password });
  };

  return (
    <div className="login-page-root relative flex min-h-screen overflow-hidden bg-gradient-to-br from-slate-50 via-teal-50/30 to-sky-50/40">
      {/* ── Background layers ─────────────────────────────────────── */}
      <MeshBackground />
      <ParticleField />

      {/* Dot grid texture */}
      <div className="absolute inset-0" style={{ backgroundImage: "radial-gradient(circle,rgba(20,184,166,0.06) 1px,transparent 1px)", backgroundSize: "28px 28px" }} />

      {/* Ambient glow orbs */}
      <div className="login-glow-1 absolute -left-40 top-1/4 h-[500px] w-[500px] rounded-full bg-teal-400/15 blur-[140px]" />
      <div className="login-glow-2 absolute -right-32 bottom-1/4 h-[400px] w-[400px] rounded-full bg-indigo-400/10 blur-[120px]" />
      <div className="login-glow-1 absolute left-1/2 -top-20 h-[350px] w-[350px] rounded-full bg-sky-400/10 blur-[100px]" style={{ animationDelay: "2s" }} />
      <div className="login-glow-2 absolute left-1/4 bottom-0 h-[300px] w-[300px] rounded-full bg-violet-400/8 blur-[100px]" style={{ animationDelay: "1s" }} />

      {/* ── Top navigation bar ────────────────────────────────────── */}
      <nav className="absolute left-0 right-0 top-0 z-20 flex items-center justify-between px-8 py-5">
        <div className="flex items-center gap-3">
          <OpsTraxLogo size={36} />
          <span className="text-lg font-bold tracking-tight text-slate-900">OpsTrax</span>
          <span className="ml-2 rounded-full border border-teal-200 bg-teal-50 px-2 py-0.5 text-[10px] font-semibold text-teal-700">Enterprise</span>
        </div>
        <div className="hidden items-center gap-6 sm:flex">
          <span className="text-sm text-slate-600">Fleet Management Platform</span>
          <a href="https://www.kodekinetics.com" target="_blank" rel="noopener noreferrer" className="text-sm text-slate-500 transition hover:text-teal-600">Contact</a>
        </div>
      </nav>

      {/* ─ Main content grid ─────────────────────────────────────── */}
      <div className="relative z-10 flex min-h-screen w-full flex-col items-center justify-center px-6 py-20 lg:flex-row lg:gap-16 lg:px-12 xl:px-20">

        {/* ── LEFT — Hero content ─────────────────────────────────── */}
        <div className="flex flex-1 flex-col items-center lg:items-start lg:pr-8">
          {/* Eyebrow badge */}
          <div className="login-hero-enter mb-6 flex items-center gap-2 rounded-full border border-teal-200 bg-teal-50 px-4 py-1.5">
            <Sparkles className="h-3.5 w-3.5 text-teal-600" />
            <span className="text-xs font-semibold text-teal-700">Unified Fleet Operations</span>
          </div>

          {/* Headline */}
          <h1 className="login-hero-enter text-center text-4xl font-extrabold leading-tight tracking-tight text-slate-900 sm:text-5xl lg:text-left xl:text-6xl" style={{ animationDelay: "0.1s" }}>
            Fleet intelligence,
            <br />
            <span className="bg-gradient-to-r from-teal-600 via-emerald-600 to-cyan-600 bg-clip-text text-transparent">delivered live.</span>
          </h1>

          {/* Subheadline */}
          <p className="login-hero-enter mt-5 max-w-lg text-center text-base leading-relaxed text-slate-600 lg:text-left lg:text-lg" style={{ animationDelay: "0.2s" }}>
            Dispatch, safety, compliance, maintenance, and AI-powered insights — unified in a single operations platform built for modern fleets.
          </p>

          {/* Feature pills grid */}
          <div className="login-hero-enter mt-8 grid grid-cols-2 gap-2.5 sm:grid-cols-3" style={{ animationDelay: "0.3s" }}>
            {features.map((f) => (
              <div key={f.label} className={`flex items-center gap-2 rounded-xl border ${f.border} ${f.bg} px-3 py-2 transition hover:scale-[1.02]`}>
                <span className={f.color}>{f.icon}</span>
                <span className="text-xs font-medium text-slate-700">{f.label}</span>
              </div>
            ))}
          </div>

          {/* Trust metrics */}
          <div className="login-hero-enter mt-10 flex flex-wrap items-center justify-center gap-6 lg:justify-start" style={{ animationDelay: "0.4s" }}>
            {trustMetrics.map((m, i) => (
              <div key={m.label} className={`flex items-center gap-3 ${i !== 0 ? "border-l border-slate-200 pl-6" : ""}`}>
                <div>
                  <p className="text-xl font-bold text-slate-900">{m.value}</p>
                  <p className="text-[10px] font-medium uppercase tracking-wider text-slate-500">{m.label}</p>
                </div>
              </div>
            ))}
          </div>

          {/* Security badges */}
          <div className="login-hero-enter mt-6 flex items-center gap-3" style={{ animationDelay: "0.5s" }}>
            <div className="flex items-center gap-1.5 text-slate-500">
              <Shield className="h-3.5 w-3.5" />
              <span className="text-[10px] font-medium">CSRF Protected</span>
            </div>
            <div className="h-3 w-px bg-slate-300" />
            <div className="flex items-center gap-1.5 text-slate-500">
              <Lock className="h-3.5 w-3.5" />
              <span className="text-[10px] font-medium">RBAC Enforced</span>
            </div>
            <div className="h-3 w-px bg-slate-300" />
            <div className="flex items-center gap-1.5 text-slate-500">
              <Globe className="h-3.5 w-3.5" />
              <span className="text-[10px] font-medium">Multi-Region</span>
            </div>
          </div>
        </div>

        {/* ── RIGHT — Glass login card ────────────────────────────── */}
        <div className="login-card-enter mt-12 w-full max-w-[420px] lg:mt-0">
          <div className="relative rounded-3xl border border-slate-200/80 bg-white/80 p-8 shadow-xl backdrop-blur-xl">
            {/* Inner gradient overlay */}
            <div className="absolute inset-0 rounded-3xl bg-gradient-to-br from-teal-50/50 via-transparent to-sky-50/30" />

            <div className="relative">
              {/* Card header */}
              <div className="mb-6">
                <h2 className="text-xl font-bold text-slate-900">Welcome back</h2>
                <p className="mt-1 text-sm text-slate-600">Sign in to access your fleet operations dashboard</p>
              </div>

              {/* Error */}
              {login.isError && (
                <div className="mb-5 flex items-start gap-2.5 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
                  <AlertCircle className="mt-0.5 h-4 w-4 shrink-0 text-red-500" />
                  <span>{getLoginErrorMessage(login.error)}</span>
                </div>
              )}

              {/* Form */}
              <form onSubmit={submit} className="space-y-4">
                <div>
                  <label className="mb-1.5 flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wider text-slate-600">
                    <Mail className="h-3.5 w-3.5" /> Email
                  </label>
                  <input
                    type="email"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    autoComplete="email"
                    placeholder="you@company.com"
                    className="w-full rounded-xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 placeholder-slate-400 outline-none transition focus:border-teal-500 focus:ring-2 focus:ring-teal-500/20"
                  />
                </div>

                <div>
                  <label className="mb-1.5 flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wider text-slate-600">
                    <Lock className="h-3.5 w-3.5" /> Password
                  </label>
                  <div className="relative">
                    <input
                      type={showPassword ? "text" : "password"}
                      value={password}
                      onChange={(e) => setPassword(e.target.value)}
                      autoComplete="current-password"
                      placeholder="Enter your password"
                      className="w-full rounded-xl border border-slate-200 bg-white px-4 py-3 pr-12 text-sm text-slate-900 placeholder-slate-400 outline-none transition focus:border-teal-500 focus:ring-2 focus:ring-teal-500/20"
                    />
                    <button
                      type="button"
                      onClick={() => setShowPass((v) => !v)}
                      className="absolute right-3 top-1/2 -translate-y-1/2 text-slate-400 transition hover:text-slate-600 cursor-pointer"
                    >
                      {showPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                    </button>
                  </div>
                </div>

                <button
                  type="submit"
                  disabled={login.isPending || !email.trim() || !password}
                  className="mt-2 flex w-full items-center justify-center gap-2 rounded-xl bg-gradient-to-r from-teal-500 to-emerald-500 px-4 py-3 text-sm font-bold text-white shadow-lg shadow-teal-500/25 transition hover:shadow-xl hover:shadow-teal-500/30 hover:from-teal-600 hover:to-emerald-600 disabled:cursor-not-allowed disabled:opacity-50 cursor-pointer"
                >
                  {login.isPending ? (
                    <><span className="h-4 w-4 animate-spin rounded-full border-2 border-white/30 border-t-white" /> Signing in…</>
                  ) : (
                    <>Sign in <ArrowRight className="h-4 w-4" /></>
                  )}
                </button>
              </form>

              {/* Divider */}
              <div className="mt-6 flex items-center gap-3">
                <div className="h-px flex-1 bg-slate-200" />
                <span className="text-[10px] font-medium uppercase tracking-wider text-slate-500">Secured Access</span>
                <div className="h-px flex-1 bg-slate-200" />
              </div>

              {/* Security features */}
              <div className="mt-4 grid grid-cols-3 gap-2">
                {[
                  { icon: <Shield className="h-3.5 w-3.5" />, label: "CSRF" },
                  { icon: <Lock className="h-3.5 w-3.5" />, label: "RBAC" },
                  { icon: <CheckCircle2 className="h-3.5 w-3.5" />, label: "Encrypted" },
                ].map((s) => (
                  <div key={s.label} className="flex items-center justify-center gap-1.5 rounded-lg border border-slate-200 bg-slate-50 py-2">
                    <span className="text-teal-600">{s.icon}</span>
                    <span className="text-[10px] font-medium text-slate-600">{s.label}</span>
                  </div>
                ))}
              </div>
            </div>
          </div>

          {/* Card footer */}
          <div className="mt-4 text-center">
            <p className="text-[11px] text-slate-500">
              Engineered by{" "}
              <a href="https://www.kodekinetics.com" target="_blank" rel="noopener noreferrer" className="font-medium text-slate-600 transition hover:text-teal-600">Kode Kinetics</a>
              {" · "}
              <a href="mailto:info@kodekinetics.com" className="text-slate-500 transition hover:text-teal-600">info@kodekinetics.com</a>
            </p>
          </div>
        </div>
      </div>

      {/* ── Bottom status bar ──────────────────────────────────────── */}
      <div className="absolute bottom-0 left-0 right-0 z-20 flex items-center justify-between border-t border-slate-200/60 bg-white/70 px-8 py-3 backdrop-blur-sm">
        <div className="flex items-center gap-2">
          <span className="h-1.5 w-1.5 rounded-full bg-emerald-500 animate-pulse" />
          <span className="text-[10px] font-medium text-slate-600">All systems operational</span>
        </div>
        <div className="flex items-center gap-4">
          <span className="text-[10px] text-slate-500">v3.2.1</span>
          <span className="text-[10px] text-slate-500">Northshore Fleet Logistics</span>
        </div>
      </div>
    </div>
  );
}
