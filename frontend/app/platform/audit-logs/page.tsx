'use client';

import { useState, useEffect, useCallback } from 'react';
import { useRouter } from 'next/navigation';
import { RefreshCw } from 'lucide-react';
import { platformApi, type PlatformAuditLog } from '@/src/api/platform';

export default function AuditLogsPage() {
  const router = useRouter();
  const [logs, setLogs]     = useState<PlatformAuditLog[]>([]);
  const [total, setTotal]   = useState(0);
  const [page, setPage]     = useState(1);
  const [loading, setLoading] = useState(true);
  const PAGE_SIZE = 50;

  useEffect(() => {
    const token = typeof window !== 'undefined' ? localStorage.getItem('platform_access_token') : null;
    if (!token) { router.replace('/platform/login'); return; }
    load();
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page]);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const r = await platformApi.getAuditLogs(undefined, page, PAGE_SIZE);
      setLogs(r.logs); setTotal(r.total);
    } finally { setLoading(false); }
  }, [page]);

  const totalPages = Math.ceil(total / PAGE_SIZE);

  return (
    <div className="space-y-5">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-lg font-bold text-white">Platform Audit Log</h1>
          <p className="text-xs text-slate-500 mt-0.5">All platform-level events across all tenants</p>
        </div>
        <button type="button" onClick={load} disabled={loading}
          className="h-8 w-8 flex items-center justify-center text-slate-500 hover:text-white border border-white/10 rounded-lg transition-colors disabled:opacity-40">
          <RefreshCw className={`h-3.5 w-3.5 ${loading ? 'animate-spin' : ''}`} />
        </button>
      </div>

      <div className="bg-[#161b22] border border-white/[0.07] rounded-xl overflow-hidden">
        {loading ? (
          <div className="flex items-center justify-center py-16">
            <div className="h-5 w-5 animate-spin rounded-full border-2 border-sapphire border-t-transparent" />
          </div>
        ) : logs.length === 0 ? (
          <p className="text-sm text-slate-600 text-center py-16">No audit events recorded.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[700px]">
              <thead>
                <tr className="border-b border-white/[0.06]">
                  {['Time', 'Action', 'Entity', 'Tenant', 'Performed By', 'IP'].map(h => (
                    <th key={h} className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-widest">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {logs.map(log => (
                  <tr key={log.id} className="border-b border-white/[0.04] last:border-0 hover:bg-white/[0.02] transition-colors">
                    <td className="px-4 py-2.5 text-[11px] text-slate-600 tabular-nums whitespace-nowrap">
                      {new Date(log.createdAtUtc).toLocaleString('en-GB')}
                    </td>
                    <td className="px-3 py-2.5">
                      <span className="text-xs font-medium text-slate-300">{log.action}</span>
                    </td>
                    <td className="px-3 py-2.5 text-[11px] text-slate-500">
                      {log.entityType}{log.entityId ? ` #${log.entityId.slice(0, 8)}` : ''}
                    </td>
                    <td className="px-3 py-2.5 text-[11px] text-slate-600 font-mono">
                      {log.tenantId ? log.tenantId.slice(0, 8) : 'Platform'}
                    </td>
                    <td className="px-3 py-2.5 text-xs text-slate-400">{log.performedByName}</td>
                    <td className="px-3 py-2.5 text-[11px] text-slate-600 font-mono">{log.ipAddress || '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {totalPages > 1 && (
        <div className="flex items-center justify-between text-xs text-slate-500">
          <span>{total} total events</span>
          <div className="flex items-center gap-2">
            <button type="button" onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page === 1 || loading}
              className="px-3 py-1.5 border border-white/10 rounded-lg hover:border-white/20 disabled:opacity-40 transition-colors">
              Prev
            </button>
            <span className="px-2">{page} / {totalPages}</span>
            <button type="button" onClick={() => setPage(p => Math.min(totalPages, p + 1))} disabled={page === totalPages || loading}
              className="px-3 py-1.5 border border-white/10 rounded-lg hover:border-white/20 disabled:opacity-40 transition-colors">
              Next
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
