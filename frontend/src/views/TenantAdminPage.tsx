'use client';

import { useState, useEffect } from 'react';
import {
  tenantAdminApi,
  type TenantFeatureFlag,
  type TenantLocalizationSetting,
  type TenantBranding,
  type TenantSubscription,
} from '../api/intelligence';
import client from '../api/client';

type Tab = 'subscription' | 'features' | 'localization' | 'branding' | 'security' | 'country-rules' | 'audit';

const FEATURE_KEYS = [
  { key: 'ai_assistant', label: 'AI HR Assistant', description: 'Enable natural-language HR queries' },
  { key: 'mobile_app', label: 'Mobile App', description: 'Enable mobile API endpoints' },
  { key: 'wps_export', label: 'WPS/SIF Export', description: 'GCC WPS payroll file generation' },
  { key: 'eosb_calc', label: 'EOSB Calculator', description: 'End-of-service benefit computation' },
  { key: 'resume_screening', label: 'AI Resume Screening', description: 'AI-powered CV analysis (advisory)' },
  { key: 'payroll_ai_validation', label: 'AI Payroll Validation', description: 'AI variance detection for payroll' },
  { key: 'risk_scores', label: 'Employee Risk Scores', description: 'Churn and burnout risk indicators (advisory)' },
  { key: 'hijri_calendar', label: 'Hijri Calendar', description: 'Show Hijri dates alongside Gregorian' },
];

const GCC_COUNTRIES = [
  { code: 'AE', name: 'United Arab Emirates' },
  { code: 'SA', name: 'Saudi Arabia' },
  { code: 'QA', name: 'Qatar' },
  { code: 'KW', name: 'Kuwait' },
  { code: 'BH', name: 'Bahrain' },
  { code: 'OM', name: 'Oman' },
  { code: 'JO', name: 'Jordan' },
  { code: 'EG', name: 'Egypt' },
  { code: 'PK', name: 'Pakistan' },
  { code: 'IN', name: 'India' },
];

interface UsageData {
  activeEmployees: number;
  maxEmployees: number;
  activeUsers: number;
  storageUsedMb: number;
}

interface SecuritySettings {
  passwordMinLength: number;
  passwordRequireUppercase: boolean;
  passwordRequireDigit: boolean;
  passwordRequireSpecialChar: boolean;
  sessionTimeoutMinutes: number;
  maxFailedLoginAttempts: number;
  lockoutDurationMinutes: number;
}

interface CountryRule {
  id: string;
  countryCode: string;
  ruleKey: string;
  ruleValue: string;
  dataType: string;
  isOverride: boolean;
  effectiveFrom: string | null;
  effectiveTo: string | null;
}

// Reusable progress bar using a <meter> element — semantic, no inline styles needed.
function EmployeeProgressBar({ pct }: { pct: number }) {
  const clamped = Math.min(Math.max(pct, 0), 100);
  // Tailwind can't express arbitrary dynamic widths, so we use <meter> which renders
  // a native progress indicator and carries its own accessible semantics.
  const barColor = pct > 95 ? 'accent-red-500' : pct > 80 ? 'accent-orange-400' : 'accent-indigo-500';
  return (
    <meter
      className={`w-full h-2 rounded-full ${barColor}`}
      value={clamped}
      min={0}
      max={100}
      aria-label={`Employee usage: ${Math.round(clamped)}%`}
    />
  );
}

// Simple toggle switch with accessible label
function Toggle({ id, checked, onChange }: { id: string; checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <button
      id={id}
      type="button"
      role="switch"
      aria-checked={checked ? 'true' : 'false'}
      onClick={() => onChange(!checked)}
      className={`relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 ${
        checked ? 'bg-indigo-600' : 'bg-gray-200'
      }`}
    >
      <span className={`inline-block h-4 w-4 transform rounded-full bg-white shadow transition-transform ${
        checked ? 'translate-x-6' : 'translate-x-1'
      }`} />
    </button>
  );
}

