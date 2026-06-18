'use client';

import { useState, useEffect, useCallback } from 'react';
import { useParams, useRouter } from 'next/navigation';
import Link from 'next/link';
import {
  ArrowLeft, RefreshCw, Edit3, Check, X, AlertTriangle,
  Zap, Users, CreditCard, FileText, Shield, ExternalLink,
  UserCog, PlayCircle, StopCircle, Palette, Globe, Trash2,
} from 'lucide-react';
import {
  platformApi,
  type PlatformTenantDetail,
  type TenantAdminUser,
  type TenantBranding,
  type TenantLocalization,
} from '@/src/api/platform';

const FEATURE_FLAGS: { key: string; label: string; description: string; category: string }[] = [
  // ── Core Modules ────────────────────────────────────────────────────────────
  { key: 'payroll',              label: 'Payroll',                  description: 'Payroll processing and payslip generation',                category: 'Core Modules' },
  { key: 'recruitment',         label: 'Recruitment',              description: 'Job postings, applications and hiring pipeline',          category: 'Core Modules' },
  { key: 'performance',         label: 'Performance & Appraisals', description: 'Goal setting, reviews and performance cycles',            category: 'Core Modules' },
  { key: 'compliance',          label: 'Compliance',               description: 'Labour law compliance and regulatory reporting',          category: 'Core Modules' },
  { key: 'finance',             label: 'Finance',                  description: 'Loans, advances and financial module access',             category: 'Core Modules' },
  { key: 'shifts',              label: 'Shifts & Scheduling',      description: 'Shift rosters and workforce scheduling',                  category: 'Core Modules' },
  { key: 'overtime',            label: 'Overtime Management',      description: 'Overtime requests, approvals and calculation',            category: 'Core Modules' },
  // ── AI & Intelligence ───────────────────────────────────────────────────────
  { key: 'ai_assistant',        label: 'AI HR Assistant',          description: 'Enable natural-language HR queries',                      category: 'AI & Intelligence' },
  { key: 'resume_screening',    label: 'AI Resume Screening',      description: 'AI-powered CV analysis (advisory)',                       category: 'AI & Intelligence' },
  { key: 'payroll_ai_validation', label: 'AI Payroll Validation',  description: 'AI variance detection for payroll',                       category: 'AI & Intelligence' },
  { key: 'risk_scores',         label: 'Employee Risk Scores',     description: 'Churn and burnout risk indicators (advisory)',            category: 'AI & Intelligence' },
  // ── GCC / Regional ──────────────────────────────────────────────────────────
  { key: 'wps_export',          label: 'WPS/SIF Export',           description: 'GCC WPS payroll file generation',                        category: 'GCC / Regional' },
  { key: 'eosb_calc',           label: 'EOSB Calculator',          description: 'End-of-service benefit computation',                     category: 'GCC / Regional' },
  { key: 'qiwa_integration',    label: 'Qiwa Integration',         description: 'Saudi Arabia Qiwa platform sync',                        category: 'GCC / Regional' },
  { key: 'hijri_calendar',      label: 'Hijri Calendar',           description: 'Show Hijri dates alongside Gregorian',                   category: 'GCC / Regional' },
  // ── Platform & Access ────────────────────────────────────────────────────────
  { key: 'mobile_app',          label: 'Mobile App',               description: 'Enable mobile API endpoints',                            category: 'Platform & Access' },
];

const PLAN_BADGE: Record<string, string> = {
  trial:      'bg-slate-700 text-slate-300 border-slate-600',
  starter:    'bg-blue-900/50 text-blue-300 border-blue-700/40',
  growth:     'bg-purple-900/50 text-purple-300 border-purple-700/40',
  enterprise: 'bg-amber-900/50 text-amber-300 border-amber-700/40',
};

const STATUS_CLS: Record<string, string> = {
  active:    'text-emerald-400',
  trial:     'text-cyan-400',
  pastdue:   'text-amber-400',
  suspended: 'text-rose-400',
  cancelled: 'text-slate-500',
};

type Tab = 'overview' | 'features' | 'users' | 'billing' | 'audit' | 'security' | 'branding' | 'localization';
const TABS: { id: Tab; label: string; icon: React.ElementType }[] = [
  { id: 'overview',     label: 'Overview',     icon: Edit3 },
  { id: 'features',     label: 'Features',     icon: Zap },
  { id: 'users',        label: 'Users',        icon: Users },
  { id: 'billing',      label: 'Billing',      icon: CreditCard },
  { id: 'branding',     label: 'Branding',     icon: Palette },
  { id: 'localization', label: 'Localization', icon: Globe },
  { id: 'audit',        label: 'Audit',        icon: FileText },
  { id: 'security',     label: 'Security',     icon: Shield },
];

// ── Overview Tab ──────────────────────────────────────────────────────────────

