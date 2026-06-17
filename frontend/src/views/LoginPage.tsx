'use client';

import { useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import {
  BarChart3, CheckCircle2, Clock, Eye, EyeOff,
  ShieldCheck, TrendingUp, Users, Zap,
} from 'lucide-react';
import { useAuth } from '../contexts/AuthContext';
import { Logo } from '../components/Logo';
import { InfoTip } from '../components/InfoTip';

const FEATURES = [
  { icon: Users,      label: 'People & Payroll',   desc: 'Headcount, payslips, WPS compliance' },
  { icon: BarChart3,  label: 'Live Analytics',      desc: 'Real-time KPI trends and AI insights' },
  { icon: ShieldCheck,label: 'GCC Compliance',      desc: 'Saudi, UAE labour law built-in' },
  { icon: Zap,        label: 'Auto Workflows',      desc: 'Leave, overtime, approvals automated' },
];

export function LoginPage() {
  const { login } = useAuth();
  const router     = useRouter();
  const searchParams = useSearchParams();
  const from = searchParams?.get('from') ?? '/dashboard';

  const [email,        setEmail]        = useState('');
  const [password,     setPassword]     = useState('');
  const [tenantSlug,   setTenantSlug]   = useState('');
  const [error,        setError]        = useState('');
  const [isLoading,    setIsLoading]    = useState(false);
  const [showPassword, setShowPassword] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setIsLoading(true);
    try {
      await login(email, password, tenantSlug);
      router.replace(from);
    } catch {
      setError('Invalid credentials. Please check your email, password, and workspace.');
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <>
      <style>{`
        @keyframes kx-float  { 0%,100%{transform:translateY(0)}   50%{transform:translateY(-14px)} }
        @keyframes kx-float2 { 0%,100%{transform:translateY(0)}   50%{transform:translateY(-20px)} }
        @keyframes kx-float3 { 0%,100%{transform:translateY(0)}   50%{transform:translateY(-10px)} }
        @keyframes kx-spin-l { from{transform:rotate(0deg)}   to{transform:rotate(360deg)}  }
        @keyframes kx-spin-r { from{transform:rotate(0deg)}   to{transform:rotate(-360deg)} }
        @keyframes kx-ring   { 0%{transform:scale(1);opacity:0.35} 100%{transform:scale(2.4);opacity:0} }
        @keyframes kx-drift  { 0%,100%{transform:translate(0,0)} 40%{transform:translate(10px,-8px)} 70%{transform:translate(-8px,10px)} }
        @keyframes kx-glow   { 0%,100%{opacity:0.12} 50%{opacity:0.22} }
        .kx-f1{animation:kx-float  5.5s ease-in-out infinite}
        .kx-f2{animation:kx-float2 7.2s ease-in-out infinite 1.8s}
        .kx-f3{animation:kx-float3 6.4s ease-in-out infinite 3.6s}
        .kx-sl{animation:kx-spin-l 80s linear infinite}
        .kx-sr{animation:kx-spin-r 60s linear infinite}
        .kx-r1{animation:kx-ring 2.8s ease-out infinite}
        .kx-r2{animation:kx-ring 2.8s ease-out infinite 0.9s}
        .kx-r3{animation:kx-ring 2.8s ease-out infinite 1.8s}
        .kx-dr{animation:kx-drift 12s ease-in-out infinite}
        .kx-dr2{animation:kx-drift 16s ease-in-out infinite 4s}
        .kx-dr3{animation:kx-drift 20s ease-in-out infinite 8s}
        .kx-gl{animation:kx-glow 4s ease-in-out infinite}
        .kx-gl2{animation:kx-glow 5s ease-in-out infinite 2s}
        .kx-dots{background-image:radial-gradient(circle,#7fb3ff 1.2px,transparent 1.2px);background-size:28px 28px}
      `}</style>

      <div className="flex min-h-screen bg-[#020b1f]">

        {/* ══ LEFT PANEL ═══════════════════════════════════════════════════════ */}
        <div className="relative hidden lg:flex lg:w-[58%] flex-col overflow-hidden">

          {/* Deep gradient base */}
          <div className="absolute inset-0 bg-gradient-to-br from-[#020818] via-[#07122e] to-[#030b1c]" />

          {/* Animated colour blobs */}
          <div className="kx-dr  pointer-events-none absolute -top-40 -left-40 h-[600px] w-[600px] rounded-full bg-blue-600/[0.13] blur-[80px] kx-gl" />
          <div className="kx-dr2 pointer-events-none absolute top-1/3 right-0 h-[450px] w-[450px] translate-x-1/3 rounded-full bg-indigo-500/[0.10] blur-[80px] kx-gl2" />
          <div className="kx-dr3 pointer-events-none absolute bottom-0 left-1/4 h-[400px] w-[400px] rounded-full bg-cyan-500/[0.08] blur-[80px] kx-gl" />

          {/* Dot-grid watermark */}
          <div className="kx-dots pointer-events-none absolute inset-0 opacity-[0.045]" />

          {/* Rotating concentric rings */}
          <div className="pointer-events-none absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2">
            <div className="kx-sl  h-[900px] w-[900px] rounded-full border border-white/[0.035]" />
            <div className="kx-sr  absolute inset-[60px] rounded-full border border-white/[0.025]" />
            <div className="kx-sl  absolute inset-[120px] rounded-full border border-dashed border-white/[0.018]" />
            <div className="kx-sr  absolute inset-[200px] rounded-full border border-white/[0.012]" />
          </div>

          {/* Large text watermark */}
          <div className="pointer-events-none absolute inset-0 flex items-center justify-center overflow-hidden">
            <p className="select-none whitespace-nowrap text-[160px] font-black tracking-[-0.06em] text-white opacity-[0.022] -rotate-[10deg]">
              KYNEXONE
            </p>
          </div>

          {/* Radar pulse rings (anchored at mid-left of panel) */}
          <div className="pointer-events-none absolute left-[28%] top-[52%]">
            <div className="kx-r1 absolute h-3 w-3 -translate-x-1/2 -translate-y-1/2 rounded-full border-2 border-blue-400/50" />
            <div className="kx-r2 absolute h-3 w-3 -translate-x-1/2 -translate-y-1/2 rounded-full border-2 border-cyan-400/40" />
            <div className="kx-r3 absolute h-3 w-3 -translate-x-1/2 -translate-y-1/2 rounded-full border-2 border-indigo-400/30" />
            <div className="absolute h-1.5 w-1.5 -translate-x-1/2 -translate-y-1/2 rounded-full bg-blue-400 shadow-[0_0_12px_4px_rgba(96,165,250,0.5)]" />
          </div>

          {/* ── Content ────────────────────────────────────────────────────── */}
          <div className="relative z-10 flex h-full flex-col justify-between p-12 xl:p-14">

            {/* Logo — large + glow */}
            <div className="flex items-center gap-3">
              <div className="relative">
                <div className="absolute -inset-3 rounded-2xl bg-blue-500/25 blur-2xl" />
                <div className="relative flex items-center gap-3">
                  <Logo collapsed={false} size="lg" />
                </div>
              </div>
            </div>

            {/* Headline + stats */}
            <div className="space-y-10">
              <div>
                <p className="mb-3 text-[11px] font-bold uppercase tracking-[0.24em] text-blue-400/70">
                  Enterprise Workforce Intelligence
                </p>
                <h1 className="text-[2.6rem] font-extrabold leading-[1.1] tracking-tight text-white xl:text-5xl">
                  One platform for<br />
                  <span className="bg-gradient-to-r from-cyan-300 via-blue-300 to-indigo-400 bg-clip-text text-transparent">
                    every workforce
                  </span>
                  <br />operation.
                </h1>
                <p className="mt-4 max-w-md text-sm leading-relaxed text-blue-200/55">
                  From hire to retire — payroll, attendance, compliance, and AI insights in a single unified workspace built for GCC enterprises.
                </p>
              </div>

              {/* ── Floating glassmorphism stat cards ─────────────────────── */}
              <div className="relative h-[200px]">

                {/* Card 1 — employees */}
                <div className="kx-f1 absolute left-0 top-0 flex items-center gap-3.5 rounded-2xl border border-white/[0.14] bg-white/[0.07] px-4 py-3.5 shadow-2xl backdrop-blur-md">
                  <div className="relative flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-emerald-500/20 ring-1 ring-emerald-500/30">
                    <Users className="h-5 w-5 text-emerald-300" />
                    <span className="absolute -right-1 -top-1 h-2.5 w-2.5 animate-pulse rounded-full bg-emerald-400 ring-[3px] ring-[#07122e]" />
                  </div>
                  <div>
                    <p className="text-[11px] font-medium text-white/40 uppercase tracking-wide">Active today</p>
                    <p className="text-3xl font-black leading-none text-emerald-300">347</p>
                    <p className="text-[10px] text-white/25 mt-0.5">employees clocked in</p>
                  </div>
                </div>

                {/* Card 2 — attendance */}
                <div className="kx-f2 absolute right-6 top-4 flex items-center gap-3.5 rounded-2xl border border-white/[0.14] bg-white/[0.07] px-4 py-3.5 shadow-2xl backdrop-blur-md">
                  <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-cyan-500/20 ring-1 ring-cyan-500/30">
                    <TrendingUp className="h-5 w-5 text-cyan-300" />
                  </div>
                  <div>
                    <p className="text-[11px] font-medium text-white/40 uppercase tracking-wide">Attendance</p>
                    <p className="text-3xl font-black leading-none text-cyan-300">98.2%</p>
                    <p className="text-[10px] text-white/25 mt-0.5">monthly average</p>
                  </div>
                </div>

                {/* Card 3 — payroll */}
                <div className="kx-f3 absolute left-20 bottom-0 flex items-center gap-3.5 rounded-2xl border border-white/[0.14] bg-white/[0.07] px-4 py-3.5 shadow-2xl backdrop-blur-md">
                  <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-amber-500/20 ring-1 ring-amber-500/30">
                    <CheckCircle2 className="h-5 w-5 text-amber-300" />
                  </div>
                  <div>
                    <p className="text-[11px] font-medium text-white/40 uppercase tracking-wide">Payroll</p>
                    <p className="text-3xl font-black leading-none text-amber-300">SAR 2.4M</p>
                    <p className="text-[10px] text-white/25 mt-0.5">last cycle · on time</p>
                  </div>
                </div>

              </div>

              {/* Feature grid — glassmorphism */}
              <div className="grid grid-cols-2 gap-2.5">
                {FEATURES.map(({ icon: Icon, label, desc }) => (
                  <div key={label} className="flex items-start gap-2.5 rounded-xl border border-white/[0.09] bg-white/[0.04] p-3 backdrop-blur-sm transition-colors hover:bg-white/[0.07]">
                    <div className="mt-0.5 flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-blue-500/20">
                      <Icon className="h-3.5 w-3.5 text-blue-300" />
                    </div>
                    <div>
                      <p className="text-xs font-semibold text-white">{label}</p>
                      <p className="mt-0.5 text-[11px] leading-relaxed text-blue-200/45">{desc}</p>
                    </div>
                  </div>
                ))}
              </div>
            </div>

            {/* Footer */}
            <p className="text-[11px] text-blue-300/30">
              A{' '}
              <span className="font-semibold text-blue-300/50">Kode Kinetics</span>{' '}
              product · GCC-ready · Cloud-native
            </p>
          </div>
        </div>

        {/* ══ RIGHT PANEL ══════════════════════════════════════════════════════ */}
        <div className="flex flex-1 flex-col items-center justify-center bg-[#f1f5ff] px-6 py-12 dark:bg-[#030d24]">
          {/* Mobile logo */}
          <div className="mb-8 lg:hidden">
            <Logo />
          </div>

          <div className="w-full max-w-[400px]">
            <div className="mb-7">
              <h2 className="text-2xl font-extrabold tracking-tight text-slate-900 dark:text-white">
                Welcome back
              </h2>
              <p className="mt-1 text-sm text-slate-500 dark:text-slate-400">
                Sign in to your workspace to continue.
              </p>
            </div>

            {/* Glass form card */}
            <div className="rounded-2xl border border-slate-200/80 bg-white p-7 shadow-xl shadow-blue-900/[0.07] dark:border-white/[0.08] dark:bg-white/[0.04] dark:backdrop-blur-xl dark:shadow-none">
              <form onSubmit={handleSubmit} className="space-y-4">

                <div>
                  <label htmlFor="login-email" className="mb-1.5 flex items-center gap-1.5 text-sm font-medium text-slate-700 dark:text-slate-300">
                    Email address
                    <InfoTip text="The work email your company admin registered you with, e.g. you@company.com." />
                  </label>
                  <input
                    id="login-email" type="email" value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    className="input w-full" placeholder="you@company.com"
                    autoComplete="email" required
                  />
                </div>

                <div>
                  <label htmlFor="login-password" className="mb-1.5 flex items-center gap-1.5 text-sm font-medium text-slate-700 dark:text-slate-300">
                    Password
                    <InfoTip text="Your account password (case-sensitive). Contact your HR admin if you can't sign in." />
                  </label>
                  <div className="relative">
                    <input
                      id="login-password" type={showPassword ? 'text' : 'password'} value={password}
                      onChange={(e) => setPassword(e.target.value)}
                      className="input w-full pr-10" placeholder="••••••••"
                      autoComplete="current-password" required
                    />
                    <button
                      type="button" onClick={() => setShowPassword(v => !v)}
                      className="absolute right-3 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-600 dark:hover:text-slate-300"
                      aria-label={showPassword ? 'Hide password' : 'Show password'} tabIndex={-1}
                    >
                      {showPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                    </button>
                  </div>
                </div>

                <div>
                  <label htmlFor="login-workspace" className="mb-1.5 flex items-center gap-1.5 text-sm font-medium text-slate-700 dark:text-slate-300">
                    Workspace
                    <InfoTip text="Your company's unique workspace ID (lowercase, e.g. acme-industries). Shared by your admin when the workspace was created." />
                  </label>
                  <input
                    id="login-workspace" type="text" value={tenantSlug}
                    onChange={(e) => setTenantSlug(e.target.value)}
                    className="input w-full" placeholder="your-workspace"
                    autoComplete="organization" required
                  />
                  <p className="mt-1 text-xs text-slate-400 dark:text-slate-500">
                    Your company&apos;s unique workspace identifier
                  </p>
                </div>

                {error && (
                  <div className="flex items-start gap-2 rounded-xl border border-rose-200 bg-rose-50 px-3 py-2.5 dark:border-rose-500/20 dark:bg-rose-500/[0.08]">
                    <span className="mt-0.5 h-2 w-2 shrink-0 rounded-full bg-rose-500" />
                    <p className="text-sm text-rose-600 dark:text-rose-400">{error}</p>
                  </div>
                )}

                <button
                  type="submit" disabled={isLoading}
                  className="btn-primary mt-1 w-full justify-center py-2.5 disabled:cursor-not-allowed disabled:opacity-60"
                >
                  {isLoading ? (
                    <>
                      <span className="h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" />
                      Signing in…
                    </>
                  ) : 'Sign in'}
                </button>
              </form>
            </div>

            <p className="mt-6 text-center text-xs text-slate-400 dark:text-slate-600">
              One Platform for Every Workforce Operation
            </p>
            <p className="mt-1 text-center text-[11px] text-slate-400 dark:text-slate-600 lg:hidden">
              A <span className="font-semibold text-slate-500 dark:text-slate-400">Kode Kinetics</span> product
            </p>
          </div>
        </div>

      </div>
    </>
  );
}
