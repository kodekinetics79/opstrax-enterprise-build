'use client';

import { useState, useEffect, useCallback } from 'react';
import { useParams, useRouter } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, Search, RefreshCw, UserCog, Key, X } from 'lucide-react';
import { platformApi, type TenantUser, type PasswordResetResult } from '@/src/api/platform';

export default function TenantUsersPage() {
  const { id } = useParams<{ id: string }>();
  const router  = useRouter();
  const [users, setUsers]     = useState<TenantUser[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch]   = useState('');
  const [msg, setMsg]         = useState<{ text: string; ok: boolean } | null>(null);
  const [resetting, setResetting] = useState<string | null>(null);
  const [resetResult, setResetResult] = useState<PasswordResetResult | null>(null);

  useEffect(() => {
    const token = typeof window !== 'undefined' ? localStorage.getItem('platform_access_token') : null;
    if (!token) { router.replace('/platform/login'); return; }
    load();
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id]);

  const load = useCallback(async () => {
    setLoading(true);
    try { setUsers(await platformApi.listTenantUsers(id, search || undefined)); }
    finally { setLoading(false); }
  }, [id, search]);

  async function impersonate(userId: string, email: string) {
    try {
      const { token } = await platformApi.impersonate(id, userId);
      window.open(`${window.location.protocol}//${window.location.hostname}:3000/login?impersonate=${token}`, '_blank');
      setMsg({ text: `Impersonating ${email} — new tab opened.`, ok: true });
    } catch { setMsg({ text: 'Impersonation failed.', ok: false }); }
  }

  async function resetPassword(userId: string) {
    setResetting(userId);
    try {
      const result = await platformApi.sendPasswordReset(userId);
      setResetResult(result);
    } catch { setMsg({ text: 'Password reset failed.', ok: false }); }
    finally { setResetting(null); }
  }

  return (
    <div className="space-y-5">
      <div className="flex items-center gap-3">
        <Link href={`/platform/tenants/${id}`} className="h-8 w-8 flex items-center justify-center text-slate-500 hover:text-white border border-white/10 rounded-lg transition-colors shrink-0">
          <ArrowLeft className="h-4 w-4" />
        </Link>
        <h1 className="text-lg font-bold text-white">Tenant Users</h1>
      </div>

      {msg && (
        <div className={`flex items-center justify-between px-4 py-2.5 rounded-lg border text-sm ${msg.ok ? 'bg-emerald-500/10 border-emerald-500/20 text-emerald-400' : 'bg-rose-500/10 border-rose-500/20 text-rose-400'}`}>
          {msg.text}
          <button type="button" onClick={() => setMsg(null)}><X className="h-3.5 w-3.5" /></button>
        </div>
      )}

      {/* Reset result */}
      {resetResult && (
        <div className="bg-blue-900/20 border border-blue-700/30 rounded-xl p-4 space-y-2">
          <p className="text-sm font-semibold text-blue-300">Password Reset</p>
          <p className="text-xs text-slate-400">{resetResult.message}</p>
          {!resetResult.emailSent && (
            <p className="text-xs text-amber-400">Email not sent (SMTP not configured). Share the temp password manually.</p>
          )}
          <button type="button" onClick={() => setResetResult(null)} className="text-xs text-slate-500 hover:text-white">Dismiss</button>
        </div>
      )}

      <div className="flex items-center gap-3">
        <div className="relative flex-1 max-w-xs">
          <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-slate-600 pointer-events-none" />
          <input type="text" value={search} onChange={e => setSearch(e.target.value)} onKeyDown={e => e.key === 'Enter' && load()}
            placeholder="Search by name or email…"
            className="w-full bg-white/[0.04] border border-white/[0.08] rounded-lg pl-8 pr-3 py-1.5 text-sm text-slate-300 placeholder-slate-600 focus:outline-none focus:border-sapphire/60 transition-colors" />
        </div>
        <button type="button" onClick={load} disabled={loading}
          className="h-8 w-8 flex items-center justify-center text-slate-500 hover:text-white border border-white/10 rounded-lg transition-colors disabled:opacity-40">
          <RefreshCw className={`h-3.5 w-3.5 ${loading ? 'animate-spin' : ''}`} />
        </button>
      </div>

      <div className="bg-[#161b22] border border-white/[0.07] rounded-xl overflow-hidden">
        {loading ? (
          <div className="flex items-center justify-center py-10">
            <div className="h-4 w-4 animate-spin rounded-full border-2 border-sapphire border-t-transparent" />
          </div>
        ) : users.length === 0 ? (
          <p className="text-sm text-slate-600 text-center py-10">No users found.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[600px]">
              <thead>
                <tr className="border-b border-white/[0.06]">
                  {['User', 'Roles', 'Status', 'Joined', ''].map(h => (
                    <th key={h} className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-widest">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {users.map(u => (
                  <tr key={u.id} className="border-b border-white/[0.04] last:border-0 hover:bg-white/[0.02] transition-colors">
                    <td className="px-4 py-3">
                      <p className="text-sm text-white font-medium">{u.fullName}</p>
                      <p className="text-[11px] text-slate-600">{u.email}</p>
                    </td>
                    <td className="px-3 py-3">
                      <div className="flex flex-wrap gap-1">
                        {(u.roles ?? []).slice(0, 3).map(r => (
                          <span key={r} className="text-[10px] bg-white/[0.05] text-slate-400 px-1.5 py-0.5 rounded">{r}</span>
                        ))}
                        {(u.roles ?? []).length > 3 && (
                          <span className="text-[10px] text-slate-600">+{u.roles.length - 3}</span>
                        )}
                      </div>
                    </td>
                    <td className="px-3 py-3">
                      <span className={`text-xs ${u.isActive ? 'text-emerald-400' : 'text-slate-500'}`}>
                        {u.status}
                      </span>
                    </td>
                    <td className="px-3 py-3 text-xs text-slate-600">
                      {new Date(u.createdAtUtc).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })}
                    </td>
                    <td className="px-3 py-3">
                      <div className="flex items-center gap-1 justify-end">
                        <button type="button"
                          onClick={() => resetPassword(u.id)}
                          disabled={resetting === u.id}
                          className="flex items-center gap-1 text-[11px] text-amber-400 border border-amber-500/20 hover:border-amber-500/40 px-2 py-1 rounded transition-colors disabled:opacity-40">
                          <Key className="h-3 w-3" />
                          {resetting === u.id ? '…' : 'Reset'}
                        </button>
                        <button type="button"
                          onClick={() => impersonate(u.id, u.email)}
                          className="flex items-center gap-1 text-[11px] text-blue-400 border border-blue-500/20 hover:border-blue-500/40 px-2 py-1 rounded transition-colors">
                          <UserCog className="h-3 w-3" />
                          Login As
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
