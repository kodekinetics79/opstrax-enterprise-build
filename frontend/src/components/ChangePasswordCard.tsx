import { useState } from "react";
import { KeyRound, ShieldCheck } from "lucide-react";
import { authApi } from "@/services/authApi";

// Self-service password change for ANY signed-in tenant user — including customer
// portal users (they are `users` rows too). Hits POST /api/auth/change-password,
// which verifies the current password and revokes the user's other sessions.
// No email/SMTP required, so it works even when outbound mail is not configured.
export function ChangePasswordCard({ className = "" }: { className?: string }) {
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);

  const tooShort = newPassword.length > 0 && newPassword.length < 8;
  const mismatch = confirmPassword.length > 0 && newPassword !== confirmPassword;
  const canSubmit =
    currentPassword.length > 0 && newPassword.length >= 8 && newPassword === confirmPassword && newPassword !== currentPassword;

  const submit = async () => {
    setBusy(true); setMsg(null);
    try {
      await authApi.changePassword(currentPassword, newPassword);
      setCurrentPassword(""); setNewPassword(""); setConfirmPassword("");
      setMsg({ ok: true, text: "Password updated. You've been signed out on your other devices." });
    } catch (e) {
      setMsg({ ok: false, text: e instanceof Error ? e.message : "Could not change password" });
    } finally { setBusy(false); }
  };

  return (
    <div className={`panel p-5 ${className}`}>
      <div className="mb-4 flex items-center gap-2">
        <KeyRound className="h-4 w-4 text-slate-500" />
        <h2 className="section-title">Change password</h2>
      </div>

      <div className="grid gap-4 sm:max-w-md">
        <label>
          <span className="field-label">Current password</span>
          <input type="password" autoComplete="current-password" className="field mt-1"
            value={currentPassword} onChange={(e) => setCurrentPassword(e.target.value)} />
        </label>
        <label>
          <span className="field-label">New password (min 8 characters)</span>
          <input type="password" autoComplete="new-password" className="field mt-1"
            value={newPassword} onChange={(e) => setNewPassword(e.target.value)} />
        </label>
        <label>
          <span className="field-label">Confirm new password</span>
          <input type="password" autoComplete="new-password" className="field mt-1"
            value={confirmPassword} onChange={(e) => setConfirmPassword(e.target.value)} />
        </label>

        {tooShort && <p className="text-xs font-medium text-amber-600">New password must be at least 8 characters.</p>}
        {mismatch && <p className="text-xs font-medium text-amber-600">Passwords do not match.</p>}
        {msg && <p className={`text-xs font-medium ${msg.ok ? "text-emerald-600" : "text-red-600"}`}>{msg.text}</p>}

        <div>
          <button type="button" className="btn-primary" disabled={busy || !canSubmit} onClick={submit}>
            {busy ? "Updating…" : "Update password"}
          </button>
        </div>

        <p className="flex items-start gap-1.5 text-xs text-slate-500">
          <ShieldCheck className="mt-0.5 h-3.5 w-3.5 shrink-0 text-slate-400" />
          Your current password is verified before the change. This device stays signed in.
        </p>
      </div>
    </div>
  );
}
