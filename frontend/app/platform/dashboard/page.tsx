'use client';

import { useState, useEffect, useCallback } from 'react';
import { useRouter } from 'next/navigation';
import {
  platformApi,
  type PlatformStats,
  type PlatformTenantSummary,
  type PlatformTenantDetail,
} from '@/src/api/platform';

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

function TenantPanel({
  tenant,
  onClose,
}: {
  tenant: PlatformTenantSummary;
  onClose: () => void;
}) {
  const [detail, setDetail] = useState<PlatformTenantDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [editingSub, setEditingSub] = useState(false);
  const [subForm, setSubForm] = useState<SubForm>({
    plan: '',
    status: '',
    maxUsers: 0,
    maxEmployees: 0,
    billingEmail: '',
    billingCycle: '',
    monthlyAmount: 0,
    currencyCode: 'USD',
    startedAtUtc: '',
    expiresAtUtc: '',
  });
  const [subSaving, setSubSaving] = useState(false);
  const [subSaved, setSubSaved] = useState(false);
  const [impersonateUserId, setImpersonateUserId] = useState('');
  const [impersonateToken, setImpersonateToken] = useState('');
  const [impersonating, setImpersonating] = useState(false);
  const [copied, setCopied] = useState(false);
  const [togglingFeature, setTogglingFeature] = useState<string | null>(null);

  const loadDetail = useCallback(async () => {
    setLoading(true);
    try {
      const d = await platformApi.getTenant(tenant.id);
      setDetail(d);
      setSubForm({
        plan: d.subscription.plan,
        status: d.subscription.status,
        maxUsers: (d.subscription as { maxUsers?: number }).maxUsers ?? 0,
        maxEmployees: d.subscription.maxEmployees,
        billingEmail: d.subscription.billingEmail,
        billingCycle: d.subscription.billingCycle,
        monthlyAmount: d.subscription.monthlyAmount,
        currencyCode: d.subscription.currencyCode,
        startedAtUtc: d.subscription.startedAtUtc?.slice(0, 10) ?? '',
        expiresAtUtc: d.subscription.expiresAtUtc?.slice(0, 10) ?? '',
      });
    } catch {
      //
    } finally {
      setLoading(false);
    }
  }, [tenant.id]);

  useEffect(() => { loadDetail(); }, [loadDetail]);

  async function saveSubscription() {
    setSubSaving(true);
    try {
      await platformApi.updateSubscription(tenant.id, {
        ...subForm,
        startedAtUtc: subForm.startedAtUtc ? new Date(subForm.startedAtUtc).toISOString() : '',
        expiresAtUtc: subForm.expiresAtUtc ? new Date(subForm.expiresAtUtc).toISOString() : null,
        maxUsers: subForm.maxUsers,
      });
      setSubSaved(true);
      setTimeout(() => setSubSaved(false), 2000);
      setEditingSub(false);
      loadDetail();
    } catch {
      //
    } finally {
      setSubSaving(false);
    }
  }

  async function toggleFeature(featureKey: string, current: boolean) {
    setTogglingFeature(featureKey);
    try {
      await platformApi.setFeature(tenant.id, featureKey, !current);
      setDetail(prev => {
        if (!prev) return prev;
        const exists = prev.featureFlags.find(f => f.featureKey === featureKey);
        if (exists) {
          return { ...prev, featureFlags: prev.featureFlags.map(f => f.featureKey === featureKey ? { ...f, isEnabled: !current } : f) };
        }
        return { ...prev, featureFlags: [...prev.featureFlags, { featureKey, isEnabled: !current }] };
      });
    } catch {
      //
    } finally {
      setTogglingFeature(null);
    }
  }

  function isFlagEnabled(key: string) {
    return detail?.featureFlags.find(f => f.featureKey === key)?.isEnabled ?? false;
  }

  async function handleImpersonate() {
    if (!impersonateUserId.trim()) return;
    setImpersonating(true);
    setImpersonateToken('');
    try {
      const { token } = await platformApi.impersonate(tenant.id, impersonateUserId.trim());
      setImpersonateToken(token);
    } catch {
      setImpersonateToken('ERROR: Could not generate token. Check the User ID.');
    } finally {
      setImpersonating(false);
    }
  }

  function copyToken() {
    navigator.clipboard.writeText(impersonateToken);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  }

  return (
    <div className="fixed inset-0 z-50 flex">
      {/* Backdrop */}
      <div className="flex-1 bg-black/60" onClick={onClose} />

      {/* Slide-over panel */}
      <div className="w-full max-w-2xl bg-sidebarDark border-l border-white/10 overflow-y-auto flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-white/10 sticky top-0 bg-sidebarDark z-10">
          <div>
            <h2 className="text-lg font-semibold text-white">{tenant.name}</h2>
            <p className="text-xs text-slate-500 mt-0.5">/{tenant.slug}</p>
          </div>
          <button onClick={onClose} className="text-slate-500 hover:text-white transition-colors text-xl leading-none">&times;</button>
        </div>

        {loading ? (
          <div className="flex-1 flex items-center justify-center">
            <div className="h-6 w-6 animate-spin rounded-full border-2 border-sapphire border-t-transparent" />
          </div>
        ) : detail ? (
          <div className="flex-1 px-6 py-5 space-y-6">

            {/* Subscription Info */}
            <section>
              <div className="flex items-center justify-between mb-3">
                <h3 className="text-sm font-semibold text-slate-300 uppercase tracking-wide">Subscription</h3>
                {!editingSub && (
                  <button
                    onClick={() => setEditingSub(true)}
                    className="text-xs text-sapphire hover:text-cyanAccent border border-sapphire/30 rounded px-2 py-1 transition-colors"
                  >
                    Edit Subscription
                  </button>
                )}
              </div>

              {!editingSub ? (
                <div className="grid grid-cols-2 gap-3">
                  {[
                    { label: 'Plan', value: detail.subscription.plan },
                    { label: 'Status', value: detail.subscription.status },
                    { label: 'Max Users', value: (detail.subscription as { maxUsers?: number }).maxUsers === 0 ? 'Unlimited' : String((detail.subscription as { maxUsers?: number }).maxUsers ?? '—') },
                    { label: 'Max Employees', value: detail.subscription.maxEmployees === 0 ? 'Unlimited' : detail.subscription.maxEmployees },
                    { label: 'Billing Cycle', value: detail.subscription.billingCycle },
                    { label: 'Monthly Amount', value: `${detail.subscription.currencyCode} ${detail.subscription.monthlyAmount}` },
                    { label: 'Billing Email', value: detail.subscription.billingEmail },
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
                      <label className="block text-xs text-slate-400 mb-1">Plan</label>
                      <select
                        value={subForm.plan}
                        onChange={e => {
                          const plan = e.target.value;
                          const defaults = PLAN_DEFAULTS[plan];
                          setSubForm(p => ({
                            ...p,
                            plan,
                            ...(defaults ? { maxUsers: defaults.maxUsers, maxEmployees: defaults.maxEmployees } : {}),
                          }));
                        }}
                        className="w-full bg-white/10 border border-white/10 rounded-lg px-2 py-2 text-sm text-white"
                      >
                        {Object.keys(PLAN_DEFAULTS).map(p => <option key={p} value={p}>{p}</option>)}
                      </select>
                    </div>
                    <div>
                      <label className="block text-xs text-slate-400 mb-1">Status</label>
                      <select
                        value={subForm.status}
                        onChange={e => setSubForm(p => ({ ...p, status: e.target.value }))}
                        className="w-full bg-white/10 border border-white/10 rounded-lg px-2 py-2 text-sm text-white"
                      >
                        <option value="Active">Active</option>
                        <option value="Trial">Trial</option>
                        <option value="Suspended">Suspended</option>
                        <option value="Cancelled">Cancelled</option>
                      </select>
                    </div>
                    <div>
                      <label className="block text-xs text-slate-400 mb-1">
                        Max Users
                        {subForm.plan === 'Enterprise' && <span className="ml-1 text-slate-500">(0 = unlimited)</span>}
                      </label>
                      <input
                        type="number"
                        min={0}
                        value={subForm.maxUsers}
                        onChange={e => setSubForm(p => ({ ...p, maxUsers: Number(e.target.value) }))}
                        className="w-full bg-white/10 border border-white/10 rounded-lg px-2 py-2 text-sm text-white"
                        placeholder={subForm.plan === 'Enterprise' ? 'Unlimited' : ''}
                      />
                    </div>
                    <div>
                      <label className="block text-xs text-slate-400 mb-1">
                        Max Employees
                        {subForm.plan === 'Enterprise' && <span className="ml-1 text-slate-500">(0 = unlimited)</span>}
                      </label>
                      <input
                        type="number"
                        min={0}
                        value={subForm.maxEmployees}
                        onChange={e => setSubForm(p => ({ ...p, maxEmployees: Number(e.target.value) }))}
                        className="w-full bg-white/10 border border-white/10 rounded-lg px-2 py-2 text-sm text-white"
                        placeholder={subForm.plan === 'Enterprise' ? 'Unlimited' : ''}
                      />
                    </div>
                    <div>
                      <label className="block text-xs text-slate-400 mb-1">Monthly Amount (AED)</label>
                      <input
                        type="number"
                        min={0}
                        value={subForm.monthlyAmount}
                        onChange={e => setSubForm(p => ({ ...p, monthlyAmount: Number(e.target.value) }))}
                        className="w-full bg-white/10 border border-white/10 rounded-lg px-2 py-2 text-sm text-white"
                      />
                    </div>
                    <div>
                      <label className="block text-xs text-slate-400 mb-1">Billing Cycle</label>
                      <select
                        value={subForm.billingCycle}
                        onChange={e => setSubForm(p => ({ ...p, billingCycle: e.target.value }))}
                        className="w-full bg-white/10 border border-white/10 rounded-lg px-2 py-2 text-sm text-white"
                      >
                        <option value="Monthly">Monthly</option>
                        <option value="Annual">Annual</option>
                      </select>
                    </div>
                    <div className="col-span-2">
                      <label className="block text-xs text-slate-400 mb-1">Billing Email</label>
                      <input
                        type="email"
                        value={subForm.billingEmail}
                        onChange={e => setSubForm(p => ({ ...p, billingEmail: e.target.value }))}
                        className="w-full bg-white/10 border border-white/10 rounded-lg px-2 py-2 text-sm text-white"
                      />
                    </div>
                    <div>
                      <label className="block text-xs text-slate-400 mb-1">Start Date</label>
                      <input
                        type="date"
                        value={subForm.startedAtUtc}
                        onChange={e => setSubForm(p => ({ ...p, startedAtUtc: e.target.value }))}
                        className="w-full bg-white/10 border border-white/10 rounded-lg px-2 py-2 text-sm text-white"
                      />
                    </div>
                    <div>
                      <label className="block text-xs text-slate-400 mb-1">
                        Expires At
                        {subForm.plan === 'Enterprise' && <span className="ml-1 text-slate-500">(leave blank = never)</span>}
                      </label>
                      <input
                        type="date"
                        value={subForm.expiresAtUtc}
                        onChange={e => setSubForm(p => ({ ...p, expiresAtUtc: e.target.value }))}
                        className="w-full bg-white/10 border border-white/10 rounded-lg px-2 py-2 text-sm text-white"
                      />
                    </div>
                  </div>
                  {subForm.plan in PLAN_DEFAULTS && (
                    <p className="text-xs text-slate-500">
                      Tier defaults for {subForm.plan}: {PLAN_DEFAULTS[subForm.plan].maxUsers === 0 ? 'Unlimited' : PLAN_DEFAULTS[subForm.plan].maxUsers} users, {PLAN_DEFAULTS[subForm.plan].maxEmployees === 0 ? 'Unlimited' : PLAN_DEFAULTS[subForm.plan].maxEmployees} employees. You can override above for custom deals.
                    </p>
                  )}
                  <div className="flex items-center gap-2 pt-1">
                    <button
                      onClick={saveSubscription}
                      disabled={subSaving}
                      className="px-4 py-2 bg-sapphire hover:opacity-90 disabled:opacity-50 text-white text-xs font-medium rounded-lg transition-colors"
                    >
                      {subSaving ? 'Saving...' : 'Save Changes'}
                    </button>
                    <button
                      onClick={() => setEditingSub(false)}
                      className="px-4 py-2 bg-white/10 hover:bg-white/20 text-slate-300 text-xs font-medium rounded-lg transition-colors"
                    >
                      Cancel
                    </button>
                    {subSaved && <span className="text-xs text-green-400">Saved!</span>}
                  </div>
                </div>
              )}
            </section>

            {/* Feature Flags */}
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
                        className={`relative inline-flex h-5 w-9 items-center rounded-full transition-colors focus:outline-none disabled:opacity-50 ${
                          enabled ? 'bg-sapphire' : 'bg-white/10'
                        }`}
                      >
                        <span className={`inline-block h-3.5 w-3.5 transform rounded-full bg-white shadow transition-transform ${
                          enabled ? 'translate-x-4.5' : 'translate-x-0.5'
                        }`} />
                      </button>
                    </div>
                  );
                })}
              </div>
            </section>

            {/* Impersonation */}
            <section>
              <h3 className="text-sm font-semibold text-slate-300 uppercase tracking-wide mb-3">Impersonate User</h3>
              <div className="bg-darkSlate/60 border border-white/10 rounded-xl p-4 space-y-3">
                <p className="text-xs text-slate-500">Generate a short-lived token to act as a specific user in this tenant. Use with caution.</p>
                <div className="flex gap-2">
                  <input
                    type="text"
                    value={impersonateUserId}
                    onChange={e => setImpersonateUserId(e.target.value)}
                    placeholder="User ID (GUID)"
                    className="flex-1 bg-white/10 border border-white/10 rounded-lg px-3 py-2 text-sm text-white placeholder-slate-600 focus:outline-none focus:border-sapphire"
                  />
                  <button
                    onClick={handleImpersonate}
                    disabled={impersonating || !impersonateUserId.trim()}
                    className="px-4 py-2 bg-amber-600 hover:bg-amber-500 disabled:opacity-50 text-white text-xs font-medium rounded-lg transition-colors whitespace-nowrap"
                  >
                    {impersonating ? 'Generating...' : 'Get Token'}
                  </button>
                </div>
                {impersonateToken && (
                  <div className="space-y-2">
                    <div className="flex items-center gap-2">
                      <input
                        readOnly
                        value={impersonateToken}
                        className="flex-1 bg-white/10 border border-white/10 rounded-lg px-3 py-2 text-xs text-slate-300 font-mono"
                      />
                      <button
                        onClick={copyToken}
                        className="px-3 py-2 bg-white/10 hover:bg-white/20 text-slate-300 text-xs rounded-lg transition-colors whitespace-nowrap"
                      >
                        {copied ? 'Copied!' : 'Copy'}
                      </button>
                    </div>
                    <p className="text-xs text-amber-500">
                      Use this token in the Authorization header as <code className="font-mono">Bearer &lt;token&gt;</code>, or paste it to the login page.
                    </p>
                  </div>
                )}
              </div>
            </section>

          </div>
        ) : (
          <div className="flex-1 flex items-center justify-center text-slate-500 text-sm">
            Failed to load tenant detail.
          </div>
        )}
      </div>
    </div>
  );
}