export default function TenantAdminPage() {
  const [tab, setTab] = useState<Tab>('subscription');
  const [subscription, setSubscription] = useState<TenantSubscription | null>(null);
  const [flags, setFlags] = useState<TenantFeatureFlag[]>([]);
  const [localization, setLocalization] = useState<TenantLocalizationSetting | null>(null);
  const [branding, setBranding] = useState<TenantBranding | null>(null);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);

  // Usage
  const [usage, setUsage] = useState<UsageData | null>(null);

  // Security
  const [security, setSecurity] = useState<SecuritySettings | null>(null);
  const [securitySaving, setSecuritySaving] = useState(false);
  const [securitySaved, setSecuritySaved] = useState(false);
  const [securityError, setSecurityError] = useState('');

  // Country rules
  const [rules, setRules] = useState<CountryRule[]>([]);
  const [rulesLoading, setRulesLoading] = useState(false);
  const [newRule, setNewRule] = useState({ countryCode: 'AE', ruleKey: '', ruleValue: '', dataType: 'string' });
  const [addingRule, setAddingRule] = useState(false);
  const [showAddRule, setShowAddRule] = useState(false);

  useEffect(() => {
    if (tab === 'subscription') { loadSubscription(); loadUsage(); }
    if (tab === 'features') loadFlags();
    if (tab === 'localization') loadLocalization();
    if (tab === 'branding') loadBranding();
    if (tab === 'security') loadSecurity();
    if (tab === 'country-rules') loadRules();
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tab]);

  async function loadSubscription() {
    try { setSubscription(await tenantAdminApi.getSubscription()); } catch {}
  }

  async function loadUsage() {
    try {
      const data = await client.get<UsageData>('/api/tenant-admin/usage').then(r => r.data);
      setUsage(data);
    } catch {}
  }

  async function loadFlags() {
    try { setFlags(await tenantAdminApi.listFeatureFlags()); } catch {}
  }

  async function loadLocalization() {
    try { setLocalization(await tenantAdminApi.getLocalization()); } catch {}
  }

  async function loadBranding() {
    try { setBranding(await tenantAdminApi.getBranding()); } catch {}
  }

  async function loadSecurity() {
    try {
      const data = await client.get<SecuritySettings>('/api/access/security-settings').then(r => r.data);
      setSecurity(data);
    } catch {}
  }

  async function loadRules() {
    setRulesLoading(true);
    try {
      const data = await client.get<CountryRule[]>('/api/tenant-admin/country-rules').then(r => r.data);
      setRules(data);
    } catch {} finally {
      setRulesLoading(false);
    }
  }

  async function toggleFlag(key: string, current: boolean) {
    try {
      const updated = await tenantAdminApi.setFeatureFlag(key, !current);
      setFlags(prev => {
        const exists = prev.find(f => f.featureKey === key);
        if (exists) return prev.map(f => f.featureKey === key ? updated : f);
        return [...prev, updated];
      });
    } catch {}
  }

  function isFlagEnabled(key: string) {
    return flags.find(f => f.featureKey === key)?.isEnabled ?? false;
  }

  async function saveLocalization() {
    if (!localization) return;
    setSaving(true);
    try {
      const updated = await tenantAdminApi.upsertLocalization(localization);
      setLocalization(updated);
      setSaved(true);
      setTimeout(() => setSaved(false), 2000);
    } catch {} finally {
      setSaving(false);
    }
  }

  async function saveBranding() {
    if (!branding) return;
    setSaving(true);
    try {
      const updated = await tenantAdminApi.upsertBranding(branding);
      setBranding(updated);
      setSaved(true);
      setTimeout(() => setSaved(false), 2000);
    } catch {} finally {
      setSaving(false);
    }
  }

  async function saveSecurity() {
    if (!security) return;
    setSecuritySaving(true);
    setSecurityError('');
    try {
      await client.put('/api/access/security-settings', security);
      setSecuritySaved(true);
      setTimeout(() => setSecuritySaved(false), 2000);
    } catch {
      setSecurityError('Failed to save security settings. Please try again.');
    } finally {
      setSecuritySaving(false);
    }
  }

  async function deleteRule(id: string) {
    try {
      await client.delete(`/api/tenant-admin/country-rules/${id}`);
      setRules(prev => prev.filter(r => r.id !== id));
    } catch {}
  }

  async function addRule() {
    if (!newRule.ruleKey.trim() || !newRule.ruleValue.trim()) return;
    setAddingRule(true);
    try {
      const created = await client.post<CountryRule>('/api/tenant-admin/country-rules', newRule).then(r => r.data);
      setRules(prev => [...prev, created]);
      setNewRule({ countryCode: 'AE', ruleKey: '', ruleValue: '', dataType: 'string' });
      setShowAddRule(false);
    } catch {} finally {
      setAddingRule(false);
    }
  }

  function getCountryName(code: string) {
    return GCC_COUNTRIES.find(c => c.code === code)?.name ?? code;
  }

  // Group rules by country
  const rulesByCountry = rules.reduce<Record<string, CountryRule[]>>((acc, r) => {
    if (!acc[r.countryCode]) acc[r.countryCode] = [];
    acc[r.countryCode].push(r);
    return acc;
  }, {});

  const tabs: { id: Tab; label: string }[] = [
    { id: 'subscription', label: 'Subscription' },
    { id: 'features', label: 'Feature Flags' },
    { id: 'localization', label: 'Localization' },
    { id: 'branding', label: 'Branding' },
    { id: 'security', label: 'Security' },
    { id: 'country-rules', label: 'Country Rules' },
    { id: 'audit', label: 'Audit Log' },
  ];

  return (
    <div className="p-6 max-w-4xl mx-auto space-y-4">
      <div>
        <h1 className="text-2xl font-bold text-gray-900">Tenant Administration</h1>
        <p className="text-sm text-gray-500">Manage subscription, feature flags, localization, and branding</p>
      </div>

      <div className="flex gap-1 border-b border-gray-200 flex-wrap">
        {tabs.map(t => (
          <button
            key={t.id}
            type="button"
            onClick={() => setTab(t.id)}
            className={`px-4 py-2.5 text-sm font-medium border-b-2 transition-colors ${
              tab === t.id ? 'border-indigo-600 text-indigo-600' : 'border-transparent text-gray-500 hover:text-gray-700'
            }`}
          >
            {t.label}
          </button>
        ))}
      </div>

      {/* Subscription */}
      {tab === 'subscription' && (
        <div className="space-y-4">
          <div className="bg-white rounded-xl border border-gray-200 p-6">
            <h2 className="text-lg font-semibold text-gray-900 mb-4">Current Subscription</h2>
            {!subscription ? (
              <p className="text-sm text-gray-500">No subscription found. Contact your KynexOne account manager.</p>
            ) : (
              <dl className="grid grid-cols-2 gap-x-6 gap-y-4">
                {[
                  { label: 'Plan', value: subscription.plan },
                  { label: 'Monthly Amount', value: `${subscription.currencyCode} ${subscription.monthlyAmount}` },
                  { label: 'Status', value: subscription.status },
                  { label: 'Billing Email', value: subscription.billingEmail },
                  { label: 'Max Employees', value: subscription.maxEmployees },
                  { label: 'Started', value: new Date(subscription.startedAtUtc).toLocaleDateString() },
                  { label: 'Billing Cycle', value: subscription.billingCycle },
                  { label: 'Expires', value: subscription.expiresAtUtc ? new Date(subscription.expiresAtUtc).toLocaleDateString() : 'Never' },
                ].map(item => (
                  <div key={item.label}>
                    <dt className="text-xs font-medium text-gray-500">{item.label}</dt>
                    <dd className="text-sm font-semibold text-gray-900 mt-0.5">{String(item.value)}</dd>
                  </div>
                ))}
              </dl>
            )}
          </div>

          {/* Usage section */}
          {usage && (
            <div className="bg-white rounded-xl border border-gray-200 p-6">
              <h2 className="text-lg font-semibold text-gray-900 mb-4">Usage</h2>
              <div className="space-y-4">
                {/* Employees progress */}
                <div>
                  <div className="flex items-center justify-between mb-1.5">
                    <span className="text-sm font-medium text-gray-700">Active Employees</span>
                    <span className="text-sm text-gray-500">{usage.activeEmployees} / {usage.maxEmployees}</span>
                  </div>
                  <EmployeeProgressBar
                    pct={usage.maxEmployees > 0 ? (usage.activeEmployees / usage.maxEmployees) * 100 : 0}
                  />
                  {usage.maxEmployees > 0 && usage.activeEmployees / usage.maxEmployees > 0.8 && (
                    <p className={`text-xs mt-1 ${usage.activeEmployees / usage.maxEmployees > 0.95 ? 'text-red-500' : 'text-orange-500'}`}>
                      {usage.activeEmployees / usage.maxEmployees > 0.95
                        ? 'Critical: approaching employee limit'
                        : 'Warning: employee limit approaching'}
                    </p>
                  )}
                </div>

                <dl className="grid grid-cols-2 gap-4">
                  <div className="bg-gray-50 rounded-lg px-4 py-3">
                    <dt className="text-xs font-medium text-gray-500">Active Users</dt>
                    <dd className="text-xl font-bold text-gray-900 mt-0.5">{usage.activeUsers}</dd>
                  </div>
                  <div className="bg-gray-50 rounded-lg px-4 py-3">
                    <dt className="text-xs font-medium text-gray-500">Storage Used</dt>
                    <dd className="text-xl font-bold text-gray-900 mt-0.5">{usage.storageUsedMb} MB</dd>
                  </div>
                </dl>
              </div>
            </div>
          )}
        </div>
      )}

      {/* Feature Flags */}
      {tab === 'features' && (
        <div className="bg-white rounded-xl border border-gray-200 divide-y divide-gray-100">
          {FEATURE_KEYS.map(feat => {
            const enabled = isFlagEnabled(feat.key);
            return (
              <div key={feat.key} className="flex items-center justify-between px-6 py-4">
                <div>
                  <p className="text-sm font-medium text-gray-900">{feat.label}</p>
                  <p className="text-xs text-gray-500 mt-0.5">{feat.description}</p>
                </div>
                <Toggle
                  id={`flag-${feat.key}`}
                  checked={enabled}
                  onChange={() => toggleFlag(feat.key, enabled)}
                />
              </div>
            );
          })}
        </div>
      )}

      {/* Localization */}
      {tab === 'localization' && localization && (
        <div className="bg-white rounded-xl border border-gray-200 p-6 space-y-4">
          <h2 className="text-lg font-semibold text-gray-900">Localization Settings</h2>
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label htmlFor="loc-language" className="block text-xs font-medium text-gray-700 mb-1">Default Language</label>
              <select
                id="loc-language"
                value={localization.defaultLanguage}
                onChange={e => setLocalization(p => p ? { ...p, defaultLanguage: e.target.value } : p)}
                className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm"
              >
                <option value="en">English</option>
                <option value="ar">Arabic</option>
              </select>
            </div>
            <div>
              <label htmlFor="loc-calendar" className="block text-xs font-medium text-gray-700 mb-1">Calendar System</label>
              <select
                id="loc-calendar"
                value={localization.calendarSystem}
                onChange={e => setLocalization(p => p ? { ...p, calendarSystem: e.target.value } : p)}
                className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm"
              >
                <option value="Gregorian">Gregorian</option>
                <option value="Hijri">Hijri</option>
              </select>
            </div>
            <div>
              <label htmlFor="loc-country" className="block text-xs font-medium text-gray-700 mb-1">Country Code</label>
              <select
                id="loc-country"
                value={localization.countryCode}
                onChange={e => setLocalization(p => p ? { ...p, countryCode: e.target.value } : p)}
                className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm"
              >
                {['AE', 'SA', 'QA', 'KW', 'BH', 'OM', 'JO', 'EG', 'PK', 'IN'].map(c => <option key={c}>{c}</option>)}
              </select>
            </div>
            <div>
              <label htmlFor="loc-currency" className="block text-xs font-medium text-gray-700 mb-1">Currency</label>
              <select
                id="loc-currency"
                value={localization.currencyCode}
                onChange={e => setLocalization(p => p ? { ...p, currencyCode: e.target.value } : p)}
                className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm"
              >
                {['AED', 'SAR', 'QAR', 'KWD', 'BHD', 'OMR', 'JOD', 'EGP', 'PKR', 'INR', 'USD'].map(c => <option key={c}>{c}</option>)}
              </select>
            </div>
            <div>
              <label htmlFor="loc-workweek" className="block text-xs font-medium text-gray-700 mb-1">Work Week</label>
              <select
                id="loc-workweek"
                value={localization.workWeek}
                onChange={e => setLocalization(p => p ? { ...p, workWeek: e.target.value } : p)}
                className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm"
              >
                <option value="Sun-Thu">Sun–Thu (GCC)</option>
                <option value="Mon-Fri">Mon–Fri</option>
                <option value="Mon-Sat">Mon–Sat</option>
              </select>
            </div>
            <div>
              <label htmlFor="loc-timezone" className="block text-xs font-medium text-gray-700 mb-1">Timezone</label>
              <select
                id="loc-timezone"
                value={localization.defaultTimezone}
                onChange={e => setLocalization(p => p ? { ...p, defaultTimezone: e.target.value } : p)}
                className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm"
              >
                {['Asia/Dubai', 'Asia/Riyadh', 'Asia/Kuwait', 'Asia/Bahrain', 'Asia/Muscat', 'Asia/Doha', 'Africa/Cairo', 'Asia/Karachi', 'Asia/Kolkata'].map(tz => (
                  <option key={tz}>{tz}</option>
                ))}
              </select>
            </div>
          </div>

          <div className="flex items-center gap-6">
            <div className="flex items-center gap-2">
              <Toggle
                id="loc-rtl"
                checked={localization.rtlEnabled}
                onChange={v => setLocalization(p => p ? { ...p, rtlEnabled: v } : p)}
              />
              <label htmlFor="loc-rtl" className="text-sm text-gray-700 cursor-pointer">RTL Layout</label>
            </div>
            <div className="flex items-center gap-2">
              <Toggle
                id="loc-hijri"
                checked={localization.hijriDatesEnabled}
                onChange={v => setLocalization(p => p ? { ...p, hijriDatesEnabled: v } : p)}
              />
              <label htmlFor="loc-hijri" className="text-sm text-gray-700 cursor-pointer">Show Hijri Dates</label>
            </div>
          </div>

          <div className="flex items-center gap-3">
            <button
              type="button"
              onClick={saveLocalization}
              disabled={saving}
              className="px-6 py-2.5 bg-indigo-600 text-white rounded-lg text-sm font-medium disabled:opacity-50 hover:bg-indigo-700"
            >
              {saving ? 'Saving...' : 'Save Settings'}
            </button>
            {saved && <span className="text-sm text-green-600 font-medium">Saved!</span>}
          </div>
        </div>
      )}

      {/* Branding */}
      {tab === 'branding' && (
        <div className="bg-white rounded-xl border border-gray-200 p-6 space-y-4">
          <h2 className="text-lg font-semibold text-gray-900">Branding</h2>
          {branding && (
            <>
              <div className="grid grid-cols-2 gap-4">
                {[
                  { key: 'companyNameEn', label: 'Company Name (English)' },
                  { key: 'companyNameAr', label: 'Company Name (Arabic)' },
                  { key: 'portalTitle', label: 'Portal Title' },
                  { key: 'logoUrl', label: 'Logo URL' },
                  { key: 'faviconUrl', label: 'Favicon URL' },
                ].map(field => (
                  <div key={field.key}>
                    <label htmlFor={`brand-${field.key}`} className="block text-xs font-medium text-gray-700 mb-1">{field.label}</label>
                    <input
                      id={`brand-${field.key}`}
                      value={branding[field.key as keyof TenantBranding] as string}
                      onChange={e => setBranding(p => p ? { ...p, [field.key]: e.target.value } : p)}
                      className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm"
                    />
                  </div>
                ))}
                <div>
                  <label htmlFor="brand-primaryColor-text" className="block text-xs font-medium text-gray-700 mb-1">Primary Color</label>
                  <div className="flex items-center gap-2">
                    <input
                      id="brand-primaryColor-picker"
                      type="color"
                      aria-label="Primary color picker"
                      value={branding.primaryColor}
                      onChange={e => setBranding(p => p ? { ...p, primaryColor: e.target.value } : p)}
                      className="h-9 w-16 border border-gray-200 rounded cursor-pointer"
                    />
                    <input
                      id="brand-primaryColor-text"
                      value={branding.primaryColor}
                      onChange={e => setBranding(p => p ? { ...p, primaryColor: e.target.value } : p)}
                      className="flex-1 border border-gray-200 rounded-lg px-3 py-2 text-sm font-mono"
                    />
                  </div>
                </div>
              </div>
              <div className="flex items-center gap-3 pt-2">
                <button
                  type="button"
                  onClick={saveBranding}
                  disabled={saving}
                  className="px-6 py-2.5 bg-indigo-600 text-white rounded-lg text-sm font-medium disabled:opacity-50 hover:bg-indigo-700"
                >
                  {saving ? 'Saving...' : 'Save Branding'}
                </button>
                {saved && <span className="text-sm text-green-600 font-medium">Saved!</span>}
              </div>
            </>
          )}
          {!branding && (
            <div className="text-center py-8 text-sm text-gray-400">
              <p>No branding configured. Default KynexOne branding is active.</p>
              <button
                type="button"
                onClick={() => setBranding({ logoUrl: '', primaryColor: '#2563EB', accentColor: '#7C3AED', companyNameEn: '', companyNameAr: '', portalTitle: 'HR Portal', faviconUrl: '' })}
                className="mt-3 px-4 py-2 bg-indigo-600 text-white rounded-lg text-sm"
              >
                Configure Branding
              </button>
            </div>
          )}
        </div>
      )}

      {/* Security */}
      {tab === 'security' && (
        <div className="bg-white rounded-xl border border-gray-200 p-6 space-y-5">
          <h2 className="text-lg font-semibold text-gray-900">Security Settings</h2>
          {!security ? (
            <p className="text-sm text-gray-500">Loading security settings...</p>
          ) : (
            <>
              <div className="space-y-4">
                <h3 className="text-sm font-semibold text-gray-700 border-b border-gray-100 pb-2">Password Policy</h3>
                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label htmlFor="sec-minlen" className="block text-xs font-medium text-gray-700 mb-1">
                      Minimum Length <span className="text-gray-400">(6–32)</span>
                    </label>
                    <input
                      id="sec-minlen"
                      type="number"
                      min={6}
                      max={32}
                      value={security.passwordMinLength}
                      onChange={e => setSecurity(p => p ? { ...p, passwordMinLength: Number(e.target.value) } : p)}
                      className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm"
                    />
                  </div>
                </div>

                <div className="flex flex-wrap gap-6">
                  {[
                    { key: 'passwordRequireUppercase', label: 'Require Uppercase Letter', id: 'sec-upper' },
                    { key: 'passwordRequireDigit', label: 'Require Digit', id: 'sec-digit' },
                    { key: 'passwordRequireSpecialChar', label: 'Require Special Character', id: 'sec-special' },
                  ].map(opt => (
                    <div key={opt.key} className="flex items-center gap-2">
                      <Toggle
                        id={opt.id}
                        checked={security[opt.key as keyof SecuritySettings] as boolean}
                        onChange={v => setSecurity(p => p ? { ...p, [opt.key]: v } : p)}
                      />
                      <label htmlFor={opt.id} className="text-sm text-gray-700 cursor-pointer">{opt.label}</label>
                    </div>
                  ))}
                </div>
              </div>

              <div className="space-y-4">
                <h3 className="text-sm font-semibold text-gray-700 border-b border-gray-100 pb-2">Session &amp; Lockout</h3>
                <div className="grid grid-cols-3 gap-4">
                  <div>
                    <label htmlFor="sec-timeout" className="block text-xs font-medium text-gray-700 mb-1">Session Timeout (min)</label>
                    <input
                      id="sec-timeout"
                      type="number"
                      min={1}
                      value={security.sessionTimeoutMinutes}
                      onChange={e => setSecurity(p => p ? { ...p, sessionTimeoutMinutes: Number(e.target.value) } : p)}
                      className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm"
                    />
                  </div>
                  <div>
                    <label htmlFor="sec-maxfail" className="block text-xs font-medium text-gray-700 mb-1">
                      Max Failed Attempts <span className="text-gray-400">(1–20)</span>
                    </label>
                    <input
                      id="sec-maxfail"
                      type="number"
                      min={1}
                      max={20}
                      value={security.maxFailedLoginAttempts}
                      onChange={e => setSecurity(p => p ? { ...p, maxFailedLoginAttempts: Number(e.target.value) } : p)}
                      className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm"
                    />
                  </div>
                  <div>
                    <label htmlFor="sec-lockout" className="block text-xs font-medium text-gray-700 mb-1">Lockout Duration (min)</label>
                    <input
                      id="sec-lockout"
                      type="number"
                      min={1}
                      value={security.lockoutDurationMinutes}
                      onChange={e => setSecurity(p => p ? { ...p, lockoutDurationMinutes: Number(e.target.value) } : p)}
                      className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm"
                    />
                  </div>
                </div>
              </div>

              {securityError && (
                <p className="text-sm text-red-600 bg-red-50 border border-red-100 rounded-lg px-3 py-2">{securityError}</p>
              )}

              <div className="flex items-center gap-3 pt-1">
                <button
                  type="button"
                  onClick={saveSecurity}
                  disabled={securitySaving}
                  className="px-6 py-2.5 bg-indigo-600 text-white rounded-lg text-sm font-medium disabled:opacity-50 hover:bg-indigo-700"
                >
                  {securitySaving ? 'Saving...' : 'Save Settings'}
                </button>
                {securitySaved && <span className="text-sm text-green-600 font-medium">Saved!</span>}
              </div>
            </>
          )}
        </div>
      )}

      {/* Country Rules */}
      {tab === 'country-rules' && (
        <div className="space-y-4">
          <div className="flex items-center justify-between">
            <h2 className="text-lg font-semibold text-gray-900">Country Rules</h2>
            <button
              type="button"
              onClick={() => setShowAddRule(v => !v)}
              className="px-4 py-2 bg-indigo-600 text-white rounded-lg text-sm font-medium hover:bg-indigo-700"
            >
              {showAddRule ? 'Cancel' : '+ Add Rule'}
            </button>
          </div>

          {showAddRule && (
            <div className="bg-white rounded-xl border border-gray-200 p-5 space-y-4">
              <h3 className="text-sm font-semibold text-gray-900">New Rule</h3>
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label htmlFor="rule-country" className="block text-xs font-medium text-gray-700 mb-1">Country</label>
                  <select
                    id="rule-country"
                    value={newRule.countryCode}
                    onChange={e => setNewRule(p => ({ ...p, countryCode: e.target.value }))}
                    className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm"
                  >
                    {GCC_COUNTRIES.map(c => (
                      <option key={c.code} value={c.code}>{c.name} ({c.code})</option>
                    ))}
                  </select>
                </div>
                <div>
                  <label htmlFor="rule-datatype" className="block text-xs font-medium text-gray-700 mb-1">Data Type</label>
                  <select
                    id="rule-datatype"
                    value={newRule.dataType}
                    onChange={e => setNewRule(p => ({ ...p, dataType: e.target.value }))}
                    className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm"
                  >
                    <option value="string">String</option>
                    <option value="number">Number</option>
                    <option value="bool">Boolean</option>
                  </select>
                </div>
                <div>
                  <label htmlFor="rule-key" className="block text-xs font-medium text-gray-700 mb-1">Rule Key</label>
                  <input
                    id="rule-key"
                    value={newRule.ruleKey}
                    onChange={e => setNewRule(p => ({ ...p, ruleKey: e.target.value }))}
                    placeholder="e.g. min_wage"
                    className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm"
                  />
                </div>
                <div>
                  <label htmlFor="rule-value" className="block text-xs font-medium text-gray-700 mb-1">Rule Value</label>
                  <input
                    id="rule-value"
                    value={newRule.ruleValue}
                    onChange={e => setNewRule(p => ({ ...p, ruleValue: e.target.value }))}
                    placeholder="e.g. 4000"
                    className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm"
                  />
                </div>
              </div>
              <button
                type="button"
                onClick={addRule}
                disabled={addingRule || !newRule.ruleKey.trim() || !newRule.ruleValue.trim()}
                className="px-5 py-2 bg-indigo-600 text-white rounded-lg text-sm font-medium disabled:opacity-50 hover:bg-indigo-700"
              >
                {addingRule ? 'Adding...' : 'Add Rule'}
              </button>
            </div>
          )}

          {rulesLoading ? (
            <div className="bg-white rounded-xl border border-gray-200 p-8 text-center text-sm text-gray-400">Loading...</div>
          ) : rules.length === 0 ? (
            <div className="bg-white rounded-xl border border-gray-200 p-8 text-center text-sm text-gray-400">
              No country rules configured.
            </div>
          ) : (
            <div className="space-y-3">
              {Object.entries(rulesByCountry).map(([code, countryRules]) => (
                <div key={code} className="bg-white rounded-xl border border-gray-200 overflow-hidden">
                  <div className="px-5 py-3 bg-gray-50 border-b border-gray-100">
                    <p className="text-sm font-semibold text-gray-800">
                      {getCountryName(code)} <span className="text-gray-400 font-normal ml-1">({code})</span>
                    </p>
                  </div>
                  <table className="w-full text-sm">
                    <thead>
                      <tr className="border-b border-gray-100">
                        {['Rule Key', 'Rule Value', 'Type', 'Override', 'Eff. From', 'Eff. To', 'Actions'].map(col => (
                          <th key={col} className="text-left text-xs font-medium text-gray-500 px-4 py-2">{col}</th>
                        ))}
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-50">
                      {countryRules.map(rule => (
                        <tr key={rule.id} className="hover:bg-gray-50 transition-colors">
                          <td className="px-4 py-2.5 font-mono text-xs text-gray-700">{rule.ruleKey}</td>
                          <td className="px-4 py-2.5 text-gray-900">{rule.ruleValue}</td>
                          <td className="px-4 py-2.5 text-gray-500">{rule.dataType}</td>
                          <td className="px-4 py-2.5">
                            {rule.isOverride ? (
                              <span className="inline-block px-1.5 py-0.5 bg-amber-100 text-amber-700 rounded text-xs">Override</span>
                            ) : (
                              <span className="text-gray-400">—</span>
                            )}
                          </td>
                          <td className="px-4 py-2.5 text-xs text-gray-400">{rule.effectiveFrom ? new Date(rule.effectiveFrom).toLocaleDateString() : '—'}</td>
                          <td className="px-4 py-2.5 text-xs text-gray-400">{rule.effectiveTo ? new Date(rule.effectiveTo).toLocaleDateString() : '—'}</td>
                          <td className="px-4 py-2.5">
                            <button
                              type="button"
                              onClick={() => deleteRule(rule.id)}
                              aria-label={`Delete rule ${rule.ruleKey}`}
                              className="text-xs text-red-500 hover:text-red-700 transition-colors"
                            >
                              Delete
                            </button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* Audit Log */}
      {tab === 'audit' && (
        <div className="bg-white rounded-xl border border-gray-200 p-12 text-center">
          <div className="inline-flex items-center justify-center w-12 h-12 bg-gray-100 rounded-xl mb-4">
            <svg className="w-6 h-6 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" />
            </svg>
          </div>
          <h3 className="text-sm font-semibold text-gray-700 mb-1">Audit Log</h3>
          <p className="text-sm text-gray-400">Audit log coming soon. This will display all administrative actions performed within this tenant.</p>
        </div>
      )}
    </div>
  );
}
