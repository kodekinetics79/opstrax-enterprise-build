'use client';

import { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { platformApi } from '@/src/api/platform';
import { InfoTip } from '@/src/components/InfoTip';

export default function PlatformLoginPage() {
  const router = useRouter();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    const token = localStorage.getItem('platform_access_token');
    if (token) router.replace('/platform/dashboard');
  }, [router]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      const { token } = await platformApi.login(email, password);
      localStorage.setItem('platform_access_token', token);
      router.replace('/platform/dashboard');
    } catch {
      setError('Invalid credentials. Please try again.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-midnight px-4">
      <div className="w-full max-w-sm">
        <div className="mb-8 text-center">
          <h1 className="text-2xl font-semibold tracking-tight text-white">Platform Administration</h1>
          <p className="mt-1 text-sm text-slate-400">Internal access only</p>
        </div>

        <form onSubmit={handleSubmit} className="space-y-4 rounded-xl border border-white/10 bg-sidebarDark p-6">
          <div>
            <label className="mb-1.5 flex items-center gap-1.5 text-xs font-medium text-slate-400">
              Email
              <InfoTip text="The platform owner email configured for this KynexOne instance. This is NOT a tenant login — clients sign in on the regular login page." />
            </label>
            <input
              type="email"
              value={email}
              onChange={e => setEmail(e.target.value)}
              required
              autoComplete="email"
              className="w-full rounded-lg border border-white/10 bg-midnight px-3 py-2.5 text-sm text-white placeholder-slate-600 focus:border-sapphire focus:outline-none focus:ring-1 focus:ring-sapphire"
              placeholder="admin@example.com"
            />
          </div>

          <div>
            <label className="mb-1.5 flex items-center gap-1.5 text-xs font-medium text-slate-400">
              Password
              <InfoTip text="The platform owner password (case-sensitive), set in the server configuration." />
            </label>
            <input
              type="password"
              value={password}
              onChange={e => setPassword(e.target.value)}
              required
              autoComplete="current-password"
              className="w-full rounded-lg border border-white/10 bg-midnight px-3 py-2.5 text-sm text-white placeholder-slate-600 focus:border-sapphire focus:outline-none focus:ring-1 focus:ring-sapphire"
              placeholder="••••••••"
            />
          </div>

          {error && (
            <p className="rounded-lg border border-rose-500/20 bg-rose-500/10 px-3 py-2 text-xs text-rose-400">{error}</p>
          )}

          <button
            type="submit"
            disabled={loading}
            className="w-full rounded-lg bg-sapphire py-2.5 text-sm font-medium text-white transition-opacity hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {loading ? 'Signing in…' : 'Sign In'}
          </button>
        </form>
      </div>
    </div>
  );
}
