import { useEffect, useRef, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import axios from "axios";
import { AlertCircle, ArrowRight } from "lucide-react";
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

/* ── Telemetry particle canvas ──────────────────────────────────────────── */
function TelemetryCanvas() {
  const ref = useRef<HTMLCanvasElement>(null);

  useEffect(() => {
    const canvas = ref.current;
    if (!canvas) return;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    let raf = 0;
    const W = canvas.width  = canvas.offsetWidth;
    const H = canvas.height = canvas.offsetHeight;

    type Particle = { x: number; y: number; speed: number; size: number; opacity: number; color: string; burst: boolean };

    const COLORS = ["#2dd4bf", "#7dd3fc", "#a5f3fc", "#ffffff", "#2dd4bf", "#2dd4bf"];
    const particles: Particle[] = Array.from({ length: 55 }, () => ({
      x:       Math.random() * W,
      y:       Math.random() * H,
      speed:   0.3 + Math.random() * 0.9,
      size:    Math.random() < 0.15 ? 2.2 : 1.1,
      opacity: 0.05 + Math.random() * 0.4,
      color:   COLORS[Math.floor(Math.random() * COLORS.length)],
      burst:   Math.random() < 0.08,
    }));

    function draw() {
      ctx!.clearRect(0, 0, W, H);
      for (const p of particles) {
        ctx!.beginPath();
        ctx!.arc(p.x, p.y, p.size, 0, Math.PI * 2);
        ctx!.fillStyle = p.color;
        ctx!.globalAlpha = p.opacity;
        ctx!.fill();

        // occasional connecting lines between nearby particles
        if (p.burst) {
          for (const q of particles) {
            const d = Math.hypot(p.x - q.x, p.y - q.y);
            if (d < 60 && d > 0) {
              ctx!.beginPath();
              ctx!.moveTo(p.x, p.y);
              ctx!.lineTo(q.x, q.y);
              ctx!.strokeStyle = "#2dd4bf";
              ctx!.globalAlpha = 0.04 * (1 - d / 60);
              ctx!.lineWidth = 0.5;
              ctx!.stroke();
            }
          }
        }

        p.y -= p.speed;
        if (p.y < -4) {
          p.y = H + 4;
          p.x = Math.random() * W;
          p.opacity = 0.05 + Math.random() * 0.35;
        }
      }
      ctx!.globalAlpha = 1;
      raf = requestAnimationFrame(draw);
    }

    draw();
    return () => cancelAnimationFrame(raf);
  }, []);

  return (
    <canvas
      ref={ref}
      className="absolute inset-0 h-full w-full"
      style={{ opacity: 0.55 }}
      aria-hidden="true"
    />
  );
}

/* ── Floating live vehicle cards ────────────────────────────────────────── */
const vehicleEvents = [
  { code: "TRK-114", status: "On Route", detail: "87 mph · Dubai–Abu Dhabi E11", color: "#2dd4bf", dot: "●" },
  { code: "BOX-106", status: "Arrived",  detail: "Manassas Depot · 09:44 AM",   color: "#4ade80", dot: "✓" },
  { code: "VAN-211", status: "Alert",    detail: "Speed event · Jeddah Ring Rd", color: "#fbbf24", dot: "⚠" },
  { code: "KSA-119", status: "En Route", detail: "ETA 14 min · Riyadh N Gate",   color: "#7dd3fc", dot: "●" },
];

function FloatingStatusCards() {
  return (
    <div className="absolute right-8 top-1/2 -translate-y-1/2 flex flex-col gap-3 pointer-events-none" aria-hidden="true">
      {vehicleEvents.map((v, i) => (
        <div
          key={v.code}
          className="login-float-card rounded-xl border px-3.5 py-2.5"
          style={{
            animationDelay: `${i * 0.9}s`,
            borderColor: `${v.color}30`,
            background: `rgba(12,21,38,0.72)`,
            backdropFilter: "blur(8px)",
            minWidth: 188,
          }}
        >
          <div className="flex items-center justify-between gap-4">
            <span className="text-[11px] font-bold text-white/80">{v.code}</span>
            <span className="text-[10px] font-semibold" style={{ color: v.color }}>
              {v.dot} {v.status}
            </span>
          </div>
          <p className="mt-0.5 text-[10px] text-slate-500">{v.detail}</p>
        </div>
      ))}
    </div>
  );
}

/* ── Live event ticker ──────────────────────────────────────────────────── */
const tickerItems = [
  "TRK-114 · En route Dubai–Abu Dhabi · ETA 14 min",
  "BOX-106 · Arrived Manassas Depot · 09:44 AM",
  "VAN-211 · Speed alert · 87 mph Jeddah Ring Rd",
  "KSA-REEFER-119 · Departed Riyadh North · Load confirmed",
  "KSA-REEFER-214 · Checkpoint clear · Jubail Gate 3",
  "DISPATCH · JOB-0520 assigned to TRK-114 · Priority High",
];

function LiveTicker() {
  const text = tickerItems.join("   ·   ");
  return (
    <div className="relative overflow-hidden border-t border-white/6 py-2" aria-hidden="true">
      <div className="flex gap-0 whitespace-nowrap login-ticker-track">
        {[text, text].map((t, i) => (
          <span key={i} className="inline-block shrink-0 pr-16 text-[10px] font-medium text-slate-500">
            {t}
          </span>
        ))}
      </div>
    </div>
  );
}

/* ── Route SVG with animated vehicles ──────────────────────────────────── */
function RouteMap() {
  return (
    <svg
      viewBox="0 0 600 480"
      fill="none"
      className="absolute inset-0 h-full w-full"
      preserveAspectRatio="xMidYMid slice"
      aria-hidden="true"
    >
      <defs>
        {/* Glow filter for vehicle dots */}
        <filter id="vglow" x="-80%" y="-80%" width="260%" height="260%">
          <feGaussianBlur stdDeviation="4" result="blur" />
          <feMerge>
            <feMergeNode in="blur" />
            <feMergeNode in="SourceGraphic" />
          </feMerge>
        </filter>
        <filter id="sglow" x="-60%" y="-60%" width="220%" height="220%">
          <feGaussianBlur stdDeviation="7" result="blur" />
          <feMerge>
            <feMergeNode in="blur" />
            <feMergeNode in="SourceGraphic" />
          </feMerge>
        </filter>

        {/* Scanning-line gradient */}
        <linearGradient id="scanGrad" x1="0" y1="0" x2="1" y2="0">
          <stop offset="0%"   stopColor="transparent" />
          <stop offset="35%"  stopColor="#2dd4bf" stopOpacity="0.25" />
          <stop offset="50%"  stopColor="#2dd4bf" stopOpacity="0.55" />
          <stop offset="65%"  stopColor="#2dd4bf" stopOpacity="0.25" />
          <stop offset="100%" stopColor="transparent" />
        </linearGradient>

        {/* Route paths (referenced by animateMotion) */}
        <path id="r1" d="M 20 400 Q 160 300 320 210 L 510 120" />
        <path id="r2" d="M 50 450 Q 220 370 390 330 L 570 270" />
        <path id="r3" d="M 10 190 Q 150 145 300 160 Q 430 175 550 115" />
      </defs>

      {/* Visible route strokes */}
      <use href="#r1" stroke="#2dd4bf"  strokeWidth="1.4" strokeDasharray="7 5"  opacity="0.28" />
      <use href="#r2" stroke="#7dd3fc"  strokeWidth="1"   strokeDasharray="5 7"  opacity="0.2" />
      <use href="#r3" stroke="#a5f3fc"  strokeWidth="0.8"                        opacity="0.12" />
      {/* fourth subtle route */}
      <path d="M 120 460 Q 280 400 440 390 L 590 360" stroke="#64748b" strokeWidth="0.7" strokeDasharray="3 9" opacity="0.15" />

      {/* ── Moving vehicle 1 (teal) — primary route ── */}
      {/* Ghost trail dot 2 */}
      <circle r="1.5" fill="#2dd4bf">
        <animateMotion dur="9s" begin="-1.4s" repeatCount="indefinite">
          <mpath href="#r1" />
        </animateMotion>
        <animate attributeName="opacity" values="0;0.22;0.22;0" dur="9s" begin="-1.4s" repeatCount="indefinite" />
      </circle>
      {/* Ghost trail dot 1 */}
      <circle r="2.5" fill="#2dd4bf">
        <animateMotion dur="9s" begin="-0.7s" repeatCount="indefinite">
          <mpath href="#r1" />
        </animateMotion>
        <animate attributeName="opacity" values="0;0.45;0.45;0" dur="9s" begin="-0.7s" repeatCount="indefinite" />
      </circle>
      {/* Main vehicle dot with glow */}
      <circle r="5" filter="url(#vglow)" fill="#2dd4bf">
        <animateMotion dur="9s" repeatCount="indefinite">
          <mpath href="#r1" />
        </animateMotion>
        <animate attributeName="opacity" values="0;1;1;0.8;0" dur="9s" repeatCount="indefinite" />
        <animate attributeName="r" values="5;6;5" dur="1.8s" repeatCount="indefinite" />
      </circle>

      {/* ── Moving vehicle 2 (sky blue) — second route ── */}
      <circle r="1.5" fill="#7dd3fc">
        <animateMotion dur="13s" begin="-7.5s" repeatCount="indefinite">
          <mpath href="#r2" />
        </animateMotion>
        <animate attributeName="opacity" values="0;0.3;0.3;0" dur="13s" begin="-7.5s" repeatCount="indefinite" />
      </circle>
      <circle r="4" filter="url(#vglow)" fill="#7dd3fc">
        <animateMotion dur="13s" begin="-6s" repeatCount="indefinite">
          <mpath href="#r2" />
        </animateMotion>
        <animate attributeName="opacity" values="0;0.85;0.85;0" dur="13s" begin="-6s" repeatCount="indefinite" />
      </circle>

      {/* ── Moving vehicle 3 (cyan soft) — upper route ── */}
      <circle r="3" filter="url(#vglow)" fill="#a5f3fc">
        <animateMotion dur="16s" begin="-9s" repeatCount="indefinite">
          <mpath href="#r3" />
        </animateMotion>
        <animate attributeName="opacity" values="0;0.6;0.6;0" dur="16s" begin="-9s" repeatCount="indefinite" />
      </circle>

      {/* ── Static endpoint nodes ── */}
      {/* Primary terminus — teal */}
      <g filter="url(#sglow)">
        <circle cx="510" cy="120" r="6" fill="#2dd4bf" opacity="0.95" />
      </g>
      <circle cx="510" cy="120" r="13" stroke="#2dd4bf" strokeWidth="1"   fill="none" opacity="0" className="login-dot-ping" />
      <circle cx="510" cy="120" r="20" stroke="#2dd4bf" strokeWidth="0.5" fill="none" opacity="0" className="login-dot-ping-outer" />

      {/* Secondary terminus */}
      <circle cx="570" cy="270" r="4.5" fill="#7dd3fc" opacity="0.8" filter="url(#vglow)" />
      <circle cx="570" cy="270" r="10"  stroke="#7dd3fc" strokeWidth="0.8" fill="none" opacity="0" className="login-dot-ping-2" />

      {/* Waypoint labels */}
      <circle cx="20" cy="400" r="3" fill="#2dd4bf" opacity="0.5" />
      <text x="28" y="404" fill="#2dd4bf" fontSize="8" opacity="0.45" fontFamily="Inter, sans-serif">Riyadh</text>

      <circle cx="50" cy="450" r="2.5" fill="#7dd3fc" opacity="0.4" />
      <text x="58" y="454" fill="#7dd3fc" fontSize="8" opacity="0.35" fontFamily="Inter, sans-serif">Jeddah</text>

      <circle cx="10" cy="190" r="2.5" fill="#a5f3fc" opacity="0.35" />
      <text x="18" y="194" fill="#a5f3fc" fontSize="7" opacity="0.3" fontFamily="Inter, sans-serif">Dubai</text>

      {/* ── Horizontal scanning sweep ── */}
      <rect x="0" y="0" width="600" height="2" fill="url(#scanGrad)" className="login-scan-line" />
    </svg>
  );
}

/* ── Live metrics ───────────────────────────────────────────────────────── */
const metrics = [
  { value: "12",  label: "vehicles active" },
  { value: "94%", label: "on-time rate"    },
  { value: "3",   label: "open exceptions" },
];

/* ── Main component ─────────────────────────────────────────────────────── */
export function LoginPage() {
  const { setSession } = useAuth();
  const navigate = useNavigate();
  const [email, setEmail]           = useState("");
  const [password, setPassword]     = useState("");
  const [showPassword, setShowPass] = useState(false);

  const login = useMutation({
    mutationFn: async ({ email: e, password: p }: { email: string; password: string }) => {
      await authApi.bootstrap();
      return authApi.login(e, p);
    },
    onSuccess: (session) => {
      flushSync(() => {
        setSession(session);
      });
      navigate(getLandingRouteForSession(session), { replace: true });
    },
  });

  const submit = (e: React.FormEvent) => { e.preventDefault(); if (email.trim() && password) login.mutate({ email: email.trim(), password }); };

  return (
    <div className="flex min-h-screen">

      {/* ── LEFT — animated brand panel ───────────────────── */}
      <div className="login-brand-panel login-panel-enter relative hidden lg:flex lg:w-[58%] xl:w-[62%] flex-col overflow-hidden">

        {/* Dot-grid texture */}
        <div className="absolute inset-0" style={{ backgroundImage: "radial-gradient(circle,rgba(255,255,255,0.05) 1px,transparent 1px)", backgroundSize: "32px 32px", opacity: 0.45 }} />

        {/* Ambient glow — breathing */}
        <div className="login-glow-1 absolute -left-32 top-1/4 h-80 w-80 rounded-full bg-teal-500/12 blur-[96px]" />
        <div className="login-glow-2 absolute right-0 bottom-1/3 h-64 w-64 rounded-full bg-sky-500/8 blur-[80px]"  />
        <div className="login-glow-1 absolute left-1/3 bottom-0  h-48 w-48 rounded-full bg-teal-400/7 blur-[60px]"  style={{ animationDelay: "2s" }} />

        {/* Canvas particle stream */}
        <TelemetryCanvas />

        {/* Animated route map */}
        <RouteMap />

        {/* Floating live vehicle cards */}
        <FloatingStatusCards />

        {/* Content layer */}
        <div className="relative flex h-full flex-col px-12 py-10 xl:px-16">

          {/* Logo */}
          <div className="flex items-center gap-3">
            <OpsTraxLogo size={40} />
            <span className="text-xl font-semibold tracking-tight text-white">OpsTrax</span>
          </div>

          {/* Hero */}
          <div className="flex flex-1 flex-col justify-center">
            <p className="text-[11px] font-semibold uppercase tracking-widest text-teal-400">Fleet Management Platform</p>
            <h1 className="mt-4 text-5xl font-bold leading-[1.08] tracking-tight text-white xl:text-6xl">
              Fleet intelligence,
              <br />
              <span className="text-teal-400">live.</span>
            </h1>
            <p className="mt-5 max-w-sm text-base leading-7 text-slate-400">
              Dispatch, safety, compliance, and maintenance — unified for operations teams.
            </p>

            {/* Live metrics */}
            <div className="mt-10 flex items-start gap-10">
              {metrics.map((m, i) => (
                <div key={m.label} className={`${i !== 0 ? "border-l border-white/10 pl-10" : ""} login-metric-${i + 1}`}>
                  <div className="flex items-baseline gap-2">
                    <span className="text-4xl font-bold text-white">{m.value}</span>
                    <span className="text-[10px] font-semibold uppercase tracking-widest text-teal-400">live</span>
                  </div>
                  <p className="mt-1 text-sm text-slate-500">{m.label}</p>
                </div>
              ))}
            </div>
          </div>

          {/* Live ticker strip */}
          <div className="mb-3">
            <LiveTicker />
          </div>

          {/* Bottom bar */}
          <div className="flex items-end justify-between gap-4">
            <p className="text-xs text-slate-700">Northshore Fleet Logistics · Enterprise</p>
            <div className="text-right">
              <p className="text-[11px] text-slate-600">Engineered by</p>
              <a href="https://www.kodekinetics.com" target="_blank" rel="noopener noreferrer"
                className="mt-0.5 inline-flex items-center gap-1.5 text-xs font-semibold text-slate-400 transition hover:text-teal-400">
                <span className="inline-flex h-4 w-4 items-center justify-center rounded bg-teal-500/20 text-[9px] font-bold text-teal-400">KK</span>
                Kode Kinetics
              </a>
            </div>
          </div>
        </div>
      </div>

      {/* ── RIGHT — sign-in form ───────────────────────────── */}
      <div className="flex flex-1 flex-col items-center justify-center bg-white px-8 py-12 lg:px-12">

        {/* Mobile logo */}
        <div className="mb-10 flex items-center gap-2.5 lg:hidden">
          <OpsTraxLogo size={32} />
          <span className="text-lg font-bold text-slate-900">OpsTrax</span>
        </div>

        <div className="login-form-enter w-full max-w-sm">

          <div className="mb-8">
            <h2 className="text-2xl font-bold text-slate-900">Sign in</h2>
            <p className="mt-1.5 text-sm text-slate-500">Access your fleet operations dashboard</p>
          </div>

          {login.isError && (
            <div className="mb-5 flex items-center gap-2.5 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
              <AlertCircle className="h-4 w-4 shrink-0" />
              {getLoginErrorMessage(login.error)}
            </div>
          )}

          <form onSubmit={submit} className="space-y-4">
            <div>
              <label className="mb-1.5 block text-sm font-medium text-slate-700">Email</label>
              <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="email"
                placeholder="you@company.com"
                className="w-full rounded-lg border border-slate-300 bg-white px-3.5 py-2.5 text-sm text-slate-900 outline-none placeholder:text-slate-400 transition focus:border-teal-500 focus:ring-2 focus:ring-teal-500/15" />
            </div>

            <div>
              <label className="mb-1.5 block text-sm font-medium text-slate-700">Password</label>
              <div className="relative">
                <input type={showPassword ? "text" : "password"} value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password"
                  placeholder="••••••••"
                  className="w-full rounded-lg border border-slate-300 bg-white px-3.5 py-2.5 pr-14 text-sm text-slate-900 outline-none placeholder:text-slate-400 transition focus:border-teal-500 focus:ring-2 focus:ring-teal-500/15" />
                <button type="button" onClick={() => setShowPass((v) => !v)}
                  className="absolute right-3 top-1/2 -translate-y-1/2 text-xs font-medium text-slate-400 transition hover:text-slate-700">
                  {showPassword ? "Hide" : "Show"}
                </button>
              </div>
            </div>

            <button type="submit" disabled={login.isPending || !email.trim() || !password}
              className="mt-2 flex w-full items-center justify-center gap-2 rounded-lg bg-teal-500 px-4 py-2.5 text-sm font-semibold text-white shadow-sm transition hover:bg-teal-600 focus:outline-none focus:ring-2 focus:ring-teal-500/30 disabled:cursor-not-allowed disabled:opacity-50">
              {login.isPending
                ? <><span className="h-4 w-4 animate-spin rounded-full border-2 border-white/30 border-t-white" /> Signing in…</>
                : <>Sign in <ArrowRight className="h-4 w-4" /></>}
            </button>
          </form>

          {/* Footer */}
          <div className="mt-8 space-y-1.5 text-center">
            <p className="text-[11px] text-slate-300">Protected by CSRF · RBAC · Session isolation</p>
            <p className="text-[11px] text-slate-300">
              Built by{" "}
              <a href="https://www.kodekinetics.com" target="_blank" rel="noopener noreferrer"
                className="font-medium text-slate-400 transition hover:text-teal-500">Kode Kinetics</a>
              {" · "}
              <a href="mailto:info@kodekinetics.com" className="text-slate-400 transition hover:text-teal-500">info@kodekinetics.com</a>
            </p>
          </div>
        </div>
      </div>
    </div>
  );
}
