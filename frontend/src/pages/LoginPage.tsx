import { useState } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import { Logo } from '../components/Logo';

export function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const from = (location.state as { from?: { pathname?: string } } | null)?.from?.pathname ?? '/dashboard';

  const [email, setEmail] = useState('admin@zayra.local');
  const [password, setPassword] = useState('ChangeMe123!');
  const [tenantSlug, setTenantSlug] = useState('zayra');
  const [error, setError] = useState('');
  const [isLoading, setIsLoading] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setIsLoading(true);
    try {
      await login(email, password, tenantSlug);
      navigate(from, { replace: true });
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
          <h1 className="mb-1 text-xl font-bold text-slate-900 dark:text-white">Sign in</h1>
          <p className="mb-6 text-sm text-slate-500 dark:text-slate-400">
            Welcome back to KynexOne
          </p>

          <form onSubmit={handleSubmit} className="space-y-4">
            <div>
              <label className="mb-1.5 block text-sm font-medium text-slate-700 dark:text-slate-300">
                Email address
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
              <label className="mb-1.5 block text-sm font-medium text-slate-700 dark:text-slate-300">
                Password
              </label>
              <input
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                className="input w-full"
                placeholder="••••••••"
                autoComplete="current-password"
                required
              />
            </div>

            <div>
              <label className="mb-1.5 block text-sm font-medium text-slate-700 dark:text-slate-300">
                Workspace
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
                Your company's KynexOne workspace identifier
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
          KynexOne · One Platform for Every Workforce Operation
        </p>
        <p className="mt-1 text-center text-[11px] text-slate-400 dark:text-slate-600">
          A <span className="font-semibold text-slate-500 dark:text-slate-400">Kode Kinetics</span> product
        </p>
      </div>
    </div>
  );
}
