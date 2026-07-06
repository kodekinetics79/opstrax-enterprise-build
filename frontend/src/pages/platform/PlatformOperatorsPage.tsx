import { useCallback, useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Copy, KeyRound, MailCheck, RefreshCw, ScrollText, ShieldOff, ShieldCheck, Smartphone, UserPlus } from "lucide-react";
import { PHeader, PCard, PBadge, PButton, PField, PInput, PSelect, PLoading, PError, PEmpty, PDrawer, PConfirm } from "./ui";
import { platformApi } from "@/services/platformApi";
import { usePlatformAuth } from "@/hooks/usePlatformAuth";
import type { AnyRecord } from "@/types";

// One-time invite artifact: shown exactly once after invite/reset. The token is
// never retrievable again (only its hash is stored server-side). When SMTP is
// configured the backend has already emailed the link; otherwise the panel
// makes the copy step explicit before it can be dismissed.
function InviteLinkPanel({ email, token, emailSent, onDone }: { email: string; token: string; emailSent: boolean; onDone: () => void }) {
  const [copied, setCopied] = useState(false);
  const link = `${window.location.origin}/platform/accept-invite?email=${encodeURIComponent(email)}&token=${encodeURIComponent(token)}`;
  return (
    <div className="rounded-[14px] border border-amber-200 bg-amber-50 p-4">
      <p className="text-sm font-bold text-amber-800">Invite link — visible only once</p>
      {emailSent ? (
        <p className="mt-1 flex items-center gap-1.5 text-xs leading-5 text-emerald-700">
          <MailCheck className="h-3.5 w-3.5 shrink-0" /> Invite email sent to {email}. The link below is your backup copy.
        </p>
      ) : (
        <p className="mt-1 text-xs leading-5 text-amber-700">
          Deliver this link to the operator through a secure channel. It expires in 7 days and cannot be shown again — only reset.
        </p>
      )}
      <div className="mt-3 flex items-center gap-2">
        <code className="min-w-0 flex-1 truncate rounded-lg border border-amber-200 bg-white px-3 py-2 text-xs text-slate-700">{link}</code>
        <PButton
          variant="ghost"
          onClick={() => {
            void navigator.clipboard.writeText(link).then(() => setCopied(true));
          }}
        >
          <Copy className="h-3.5 w-3.5" /> {copied ? "Copied" : "Copy"}
        </PButton>
      </div>
      <div className="mt-3 flex justify-end">
        <PButton variant="primary" onClick={onDone} disabled={!emailSent && !copied}>
          {emailSent || copied ? "Done" : "Copy the link to continue"}
        </PButton>
      </div>
    </div>
  );
}

