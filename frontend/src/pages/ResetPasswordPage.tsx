import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { Link, useSearchParams } from "react-router-dom";
import { AlertCircle, CheckCircle2, ShieldCheck } from "lucide-react";
import { authApi } from "@/services/authApi";
import { OpsTraxLogo } from "@/components/OpsTraxLogo";

export function ResetPasswordPage() {
  const [params] = useSearchParams();
  const token = params.get("token") ?? "";
  const linkedEmail = params.get("email") ?? "";
  const resetting = Boolean(token && linkedEmail);
  // Activation links from the admin invite flow carry welcome=1 — same set-password
  // mechanics, different framing for a first-time user.
  const welcome = params.get("welcome") === "1";
  const [email, setEmail] = useState(linkedEmail);
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [localError, setLocalError] = useState("");
  const action = useMutation({
    mutationFn: async () => {
      if (resetting) await authApi.resetPassword(linkedEmail, token, password);
      else await authApi.forgotPassword(email);
      return true;
    },
  });
  const submit = (event: React.FormEvent) => {
    event.preventDefault(); setLocalError("");
    if (resetting && password !== confirm) { setLocalError("Passwords do not match."); return; }
    action.mutate();
  };
  const apiError = action.isError ? "We could not complete this request. The link may be invalid or expired." : "";

  return <main className="flex min-h-screen items-center justify-center bg-gradient-to-br from-slate-950 via-slate-900 to-teal-950 px-5 py-12">
    <div className="w-full max-w-md rounded-[26px] border border-white/15 bg-white/95 p-8 shadow-2xl">
      <div className="mb-7 flex items-center gap-3"><OpsTraxLogo size={38} /><div><p className="font-bold text-slate-950">OpsTrax</p><p className="text-xs text-slate-500">Secure account recovery</p></div></div>
      <h1 className="text-2xl font-bold text-slate-950">{resetting ? (welcome ? "Welcome to OpsTrax" : "Choose a new password") : "Reset your password"}</h1>
      <p className="mt-2 text-sm leading-6 text-slate-600">{resetting ? (welcome ? "Set a password to activate your account. This one-time link was issued by your administrator." : "Create a new password for your account. This one-time link expires after 30 minutes.") : "Enter your work email. If an active account matches, we’ll send a secure reset link."}</p>
      {action.isSuccess ? <div role="status" className="mt-6 rounded-xl border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-800"><CheckCircle2 className="mb-2 h-5 w-5" />{resetting ? (welcome ? "Your account is active. You can now sign in." : "Password updated. You can now sign in.") : "If an active account matches that email, reset instructions have been sent."}<div className="mt-3"><Link to="/login" className="font-semibold underline">Return to sign in</Link></div></div> :
      <form onSubmit={submit} className="mt-6 space-y-4">
        {!resetting && <label className="block text-sm font-medium text-slate-700">Work email<input type="email" autoComplete="email" required value={email} onChange={e => setEmail(e.target.value)} className="mt-1.5 w-full rounded-lg border border-slate-300 px-3.5 py-2.5 outline-none focus:border-teal-500 focus:ring-2 focus:ring-teal-500/15" /></label>}
        {resetting && <><label className="block text-sm font-medium text-slate-700">New password<input type="password" autoComplete="new-password" required minLength={8} value={password} onChange={e => setPassword(e.target.value)} className="mt-1.5 w-full rounded-lg border border-slate-300 px-3.5 py-2.5 outline-none focus:border-teal-500" /></label><label className="block text-sm font-medium text-slate-700">Confirm password<input type="password" autoComplete="new-password" required minLength={8} value={confirm} onChange={e => setConfirm(e.target.value)} className="mt-1.5 w-full rounded-lg border border-slate-300 px-3.5 py-2.5 outline-none focus:border-teal-500" /></label></>}
        {(localError || apiError) && <div role="alert" className="flex gap-2 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700"><AlertCircle className="h-4 w-4 shrink-0" />{localError || apiError}</div>}
        <button disabled={action.isPending || (!resetting && !email.trim()) || (resetting && (!password || !confirm))} className="w-full rounded-lg bg-teal-600 px-4 py-2.5 text-sm font-semibold text-white hover:bg-teal-500 disabled:opacity-50">{action.isPending ? "Please wait…" : resetting ? "Update password" : "Send reset link"}</button>
      </form>}
      <p className="mt-6 flex items-center justify-center gap-1.5 text-xs text-slate-500"><ShieldCheck className="h-4 w-4 text-teal-600" />One-time, time-limited account recovery</p>
    </div>
  </main>;
}
