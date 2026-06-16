'use client';

import { useState, useEffect, useCallback } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, RefreshCw, X, Shield, CheckCircle } from 'lucide-react';
import { platformApi, type TenantSecurityPolicy } from '@/src/api/platform';

function Toggle({ checked, onChange, label }: { checked: boolean; onChange: (v: boolean) => void; label: string }) {
  return (
    <label className="flex items-center justify-between cursor-pointer py-1">
      <span className="text-sm text-slate-300">{label}</span>
      <button
        type="button"
        role="switch"
        aria-checked={checked ? 'true' : 'false'}
        onClick={() => onChange(!checked)}
        className={`relative h-5 w-9 rounded-full border transition-colors shrink-0 ${
          checked ? 'bg-sapphire border-sapphire/60' : 'bg-white/10 border-white/10'
        }`}
      >
        <span className={`absolute top-0.5 h-4 w-4 rounded-full bg-white shadow transition-transform ${
          checked ? 'translate-x-4' : 'translate-x-0.5'
        }`} />
      </button>
    </label>
  );
}

function NumInput({ label, value, onChange, min, max, step = 1, unit }: {
  label: string; value: number; onChange: (v: number) => void;
  min?: number; max?: number; step?: number; unit?: string;
}) {
  return (
    <div className="flex items-center justify-between py-1">
      <span className="text-sm text-slate-300">{label}</span>
      <div className="flex items-center gap-1.5">
        <input
          type="number"
          value={value}
          min={min}
          max={max}
          step={step}
          onChange={e => onChange(parseInt(e.target.value) || 0)}
          aria-label={label}
          className="w-20 bg-white/[0.04] border border-white/[0.08] rounded-lg px-2 py-1 text-sm text-white text-right focus:outline-none focus:border-sapphire/60"
        />
        {unit && <span className="text-xs text-slate-600 w-10">{unit}</span>}
      </div>
    </div>
  );
}

