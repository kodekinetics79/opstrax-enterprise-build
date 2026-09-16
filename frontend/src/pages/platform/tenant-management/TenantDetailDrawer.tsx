import { useEffect, useMemo, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, CircleAlert } from "lucide-react";
import type { AnyRecord } from "@/types";
import { formatMoney, platformApi } from "@/services/platformApi";
import { PBadge, PButton, PConfirm, PDrawer, PField, PInput, PLoading, PSelect } from "../ui";
import type { GatedModule } from "./tenantManagementModel";
import { TenantBillingPlanSection } from "./TenantBillingPlanSection";

const isEmail = (value: string) => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value);

function ActivationLinkPanel({ email, url, token, emailSent, onDismiss }: {
  email: string; url?: string; token?: string; emailSent: boolean; onDismiss: () => void;
}) {
  const [copied, setCopied] = useState(false);
  if (!url && !token) return null;
  return (
    <div className={`rounded-[14px] border p-4 ${emailSent ? "border-emerald-200 bg-emerald-50" : "border-amber-200 bg-amber-50"}`}>
      <p className={`text-sm font-bold ${emailSent ? "text-emerald-800" : "text-amber-800"}`}>
        {emailSent ? `Activation email sent to ${email}` : "Activation link — deliver this manually"}
      </p>
      <p className={`mt-1 text-xs leading-5 ${emailSent ? "text-emerald-700" : "text-amber-700"}`}>
        {emailSent
          ? "The link below is your backup copy. It is valid for 7 days and is shown only once."
          : `Email is not configured, so nothing was sent to ${email}. Give them this link — it is valid for 7 days and is shown only once.`}
      </p>
      {url ? (
        <div className="mt-3 flex items-center gap-2">
          <code className="min-w-0 flex-1 truncate rounded-lg border border-slate-200 bg-white px-3 py-2 text-xs text-slate-700">{url}</code>
          <PButton variant="ghost" onClick={() => { void navigator.clipboard.writeText(url).then(() => setCopied(true)); }}>
            {copied ? "Copied" : "Copy"}
          </PButton>
        </div>
      ) : (
        <p className="mt-3 text-xs leading-5 text-amber-800">
          No tenant application URL is configured, so a full link cannot be built. Set one under
          Email &amp; SMTP → Application URLs, then re-issue the invite.
        </p>
      )}
      <div className="mt-3 flex justify-end">
        <PButton variant="ghost" onClick={onDismiss}>Dismiss</PButton>
      </div>
    </div>
  );
}

