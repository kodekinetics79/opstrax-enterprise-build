'use client';

import { useEffect, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import {
  AlertCircle, BarChart3, CheckCircle2, Clock, Eye, EyeOff,
  FileCheck, KeyRound, Lock, Mail, ShieldCheck, TrendingUp, Users, Zap,
} from 'lucide-react';
import { useAuth } from '../contexts/AuthContext';
import { authApi } from '../api/auth';
import { Logo } from '../components/Logo';
import { InfoTip } from '../components/InfoTip';

const FEATURES = [
  { icon: Users,      label: 'People & Payroll',   desc: 'Headcount, payslips, WPS compliance' },
  { icon: BarChart3,  label: 'Live Analytics',      desc: 'Real-time KPI trends and AI insights' },
  { icon: ShieldCheck,label: 'GCC Compliance',      desc: 'Saudi, UAE labour law built-in' },
  { icon: Zap,        label: 'Auto Workflows',      desc: 'Leave, overtime, approvals automated' },
];

type LoginMode = 'login' | 'forgot' | 'reset';

export function LoginPage() {
  const { login } = useAuth();
  const router       = useRouter();
  const searchParams = useSearchParams();
  const from = searchParams?.get('from') ?? '/dashboard';

  const [mode,          setMode]          = useState<LoginMode>('login');
  const [email,         setEmail]         = useState('');
  const [password,      setPassword]      = useState('');
  const [tenantSlug,    setTenantSlug]    = useState('');
  const [tenantLocked,  setTenantLocked]  = useState(false);
  const [error,         setError]         = useState('');
  const [info,          setInfo]          = useState('');
  const [isLoading,     setIsLoading]     = useState(false);
  const [showPassword,  setShowPassword]  = useState(false);

  // Forgot / reset password fields
  const [forgotEmail,   setForgotEmail]   = useState('');
  const [resetToken,    setResetToken]    = useState('');
  const [newPassword,   setNewPassword]   = useState('');
  const [confirmPass,   setConfirmPass]   = useState('');

  // Auto-detect tenant slug from subdomain or ?workspace= query param (pre-fills but stays editable)
  useEffect(() => {
    const wsParam = searchParams?.get('workspace') ?? searchParams?.get('w');
    if (wsParam) { setTenantSlug(wsParam); setTenantLocked(true); return; }
    if (typeof window === 'undefined') return;
    const parts = window.location.hostname.split('.');
    const skip = new Set(['www', 'app', 'admin', 'mail', 'localhost']);
    if (parts.length >= 3 && !skip.has(parts[0])) {
      setTenantSlug(parts[0]);
      setTenantLocked(false); // pre-fill only, not locked
    }
  }, [searchParams]);

  const handleLogin = async (e: React.FormEvent) => {
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

  const handleForgot = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setIsLoading(true);
    try {
      const res = await authApi.forgotPassword(forgotEmail || email, tenantSlug || undefined);
      setInfo(res.message ?? 'Check your email for a reset link.');
      if (res.resetToken) {
        setResetToken(res.resetToken);
        setMode('reset');
        setInfo('');
      }
    } catch (err: any) {
      setError(err.response?.data?.message ?? 'Could not process reset request.');
    } finally {
      setIsLoading(false);
    }
  };

  const handleReset = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    if (newPassword !== confirmPass) { setError('Passwords do not match.'); return; }
    if (newPassword.length < 10) { setError('Password must be at least 10 characters.'); return; }
    setIsLoading(true);
    try {
      await authApi.resetPassword(forgotEmail || email, resetToken, newPassword, tenantSlug || undefined);
      setInfo('Password updated. You can now sign in.');
      setMode('login');
      setError('');
    } catch (err: any) {
      setError(err.response?.data?.message ?? 'Reset failed. The token may have expired.');
    } finally {
      setIsLoading(false);
    }
  };

  const switchMode = (m: LoginMode) => {
    setError('');
    setInfo('');
    setMode(m);
    if (m === 'forgot' && email) setForgotEmail(email);
  };

  return (
    <>
      <style>{`
        @keyframes kx-float  { 0%,100%{transform:translateY(0)}   50%{transform:translateY(-14px)} }
        @keyframes kx-float2 { 0%,100%{transform:translateY(0)}   50%{transform:translateY(-20px)} }
        @keyframes kx-float3 { 0%,100%{transform:translateY(0)}   50%{transform:translateY(-10px)} }
        @keyframes kx-float4 { 0%,100%{transform:translateY(0)}   50%{transform:translateY(-17px)} }
        @keyframes kx-float5 { 0%,100%{transform:translateY(0)}   50%{transform:translateY(-12px)} }
        @keyframes kx-float6 { 0%,100%{transform:translateY(0)}   50%{transform:translateY(-19px)} }
        @keyframes kx-float7 { 0%,100%{transform:translateY(0)}   50%{transform:translateY(-11px)} }
        @keyframes kx-float8 { 0%,100%{transform:translateY(0)}   50%{transform:translateY(-16px)} }
        @keyframes kx-spin-l { from{transform:rotate(0deg)}   to{transform:rotate(360deg)}  }
        @keyframes kx-spin-r { from{transform:rotate(0deg)}   to{transform:rotate(-360deg)} }
        @keyframes kx-ring   { 0%{transform:scale(1);opacity:0.35} 100%{transform:scale(2.4);opacity:0} }
        @keyframes kx-drift  { 0%,100%{transform:translate(0,0)} 40%{transform:translate(10px,-8px)} 70%{transform:translate(-8px,10px)} }
        @keyframes kx-glow   { 0%,100%{opacity:0.12} 50%{opacity:0.22} }
        .kx-f1{animation:kx-float  5.5s ease-in-out infinite}
        .kx-f2{animation:kx-float2 7.2s ease-in-out infinite 1.8s}
        .kx-f3{animation:kx-float3 6.4s ease-in-out infinite 3.6s}
        .kx-f4{animation:kx-float4 8.1s ease-in-out infinite 0.9s}
        .kx-f5{animation:kx-float5 5.9s ease-in-out infinite 2.7s}
        .kx-f6{animation:kx-float6 9.3s ease-in-out infinite 1.4s}
        .kx-f7{animation:kx-float7 6.7s ease-in-out infinite 3.2s}
        .kx-f8{animation:kx-float8 7.8s ease-in-out infinite 0.5s}
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

          {/* Dot-grid watermark */}
          <div className="kx-dots absolute inset-0 opacity-[0.045]" />

          {/* Drifting colour blobs */}
          <div className="kx-dr absolute -left-32 -top-32 h-[520px] w-[520px] rounded-full bg-blue-600/[0.14] blur-[100px]" />
          <div className="kx-dr2 absolute -right-24 top-1/4 h-[420px] w-[420px] rounded-full bg-indigo-600/[0.12] blur-[90px]" />
          <div className="kx-dr3 absolute bottom-0 left-1/3 h-[380px] w-[380px] rounded-full bg-cyan-600/[0.10] blur-[80px]" />

          {/* Rotating concentric rings */}
          <div className="kx-sl absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 h-[700px] w-[700px] rounded-full border border-blue-500/[0.05]" />
          <div className="kx-sr absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 h-[560px] w-[560px] rounded-full border border-indigo-400/[0.06]" />
          <div className="kx-sl absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 h-[420px] w-[420px] rounded-full border border-cyan-400/[0.07]" />
          <div className="kx-sr absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 h-[280px] w-[280px] rounded-full border border-blue-300/[0.08]" />

          {/* KYNEXONE text watermark */}
          <div className="pointer-events-none absolute inset-0 flex items-center justify-center overflow-hidden select-none">
            <p className="text-[160px] font-black text-white opacity-[0.022] -rotate-[10deg] tracking-tighter whitespace-nowrap">KYNEXONE</p>
          </div>

          {/* ── Floating cards — scattered across full panel ───────────── */}
          <div className="pointer-events-none absolute inset-0 z-[5] overflow-hidden">

            {/* Card 1 — Active employees (top-left) */}
            <div className="kx-f1 absolute left-[4%] top-[12%] flex items-center gap-4 rounded-2xl border border-white/[0.16] bg-white/[0.09] px-5 py-4 shadow-2xl backdrop-blur-md">
              <div className="relative flex h-14 w-14 shrink-0 items-center justify-center rounded-2xl bg-emerald-500/20 ring-1 ring-emerald-500/30">
                <Users className="h-7 w-7 text-emerald-300" />
                <span className="absolute -right-1 -top-1 h-3 w-3 animate-pulse rounded-full bg-emerald-400 ring-[3px] ring-[#07122e]" />
              </div>
              <div>
                <p className="text-[11px] font-semibold uppercase tracking-widest text-white/40">Active Today</p>
                <p className="mt-0.5 text-4xl font-black leading-none text-emerald-300">347</p>
                <p className="mt-1 text-[11px] text-white/25">employees clocked in</p>
              </div>
            </div>

            {/* Card 2 — Attendance rate (top-right) */}
            <div className="kx-f2 absolute right-[5%] top-[8%] flex items-center gap-4 rounded-2xl border border-white/[0.16] bg-white/[0.09] px-5 py-4 shadow-2xl backdrop-blur-md">
              <div className="flex h-14 w-14 shrink-0 items-center justify-center rounded-2xl bg-cyan-500/20 ring-1 ring-cyan-500/30">
                <TrendingUp className="h-7 w-7 text-cyan-300" />
              </div>
              <div>
                <p className="text-[11px] font-semibold uppercase tracking-widest text-white/40">Attendance</p>
                <p className="mt-0.5 text-4xl font-black leading-none text-cyan-300">98.2%</p>
                <p className="mt-1 text-[11px] text-white/25">monthly average</p>
              </div>
            </div>

            {/* Card 3 — Payroll (mid-left) */}
            <div className="kx-f3 absolute left-[3%] top-[38%] flex items-center gap-4 rounded-2xl border border-white/[0.16] bg-white/[0.09] px-5 py-4 shadow-2xl backdrop-blur-md">
              <div className="flex h-14 w-14 shrink-0 items-center justify-center rounded-2xl bg-amber-500/20 ring-1 ring-amber-500/30">
                <CheckCircle2 className="h-7 w-7 text-amber-300" />
              </div>
              <div>
                <p className="text-[11px] font-semibold uppercase tracking-widest text-white/40">Payroll</p>
                <p className="mt-0.5 text-4xl font-black leading-none text-amber-300">$2.4M</p>
                <p className="mt-1 text-[11px] text-white/25">last cycle · on time</p>
              </div>
            </div>

            {/* Card 4 — Leave pending (mid-right) */}
            <div className="kx-f4 absolute right-[4%] top-[36%] flex items-center gap-4 rounded-2xl border border-white/[0.16] bg-white/[0.09] px-5 py-4 shadow-2xl backdrop-blur-md">
              <div className="flex h-14 w-14 shrink-0 items-center justify-center rounded-2xl bg-violet-500/20 ring-1 ring-violet-500/30">
                <Clock className="h-7 w-7 text-violet-300" />
              </div>
              <div>
                <p className="text-[11px] font-semibold uppercase tracking-widest text-white/40">Leave Pending</p>
                <p className="mt-0.5 text-4xl font-black leading-none text-violet-300">24</p>
                <p className="mt-1 text-[11px] text-white/25">awaiting approval</p>
              </div>
            </div>

            {/* Card 5 — WPS Compliance (lower-left) */}
            <div className="kx-f5 absolute left-[6%] top-[62%] flex items-center gap-4 rounded-2xl border border-white/[0.16] bg-white/[0.09] px-5 py-4 shadow-2xl backdrop-blur-md">
              <div className="flex h-14 w-14 shrink-0 items-center justify-center rounded-2xl bg-indigo-500/20 ring-1 ring-indigo-500/30">
                <FileCheck className="h-7 w-7 text-indigo-300" />
              </div>
              <div>
                <p className="text-[11px] font-semibold uppercase tracking-widest text-white/40">WPS Compliance</p>
                <p className="mt-0.5 text-4xl font-black leading-none text-indigo-300">100%</p>
                <p className="mt-1 text-[11px] text-white/25">all records submitted</p>
              </div>
            </div>

            {/* Card 6 — Overtime (lower-right) */}
            <div className="kx-f6 absolute right-[3%] top-[60%] flex items-center gap-4 rounded-2xl border border-white/[0.16] bg-white/[0.09] px-5 py-4 shadow-2xl backdrop-blur-md">
              <div className="flex h-14 w-14 shrink-0 items-center justify-center rounded-2xl bg-orange-500/20 ring-1 ring-orange-500/30">
                <BarChart3 className="h-7 w-7 text-orange-300" />
              </div>
              <div>
                <p className="text-[11px] font-semibold uppercase tracking-widest text-white/40">Overtime hrs</p>
                <p className="mt-0.5 text-4xl font-black leading-none text-orange-300">1,280</p>
                <p className="mt-1 text-[11px] text-white/25">this month</p>
              </div>
            </div>

            {/* Card 7 — New Hires (bottom-left) */}
            <div className="kx-f7 absolute left-[8%] top-[80%] flex items-center gap-4 rounded-2xl border border-white/[0.16] bg-white/[0.09] px-5 py-4 shadow-2xl backdrop-blur-md">
              <div className="flex h-14 w-14 shrink-0 items-center justify-center rounded-2xl bg-teal-500/20 ring-1 ring-teal-500/30">
                <Zap className="h-7 w-7 text-teal-300" />
              </div>
              <div>
                <p className="text-[11px] font-semibold uppercase tracking-widest text-white/40">New Hires</p>
                <p className="mt-0.5 text-4xl font-black leading-none text-teal-300">18</p>
                <p className="mt-1 text-[11px] text-white/25">onboarded this month</p>
              </div>
            </div>

            {/* Card 8 — AI Alerts (bottom-right) */}
            <div className="kx-f8 absolute right-[6%] top-[78%] flex items-center gap-4 rounded-2xl border border-white/[0.16] bg-white/[0.09] px-5 py-4 shadow-2xl backdrop-blur-md">
              <div className="flex h-14 w-14 shrink-0 items-center justify-center rounded-2xl bg-rose-500/20 ring-1 ring-rose-500/30">
                <ShieldCheck className="h-7 w-7 text-rose-300" />
              </div>
              <div>
                <p className="text-[11px] font-semibold uppercase tracking-widest text-white/40">AI Alerts</p>
                <p className="mt-0.5 text-4xl font-black leading-none text-rose-300">3</p>
                <p className="mt-1 text-[11px] text-white/25">anomalies flagged</p>
              </div>
            </div>

          </div>

          {/* Content */}
          <div className="relative z-10 flex h-full flex-col justify-between px-14 py-14">

            {/* Radar pulse + Logo */}
            <div className="flex items-center gap-4">
              <div className="relative">
                <div className="kx-r1 absolute inset-0 rounded-full border-2 border-blue-400/40" />
                <div className="kx-r2 absolute inset-0 rounded-full border-2 border-blue-400/25" />
                <div className="kx-r3 absolute inset-0 rounded-full border-2 border-blue-400/15" />
                <div className="relative flex h-10 w-10 items-center justify-center rounded-full bg-blue-500/20 backdrop-blur-sm ring-1 ring-blue-400/30">
                  <span className="h-2.5 w-2.5 rounded-full bg-blue-400 shadow-lg shadow-blue-400/60" />
                </div>
              </div>
              <div className="relative">
                <div className="absolute -inset-6 rounded-full bg-blue-400/[0.06] blur-2xl" />
                <Logo size="lg" />
              </div>
            </div>

            {/* Headline */}
            <div>
              <h1 className="text-5xl font-extrabold leading-tight tracking-tight">
                <span className="bg-gradient-to-r from-cyan-300 via-blue-300 to-indigo-400 bg-clip-text text-transparent">
                  The Complete
                </span>
                <br />
                <span className="text-white">Workforce Platform</span>
              </h1>
              <p className="mt-4 max-w-md text-base leading-relaxed text-blue-200/50">
                From hire to retire — payroll, attendance, compliance, and AI insights in a single unified workspace built for GCC enterprises.
              </p>
            </div>

            {/* Feature grid — glassmorphism */}
            <div className="grid grid-cols-2 gap-2.5">
              {FEATURES.map(({ icon: Icon, label, desc }) => (
                <div key={label} className="flex items-start gap-2.5 rounded-xl border border-white/[0.07] bg-white/[0.04] p-3 transition hover:bg-white/[0.07]">
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

            {/* ── Login form ──────────────────────────────────────────────── */}
            {mode === 'login' && (
              <>
                <div className="mb-7">
                  <h2 className="text-2xl font-extrabold tracking-tight text-slate-900 dark:text-white">
                    Welcome back
                  </h2>
                  <p className="mt-1 text-sm text-slate-500 dark:text-slate-400">
                    Sign in to your workspace to continue.
                  </p>
                </div>

                <div className="rounded-2xl border border-slate-200/80 bg-white p-7 shadow-xl shadow-blue-900/[0.07] dark:border-white/[0.08] dark:bg-white/[0.04] dark:backdrop-blur-xl dark:shadow-none">
                  <form onSubmit={handleLogin} className="space-y-4">

                    <div>
                      <label htmlFor="login-email" className="mb-1.5 flex items-center gap-1.5 text-sm font-medium text-slate-700 dark:text-slate-300">
                        Email address
                        <InfoTip text="The work email your company admin registered you with." />
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
                        <InfoTip text="Your account password (case-sensitive)." />
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
                      <div className="mt-1.5 flex justify-end">
                        <button type="button" onClick={() => switchMode('forgot')} className="text-xs text-blue-500 hover:text-blue-600 dark:text-blue-400 dark:hover:text-blue-300">
                          Forgot password?
                        </button>
                      </div>
                    </div>

                    <div>
                      <label htmlFor="login-workspace" className="mb-1.5 flex items-center gap-1.5 text-sm font-medium text-slate-700 dark:text-slate-300">
                        Workspace
                        {tenantLocked && <span className="ml-auto flex items-center gap-1 text-[10px] font-medium text-emerald-600 dark:text-emerald-400"><Lock className="h-3 w-3" />Auto-detected</span>}
                        {!tenantLocked && tenantSlug && <span className="ml-auto text-[10px] text-slate-400">Pre-filled · you can edit</span>}
                        {!tenantLocked && !tenantSlug && <InfoTip text="Your company's unique workspace ID (lowercase, e.g. acme-industries)." />}
                      </label>
                      <input
                        id="login-workspace" type="text" value={tenantSlug}
                        onChange={(e) => setTenantSlug(e.target.value)}
                        className="input w-full"
                        placeholder="your-workspace"
                        autoComplete="organization" required
                      />
                      {!tenantLocked && (
                        <p className="mt-1 text-xs text-slate-400 dark:text-slate-500">
                          Your company&apos;s unique workspace identifier
                        </p>
                      )}
                    </div>

                    {info && (
                      <div className="flex items-start gap-2 rounded-xl border border-emerald-200 bg-emerald-50 px-3 py-2.5 dark:border-emerald-500/20 dark:bg-emerald-500/[0.08]">
                        <span className="mt-0.5 h-2 w-2 shrink-0 rounded-full bg-emerald-500" />
                        <p className="text-sm text-emerald-700 dark:text-emerald-400">{info}</p>
                      </div>
                    )}

                    {error && (
                      <div className="flex items-start gap-2 rounded-xl border border-rose-200 bg-rose-50 px-3 py-2.5 dark:border-rose-500/20 dark:bg-rose-500/[0.08]">
                        <AlertCircle className="mt-0.5 h-4 w-4 shrink-0 text-rose-500" />
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
              </>
            )}

            {/* ── Forgot password ──────────────────────────────────────────── */}
            {mode === 'forgot' && (
              <>
                <div className="mb-7">
                  <button type="button" onClick={() => switchMode('login')} className="mb-4 flex items-center gap-1.5 text-sm text-slate-500 hover:text-slate-700 dark:text-slate-400 dark:hover:text-slate-200">
                    ← Back to sign in
                  </button>
                  <h2 className="text-2xl font-extrabold tracking-tight text-slate-900 dark:text-white flex items-center gap-2">
                    <Mail className="h-6 w-6 text-blue-500" /> Reset password
                  </h2>
                  <p className="mt-1 text-sm text-slate-500 dark:text-slate-400">
                    Enter your email and workspace to receive a reset link.
                  </p>
                </div>

                <div className="rounded-2xl border border-slate-200/80 bg-white p-7 shadow-xl shadow-blue-900/[0.07] dark:border-white/[0.08] dark:bg-white/[0.04] dark:backdrop-blur-xl dark:shadow-none">
                  <form onSubmit={handleForgot} className="space-y-4">

                    <div>
                      <label htmlFor="forgot-email" className="mb-1.5 block text-sm font-medium text-slate-700 dark:text-slate-300">Email address</label>
                      <input
                        id="forgot-email" type="email" value={forgotEmail || email}
                        onChange={(e) => setForgotEmail(e.target.value)}
                        className="input w-full" placeholder="you@company.com" autoComplete="email" required
                      />
                    </div>

                    <div>
                      <label htmlFor="forgot-workspace" className="mb-1.5 flex items-center gap-1.5 text-sm font-medium text-slate-700 dark:text-slate-300">
                        Workspace
                        {tenantLocked && <span className="ml-auto flex items-center gap-1 text-[10px] font-medium text-emerald-600 dark:text-emerald-400"><Lock className="h-3 w-3" />Auto-detected</span>}
                      </label>
                      <input
                        id="forgot-workspace" type="text" value={tenantSlug}
                        onChange={(e) => setTenantSlug(e.target.value)}
                        className="input w-full"
                        placeholder="your-workspace"
                      />
                    </div>

                    {error && (
                      <div className="flex items-start gap-2 rounded-xl border border-rose-200 bg-rose-50 px-3 py-2.5 dark:border-rose-500/20 dark:bg-rose-500/[0.08]">
                        <AlertCircle className="mt-0.5 h-4 w-4 shrink-0 text-rose-500" />
                        <p className="text-sm text-rose-600 dark:text-rose-400">{error}</p>
                      </div>
                    )}

                    {info && (
                      <div className="flex items-start gap-2 rounded-xl border border-emerald-200 bg-emerald-50 px-3 py-2.5 dark:border-emerald-500/20 dark:bg-emerald-500/[0.08]">
                        <span className="mt-0.5 h-2 w-2 shrink-0 rounded-full bg-emerald-500" />
                        <p className="text-sm text-emerald-700 dark:text-emerald-400">{info}</p>
                      </div>
                    )}

                    <button type="submit" disabled={isLoading} className="btn-primary w-full justify-center py-2.5 disabled:cursor-not-allowed disabled:opacity-60">
                      {isLoading ? <span className="h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" /> : 'Send reset link'}
                    </button>

                    <p className="text-center text-xs text-slate-400">
                      Already have a reset code?{' '}
                      <button type="button" onClick={() => switchMode('reset')} className="text-blue-500 hover:text-blue-600 dark:text-blue-400">
                        Enter it here
                      </button>
                    </p>
                  </form>
                </div>
              </>
            )}

            {/* ── Reset password ───────────────────────────────────────────── */}
            {mode === 'reset' && (
              <>
                <div className="mb-7">
                  <button type="button" onClick={() => switchMode('forgot')} className="mb-4 flex items-center gap-1.5 text-sm text-slate-500 hover:text-slate-700 dark:text-slate-400 dark:hover:text-slate-200">
                    ← Back
                  </button>
                  <h2 className="text-2xl font-extrabold tracking-tight text-slate-900 dark:text-white flex items-center gap-2">
                    <KeyRound className="h-6 w-6 text-blue-500" /> Set new password
                  </h2>
                  <p className="mt-1 text-sm text-slate-500 dark:text-slate-400">
                    Enter the reset code from your email and choose a new password.
                  </p>
                </div>

                <div className="rounded-2xl border border-slate-200/80 bg-white p-7 shadow-xl shadow-blue-900/[0.07] dark:border-white/[0.08] dark:bg-white/[0.04] dark:backdrop-blur-xl dark:shadow-none">
                  <form onSubmit={handleReset} className="space-y-4">

                    <div>
                      <label htmlFor="reset-email" className="mb-1.5 block text-sm font-medium text-slate-700 dark:text-slate-300">Email address</label>
                      <input
                        id="reset-email" type="email" value={forgotEmail || email}
                        onChange={(e) => setForgotEmail(e.target.value)}
                        className="input w-full" placeholder="you@company.com" autoComplete="email" required
                      />
                    </div>

                    <div>
                      <label htmlFor="reset-token" className="mb-1.5 block text-sm font-medium text-slate-700 dark:text-slate-300">Reset code</label>
                      <input
                        id="reset-token" type="text" value={resetToken}
                        onChange={(e) => setResetToken(e.target.value)}
                        className="input w-full font-mono tracking-wider" placeholder="Paste reset code here" required
                      />
                    </div>

                    <div>
                      <label htmlFor="reset-newpw" className="mb-1.5 block text-sm font-medium text-slate-700 dark:text-slate-300">New password <span className="text-slate-400 font-normal">(min 10 chars)</span></label>
                      <input
                        id="reset-newpw" type="password" value={newPassword}
                        onChange={(e) => setNewPassword(e.target.value)}
                        className="input w-full" placeholder="••••••••••" autoComplete="new-password" required
                      />
                    </div>

                    <div>
                      <label htmlFor="reset-confirm" className="mb-1.5 block text-sm font-medium text-slate-700 dark:text-slate-300">Confirm password</label>
                      <input
                        id="reset-confirm" type="password" value={confirmPass}
                        onChange={(e) => setConfirmPass(e.target.value)}
                        className="input w-full" placeholder="••••••••••" autoComplete="new-password" required
                      />
                    </div>

                    {error && (
                      <div className="flex items-start gap-2 rounded-xl border border-rose-200 bg-rose-50 px-3 py-2.5 dark:border-rose-500/20 dark:bg-rose-500/[0.08]">
                        <AlertCircle className="mt-0.5 h-4 w-4 shrink-0 text-rose-500" />
                        <p className="text-sm text-rose-600 dark:text-rose-400">{error}</p>
                      </div>
                    )}

                    <button type="submit" disabled={isLoading} className="btn-primary w-full justify-center py-2.5 disabled:cursor-not-allowed disabled:opacity-60">
                      {isLoading ? <span className="h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" /> : 'Update password'}
                    </button>
                  </form>
                </div>
              </>
            )}

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
