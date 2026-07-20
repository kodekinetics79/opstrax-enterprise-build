import { useState } from "react";
import { KeyRound, ShieldCheck, UserCog } from "lucide-react";
import { platformApi } from "@/services/platformApi";
import { usePlatformAuth } from "@/hooks/usePlatformAuth";
import { PHeader, PCard, PButton, PField, PInput } from "./ui";

// Self-service account management for ANY platform admin — no second admin needed.
// Both actions hit /api/platform/auth/* which only ever touch the caller's own row.
export function PlatformAccountPage() {
  const { session, setSession } = usePlatformAuth();

  const [fullName, setFullName] = useState(session?.admin.name ?? "");
  const [email, setEmail] = useState(session?.admin.email ?? "");
  const [profileBusy, setProfileBusy] = useState(false);
  const [profileMsg, setProfileMsg] = useState<{ ok: boolean; text: string } | null>(null);

  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [pwBusy, setPwBusy] = useState(false);
  const [pwMsg, setPwMsg] = useState<{ ok: boolean; text: string } | null>(null);

  const pwTooShort = newPassword.length > 0 && newPassword.length < 12;
  const pwMismatch = confirmPassword.length > 0 && newPassword !== confirmPassword;
  const canChangePw =
    currentPassword.length > 0 && newPassword.length >= 12 && newPassword === confirmPassword && newPassword !== currentPassword;

  const saveProfile = async () => {
    setProfileBusy(true); setProfileMsg(null);
    try {
      await platformApi.updateOwnProfile({ fullName: fullName.trim(), email: email.trim() });
      // Reflect the change in the cached session (keeps the existing bearer token).
      if (session) setSession({ ...session, admin: { ...session.admin, name: fullName.trim(), email: email.trim() } });
      setProfileMsg({ ok: true, text: "Profile updated." });
    } catch (e) {
      setProfileMsg({ ok: false, text: e instanceof Error ? e.message : "Could not update profile" });
    } finally { setProfileBusy(false); }
  };

  const changePassword = async () => {
    setPwBusy(true); setPwMsg(null);
    try {
      const res = await platformApi.changeOwnPassword(currentPassword, newPassword);
      const revoked = Number(res?.otherSessionsRevoked ?? 0);
      setCurrentPassword(""); setNewPassword(""); setConfirmPassword("");
      setPwMsg({
        ok: true,
        text: `Password updated.${revoked > 0 ? ` You were signed out of ${revoked} other session${revoked === 1 ? "" : "s"}.` : ""}`,
      });
    } catch (e) {
      setPwMsg({ ok: false, text: e instanceof Error ? e.message : "Could not change password" });
    } finally { setPwBusy(false); }
  };

  return (
    <div className="space-y-7">
      <PHeader
        eyebrow="My Account"
        title="Account & security"
        description="Manage your own profile and password. Changing your password signs you out everywhere else."
      />

      <div className="grid gap-6 lg:grid-cols-2">
        {/* Profile */}
        <PCard className="p-6">
          <div className="mb-4 flex items-center gap-2">
            <UserCog className="h-4 w-4 text-slate-500" />
            <h2 className="text-sm font-bold uppercase tracking-wider text-slate-500">Profile</h2>
          </div>
          <div className="space-y-4">
            <PField label="Full name">
              <PInput value={fullName} onChange={(e) => setFullName(e.target.value)} placeholder="Your name" />
            </PField>
            <PField label="Email (this is your sign-in address)">
              <PInput type="email" value={email} onChange={(e) => setEmail(e.target.value)} placeholder="you@company.com" />
            </PField>
            <div className="rounded-xl border border-slate-200 bg-slate-50 px-3 py-2 text-xs text-slate-500">
              Role: <span className="font-semibold text-slate-700">{session?.role.name ?? "—"}</span>
              <span className="mx-2">·</span>
              Roles can only be changed by a Platform Super Admin.
            </div>
            {profileMsg && (
              <p className={`text-xs font-medium ${profileMsg.ok ? "text-emerald-600" : "text-red-600"}`}>{profileMsg.text}</p>
            )}
            <PButton onClick={saveProfile} disabled={profileBusy || (!fullName.trim() && !email.trim())}>
              {profileBusy ? "Saving…" : "Save profile"}
            </PButton>
          </div>
        </PCard>

        {/* Change password */}
        <PCard className="p-6">
          <div className="mb-4 flex items-center gap-2">
            <KeyRound className="h-4 w-4 text-slate-500" />
            <h2 className="text-sm font-bold uppercase tracking-wider text-slate-500">Change password</h2>
          </div>
          <div className="space-y-4">
            <PField label="Current password">
              <PInput type="password" autoComplete="current-password" value={currentPassword} onChange={(e) => setCurrentPassword(e.target.value)} />
            </PField>
            <PField label="New password (min 12 characters)">
              <PInput type="password" autoComplete="new-password" value={newPassword} onChange={(e) => setNewPassword(e.target.value)} />
            </PField>
            <PField label="Confirm new password">
              <PInput type="password" autoComplete="new-password" value={confirmPassword} onChange={(e) => setConfirmPassword(e.target.value)} />
            </PField>

            {pwTooShort && <p className="text-xs font-medium text-amber-600">New password must be at least 12 characters.</p>}
            {pwMismatch && <p className="text-xs font-medium text-amber-600">Passwords do not match.</p>}
            {pwMsg && <p className={`text-xs font-medium ${pwMsg.ok ? "text-emerald-600" : "text-red-600"}`}>{pwMsg.text}</p>}

            <PButton onClick={changePassword} disabled={pwBusy || !canChangePw}>
              {pwBusy ? "Updating…" : "Update password"}
            </PButton>

            <div className="flex items-start gap-2 rounded-xl border border-slate-200 bg-slate-50 px-3 py-2 text-xs text-slate-500">
              <ShieldCheck className="mt-0.5 h-3.5 w-3.5 shrink-0 text-slate-400" />
              <span>Your current password is verified before the change. All your other sessions are signed out; this device stays signed in.</span>
            </div>
          </div>
        </PCard>
      </div>
    </div>
  );
}
