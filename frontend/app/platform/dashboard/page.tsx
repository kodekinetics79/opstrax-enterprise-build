'use client';

import { useState, useEffect, useCallback } from 'react';
import { useRouter } from 'next/navigation';
import {
  platformApi,
  type PlatformStats,
  type PlatformTenantSummary,
  type PlatformTenantDetail,
  type CreateTenantResult,
  type TenantAdminUser,
  type TenantUser,
  type PlatformAuditLog,
  type PlatformPlan,
  type SupportSession,
  type StartSupportAccessResult,
  type TenantInvoice,
} from '@/src/api/platform';
import { InfoTip } from '@/src/components/InfoTip';

const labelCls = 'mb-1 flex items-center gap-1.5 text-xs text-slate-400';

const FEATURE_KEYS = [
  { key: 'ai_assistant', label: 'AI HR Assistant' },
  { key: 'mobile_app', label: 'Mobile App' },
  { key: 'wps_export', label: 'WPS/SIF Export' },
  { key: 'eosb_calc', label: 'EOSB Calculator' },
  { key: 'resume_screening', label: 'AI Resume Screening' },
  { key: 'payroll_ai_validation', label: 'AI Payroll Validation' },
  { key: 'risk_scores', label: 'Employee Risk Scores' },
  { key: 'hijri_calendar', label: 'Hijri Calendar' },
];

function StatCard({ label, value }: { label: string; value: number | string }) {
  return (
    <div className="bg-sidebarDark border border-white/10 rounded-xl p-5">
      <p className="text-xs text-slate-500 font-medium uppercase tracking-wide">{label}</p>
      <p className="text-3xl font-bold text-white mt-1">{value}</p>
    </div>
  );
}

// ── Confirmation Modal ────────────────────────────────────────────────────────

function ConfirmModal({
  title,
  message,
  confirmLabel,
  confirmClass,
  onConfirm,
  onClose,
  requireReason,
}: {
  title: string;
  message: string;
  confirmLabel: string;
  confirmClass?: string;
  onConfirm: (reason: string) => Promise<void>;
  onClose: () => void;
  requireReason?: boolean;
}) {
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState('');

  async function handleConfirm() {
    if (requireReason && !reason.trim()) { setErr('Please enter a reason.'); return; }
    setBusy(true);
    try { await onConfirm(reason); onClose(); }
    catch (e: unknown) {
      setErr((e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? 'Action failed.');
    } finally { setBusy(false); }
  }

  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-black/70" onClick={onClose} />
      <div className="relative w-full max-w-sm bg-sidebarDark border border-white/10 rounded-2xl p-6 space-y-4">
        <h3 className="text-base font-semibold text-white">{title}</h3>
        <p className="text-sm text-slate-400">{message}</p>
        {requireReason && (
          <div>
            <label className="mb-1 block text-xs text-slate-400">Reason (required)</label>
            <input
              type="text"
              value={reason}
              onChange={e => setReason(e.target.value)}
              placeholder="Enter reason..."
              className="w-full bg-white/10 border border-white/10 rounded-lg px-3 py-2 text-sm text-white placeholder-slate-600 focus:outline-none focus:border-sapphire"
            />
          </div>
        )}
        {err && <p className="text-xs text-rose-400">{err}</p>}
        <div className="flex items-center gap-2 pt-1">
          <button
            onClick={handleConfirm}
            disabled={busy}
            className={`px-4 py-2 text-white text-xs font-medium rounded-lg disabled:opacity-50 ${confirmClass ?? 'bg-sapphire hover:opacity-90'}`}
          >
            {busy ? 'Working…' : confirmLabel}
          </button>
          <button onClick={onClose} className="px-4 py-2 bg-white/10 hover:bg-white/20 text-slate-300 text-xs font-medium rounded-lg">
            Cancel
          </button>
        </div>
      </div>
    </div>
  );
}

interface SubForm {
  plan: string;
  status: string;
  maxUsers: number;
  maxEmployees: number;
  billingEmail: string;
  billingCycle: string;
  monthlyAmount: number;
  currencyCode: string;
  startedAtUtc: string;
  expiresAtUtc: string;
}

const PLAN_DEFAULTS: Record<string, { maxUsers: number; maxEmployees: number }> = {
  Trial:      { maxUsers: 3,  maxEmployees: 10 },
  Starter:    { maxUsers: 10, maxEmployees: 50 },
  Growth:     { maxUsers: 50, maxEmployees: 250 },
  Enterprise: { maxUsers: 0,  maxEmployees: 0 },
};

const inputCls = 'w-full bg-white/10 border border-white/10 rounded-lg px-3 py-2 text-sm text-white placeholder-slate-600 focus:outline-none focus:border-sapphire';
const subInputCls = 'w-full bg-white/10 border border-white/10 rounded-lg px-2 py-2 text-sm text-white focus:outline-none focus:border-sapphire';

// ── New Client Modal ──────────────────────────────────────────────────────────

