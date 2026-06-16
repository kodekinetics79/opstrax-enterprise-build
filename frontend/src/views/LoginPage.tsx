'use client';

import { useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Eye, EyeOff, Users, BarChart3, ShieldCheck, Zap } from 'lucide-react';
import { useAuth } from '../contexts/AuthContext';
import { Logo } from '../components/Logo';
import { InfoTip } from '../components/InfoTip';

const FEATURES = [
  { icon: Users,       label: 'People & Payroll',   desc: 'Manage headcount, payslips, and WPS compliance' },
  { icon: BarChart3,   label: 'Live Analytics',      desc: 'Real-time attendance, KPI trends, and AI insights' },
  { icon: ShieldCheck, label: 'Compliance Ready',    desc: 'Saudi, UAE, and GCC labour law built-in' },
  { icon: Zap,         label: 'Automated Workflows', desc: 'Leave, overtime, recruitment, and approvals' },
];

export function LoginPage() {
  const { login } = useAuth();
  const router = useRouter();
  const searchParams = useSearchParams();
  const from = searchParams?.get('from') ?? '/dashboard';

  const [email, setEmail]           = useState('');
  const [password, setPassword]     = useState('');
  const [tenantSlug, setTenantSlug] = useState('');
  const [error, setError]           = useState('');
  const [isLoading, setIsLoading]   = useState(false);
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
    <div className="flex min-h-screen bg-lightBg dark:bg-midnight">

      {/* ── Left panel (desktop only) ───────────────────────────────────────── */}
      <div className="relative hidden lg:flex lg:w-[52%] xl:w-[55%] flex-col justify-between overflow-hidden bg-gradient-to-br from-[#0f1e3d] via-[#112155] to-[#0a1628] p-12">
        {/* Decorative blobs */}
        <div className="pointer-events-none absolute -left-16 -top-16 h-72 w-72 rounded-full bg-blue-500/[0.12] blur-3xl" />
        <div className="pointer-events-none absolute bottom-20 right-0 h-96 w-80 translate-x-20 rounded-full bg-indigo-600/[0.10] blur-3xl" />
        <div className="pointer-events-none absolute left-1/2 top-1/2 h-48 w-48 -translate-x-1/2 -translate-y-1/2 rounded-full bg-cyan-400/[0.06] blur-2xl" />

        {/* Logo */}
        <div className="relative z-10">
          <Logo collapsed={false} />
        </div>

        {/* Headline */}
        <div className="relative z-10 space-y-6">
          <div>
            <p className="text-[11px] font-bold uppercase tracking-[0.18em] text-blue-400">AI-Powered Workforce Platform</p>
            <h2 className="mt-2 text-3xl font-extrabold leading-snug tracking-tight text-white xl:text-4xl">
              One platform for<br />every workforce operation.
            </h2>
            <p className="mt-3 text-sm leading-relaxed text-blue-200/70">
              From hire to retire — payroll, attendance, compliance, and AI insights in a single unified workspace.
            </p>
          </div>

          <div className="grid grid-cols-2 gap-3">
            {FEATURES.map(({ icon: Icon, label, desc }) => (
              <div key={label} className="rounded-xl border border-white/[0.08] bg-white/[0.04] p-3.5 backdrop-blur-sm">
                <div className="mb-2 flex h-7 w-7 items-center justify-center rounded-lg bg-blue-500/20">
                  <Icon className="h-3.5 w-3.5 text-blue-300" />
                </div>
                <p className="text-xs font-semibold text-white">{label}</p>
                <p className="mt-0.5 text-[11px] leading-relaxed text-blue-200/60">{desc}</p>
              </div>
            ))}
          </div>
        </div>

        {/* Footer credit */}
        <div className="relative z-10">
          <p className="text-[11px] text-blue-300/40">
            A <span className="font-semibold text-blue-300/70">Kode Kinetics</span> product
          </p>
        </div>
      </div>

      {/* ── Right panel — form ──────────────────────────────────────────────── */}
      <div className="flex flex-1 flex-col items-center justify-center px-6 py-12">
        {/* Mobile logo */}
        <div className="mb-8 lg:hidden">
          <Logo />
        </div>

        <div className="w-full max-w-[380px]">
          <div className="mb-7">
            <h1 className="text-2xl font-extrabold tracking-tight text-slate-900 dark:text-white">Welcome back</h1>
            <p className="mt-1 text-sm text-slate-500 dark:text-slate-400">Sign in to your workspace to continue.</p>
          </div>

          <div className="rounded-2xl border border-slate-200/80 bg-white p-7 shadow-sm dark:border-white/[0.08] dark:bg-white/[0.04]">
            <form onSubmit={handleSubmit} className="space-y-4">
              <div>
                <label htmlFor="login-email" className="mb-1.5 flex items-center gap-1.5 text-sm font-medium text-slate-700 dark:text-slate-300">
                  Email address
                  <InfoTip text="The work email your company admin registered you with, e.g. you@company.com." />
                </label>
                <input
                  id="login-email"
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  className="input w-full"
                  placeholder="you@company.com"
                  autoComplete="email"
                  required
                />
              </div>

              <div>
                <label htmlFor="login-password" className="mb-1.5 flex items-center gap-1.5 text-sm font-medium text-slate-700 dark:text-slate-300">
                  Password
                  <InfoTip text="Your account password (case-sensitive). Contact your HR admin if you can't sign in." />
                </label>
                <div className="relative">
                  <input
                    id="login-password"
                    type={showPassword ? 'text' : 'password'}
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    className="input w-full pr-10"
                    placeholder="••••••••"
                    autoComplete="current-password"
                    required
                  />
                  <button
                    type="button"
                    onClick={() => setShowPassword(v => !v)}
                    className="absolute right-3 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-600 dark:hover:text-slate-300"
                    aria-label={showPassword ? 'Hide password' : 'Show password'}
                    tabIndex={-1}
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
                  id="login-workspace"
                  type="text"
                  value={tenantSlug}
                  onChange={(e) => setTenantSlug(e.target.value)}
                  className="input w-full"
                  placeholder="your-workspace"
                  autoComplete="organization"
                  required
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
                type="submit"
                disabled={isLoading}
                className="btn-primary mt-1 w-full justify-center py-2.5 disabled:cursor-not-allowed disabled:opacity-60"
              >
                {isLoading ? (
                  <>
                    <span className="h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" />
                    Signing in…
                  </>
                ) : (
                  'Sign in'
                )}
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
  );
}
