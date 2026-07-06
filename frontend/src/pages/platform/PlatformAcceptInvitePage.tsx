import { useMemo, useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { CheckCircle2, KeyRound, Loader2 } from "lucide-react";
import { OpsTraxLogo } from "@/components/OpsTraxLogo";
import { platformApi } from "@/services/platformApi";

// Public (pre-session) page: an invited operator lands here from their one-time
// invite link and sets their password. On success they sign in through the
// normal platform login — this page never mints a session.
export function PlatformAcceptInvitePage() {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [email, setEmail] = useState(params.get("email") ?? "");
  const [token, setToken] = useState(params.get("token") ?? "");
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState(false);

  const passwordIssue = useMemo(() => {
    if (!password) return null;
    if (password.length < 12) return "At least 12 characters.";
    if (!/[a-zA-Z]/.test(password) || !/\d/.test(password)) return "Must contain letters and digits.";
    if (confirm && password !== confirm) return "Passwords do not match.";
    return null;
  }, [password, confirm]);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (passwordIssue || password !== confirm) return;
    setBusy(true);
    setError(null);
    try {
      await platformApi.acceptPlatformInvite({ email: email.trim(), token: token.trim(), password });
      setDone(true);
      setTimeout(() => navigate("/platform/login", { replace: true }), 1800);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Invite could not be accepted");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="platform-shell flex min-h-screen items-center justify-center px-4 py-8 text-slate-100">
      <div className="w-full max-w-md">
        <div className="relative overflow-hidden rounded-[28px] border border-slate-800/70 bg-white/5 p-8 shadow-[0_30px_90px_rgba(2,6,23,.35)] backdrop-blur">
          <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(circle_at_top_left,rgba(45,212,191,.16),transparent_30%)]" />
          <div className="relative">
            <div className="flex items-center gap-2.5">
              <OpsTraxLogo size={30} />
              <div>
                <p className="text-sm font-bold tracking-tight">OpsTrax</p>
                <p className="text-[10px] font-semibold uppercase tracking-[0.2em] text-teal-400">Platform operator setup</p>
              </div>
            </div>

            {done ? (
              <div className="mt-8 flex flex-col items-center gap-3 py-6 text-center">
                <CheckCircle2 className="h-10 w-10 text-emerald-400" />
                <p className="font-semibold text-slate-100">Password set</p>
                <p className="text-sm text-slate-400">Redirecting you to sign in…</p>
              </div>
            ) : (
              <form onSubmit={submit} className="mt-7 space-y-4">
                <p className="text-sm leading-6 text-slate-400">
                  Set the password for your operator account. Invite links are single-use and expire 7 days after issue.
                </p>
                <label className="block">
                  <span className="mb-1.5 block text-xs font-semibold uppercase tracking-wider text-slate-400">Email</span>
                  <input
                    className="w-full rounded-[14px] border border-slate-700 bg-slate-900/60 px-3 py-2.5 text-sm text-slate-100 placeholder:text-slate-500 outline-none focus:border-teal-400"
                    type="email" value={email} onChange={(e) => setEmail(e.target.value)} required autoComplete="username"
                  />
                </label>
                <label className="block">
                  <span className="mb-1.5 block text-xs font-semibold uppercase tracking-wider text-slate-400">Invite token</span>
                  <input
                    className="w-full rounded-[14px] border border-slate-700 bg-slate-900/60 px-3 py-2.5 font-mono text-xs text-slate-100 placeholder:text-slate-500 outline-none focus:border-teal-400"
                    value={token} onChange={(e) => setToken(e.target.value)} required placeholder="Paste the token from your invite link"
                  />
                </label>
                <label className="block">
                  <span className="mb-1.5 block text-xs font-semibold uppercase tracking-wider text-slate-400">New password</span>
                  <input
                    className="w-full rounded-[14px] border border-slate-700 bg-slate-900/60 px-3 py-2.5 text-sm text-slate-100 outline-none focus:border-teal-400"
                    type="password" value={password} onChange={(e) => setPassword(e.target.value)} required autoComplete="new-password"
                  />
                </label>
                <label className="block">
                  <span className="mb-1.5 block text-xs font-semibold uppercase tracking-wider text-slate-400">Confirm password</span>
                  <input
                    className="w-full rounded-[14px] border border-slate-700 bg-slate-900/60 px-3 py-2.5 text-sm text-slate-100 outline-none focus:border-teal-400"
                    type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} required autoComplete="new-password"
                  />
                </label>
                {passwordIssue && <p className="text-xs font-semibold text-amber-400" role="alert">{passwordIssue}</p>}
                {error && <p className="text-xs font-semibold text-red-400" role="alert">{error}</p>}
                <button
                  type="submit"
                  disabled={busy || !!passwordIssue || !password || !confirm}
                  className="flex w-full items-center justify-center gap-2 rounded-[14px] bg-gradient-to-r from-teal-400 to-cyan-400 px-4 py-2.5 text-sm font-bold text-slate-950 transition hover:brightness-105 disabled:opacity-50"
                >
                  {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <KeyRound className="h-4 w-4" />}
                  {busy ? "Setting password…" : "Set password"}
                </button>
              </form>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