function NewClientModal({ onClose, onCreated }: { onClose: () => void; onCreated: () => void }) {
  const [form, setForm] = useState({
    name: '', slug: '', adminEmail: '', adminFullName: '', adminPassword: '',
    plan: 'Trial', maxUsers: PLAN_DEFAULTS.Trial.maxUsers, maxEmployees: PLAN_DEFAULTS.Trial.maxEmployees,
    billingEmail: '', billingCycle: 'Monthly', monthlyAmount: 0, currencyCode: 'USD', expiresAtUtc: '',
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [result, setResult] = useState<CreateTenantResult | null>(null);

  const set = (patch: Partial<typeof form>) => setForm(p => ({ ...p, ...patch }));

  function onNameChange(name: string) {
    const slug = name.toLowerCase().trim().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
    setForm(p => ({ ...p, name, slug: p.slug === '' || p.slug === slugFrom(p.name) ? slug : p.slug }));
  }
  function slugFrom(name: string) {
    return name.toLowerCase().trim().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
  }

  async function submit() {
    setError('');
    if (!form.name.trim() || !form.slug.trim()) { setError('Client name and slug are required.'); return; }
    if (!form.adminEmail.includes('@')) { setError('A valid admin email is required.'); return; }
    if (form.adminPassword.length < 10) { setError('Admin password must be at least 10 characters.'); return; }
    setSaving(true);
    try {
      const res = await platformApi.createTenant({
        name: form.name.trim(),
        slug: form.slug.trim(),
        adminEmail: form.adminEmail.trim(),
        adminFullName: form.adminFullName.trim() || undefined,
        adminPassword: form.adminPassword,
        plan: form.plan,
        maxUsers: form.maxUsers,
        maxEmployees: form.maxEmployees,
        billingEmail: form.billingEmail.trim() || undefined,
        billingCycle: form.billingCycle,
        monthlyAmount: form.monthlyAmount,
        currencyCode: form.currencyCode,
        expiresAtUtc: form.expiresAtUtc ? new Date(form.expiresAtUtc).toISOString() : null,
      });
      setResult(res);
      onCreated();
    } catch (err: unknown) {
      setError((err as { response?: { data?: { message?: string } } })?.response?.data?.message ?? 'Failed to create client.');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-black/60" onClick={onClose} />
      <div className="relative w-full max-w-xl bg-sidebarDark border border-white/10 rounded-2xl overflow-hidden flex flex-col max-h-[90vh]">
        <div className="flex items-center justify-between px-6 py-4 border-b border-white/10">
          <h2 className="text-lg font-semibold text-white">{result ? 'Client Created' : 'New Client'}</h2>
          <button onClick={onClose} className="text-slate-500 hover:text-white transition-colors text-xl leading-none">&times;</button>
        </div>

        {result ? (
          <div className="px-6 py-5 space-y-4">
            <p className="text-sm text-green-400">Client provisioned successfully with the full role set and an active {result.plan} subscription.</p>
            <div className="bg-darkSlate/60 border border-white/10 rounded-xl p-4 space-y-2 text-sm">
              <p className="text-slate-300"><span className="text-slate-500">Tenant:</span> {result.name} <span className="font-mono text-xs">/{result.slug}</span></p>
              <p className="text-slate-300"><span className="text-slate-500">Admin login:</span> {result.adminEmail}</p>
              <p className="text-slate-300"><span className="text-slate-500">Tenant slug for login:</span> <span className="font-mono">{result.slug}</span></p>
              <p className="text-xs text-amber-500 pt-1">Share the slug, email and the password you set with your client&apos;s admin. They sign in on the regular login page.</p>
            </div>
            <button onClick={onClose} className="px-4 py-2 bg-sapphire hover:opacity-90 text-white text-xs font-medium rounded-lg">Done</button>
          </div>
        ) : (
          <div className="px-6 py-5 space-y-4 overflow-y-auto">
            <div className="grid grid-cols-2 gap-3">
              <div className="col-span-2">
                <label className={labelCls}>Client / Company Name<InfoTip text="Your client's legal or trading name." /></label>
                <input type="text" value={form.name} onChange={e => onNameChange(e.target.value)} className={inputCls} placeholder="Acme Industries LLC" />
              </div>
              <div className="col-span-2">
                <label className={labelCls}>Tenant Slug (used at login)<InfoTip text="Unique workspace ID. 3-40 chars, lowercase, hyphens. Cannot be changed later." /></label>
                <input type="text" value={form.slug} onChange={e => set({ slug: e.target.value.toLowerCase() })} className={`${inputCls} font-mono`} placeholder="acme-industries" />
              </div>
              <div>
                <label className={labelCls}>Admin Email<InfoTip text="Email address for the client's first administrator." /></label>
                <input type="email" value={form.adminEmail} onChange={e => set({ adminEmail: e.target.value })} className={inputCls} placeholder="admin@acme.com" />
              </div>
              <div>
                <label className={labelCls}>Admin Full Name<InfoTip text="Display name for the administrator. Optional." /></label>
                <input type="text" value={form.adminFullName} onChange={e => set({ adminFullName: e.target.value })} className={inputCls} placeholder="Jane Doe" />
              </div>
              <div className="col-span-2">
                <label className={labelCls}>Admin Password (min 10 characters)<InfoTip text="Initial password — at least 10 characters. Advise client to change after first login." /></label>
                <input type="text" value={form.adminPassword} onChange={e => set({ adminPassword: e.target.value })} className={`${inputCls} font-mono`} placeholder="Set an initial password" />
              </div>
              <div>
                <label className={labelCls}>Plan<InfoTip text="Subscription tier. Auto-fills user/employee limits." /></label>
                <select
                  value={form.plan}
                  onChange={e => {
                    const plan = e.target.value;
                    const d = PLAN_DEFAULTS[plan];
                    set({ plan, ...(d ? { maxUsers: d.maxUsers, maxEmployees: d.maxEmployees } : {}) });
                  }}
                  className={inputCls}
                >
                  {Object.keys(PLAN_DEFAULTS).map(p => <option key={p} value={p}>{p}</option>)}
                </select>
              </div>
              <div>
                <label className={labelCls}>Billing Cycle</label>
                <select value={form.billingCycle} onChange={e => set({ billingCycle: e.target.value })} className={inputCls}>
                  <option value="Monthly">Monthly</option>
                  <option value="Annual">Annual</option>
                </select>
              </div>
              <div>
                <label className={labelCls}>Max Users (0 = unlimited)</label>
                <input type="number" min={0} value={form.maxUsers} onChange={e => set({ maxUsers: Number(e.target.value) })} className={inputCls} />
              </div>
              <div>
                <label className={labelCls}>Max Employees (0 = unlimited)</label>
                <input type="number" min={0} value={form.maxEmployees} onChange={e => set({ maxEmployees: Number(e.target.value) })} className={inputCls} />
              </div>
              <div>
                <label className={labelCls}>Monthly Amount</label>
                <input type="number" min={0} value={form.monthlyAmount} onChange={e => set({ monthlyAmount: Number(e.target.value) })} className={inputCls} />
              </div>
              <div>
                <label className={labelCls}>Expires At (blank = never)</label>
                <input type="date" value={form.expiresAtUtc} onChange={e => set({ expiresAtUtc: e.target.value })} className={inputCls} />
              </div>
            </div>
            {error && <p className="text-xs text-rose-400">{error}</p>}
            <div className="flex items-center gap-2 pt-1 pb-1">
              <button onClick={submit} disabled={saving} className="px-4 py-2 bg-sapphire hover:opacity-90 disabled:opacity-50 text-white text-xs font-medium rounded-lg">
                {saving ? 'Provisioning...' : 'Create Client'}
              </button>
              <button onClick={onClose} className="px-4 py-2 bg-white/10 hover:bg-white/20 text-slate-300 text-xs font-medium rounded-lg">Cancel</button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

// ── Tenant Panel ──────────────────────────────────────────────────────────────

type PanelTab = 'subscription' | 'features' | 'admins' | 'users' | 'support' | 'invoices';

function TenantPanel({
  tenant,
  onClose,
  onRefreshList,
}: {
  tenant: PlatformTenantSummary;
  onClose: () => void;
  onRefreshList: () => void;
}) {
  const [detail, setDetail] = useState<PlatformTenantDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [tab, setTab] = useState<PanelTab>('subscription');
  const [editingName, setEditingName] = useState(false);
  const [nameInput, setNameInput] = useState('');
  const [nameSaving, setNameSaving] = useState(false);

  // Subscription edit
  const [editingSub, setEditingSub] = useState(false);
  const [subForm, setSubForm] = useState<SubForm>({ plan: '', status: '', maxUsers: 0, maxEmployees: 0, billingEmail: '', billingCycle: '', monthlyAmount: 0, currencyCode: 'USD', startedAtUtc: '', expiresAtUtc: '' });
  const [subSaving, setSubSaving] = useState(false);
  const [subSaved, setSubSaved] = useState(false);
  const [subErr, setSubErr] = useState('');

  // Suspend / reactivate confirm
  const [confirmAction, setConfirmAction] = useState<'suspend' | 'reactivate' | null>(null);

  // Feature flags
  const [togglingFeature, setTogglingFeature] = useState<string | null>(null);

  // Admins
  const [admins, setAdmins] = useState<TenantAdminUser[]>([]);
  const [adminForm, setAdminForm] = useState({ email: '', fullName: '', password: '' });
  const [addingAdmin, setAddingAdmin] = useState(false);
  const [adminError, setAdminError] = useState('');
  const [showAddAdmin, setShowAddAdmin] = useState(false);

  // Users
  const [users, setUsers] = useState<TenantUser[]>([]);
  const [usersLoading, setUsersLoading] = useState(false);
  const [userSearch, setUserSearch] = useState('');
  const [resetUserId, setResetUserId] = useState<string | null>(null);
  const [resetResult, setResetResult] = useState<string | null>(null);

  // Invoices
  const [invoices, setInvoices] = useState<TenantInvoice[]>([]);
  const [invoicesLoading, setInvoicesLoading] = useState(false);
  const [showCreateInvoice, setShowCreateInvoice] = useState(false);
  const [invoiceForm, setInvoiceForm] = useState({ invoiceNumber: '', amount: '', currencyCode: 'USD', status: 'Draft', paymentMethod: '', periodDescription: '', invoiceDate: '', dueDate: '', notes: '' });
  const [invoiceSaving, setInvoiceSaving] = useState(false);
  const [invoiceErr, setInvoiceErr] = useState('');
  const [editingInvoice, setEditingInvoice] = useState<TenantInvoice | null>(null);
  const [editInvoiceForm, setEditInvoiceForm] = useState({ status: '', paymentMethod: '', paymentReference: '', paidDate: '', notes: '' });
  const [editInvoiceSaving, setEditInvoiceSaving] = useState(false);
  const [deletingInvoice, setDeletingInvoice] = useState<string | null>(null);

  // Support access (structured break-glass)
  const [supportUserId, setSupportUserId] = useState('');
  const [supportReason, setSupportReason] = useState('');
  const [supportSession, setSupportSession] = useState<StartSupportAccessResult | null>(null);
  const [supportBusy, setSupportBusy] = useState(false);
  const [supportCopied, setSupportCopied] = useState(false);
  const [supportSessions, setSupportSessions] = useState<SupportSession[]>([]);
  const [sessionsLoading, setSessionsLoading] = useState(false);
  const [endingSession, setEndingSession] = useState<string | null>(null);

  const loadDetail = useCallback(async () => {
    setLoading(true);
    try {
      const d = await platformApi.getTenant(tenant.id);
      setDetail(d);
      setNameInput(d.name);
      setSubForm({
        plan: d.subscription.plan,
        status: d.subscription.status,
        maxUsers: d.subscription.maxUsers ?? 0,
        maxEmployees: d.subscription.maxEmployees,
        billingEmail: d.subscription.billingEmail,
        billingCycle: d.subscription.billingCycle,
        monthlyAmount: d.subscription.monthlyAmount,
        currencyCode: d.subscription.currencyCode,
        startedAtUtc: d.subscription.startedAtUtc?.slice(0, 10) ?? '',
        expiresAtUtc: d.subscription.expiresAtUtc?.slice(0, 10) ?? '',
      });
    } catch { /**/ } finally { setLoading(false); }
  }, [tenant.id]);

  const loadAdmins = useCallback(async () => {
    try { setAdmins(await platformApi.listAdmins(tenant.id)); } catch { /**/ }
  }, [tenant.id]);

  const loadUsers = useCallback(async (search?: string) => {
    setUsersLoading(true);
    try { setUsers(await platformApi.listTenantUsers(tenant.id, search)); } catch { /**/ } finally { setUsersLoading(false); }
  }, [tenant.id]);

  useEffect(() => {
    loadDetail();
    loadAdmins();
  }, [loadDetail, loadAdmins]);

  useEffect(() => {
    if (tab === 'users') loadUsers();
  }, [tab, loadUsers]);

  async function saveName() {
    if (!nameInput.trim() || nameInput.trim() === detail?.name) { setEditingName(false); return; }
    setNameSaving(true);
    try {
      await platformApi.updateTenant(tenant.id, nameInput.trim());
      await loadDetail();
      onRefreshList();
      setEditingName(false);
    } catch { /**/ } finally { setNameSaving(false); }
  }

  async function saveSubscription() {
    setSubErr('');
    setSubSaving(true);
    try {
      await platformApi.updateSubscription(tenant.id, {
        ...subForm,
        startedAtUtc: subForm.startedAtUtc ? new Date(subForm.startedAtUtc).toISOString() : new Date().toISOString(),
        expiresAtUtc: subForm.expiresAtUtc ? new Date(subForm.expiresAtUtc).toISOString() : null,
      });
      setSubSaved(true);
      setTimeout(() => setSubSaved(false), 2000);
      setEditingSub(false);
      loadDetail();
      onRefreshList();
    } catch (e: unknown) {
      setSubErr((e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? 'Save failed.');
    } finally { setSubSaving(false); }
  }

  async function toggleFeature(featureKey: string, current: boolean) {
    setTogglingFeature(featureKey);
    try {
      await platformApi.setFeature(tenant.id, featureKey, !current);
      setDetail(prev => {
        if (!prev) return prev;
        const exists = prev.featureFlags.find(f => f.featureKey === featureKey);
        if (exists) return { ...prev, featureFlags: prev.featureFlags.map(f => f.featureKey === featureKey ? { ...f, isEnabled: !current } : f) };
        return { ...prev, featureFlags: [...prev.featureFlags, { featureKey, isEnabled: !current }] };
      });
    } catch { /**/ } finally { setTogglingFeature(null); }
  }

  function isFlagEnabled(key: string) {
    return detail?.featureFlags.find(f => f.featureKey === key)?.isEnabled ?? false;
  }

  async function addAdmin() {
    setAdminError('');
    if (!adminForm.email.includes('@')) { setAdminError('A valid email is required.'); return; }
    if (adminForm.password.length < 10) { setAdminError('Password must be at least 10 characters.'); return; }
    setAddingAdmin(true);
    try {
      await platformApi.addAdmin(tenant.id, { email: adminForm.email.trim(), fullName: adminForm.fullName.trim() || undefined, password: adminForm.password });
      setAdminForm({ email: '', fullName: '', password: '' });
      setShowAddAdmin(false);
      loadAdmins();
    } catch (err: unknown) {
      setAdminError((err as { response?: { data?: { message?: string } } })?.response?.data?.message ?? 'Failed to add admin.');
    } finally { setAddingAdmin(false); }
  }

  async function handlePasswordReset(userId: string) {
    setResetResult(null);
    try {
      const res = await platformApi.sendPasswordReset(userId);
      setResetResult(res.emailDeliveryAvailable
        ? `Reset email sent to ${res.userEmail}.`
        : `Reset logged. ${res.message}`);
    } catch {
      setResetResult('Password reset failed. Check network/API.');
    } finally { setResetUserId(null); }
  }

  const loadSupportSessions = useCallback(async () => {
    setSessionsLoading(true);
    try { setSupportSessions((await platformApi.listSupportSessions(tenant.id)).sessions); }
    catch { /**/ } finally { setSessionsLoading(false); }
  }, [tenant.id]);

  useEffect(() => {
    if (tab === 'support') loadSupportSessions();
  }, [tab, loadSupportSessions]);

  const loadInvoices = useCallback(async () => {
    setInvoicesLoading(true);
    try { setInvoices(await platformApi.listInvoices(tenant.id)); }
    catch { /**/ } finally { setInvoicesLoading(false); }
  }, [tenant.id]);

  useEffect(() => {
    if (tab === 'invoices') loadInvoices();
  }, [tab, loadInvoices]);

  async function createInvoice() {
    setInvoiceErr('');
    if (!invoiceForm.invoiceNumber.trim()) { setInvoiceErr('Invoice number is required.'); return; }
    if (!invoiceForm.invoiceDate || !invoiceForm.dueDate) { setInvoiceErr('Invoice date and due date are required.'); return; }
    setInvoiceSaving(true);
    try {
      await platformApi.createInvoice(tenant.id, {
        invoiceNumber: invoiceForm.invoiceNumber.trim(),
        amount: parseFloat(invoiceForm.amount) || 0,
        currencyCode: invoiceForm.currencyCode || 'USD',
        status: invoiceForm.status || 'Draft',
        paymentMethod: invoiceForm.paymentMethod || undefined,
        periodDescription: invoiceForm.periodDescription || undefined,
        invoiceDate: invoiceForm.invoiceDate,
        dueDate: invoiceForm.dueDate,
        notes: invoiceForm.notes || undefined,
      });
      setShowCreateInvoice(false);
      setInvoiceForm({ invoiceNumber: '', amount: '', currencyCode: 'USD', status: 'Draft', paymentMethod: '', periodDescription: '', invoiceDate: '', dueDate: '', notes: '' });
      loadInvoices();
    } catch (e: unknown) {
      setInvoiceErr((e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? 'Failed to create invoice.');
    } finally { setInvoiceSaving(false); }
  }

  async function saveEditInvoice() {
    if (!editingInvoice) return;
    setEditInvoiceSaving(true);
    try {
      await platformApi.updateInvoice(tenant.id, editingInvoice.id, {
        status: editInvoiceForm.status || undefined,
        paymentMethod: editInvoiceForm.paymentMethod || undefined,
        paymentReference: editInvoiceForm.paymentReference || undefined,
        paidDate: editInvoiceForm.paidDate || undefined,
        notes: editInvoiceForm.notes || undefined,
      });
      setEditingInvoice(null);
      loadInvoices();
    } catch { /**/ } finally { setEditInvoiceSaving(false); }
  }

  async function deleteInvoice(invoiceId: string) {
    setDeletingInvoice(invoiceId);
    try {
      await platformApi.deleteInvoice(tenant.id, invoiceId);
      loadInvoices();
    } catch (e: unknown) {
      alert((e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? 'Could not delete invoice.');
    } finally { setDeletingInvoice(null); }
  }

  async function handleSupportAccess() {
    if (!supportUserId.trim() || !supportReason.trim()) return;
    setSupportBusy(true);
    setSupportSession(null);
    try {
      const result = await platformApi.startSupportAccess(tenant.id, supportUserId.trim(), supportReason.trim());
      setSupportSession(result);
      loadSupportSessions();
    } catch (e: unknown) {
      const msg = (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? 'Could not start support session.';
      setSupportSession({ sessionId: '', token: 'ERROR: ' + msg, expiresAt: '', targetUserEmail: '', tenantSlug: '', reason: '' });
    } finally { setSupportBusy(false); }
  }

  async function handleEndSession(sessionId: string) {
    setEndingSession(sessionId);
    try {
      await platformApi.endSupportAccess(sessionId);
      loadSupportSessions();
    } catch { /**/ } finally { setEndingSession(null); }
  }

  function copySupportToken() {
    if (!supportSession?.token) return;
    navigator.clipboard.writeText(supportSession.token);
    setSupportCopied(true);
    setTimeout(() => setSupportCopied(false), 2000);
  }

  const isSuspended = detail?.subscription.status === 'Suspended';

  const TABS: { id: PanelTab; label: string }[] = [
    { id: 'subscription', label: 'Subscription' },
    { id: 'features', label: 'Features' },
    { id: 'admins', label: 'Admins' },
    { id: 'users', label: 'Users' },
    { id: 'invoices', label: 'Invoices' },
    { id: 'support', label: 'Support Access' },
  ];

  return (
    <div className="fixed inset-0 z-50 flex">
      <div className="flex-1 bg-black/60" onClick={onClose} />
      <div className="w-full max-w-2xl bg-sidebarDark border-l border-white/10 overflow-y-auto flex flex-col">

        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-white/10 sticky top-0 bg-sidebarDark z-10">
          <div className="flex-1 min-w-0">
            {editingName ? (
              <div className="flex items-center gap-2">
                <input
                  type="text"
                  value={nameInput}
                  onChange={e => setNameInput(e.target.value)}
                  className="bg-white/10 border border-white/10 rounded-lg px-2 py-1 text-sm text-white focus:outline-none focus:border-sapphire"
                  onKeyDown={e => { if (e.key === 'Enter') saveName(); if (e.key === 'Escape') setEditingName(false); }}
                  autoFocus
                />
                <button onClick={saveName} disabled={nameSaving} className="text-xs text-green-400 hover:text-green-300 disabled:opacity-50">Save</button>
                <button onClick={() => setEditingName(false)} className="text-xs text-slate-500 hover:text-white">Cancel</button>
              </div>
            ) : (
              <div className="flex items-center gap-2">
                <h2 className="text-lg font-semibold text-white truncate">{detail?.name ?? tenant.name}</h2>
                <button onClick={() => setEditingName(true)} className="text-xs text-slate-500 hover:text-white" title="Edit name">✏️</button>
                {isSuspended && (
                  <span className="px-2 py-0.5 rounded text-xs font-medium bg-rose-500/20 text-rose-300">Suspended</span>
                )}
              </div>
            )}
            <p className="text-xs text-slate-500 mt-0.5">/{tenant.slug}</p>
          </div>
          <button onClick={onClose} className="text-slate-500 hover:text-white transition-colors text-xl leading-none ml-4">&times;</button>
        </div>

        {/* Suspend / Reactivate quick actions */}
        {!loading && detail && (
          <div className="px-6 py-3 border-b border-white/10 flex items-center gap-2">
            {isSuspended ? (
              <button
                onClick={() => setConfirmAction('reactivate')}
                className="px-3 py-1.5 bg-green-700 hover:bg-green-600 text-white text-xs font-medium rounded-lg transition-colors"
              >
                Reactivate Tenant
              </button>
            ) : (
              <button
                onClick={() => setConfirmAction('suspend')}
                className="px-3 py-1.5 bg-rose-700 hover:bg-rose-600 text-white text-xs font-medium rounded-lg transition-colors"
              >
                Suspend Tenant
              </button>
            )}
            <span className="text-xs text-slate-500">
              {isSuspended ? 'Tenant workspace is currently blocked.' : 'Suspending blocks all tenant workspace access immediately.'}
            </span>
          </div>
        )}

        {/* Tabs */}
        <div className="flex border-b border-white/10 px-6">
          {TABS.map(t => (
            <button
              key={t.id}
              onClick={() => setTab(t.id)}
              className={`py-3 px-1 mr-5 text-xs font-medium border-b-2 transition-colors ${
                tab === t.id ? 'border-sapphire text-white' : 'border-transparent text-slate-500 hover:text-slate-300'
              }`}
            >
              {t.label}
            </button>
          ))}
        </div>

        {loading ? (
          <div className="flex-1 flex items-center justify-center">
            <div className="h-6 w-6 animate-spin rounded-full border-2 border-sapphire border-t-transparent" />
          </div>
        ) : !detail ? (
          <div className="flex-1 flex flex-col items-center justify-center gap-3 text-slate-500 text-sm">
            <p>Failed to load tenant detail.</p>
            <button onClick={loadDetail} className="text-xs text-sapphire hover:text-cyanAccent">Retry</button>
          </div>
        ) : (
          <div className="flex-1 px-6 py-5 space-y-6">

            {/* ── Subscription Tab ──────────────────────────────── */}
            {tab === 'subscription' && (
              <section>
                <div className="flex items-center justify-between mb-3">
                  <h3 className="text-sm font-semibold text-slate-300 uppercase tracking-wide">Subscription</h3>
                  {!editingSub && (
                    <button
                      onClick={() => setEditingSub(true)}
                      className="text-xs text-sapphire hover:text-cyanAccent border border-sapphire/30 rounded px-2 py-1 transition-colors"
                    >
                      Edit
                    </button>
                  )}
                </div>

                {!editingSub ? (
                  <div className="grid grid-cols-2 gap-3">
                    {[
                      { label: 'Plan', value: detail.subscription.plan },
                      { label: 'Status', value: detail.subscription.status },
                      { label: 'Max Users', value: detail.subscription.maxUsers === 0 ? 'Unlimited' : String(detail.subscription.maxUsers ?? '—') },
                      { label: 'Max Employees', value: detail.subscription.maxEmployees === 0 ? 'Unlimited' : detail.subscription.maxEmployees },
                      { label: 'Used Employees', value: detail.employeeCount },
                      { label: 'Active Users', value: detail.userCount },
                      { label: 'Billing Cycle', value: detail.subscription.billingCycle },
                      { label: 'Monthly Amount', value: `${detail.subscription.currencyCode} ${detail.subscription.monthlyAmount}` },
                      { label: 'Billing Email', value: detail.subscription.billingEmail || '—' },
                      { label: 'Started', value: detail.subscription.startedAtUtc ? new Date(detail.subscription.startedAtUtc).toLocaleDateString() : '—' },
                      { label: 'Expires', value: detail.subscription.expiresAtUtc ? new Date(detail.subscription.expiresAtUtc).toLocaleDateString() : 'Never' },
                    ].map(item => (
                      <div key={item.label} className="bg-darkSlate/60 rounded-lg px-3 py-2.5">
                        <dt className="text-xs text-slate-500">{item.label}</dt>
                        <dd className="text-sm font-medium text-white mt-0.5">{String(item.value)}</dd>
                      </div>
                    ))}
                  </div>
                ) : (
                  <div className="bg-darkSlate/60 border border-white/10 rounded-xl p-4 space-y-3">
                    <div className="grid grid-cols-2 gap-3">
                      <div>
                        <label className={labelCls}>Plan<InfoTip text="Changing auto-fills tier limits, which you can override." /></label>
                        <select
                          value={subForm.plan}
                          onChange={e => {
                            const plan = e.target.value;
                            const defaults = PLAN_DEFAULTS[plan];
                            setSubForm(p => ({ ...p, plan, ...(defaults ? { maxUsers: defaults.maxUsers, maxEmployees: defaults.maxEmployees } : {}) }));
                          }}
                          className={subInputCls}
                        >
                          {Object.keys(PLAN_DEFAULTS).map(p => <option key={p} value={p}>{p}</option>)}
                        </select>
                      </div>
                      <div>
                        <label className={labelCls}>Status<InfoTip text="Suspended/Cancelled blocks workspace access immediately." /></label>
                        <select value={subForm.status} onChange={e => setSubForm(p => ({ ...p, status: e.target.value }))} className={subInputCls}>
                          <option value="Active">Active</option>
                          <option value="Trial">Trial</option>
                          <option value="PastDue">PastDue</option>
                          <option value="Suspended">Suspended</option>
                          <option value="Cancelled">Cancelled</option>
                          <option value="ManualContract">ManualContract</option>
                        </select>
                      </div>
                      <div>
                        <label className={labelCls}>Max Users (0=unlimited)</label>
                        <input type="number" min={0} value={subForm.maxUsers} onChange={e => setSubForm(p => ({ ...p, maxUsers: Number(e.target.value) }))} className={subInputCls} />
                      </div>
                      <div>
                        <label className={labelCls}>Max Employees (0=unlimited)</label>
                        <input type="number" min={0} value={subForm.maxEmployees} onChange={e => setSubForm(p => ({ ...p, maxEmployees: Number(e.target.value) }))} className={subInputCls} />
                      </div>
                      <div>
                        <label className={labelCls}>Monthly Amount</label>
                        <input type="number" min={0} value={subForm.monthlyAmount} onChange={e => setSubForm(p => ({ ...p, monthlyAmount: Number(e.target.value) }))} className={subInputCls} />
                      </div>
                      <div>
                        <label className={labelCls}>Billing Cycle</label>
                        <select value={subForm.billingCycle} onChange={e => setSubForm(p => ({ ...p, billingCycle: e.target.value }))} className={subInputCls}>
                          <option value="Monthly">Monthly</option>
                          <option value="Annual">Annual</option>
                        </select>
                      </div>
                      <div className="col-span-2">
                        <label className={labelCls}>Billing Email</label>
                        <input type="email" value={subForm.billingEmail} onChange={e => setSubForm(p => ({ ...p, billingEmail: e.target.value }))} className={subInputCls} />
                      </div>
                      <div>
                        <label className={labelCls}>Start Date</label>
                        <input type="date" value={subForm.startedAtUtc} onChange={e => setSubForm(p => ({ ...p, startedAtUtc: e.target.value }))} className={subInputCls} />
                      </div>
                      <div>
                        <label className={labelCls}>Expires At (blank = never)</label>
                        <input type="date" value={subForm.expiresAtUtc} onChange={e => setSubForm(p => ({ ...p, expiresAtUtc: e.target.value }))} className={subInputCls} />
                      </div>
                    </div>
                    {subErr && <p className="text-xs text-rose-400">{subErr}</p>}
                    <div className="flex items-center gap-2 pt-1">
                      <button onClick={saveSubscription} disabled={subSaving} className="px-4 py-2 bg-sapphire hover:opacity-90 disabled:opacity-50 text-white text-xs font-medium rounded-lg">
                        {subSaving ? 'Saving...' : 'Save Changes'}
                      </button>
                      <button onClick={() => setEditingSub(false)} className="px-4 py-2 bg-white/10 hover:bg-white/20 text-slate-300 text-xs font-medium rounded-lg">Cancel</button>
                      {subSaved && <span className="text-xs text-green-400">Saved!</span>}
                    </div>
                  </div>
                )}
              </section>
            )}

            {/* ── Features Tab ──────────────────────────────────── */}
            {tab === 'features' && (
              <section>
                <h3 className="text-sm font-semibold text-slate-300 uppercase tracking-wide mb-3">Feature Flags</h3>
                <div className="bg-darkSlate/60 border border-white/10 rounded-xl divide-y divide-white/10">
                  {FEATURE_KEYS.map(feat => {
                    const enabled = isFlagEnabled(feat.key);
                    const toggling = togglingFeature === feat.key;
                    return (
                      <div key={feat.key} className="flex items-center justify-between px-4 py-3">
                        <span className="text-sm text-slate-300">{feat.label}</span>
                        <button
                          onClick={() => toggleFeature(feat.key, enabled)}
                          disabled={toggling}
                          className={`relative inline-flex h-5 w-9 items-center rounded-full transition-colors focus:outline-none disabled:opacity-50 ${enabled ? 'bg-sapphire' : 'bg-white/10'}`}
                        >
                          <span className={`inline-block h-3.5 w-3.5 transform rounded-full bg-white shadow transition-transform ${enabled ? 'translate-x-4.5' : 'translate-x-0.5'}`} />
                        </button>
                      </div>
                    );
                  })}
                </div>
              </section>
            )}

            {/* ── Admins Tab ────────────────────────────────────── */}
            {tab === 'admins' && (
              <section>
                <div className="flex items-center justify-between mb-3">
                  <h3 className="text-sm font-semibold text-slate-300 uppercase tracking-wide">Tenant Admins</h3>
                  {!showAddAdmin && (
                    <button onClick={() => setShowAddAdmin(true)} className="text-xs text-sapphire hover:text-cyanAccent border border-sapphire/30 rounded px-2 py-1 transition-colors">
                      Add Admin
                    </button>
                  )}
                </div>
                <div className="bg-darkSlate/60 border border-white/10 rounded-xl divide-y divide-white/10">
                  {admins.length === 0 && <p className="px-4 py-3 text-xs text-slate-500">No admin users found for this tenant.</p>}
                  {admins.map(a => (
                    <div key={a.id} className="flex items-center justify-between px-4 py-3 gap-3">
                      <div className="min-w-0">
                        <p className="text-sm text-white truncate">{a.fullName}</p>
                        <p className="text-xs text-slate-500 truncate">{a.email} · <span className="font-mono">{a.id}</span></p>
                      </div>
                      <div className="flex items-center gap-2 shrink-0">
                        <span className={`px-2 py-0.5 rounded text-xs font-medium ${a.isActive ? 'bg-green-900 text-green-300' : 'bg-rose-500/20 text-rose-300'}`}>
                          {a.isActive ? 'Active' : 'Inactive'}
                        </span>
                        <button
                          onClick={() => setSupportUserId(a.id)}
                          className="text-xs text-slate-400 hover:text-white border border-white/10 rounded px-2 py-1 transition-colors"
                          title="Use this ID in Support Access tab"
                        >
                          Use ID
                        </button>
                      </div>
                    </div>
                  ))}
                  {showAddAdmin && (
                    <div className="px-4 py-3 space-y-2">
                      <div className="grid grid-cols-2 gap-2">
                        <input type="email" value={adminForm.email} onChange={e => setAdminForm(p => ({ ...p, email: e.target.value }))} placeholder="admin@client.com"
                          className="bg-white/10 border border-white/10 rounded-lg px-3 py-2 text-sm text-white placeholder-slate-600 focus:outline-none focus:border-sapphire" />
                        <input type="text" value={adminForm.fullName} onChange={e => setAdminForm(p => ({ ...p, fullName: e.target.value }))} placeholder="Full name"
                          className="bg-white/10 border border-white/10 rounded-lg px-3 py-2 text-sm text-white placeholder-slate-600 focus:outline-none focus:border-sapphire" />
                        <input type="text" value={adminForm.password} onChange={e => setAdminForm(p => ({ ...p, password: e.target.value }))} placeholder="Password (min 10 chars)"
                          className="col-span-2 bg-white/10 border border-white/10 rounded-lg px-3 py-2 text-sm text-white placeholder-slate-600 focus:outline-none focus:border-sapphire font-mono" />
                      </div>
                      {adminError && <p className="text-xs text-rose-400">{adminError}</p>}
                      <div className="flex items-center gap-2">
                        <button onClick={addAdmin} disabled={addingAdmin} className="px-3 py-1.5 bg-sapphire hover:opacity-90 disabled:opacity-50 text-white text-xs font-medium rounded-lg">
                          {addingAdmin ? 'Adding...' : 'Add Admin'}
                        </button>
                        <button onClick={() => { setShowAddAdmin(false); setAdminError(''); }} className="px-3 py-1.5 bg-white/10 hover:bg-white/20 text-slate-300 text-xs font-medium rounded-lg">
                          Cancel
                        </button>
                      </div>
                    </div>
                  )}
                </div>
              </section>
            )}

            {/* ── Users Tab ─────────────────────────────────────── */}
            {tab === 'users' && (
              <section>
                <h3 className="text-sm font-semibold text-slate-300 uppercase tracking-wide mb-3">All Users</h3>
                <div className="mb-3">
                  <input
                    type="text"
                    value={userSearch}
                    onChange={e => { setUserSearch(e.target.value); loadUsers(e.target.value); }}
                    placeholder="Search by name or email..."
                    className="w-full bg-white/10 border border-white/10 rounded-lg px-3 py-2 text-sm text-white placeholder-slate-600 focus:outline-none focus:border-sapphire"
                  />
                </div>
                {resetResult && (
                  <div className="mb-3 px-3 py-2 bg-white/5 border border-white/10 rounded-lg text-xs text-slate-300">{resetResult}</div>
                )}
                <div className="bg-darkSlate/60 border border-white/10 rounded-xl divide-y divide-white/10">
                  {usersLoading ? (
                    <div className="flex items-center justify-center py-8">
                      <div className="h-5 w-5 animate-spin rounded-full border-2 border-sapphire border-t-transparent" />
                    </div>
                  ) : users.length === 0 ? (
                    <p className="px-4 py-3 text-xs text-slate-500">No users found.</p>
                  ) : users.map(u => (
                    <div key={u.id} className="flex items-start justify-between px-4 py-3 gap-3">
                      <div className="min-w-0">
                        <p className="text-sm text-white truncate">{u.fullName}</p>
                        <p className="text-xs text-slate-500 truncate">{u.email}</p>
                        <p className="text-xs text-slate-600 mt-0.5">{u.roles.join(', ') || 'No role'}</p>
                      </div>
                      <div className="flex items-center gap-2 shrink-0">
                        <span className={`px-2 py-0.5 rounded text-xs font-medium ${u.isActive ? 'bg-green-900 text-green-300' : 'bg-rose-500/20 text-rose-300'}`}>
                          {u.isActive ? 'Active' : 'Inactive'}
                        </span>
                        <button
                          onClick={() => setResetUserId(u.id)}
                          className="text-xs text-amber-400 hover:text-amber-300 border border-amber-500/20 rounded px-2 py-1 transition-colors"
                        >
                          Reset Password
                        </button>
                      </div>
                    </div>
                  ))}
                </div>
              </section>
            )}

            {/* ── Invoices Tab ──────────────────────────────────── */}
            {tab === 'invoices' && (
              <section className="space-y-4">
                <div className="flex items-center justify-between">
                  <h3 className="text-sm font-semibold text-slate-300 uppercase tracking-wide">Invoices</h3>
                  <button
                    type="button"
                    onClick={() => { setShowCreateInvoice(true); setInvoiceErr(''); }}
                    className="px-3 py-1.5 bg-sapphire hover:bg-sapphire/80 text-white text-xs font-medium rounded-lg transition-colors"
                  >
                    + New Invoice
                  </button>
                </div>

                {showCreateInvoice && (
                  <div className="bg-darkSlate/60 border border-white/10 rounded-xl p-4 space-y-3">
                    <h4 className="text-sm font-medium text-white">Create Invoice</h4>
                    <div className="grid grid-cols-2 gap-3">
                      <div>
                        <label className={labelCls}>Invoice #</label>
                        <input type="text" aria-label="Invoice number" placeholder="INV-2026-001" value={invoiceForm.invoiceNumber} onChange={e => setInvoiceForm(p => ({ ...p, invoiceNumber: e.target.value }))} className="w-full bg-white/10 border border-white/10 rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-sapphire" />
                      </div>
                      <div>
                        <label className={labelCls}>Amount</label>
                        <input type="number" step="0.01" aria-label="Amount" placeholder="0.00" value={invoiceForm.amount} onChange={e => setInvoiceForm(p => ({ ...p, amount: e.target.value }))} className="w-full bg-white/10 border border-white/10 rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-sapphire" />
                      </div>
                      <div>
                        <label className={labelCls}>Currency</label>
                        <select aria-label="Currency" value={invoiceForm.currencyCode} onChange={e => setInvoiceForm(p => ({ ...p, currencyCode: e.target.value }))} className="w-full bg-darkSlate border border-white/10 rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-sapphire">
                          {['USD', 'SAR', 'AED', 'QAR', 'KWD', 'BHD', 'OMR'].map(c => <option key={c}>{c}</option>)}
                        </select>
                      </div>
                      <div>
                        <label className={labelCls}>Status</label>
                        <select aria-label="Status" value={invoiceForm.status} onChange={e => setInvoiceForm(p => ({ ...p, status: e.target.value }))} className="w-full bg-darkSlate border border-white/10 rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-sapphire">
                          {['Draft', 'Sent', 'Paid', 'Overdue', 'Cancelled'].map(s => <option key={s}>{s}</option>)}
                        </select>
                      </div>
                      <div>
                        <label className={labelCls}>Invoice Date</label>
                        <input type="date" aria-label="Invoice date" value={invoiceForm.invoiceDate} onChange={e => setInvoiceForm(p => ({ ...p, invoiceDate: e.target.value }))} className="w-full bg-white/10 border border-white/10 rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-sapphire" />
                      </div>
                      <div>
                        <label className={labelCls}>Due Date</label>
                        <input type="date" aria-label="Due date" value={invoiceForm.dueDate} onChange={e => setInvoiceForm(p => ({ ...p, dueDate: e.target.value }))} className="w-full bg-white/10 border border-white/10 rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-sapphire" />
                      </div>
                      <div>
                        <label className={labelCls}>Period</label>
                        <input type="text" aria-label="Billing period" placeholder="e.g. June 2026" value={invoiceForm.periodDescription} onChange={e => setInvoiceForm(p => ({ ...p, periodDescription: e.target.value }))} className="w-full bg-white/10 border border-white/10 rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-sapphire" />
                      </div>
                      <div>
                        <label className={labelCls}>Payment Method</label>
                        <select aria-label="Payment method" value={invoiceForm.paymentMethod} onChange={e => setInvoiceForm(p => ({ ...p, paymentMethod: e.target.value }))} className="w-full bg-darkSlate border border-white/10 rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-sapphire">
                          <option value="">— none —</option>
                          {['BankTransfer', 'Cheque', 'Online', 'Cash'].map(m => <option key={m}>{m}</option>)}
                        </select>
                      </div>
                    </div>
                    <div>
                      <label className={labelCls}>Notes</label>
                      <textarea rows={2} aria-label="Notes" placeholder="Optional notes" value={invoiceForm.notes} onChange={e => setInvoiceForm(p => ({ ...p, notes: e.target.value }))} className="w-full bg-white/10 border border-white/10 rounded-lg px-3 py-2 text-sm text-white resize-none focus:outline-none focus:border-sapphire" />
                    </div>
                    {invoiceErr && <p className="text-xs text-rose-400">{invoiceErr}</p>}
                    <div className="flex gap-2 justify-end">
                      <button type="button" onClick={() => setShowCreateInvoice(false)} className="px-3 py-1.5 text-xs text-slate-400 hover:text-white">Cancel</button>
                      <button type="button" onClick={createInvoice} disabled={invoiceSaving} className="px-4 py-1.5 bg-sapphire hover:bg-sapphire/80 text-white text-xs font-medium rounded-lg disabled:opacity-50">
                        {invoiceSaving ? 'Saving…' : 'Create Invoice'}
                      </button>
                    </div>
                  </div>
                )}

                {editingInvoice && (
                  <div className="bg-darkSlate/60 border border-amber-500/30 rounded-xl p-4 space-y-3">
                    <h4 className="text-sm font-medium text-white">Edit Invoice #{editingInvoice.invoiceNumber}</h4>
                    <div className="grid grid-cols-2 gap-3">
                      <div>
                        <label className={labelCls}>Status</label>
                        <select aria-label="Status" value={editInvoiceForm.status} onChange={e => setEditInvoiceForm(p => ({ ...p, status: e.target.value }))} className="w-full bg-darkSlate border border-white/10 rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-sapphire">
                          {['Draft', 'Sent', 'Paid', 'Overdue', 'Cancelled'].map(s => <option key={s}>{s}</option>)}
                        </select>
                      </div>
                      <div>
                        <label className={labelCls}>Payment Method</label>
                        <select aria-label="Payment method" value={editInvoiceForm.paymentMethod} onChange={e => setEditInvoiceForm(p => ({ ...p, paymentMethod: e.target.value }))} className="w-full bg-darkSlate border border-white/10 rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-sapphire">
                          <option value="">— none —</option>
                          {['BankTransfer', 'Cheque', 'Online', 'Cash'].map(m => <option key={m}>{m}</option>)}
                        </select>
                      </div>
                      <div>
                        <label className={labelCls}>Payment Reference</label>
                        <input type="text" aria-label="Payment reference" placeholder="e.g. TXN-12345" value={editInvoiceForm.paymentReference} onChange={e => setEditInvoiceForm(p => ({ ...p, paymentReference: e.target.value }))} className="w-full bg-white/10 border border-white/10 rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-sapphire" />
                      </div>
                      <div>
                        <label className={labelCls}>Paid Date</label>
                        <input type="date" aria-label="Paid date" value={editInvoiceForm.paidDate} onChange={e => setEditInvoiceForm(p => ({ ...p, paidDate: e.target.value }))} className="w-full bg-white/10 border border-white/10 rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-sapphire" />
                      </div>
                    </div>
                    <div>
                      <label className={labelCls}>Notes</label>
                      <textarea rows={2} aria-label="Notes" placeholder="Optional notes" value={editInvoiceForm.notes} onChange={e => setEditInvoiceForm(p => ({ ...p, notes: e.target.value }))} className="w-full bg-white/10 border border-white/10 rounded-lg px-3 py-2 text-sm text-white resize-none focus:outline-none focus:border-sapphire" />
                    </div>
                    <div className="flex gap-2 justify-end">
                      <button type="button" onClick={() => setEditingInvoice(null)} className="px-3 py-1.5 text-xs text-slate-400 hover:text-white">Cancel</button>
                      <button type="button" onClick={saveEditInvoice} disabled={editInvoiceSaving} className="px-4 py-1.5 bg-amber-600 hover:bg-amber-500 text-white text-xs font-medium rounded-lg disabled:opacity-50">
                        {editInvoiceSaving ? 'Saving…' : 'Save Changes'}
                      </button>
                    </div>
                  </div>
                )}

                {invoicesLoading ? (
                  <p className="text-xs text-slate-500 py-4 text-center">Loading invoices…</p>
                ) : invoices.length === 0 ? (
                  <p className="text-xs text-slate-500 py-6 text-center">No invoices yet. Create one with the button above.</p>
                ) : (
                  <div className="overflow-x-auto">
                    <table className="w-full text-xs">
                      <thead>
                        <tr className="text-slate-500 border-b border-white/10">
                          <th className="py-2 text-left font-medium">Invoice #</th>
                          <th className="py-2 text-left font-medium">Period</th>
                          <th className="py-2 text-right font-medium">Amount</th>
                          <th className="py-2 text-left font-medium">Status</th>
                          <th className="py-2 text-left font-medium">Due</th>
                          <th className="py-2 text-right font-medium">Actions</th>
                        </tr>
                      </thead>
                      <tbody>
                        {invoices.map(inv => (
                          <tr key={inv.id} className="border-b border-white/5 hover:bg-white/5 transition-colors">
                            <td className="py-2 text-white font-mono">{inv.invoiceNumber}</td>
                            <td className="py-2 text-slate-400">{inv.periodDescription ?? '—'}</td>
                            <td className="py-2 text-right text-white font-medium">
                              {inv.amount.toLocaleString('en-US', { minimumFractionDigits: 2 })} {inv.currencyCode}
                            </td>
                            <td className="py-2">
                              <span className={`px-1.5 py-0.5 rounded text-xs font-medium ${
                                inv.status === 'Paid' ? 'bg-green-500/20 text-green-300' :
                                inv.status === 'Overdue' ? 'bg-rose-500/20 text-rose-300' :
                                inv.status === 'Sent' ? 'bg-blue-500/20 text-blue-300' :
                                inv.status === 'Cancelled' ? 'bg-slate-500/20 text-slate-400' :
                                'bg-amber-500/20 text-amber-300'
                              }`}>{inv.status}</span>
                            </td>
                            <td className="py-2 text-slate-400">{inv.dueDate}</td>
                            <td className="py-2 text-right flex justify-end gap-2">
                              <button
                                type="button"
                                onClick={() => { setEditingInvoice(inv); setEditInvoiceForm({ status: inv.status, paymentMethod: inv.paymentMethod ?? '', paymentReference: '', paidDate: inv.paidDate ?? '', notes: '' }); setShowCreateInvoice(false); }}
                                className="text-slate-400 hover:text-white text-xs underline"
                              >Edit</button>
                              {inv.status !== 'Paid' && (
                                <button
                                  type="button"
                                  onClick={() => deleteInvoice(inv.id)}
                                  disabled={deletingInvoice === inv.id}
                                  className="text-rose-500 hover:text-rose-300 text-xs underline disabled:opacity-50"
                                >Delete</button>
                              )}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </section>
            )}

            {/* ── Support Access Tab ───────────────────────────── */}
            {tab === 'support' && (
              <section className="space-y-5">
                {/* Start session */}
                <div>
                  <h3 className="text-sm font-semibold text-slate-300 uppercase tracking-wide mb-1">Start Support Session</h3>
                  <p className="text-xs text-slate-500 mb-3">
                    Break-glass access to act as a tenant user. Every session is recorded in the audit trail with the reason and your identity. Token expires in 1 hour.
                  </p>
                  <div className="bg-darkSlate/60 border border-white/10 rounded-xl p-4 space-y-3">
                    <div className="grid grid-cols-1 gap-2">
                      <div>
                        <label className="mb-1 block text-xs text-slate-400">User ID (GUID) — find it in the Users tab</label>
                        <input
                          type="text"
                          value={supportUserId}
                          onChange={e => setSupportUserId(e.target.value)}
                          placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
                          className="w-full bg-white/10 border border-white/10 rounded-lg px-3 py-2 text-sm text-white placeholder-slate-600 focus:outline-none focus:border-sapphire font-mono"
                        />
                      </div>
                      <div>
                        <label className="mb-1 block text-xs text-slate-400">Reason (required — written to audit log)</label>
                        <input
                          type="text"
                          value={supportReason}
                          onChange={e => setSupportReason(e.target.value)}
                          placeholder="e.g. Investigating payroll discrepancy — ticket #4291"
                          className="w-full bg-white/10 border border-white/10 rounded-lg px-3 py-2 text-sm text-white placeholder-slate-600 focus:outline-none focus:border-sapphire"
                        />
                      </div>
                    </div>
                    <button
                      onClick={handleSupportAccess}
                      disabled={supportBusy || !supportUserId.trim() || !supportReason.trim()}
                      className="px-4 py-2 bg-amber-600 hover:bg-amber-500 disabled:opacity-50 text-white text-xs font-medium rounded-lg transition-colors"
                    >
                      {supportBusy ? 'Starting session...' : 'Start Support Session'}
                    </button>

                    {supportSession && supportSession.token && (
                      <div className="space-y-2 pt-1">
                        {supportSession.sessionId ? (
                          <>
                            <div className="flex items-center gap-2 text-xs text-green-400">
                              <span>Session started · {supportSession.targetUserEmail} · expires {new Date(supportSession.expiresAt).toLocaleTimeString()}</span>
                            </div>
                            <div className="flex items-center gap-2">
                              <input readOnly value={supportSession.token} className="flex-1 bg-white/10 border border-white/10 rounded-lg px-3 py-2 text-xs text-slate-300 font-mono" />
                              <button onClick={copySupportToken} className="px-3 py-2 bg-white/10 hover:bg-white/20 text-slate-300 text-xs rounded-lg transition-colors whitespace-nowrap">
                                {supportCopied ? 'Copied!' : 'Copy Token'}
                              </button>
                            </div>
                            <div className="flex items-center gap-3">
                              <p className="text-xs text-amber-400 flex-1">Use as <code className="font-mono">Authorization: Bearer &lt;token&gt;</code>. Session ID: <span className="font-mono">{supportSession.sessionId}</span></p>
                              <button
                                onClick={() => handleEndSession(supportSession.sessionId)}
                                disabled={endingSession === supportSession.sessionId}
                                className="px-3 py-1.5 bg-rose-700 hover:bg-rose-600 disabled:opacity-50 text-white text-xs font-medium rounded-lg transition-colors whitespace-nowrap"
                              >
                                End Session
                              </button>
                            </div>
                          </>
                        ) : (
                          <p className="text-xs text-rose-400">{supportSession.token}</p>
                        )}
                      </div>
                    )}
                  </div>
                </div>

                {/* Session history */}
                <div>
                  <div className="flex items-center justify-between mb-2">
                    <h3 className="text-sm font-semibold text-slate-300 uppercase tracking-wide">Session History</h3>
                    <button onClick={loadSupportSessions} disabled={sessionsLoading} className="text-xs text-sapphire hover:text-cyanAccent disabled:opacity-50">Refresh</button>
                  </div>
                  <div className="bg-darkSlate/60 border border-white/10 rounded-xl divide-y divide-white/10">
                    {sessionsLoading ? (
                      <div className="flex justify-center py-6"><div className="h-5 w-5 animate-spin rounded-full border-2 border-sapphire border-t-transparent" /></div>
                    ) : supportSessions.length === 0 ? (
                      <p className="px-4 py-3 text-xs text-slate-500">No support sessions for this tenant.</p>
                    ) : supportSessions.map(s => (
                      <div key={s.id} className="flex items-start justify-between px-4 py-3 gap-3">
                        <div className="min-w-0 space-y-0.5">
                          <p className="text-xs text-white truncate">{s.targetUserEmail}</p>
                          <p className="text-xs text-slate-500 truncate">{s.reason}</p>
                          <p className="text-xs text-slate-600">{new Date(s.startedAtUtc).toLocaleString()} · {s.startedByIp}</p>
                        </div>
                        <div className="flex items-center gap-2 shrink-0">
                          <span className={`px-2 py-0.5 rounded text-xs font-medium ${s.isActive ? 'bg-amber-900 text-amber-300' : 'bg-white/10 text-slate-400'}`}>
                            {s.isActive ? 'Active' : 'Ended'}
                          </span>
                          {s.isActive && (
                            <button
                              onClick={() => handleEndSession(s.id)}
                              disabled={endingSession === s.id}
                              className="px-2 py-1 bg-rose-700 hover:bg-rose-600 disabled:opacity-50 text-white text-xs font-medium rounded transition-colors"
                            >
                              {endingSession === s.id ? '...' : 'End'}
                            </button>
                          )}
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              </section>
            )}

          </div>
        )}
      </div>

      {/* Suspend/Reactivate confirm modals */}
      {confirmAction === 'suspend' && (
        <ConfirmModal
          title="Suspend Tenant"
          message={`This will immediately block all users of "${detail?.name ?? tenant.name}" from accessing their workspace. You can reactivate at any time.`}
          confirmLabel="Suspend"
          confirmClass="bg-rose-700 hover:bg-rose-600"
          requireReason
          onConfirm={async (reason) => {
            await platformApi.suspendTenant(tenant.id, reason);
            await loadDetail();
            onRefreshList();
          }}
          onClose={() => setConfirmAction(null)}
        />
      )}
      {confirmAction === 'reactivate' && (
        <ConfirmModal
          title="Reactivate Tenant"
          message={`This will restore workspace access for all users of "${detail?.name ?? tenant.name}".`}
          confirmLabel="Reactivate"
          confirmClass="bg-green-700 hover:bg-green-600"
          requireReason
          onConfirm={async (reason) => {
            await platformApi.reactivateTenant(tenant.id, reason);
            await loadDetail();
            onRefreshList();
          }}
          onClose={() => setConfirmAction(null)}
        />
      )}

      {/* Password reset confirm modal */}
      {resetUserId && (
        <ConfirmModal
          title="Send Password Reset"
          message={`Send a password reset notification for user ${users.find(u => u.id === resetUserId)?.email ?? resetUserId}? The action will be logged.`}
          confirmLabel="Send Reset"
          confirmClass="bg-amber-700 hover:bg-amber-600"
          onConfirm={async () => { await handlePasswordReset(resetUserId); }}
          onClose={() => setResetUserId(null)}
        />
      )}
    </div>
  );
}

// ── Plans Section ────────────────────────────────────────────────────────────

function PlansSection() {
  const [plans, setPlans] = useState<PlatformPlan[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => {
    platformApi.getPlans()
      .then(setPlans)
      .catch(() => setError('Failed to load plans.'))
      .finally(() => setLoading(false));
  }, []);

  if (loading) return (
    <section>
      <h2 className="text-xs font-semibold text-slate-500 uppercase tracking-wider mb-3">Plan Catalog</h2>
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        {[...Array(4)].map((_, i) => <div key={i} className="bg-sidebarDark border border-white/10 rounded-xl p-5 animate-pulse h-32" />)}
      </div>
    </section>
  );

  if (error) return (
    <section>
      <h2 className="text-xs font-semibold text-slate-500 uppercase tracking-wider mb-3">Plan Catalog</h2>
      <p className="text-sm text-rose-400">{error}</p>
    </section>
  );

  return (
    <section>
      <h2 className="text-xs font-semibold text-slate-500 uppercase tracking-wider mb-3">Plan Catalog</h2>
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
        {plans.map(plan => (
          <div key={plan.name} className="bg-sidebarDark border border-white/10 rounded-xl p-5 flex flex-col gap-2">
            <div className="flex items-center justify-between">
              <p className="text-sm font-semibold text-white">{plan.name}</p>
              <p className="text-xs text-slate-400">
                {plan.monthlyPrice === 0 ? (plan.name === 'Enterprise' ? 'Custom' : 'Free') : `$${plan.monthlyPrice}/mo`}
              </p>
            </div>
            <p className="text-xs text-slate-500 leading-relaxed flex-1">{plan.description}</p>
            <div className="flex gap-3 text-xs text-slate-400 pt-1 border-t border-white/5">
              <span>{plan.maxUsers === 0 ? '∞' : plan.maxUsers} users</span>
              <span>{plan.maxEmployees === 0 ? '∞' : plan.maxEmployees} employees</span>
            </div>
          </div>
        ))}
      </div>
    </section>
  );
}

// ── Audit Log Section ─────────────────────────────────────────────────────────

function AuditLogSection() {
  const [logs, setLogs] = useState<PlatformAuditLog[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [loaded, setLoaded] = useState(false);

  async function load() {
    setLoading(true);
    setError('');
    try {
      const res = await platformApi.getAuditLogs(undefined, 1, 50);
      setLogs(res.logs);
      setLoaded(true);
    } catch {
      setError('Failed to load audit logs.');
    } finally { setLoading(false); }
  }

  if (!loaded) {
    return (
      <section>
        <h2 className="text-xs font-semibold text-slate-500 uppercase tracking-wider mb-3">Platform Audit Log</h2>
        <div className="bg-sidebarDark border border-white/10 rounded-xl p-6 flex flex-col items-center gap-2">
          <p className="text-sm text-slate-500">Platform-level actions (tenant lifecycle, password resets) are logged here.</p>
          <button onClick={load} className="px-3 py-1.5 bg-sapphire hover:opacity-90 text-white text-xs font-medium rounded-lg">Load Audit Log</button>
        </div>
      </section>
    );
  }

  return (
    <section>
      <div className="flex items-center justify-between mb-3">
        <h2 className="text-xs font-semibold text-slate-500 uppercase tracking-wider">Platform Audit Log</h2>
        <button onClick={load} disabled={loading} className="text-xs text-sapphire hover:text-cyanAccent disabled:opacity-50">Refresh</button>
      </div>
      <div className="bg-sidebarDark border border-white/10 rounded-xl overflow-hidden">
        {loading ? (
          <div className="flex items-center justify-center py-8">
            <div className="h-5 w-5 animate-spin rounded-full border-2 border-sapphire border-t-transparent" />
          </div>
        ) : error ? (
          <div className="flex flex-col items-center py-8 gap-2">
            <p className="text-sm text-rose-400">{error}</p>
            <button onClick={load} className="text-xs text-sapphire">Retry</button>
          </div>
        ) : logs.length === 0 ? (
          <p className="text-center py-8 text-sm text-slate-500">No audit events yet.</p>
        ) : (
          <table className="w-full text-xs">
            <thead>
              <tr className="border-b border-white/10">
                {['Time', 'Action', 'Entity', 'Entity ID', 'Performed By'].map(col => (
                  <th key={col} className="text-left font-medium text-slate-500 px-4 py-2">{col}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-white/10">
              {logs.map(l => (
                <tr key={l.id} className="hover:bg-white/5">
                  <td className="px-4 py-2 text-slate-400 whitespace-nowrap">{new Date(l.createdAtUtc).toLocaleString()}</td>
                  <td className="px-4 py-2 font-medium text-white">{l.action}</td>
                  <td className="px-4 py-2 text-slate-400">{l.entityType}</td>
                  <td className="px-4 py-2 text-slate-500 font-mono truncate max-w-[140px]">{l.entityId}</td>
                  <td className="px-4 py-2 text-slate-400">{l.performedByName || '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </section>
  );
}

// ── Platform Dashboard ────────────────────────────────────────────────────────

export default function PlatformDashboard() {
  const router = useRouter();
  const [stats, setStats] = useState<PlatformStats | null>(null);
  const [tenants, setTenants] = useState<PlatformTenantSummary[]>([]);
  const [loadingStats, setLoadingStats] = useState(true);
  const [loadingTenants, setLoadingTenants] = useState(true);
  const [statsError, setStatsError] = useState(false);
  const [tenantsError, setTenantsError] = useState(false);
  const [selectedTenant, setSelectedTenant] = useState<PlatformTenantSummary | null>(null);
  const [showNewClient, setShowNewClient] = useState(false);
  const [search, setSearch] = useState('');

  useEffect(() => {
    const token = localStorage.getItem('platform_access_token');
    if (!token) { router.replace('/platform/login'); return; }
    loadStats();
    loadTenants();
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [router]);

  async function loadStats() {
    setLoadingStats(true);
    setStatsError(false);
    try { setStats(await platformApi.getStats()); }
    catch { setStatsError(true); }
    finally { setLoadingStats(false); }
  }

  async function loadTenants() {
    setLoadingTenants(true);
    setTenantsError(false);
    try { setTenants(await platformApi.listTenants()); }
    catch { setTenantsError(true); }
    finally { setLoadingTenants(false); }
  }

  function logout() {
    localStorage.removeItem('platform_access_token');
    router.replace('/platform/login');
  }

  function fmtExpiry(dt: string | null) {
    if (!dt) return '—';
    return new Date(dt).toLocaleDateString();
  }

  const planBadge = (plan: string) => {
    const colors: Record<string, string> = {
      trial: 'bg-cyan-900/50 text-cyan-300',
      starter: 'bg-white/10 text-slate-300',
      growth: 'bg-blue-900 text-blue-300',
      enterprise: 'bg-purple-900 text-purple-300',
    };
    return colors[plan?.toLowerCase()] ?? 'bg-white/10 text-slate-300';
  };

  const statusBadge = (status: string) => {
    const colors: Record<string, string> = {
      active: 'bg-green-900 text-green-300',
      trial: 'bg-cyan-900/50 text-cyan-300',
      suspended: 'bg-rose-500/20 text-rose-300',
      cancelled: 'bg-rose-500/20 text-rose-300',
    };
    return colors[status?.toLowerCase()] ?? 'bg-white/10 text-slate-300';
  };

  const filtered = tenants.filter(t => {
    if (!search.trim()) return true;
    const s = search.toLowerCase();
    return t.name.toLowerCase().includes(s) || t.slug.toLowerCase().includes(s);
  });

  // When tenant details update inside TenantPanel, refresh the selected tenant in the list
  function handleListRefresh() {
    loadTenants();
    // Keep the panel open but update the selected tenant reference after reload
  }

  // After list refresh, sync selectedTenant with the updated list entry
  useEffect(() => {
    if (selectedTenant && tenants.length > 0) {
      const updated = tenants.find(t => t.id === selectedTenant.id);
      if (updated) setSelectedTenant(updated);
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenants]);

  return (
    <div className="min-h-screen bg-midnight text-white">
      {/* Top bar */}
      <div className="border-b border-white/10 bg-sidebarDark">
        <div className="max-w-7xl mx-auto px-6 py-4 flex items-center justify-between">
          <h1 className="text-lg font-semibold text-white">Platform Administration</h1>
          <button onClick={logout} className="text-sm text-slate-400 hover:text-white border border-white/10 rounded-lg px-3 py-1.5 transition-colors">
            Logout
          </button>
        </div>
      </div>

      <div className="max-w-7xl mx-auto px-6 py-8 space-y-8">

        {/* Stats */}
        <section>
          <h2 className="text-xs font-semibold text-slate-500 uppercase tracking-wider mb-3">Overview</h2>
          {loadingStats ? (
            <div className="grid grid-cols-4 gap-4">
              {[...Array(4)].map((_, i) => <div key={i} className="bg-sidebarDark border border-white/10 rounded-xl p-5 animate-pulse h-20" />)}
            </div>
          ) : statsError ? (
            <div className="flex items-center gap-2">
              <p className="text-sm text-rose-400">Failed to load stats.</p>
              <button onClick={loadStats} className="text-xs text-sapphire">Retry</button>
            </div>
          ) : stats ? (
            <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
              <StatCard label="Total Tenants" value={stats.totalTenants} />
              <StatCard label="Active Tenants" value={stats.activeTenants} />
              <StatCard label="Total Users" value={stats.totalUsers} />
              <StatCard label="Total Employees" value={stats.totalEmployees} />
            </div>
          ) : null}
        </section>

        {/* Plan breakdown */}
        {stats && (
          <section>
            <h2 className="text-xs font-semibold text-slate-500 uppercase tracking-wider mb-3">Tenants by Plan</h2>
            <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
              <StatCard label="Trial" value={stats.tenantsByPlan.trial ?? 0} />
              <StatCard label="Starter" value={stats.tenantsByPlan.starter} />
              <StatCard label="Growth" value={stats.tenantsByPlan.growth} />
              <StatCard label="Enterprise" value={stats.tenantsByPlan.enterprise} />
            </div>
          </section>
        )}

        {/* Tenants table */}
        <section>
          <div className="flex items-center justify-between mb-3 gap-3">
            <h2 className="text-xs font-semibold text-slate-500 uppercase tracking-wider shrink-0">Tenants</h2>
            <input
              type="text"
              value={search}
              onChange={e => setSearch(e.target.value)}
              placeholder="Search by name or slug…"
              className="flex-1 max-w-xs bg-sidebarDark border border-white/10 rounded-lg px-3 py-1.5 text-sm text-white placeholder-slate-600 focus:outline-none focus:border-sapphire"
            />
            <button
              onClick={() => setShowNewClient(true)}
              className="px-3 py-1.5 bg-sapphire hover:opacity-90 text-white text-xs font-medium rounded-lg transition-colors shrink-0"
            >
              + New Client
            </button>
          </div>
          <div className="bg-sidebarDark border border-white/10 rounded-xl overflow-hidden">
            {loadingTenants ? (
              <div className="flex items-center justify-center py-12">
                <div className="h-6 w-6 animate-spin rounded-full border-2 border-sapphire border-t-transparent" />
              </div>
            ) : tenantsError ? (
              <div className="flex flex-col items-center py-12 gap-2">
                <p className="text-sm text-rose-400">Failed to load tenants.</p>
                <button onClick={loadTenants} className="text-xs text-sapphire">Retry</button>
              </div>
            ) : filtered.length === 0 ? (
              <p className="text-center py-12 text-sm text-slate-500">{tenants.length === 0 ? 'No tenants yet.' : 'No tenants match your search.'}</p>
            ) : (
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-white/10">
                    {['Tenant Name', 'Slug', 'Plan', 'Status', 'Employees', 'Users', 'Expires', 'Actions'].map(col => (
                      <th key={col} className="text-left text-xs font-medium text-slate-500 px-4 py-3">{col}</th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-white/10">
                  {filtered.map(t => (
                    <tr key={t.id} className="hover:bg-white/5 transition-colors">
                      <td className="px-4 py-3 font-medium text-white">{t.name}</td>
                      <td className="px-4 py-3 text-slate-400 font-mono text-xs">/{t.slug}</td>
                      <td className="px-4 py-3">
                        <span className={`inline-block px-2 py-0.5 rounded text-xs font-medium ${planBadge(t.subscription?.plan ?? '')}`}>
                          {t.subscription?.plan ?? '—'}
                        </span>
                      </td>
                      <td className="px-4 py-3">
                        <span className={`inline-block px-2 py-0.5 rounded text-xs font-medium ${statusBadge(t.subscription?.status ?? '')}`}>
                          {t.subscription?.status ?? '—'}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-slate-300 text-xs">
                        {t.activeEmployeeCount}/{t.subscription?.maxEmployees === 0 ? '∞' : (t.subscription?.maxEmployees ?? '—')}
                      </td>
                      <td className="px-4 py-3 text-slate-300 text-xs">
                        {t.activeUserCount}/{t.subscription?.maxUsers === 0 ? '∞' : (t.subscription?.maxUsers ?? '—')}
                      </td>
                      <td className="px-4 py-3 text-slate-400 text-xs">{fmtExpiry(t.subscription?.expiresAtUtc ?? null)}</td>
                      <td className="px-4 py-3">
                        <button
                          onClick={() => setSelectedTenant(t)}
                          className="px-3 py-1.5 bg-sapphire hover:opacity-90 text-white text-xs font-medium rounded-lg transition-colors"
                        >
                          Manage
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </section>

        {/* Plans */}
        <PlansSection />

        {/* Audit Log */}
        <AuditLogSection />

      </div>

      {/* Tenant panel slide-over */}
      {selectedTenant && (
        <TenantPanel
          tenant={selectedTenant}
          onClose={() => setSelectedTenant(null)}
          onRefreshList={handleListRefresh}
        />
      )}

      {/* New client modal */}
      {showNewClient && (
        <NewClientModal
          onClose={() => setShowNewClient(false)}
          onCreated={() => { loadTenants(); loadStats(); }}
        />
      )}
    </div>
  );
}
