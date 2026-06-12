'use client';

import { useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Eye, EyeOff } from 'lucide-react';
import { useAuth } from '../contexts/AuthContext';
import { Logo } from '../components/Logo';
import { InfoTip } from '../components/InfoTip';

export function LoginPage() {
  const { login } = useAuth();
  const router = useRouter();
  const searchParams = useSearchParams();
  const from = searchParams?.get('from') ?? '/dashboard';

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [tenantSlug, setTenantSlug] = useState('');
  const [error, setError] = useState('');
  const [isLoading, setIsLoading] = useState(false);
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
    <div className="flex min-h-screen items-center justify-center bg-lightBg px-4 dark:bg-midnight">
      <div className="w-full max-w-sm">
        <div className="mb-8 flex justify-center">
          <Logo />
        </div>

        <div className="surface p-8">
          <h1 className="mb-1 text-2xl font-extrabold text-slate-950 dark:text-white">Sign in</h1>
          <p className="mb-6 text-sm text-slate-500 dark:text-slate-400">
            Welcome back
          </p>

          <form onSubmit={handleSubmit} className="space-y-4">
            <div>
              <label className="mb-1.5 flex items-center gap-1.5 text-sm font-medium text-slate-700 dark:text-slate-300">
                Email address
                <InfoTip text="The work email your company admin registered you with, e.g. you@company.com." />
              </label>
              <input
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
              <label className="mb-1.5 flex items-center gap-1.5 text-sm font-medium text-slate-700 dark:text-slate-300">
                Password
                <InfoTip text="Your account password (case-sensitive). Use 'Forgot password' or contact your HR admin if you can't sign in." />
              </label>
              <div className="relative">
                <input
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
              <label className="mb-1.5 flex items-center gap-1.5 text-sm font-medium text-slate-700 dark:text-slate-300">
                Workspace
                <InfoTip text="Your company's unique workspace ID (lowercase, e.g. acme-industries). It was shared with your admin when the workspace was created." />
              </label>
              <input
                type="text"
                value={tenantSlug}
                onChange={(e) => setTenantSlug(e.target.value)}
                className="input w-full"
                placeholder="your-workspace"
                autoComplete="organization"
                required
              />
              <p className="mt-1 text-xs text-slate-400 dark:text-slate-500">
                Your company's unique workspace identifier
              </p>
            </div>

            {error && (
              <p className="rounded-lg bg-red-50 px-3 py-2.5 text-sm text-red-600 dark:bg-red-500/10 dark:text-red-400">
                {error}
              </p>
            )}

            <button
              type="submit"
              disabled={isLoading}
              className="btn-primary w-full justify-center py-2.5 disabled:cursor-not-allowed disabled:opacity-60"
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
        <p className="mt-1 text-center text-[11px] text-slate-400 dark:text-slate-600">
          A <span className="font-semibold text-slate-500 dark:text-slate-400">Kode Kinetics</span> product
        </p>
      </div>
    </div>
  );
}