export default function TenantSecurityPage() {
  const { id } = useParams<{ id: string }>();
  const [policy, setPolicy]   = useState<TenantSecurityPolicy | null>(null);
  const [form, setForm]       = useState<Partial<TenantSecurityPolicy>>({});
  const [loading, setLoading] = useState(true);
  const [saving, setSaving]   = useState(false);
  const [msg, setMsg]         = useState<{ text: string; ok: boolean } | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const p = await platformApi.getTenantSecurityPolicy(id);
      setPolicy(p);
      setForm({
        passwordMinLength:        p.passwordMinLength,
        passwordRequireUppercase: p.passwordRequireUppercase,
        passwordRequireLowercase: p.passwordRequireLowercase,
        passwordRequireDigit:     p.passwordRequireDigit,
        passwordRequireSpecial:   p.passwordRequireSpecial,
        passwordExpiryDays:       p.passwordExpiryDays,
        passwordHistoryCount:     p.passwordHistoryCount,
        maxFailedLoginAttempts:   p.maxFailedLoginAttempts,
        lockoutDurationMinutes:   p.lockoutDurationMinutes,
        sessionTimeoutMinutes:    p.sessionTimeoutMinutes,
        refreshTokenExpiryDays:   p.refreshTokenExpiryDays,
        allowMultipleSessions:    p.allowMultipleSessions,
      });
    } catch {
      setMsg({ text: 'Failed to load security policy.', ok: false });
    } finally { setLoading(false); }
  }, [id]);

  useEffect(() => { load(); }, [load]);

  async function save(e: React.FormEvent) {
    e.preventDefault();
    setSaving(true); setMsg(null);
    try {
      await platformApi.updateTenantSecurityPolicy(id, form);
      setMsg({ text: 'Security policy saved.', ok: true });
      await load();
    } catch {
      setMsg({ text: 'Save failed. Please try again.', ok: false });
    } finally { setSaving(false); }
  }

  function set<K extends keyof TenantSecurityPolicy>(key: K, value: TenantSecurityPolicy[K]) {
    setForm(f => ({ ...f, [key]: value }));
  }

  if (loading) return (
    <div className="flex items-center justify-center min-h-[60vh]">
      <div className="h-5 w-5 animate-spin rounded-full border-2 border-sapphire border-t-transparent" />
    </div>
  );

  return (
    <div className="space-y-5">
      <div className="flex items-center gap-3">
        <Link href={`/platform/tenants/${id}`}
          className="h-8 w-8 flex items-center justify-center text-slate-500 hover:text-white border border-white/10 rounded-lg transition-colors shrink-0">
          <ArrowLeft className="h-4 w-4" />
        </Link>
        <div>
          <h1 className="text-lg font-bold text-white">Security Policy</h1>
          {policy && <p className="text-xs text-slate-500">{policy.tenantName}</p>}
        </div>
        <div className="ml-auto flex items-center gap-2">
          {policy?.isCustomPolicy && (
            <span className="text-[10px] font-semibold text-amber-400 bg-amber-500/10 border border-amber-500/20 px-2 py-0.5 rounded">
              Custom Policy
            </span>
          )}
          {!policy?.isCustomPolicy && (
            <span className="text-[10px] font-semibold text-slate-500 bg-white/[0.04] border border-white/[0.06] px-2 py-0.5 rounded">
              Platform Defaults
            </span>
          )}
          <button type="button" onClick={load} disabled={loading} aria-label="Refresh"
            className="h-8 w-8 flex items-center justify-center text-slate-500 hover:text-white border border-white/10 rounded-lg transition-colors disabled:opacity-40">
            <RefreshCw className={`h-3.5 w-3.5 ${loading ? 'animate-spin' : ''}`} />
          </button>
        </div>
      </div>

      {msg && (
        <div className={`flex items-center justify-between px-4 py-2.5 rounded-lg border text-sm ${
          msg.ok ? 'bg-emerald-500/10 border-emerald-500/20 text-emerald-400' : 'bg-rose-500/10 border-rose-500/20 text-rose-400'
        }`}>
          <div className="flex items-center gap-2">
            {msg.ok && <CheckCircle className="h-3.5 w-3.5 shrink-0" />}
            {msg.text}
          </div>
          <button type="button" aria-label="Dismiss" onClick={() => setMsg(null)}>
            <X className="h-3.5 w-3.5" />
          </button>
        </div>
      )}

      <form onSubmit={save} className="space-y-5">
        {/* Password Policy */}
        <div className="bg-[#161b22] border border-white/[0.07] rounded-xl overflow-hidden">
          <div className="px-5 py-3 border-b border-white/[0.06] flex items-center gap-2">
            <Shield className="h-3.5 w-3.5 text-slate-500" />
            <p className="text-[10px] font-semibold text-slate-500 uppercase tracking-widest">Password Policy</p>
          </div>
          <div className="px-5 py-4 space-y-2 divide-y divide-white/[0.04]">
            <NumInput label="Minimum length" value={form.passwordMinLength ?? 10}
              onChange={v => set('passwordMinLength', v)} min={6} max={32} unit="chars" />
            <div className="pt-2 space-y-1">
              <Toggle label="Require uppercase" checked={form.passwordRequireUppercase ?? true}
                onChange={v => set('passwordRequireUppercase', v)} />
              <Toggle label="Require lowercase" checked={form.passwordRequireLowercase ?? true}
                onChange={v => set('passwordRequireLowercase', v)} />
              <Toggle label="Require digit" checked={form.passwordRequireDigit ?? true}
                onChange={v => set('passwordRequireDigit', v)} />
              <Toggle label="Require special character" checked={form.passwordRequireSpecial ?? true}
                onChange={v => set('passwordRequireSpecial', v)} />
            </div>
            <div className="pt-2 space-y-1">
              <NumInput label="Password expiry" value={form.passwordExpiryDays ?? 90}
                onChange={v => set('passwordExpiryDays', v)} min={0} max={365} unit="days" />
              <NumInput label="Password history" value={form.passwordHistoryCount ?? 5}
                onChange={v => set('passwordHistoryCount', v)} min={0} max={24} unit="prev" />
            </div>
          </div>
        </div>

        {/* Lockout Policy */}
        <div className="bg-[#161b22] border border-white/[0.07] rounded-xl overflow-hidden">
          <div className="px-5 py-3 border-b border-white/[0.06] flex items-center gap-2">
            <Shield className="h-3.5 w-3.5 text-slate-500" />
            <p className="text-[10px] font-semibold text-slate-500 uppercase tracking-widest">Lockout Policy</p>
          </div>
          <div className="px-5 py-4 space-y-2">
            <NumInput label="Max failed login attempts" value={form.maxFailedLoginAttempts ?? 5}
              onChange={v => set('maxFailedLoginAttempts', v)} min={3} max={20} unit="tries" />
            <NumInput label="Lockout duration" value={form.lockoutDurationMinutes ?? 30}
              onChange={v => set('lockoutDurationMinutes', v)} min={5} max={1440} unit="min" />
          </div>
        </div>

        {/* Session Policy */}
        <div className="bg-[#161b22] border border-white/[0.07] rounded-xl overflow-hidden">
          <div className="px-5 py-3 border-b border-white/[0.06] flex items-center gap-2">
            <Shield className="h-3.5 w-3.5 text-slate-500" />
            <p className="text-[10px] font-semibold text-slate-500 uppercase tracking-widest">Session Policy</p>
          </div>
          <div className="px-5 py-4 space-y-2">
            <NumInput label="Session timeout (inactivity)" value={form.sessionTimeoutMinutes ?? 480}
              onChange={v => set('sessionTimeoutMinutes', v)} min={15} max={10080} unit="min" />
            <NumInput label="Refresh token expiry" value={form.refreshTokenExpiryDays ?? 30}
              onChange={v => set('refreshTokenExpiryDays', v)} min={1} max={365} unit="days" />
            <Toggle label="Allow multiple concurrent sessions" checked={form.allowMultipleSessions ?? true}
              onChange={v => set('allowMultipleSessions', v)} />
          </div>
        </div>

        {policy?.updatedAtUtc && (
          <p className="text-[11px] text-slate-700">
            Last updated {new Date(policy.updatedAtUtc).toLocaleString('en-GB')}
          </p>
        )}

        <div className="flex justify-end">
          <button type="submit" disabled={saving}
            className="bg-sapphire hover:bg-blue-500 text-white px-8 py-2.5 rounded-lg text-sm font-semibold transition-colors disabled:opacity-40">
            {saving ? 'Saving…' : 'Save Security Policy'}
          </button>
        </div>
      </form>
    </div>
  );
}
