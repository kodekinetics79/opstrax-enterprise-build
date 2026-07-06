import { useState } from "react";
import { useNavigate } from "react-router-dom";
import axios from "axios";
import { ArrowRight, Loader2, ShieldCheck } from "lucide-react";
import { OpsTraxLogo } from "@/components/OpsTraxLogo";
import { usePlatformAuth } from "@/hooks/usePlatformAuth";
import { platformApi } from "@/services/platformApi";

export function PlatformLoginPage() {
  const { setSession } = usePlatformAuth();
  const navigate = useNavigate();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [mfaCode, setMfaCode] = useState("");
  const [mfaRequired, setMfaRequired] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const loginWith = async (nextEmail: string, nextPassword: string) => {
    setError(null);
    setLoading(true);
    try {
      const session = await platformApi.login(nextEmail, nextPassword, mfaCode.trim() || undefined);
      setSession(session);
      navigate("/platform", { replace: true });
    } catch (err) {
      if (axios.isAxiosError(err)) {
        const data = err.response?.data as { message?: string; errors?: string[] } | undefined;
        if (data?.errors?.includes("mfa_required")) {
          setMfaRequired(true);
          setError("Enter the 6-digit code from your authenticator app.");
          return;
        }
        setError(data?.message ?? "Login failed");
        return;
      }
      setError(err instanceof Error ? err.message : "Login failed");
    } finally {
      setLoading(false);
    }
  };

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    await loginWith(email, password);
  };

  return (
    <div className="platform-shell min-h-screen px-4 py-6 text-slate-100 lg:px-8">
      <div className="mx-auto grid min-h-[calc(100vh-3rem)] w-full max-w-6xl items-center gap-8 lg:grid-cols-[1.05fr_.95fr]">
        <section className="relative overflow-hidden rounded-[28px] border border-slate-800/70 bg-white/5 p-8 shadow-[0_30px_90px_rgba(2,6,23,.35)] backdrop-blur">
          <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(circle_at_top_left,rgba(45,212,191,.18),transparent_28%),radial-gradient(circle_at_bottom_right,rgba(59,130,246,.12),transparent_30%)]" />
          <div className="relative">
            <div className="flex items-center gap-3">
              <OpsTraxLogo size={54} />
              <div>
                <p className="text-[11px] font-semibold uppercase tracking-[0.22em] text-teal-400">Platform Admin</p>
                <h1 className="mt-1 text-3xl font-bold tracking-tight text-white">Commercial control plane</h1>
              </div>
            </div>
            <p className="mt-5 max-w-xl text-sm leading-7 text-slate-300">
              Govern tenants, subscriptions, invoices, health, and audit from one secure command surface.
              This is the staff-only portal for platform operations.
            </p>

            <div className="mt-8 grid gap-3 sm:grid-cols-3">
              {[
                "Tenant visibility",
                "Commercial workflow",
                "Security and audit",
              ].map((item) => (
                <div key={item} className="rounded-2xl border border-slate-800/80 bg-slate-950/50 px-4 py-3">
                  <p className="text-sm font-semibold text-white">{item}</p>
                  <p className="mt-1 text-[11px] leading-5 text-slate-400">
                    Live platform controls with strong separation from tenant sessions.
                  </p>
                </div>
              ))}
            </div>

            <div className="mt-8 flex flex-wrap items-center gap-2 text-[11px] font-semibold uppercase tracking-[0.18em] text-slate-400">
              <span className="inline-flex items-center gap-2 rounded-full border border-emerald-500/25 bg-emerald-500/10 px-3 py-1 text-emerald-300">
                <ShieldCheck className="h-3.5 w-3.5" />
                Staff only
              </span>
            </div>
          </div>
        </section>

        <section className="rounded-[28px] border border-slate-800/70 bg-slate-950/80 p-7 shadow-[0_30px_90px_rgba(2,6,23,.42)] backdrop-blur">
          <div className="mb-8 flex flex-col items-start gap-3">
            <OpsTraxLogo size={42} />
            <div>
              <p className="text-[11px] font-semibold uppercase tracking-[0.22em] text-slate-500">Secure access</p>
              <h2 className="mt-2 text-2xl font-bold text-white">Sign in to Platform Admin</h2>
              <p className="mt-1.5 text-sm leading-6 text-slate-400">Use your configured platform credentials for the staff-only portal.</p>
            </div>
          </div>

          {error && (
            <div className="mb-4 rounded-2xl border border-red-500/30 bg-red-500/10 px-4 py-3 text-sm text-red-200">
              {error}
            </div>
          )}

          <form onSubmit={submit} className="space-y-4">
            <label className="block">
              <span className="mb-1.5 block text-xs font-semibold uppercase tracking-wider text-slate-500">Email</span>
              <input
                type="email"
                required
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="platform@opstrax.io"
                className="field border-slate-700 bg-slate-900/70 text-slate-100 placeholder:text-slate-500 focus:border-teal-400"
              />
            </label>
            <label className="block">
              <span className="mb-1.5 block text-xs font-semibold uppercase tracking-wider text-slate-500">Password</span>
              <input
                type="password"
                required
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="Enter password"
                className="field border-slate-700 bg-slate-900/70 text-slate-100 placeholder:text-slate-500 focus:border-teal-400"
              />
            </label>
            {mfaRequired && (
              <label className="block">
                <span className="mb-1.5 block text-xs font-semibold uppercase tracking-wider text-slate-500">Authenticator code</span>
                <input
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  maxLength={6}
                  required
                  value={mfaCode}
                  onChange={(e) => setMfaCode(e.target.value.replace(/\D/g, ""))}
                  placeholder="123456"
                  className="field border-slate-700 bg-slate-900/70 text-center font-mono text-lg tracking-[0.4em] text-slate-100 placeholder:text-slate-600 focus:border-teal-400"
                />
              </label>
            )}
            <button
              type="submit"
              disabled={loading}
              className="btn-primary mt-2 w-full justify-center text-sm disabled:cursor-not-allowed"
            >
              {loading ? <Loader2 className="h-4 w-4 animate-spin" /> : <ArrowRight className="h-4 w-4" />}
              Sign in
            </button>
          </form>

          <p className="mt-5 text-center text-xs text-slate-500">
            Tenant users sign in at the main application login. Platform access stays isolated.
          </p>
        </section>
      </div>
    </div>
  );
}
