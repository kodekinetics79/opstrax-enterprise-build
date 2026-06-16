'use client';

import { useEffect, useState } from 'react';
import {
  ShieldCheck, AlertTriangle, CheckCircle, XCircle, RefreshCw, Banknote, FileWarning,
} from 'lucide-react';
import client from '../api/client';

// ── Types ───────────────────────────────────────────────────────────────────

interface BlockedEmployee {
  employeeId: number;
  employeeCode: string;
  fullName: string;
  missingFields: string[];
}

interface QiwaSection {
  connectionStatus: string;
  lastConnectedAt: string | null;
  totalEmployees: number;
  readyForSync: number;
  blockedFromSync: number;
  readinessPercent: number;
  failedSyncCount: number;
  lastSuccessfulSync: string | null;
  blockedEmployees: BlockedEmployee[];
}

interface WpsSection {
  lastRunStatus: string | null;
  lastRunPeriod: string | null;
  pendingApprovals: number;
  blockingIssues: string[];
}

interface GosiSection {
  employeesMissingGosiRef: number;
  employeesMissingGosiEmployerId: number;
  warnings: string[];
}

interface ActionItem {
  severity: 'critical' | 'warning' | 'info';
  area: string;
  message: string;
}

interface Dashboard {
  qiwa: QiwaSection;
  wps: WpsSection;
  gosi: GosiSection;
  actionItems: ActionItem[];
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function fmtDate(s: string | null): string {
  if (!s) return '—';
  const d = new Date(s);
  return Number.isNaN(d.getTime()) ? '—' : d.toLocaleDateString();
}

function StatusBadge({ status }: { status: string }) {
  const map: Record<string, string> = {
    Connected: 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/20 dark:text-emerald-400',
    Disconnected: 'bg-slate-100 text-slate-600 dark:bg-slate-500/20 dark:text-slate-300',
    NotConfigured: 'bg-slate-100 text-slate-600 dark:bg-slate-500/20 dark:text-slate-300',
    Error: 'bg-rose-100 text-rose-700 dark:bg-rose-500/20 dark:text-rose-400',
    ApiError: 'bg-rose-100 text-rose-700 dark:bg-rose-500/20 dark:text-rose-400',
    ConfigurationError: 'bg-amber-100 text-amber-700 dark:bg-amber-500/20 dark:text-amber-400',
  };
  const cls = map[status] ?? 'bg-slate-100 text-slate-600';
  return <span className={`rounded-full px-2.5 py-0.5 text-xs font-medium ${cls}`}>{status}</span>;
}

function SeverityPill({ severity }: { severity: string }) {
  const cls = severity === 'critical'
    ? 'bg-rose-100 text-rose-700 dark:bg-rose-500/20 dark:text-rose-400'
    : severity === 'warning'
    ? 'bg-amber-100 text-amber-700 dark:bg-amber-500/20 dark:text-amber-400'
    : 'bg-sky-100 text-sky-700 dark:bg-sky-500/20 dark:text-sky-400';
  return <span className={`rounded-full px-2 py-0.5 text-[11px] font-semibold uppercase ${cls}`}>{severity}</span>;
}

function ProgressBar({ percent }: { percent: number }) {
  const colour = percent >= 90 ? 'bg-emerald-500' : percent >= 60 ? 'bg-amber-500' : 'bg-rose-500';
  return (
    <div className="h-2 w-full overflow-hidden rounded-full bg-slate-200 dark:bg-slate-700">
      <div className={`h-full ${colour}`} style={{ width: `${Math.min(100, Math.max(0, percent))}%` }} />
    </div>
  );
}

// ── Component ─────────────────────────────────────────────────────────────────

export function SaudiComplianceDashboard() {
  const [data, setData] = useState<Dashboard | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function load() {
    setLoading(true);
    setError(null);
    try {
      const res = await client.get<Dashboard>('/api/saudi-compliance/dashboard');
      setData(res.data);
    } catch {
      setError('Unable to load Saudi compliance data. You may not have access or the module is not enabled.');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { load(); }, []);

  if (loading) {
    return (
      <div className="space-y-4">
        <div className="h-8 w-64 animate-pulse rounded bg-slate-200 dark:bg-slate-700" />
        <div className="grid gap-4 lg:grid-cols-3">
          {[0, 1, 2].map(i => <div key={i} className="surface h-48 animate-pulse p-4" />)}
        </div>
      </div>
    );
  }

  if (error || !data) {
    return (
      <div className="surface flex flex-col items-center gap-3 p-10 text-center">
        <FileWarning className="h-8 w-8 text-amber-500" />
        <p className="text-sm text-slate-600 dark:text-slate-300">{error ?? 'No data available.'}</p>
        <button type="button" onClick={load} className="rounded-lg bg-sapphire px-4 py-2 text-sm font-medium text-white">
          Retry
        </button>
      </div>
    );
  }

  const { qiwa, wps, gosi, actionItems } = data;

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <ShieldCheck className="h-6 w-6 text-sapphire" />
          <h1 className="text-xl font-semibold text-slate-800 dark:text-slate-100">Saudi Compliance</h1>
        </div>
        <button type="button" onClick={load} className="flex items-center gap-1.5 rounded-lg border border-slate-200 px-3 py-1.5 text-sm dark:border-slate-700">
          <RefreshCw className="h-4 w-4" /> Refresh
        </button>
      </div>

      {/* Action items */}
      <section className="surface p-4">
        <h2 className="mb-3 text-sm font-semibold text-slate-700 dark:text-slate-200">Action Items</h2>
        {actionItems.length === 0 ? (
          <p className="flex items-center gap-2 text-sm text-emerald-600">
            <CheckCircle className="h-4 w-4" /> No outstanding compliance issues.
          </p>
        ) : (
          <ul className="space-y-2">
            {actionItems.map((a, i) => (
              <li key={i} className="flex items-center gap-3 rounded-lg border border-slate-100 px-3 py-2 dark:border-slate-700/60">
                <SeverityPill severity={a.severity} />
                <span className="text-xs font-medium text-slate-500">{a.area}</span>
                <span className="text-sm text-slate-700 dark:text-slate-200">{a.message}</span>
              </li>
            ))}
          </ul>
        )}
      </section>

      <div className="grid gap-4 lg:grid-cols-3">
        {/* QIWA */}
        <section className="surface flex flex-col gap-3 p-4">
          <div className="flex items-center justify-between">
            <h2 className="text-sm font-semibold text-slate-700 dark:text-slate-200">QIWA</h2>
            <StatusBadge status={qiwa.connectionStatus} />
          </div>
          <div className="space-y-1">
            <div className="flex items-center justify-between text-xs text-slate-500">
              <span>Readiness</span>
              <span>{qiwa.readinessPercent}% ({qiwa.readyForSync}/{qiwa.totalEmployees})</span>
            </div>
            <ProgressBar percent={qiwa.readinessPercent} />
          </div>
          <dl className="grid grid-cols-2 gap-2 text-xs">
            <div><dt className="text-slate-400">Blocked</dt><dd className="font-semibold text-slate-700 dark:text-slate-200">{qiwa.blockedFromSync}</dd></div>
            <div><dt className="text-slate-400">Failed syncs</dt><dd className="font-semibold text-slate-700 dark:text-slate-200">{qiwa.failedSyncCount}</dd></div>
            <div><dt className="text-slate-400">Last connected</dt><dd className="text-slate-600 dark:text-slate-300">{fmtDate(qiwa.lastConnectedAt)}</dd></div>
            <div><dt className="text-slate-400">Last sync</dt><dd className="text-slate-600 dark:text-slate-300">{fmtDate(qiwa.lastSuccessfulSync)}</dd></div>
          </dl>
          {qiwa.blockedEmployees.length > 0 && (
            <div className="mt-1 max-h-40 overflow-auto rounded-lg border border-slate-100 dark:border-slate-700/60">
              <table className="w-full text-left text-xs">
                <thead className="sticky top-0 bg-slate-50 text-slate-500 dark:bg-slate-800">
                  <tr><th className="px-2 py-1.5">Code</th><th className="px-2 py-1.5">Name</th><th className="px-2 py-1.5">Missing</th></tr>
                </thead>
                <tbody>
                  {qiwa.blockedEmployees.map(e => (
                    <tr key={e.employeeId} className="border-t border-slate-100 dark:border-slate-700/60">
                      <td className="px-2 py-1.5 font-medium">{e.employeeCode}</td>
                      <td className="px-2 py-1.5">{e.fullName}</td>
                      <td className="px-2 py-1.5 text-rose-600">{e.missingFields.join(', ')}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>

        {/* WPS */}
        <section className="surface flex flex-col gap-3 p-4">
          <div className="flex items-center justify-between">
            <h2 className="flex items-center gap-1.5 text-sm font-semibold text-slate-700 dark:text-slate-200">
              <Banknote className="h-4 w-4" /> WPS
            </h2>
            {wps.lastRunStatus && <StatusBadge status={wps.lastRunStatus} />}
          </div>
          <dl className="grid grid-cols-2 gap-2 text-xs">
            <div><dt className="text-slate-400">Last run period</dt><dd className="font-semibold text-slate-700 dark:text-slate-200">{wps.lastRunPeriod ?? '—'}</dd></div>
            <div><dt className="text-slate-400">Pending approvals</dt><dd className="font-semibold text-slate-700 dark:text-slate-200">{wps.pendingApprovals}</dd></div>
          </dl>
          <div>
            <p className="mb-1 text-xs font-medium text-slate-500">Blocking issues</p>
            {wps.blockingIssues.length === 0 ? (
              <p className="flex items-center gap-1.5 text-xs text-emerald-600"><CheckCircle className="h-3.5 w-3.5" /> None</p>
            ) : (
              <ul className="space-y-1">
                {wps.blockingIssues.map((it, i) => (
                  <li key={i} className="flex items-start gap-1.5 text-xs text-amber-700 dark:text-amber-400">
                    <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" /> {it}
                  </li>
                ))}
              </ul>
            )}
          </div>
        </section>

        {/* GOSI */}
        <section className="surface flex flex-col gap-3 p-4">
          <h2 className="text-sm font-semibold text-slate-700 dark:text-slate-200">GOSI</h2>
          <dl className="grid grid-cols-2 gap-2 text-xs">
            <div><dt className="text-slate-400">Missing GOSI ref</dt><dd className="font-semibold text-slate-700 dark:text-slate-200">{gosi.employeesMissingGosiRef}</dd></div>
            <div><dt className="text-slate-400">Missing employer ID</dt><dd className="font-semibold text-slate-700 dark:text-slate-200">{gosi.employeesMissingGosiEmployerId}</dd></div>
          </dl>
          {gosi.warnings.length > 0 ? (
            <ul className="space-y-1">
              {gosi.warnings.map((w, i) => (
                <li key={i} className="flex items-start gap-1.5 text-xs text-amber-700 dark:text-amber-400">
                  <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" /> {w}
                </li>
              ))}
            </ul>
          ) : (
            <p className="flex items-center gap-1.5 text-xs text-emerald-600"><CheckCircle className="h-3.5 w-3.5" /> No GOSI gaps.</p>
          )}
          <p className="mt-auto flex items-start gap-1.5 text-[11px] text-slate-400">
            <XCircle className="mt-0.5 h-3 w-3 shrink-0" />
            GOSI contribution rates are illustrative. Verify current rates with the GOSI portal.
          </p>
        </section>
      </div>
    </div>
  );
}

export default SaudiComplianceDashboard;
