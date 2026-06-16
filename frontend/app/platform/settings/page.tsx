'use client';

import { useState, useEffect, useCallback } from 'react';
import { useRouter } from 'next/navigation';
import { RefreshCw, Send, CheckCircle, AlertTriangle, X } from 'lucide-react';
import { platformApi, type PlatformSettings } from '@/src/api/platform';

export default function PlatformSettingsPage() {
  const router = useRouter();
  const [settings, setSettings] = useState<PlatformSettings | null>(null);
  const [loading, setLoading]   = useState(true);
  const [loadErr, setLoadErr]   = useState('');
  const [msg, setMsg]           = useState<{ text: string; ok: boolean } | null>(null);

  // SMTP form state
  const [smtp, setSmtp] = useState({ host: '', port: 587, username: '', password: '', fromEmail: '', fromName: '', useSsl: true });
  const [savingSmtp, setSavingSmtp] = useState(false);
  const [testingSmtp, setTestingSmtp] = useState(false);

  useEffect(() => {
    const token = typeof window !== 'undefined' ? localStorage.getItem('platform_access_token') : null;
    if (!token) { router.replace('/platform/login'); return; }
    load();
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const load = useCallback(async () => {
    setLoading(true); setLoadErr('');
    try {
      const s = await platformApi.getSettings();
      setSettings(s);
      if (s.smtp) {
        setSmtp({
          host: s.smtp.host ?? '',
          port: s.smtp.port ?? 587,
          username: s.smtp.username ?? '',
          password: '',
          fromEmail: s.smtp.fromEmail ?? '',
          fromName: s.smtp.fromName ?? '',
          useSsl: s.smtp.useSsl ?? true,
        });
      }
    } catch {
      setLoadErr('Failed to load settings. This endpoint may not be implemented yet.');
    } finally { setLoading(false); }
  }, []);

  async function saveSmtp(e: React.FormEvent) {
    e.preventDefault();
    setSavingSmtp(true);
    try {
      await platformApi.updateSmtpSettings(smtp);
      setMsg({ text: 'SMTP settings saved.', ok: true });
      await load();
    } catch { setMsg({ text: 'Save failed.', ok: false }); }
    finally { setSavingSmtp(false); }
  }

  async function testSmtp() {
    setTestingSmtp(true);
    try {
      const r = await platformApi.testSmtp();
      setMsg({ text: r.message, ok: r.sent });
    } catch { setMsg({ text: 'Test failed. Check SMTP config.', ok: false }); }
    finally { setTestingSmtp(false); }
  }

  if (loading) {
    return (
      <div className="flex items-center justify-center min-h-[60vh]">
        <div className="h-5 w-5 animate-spin rounded-full border-2 border-sapphire border-t-transparent" />
      </div>
    );
  }

  if (loadErr) {
    return (
      <div className="space-y-5">
        <h1 className="text-lg font-bold text-white">Platform Settings</h1>
        <div className="px-5 py-6 bg-amber-500/5 border border-amber-500/20 rounded-xl">
          <p className="text-sm text-amber-400">{loadErr}</p>
          <p className="text-xs text-slate-600 mt-2">TODO: Implement GET /api/platform/settings in backend</p>
        </div>
        {/* Show SMTP form anyway for configuration */}
        <SmtpForm smtp={smtp} setSmtp={setSmtp} onSave={saveSmtp} saving={savingSmtp} onTest={testSmtp} testing={testingSmtp} />
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-lg font-bold text-white">Platform Settings</h1>
        <button type="button" onClick={load} disabled={loading} aria-label="Refresh"
          className="h-8 w-8 flex items-center justify-center text-slate-500 hover:text-white border border-white/10 rounded-lg transition-colors">
          <RefreshCw className="h-3.5 w-3.5" />
        </button>
      </div>

      {msg && (
        <div className={`flex items-center justify-between px-4 py-2.5 rounded-lg border text-sm ${msg.ok ? 'bg-emerald-500/10 border-emerald-500/20 text-emerald-400' : 'bg-rose-500/10 border-rose-500/20 text-rose-400'}`}>
          {msg.text}
          <button type="button" aria-label="Dismiss" onClick={() => setMsg(null)}><X className="h-3.5 w-3.5" /></button>
        </div>
      )}

      {/* SMTP status banner */}
      {settings && (
        <div className={`flex items-center gap-3 px-5 py-3.5 rounded-xl border ${
          settings.smtp?.isConfigured
            ? 'bg-emerald-500/5 border-emerald-500/20'
            : 'bg-amber-500/5 border-amber-500/20'
        }`}>
          {settings.smtp?.isConfigured
            ? <CheckCircle className="h-4 w-4 text-emerald-400 shrink-0" />
            : <AlertTriangle className="h-4 w-4 text-amber-400 shrink-0" />}
          <p className={`text-sm font-medium ${settings.smtp?.isConfigured ? 'text-emerald-300' : 'text-amber-300'}`}>
            {settings.smtp?.isConfigured
              ? `SMTP configured — sending via ${settings.smtp.host}`
              : 'SMTP not configured — invoice emails and password reset emails will not work'}
          </p>
        </div>
      )}

      <SmtpForm smtp={smtp} setSmtp={setSmtp} onSave={saveSmtp} saving={savingSmtp} onTest={testSmtp} testing={testingSmtp} />

      {/* Planned settings (TODO) */}
      <div className="bg-[#161b22] border border-white/[0.07] rounded-xl overflow-hidden opacity-40">
        <div className="px-5 py-3 border-b border-white/[0.06]">
          <p className="text-[10px] font-semibold text-slate-600 uppercase tracking-widest">Planned Settings</p>
        </div>
        {[
          { label: 'AI Gateway API Key', note: 'PUT /api/platform/settings/ai' },
          { label: 'Default Trial Duration', note: 'PUT /api/platform/settings/trial' },
          { label: 'Platform Branding', note: 'PUT /api/platform/settings/branding' },
          { label: 'Webhook Endpoints', note: 'PUT /api/platform/settings/webhooks' },
          { label: 'Maintenance Mode', note: 'POST /api/platform/settings/maintenance' },
        ].map(s => (
          <div key={s.label} className="flex items-center justify-between px-5 py-3.5 border-b border-white/[0.04] last:border-0">
            <span className="text-sm text-slate-400">{s.label}</span>
            <span className="text-[11px] text-slate-700 font-mono">{s.note}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

function SmtpForm({ smtp, setSmtp, onSave, saving, onTest, testing }: {
  smtp: { host: string; port: number; username: string; password: string; fromEmail: string; fromName: string; useSsl: boolean };
  setSmtp: (fn: (prev: typeof smtp) => typeof smtp) => void;
  onSave: (e: React.FormEvent) => void;
  saving: boolean;
  onTest: () => void;
  testing: boolean;
}) {
  return (
    <div className="bg-[#161b22] border border-white/[0.07] rounded-xl overflow-hidden">
      <div className="px-5 py-3 border-b border-white/[0.06]">
        <p className="text-[10px] font-semibold text-slate-600 uppercase tracking-widest">SMTP Configuration</p>
      </div>
      <form onSubmit={onSave} className="px-5 py-5 space-y-4">
        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="block text-xs text-slate-400 mb-1">SMTP Host</label>
            <input value={smtp.host} onChange={e => setSmtp(f => ({ ...f, host: e.target.value }))}
              className="w-full bg-white/[0.04] border border-white/[0.08] rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-sapphire/60 placeholder-slate-600"
              placeholder="smtp.sendgrid.net" />
          </div>
          <div>
            <label className="block text-xs text-slate-400 mb-1">Port</label>
            <input type="number" aria-label="SMTP port" value={smtp.port} onChange={e => setSmtp(f => ({ ...f, port: parseInt(e.target.value) || 587 }))}
              className="w-full bg-white/[0.04] border border-white/[0.08] rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-sapphire/60" />
          </div>
        </div>
        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="block text-xs text-slate-400 mb-1">Username</label>
            <input value={smtp.username} onChange={e => setSmtp(f => ({ ...f, username: e.target.value }))}
              className="w-full bg-white/[0.04] border border-white/[0.08] rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-sapphire/60 placeholder-slate-600"
              placeholder="apikey" />
          </div>
          <div>
            <label className="block text-xs text-slate-400 mb-1">Password</label>
            <input type="password" value={smtp.password} onChange={e => setSmtp(f => ({ ...f, password: e.target.value }))}
              className="w-full bg-white/[0.04] border border-white/[0.08] rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-sapphire/60"
              placeholder="Leave blank to keep current" />
          </div>
        </div>
        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="block text-xs text-slate-400 mb-1">From Email</label>
            <input type="email" value={smtp.fromEmail} onChange={e => setSmtp(f => ({ ...f, fromEmail: e.target.value }))}
              className="w-full bg-white/[0.04] border border-white/[0.08] rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-sapphire/60 placeholder-slate-600"
              placeholder="noreply@yourdomain.com" />
          </div>
          <div>
            <label className="block text-xs text-slate-400 mb-1">From Name</label>
            <input value={smtp.fromName} onChange={e => setSmtp(f => ({ ...f, fromName: e.target.value }))}
              className="w-full bg-white/[0.04] border border-white/[0.08] rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-sapphire/60 placeholder-slate-600"
              placeholder="KynexOne" />
          </div>
        </div>
        <label className="flex items-center gap-2 cursor-pointer">
          <input type="checkbox" checked={smtp.useSsl} onChange={e => setSmtp(f => ({ ...f, useSsl: e.target.checked }))}
            className="h-4 w-4 rounded accent-sapphire" />
          <span className="text-sm text-slate-400">Use SSL/TLS</span>
        </label>
        <div className="flex gap-3 pt-1">
          <button type="button" onClick={onTest} disabled={testing || !smtp.host}
            className="flex items-center gap-1.5 text-sm text-slate-400 border border-white/10 hover:border-white/20 px-4 py-2 rounded-lg transition-colors disabled:opacity-40">
            <Send className="h-3.5 w-3.5" />
            {testing ? 'Sending…' : 'Test Email'}
          </button>
          <button type="submit" disabled={saving}
            className="bg-sapphire hover:bg-blue-500 text-white px-6 py-2 rounded-lg text-sm font-semibold transition-colors disabled:opacity-40">
            {saving ? 'Saving…' : 'Save SMTP Settings'}
          </button>
        </div>
      </form>
    </div>
  );
}
