'use client';

import { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { Eye, EyeOff } from 'lucide-react';
import { platformApi } from '@/src/api/platform';
import { Logo } from '@/src/components/Logo';

type ErrorKind = 'invalid_credentials' | 'not_configured' | 'network' | null;

function errorMessage(kind: ErrorKind): string {
  switch (kind) {
    case 'invalid_credentials': return 'Invalid platform admin credentials. Please check your email and password.';
    case 'not_configured': return 'Platform admin access is not configured on this server. Set PLATFORM_ADMIN_EMAIL and PLATFORM_ADMIN_PASSWORD environment variables.';
    case 'network': return 'Cannot reach the server. Check that the backend is running and reachable.';
    default: return '';
  }
}

export default function PlatformLoginPage() {
  const router = useRouter();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [errorKind, setErrorKind] = useState<ErrorKind>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (typeof window !== 'undefined' && localStorage.getItem('platform_access_token')) {
      router.replace('/platform/dashboard');
    }
  }, [router]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setErrorKind(null);
    setLoading(true);
    try {
      const { token } = await platformApi.login(email, password);
      localStorage.setItem('platform_access_token', token);
      router.replace('/platform/dashboard');
    } catch (err: unknown) {
      const status = (err as { response?: { status?: number } })?.response?.status;
      if (status === 401) {
        setErrorKind('invalid_credentials');
      } else if (status === 503) {
        setErrorKind('not_configured');
      } else if (!(err as { response?: unknown })?.response) {
        setErrorKind('network');
      } else {
        setErrorKind('invalid_credentials');
      }
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-midnight px-4">
      <div className="w-full max-w-sm">
        <div className="mb-8 flex justify-center">
          <Logo size="lg" />
        </div>

        <div className="rounded-2xl border border-white/10 bg-sidebarDark p-8">
          <h1 className="mb-1 text-2xl font-extrabold text-white">Platform Admin</h1>
          <p className="mb-6 text-sm text-slate-500">
            Internal access only — not for tenant users
          </p>

          <form onSubmit={handleSubmit} className="space-y-4">
            <div>
              <label htmlFor="platform-email" className="mb-1.5 block text-xs font-medium text-slate-400">
                Email address
              </label>
              <input
                id="platform-email"
                type="email"
                value={email}
                onChange={e => setEmail(e.target.value)}
                required
                autoComplete="email"
                placeholder="admin@example.com"
                className="w-full rounded-lg border border-white/10 bg-midnight px-3 py-2.5 text-sm text-white placeholder-slate-600 focus:border-sapphire focus:outline-none focus:ring-1 focus:ring-sapphire"
              />
            </div>

            <div>
              <label htmlFor="platform-password" className="mb-1.5 block text-xs font-medium text-slate-400">
                Password
              </label>
              <div className="relative">
                <input
                  id="platform-password"
                  type={showPassword ? 'text' : 'password'}
                  value={password}
                  onChange={e => setPassword(e.target.value)}
                  required
                  autoComplete="current-password"
                  placeholder="••••••••"
                  className="w-full rounded-lg border border-white/10 bg-midnight px-3 py-2.5 pr-10 text-sm text-white placeholder-slate-600 focus:border-sapphire focus:outline-none focus:ring-1 focus:ring-sapphire"
                />
                <button
                  type="button"
                  onClick={() => setShowPassword(v => !v)}
                  tabIndex={-1}
                  aria-label={showPassword ? 'Hide password' : 'Show password'}
                  className="absolute right-3 top-1/2 -translate-y-1/2 text-slate-500 hover:text-slate-300"
                >
                  {showPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                </button>
              </div>
            </div>

            {errorKind && (
              <div className="rounded-lg border border-rose-500/20 bg-rose-500/10 px-3 py-2.5 text-sm text-rose-400">
                {errorMessage(errorKind)}
              </div>
            )}

            <button
              type="submit"
              disabled={loading}
              className="flex w-full items-center justify-center gap-2 rounded-lg bg-sapphire py-2.5 text-sm font-medium text-white transition-opacity hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-50"
            >
              {loading ? (
                <>
                  <span className="h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" />
                  Signing in…
                </>
              ) : (
                'Sign in'
              )}
            </button>
          </form>

          <p className="mt-6 text-center text-xs text-slate-600">
            Tenant workspace login?{' '}
            <a href="/login" className="text-slate-400 underline hover:text-white">
              Sign in here
            </a>
          </p>
        </div>

        <p className="mt-6 text-center text-xs text-slate-600">
          A{' '}
          <span className="font-semibold text-slate-500">Kode Kinetics</span>{' '}
          product
        </p>
      </div>
    </div>
  );
}