export function TenantDetailDrawer({ id, packages, canManage, canOffboard, canEntitlements, gatedModules, onClose, onChanged }: {
  id: number; packages: AnyRecord[]; canManage: boolean; canOffboard: boolean; canEntitlements: boolean; gatedModules: GatedModule[];
  onClose: () => void; onChanged: () => void;
}) {
  const qc = useQueryClient();
  const { data, isLoading } = useQuery({ queryKey: ["platform", "tenant", id], queryFn: () => platformApi.tenant(id) });
  const [busy, setBusy] = useState(false);
  const [assignPkg, setAssignPkg] = useState("");
  const [confirm, setConfirm] = useState<"suspend" | "cancel" | "revoke" | "delete" | null>(null);
  const [seatEdit, setSeatEdit] = useState("");
  const [regionEdit, setRegionEdit] = useState("");
  const [inviteEmail, setInviteEmail] = useState("");
  const [notice, setNotice] = useState<string | null>(null);
  const [snapshotBusy, setSnapshotBusy] = useState(false);
  const snapshotBaselineKey = `opstrax:platform:control-snapshot-baseline:${id}`;
  const [snapshotBaseline, setSnapshotBaseline] = useState<{ semanticSha256: string; snapshotSha256: string } | null>(() => {
    try {
      const raw = sessionStorage.getItem(snapshotBaselineKey);
      return raw ? JSON.parse(raw) : null;
    } catch { return null; }
  });
  useEffect(() => {
    try {
      const raw = sessionStorage.getItem(snapshotBaselineKey);
      setSnapshotBaseline(raw ? JSON.parse(raw) : null);
    } catch { setSnapshotBaseline(null); }
  }, [snapshotBaselineKey]);
  const [policyChange, setPolicyChange] = useState<"legacy_allow" | "package_allowlist" | null>(null);

  // Server-driven country list (shared cache with the create drawer). Changing a
  // tenant's region re-runs the country cascade: currency/timezone defaults plus
  // country-gated modules (e.g. Saudi Readiness) follow the reassignment.
  const { data: countryProfiles } = useQuery({ queryKey: ["platform", "country-profiles"], queryFn: platformApi.countryProfiles });
  const countryOptions = (countryProfiles ?? []) as AnyRecord[];

  const reload = () => { qc.invalidateQueries({ queryKey: ["platform", "tenant", id] }); onChanged(); };

  // Full company-profile edit form, hydrated once the tenant loads.
  const [edit, setEdit] = useState<Record<string, string>>({});
  useEffect(() => {
    const t = data?.tenant as AnyRecord | undefined;
    if (!t) return;
    setEdit({
      name: String(t.name ?? ""),
      legalName: String(t.legalName ?? ""),
      industry: String(t.industry ?? ""),
      website: String(t.website ?? ""),
      fleetSize: t.fleetSize != null ? String(t.fleetSize) : "",
      taxId: String(t.taxId ?? ""),
      primaryContactName: String(t.primaryContactName ?? ""),
      primaryContactEmail: String(t.primaryContactEmail ?? ""),
      primaryContactPhone: String(t.primaryContactPhone ?? ""),
      billingEmail: String(t.billingEmail ?? ""),
      billingCycle: String(t.billingCycle ?? "monthly"),
    });
  }, [data]);
  const setF = (k: string, v: string) => setEdit((f) => ({ ...f, [k]: v }));

  // Tenant user directory + platform-initiated password reset.
  const usersQ = useQuery({ queryKey: ["platform", "tenant", id, "users"], queryFn: () => platformApi.tenantUsers(id) });
  const tenantUsers = ((usersQ.data as AnyRecord)?.users ?? []) as AnyRecord[];
  const assignableRoles = ((usersQ.data as AnyRecord)?.roles ?? []) as AnyRecord[];
  const reloadUsers = () => qc.invalidateQueries({ queryKey: ["platform", "tenant", id, "users"] });
  const [resetBusyId, setResetBusyId] = useState<number | null>(null);
  const [tempPw, setTempPw] = useState<AnyRecord | null>(null);
  const [copied, setCopied] = useState(false);
  const [editingUserId, setEditingUserId] = useState<number | null>(null);
  const [addUserOpen, setAddUserOpen] = useState(false);

  const resetUserPassword = async (userId: number) => {
    setResetBusyId(userId); setNotice(null); setTempPw(null); setCopied(false);
    try {
      setTempPw(await platformApi.resetTenantUserPassword(id, userId));
    } catch (e) {
      setNotice(e instanceof Error ? e.message : "Password reset failed");
    } finally { setResetBusyId(null); }
  };

  // Holds the one-time activation artifact returned by the last invite so it can be copied.
  // Cleared explicitly by the operator, never on reload — losing it would mean re-issuing the
  // invite just to get the link back.
  const [issuedInvite, setIssuedInvite] = useState<
    { email: string; url?: string; token?: string; emailSent: boolean } | null>(null);

  const act = async (fn: () => Promise<unknown>, done?: string) => {
    setBusy(true); setNotice(null);
    try { await fn(); reload(); if (done) setNotice(done); }
    catch (e) { setNotice(e instanceof Error ? e.message : "Action failed"); }
    finally { setBusy(false); setConfirm(null); }
  };

  const tenant = (data?.tenant ?? {}) as AnyRecord;
  const entitlements = (data?.entitlements ?? []) as AnyRecord[];
  const entMap = new Map(entitlements.map((e) => [String(e.moduleKey), e]));
  const tenantCode = String(tenant.companyCode ?? "");
  const policyMode = String(tenant.entitlementPolicyMode ?? "legacy_allow") as "legacy_allow" | "package_allowlist";
  const hasClientIdentity = Boolean(String(tenant.name ?? "").trim())
    && Boolean(String(tenant.primaryContactEmail ?? "").trim())
    && !/\b(demo|synthetic|test)\b/i.test(String(tenant.name ?? ""));
  const hasActiveClientAdmin = tenantUsers.some((user) =>
    isAdminRole(String(user.roleName ?? ""))
    && String(user.status ?? "").toLowerCase() === "active"
    && user.hasPassword !== false,
  );
  const pocReadinessChecks = [
    { label: "Client identity and contact", pass: hasClientIdentity },
    { label: "Deny-by-default package access", pass: policyMode === "package_allowlist" },
    { label: "Scoped package assigned", pass: Boolean(String(tenant.packageName ?? "").trim()) },
    { label: "Operating region selected", pass: Boolean(String(tenant.country ?? "").trim()) },
    { label: "Active client administrator", pass: !usersQ.isLoading && hasActiveClientAdmin },
  ];
  const pocReadinessPassed = pocReadinessChecks.filter((check) => check.pass).length;

  const captureControlSnapshot = async () => {
    setSnapshotBusy(true); setNotice(null);
    try {
      const evidence = await platformApi.captureTenantControlSnapshot(id);
      const blob = new Blob([JSON.stringify(evidence, null, 2)], { type: "application/json" });
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement("a");
      anchor.href = url;
      anchor.download = `${tenantCode || `tenant-${id}`}-control-snapshot-${new Date().toISOString().replace(/[:.]/g, "-")}.json`;
      anchor.click();
      URL.revokeObjectURL(url);
      const snapshot = (evidence.snapshot ?? {}) as AnyRecord;
      const semanticComparison = (snapshot.semanticComparison ?? {}) as AnyRecord;
      const current = {
        semanticSha256: String(evidence.semanticSha256 ?? semanticComparison.semanticSha256 ?? ""),
        snapshotSha256: String(evidence.snapshotSha256 ?? "unavailable"),
      };
      if (!current.semanticSha256) {
        setNotice(`Audited control snapshot captured · SHA-256 ${current.snapshotSha256} · semantic comparison unavailable`);
      } else if (!snapshotBaseline) {
        sessionStorage.setItem(snapshotBaselineKey, JSON.stringify(current));
        setSnapshotBaseline(current);
        setNotice(`Audited baseline captured · evidence SHA-256 ${current.snapshotSha256} · semantic SHA-256 ${current.semanticSha256}`);
      } else {
        const unchanged = snapshotBaseline.semanticSha256 === current.semanticSha256;
        setNotice(
          `${unchanged ? "No semantic control drift" : "Semantic control drift detected"} · ` +
          `baseline ${snapshotBaseline.semanticSha256} · current ${current.semanticSha256} · evidence ${current.snapshotSha256}`,
        );
      }
    } catch (e) {
      setNotice(e instanceof Error ? e.message : "Control snapshot capture failed");
    } finally { setSnapshotBusy(false); }
  };

  return (
    <PDrawer open onClose={onClose} title={isLoading ? "Loading…" : String(tenant.name ?? "Tenant")}>
      {isLoading ? <PLoading /> : (
        <div className="space-y-6">
          <div className="flex items-center gap-2">
            <PBadge value={tenant.status} />
            <span className="text-xs text-slate-500">{String(tenant.companyCode ?? "")}</span>
          </div>

          <div className="grid grid-cols-2 gap-3 text-sm">
            <Info label="Package" value={String(tenant.packageName ?? "—")} />
            <Info label="Access policy" value={policyMode === "package_allowlist" ? "Package allowlist" : "Legacy allow"} />
            <Info label="MRR" value={formatMoney(Number(tenant.mrrCents))} />
            <Info label="Seat limit" value={String(tenant.seatLimit ?? "—")} />
            <Info label="Users" value={String(tenant.userCount ?? 0)} />
            <Info label="Operating region" value={tenant.country ? `${String(tenant.country)} · ${String(tenant.currency ?? "")}` : "Not set"} />
            <Info label="Account owner" value={String(tenant.accountOwner ?? "—")} />
            <Info label="Support owner" value={String(tenant.supportOwner ?? "—")} />
            <Info label="Fleet size" value={tenant.fleetSize != null ? `${String(tenant.fleetSize)} vehicles` : "—"} />
            <Info label="Billing cycle" value={String(tenant.billingCycle ?? "—")} />
            <Info label="Primary contact" value={String(tenant.primaryContactName ?? "—")} />
            <Info label="Contact email" value={String(tenant.primaryContactEmail ?? "—")} />
            <Info label="Billing email" value={String(tenant.billingEmail ?? "—")} />
            <Info label="Tax / VAT ID" value={String(tenant.taxId ?? "—")} />
            <Info label="Trial ends" value={String(tenant.trialEndsAt ?? "—").slice(0, 10) || "—"} />
            <Info label="Contract end" value={String(tenant.contractEnd ?? "—").slice(0, 10) || "—"} />
          </div>

          <section className="rounded-xl border border-slate-200 bg-white p-4" data-testid="client-poc-preflight">
            <div className="flex flex-wrap items-start justify-between gap-2">
              <div>
                <h3 className="text-xs font-bold uppercase tracking-wider text-slate-700">Independent client POC preflight</h3>
                <p className="mt-1 text-xs leading-5 text-slate-500">
                  Automated account controls for a clean client workspace. Do not use a demo or test tenant for client acceptance.
                </p>
              </div>
              <span className={`rounded-full border px-2.5 py-1 text-[11px] font-bold tabular-nums ${
                pocReadinessPassed === pocReadinessChecks.length
                  ? "border-emerald-200 bg-emerald-50 text-emerald-700"
                  : "border-amber-200 bg-amber-50 text-amber-800"
              }`}>
                {pocReadinessPassed}/{pocReadinessChecks.length} setup controls
              </span>
            </div>
            <div className="mt-3 grid gap-2 sm:grid-cols-2">
              {pocReadinessChecks.map((check) => (
                <div key={check.label} className="flex items-center gap-2 rounded-lg border border-slate-100 bg-slate-50/70 px-3 py-2 text-xs">
                  {check.pass
                    ? <CheckCircle2 className="h-4 w-4 shrink-0 text-emerald-600" />
                    : <CircleAlert className="h-4 w-4 shrink-0 text-amber-600" />}
                  <span className={check.pass ? "font-semibold text-slate-700" : "font-semibold text-amber-900"}>{check.label}</span>
                </div>
              ))}
            </div>
            <div className="mt-3 rounded-lg border border-blue-100 bg-blue-50/70 px-3 py-2 text-xs leading-5 text-blue-900">
              Final POC GO also requires a delivered client invitation, approved real client data, and a signed-in exact-SHA browser smoke test. Capture the audited control snapshot immediately before handover.
            </div>
          </section>

          <section className="rounded-xl border border-teal-200 bg-teal-50/60 p-4">
            <h3 className="mb-1 text-xs font-bold uppercase tracking-wider text-teal-700">Release control evidence</h3>
            <p className="mb-3 text-xs leading-5 text-teal-800/80">
              Capture a server-generated, SHA-256 identified JSON snapshot of lifecycle, package policy,
              effective entitlements, role grants, pseudonymous user-to-branch bindings, market packs,
              flags, connector readiness, environment posture and recent Platform audit IDs. Secrets
              and actor/user PII are excluded. Repeat capture compares semantic controls while ignoring
              timestamps and audit-capture drift.
            </p>
            <div className="flex flex-wrap gap-2">
              <PButton variant="ghost" disabled={snapshotBusy} onClick={captureControlSnapshot}>
                {snapshotBusy ? "Capturing…" : snapshotBaseline ? "Capture and compare snapshot" : "Capture audited control snapshot"}
              </PButton>
              {snapshotBaseline && (
                <PButton variant="ghost" disabled={snapshotBusy} onClick={() => {
                  sessionStorage.removeItem(snapshotBaselineKey);
                  setSnapshotBaseline(null);
                  setNotice("Snapshot comparison baseline cleared; the next capture becomes the baseline.");
                }}>
                  Clear comparison baseline
                </PButton>
              )}
            </div>
          </section>

          {notice && (
            <div className="rounded-xl border border-teal-500/30 bg-teal-500/5 px-4 py-2.5 text-sm text-teal-700">{notice}</div>
          )}

          {canManage && (
            <section>
              <h3 className="mb-2 text-xs font-bold uppercase tracking-wider text-slate-400">Tenant details</h3>
              <div className="space-y-3">
                <PField label="Company name"><PInput value={edit.name ?? ""} onChange={(e) => setF("name", e.target.value)} /></PField>
                <PField label="Legal entity name"><PInput value={edit.legalName ?? ""} onChange={(e) => setF("legalName", e.target.value)} /></PField>
                <div className="grid grid-cols-2 gap-3">
                  <PField label="Industry"><PInput value={edit.industry ?? ""} onChange={(e) => setF("industry", e.target.value)} /></PField>
                  <PField label="Fleet size"><PInput type="number" min={0} value={edit.fleetSize ?? ""} onChange={(e) => setF("fleetSize", e.target.value)} /></PField>
                </div>
                <div className="grid grid-cols-2 gap-3">
                  <PField label="Website"><PInput value={edit.website ?? ""} onChange={(e) => setF("website", e.target.value)} /></PField>
                  <PField label="Tax / VAT ID"><PInput value={edit.taxId ?? ""} onChange={(e) => setF("taxId", e.target.value)} /></PField>
                </div>
                <PField label="Primary contact"><PInput value={edit.primaryContactName ?? ""} onChange={(e) => setF("primaryContactName", e.target.value)} /></PField>
                <div className="grid grid-cols-2 gap-3">
                  <PField label="Contact email"><PInput type="email" value={edit.primaryContactEmail ?? ""} onChange={(e) => setF("primaryContactEmail", e.target.value)} /></PField>
                  <PField label="Contact phone"><PInput value={edit.primaryContactPhone ?? ""} onChange={(e) => setF("primaryContactPhone", e.target.value)} /></PField>
                </div>
                <div className="grid grid-cols-2 gap-3">
                  <PField label="Billing email"><PInput type="email" value={edit.billingEmail ?? ""} onChange={(e) => setF("billingEmail", e.target.value)} /></PField>
                  <PField label="Billing cycle">
                    <PSelect value={edit.billingCycle ?? "monthly"} onChange={(e) => setF("billingCycle", e.target.value)}>
                      <option value="monthly">Monthly</option>
                      <option value="annual">Annual</option>
                    </PSelect>
                  </PField>
                </div>
                <PButton
                  disabled={busy || !edit.name?.trim()}
                  onClick={() => act(() => platformApi.updateTenant(id, {
                    name: edit.name || undefined,
                    legalName: edit.legalName || undefined,
                    industry: edit.industry || undefined,
                    website: edit.website || undefined,
                    fleetSize: edit.fleetSize ? Number(edit.fleetSize) : undefined,
                    taxId: edit.taxId || undefined,
                    primaryContactName: edit.primaryContactName || undefined,
                    primaryContactEmail: edit.primaryContactEmail || undefined,
                    primaryContactPhone: edit.primaryContactPhone || undefined,
                    billingEmail: edit.billingEmail || undefined,
                    billingCycle: edit.billingCycle || undefined,
                  }), "Tenant details saved")}
                >
                  Save details
                </PButton>
              </div>
            </section>
          )}

          {/* Tenant users — full 360 administration. A platform operator can correct a
              sign-in email, change a role, disable an account, hand over a temporary
              password or re-arm an invite, without anyone inside the tenant needing to
              be able to log in first. */}
          <section>
            <div className="mb-2 flex items-center justify-between">
              <h3 className="text-xs font-bold uppercase tracking-wider text-slate-400">
                Tenant users &amp; access
              </h3>
              {canManage && (
                <PButton variant="ghost" onClick={() => { setAddUserOpen((v) => !v); setEditingUserId(null); }}>
                  {addUserOpen ? "Cancel" : "Add user"}
                </PButton>
              )}
            </div>

            {tempPw && (
              <div className="mb-3 rounded-xl border border-amber-300 bg-amber-50 p-3">
                <p className="text-xs font-bold text-amber-800">Temporary password — shown only once</p>
                <p className="mt-1 text-[11px] text-amber-700">Give this to {String(tempPw.email)}. They should change it after signing in.</p>
                <div className="mt-2 flex items-center gap-2">
                  <code className="flex-1 truncate rounded-lg border border-amber-300 bg-white px-2 py-1.5 font-mono text-sm text-slate-900">{String(tempPw.temporaryPassword)}</code>
                  <PButton variant="ghost" onClick={() => { void navigator.clipboard?.writeText(String(tempPw.temporaryPassword)); setCopied(true); }}>
                    {copied ? "Copied" : "Copy"}
                  </PButton>
                </div>
              </div>
            )}

            {addUserOpen && canManage && (
              <AddTenantUserForm
                tenantId={id}
                roles={assignableRoles}
                onDone={(result, msg) => {
                  setAddUserOpen(false);
                  setTempPw(result); setCopied(false);
                  setNotice(msg);
                  reloadUsers(); reload();
                }}
              />
            )}

            {usersQ.isLoading ? <p className="text-xs text-slate-500">Loading users…</p>
              : tenantUsers.length === 0 ? <p className="text-xs text-slate-500">No users in this tenant yet.</p>
              : (
                <div className="space-y-2">
                  {tenantUsers.map((u) => (
                    <div key={String(u.id)} className="rounded-xl border border-slate-200 bg-white px-3 py-2.5">
                      <div className="flex items-center justify-between gap-3">
                        <div className="min-w-0">
                          <p className="truncate text-sm font-medium text-slate-800">
                            {String(u.fullName ?? "—")}
                            {isAdminRole(String(u.roleName ?? "")) && (
                              <span className="ml-2 rounded bg-teal-50 px-1.5 py-0.5 text-[10px] font-bold uppercase text-teal-700">Admin</span>
                            )}
                          </p>
                          <p className="truncate text-[11px] text-slate-500">
                            {String(u.email ?? "")} · {String(u.roleName ?? "—")} · {String(u.status ?? "")}
                            {Number(u.activeSessions ?? 0) > 0 && ` · ${Number(u.activeSessions)} live session${Number(u.activeSessions) === 1 ? "" : "s"}`}
                            {u.hasPassword === false && " · no password set"}
                          </p>
                        </div>
                        {canManage && (
                          <div className="flex shrink-0 gap-1">
                            <PButton
                              variant="ghost"
                              onClick={() => { setEditingUserId(editingUserId === Number(u.id) ? null : Number(u.id)); setAddUserOpen(false); }}
                            >
                              {editingUserId === Number(u.id) ? "Close" : "Edit"}
                            </PButton>
                            <PButton variant="ghost" disabled={busy || resetBusyId === Number(u.id)}
                              onClick={() => resetUserPassword(Number(u.id))}>
                              {resetBusyId === Number(u.id) ? "Resetting…" : "Reset password"}
                            </PButton>
                          </div>
                        )}
                      </div>

                      {canManage && editingUserId === Number(u.id) && (
                        <EditTenantUserForm
                          tenantId={id}
                          user={u}
                          roles={assignableRoles}
                          onDone={(msg) => { setEditingUserId(null); setNotice(msg); reloadUsers(); reload(); }}
                          onInviteSent={(msg) => setNotice(msg)}
                        />
                      )}
                    </div>
                  ))}
                </div>
              )}
          </section>

          {/* Commercial terms — what this tenant is actually charged, feature by feature */}
          <TenantBillingPlanSection tenantId={id} canManage={canManage} onNotice={setNotice} />

          {canManage && (
            <section>
              <h3 className="mb-2 text-xs font-bold uppercase tracking-wider text-slate-400">Subscription actions</h3>
              <div className="flex flex-wrap gap-2">
                <PButton variant="ghost" disabled={busy} onClick={() => act(() => platformApi.tenantStatus(id, { action: "activate" }), "Tenant activated")}>Activate</PButton>
                <PButton variant="ghost" disabled={busy} onClick={() => act(() => platformApi.tenantStatus(id, { action: "extend-trial", days: 14 }), "Trial extended 14 days")}>Extend trial +14d</PButton>
                <PButton variant="ghost" disabled={busy} onClick={() => setConfirm("suspend")}>Suspend</PButton>
                <PButton variant="danger" disabled={busy} onClick={() => setConfirm("cancel")}>Cancel</PButton>
              </div>
            </section>
          )}

          {canManage && (
            <section>
              <h3 className="mb-2 text-xs font-bold uppercase tracking-wider text-slate-400">Limits</h3>
              <div className="flex gap-2">
                <PInput
                  type="number"
                  min={1}
                  value={seatEdit}
                  onChange={(e) => setSeatEdit(e.target.value)}
                  placeholder={`Seat limit (current: ${String(tenant.seatLimit ?? "—")})`}
                  aria-label="Seat limit"
                />
                <PButton
                  disabled={busy || !seatEdit || Number(seatEdit) < 1}
                  onClick={() => act(() => platformApi.updateTenant(id, { seatLimit: Number(seatEdit) }), "Seat limit updated")}
                >
                  Save
                </PButton>
              </div>
            </section>
          )}

          {canManage && (
            <section>
              <h3 className="mb-2 text-xs font-bold uppercase tracking-wider text-slate-400">Operating region</h3>
              <div className="flex gap-2">
                <PSelect value={regionEdit || String(tenant.country ?? "")} onChange={(e) => setRegionEdit(e.target.value)} aria-label="Operating region">
                  <option value="">— Not set —</option>
                  {countryOptions.map((c) => (
                    <option key={String(c.countryCode)} value={String(c.countryCode)}>
                      {String(c.countryName)} ({String(c.countryCode)})
                    </option>
                  ))}
                </PSelect>
                <PButton
                  disabled={busy || !regionEdit || regionEdit === String(tenant.country ?? "")}
                  onClick={() => act(() => platformApi.updateTenant(id, { countryCode: regionEdit }), "Operating region updated — tenant users see region modules after next login")}
                >
                  Save
                </PButton>
              </div>
              <p className="mt-1.5 text-[11px] text-slate-500">
                Applies the country profile cascade: default currency, timezone and country
                features. Region-scoped modules (e.g. Saudi Readiness) follow this setting.
              </p>
            </section>
          )}

          {canManage && (
            <section>
              <h3 className="mb-2 text-xs font-bold uppercase tracking-wider text-slate-400">Tenant admin & sessions</h3>
              <div className="flex gap-2">
                <PInput
                  type="email"
                  value={inviteEmail}
                  onChange={(e) => setInviteEmail(e.target.value)}
                  placeholder="admin@tenant.com"
                  aria-label="Tenant admin email"
                />
                <PButton
                  disabled={busy || !inviteEmail.includes("@")}
                  onClick={() => act(async () => {
                    const res = await platformApi.resetInvite(id, { adminEmail: inviteEmail }) as AnyRecord;
                    setIssuedInvite({
                      email: inviteEmail,
                      url: res?.activationUrl ? String(res.activationUrl) : undefined,
                      token: res?.activationToken ? String(res.activationToken) : undefined,
                      emailSent: res?.emailSent === true,
                    });
                  })}
                >
                  Invite / Reset admin
                </PButton>
              </div>
              {issuedInvite && (
                <div className="mt-3">
                  <ActivationLinkPanel {...issuedInvite} onDismiss={() => setIssuedInvite(null)} />
                </div>
              )}
              <div className="mt-2">
                <PButton variant="ghost" disabled={busy} onClick={() => setConfirm("revoke")}>
                  Revoke all tenant sessions
                </PButton>
              </div>
            </section>
          )}

          {canEntitlements && (
            <section className="rounded-xl border border-amber-200 bg-amber-50/60 p-4">
              <h3 className="mb-1 text-xs font-bold uppercase tracking-wider text-amber-700">Commercial access policy</h3>
              <p className="mb-3 text-xs leading-5 text-amber-800/80">
                Package allowlist denies every governed module without an enabled entitlement. Legacy allow preserves inherited access when no row exists. Changes take effect at the API edge immediately and are audited.
              </p>
              <PSelect
                value={policyChange ?? policyMode}
                aria-label="Commercial access policy"
                onChange={(e) => setPolicyChange(e.target.value as "legacy_allow" | "package_allowlist")}
                disabled={busy}
              >
                <option value="package_allowlist">Package allowlist (deny by default)</option>
                <option value="legacy_allow">Legacy allow (compatibility)</option>
              </PSelect>
            </section>
          )}

          {canManage && (
            <section>
              <h3 className="mb-2 text-xs font-bold uppercase tracking-wider text-slate-400">Assign package</h3>
              <div className="flex gap-2">
                <PSelect value={assignPkg} onChange={(e) => setAssignPkg(e.target.value)}>
                  <option value="">— Select package —</option>
                  {packages.map((p) => <option key={String(p.id)} value={String(p.id)}>{String(p.name)}</option>)}
                </PSelect>
                <PButton disabled={busy || !assignPkg} onClick={() => act(() => platformApi.assignPackage(id, { packageId: Number(assignPkg) }))}>Assign</PButton>
              </div>
            </section>
          )}

          <section>
            <h3 className="mb-1 text-xs font-bold uppercase tracking-wider text-slate-500">Feature entitlements</h3>
            <p className="mb-3 text-xs leading-5 text-slate-500">
              Controls which product modules this tenant may use. <strong className="font-semibold text-slate-700">Server-enforced</strong> —
              turning one off makes every API route it owns return <span className="font-mono">403</span> immediately, even if called
              directly outside the UI. Missing rows follow the access policy: {policyMode === "package_allowlist"
                ? <strong className="font-semibold text-slate-700">off by default</strong>
                : <strong className="font-semibold text-slate-700">on by default</strong>}.
            </p>
            <div className="space-y-2">
              {gatedModules.map((m) => {
                const ent = entMap.get(m.key);
                const enabled = ent ? Boolean(ent.enabled) : policyMode === "legacy_allow";
                return (
                  <div key={m.key} className="flex items-center justify-between gap-3 rounded-xl border border-slate-200 bg-white px-4 py-2.5">
                    <div className="min-w-0">
                      <p className="text-sm font-semibold text-slate-800">{m.label}</p>
                      <p className="text-[11px] leading-4 text-slate-500">{m.blurb}</p>
                      <p className="mt-0.5 text-[11px] font-medium text-slate-400">
                        {ent ? `${String(ent.source)} · ${String(ent.tier)}` : policyMode === "legacy_allow" ? "inherited (legacy default on)" : "not in plan (default off)"}
                      </p>
                    </div>
                    <button
                      disabled={!canEntitlements || busy}
                      onClick={() => act(() => platformApi.setEntitlement(id, { moduleKey: m.key, enabled: !enabled }))}
                      className={`relative h-6 w-11 shrink-0 rounded-full transition disabled:opacity-40 ${enabled ? "bg-teal-500" : "bg-slate-300"}`}
                      aria-label={`${enabled ? "Disable" : "Enable"} ${m.label}`}
                    >
                      <span className={`absolute top-0.5 h-5 w-5 rounded-full bg-white shadow transition ${enabled ? "left-[22px]" : "left-0.5"}`} />
                    </button>
                  </div>
                );
              })}
            </div>
          </section>

          {canOffboard && (
            <section className="rounded-xl border border-red-200 bg-red-50/60 p-4">
              <h3 className="mb-1 text-xs font-bold uppercase tracking-wider text-red-600">Danger zone</h3>
              <p className="mb-3 text-xs text-red-600/90">
                Permanently deletes this tenant and ALL its data (jobs, vehicles, drivers, users, records). Irreversible.
              </p>
              <PButton variant="danger" disabled={busy} onClick={() => setConfirm("delete")}>Delete tenant permanently</PButton>
            </section>
          )}

          <PConfirm
            open={policyChange !== null && policyChange !== policyMode}
            title={policyChange === "package_allowlist" ? "Enable deny-by-default package access?" : "Restore legacy inherited access?"}
            body={policyChange === "package_allowlist"
              ? <>Modules outside the assigned package or explicit grants will be blocked immediately. Current package-derived rights will be reconciled.</>
              : <>Missing entitlement rows will become accessible immediately. Use this compatibility mode only for a reviewed legacy tenant.</>}
            confirmLabel={policyChange === "package_allowlist" ? "Enable package allowlist" : "Enable legacy access"}
            busy={busy}
            onConfirm={() => policyChange && act(
              () => platformApi.setEntitlementPolicy(id, policyChange),
              `Access policy changed to ${policyChange === "package_allowlist" ? "package allowlist" : "legacy allow"}`,
            ).finally(() => setPolicyChange(null))}
            onClose={() => setPolicyChange(null)}
          />
          <PConfirm
            open={confirm === "delete"}
            title="Permanently delete this tenant?"
            body={<>This purges <strong>{String(tenant.name ?? "this tenant")}</strong> and every record it owns. This cannot be undone. Type the tenant code to confirm.</>}
            confirmLabel="Delete tenant"
            confirmText={tenantCode || undefined}
            busy={busy}
            onConfirm={() => act(async () => { await platformApi.deleteTenant(id, tenantCode); onClose(); }, "Tenant deleted")}
            onClose={() => setConfirm(null)}
          />
          <PConfirm
            open={confirm === "suspend"}
            title="Suspend this tenant?"
            body={<>All users of <strong>{String(tenant.name ?? "this tenant")}</strong> will be locked out immediately and every active session revoked. You can reactivate at any time.</>}
            confirmLabel="Suspend tenant"
            busy={busy}
            onConfirm={() => act(() => platformApi.tenantStatus(id, { action: "suspend" }), "Tenant suspended — sessions revoked")}
            onClose={() => setConfirm(null)}
          />
          <PConfirm
            open={confirm === "cancel"}
            title="Cancel this tenant's subscription?"
            body={<>Cancelling locks out all users and revokes every session. Tenant data is retained. This is a commercial off-switch — reactivation requires platform action.</>}
            confirmLabel="Cancel subscription"
            confirmText={tenantCode || undefined}
            busy={busy}
            onConfirm={() => act(() => platformApi.tenantStatus(id, { action: "cancel" }), "Tenant cancelled — sessions revoked")}
            onClose={() => setConfirm(null)}
          />
          <PConfirm
            open={confirm === "revoke"}
            title="Revoke all tenant sessions?"
            body={<>Every logged-in user of <strong>{String(tenant.name ?? "this tenant")}</strong> is signed out immediately. The subscription status does not change.</>}
            confirmLabel="Revoke sessions"
            busy={busy}
            onConfirm={() => act(() => platformApi.revokeSessions(id), "All tenant sessions revoked")}
            onClose={() => setConfirm(null)}
          />
        </div>
      )}
    </PDrawer>
  );
}

