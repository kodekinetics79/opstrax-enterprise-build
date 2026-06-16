'use client';

import { useState, useEffect, useCallback } from 'react';
import { useRouter } from 'next/navigation';
import { RefreshCw, CheckCircle, XCircle, AlertTriangle, Circle } from 'lucide-react';
import { platformApi, type PlatformHealthStatus } from '@/src/api/platform';

type ComponentStatus = 'ok' | 'configured' | 'error' | 'unknown' | 'not_configured';

function StatusIcon({ status }: { status: ComponentStatus }) {
  if (status === 'ok' || status === 'configured') return <CheckCircle className="h-4 w-4 text-emerald-400" />;
  if (status === 'error') return <XCircle className="h-4 w-4 text-rose-400" />;
  if (status === 'not_configured') return <AlertTriangle className="h-4 w-4 text-amber-400" />;
  return <Circle className="h-4 w-4 text-slate-600" />;
}

function statusLabel(status: ComponentStatus) {
  if (status === 'ok') return { text: 'Healthy', cls: 'text-emerald-400' };
  if (status === 'configured') return { text: 'Configured', cls: 'text-emerald-400' };
  if (status === 'error') return { text: 'Error', cls: 'text-rose-400' };
  if (status === 'not_configured') return { text: 'Not Configured', cls: 'text-amber-400' };
  return { text: 'Unknown', cls: 'text-slate-600' };
}

const COMPONENT_LABELS: Record<string, string> = {
  database: 'Database (MySQL)',
  smtp:     'Email (SMTP)',
  redis:    'Cache (Redis)',
  jobs:     'Background Jobs',
};

export default function SystemHealthPage() {
  const router = useRouter();
  const [health, setHealth]   = useState<PlatformHealthStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError]     = useState('');
  const [lastChecked, setLastChecked] = useState<Date | null>(null);

  useEffect(() => {
    const token = typeof window !== 'undefined' ? localStorage.getItem('platform_access_token') : null;
    if (!token) { router.replace('/platform/login'); return; }
    check();
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const check = useCallback(async () => {
    setLoading(true); setError('');
    try {
      const h = await platformApi.getHealth();
      setHealth(h);
      setLastChecked(new Date());
    } catch {
      setError('Failed to reach health endpoint.');
    } finally { setLoading(false); }
  }, []);

  const overallOk = health?.status === 'healthy';
  const components = health?.components
    ? Object.entries(health.components) as [string, { status: ComponentStatus }][]
    : [];

  return (
    <div className="space-y-5">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-lg font-bold text-white">System Health</h1>
          <p className="text-xs text-slate-500 mt-0.5">
            {lastChecked ? `Last checked ${lastChecked.toLocaleTimeString()}` : 'Checking…'}
            {health && ` · Build ${health.version} · ${health.environment}`}
          </p>
        </div>
        <button type="button" onClick={check} disabled={loading} aria-label="Refresh health"
          className="flex items-center gap-1.5 text-xs text-slate-500 hover:text-slate-300 border border-white/10 rounded-lg px-3 py-1.5 transition-colors disabled:opacity-40">
          <RefreshCw className={`h-3 w-3 ${loading ? 'animate-spin' : ''}`} />
          Refresh
        </button>
      </div>

      {error && (
        <div className="px-4 py-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-sm text-rose-400">
          {error}
        </div>
      )}

      {/* Overall status banner */}
      {health && (
        <div className={`flex items-center gap-3 px-5 py-4 rounded-xl border ${
          overallOk
            ? 'bg-emerald-500/5 border-emerald-500/20'
            : 'bg-amber-500/5 border-amber-500/20'
        }`}>
          {overallOk
            ? <CheckCircle className="h-5 w-5 text-emerald-400 shrink-0" />
            : <AlertTriangle className="h-5 w-5 text-amber-400 shrink-0" />}
          <div>
            <p className={`text-sm font-semibold ${overallOk ? 'text-emerald-300' : 'text-amber-300'}`}>
              {overallOk ? 'All Systems Operational' : 'Degraded — check components below'}
            </p>
            <p className="text-xs text-slate-500 mt-0.5">
              Checked at {health.checkedAtUtc ? new Date(health.checkedAtUtc).toLocaleTimeString() : '—'}
            </p>
          </div>
        </div>
      )}

      {/* Component grid */}
      {loading && !health ? (
        <div className="flex items-center justify-center py-16">
          <div className="h-5 w-5 animate-spin rounded-full border-2 border-sapphire border-t-transparent" />
        </div>
      ) : (
        <div className="bg-[#161b22] border border-white/[0.07] rounded-xl overflow-hidden divide-y divide-white/[0.04]">
          {components.map(([key, comp]) => {
            const label = statusLabel(comp.status);
            return (
              <div key={key} className="flex items-center gap-4 px-5 py-4">
                <StatusIcon status={comp.status} />
                <div className="flex-1">
                  <p className="text-sm text-white">{COMPONENT_LABELS[key] ?? key}</p>
                </div>
                <span className={`text-xs font-medium ${label.cls}`}>{label.text}</span>
              </div>
            );
          })}

          {/* Planned components (TODO) */}
          {[
            { key: 'ai_gateway', label: 'AI Gateway' },
            { key: 'storage',    label: 'Object Storage' },
            { key: 'cdn',        label: 'CDN / Assets' },
          ].map(c => (
            <div key={c.key} className="flex items-center gap-4 px-5 py-4 opacity-30">
              <Circle className="h-4 w-4 text-slate-600" />
              <div className="flex-1">
                <p className="text-sm text-white">{c.label}</p>
              </div>
              <span className="text-xs text-slate-700 font-mono">TODO</span>
            </div>
          ))}
        </div>
      )}

      {/* Build info */}
      {health && (
        <div className="bg-[#161b22] border border-white/[0.07] rounded-xl px-5 py-4 space-y-2">
          <p className="text-[10px] font-semibold text-slate-600 uppercase tracking-widest mb-3">Build Info</p>
          {[
            { label: 'Version',     value: health.version },
            { label: 'Environment', value: health.environment },
            { label: 'Checked At',  value: new Date(health.checkedAtUtc).toLocaleString('en-GB') },
          ].map(({ label, value }) => (
            <div key={label} className="flex items-center justify-between text-sm">
              <span className="text-slate-500">{label}</span>
              <span className="text-slate-300 font-mono">{value}</span>
            </div>
          ))}
          <div className="flex items-center justify-between text-sm pt-1 border-t border-white/[0.04]">
            <span className="text-slate-500">Deploy Timestamp</span>
            <span className="text-slate-700 font-mono text-[11px]">TODO: /api/platform/health/version</span>
          </div>
        </div>
      )}
    </div>
  );
}
