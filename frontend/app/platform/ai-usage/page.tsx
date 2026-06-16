'use client';

import { useState, useEffect, useCallback } from 'react';
import { useRouter } from 'next/navigation';
import { RefreshCw, Brain } from 'lucide-react';
import { platformApi, type PlatformTenantSummary, type TenantAiUsage } from '@/src/api/platform';

function UsageBar({ pct }: { pct: number }) {
  return (
    <div className="flex items-center gap-2">
      <div className="flex-1 h-1.5 bg-white/[0.06] rounded-full overflow-hidden">
        <div className={`h-full rounded-full ${pct >= 90 ? 'bg-rose-500' : pct >= 70 ? 'bg-amber-500' : 'bg-purple-500'}`}
          style={{ width: `${Math.min(pct, 100)}%` }} />
      </div>
      <span className="text-[11px] text-slate-500 tabular-nums w-8 text-right">{pct}%</span>
    </div>
  );
}

export default function AiUsagePage() {
  const router = useRouter();
  const [tenants, setTenants] = useState<PlatformTenantSummary[]>([]);
  const [usages, setUsages]   = useState<Record<string, TenantAiUsage>>({});
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const token = typeof window !== 'undefined' ? localStorage.getItem('platform_access_token') : null;
    if (!token) { router.replace('/platform/login'); return; }
    load();
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const ts = await platformApi.listTenants();
      setTenants(ts);
      // Load AI usage per tenant (parallelize)
      const results = await Promise.allSettled(
        ts.filter(t => t.subscription?.status === 'Active' || t.subscription?.status === 'Trial')
          .map(t => platformApi.getTenantAiUsage(t.id).then(u => ({ id: t.id, usage: u })))
      );
      const map: Record<string, TenantAiUsage> = {};
      for (const r of results) {
        if (r.status === 'fulfilled') map[r.value.id] = r.value.usage;
      }
      setUsages(map);
    } finally { setLoading(false); }
  }, []);

  const tenantsWithAi = tenants.filter(t => t.subscription?.status === 'Active' || t.subscription?.status === 'Trial');

  return (
    <div className="space-y-5">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-lg font-bold text-white">AI Usage & Cost</h1>
          <p className="text-xs text-slate-500 mt-0.5">Token consumption per tenant this month</p>
        </div>
        <button type="button" onClick={load} disabled={loading}
          className="h-8 w-8 flex items-center justify-center text-slate-500 hover:text-white border border-white/10 rounded-lg transition-colors disabled:opacity-40">
          <RefreshCw className={`h-3.5 w-3.5 ${loading ? 'animate-spin' : ''}`} />
        </button>
      </div>

      <div className="bg-[#161b22] border border-white/[0.07] rounded-xl overflow-hidden">
        {loading ? (
          <div className="flex items-center justify-center py-16">
            <div className="h-5 w-5 animate-spin rounded-full border-2 border-purple-500 border-t-transparent" />
          </div>
        ) : tenantsWithAi.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-16 gap-3">
            <Brain className="h-8 w-8 text-slate-700" />
            <p className="text-sm text-slate-600">No active tenants with AI enabled.</p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[600px]">
              <thead>
                <tr className="border-b border-white/[0.06]">
                  {['Tenant', 'Plan', 'Tokens Used', 'Requests', 'Blocked', 'Usage'].map(h => (
                    <th key={h} className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-widest">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {tenantsWithAi.map(t => {
                  const u = usages[t.id];
                  return (
                    <tr key={t.id} className="border-b border-white/[0.04] last:border-0 hover:bg-white/[0.02] transition-colors">
                      <td className="px-4 py-3">
                        <p className="text-sm text-white font-medium">{t.name}</p>
                        <p className="text-[11px] text-slate-600 font-mono">/{t.slug}</p>
                      </td>
                      <td className="px-3 py-3 text-xs text-slate-400">{t.subscription?.plan}</td>
                      <td className="px-3 py-3 text-xs text-slate-300 tabular-nums">
                        {u ? u.tokensUsed.toLocaleString() : <span className="text-slate-700">—</span>}
                      </td>
                      <td className="px-3 py-3 text-xs text-slate-400 tabular-nums">
                        {u ? u.requestCount.toLocaleString() : '—'}
                      </td>
                      <td className="px-3 py-3 text-xs text-slate-500 tabular-nums">
                        {u ? u.blockedCount.toLocaleString() : '—'}
                      </td>
                      <td className="px-3 py-3 w-36">
                        {u ? (
                          u.isUnlimited
                            ? <span className="text-[11px] text-slate-600">Unlimited</span>
                            : <UsageBar pct={u.usagePct} />
                        ) : <span className="text-[11px] text-slate-700">—</span>}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