function Info({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl border border-slate-200 bg-slate-50 px-3 py-2.5">
      <p className="text-[10px] uppercase tracking-wider text-slate-500">{label}</p>
      <p className="mt-0.5 text-sm font-medium text-slate-800">{value}</p>
    </div>
  );
}

// Roles carrying tenant-wide authority — mirrors PlatformEndpoints.TenantAdminRoles.
// Used only for labelling here; the server enforces the last-admin guard.
const TENANT_ADMIN_ROLES = ["tenant admin", "company admin", "super admin", "reseller / partner admin"];

function isAdminRole(roleName: string) {
  return TENANT_ADMIN_ROLES.includes(roleName.trim().toLowerCase());
}

function AddTenantUserForm({ tenantId, roles, onDone }: {
  tenantId: number;
  roles: AnyRecord[];
  onDone: (result: AnyRecord, message: string) => void;
}) {
  const [form, setForm] = useState({ fullName: "", email: "", roleName: "Tenant Admin" });
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const valid = form.fullName.trim() !== "" && isEmail(form.email.trim());

  const submit = async () => {
    setBusy(true); setErr(null);
    try {
      const res = await platformApi.createTenantUser(tenantId, {
        fullName: form.fullName.trim(),
        email: form.email.trim(),
        roleName: form.roleName,
      });
      onDone(res, `${form.email.trim()} created — copy the temporary password now`);
    } catch (e) { setErr(e instanceof Error ? e.message : "Could not create the user"); }
    finally { setBusy(false); }
  };

  return (
    <div className="mb-3 rounded-xl border border-slate-200 bg-slate-50 p-3">
      <p className="mb-2 text-xs font-bold uppercase tracking-wider text-slate-500">New user</p>
      {err && <p className="mb-2 text-xs font-medium text-red-600">{err}</p>}
      <div className="space-y-3">
        <PField label="Full name">
          <PInput value={form.fullName} onChange={(e) => setForm({ ...form, fullName: e.target.value })} placeholder="Jane Doe" />
        </PField>
        <div className="grid grid-cols-2 gap-3">
          <PField label="Email (sign-in address)">
            <PInput type="email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} placeholder="jane@acme.com" />
          </PField>
          <PField label="Role">
            <PSelect value={form.roleName} onChange={(e) => setForm({ ...form, roleName: e.target.value })}>
              {roles.map((r) => <option key={String(r.name)} value={String(r.name)}>{String(r.name)}</option>)}
            </PSelect>
          </PField>
        </div>
        <p className="text-[11px] text-slate-500">
          The account is created active with a one-time password shown once here — so a locked-out customer can be
          recovered on the call, without waiting on SMTP.
        </p>
        <PButton disabled={busy || !valid} onClick={submit}>{busy ? "Creating…" : "Create user"}</PButton>
      </div>
    </div>
  );
}