export default function PlatformDashboard() {
  const router = useRouter();
  const [stats, setStats] = useState<PlatformStats | null>(null);
  const [tenants, setTenants] = useState<PlatformTenantSummary[]>([]);
  const [loadingStats, setLoadingStats] = useState(true);
  const [loadingTenants, setLoadingTenants] = useState(true);
  const [selectedTenant, setSelectedTenant] = useState<PlatformTenantSummary | null>(null);

  useEffect(() => {
    const token = localStorage.getItem('platform_access_token');
    if (!token) {
      router.replace('/platform/login');
      return;
    }
    loadStats();
    loadTenants();
  }, [router]);

  async function loadStats() {
    setLoadingStats(true);
    try { setStats(await platformApi.getStats()); } catch {} finally { setLoadingStats(false); }
  }

  async function loadTenants() {
    setLoadingTenants(true);
    try { setTenants(await platformApi.listTenants()); } catch {} finally { setLoadingTenants(false); }
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
      starter: 'bg-white/10 text-slate-300',
      growth: 'bg-blue-900 text-blue-300',
      enterprise: 'bg-purple-900 text-purple-300',
    };
    return colors[plan?.toLowerCase()] ?? 'bg-white/10 text-slate-300';
  };

  const statusBadge = (status: string) => {
    const colors: Record<string, string> = {
      active: 'bg-green-900 text-green-300',
      trialing: 'bg-cyan-900 text-cyan-300',
      past_due: 'bg-amber-900 text-amber-300',
      canceled: 'bg-rose-500/20 text-rose-300',
      suspended: 'bg-rose-500/20 text-rose-300',
    };
    return colors[status?.toLowerCase()] ?? 'bg-white/10 text-slate-300';
  };

  return (
    <div className="min-h-screen bg-midnight text-white">
      {/* Top bar */}
      <div className="border-b border-white/10 bg-sidebarDark">
        <div className="max-w-7xl mx-auto px-6 py-4 flex items-center justify-between">
          <h1 className="text-lg font-semibold text-white">Platform Administration</h1>
          <button
            onClick={logout}
            className="text-sm text-slate-400 hover:text-white border border-white/10 rounded-lg px-3 py-1.5 transition-colors"
          >
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
              {[...Array(4)].map((_, i) => (
                <div key={i} className="bg-sidebarDark border border-white/10 rounded-xl p-5 animate-pulse h-20" />
              ))}
            </div>
          ) : stats ? (
            <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
              <StatCard label="Total Tenants" value={stats.totalTenants} />
              <StatCard label="Active Tenants" value={stats.activeTenants} />
              <StatCard label="Total Users" value={stats.totalUsers} />
              <StatCard label="Total Employees" value={stats.totalEmployees} />
            </div>
          ) : (
            <p className="text-sm text-slate-500">Failed to load stats.</p>
          )}
        </section>

        {/* Plan breakdown */}
        {stats && (
          <section>
            <h2 className="text-xs font-semibold text-slate-500 uppercase tracking-wider mb-3">Tenants by Plan</h2>
            <div className="grid grid-cols-3 gap-4">
              <StatCard label="Starter" value={stats.tenantsByPlan.starter} />
              <StatCard label="Growth" value={stats.tenantsByPlan.growth} />
              <StatCard label="Enterprise" value={stats.tenantsByPlan.enterprise} />
            </div>
          </section>
        )}

        {/* Tenants table */}
        <section>
          <h2 className="text-xs font-semibold text-slate-500 uppercase tracking-wider mb-3">Tenants</h2>
          <div className="bg-sidebarDark border border-white/10 rounded-xl overflow-hidden">
            {loadingTenants ? (
              <div className="flex items-center justify-center py-12">
                <div className="h-6 w-6 animate-spin rounded-full border-2 border-sapphire border-t-transparent" />
              </div>
            ) : tenants.length === 0 ? (
              <p className="text-center py-12 text-sm text-slate-500">No tenants found.</p>
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
                  {tenants.map(t => (
                    <tr key={t.id} className="hover:bg-darkSlate/60/50 transition-colors">
                      <td className="px-4 py-3 font-medium text-white">{t.name}</td>
                      <td className="px-4 py-3 text-slate-400 font-mono text-xs">/{t.slug}</td>
                      <td className="px-4 py-3">
                        <span className={`inline-block px-2 py-0.5 rounded text-xs font-medium ${planBadge(t.subscription.plan)}`}>
                          {t.subscription.plan}
                        </span>
                      </td>
                      <td className="px-4 py-3">
                        <span className={`inline-block px-2 py-0.5 rounded text-xs font-medium ${statusBadge(t.subscription.status)}`}>
                          {t.subscription.status}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-slate-300">—/{t.subscription.maxEmployees}</td>
                      <td className="px-4 py-3 text-slate-300">{t.activeUserCount}</td>
                      <td className="px-4 py-3 text-slate-400 text-xs">{fmtExpiry(t.subscription.expiresAtUtc)}</td>
                      <td className="px-4 py-3">
                        <button
                          onClick={() => setSelectedTenant(t)}
                          className="px-3 py-1.5 bg-sapphire hover:bg-sapphire text-white text-xs font-medium rounded-lg transition-colors"
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
      </div>

      {/* Slide-over panel */}
      {selectedTenant && (
        <TenantPanel
          tenant={selectedTenant}
          onClose={() => setSelectedTenant(null)}
        />
      )}
    </div>
  );
}
