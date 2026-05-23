import { useState, useEffect } from 'react';
import {
  tenantAdminApi,
  type TenantFeatureFlag,
  type TenantLocalizationSetting,
  type TenantBranding,
  type TenantSubscription,
} from '../api/intelligence';

type Tab = 'subscription' | 'features' | 'localization' | 'branding';

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

export default function TenantAdminPage() {
  const [tab, setTab] = useState<Tab>('subscription');
  const [subscription, setSubscription] = useState<TenantSubscription | null>(null);
  const [flags, setFlags] = useState<TenantFeatureFlag[]>([]);
  const [localization, setLocalization] = useState<TenantLocalizationSetting | null>(null);
  const [branding, setBranding] = useState<TenantBranding | null>(null);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    if (tab === 'subscription') loadSubscription();
    if (tab === 'features') loadFlags();
    if (tab === 'localization') loadLocalization();
    if (tab === 'branding') loadBranding();
  }, [tab]);

  async function loadSubscription() {
    try { setSubscription(await tenantAdminApi.getSubscription()); } catch {}
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

  const tabs: { id: Tab; label: string }[] = [
    { id: 'subscription', label: 'Subscription' },
    { id: 'features', label: 'Feature Flags' },
    { id: 'localization', label: 'Localization' },
    { id: 'branding', label: 'Branding' },
  ];

  return (
    <div className="p-6 max-w-4xl mx-auto space-y-4">
      <div>
        <h1 className="text-2xl font-bold text-gray-900">Tenant Administration</h1>
        <p className="text-sm text-gray-500">Manage subscription, feature flags, localization, and branding</p>
      </div>

      <div className="flex gap-1 border-b border-gray-200">
        {tabs.map(t => (
          <button
            key={t.id}
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
        <div className="bg-white rounded-xl border border-gray-200 p-6">
          <h2 className="text-lg font-semibold text-gray-900 mb-4">Current Subscription</h2>
          {!subscription ? (
            <p className="text-sm text-gray-500">No subscription found. Contact your Zayra account manager.</p>
          ) : (
            <div className="grid grid-cols-2 gap-6">
              <div className="space-y-4">
                {[
                  { label: 'Plan', value: subscription.plan },
                  { label: 'Status', value: subscription.status },
                  { label: 'Max Employees', value: subscription.maxEmployees },
                  { label: 'Billing Cycle', value: subscription.billingCycle },
                ].map(item => (
                  <div key={item.label}>
                    <dt className="text-xs font-medium text-gray-500">{item.label}</dt>
                    <dd className="text-sm font-semibold text-gray-900 mt-0.5">{String(item.value)}</dd>
                  </div>
                ))}
              </div>
              <div className="space-y-4">
                {[
                  { label: 'Monthly Amount', value: `${subscription.currencyCode} ${subscription.monthlyAmount}` },
                  { label: 'Billing Email', value: subscription.billingEmail },
                  { label: 'Started', value: new Date(subscription.startedAtUtc).toLocaleDateString() },
                  { label: 'Expires', value: subscription.expiresAtUtc ? new Date(subscription.expiresAtUtc).toLocaleDateString() : 'Never' },
                ].map(item => (
                  <div key={item.label}>
                    <dt className="text-xs font-medium text-gray-500">{item.label}</dt>
                    <dd className="text-sm font-semibold text-gray-900 mt-0.5">{item.value}</dd>
                  </div>
                ))}
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
                <button
                  onClick={() => toggleFlag(feat.key, enabled)}
                  className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors focus:outline-none ${
                    enabled ? 'bg-indigo-600' : 'bg-gray-200'
                  }`}
                >
                  <span className={`inline-block h-4 w-4 transform rounded-full bg-white shadow transition-transform ${
                    enabled ? 'translate-x-6' : 'translate-x-1'
                  }`} />
                </button>
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
              <label className="block text-xs font-medium text-gray-700 mb-1">Default Language</label>
              <select
                value={localization.defaultLanguage}
                onChange={e => setLocalization(p => p ? { ...p, defaultLanguage: e.target.value } : p)}
                className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm"
              >
                <option value="en">English</option>
                <option value="ar">Arabic</option>
              </select>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-700 mb-1">Calendar System</label>
              <select
                value={localization.calendarSystem}
                onChange={e => setLocalization(p => p ? { ...p, calendarSystem: e.target.value } : p)}
                className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm"
              >
                <option value="Gregorian">Gregorian</option>
                <option value="Hijri">Hijri</option>
              </select>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-700 mb-1">Country Code</label>
              <select
                value={localization.countryCode}
                onChange={e => setLocalization(p => p ? { ...p, countryCode: e.target.value } : p)}
                className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm"
              >
                {['AE', 'SA', 'QA', 'KW', 'BH', 'OM', 'JO', 'EG', 'PK', 'IN'].map(c => <option key={c}>{c}</option>)}
              </select>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-700 mb-1">Currency</label>
              <select
                value={localization.currencyCode}
                onChange={e => setLocalization(p => p ? { ...p, currencyCode: e.target.value } : p)}
                className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm"
              >
                {['AED', 'SAR', 'QAR', 'KWD', 'BHD', 'OMR', 'JOD', 'EGP', 'PKR', 'INR', 'USD'].map(c => <option key={c}>{c}</option>)}
              </select>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-700 mb-1">Work Week</label>
              <select
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
              <label className="block text-xs font-medium text-gray-700 mb-1">Timezone</label>
              <select
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

          <div className="flex items-center gap-4">
            {[
              { key: 'rtlEnabled', label: 'RTL Layout' },
              { key: 'hijriDatesEnabled', label: 'Show Hijri Dates' },
            ].map(opt => (
              <label key={opt.key} className="flex items-center gap-2 cursor-pointer">
                <input
                  type="checkbox"
                  checked={localization[opt.key as keyof TenantLocalizationSetting] as boolean}
                  onChange={e => setLocalization(p => p ? { ...p, [opt.key]: e.target.checked } : p)}
                  className="rounded"
                />
                <span className="text-sm text-gray-700">{opt.label}</span>
              </label>
            ))}
          </div>

          <div className="flex items-center gap-3">
            <button
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
                    <label className="block text-xs font-medium text-gray-700 mb-1">{field.label}</label>
                    <input
                      value={branding[field.key as keyof TenantBranding] as string}
                      onChange={e => setBranding(p => p ? { ...p, [field.key]: e.target.value } : p)}
                      className="w-full border border-gray-200 rounded-lg px-3 py-2 text-sm"
                    />
                  </div>
                ))}
                <div>
                  <label className="block text-xs font-medium text-gray-700 mb-1">Primary Color</label>
                  <div className="flex items-center gap-2">
                    <input
                      type="color"
                      value={branding.primaryColor}
                      onChange={e => setBranding(p => p ? { ...p, primaryColor: e.target.value } : p)}
                      className="h-9 w-16 border border-gray-200 rounded cursor-pointer"
                    />
                    <input
                      value={branding.primaryColor}
                      onChange={e => setBranding(p => p ? { ...p, primaryColor: e.target.value } : p)}
                      className="flex-1 border border-gray-200 rounded-lg px-3 py-2 text-sm font-mono"
                    />
                  </div>
                </div>
              </div>
              <div className="flex items-center gap-3 pt-2">
                <button
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
              <p>No branding configured. Default Zayra branding is active.</p>
              <button
                onClick={() => setBranding({ logoUrl: '', primaryColor: '#2563EB', accentColor: '#7C3AED', companyNameEn: '', companyNameAr: '', portalTitle: 'HR Portal', faviconUrl: '' })}
                className="mt-3 px-4 py-2 bg-indigo-600 text-white rounded-lg text-sm"
              >
                Configure Branding
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