function EditTenantUserForm({ tenantId, user, roles, onDone, onInviteSent }: {
  tenantId: number;
  user: AnyRecord;
  roles: AnyRecord[];
  onDone: (message: string) => void;
  onInviteSent: (message: string) => void;
}) {
  const currentEmail = String(user.email ?? "");
  const [form, setForm] = useState({
    fullName: String(user.fullName ?? ""),
    email: currentEmail,
    roleName: String(user.roleName ?? ""),
    status: String(user.status ?? "Active"),
  });
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const [issuedInvite, setIssuedInvite] = useState<{
    email: string; url?: string; token?: string; emailSent: boolean;
  } | null>(null);

  const emailChanged = form.email.trim().toLowerCase() !== currentEmail.toLowerCase();
  const valid = form.fullName.trim() !== "" && isEmail(form.email.trim());

  const save = async () => {
    setBusy(true); setErr(null);
    try {
      const res = await platformApi.updateTenantUser(tenantId, Number(user.id), {
        fullName: form.fullName.trim(),
        email: form.email.trim(),
        roleName: form.roleName,
        status: form.status,
      });
      const revoked = Number(res?.sessionsRevoked ?? 0);
      onDone(revoked > 0
        ? `User updated — ${revoked} active session${revoked === 1 ? "" : "s"} revoked`
        : "User updated");
    } catch (e) { setErr(e instanceof Error ? e.message : "Could not update the user"); }
    finally { setBusy(false); }
  };

  const resendInvite = async () => {
    setBusy(true); setErr(null);
    try {
      const res = await platformApi.resendTenantUserInvite(tenantId, Number(user.id));
      setIssuedInvite({
        email: String(res.email ?? currentEmail),
        url: res.activationUrl ? String(res.activationUrl) : undefined,
        token: res.activationToken ? String(res.activationToken) : undefined,
        emailSent: res.emailSent === true,
      });
      onInviteSent(res?.emailSent
        ? `Set-password invite emailed to ${String(res.email)}`
        : "Invite re-armed — deliver the one-time activation link shown below");
    } catch (e) { setErr(e instanceof Error ? e.message : "Could not re-send the invite"); }
    finally { setBusy(false); }
  };

  return (
    <div className="mt-3 space-y-3 border-t border-slate-200 pt-3">
      {err && <p className="text-xs font-medium text-red-600">{err}</p>}
      <PField label="Full name">
        <PInput value={form.fullName} onChange={(e) => setForm({ ...form, fullName: e.target.value })} />
      </PField>
      <PField label="Email (sign-in address)">
        <PInput type="email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} />
      </PField>
      {emailChanged && (
        <p className="rounded-lg border border-amber-200 bg-amber-50 px-2.5 py-1.5 text-[11px] text-amber-800">
          This is the address they sign in with. Saving signs them out everywhere; their password is unchanged.
        </p>
      )}
      <div className="grid grid-cols-2 gap-3">
        <PField label="Role">
          <PSelect value={form.roleName} onChange={(e) => setForm({ ...form, roleName: e.target.value })}>
            {!roles.some((r) => String(r.name) === form.roleName) && <option value={form.roleName}>{form.roleName}</option>}
            {roles.map((r) => <option key={String(r.name)} value={String(r.name)}>{String(r.name)}</option>)}
          </PSelect>
        </PField>
        <PField label="Status">
          <PSelect value={form.status} onChange={(e) => setForm({ ...form, status: e.target.value })}>
            <option value="Active">Active</option>
            <option value="Disabled">Disabled</option>
            <option value="Pending">Pending (awaiting set-password)</option>
          </PSelect>
        </PField>
      </div>
      <div className="flex flex-wrap gap-2">
        <PButton disabled={busy || !valid} onClick={save}>{busy ? "Saving…" : "Save user"}</PButton>
        <PButton variant="ghost" disabled={busy} onClick={resendInvite}>Re-send set-password invite</PButton>
      </div>
      {issuedInvite && (
        <ActivationLinkPanel {...issuedInvite} onDismiss={() => setIssuedInvite(null)} />
      )}
    </div>
  );
}

// ── Per-feature commercial terms ─────────────────────────────────────────────
// This is the flexible half of billing: any feature can be free for one customer,
// a flat fee for another, and billed per event for a third. A feature with no term
// here simply follows its package default.
