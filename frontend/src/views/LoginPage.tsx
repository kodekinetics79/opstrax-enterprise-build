'use client';

import { useEffect, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import {
  AlertCircle, BarChart3, CheckCircle2, Clock,
  Eye, EyeOff, FileCheck, KeyRound, Lock, Mail,
  ShieldCheck, TrendingUp, Users, Zap,
} from 'lucide-react';
import { useAuth } from '../contexts/AuthContext';
import { authApi } from '../api/auth';
import { Logo } from '../components/Logo';

// Mock data for the product preview card
const WEEK = [
  { d: 'Mon', v: 96 }, { d: 'Tue', v: 99 }, { d: 'Wed', v: 97 },
  { d: 'Thu', v: 98 }, { d: 'Fri', v: 94 },
];

const PILLS = ['Saudi Labour Law', 'UAE MoHRE', 'WPS Ready', 'Multi-branch', 'AI Insights', 'Cloud-native'];

type LoginMode = 'login' | 'forgot' | 'reset';

export function LoginPage() {
  const { login }    = useAuth();
  const router       = useRouter();
  const searchParams = useSearchParams();
  const from         = searchParams?.get('from') ?? '/dashboard';

  const [mode,         setMode]         = useState<LoginMode>('login');
  const [email,        setEmail]        = useState('');
  const [password,     setPassword]     = useState('');
  const [tenantSlug,   setTenantSlug]   = useState('');
  const [tenantLocked, setTenantLocked] = useState(false);
  const [error,        setError]        = useState('');
  const [info,         setInfo]         = useState('');
  const [isLoading,    setIsLoading]    = useState(false);
  const [showPw,       setShowPw]       = useState(false);
  const [forgotEmail,  setForgotEmail]  = useState('');
  const [resetToken,   setResetToken]   = useState('');
  const [newPw,        setNewPw]        = useState('');
  const [confirmPw,    setConfirmPw]    = useState('');

  useEffect(() => {
    const wsParam = searchParams?.get('workspace') ?? searchParams?.get('w');
    if (wsParam) { setTenantSlug(wsParam); setTenantLocked(true); return; }
    if (typeof window === 'undefined') return;
    const parts = window.location.hostname.split('.');
    const skip = new Set(['www', 'app', 'admin', 'mail', 'localhost']);
    if (parts.length >= 3 && !skip.has(parts[0])) setTenantSlug(parts[0]);
  }, [searchParams]);

  const handleLogin = async (e: React.FormEvent) => {
    e.preventDefault(); setError(''); setIsLoading(true);
    try { await login(email, password, tenantSlug); router.replace(from); }
    catch { setError('Invalid credentials. Check your email, password, and workspace.'); }
    finally { setIsLoading(false); }
  };

  const handleForgot = async (e: React.FormEvent) => {
    e.preventDefault(); setError(''); setIsLoading(true);
    try {
      const res = await authApi.forgotPassword(forgotEmail || email, tenantSlug || undefined);
      if (res.resetToken) { setResetToken(res.resetToken); setMode('reset'); }
      else setInfo(res.message ?? 'Check your email for a reset link.');
    } catch (err: any) { setError(err.response?.data?.message ?? 'Could not process request.'); }
    finally { setIsLoading(false); }
  };

  const handleReset = async (e: React.FormEvent) => {
    e.preventDefault(); setError('');
    if (newPw !== confirmPw) { setError('Passwords do not match.'); return; }
    if (newPw.length < 10)   { setError('Password must be at least 10 characters.'); return; }
    setIsLoading(true);
    try {
      await authApi.resetPassword(forgotEmail || email, resetToken, newPw, tenantSlug || undefined);
      setInfo('Password updated. You can now sign in.');
      setMode('login');
    } catch (err: any) { setError(err.response?.data?.message ?? 'Reset failed. Token may have expired.'); }
    finally { setIsLoading(false); }
  };

  const go = (m: LoginMode) => {
    setError(''); setInfo(''); setMode(m);
    if (m === 'forgot' && email) setForgotEmail(email);
  };

  return (
    <>
      <style>{`
        @keyframes kx-blob  { 0%,100%{transform:translate(0,0) scale(1)}   33%{transform:translate(18px,-12px) scale(1.04)}  66%{transform:translate(-10px,14px) scale(0.97)} }
        @keyframes kx-blob2 { 0%,100%{transform:translate(0,0) scale(1)}   40%{transform:translate(-14px,10px) scale(1.05)} 70%{transform:translate(12px,-8px) scale(0.96)} }
        @keyframes kx-lift  { 0%,100%{transform:translateY(0)}  50%{transform:translateY(-8px)} }
        @keyframes kx-bar   { from{opacity:0;transform:scaleY(0)} to{opacity:1;transform:scaleY(1)} }
        .kx-b1{animation:kx-blob  18s ease-in-out infinite}
        .kx-b2{animation:kx-blob2 22s ease-in-out infinite}
        .kx-lf{animation:kx-lift   6s ease-in-out infinite}
        .kx-bar{animation:kx-bar 0.7s ease-out both;transform-origin:bottom}
        .kx-dots{background-image:radial-gradient(circle,rgba(120,160,255,0.5) 1px,transparent 1px);background-size:32px 32px}
      `}</style>

      <div className="flex min-h-screen">

        {/* ══════════════ LEFT — product story panel ══════════════════════════ */}
        <div className="relative hidden overflow-hidden lg:flex lg:w-[56%]">

          {/* Base gradient */}
          <div className="absolute inset-0 bg-gradient-to-br from-[#020818] via-[#061428] to-[#030c20]" />

          {/* Dot grid */}
          <div className="kx-dots absolute inset-0 opacity-[0.028]" />

          {/* Atmospheric glow blobs — stay behind everything */}
          <div className="kx-b1 absolute -left-48 -top-48 h-[600px] w-[600px] rounded-full bg-blue-700/[0.18] blur-[120px]" />
          <div className="kx-b2 absolute -bottom-32 -right-32 h-[500px] w-[500px] rounded-full bg-indigo-700/[0.14] blur-[100px]" />
          <div className="absolute left-1/3 top-1/2 h-[300px] w-[300px] -translate-y-1/2 rounded-full bg-cyan-700/[0.09] blur-[80px]" />

          {/* Faint KYNEXONE watermark */}
          <div className="pointer-events-none absolute inset-0 flex items-end justify-end overflow-hidden select-none pb-6 pr-8">
            <p className="text-[88px] font-black tracking-tighter text-white opacity-[0.018] -rotate-6 whitespace-nowrap">KYNEXONE</p>
          </div>

          {/* ── Main content ─────────────────────────────────────────────────── */}
          <div className="relative z-10 flex w-full flex-col justify-between px-12 py-10">

            {/* Logo + tagline */}
            <div className="flex items-center gap-3">
              <Logo size="lg" />
              <div className="h-5 w-px bg-white/[0.12]" />
              <span className="text-xs font-semibold tracking-[0.2em] text-blue-300/50 uppercase">Workforce Intelligence</span>
            </div>

            {/* Headline */}
            <div className="mt-2">
              <p className="mb-3 inline-flex items-center gap-2 rounded-full border border-cyan-500/20 bg-cyan-500/[0.07] px-3 py-1 text-[11px] font-semibold tracking-widest text-cyan-400/80 uppercase">
                <span className="h-1.5 w-1.5 rounded-full bg-cyan-400 animate-pulse" />
                GCC Enterprise Platform
              </p>
              <h1 className="text-[52px] font-black leading-[1.04] tracking-tight">
                <span className="text-white">Everything your</span><br />
                <span className="bg-gradient-to-r from-cyan-300 via-blue-300 to-indigo-400 bg-clip-text text-transparent">workforce needs.</span>
              </h1>
              <p className="mt-3 max-w-[380px] text-[15px] leading-relaxed text-blue-200/40">
                HR, payroll, attendance &amp; compliance — unified for GCC enterprises. Saudi, UAE, Qatar labour law built in.
              </p>
            </div>

            {/* ── Dashboard preview card — the hero visual ─────────────────── */}
            <div className="kx-lf rounded-2xl border border-white/[0.10] bg-white/[0.05] p-5 shadow-[0_32px_80px_rgba(0,0,0,0.5)] backdrop-blur-2xl ring-1 ring-white/[0.04]">

              {/* Card header */}
              <div className="mb-4 flex items-center justify-between">
                <div>
                  <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-white/35">Live Workforce Overview</p>
                  <p className="mt-0.5 text-xs text-white/20">Monday, Jun 16 · KSA time zone</p>
                </div>
                <div className="flex items-center gap-2 rounded-full border border-emerald-500/25 bg-emerald-500/[0.08] px-2.5 py-1">
                  <span className="h-1.5 w-1.5 animate-pulse rounded-full bg-emerald-400" />
                  <span className="text-[11px] font-medium text-emerald-400">Live</span>
                </div>
              </div>

              {/* KPI row */}
              <div className="mb-4 grid grid-cols-3 gap-2.5">
                {[
                  { label: 'Active Employees', value: '347', sub: '↑ 12 today', color: 'text-emerald-300', bg: 'bg-emerald-500/[0.08]', border: 'border-emerald-500/15', icon: Users },
                  { label: 'Attendance Rate',  value: '98.2%', sub: 'monthly avg', color: 'text-cyan-300', bg: 'bg-cyan-500/[0.08]', border: 'border-cyan-500/15', icon: TrendingUp },
                  { label: 'Payroll Processed', value: '$2.4M', sub: 'on time', color: 'text-amber-300', bg: 'bg-amber-500/[0.08]', border: 'border-amber-500/15', icon: CheckCircle2 },
                ].map(({ label, value, sub, color, bg, border, icon: Icon }) => (
                  <div key={label} className={`rounded-xl border ${border} ${bg} p-3`}>
                    <Icon className={`mb-2 h-4 w-4 ${color} opacity-70`} />
                    <p className={`text-[22px] font-black leading-none ${color}`}>{value}</p>
                    <p className="mt-1 text-[10px] font-medium text-white/30 leading-tight">{label}</p>
                    <p className="mt-0.5 text-[10px] text-white/20">{sub}</p>
                  </div>
                ))}
              </div>

              {/* Attendance sparkline */}
              <div className="mb-4 rounded-xl border border-white/[0.06] bg-white/[0.03] p-3">
                <p className="mb-3 text-[10px] font-semibold uppercase tracking-widest text-white/25">Attendance — This Week</p>
                <div className="flex items-end gap-2 h-14">
                  {WEEK.map(({ d, v }, i) => (
                    <div key={d} className="flex flex-1 flex-col items-center gap-1.5">
                      <p className="text-[9px] font-semibold text-white/35">{v}%</p>
                      <div
                        className="kx-bar w-full rounded-t-sm bg-gradient-to-t from-cyan-500/40 to-cyan-400/70"
                        style={{ height: `${Math.round((v / 100) * 44)}px`, animationDelay: `${i * 80}ms` }}
                      />
                      <p className="text-[9px] text-white/20">{d}</p>
                    </div>
                  ))}
                </div>
              </div>

              {/* Status strip */}
              <div className="flex gap-2">
                {[
                  { icon: Clock,     text: '24 leave pending',  color: 'text-violet-400', bg: 'bg-violet-500/[0.08]', border: 'border-violet-500/15' },
                  { icon: FileCheck, text: 'WPS 100%',           color: 'text-indigo-400', bg: 'bg-indigo-500/[0.08]', border: 'border-indigo-500/15' },
                  { icon: Zap,       text: '18 new hires',       color: 'text-teal-400',   bg: 'bg-teal-500/[0.08]',   border: 'border-teal-500/15'   },
                  { icon: ShieldCheck, text: '3 AI alerts',      color: 'text-rose-400',   bg: 'bg-rose-500/[0.08]',   border: 'border-rose-500/15'   },
                ].map(({ icon: Icon, text, color, bg, border }) => (
                  <div key={text} className={`flex items-center gap-1.5 rounded-lg border ${border} ${bg} px-2 py-1.5`}>
                    <Icon className={`h-3 w-3 ${color}`} />
                    <span className="text-[10px] font-medium text-white/50 whitespace-nowrap">{text}</span>
                  </div>
                ))}
              </div>
            </div>

            {/* Compliance pill strip */}
            <div className="flex flex-wrap gap-1.5">
              {PILLS.map(p => (
                <span key={p} className="rounded-full border border-white/[0.07] bg-white/[0.03] px-3 py-1 text-[11px] font-medium text-white/30 hover:border-white/[0.12] hover:text-white/45 transition-colors">
                  {p}
                </span>
              ))}
            </div>

            {/* Footer */}
            <p className="text-[11px] text-white/20">
              A <span className="font-semibold text-white/30">Kode Kinetics</span> product · Trusted by 50+ GCC enterprises
            </p>

          </div>
        </div>

        {/* ══════════════ RIGHT — auth panel ══════════════════════════════════ */}
        <div className="flex flex-1 flex-col items-center justify-center bg-slate-50 px-8 py-12 dark:bg-[#040e22]">

          {/* Mobile logo */}
          <div className="mb-10 lg:hidden"><Logo /></div>

          <div className="w-full max-w-[420px]">

            {/* ── LOGIN ────────────────────────────────────────────────────── */}
            {mode === 'login' && (
              <form onSubmit={handleLogin} className="space-y-1">
                <h2 className="text-[28px] font-black tracking-tight text-slate-900 dark:text-white">Welcome back</h2>
                <p className="pb-6 text-sm text-slate-400 dark:text-slate-500">Sign in to your workspace to continue.</p>

                <Field label="Work email">
                  <input
                    id="f-email" type="email" value={email} onChange={e => setEmail(e.target.value)}
                    className="input w-full" placeholder="you@company.com"
                    autoComplete="email" required
                  />
                </Field>

                <Field label="Password" action={
                  <button type="button" onClick={() => go('forgot')}
                    className="text-[11px] text-blue-500 hover:text-blue-600 dark:text-blue-400">
                    Forgot password?
                  </button>
                }>
                  <div className="relative">
                    <input
                      id="f-pw" type={showPw ? 'text' : 'password'} value={password}
                      onChange={e => setPassword(e.target.value)}
                      className="input w-full pr-10" placeholder="••••••••••"
                      autoComplete="current-password" required
                    />
                    <button type="button" onClick={() => setShowPw(v => !v)}
                      className="absolute right-3 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-600 dark:hover:text-slate-300"
                      aria-label={showPw ? 'Hide' : 'Show'} tabIndex={-1}>
                      {showPw ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                    </button>
                  </div>
                </Field>

                <Field
                  label="Workspace"
                  hint={tenantLocked
                    ? <span className="flex items-center gap-1 text-emerald-600 dark:text-emerald-400"><Lock className="h-3 w-3" />Auto-detected</span>
                    : tenantSlug ? 'Pre-filled — you can edit' : "Your company's unique workspace ID"}
                >
                  <input
                    id="f-ws" type="text" value={tenantSlug} onChange={e => setTenantSlug(e.target.value)}
                    className="input w-full" placeholder="your-workspace"
                    autoComplete="organization" required
                  />
                </Field>

                <Feedback error={error} info={info} />

                <button type="submit" disabled={isLoading}
                  className="btn-primary mt-2 w-full justify-center py-3 text-[15px] font-bold disabled:cursor-not-allowed disabled:opacity-60">
                  {isLoading
                    ? <><span className="h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" /> Signing in…</>
                    : 'Sign in →'}
                </button>

                <p className="pt-4 text-center text-[11px] text-slate-400 dark:text-slate-600">
                  Secured with AES-256 · SOC 2 ready · GCC data residency
                </p>
              </form>
            )}

            {/* ── FORGOT ───────────────────────────────────────────────────── */}
            {mode === 'forgot' && (
              <form onSubmit={handleForgot} className="space-y-1">
                <button type="button" onClick={() => go('login')}
                  className="mb-5 flex items-center gap-1 text-sm text-slate-400 hover:text-slate-600 dark:hover:text-slate-300">
                  ← Back to sign in
                </button>
                <div className="mb-1 flex h-10 w-10 items-center justify-center rounded-xl bg-blue-500/10 ring-1 ring-blue-500/20">
                  <Mail className="h-5 w-5 text-blue-500" />
                </div>
                <h2 className="text-[28px] font-black tracking-tight text-slate-900 dark:text-white">Reset password</h2>
                <p className="pb-6 text-sm text-slate-400 dark:text-slate-500">Enter your email and we'll send a reset code.</p>

                <Field label="Work email">
                  <input id="fg-em" type="email" value={forgotEmail || email}
                    onChange={e => setForgotEmail(e.target.value)}
                    className="input w-full" placeholder="you@company.com" autoComplete="email" required />
                </Field>

                <Field label="Workspace" hint="Optional — helps locate your account faster">
                  <input id="fg-ws" type="text" value={tenantSlug}
                    onChange={e => setTenantSlug(e.target.value)}
                    className="input w-full" placeholder="your-workspace" />
                </Field>

                <Feedback error={error} info={info} />

                <button type="submit" disabled={isLoading}
                  className="btn-primary mt-2 w-full justify-center py-3 text-[15px] font-bold disabled:cursor-not-allowed disabled:opacity-60">
                  {isLoading ? <span className="h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" /> : 'Send reset code →'}
                </button>

                <p className="pt-3 text-center text-xs text-slate-400">
                  Already have a code?{' '}
                  <button type="button" onClick={() => go('reset')} className="text-blue-500 hover:text-blue-600 dark:text-blue-400">
                    Enter it here
                  </button>
                </p>
              </form>
            )}

            {/* ── RESET ────────────────────────────────────────────────────── */}
            {mode === 'reset' && (
              <form onSubmit={handleReset} className="space-y-1">
                <button type="button" onClick={() => go('forgot')}
                  className="mb-5 flex items-center gap-1 text-sm text-slate-400 hover:text-slate-600 dark:hover:text-slate-300">
                  ← Back
                </button>
                <div className="mb-1 flex h-10 w-10 items-center justify-center rounded-xl bg-blue-500/10 ring-1 ring-blue-500/20">
                  <KeyRound className="h-5 w-5 text-blue-500" />
                </div>
                <h2 className="text-[28px] font-black tracking-tight text-slate-900 dark:text-white">New password</h2>
                <p className="pb-6 text-sm text-slate-400 dark:text-slate-500">Enter the reset code from your email.</p>

                <Field label="Work email">
                  <input id="rs-em" type="email" value={forgotEmail || email}
                    onChange={e => setForgotEmail(e.target.value)}
                    className="input w-full" placeholder="you@company.com" autoComplete="email" required />
                </Field>

                <Field label="Reset code">
                  <input id="rs-tk" type="text" value={resetToken}
                    onChange={e => setResetToken(e.target.value)}
                    className="input w-full font-mono tracking-wider" placeholder="Paste code from email" required />
                </Field>

                <Field label="New password" hint="Minimum 10 characters">
                  <input id="rs-pw" type="password" value={newPw}
                    onChange={e => setNewPw(e.target.value)}
                    className="input w-full" placeholder="••••••••••" autoComplete="new-password" required />
                </Field>

                <Field label="Confirm password">
                  <input id="rs-cf" type="password" value={confirmPw}
                    onChange={e => setConfirmPw(e.target.value)}
                    className="input w-full" placeholder="••••••••••" autoComplete="new-password" required />
                </Field>

                <Feedback error={error} info={info} />

                <button type="submit" disabled={isLoading}
                  className="btn-primary mt-2 w-full justify-center py-3 text-[15px] font-bold disabled:cursor-not-allowed disabled:opacity-60">
                  {isLoading ? <span className="h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" /> : 'Update password →'}
                </button>
              </form>
            )}

            <p className="mt-8 text-center text-[11px] text-slate-300 dark:text-slate-700 lg:hidden">
              A <span className="font-semibold">Kode Kinetics</span> product
            </p>
          </div>
        </div>

      </div>
    </>
  );
}

// ── Tiny shared components (file-local) ───────────────────────────────────────

function Field({ label, hint, action, children }: {
  label: string; hint?: React.ReactNode; action?: React.ReactNode; children: React.ReactNode;
}) {
  return (
    <div className="pt-4">
      <div className="mb-1.5 flex items-baseline justify-between">
        <label className="text-sm font-semibold text-slate-700 dark:text-slate-300">{label}</label>
        {action && <span>{action}</span>}
        {!action && hint && <span className="text-[11px] text-slate-400 dark:text-slate-500">{hint}</span>}
      </div>
      {children}
    </div>
  );
}

function Feedback({ error, info }: { error: string; info: string }) {
  if (error) return (
    <div className="mt-4 flex items-start gap-2.5 rounded-xl border border-rose-200 bg-rose-50 px-3.5 py-3 dark:border-rose-500/20 dark:bg-rose-500/[0.07]">
      <AlertCircle className="mt-0.5 h-4 w-4 shrink-0 text-rose-500" />
      <p className="text-sm text-rose-700 dark:text-rose-400">{error}</p>
    </div>
  );
  if (info) return (
    <div className="mt-4 flex items-start gap-2.5 rounded-xl border border-emerald-200 bg-emerald-50 px-3.5 py-3 dark:border-emerald-500/20 dark:bg-emerald-500/[0.07]">
      <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-emerald-500" />
      <p className="text-sm text-emerald-700 dark:text-emerald-400">{info}</p>
    </div>
  );
  return null;
}