// Self-service TOTP enrollment: secret shown once → operator adds it to an
// authenticator app → proves possession with a code → MFA enforced at login.
function MfaEnrollPanel({ onDone }: { onDone: () => void }) {
  const [enrollment, setEnrollment] = useState<{ secret: string; otpauthUri: string } | null>(null);
  const [code, setCode] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [verified, setVerified] = useState(false);

  const start = async () => {
    setBusy(true);
    setError(null);
    try {
      setEnrollment(await platformApi.mfaEnroll());
    } catch (err) {
      setError(err instanceof Error ? err.message : "Enrollment failed");
    } finally {
      setBusy(false);
    }
  };

  const verify = async () => {
    setBusy(true);
    setError(null);
    try {
      await platformApi.mfaVerify(code.trim());
      setVerified(true);
    } catch {
      setError("That code didn't match — check your authenticator and try again.");
    } finally {
      setBusy(false);
    }
  };

  if (verified) {
    return (
      <div className="space-y-4 text-center">
        <ShieldCheck className="mx-auto h-10 w-10 text-emerald-500" />
        <p className="font-semibold text-slate-900">MFA is active</p>
        <p className="text-sm text-slate-500">From now on, sign-in requires your password and a 6-digit authenticator code.</p>
        <PButton onClick={onDone}>Done</PButton>
      </div>
    );
  }

  if (!enrollment) {
    return (
      <div className="space-y-4">
        <p className="text-sm leading-6 text-slate-600">
          Add a second factor to your operator account. You'll need an authenticator app (Google Authenticator, 1Password, Authy…).
          Starting enrollment replaces any previous authenticator setup.
        </p>
        {error && <PError message={error} />}
        <PButton onClick={() => void start()} disabled={busy}>{busy ? "Preparing…" : "Start enrollment"}</PButton>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <p className="text-sm leading-6 text-slate-600">
        Add this secret to your authenticator app (shown only once), then enter the current 6-digit code to activate.
      </p>
      <div className="rounded-[14px] border border-slate-200 bg-slate-50 p-4">
        <p className="text-xs font-semibold uppercase tracking-wider text-slate-500">Setup key</p>
        <code className="mt-1 block break-all font-mono text-sm text-slate-800">{enrollment.secret}</code>
        <p className="mt-3 text-xs font-semibold uppercase tracking-wider text-slate-500">Or add via URI</p>
        <code className="mt-1 block break-all font-mono text-[11px] text-slate-600">{enrollment.otpauthUri}</code>
      </div>
      <PField label="6-digit code">
        <PInput
          inputMode="numeric"
          maxLength={6}
          value={code}
          onChange={(e) => setCode(e.target.value.replace(/\D/g, ""))}
          placeholder="123456"
        />
      </PField>
      {error && <PError message={error} />}
      <div className="flex justify-end gap-2">
        <PButton variant="ghost" onClick={onDone} disabled={busy}>Cancel</PButton>
        <PButton onClick={() => void verify()} disabled={busy || code.length !== 6}>{busy ? "Verifying…" : "Verify & activate"}</PButton>
      </div>
    </div>
  );
}

export function PlatformOperatorsPage() {
  const { session, can } = usePlatformAuth();
  const navigate = useNavigate();
  const canManage = can("platform:admins:manage");
  const canSeeAudit = can("platform:audit:view");

  const [admins, setAdmins] = useState<AnyRecord[] | null>(null);
  const [roles, setRoles] = useState<AnyRecord[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<number | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  // Invite drawer state
  const [inviteOpen, setInviteOpen] = useState(false);
  const [inviteForm, setInviteForm] = useState({ email: "", fullName: "", roleKey: "" });
  const [inviteBusy, setInviteBusy] = useState(false);
  const [inviteError, setInviteError] = useState<string | null>(null);
  // One-time link (from invite or reset)
  const [issued, setIssued] = useState<{ email: string; token: string; emailSent: boolean } | null>(null);
  const [confirmDisable, setConfirmDisable] = useState<AnyRecord | null>(null);
  const [mfaOpen, setMfaOpen] = useState(false);

  const reload = useCallback(async () => {
    try {
      setError(null);
      const [adminRows, roleRows] = await Promise.all([platformApi.platformAdmins(), platformApi.roles()]);
      setAdmins(adminRows);
      setRoles(roleRows);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to load operators");
    }
  }, []);

  useEffect(() => {
    void reload();
  }, [reload]);

  const run = async (id: number, action: () => Promise<unknown>) => {
    setBusyId(id);
    setActionError(null);
    try {
      await action();
      await reload();
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "Action failed");
    } finally {
      setBusyId(null);
    }
  };

  const submitInvite = async () => {
    setInviteBusy(true);
    setInviteError(null);
    try {
      const created = await platformApi.createPlatformAdmin(inviteForm);
      setIssued({ email: String(created.email ?? inviteForm.email), token: String(created.inviteToken), emailSent: created.emailSent === true });
      setInviteForm({ email: "", fullName: "", roleKey: "" });
      await reload();
    } catch (err) {
      setInviteError(err instanceof Error ? err.message : "Invite failed");
    } finally {
      setInviteBusy(false);
    }
  };

  if (error) return <PError message={error} />;
  if (admins === null) return <PLoading />;

  return (
    <div className="space-y-6">
      <PHeader
        eyebrow="Control Plane"
        title="Platform Operators"
        description="Invite, disable and govern the people who run the OpsTrax control plane. Every action here is audited; Super Admin changes require a Super Admin."
        actions={
          canManage || canSeeAudit ? (
            <>
              {canSeeAudit && (
                <PButton variant="ghost" onClick={() => navigate("/platform/audit")}>
                  <ScrollText className="h-4 w-4" /> View audit trail
                </PButton>
              )}
              <PButton variant="ghost" onClick={() => setMfaOpen(true)}>
                <Smartphone className="h-4 w-4" /> My MFA
              </PButton>
              {canManage && (
                <PButton onClick={() => { setInviteOpen(true); setInviteError(null); setIssued(null); }}>
                  <UserPlus className="h-4 w-4" /> Invite operator
                </PButton>
              )}
            </>
          ) : undefined
        }
      />

      {actionError && <PError message={actionError} />}

      {admins.length === 0 ? (
        <PEmpty title="No operators" subtitle="Invite the first platform operator to get started." />
      ) : (
        <PCard className="overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[860px] text-left text-sm">
              <thead className="border-b border-slate-200 bg-slate-50">
                <tr>
                  {["Operator", "Role", "Status", "Sessions", "Last login", canManage ? "Actions" : ""].filter(Boolean).map((h) => (
                    <th key={h} className="px-5 py-3 text-xs font-semibold uppercase tracking-wider text-slate-500">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {admins.map((a) => {
                  const id = Number(a.id);
                  const status = String(a.status ?? "");
                  const isSelf = id === Number(session?.admin.id);
                  const busy = busyId === id;
                  return (
                    <tr key={id} className="transition hover:bg-slate-50">
                      <td className="px-5 py-3.5">
                        <p className="font-semibold text-slate-900">{String(a.fullName ?? "")}{isSelf && <span className="ml-2 text-[10px] font-bold uppercase text-teal-700">you</span>}</p>
                        <p className="text-xs text-slate-500">{String(a.email ?? "")}</p>
                      </td>
                      <td className="px-5 py-3.5">
                        {canManage && !isSelf ? (
                          <PSelect
                            value={String(a.roleKey ?? "")}
                            disabled={busy}
                            onChange={(e) => void run(id, () => platformApi.setPlatformAdminRole(id, e.target.value))}
                          >
                            {roles.map((r) => (
                              <option key={String(r.roleKey)} value={String(r.roleKey)}>{String(r.name ?? r.roleKey)}</option>
                            ))}
                          </PSelect>
                        ) : (
                          <span className="text-slate-700">{String(a.roleName ?? a.roleKey ?? "—")}</span>
                        )}
                      </td>
                      <td className="px-5 py-3.5">
                        <div className="flex items-center gap-1.5">
                          <PBadge value={status} />
                          {a.mfaEnabled === true && (
                            <span title="MFA enabled" className="inline-flex items-center gap-1 rounded-full border border-teal-200 bg-teal-50 px-2 py-[3px] text-[10px] font-bold text-teal-700">
                              <Smartphone className="h-3 w-3" /> MFA
                            </span>
                          )}
                        </div>
                      </td>
                      <td className="px-5 py-3.5 text-slate-700 tabular-nums">{String(a.activeSessions ?? 0)}</td>
                      <td className="px-5 py-3.5 text-xs text-slate-500">
                        {a.lastLoginAt ? String(a.lastLoginAt).slice(0, 16).replace("T", " ") : "Never"}
                      </td>
                      {canManage && (
                        <td className="px-5 py-3.5">
                          <div className="flex flex-wrap items-center gap-1.5">
                            {status === "Disabled" ? (
                              <PButton variant="ghost" disabled={busy} onClick={() => void run(id, () => platformApi.setPlatformAdminStatus(id, "Active"))}>
                                <ShieldCheck className="h-3.5 w-3.5" /> Enable
                              </PButton>
                            ) : (
                              <PButton variant="danger" disabled={busy || isSelf} onClick={() => setConfirmDisable(a)}>
                                <ShieldOff className="h-3.5 w-3.5" /> Disable
                              </PButton>
                            )}
                            <PButton variant="ghost" disabled={busy} onClick={() => void run(id, () => platformApi.revokePlatformAdminSessions(id))}>
                              <RefreshCw className="h-3.5 w-3.5" /> Revoke sessions
                            </PButton>
                            <PButton
                              variant="ghost"
                              disabled={busy || status === "Disabled"}
                              onClick={() =>
                                void run(id, async () => {
                                  const reset = await platformApi.resetPlatformAdminInvite(id);
                                  setIssued({ email: String(a.email), token: String(reset.inviteToken), emailSent: reset.emailSent === true });
                                  setInviteOpen(true);
                                })
                              }
                            >
                              <KeyRound className="h-3.5 w-3.5" /> Reset invite
                            </PButton>
                            {a.mfaEnabled === true && (
                              <PButton variant="ghost" disabled={busy} onClick={() => void run(id, () => platformApi.resetPlatformAdminMfa(id))}>
                                <Smartphone className="h-3.5 w-3.5" /> Reset MFA
                              </PButton>
                            )}
                          </div>
                        </td>
                      )}
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </PCard>
      )}

      {/* Invite drawer — also hosts the one-time link after invite/reset */}
      <PDrawer
        open={inviteOpen}
        onClose={() => { if (!issued) setInviteOpen(false); }}
        title={issued ? "Deliver the invite link" : "Invite platform operator"}
      >
        {issued ? (
          <InviteLinkPanel email={issued.email} token={issued.token} emailSent={issued.emailSent} onDone={() => { setIssued(null); setInviteOpen(false); }} />
        ) : (
          <div className="space-y-4">
            <PField label="Full name">
              <PInput value={inviteForm.fullName} onChange={(e) => setInviteForm((f) => ({ ...f, fullName: e.target.value }))} placeholder="Operator name" />
            </PField>
            <PField label="Email">
              <PInput type="email" value={inviteForm.email} onChange={(e) => setInviteForm((f) => ({ ...f, email: e.target.value }))} placeholder="operator@company.com" />
            </PField>
            <PField label="Platform role">
              <PSelect value={inviteForm.roleKey} onChange={(e) => setInviteForm((f) => ({ ...f, roleKey: e.target.value }))}>
                <option value="">Select a role…</option>
                {roles.map((r) => (
                  <option key={String(r.roleKey)} value={String(r.roleKey)}>{String(r.name ?? r.roleKey)}</option>
                ))}
              </PSelect>
            </PField>
            {inviteError && <PError message={inviteError} />}
            <div className="flex justify-end gap-2 pt-1">
              <PButton variant="ghost" onClick={() => setInviteOpen(false)} disabled={inviteBusy}>Cancel</PButton>
              <PButton onClick={() => void submitInvite()} disabled={inviteBusy || !inviteForm.email || !inviteForm.fullName || !inviteForm.roleKey}>
                {inviteBusy ? "Inviting…" : "Send invite"}
              </PButton>
            </div>
          </div>
        )}
      </PDrawer>

      {/* Self-service MFA enrollment */}
      <PDrawer open={mfaOpen} onClose={() => setMfaOpen(false)} title="Two-factor authentication">
        {mfaOpen && <MfaEnrollPanel onDone={() => { setMfaOpen(false); void reload(); }} />}
      </PDrawer>

      <PConfirm
        open={confirmDisable !== null}
        title={`Disable ${String(confirmDisable?.fullName ?? "operator")}?`}
        body="Their sessions are revoked immediately and they cannot sign in until re-enabled. The last active Super Admin cannot be disabled."
        confirmLabel="Disable operator"
        busy={busyId !== null}
        onClose={() => setConfirmDisable(null)}
        onConfirm={() => {
          const target = confirmDisable;
          setConfirmDisable(null);
          if (target) void run(Number(target.id), () => platformApi.setPlatformAdminStatus(Number(target.id), "Disabled"));
        }}
      />
    </div>
  );
}