function OverviewTab({ tenant, onRefresh, onDelete }: { tenant: PlatformTenantDetail; onRefresh: () => void; onDelete: () => void }) {
  const [editing, setEditing] = useState(false);
  const [name, setName] = useState(tenant.name);
  const [saving, setSaving] = useState(false);
  const [suspendOpen, setSuspendOpen] = useState(false);
  const [suspendReason, setSuspendReason] = useState('');
  const [msg, setMsg] = useState<{ text: string; ok: boolean } | null>(null);

  const status = (tenant.subscription?.status ?? '').toLowerCase();
  const plan   = (tenant.subscription?.plan ?? 'trial').toLowerCase();

  async function saveName() {
    setSaving(true);
    try {
      await platformApi.updateTenant(tenant.id, name);
      setMsg({ text: 'Name updated.', ok: true });
      setEditing(false);
      onRefresh();
    } catch { setMsg({ text: 'Save failed.', ok: false }); }
    finally { setSaving(false); }
  }

  async function doSuspend() {
    try {
      await platformApi.suspendTenant(tenant.id, suspendReason);
      setMsg({ text: 'Tenant suspended.', ok: true });
      setSuspendOpen(false);
      onRefresh();
    } catch { setMsg({ text: 'Failed.', ok: false }); }
  }

  async function doReactivate() {
    try {
      await platformApi.reactivateTenant(tenant.id, 'Reactivated via platform admin');
      setMsg({ text: 'Tenant reactivated.', ok: true });
      onRefresh();
    } catch { setMsg({ text: 'Failed.', ok: false }); }
  }

  return (
    <div className="space-y-5">
      {msg && (
        <div className={`flex items-center justify-between px-4 py-2.5 rounded-lg border text-sm ${msg.ok ? 'bg-emerald-500/10 border-emerald-500/20 text-emerald-400' : 'bg-rose-500/10 border-rose-500/20 text-rose-400'}`}>
          {msg.text}
          <button type="button" title="Dismiss" onClick={() => setMsg(null)}><X className="h-3.5 w-3.5" /></button>
        </div>
      )}

      {/* Info card */}
      <div className="bg-[#161b22] border border-white/[0.07] rounded-xl divide-y divide-white/[0.04]">
        <div className="px-5 py-3.5 flex items-center justify-between gap-4">
          <span className="text-xs text-slate-500 w-32 shrink-0">Name</span>
          {editing ? (
            <div className="flex items-center gap-2 flex-1">
              <input value={name} onChange={e => setName(e.target.value)} autoFocus title="Tenant name" placeholder="Tenant name"
                className="flex-1 bg-white/[0.05] border border-white/[0.12] rounded-lg px-3 py-1.5 text-sm text-white focus:outline-none focus:border-sapphire/60" />
              <button type="button" title="Save name" onClick={saveName} disabled={saving} className="text-emerald-400 hover:text-emerald-300 transition-colors disabled:opacity-40">
                <Check className="h-4 w-4" />
              </button>
              <button type="button" title="Cancel" onClick={() => { setEditing(false); setName(tenant.name); }} className="text-slate-500 hover:text-white transition-colors">
                <X className="h-4 w-4" />
              </button>
            </div>
          ) : (
            <div className="flex items-center gap-2 flex-1">
              <span className="text-sm text-white flex-1">{tenant.name}</span>
              <button type="button" title="Edit name" onClick={() => setEditing(true)} className="text-slate-600 hover:text-white transition-colors">
                <Edit3 className="h-3.5 w-3.5" />
              </button>
            </div>
          )}
        </div>
        <div className="px-5 py-3.5 flex items-center gap-4">
          <span className="text-xs text-slate-500 w-32 shrink-0">Slug</span>
          <span className="text-sm text-slate-300 font-mono">/{tenant.slug}</span>
        </div>
        <div className="px-5 py-3.5 flex items-center gap-4">
          <span className="text-xs text-slate-500 w-32 shrink-0">Plan</span>
          <span className={`text-[10px] font-semibold uppercase tracking-wider px-1.5 py-0.5 rounded border ${PLAN_BADGE[plan] ?? PLAN_BADGE.trial}`}>
            {tenant.subscription?.plan ?? 'Trial'}
          </span>
        </div>
        <div className="px-5 py-3.5 flex items-center gap-4">
          <span className="text-xs text-slate-500 w-32 shrink-0">Status</span>
          <span className={`text-sm font-medium capitalize ${STATUS_CLS[status] ?? 'text-slate-400'}`}>
            {tenant.subscription?.status ?? 'Unknown'}
          </span>
        </div>
        <div className="px-5 py-3.5 flex items-center gap-4">
          <span className="text-xs text-slate-500 w-32 shrink-0">Monthly</span>
          <span className="text-sm text-slate-300">
            {tenant.subscription?.monthlyAmount
              ? `${tenant.subscription.currencyCode} ${tenant.subscription.monthlyAmount.toLocaleString()}`
              : <span className="text-slate-600">—</span>}
          </span>
        </div>
        <div className="px-5 py-3.5 flex items-center gap-4">
          <span className="text-xs text-slate-500 w-32 shrink-0">Users / Employees</span>
          <span className="text-sm text-slate-300">
            {tenant.userCount} users · {tenant.employeeCount} employees
          </span>
        </div>
        <div className="px-5 py-3.5 flex items-center gap-4">
          <span className="text-xs text-slate-500 w-32 shrink-0">Billing Email</span>
          <span className="text-sm text-slate-300 font-mono">{tenant.subscription?.billingEmail || <span className="text-slate-600">—</span>}</span>
        </div>
        {tenant.subscription?.expiresAtUtc && (
          <div className="px-5 py-3.5 flex items-center gap-4">
            <span className="text-xs text-slate-500 w-32 shrink-0">Expires</span>
            <span className="text-sm text-slate-300">
              {new Date(tenant.subscription.expiresAtUtc).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })}
            </span>
          </div>
        )}
      </div>

      {/* Actions */}
      <div className="flex flex-wrap gap-3">
        {status === 'suspended' ? (
          <button type="button" onClick={doReactivate}
            className="flex items-center gap-1.5 text-sm text-emerald-400 border border-emerald-500/30 hover:border-emerald-500/60 px-4 py-2 rounded-lg transition-colors">
            <PlayCircle className="h-3.5 w-3.5" />
            Reactivate Tenant
          </button>
        ) : (
          <button type="button" onClick={() => setSuspendOpen(true)}
            className="flex items-center gap-1.5 text-sm text-rose-400 border border-rose-500/30 hover:border-rose-500/60 px-4 py-2 rounded-lg transition-colors">
            <StopCircle className="h-3.5 w-3.5" />
            Suspend Tenant
          </button>
        )}
        <a href={`${typeof window !== 'undefined' ? window.location.origin.replace('localhost:3000', 'localhost:3000') : ''}`}
          target="_blank" rel="noopener noreferrer"
          className="flex items-center gap-1.5 text-sm text-slate-400 border border-white/10 hover:border-white/20 px-4 py-2 rounded-lg transition-colors">
          <ExternalLink className="h-3.5 w-3.5" />
          Open Tenant App
        </a>
        <button type="button" onClick={onDelete}
          className="flex items-center gap-1.5 text-sm text-rose-500 border border-rose-900/40 hover:border-rose-500/40 px-4 py-2 rounded-lg transition-colors ml-auto">
          <Trash2 className="h-3.5 w-3.5" />
          Delete Tenant
        </button>
      </div>

      {/* Suspend modal */}
      {suspendOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
          <div className="absolute inset-0 bg-black/70 backdrop-blur-sm" onClick={() => setSuspendOpen(false)} />
          <div className="relative w-full max-w-sm bg-[#0d1117] border border-white/10 rounded-2xl shadow-2xl p-6 space-y-4">
            <h3 className="text-sm font-semibold text-white">Suspend {tenant.name}?</h3>
            <p className="text-sm text-slate-400">This will immediately disable all tenant users. Provide a reason.</p>
            <textarea value={suspendReason} onChange={e => setSuspendReason(e.target.value)} rows={2}
              placeholder="Reason (required)"
              className="w-full bg-white/[0.04] border border-white/[0.08] rounded-lg px-3 py-2 text-sm text-white placeholder-slate-600 focus:outline-none focus:border-sapphire/60 resize-none" />
            <div className="flex gap-3">
              <button type="button" onClick={() => setSuspendOpen(false)}
                className="flex-1 border border-white/10 text-slate-400 rounded-lg py-2 text-sm transition-colors">Cancel</button>
              <button type="button" onClick={doSuspend} disabled={!suspendReason.trim()}
                className="flex-1 bg-rose-600 hover:bg-rose-500 text-white rounded-lg py-2 text-sm font-semibold transition-colors disabled:opacity-40">
                Suspend
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

// ── Features Tab ──────────────────────────────────────────────────────────────

function FeaturesTab({ tenant, onRefresh }: { tenant: PlatformTenantDetail; onRefresh: () => void }) {
  const [toggling, setToggling] = useState<string | null>(null);
  const [msg, setMsg] = useState<{ text: string; ok: boolean } | null>(null);
  const flagMap = Object.fromEntries(tenant.featureFlags.map(f => [f.featureKey, f.isEnabled]));

  async function toggle(key: string, current: boolean) {
    setToggling(key);
    try {
      await platformApi.setFeature(tenant.id, key, !current);
      const flag = FEATURE_FLAGS.find(f => f.key === key);
      setMsg({ text: `${flag?.label ?? key} ${!current ? 'enabled' : 'disabled'}.`, ok: true });
      onRefresh();
    } catch { setMsg({ text: `Failed to update ${key}.`, ok: false }); }
    finally { setToggling(null); }
  }

  const categories = Array.from(new Set(FEATURE_FLAGS.map(f => f.category)));

  return (
    <div className="space-y-5">
      {msg && (
        <div className={`flex items-center justify-between px-4 py-2.5 rounded-lg border text-sm ${msg.ok ? 'bg-emerald-500/10 border-emerald-500/20 text-emerald-400' : 'bg-rose-500/10 border-rose-500/20 text-rose-400'}`}>
          {msg.text}
          <button type="button" title="Dismiss" onClick={() => setMsg(null)}><X className="h-3 w-3" /></button>
        </div>
      )}
      {categories.map(cat => (
        <div key={cat}>
          <p className="text-[10px] font-semibold text-slate-600 uppercase tracking-widest mb-2">{cat}</p>
          <div className="bg-[#161b22] border border-white/[0.07] rounded-xl overflow-hidden divide-y divide-white/[0.04]">
            {FEATURE_FLAGS.filter(f => f.category === cat).map(f => {
              const enabled = flagMap[f.key] ?? false;
              return (
                <div key={f.key} className="flex items-center gap-4 px-5 py-3.5">
                  <div className="flex-1 min-w-0">
                    <p className="text-sm text-white">{f.label}</p>
                    <p className="text-[11px] text-slate-500">{f.description}</p>
                  </div>
                  <button
                    type="button"
                    title={`${enabled ? 'Disable' : 'Enable'} ${f.label}`}
                    onClick={() => toggle(f.key, enabled)}
                    disabled={toggling === f.key}
                    role="switch"
                    aria-checked={enabled}
                    className={`relative inline-flex h-5 w-9 shrink-0 items-center rounded-full transition-colors focus:outline-none disabled:opacity-40
                      ${enabled ? 'bg-sapphire' : 'bg-slate-700'}`}
                  >
                    <span className={`inline-block h-3.5 w-3.5 transform rounded-full bg-white transition-transform
                      ${enabled ? 'translate-x-4' : 'translate-x-1'}`} />
                  </button>
                </div>
              );
            })}
          </div>
        </div>
      ))}
    </div>
  );
}

// ── Users Tab ─────────────────────────────────────────────────────────────────

function UsersTab({ tenantId }: { tenantId: string }) {
  const [admins, setAdmins] = useState<TenantAdminUser[]>([]);
  const [loading, setLoading] = useState(true);
  const [impersonating, setImpersonating] = useState<string | null>(null);
  const [msg, setMsg] = useState<{ text: string; ok: boolean } | null>(null);

  useEffect(() => { load(); }, [tenantId]); // eslint-disable-line react-hooks/exhaustive-deps

  async function load() {
    setLoading(true);
    try { setAdmins(await platformApi.listAdmins(tenantId)); }
    finally { setLoading(false); }
  }

  async function impersonate(userId: string, email: string) {
    setImpersonating(userId);
    try {
      const { token } = await platformApi.impersonate(tenantId, userId);
      // Open tenant app with impersonation token in a new tab
      const url = new URL(window.location.href);
      window.open(`${url.protocol}//${url.hostname}:3000/login?impersonate=${token}`, '_blank');
      setMsg({ text: `Impersonating ${email} — new tab opened.`, ok: true });
    } catch {
      setMsg({ text: 'Impersonation failed. Ensure the user exists.', ok: false });
    } finally { setImpersonating(null); }
  }

  return (
    <div className="space-y-4">
      {msg && (
        <div className={`flex items-center justify-between px-4 py-2.5 rounded-lg border text-sm ${msg.ok ? 'bg-emerald-500/10 border-emerald-500/20 text-emerald-400' : 'bg-rose-500/10 border-rose-500/20 text-rose-400'}`}>
          {msg.text}
          <button type="button" title="Dismiss" onClick={() => setMsg(null)}><X className="h-3.5 w-3.5" /></button>
        </div>
      )}
      <div className="bg-[#161b22] border border-white/[0.07] rounded-xl overflow-hidden">
        <div className="px-5 py-3 border-b border-white/[0.06]">
          <p className="text-[10px] font-semibold text-slate-600 uppercase tracking-widest">Admin Users</p>
        </div>
        {loading ? (
          <div className="flex items-center justify-center py-10">
            <div className="h-4 w-4 animate-spin rounded-full border-2 border-sapphire border-t-transparent" />
          </div>
        ) : admins.length === 0 ? (
          <p className="text-sm text-slate-600 text-center py-10">No admin users found.</p>
        ) : (
          admins.map(u => (
            <div key={u.id} className="flex items-center gap-4 px-5 py-3 border-b border-white/[0.04] last:border-0">
              <div className="h-7 w-7 rounded-full bg-slate-800 border border-white/10 flex items-center justify-center shrink-0">
                <span className="text-[10px] font-bold text-slate-400">{u.fullName?.slice(0, 2).toUpperCase() ?? '??'}</span>
              </div>
              <div className="flex-1 min-w-0">
                <p className="text-sm text-white font-medium">{u.fullName}</p>
                <p className="text-[11px] text-slate-600">{u.email}</p>
              </div>
              <span className={`text-[11px] px-1.5 py-0.5 rounded border ${u.isActive ? 'text-emerald-400 border-emerald-500/20 bg-emerald-500/5' : 'text-slate-500 border-white/10 bg-white/5'}`}>
                {u.status}
              </span>
              <button type="button"
                onClick={() => impersonate(u.id, u.email)}
                disabled={impersonating === u.id}
                className="flex items-center gap-1 text-[11px] text-blue-400 border border-blue-500/20 hover:border-blue-500/40 px-2 py-1 rounded transition-colors disabled:opacity-40">
                <UserCog className="h-3 w-3" />
                {impersonating === u.id ? 'Opening…' : 'Impersonate'}
              </button>
            </div>
          ))
        )}
      </div>
      <p className="text-xs text-slate-600">
        For full user list, password resets, and role management, see{' '}
        <Link href={`/platform/tenants/${tenantId}/users`} className="text-sapphire hover:text-blue-300">Users page →</Link>
      </p>
    </div>
  );
}

// ── Billing Tab ───────────────────────────────────────────────────────────────

function BillingTab({ tenantId }: { tenantId: string }) {
  return (
    <div className="flex flex-col items-center justify-center py-14 gap-4">
      <div className="h-12 w-12 rounded-2xl bg-blue-500/10 border border-blue-500/20 flex items-center justify-center">
        <CreditCard className="h-5 w-5 text-blue-400" />
      </div>
      <div className="text-center">
        <p className="text-sm font-semibold text-white mb-1">Billing & Invoices</p>
        <p className="text-xs text-slate-500">Create invoices, track payments, download PDFs and send to the tenant.</p>
      </div>
      <Link
        href={`/platform/tenants/${tenantId}/billing`}
        className="inline-flex items-center gap-2 bg-blue-600 hover:bg-blue-500 text-white px-5 py-2.5 rounded-xl text-sm font-semibold transition-colors"
      >
        <CreditCard className="h-3.5 w-3.5" />
        Open Billing Manager
      </Link>
    </div>
  );
}

// ── Audit Tab ─────────────────────────────────────────────────────────────────

function AuditTab({ tenantId }: { tenantId: string }) {
  return (
    <div className="text-center py-10 text-sm text-slate-500">
      Tenant-scoped audit log →{' '}
      <Link href={`/platform/tenants/${tenantId}/audit`} className="text-sapphire hover:text-blue-300">Open Audit Page</Link>
    </div>
  );
}

// ── Security Tab ──────────────────────────────────────────────────────────────

function SecurityTabContent({ tenantId }: { tenantId: string }) {
  return (
    <div className="text-center py-10 text-sm text-slate-500">
      Tenant security details →{' '}
      <Link href={`/platform/tenants/${tenantId}/security`} className="text-sapphire hover:text-blue-300">Open Security Page</Link>
    </div>
  );
}

// ── Branding Tab ──────────────────────────────────────────────────────────────

function BrandingTab({ tenant, onRefresh }: { tenant: PlatformTenantDetail; onRefresh: () => void }) {
  const initial: Partial<TenantBranding> = {
    logoUrl:       tenant.branding?.logoUrl       ?? '',
    faviconUrl:    tenant.branding?.faviconUrl     ?? '',
    primaryColor:  tenant.branding?.primaryColor   ?? '#2563EB',
    accentColor:   tenant.branding?.accentColor    ?? '#7C3AED',
    portalTitle:   tenant.branding?.portalTitle    ?? '',
    companyNameEn: tenant.branding?.companyNameEn  ?? '',
    companyNameAr: tenant.branding?.companyNameAr  ?? '',
  };
  const [form, setForm] = useState(initial);
  const [saving, setSaving] = useState(false);
  const [msg, setMsg] = useState<{ text: string; ok: boolean } | null>(null);

  function f(k: keyof typeof form, v: string) { setForm(p => ({ ...p, [k]: v })); }

  async function save() {
    setSaving(true);
    try {
      await platformApi.updateBranding(tenant.id, form);
      setMsg({ text: 'Branding saved.', ok: true });
      onRefresh();
    } catch { setMsg({ text: 'Save failed.', ok: false }); }
    finally { setSaving(false); }
  }

  return (
    <div className="space-y-5">
      {msg && (
        <div className={`flex items-center justify-between px-4 py-2.5 rounded-lg border text-sm ${msg.ok ? 'bg-emerald-500/10 border-emerald-500/20 text-emerald-400' : 'bg-rose-500/10 border-rose-500/20 text-rose-400'}`}>
          {msg.text}
          <button type="button" title="Dismiss" onClick={() => setMsg(null)}><X className="h-3.5 w-3.5" /></button>
        </div>
      )}
      <div className="bg-[#161b22] border border-white/[0.07] rounded-xl divide-y divide-white/[0.04]">
        {([
          { label: 'Portal Title',     key: 'portalTitle',   placeholder: 'My Company HR Portal' },
          { label: 'Company (English)', key: 'companyNameEn', placeholder: 'Acme Corp' },
          { label: 'Company (Arabic)',  key: 'companyNameAr', placeholder: 'أكمي كورب' },
          { label: 'Logo URL',          key: 'logoUrl',       placeholder: 'https://cdn.example.com/logo.png' },
          { label: 'Favicon URL',       key: 'faviconUrl',    placeholder: 'https://cdn.example.com/favicon.ico' },
        ] as { label: string; key: keyof typeof form; placeholder: string }[]).map(row => (
          <div key={row.key} className="flex items-center gap-4 px-5 py-3">
            <span className="text-xs text-slate-500 w-36 shrink-0">{row.label}</span>
            <input
              title={row.label}
              value={form[row.key] as string ?? ''}
              onChange={e => f(row.key, e.target.value)}
              placeholder={row.placeholder}
              className="flex-1 bg-white/[0.04] border border-white/[0.08] rounded-lg px-3 py-1.5 text-sm text-white focus:outline-none focus:border-sapphire/60 placeholder-slate-700"
            />
          </div>
        ))}
        <div className="flex items-center gap-4 px-5 py-3">
          <span className="text-xs text-slate-500 w-36 shrink-0">Primary Color</span>
          <div className="flex items-center gap-2">
            <input type="color" title="Primary color" value={form.primaryColor ?? '#2563EB'} onChange={e => f('primaryColor', e.target.value)}
              className="h-8 w-8 rounded cursor-pointer bg-transparent border-0" />
            <span className="text-sm text-slate-400 font-mono">{form.primaryColor}</span>
          </div>
        </div>
        <div className="flex items-center gap-4 px-5 py-3">
          <span className="text-xs text-slate-500 w-36 shrink-0">Accent Color</span>
          <div className="flex items-center gap-2">
            <input type="color" title="Accent color" value={form.accentColor ?? '#7C3AED'} onChange={e => f('accentColor', e.target.value)}
              className="h-8 w-8 rounded cursor-pointer bg-transparent border-0" />
            <span className="text-sm text-slate-400 font-mono">{form.accentColor}</span>
          </div>
        </div>
      </div>
      {(form.logoUrl || form.portalTitle) && (
        <div className="bg-[#161b22] border border-white/[0.07] rounded-xl p-4">
          <p className="text-[10px] font-semibold text-slate-600 uppercase tracking-wider mb-3">Preview</p>
          <div className="flex items-center gap-3">
            {form.logoUrl && <img src={form.logoUrl} alt="Logo preview" className="h-8 object-contain" onError={e => { (e.target as HTMLImageElement).style.display = 'none'; }} />}
            {form.portalTitle && <span className="text-sm text-white">{form.portalTitle}</span>}
          </div>
        </div>
      )}
      <button type="button" onClick={save} disabled={saving}
        className="flex items-center gap-2 bg-sapphire/90 hover:bg-sapphire text-white text-sm font-medium px-5 py-2 rounded-lg transition-colors disabled:opacity-50">
        {saving ? <RefreshCw className="h-3.5 w-3.5 animate-spin" /> : <Check className="h-3.5 w-3.5" />}
        Save Branding
      </button>
    </div>
  );
}

// ── Localization Tab ───────────────────────────────────────────────────────────

function LocalizationTab({ tenant, onRefresh }: { tenant: PlatformTenantDetail; onRefresh: () => void }) {
  const initial: Partial<TenantLocalization> = {
    defaultLanguage:  tenant.localization?.defaultLanguage  ?? 'en',
    defaultTimezone:  tenant.localization?.defaultTimezone  ?? 'Asia/Riyadh',
    dateFormat:       tenant.localization?.dateFormat       ?? 'DD/MM/YYYY',
    currencyCode:     tenant.localization?.currencyCode     ?? 'SAR',
    countryCode:      tenant.localization?.countryCode      ?? 'SA',
    calendarSystem:   tenant.localization?.calendarSystem   ?? 'Gregorian',
    workWeek:         tenant.localization?.workWeek         ?? 'Sun-Thu',
    weekStartDay:     tenant.localization?.weekStartDay     ?? 'Sunday',
    rtlEnabled:       tenant.localization?.rtlEnabled       ?? false,
    hijriDatesEnabled: tenant.localization?.hijriDatesEnabled ?? false,
  };
  const [form, setForm] = useState(initial);
  const [saving, setSaving] = useState(false);
  const [msg, setMsg] = useState<{ text: string; ok: boolean } | null>(null);

  function sf(k: keyof typeof form, v: string | boolean) { setForm(p => ({ ...p, [k]: v })); }

  async function save() {
    setSaving(true);
    try {
      await platformApi.updateLocalization(tenant.id, form);
      setMsg({ text: 'Localization saved.', ok: true });
      onRefresh();
    } catch { setMsg({ text: 'Save failed.', ok: false }); }
    finally { setSaving(false); }
  }

  const selectCls = 'w-full bg-white/[0.04] border border-white/[0.08] rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-sapphire/60';
  const inputCls  = 'w-full bg-white/[0.04] border border-white/[0.08] rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-sapphire/60';

  return (
    <div className="space-y-5">
      {msg && (
        <div className={`flex items-center justify-between px-4 py-2.5 rounded-lg border text-sm ${msg.ok ? 'bg-emerald-500/10 border-emerald-500/20 text-emerald-400' : 'bg-rose-500/10 border-rose-500/20 text-rose-400'}`}>
          {msg.text}
          <button type="button" title="Dismiss" onClick={() => setMsg(null)}><X className="h-3.5 w-3.5" /></button>
        </div>
      )}
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <div>
          <label className="block text-[10px] font-semibold text-slate-500 uppercase tracking-wider mb-1">Default Language</label>
          <select title="Default language" value={form.defaultLanguage} onChange={e => sf('defaultLanguage', e.target.value)} className={selectCls}>
            <option value="en">English</option>
            <option value="ar">Arabic</option>
          </select>
        </div>
        <div>
          <label className="block text-[10px] font-semibold text-slate-500 uppercase tracking-wider mb-1">Timezone</label>
          <input title="Default timezone" value={form.defaultTimezone ?? ''} onChange={e => sf('defaultTimezone', e.target.value)} placeholder="Asia/Riyadh" className={inputCls} />
        </div>
        <div>
          <label className="block text-[10px] font-semibold text-slate-500 uppercase tracking-wider mb-1">Date Format</label>
          <select title="Date format" value={form.dateFormat} onChange={e => sf('dateFormat', e.target.value)} className={selectCls}>
            <option value="DD/MM/YYYY">DD/MM/YYYY</option>
            <option value="MM/DD/YYYY">MM/DD/YYYY</option>
            <option value="YYYY-MM-DD">YYYY-MM-DD</option>
          </select>
        </div>
        <div>
          <label className="block text-[10px] font-semibold text-slate-500 uppercase tracking-wider mb-1">Currency</label>
          <input title="Currency code" value={form.currencyCode ?? ''} onChange={e => sf('currencyCode', e.target.value)} placeholder="SAR" className={inputCls} />
        </div>
        <div>
          <label className="block text-[10px] font-semibold text-slate-500 uppercase tracking-wider mb-1">Country</label>
          <input title="Country code" value={form.countryCode ?? ''} onChange={e => sf('countryCode', e.target.value)} placeholder="SA" className={inputCls} />
        </div>
        <div>
          <label className="block text-[10px] font-semibold text-slate-500 uppercase tracking-wider mb-1">Calendar System</label>
          <select title="Calendar system" value={form.calendarSystem} onChange={e => sf('calendarSystem', e.target.value)} className={selectCls}>
            <option value="Gregorian">Gregorian</option>
            <option value="Hijri">Hijri</option>
          </select>
        </div>
        <div>
          <label className="block text-[10px] font-semibold text-slate-500 uppercase tracking-wider mb-1">Work Week</label>
          <select title="Work week" value={form.workWeek} onChange={e => sf('workWeek', e.target.value)} className={selectCls}>
            <option value="Sun-Thu">Sun–Thu (GCC)</option>
            <option value="Mon-Fri">Mon–Fri</option>
            <option value="Mon-Sat">Mon–Sat</option>
          </select>
        </div>
        <div>
          <label className="block text-[10px] font-semibold text-slate-500 uppercase tracking-wider mb-1">Week Starts On</label>
          <select title="Week start day" value={form.weekStartDay} onChange={e => sf('weekStartDay', e.target.value)} className={selectCls}>
            <option value="Sunday">Sunday</option>
            <option value="Monday">Monday</option>
            <option value="Saturday">Saturday</option>
          </select>
        </div>
      </div>
      <div className="flex flex-wrap gap-4">
        {([
          { key: 'rtlEnabled', label: 'RTL Layout' },
          { key: 'hijriDatesEnabled', label: 'Show Hijri Dates' },
        ] as { key: 'rtlEnabled' | 'hijriDatesEnabled'; label: string }[]).map(tog => (
          <label key={tog.key} className="flex items-center gap-2 cursor-pointer">
            <input type="checkbox" checked={!!form[tog.key]} onChange={e => sf(tog.key, e.target.checked)}
              className="h-4 w-4 rounded accent-sapphire" />
            <span className="text-sm text-slate-300">{tog.label}</span>
          </label>
        ))}
      </div>
      <button type="button" onClick={save} disabled={saving}
        className="flex items-center gap-2 bg-sapphire/90 hover:bg-sapphire text-white text-sm font-medium px-5 py-2 rounded-lg transition-colors disabled:opacity-50">
        {saving ? <RefreshCw className="h-3.5 w-3.5 animate-spin" /> : <Check className="h-3.5 w-3.5" />}
        Save Localization
      </button>
    </div>
  );
}

// ── Main ──────────────────────────────────────────────────────────────────────

export default function TenantDetailPage() {
  const { id } = useParams<{ id: string }>();
  const router  = useRouter();
  const [tenant, setTenant] = useState<PlatformTenantDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [tab, setTab] = useState<Tab>('overview');
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [deleting, setDeleting] = useState(false);

  useEffect(() => {
    const token = typeof window !== 'undefined' ? localStorage.getItem('platform_access_token') : null;
    if (!token) { router.replace('/platform/login'); return; }
    load();
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id]);

  const load = useCallback(async () => {
    setLoading(true);
    try { setTenant(await platformApi.getTenant(id)); }
    catch { router.replace('/platform/tenants'); }
    finally { setLoading(false); }
  }, [id, router]);

  if (loading || !tenant) {
    return (
      <div className="flex items-center justify-center min-h-[60vh]">
        <div className="h-5 w-5 animate-spin rounded-full border-2 border-sapphire border-t-transparent" />
      </div>
    );
  }

  return (
    <div className="space-y-5">
      {/* Header */}
      <div className="flex items-start gap-4">
        <Link href="/platform/tenants" className="h-8 w-8 flex items-center justify-center text-slate-500 hover:text-white border border-white/10 rounded-lg transition-colors shrink-0 mt-0.5">
          <ArrowLeft className="h-4 w-4" />
        </Link>
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 flex-wrap">
            <h1 className="text-lg font-bold text-white">{tenant.name}</h1>
            <span className="text-[10px] font-semibold uppercase tracking-wider px-1.5 py-0.5 rounded border bg-slate-700 text-slate-300 border-slate-600">
              {tenant.slug}
            </span>
          </div>
          <p className="text-xs text-slate-500 mt-0.5">
            {tenant.userCount} users · {tenant.employeeCount} employees
          </p>
        </div>
        <button type="button" title="Refresh" onClick={load} className="h-8 w-8 flex items-center justify-center text-slate-500 hover:text-white border border-white/10 rounded-lg transition-colors shrink-0">
          <RefreshCw className="h-3.5 w-3.5" />
        </button>
      </div>

      {/* Tab bar */}
      <div className="flex gap-1 border-b border-white/[0.06] overflow-x-auto">
        {TABS.map(t => {
          const Icon = t.icon;
          return (
            <button type="button" key={t.id} onClick={() => setTab(t.id)}
              className={`flex items-center gap-1.5 px-4 py-2.5 text-sm font-medium border-b-2 whitespace-nowrap transition-colors
                ${tab === t.id
                  ? 'border-sapphire text-white'
                  : 'border-transparent text-slate-500 hover:text-slate-300'}`}>
              <Icon className="h-3.5 w-3.5" />
              {t.label}
            </button>
          );
        })}
      </div>

      {/* Tab content */}
      {tab === 'overview'     && <OverviewTab tenant={tenant} onRefresh={load} onDelete={() => setDeleteOpen(true)} />}
      {tab === 'features'     && <FeaturesTab tenant={tenant} onRefresh={load} />}
      {tab === 'users'        && <UsersTab tenantId={tenant.id} />}
      {tab === 'billing'      && <BillingTab tenantId={tenant.id} />}
      {tab === 'branding'     && <BrandingTab tenant={tenant} onRefresh={load} />}
      {tab === 'localization' && <LocalizationTab tenant={tenant} onRefresh={load} />}
      {tab === 'audit'        && <AuditTab tenantId={tenant.id} />}
      {tab === 'security'     && <SecurityTabContent tenantId={tenant.id} />}

      {/* Delete tenant modal — Owner only */}
      {deleteOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
          <div className="absolute inset-0 bg-black/70 backdrop-blur-sm" onClick={() => setDeleteOpen(false)} />
          <div className="relative w-full max-w-sm bg-[#0d1117] border border-rose-900/40 rounded-2xl shadow-2xl p-6 space-y-4">
            <div className="flex items-center gap-3">
              <div className="h-9 w-9 rounded-xl bg-rose-500/10 border border-rose-500/20 flex items-center justify-center shrink-0">
                <Trash2 className="h-4 w-4 text-rose-400" />
              </div>
              <div>
                <h3 className="text-sm font-semibold text-white">Delete Tenant?</h3>
                <p className="text-xs text-slate-500">{tenant.name}</p>
              </div>
            </div>
            <p className="text-xs text-slate-400">This will deactivate the tenant, cancel the subscription, and revoke all active sessions. This action is irreversible.</p>
            <div className="flex gap-3">
              <button type="button" onClick={() => setDeleteOpen(false)}
                className="flex-1 border border-white/10 text-slate-400 rounded-lg py-2 text-sm transition-colors">Cancel</button>
              <button type="button" disabled={deleting} onClick={async () => {
                setDeleting(true);
                try { await platformApi.deleteTenant(tenant.id); router.replace('/platform/tenants'); }
                catch { setDeleting(false); setDeleteOpen(false); }
              }}
                className="flex-1 bg-rose-600 hover:bg-rose-500 text-white rounded-lg py-2 text-sm font-semibold transition-colors disabled:opacity-40">
                {deleting ? 'Deleting…' : 'Delete Tenant'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
