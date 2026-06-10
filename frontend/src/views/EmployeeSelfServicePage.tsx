'use client';

import { useEffect, useState } from 'react';
import { Bot, Loader2 } from 'lucide-react';
import { essApi, type EssDashboard } from '../api/ess';
import { useAuth } from '../contexts/AuthContext';
import { KpiCard } from '../components/KpiCard';
import { DataPanel } from '../components/DataPanel';
import { StatusChip } from '../components/StatusChip';

function minutes(value?: number) {
  if (!value) return '0h';
  const h = Math.floor(value / 60);
  const m = value % 60;
  return `${h}h ${m}m`;
}

export function EmployeeSelfServicePage() {
  const { user } = useAuth();
  const [dashboard, setDashboard] = useState<EssDashboard | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const [question, setQuestion] = useState('How many leave days do I have?');
  const [answer, setAnswer] = useState('');
  const [asking, setAsking] = useState(false);
  const [ticketSubject, setTicketSubject] = useState('');
  const [ticketMessage, setTicketMessage] = useState('');

  const load = async () => {
    setLoading(true);
    setError('');
    try {
      setDashboard(await essApi.dashboard());
    } catch (err: any) {
      setError(err.response?.data?.message ?? 'Unable to load Employee Self-Service data.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load();
  }, []);

  const askAi = async () => {
    setAsking(true);
    setAnswer('');
    try {
      const res = await essApi.askAi(question);
      setAnswer(res.answer);
    } catch (err: any) {
      setAnswer(err.response?.data?.message ?? 'The assistant could not answer right now.');
    } finally {
      setAsking(false);
    }
  };

  const createTicket = async () => {
    if (!ticketSubject.trim() || !ticketMessage.trim()) return;
    await essApi.createHrRequest({ categoryName: 'General HR', subject: ticketSubject, description: ticketMessage, priority: 'Normal' });
    setTicketSubject('');
    setTicketMessage('');
    await load();
  };

  if (loading) {
    return (
      <div className="flex h-96 items-center justify-center text-slate-500 dark:text-slate-400">
        <Loader2 className="mr-2 h-5 w-5 animate-spin" />
        Loading your workspace
      </div>
    );
  }

  if (error || !dashboard) {
    return (
      <div className="rounded-lg border border-rose-200 bg-rose-50 p-6 text-sm text-rose-700 dark:border-rose-500/30 dark:bg-rose-500/10 dark:text-rose-200">
        {error || 'ESS dashboard was empty.'}
      </div>
    );
  }

  const attendance = dashboard.attendanceToday;
  const leaveAvailable = dashboard.leaveBalances.reduce((sum, item) => sum + item.available, 0);

  return (
    <div className="space-y-6">
      <div className="flex flex-col justify-between gap-4 lg:flex-row lg:items-end">
        <div>
          <p className="text-sm font-medium text-sapphire-600 dark:text-cyan-300">Employee Self-Service</p>
          <h1 className="mt-1 text-2xl font-semibold text-slate-950 dark:text-white">Good day, {dashboard.profile.fullName}</h1>
          <p className="mt-2 text-sm text-slate-500 dark:text-slate-400">
            {dashboard.profile.jobTitle || 'Employee'} · {dashboard.profile.department || 'Unassigned'} · {user?.accessMode ?? 'Portal'}
          </p>
        </div>
        <button
          type="button"
          onClick={load}
          className="w-fit rounded-md bg-slate-950 px-4 py-2 text-sm font-medium text-white hover:bg-slate-800 dark:bg-white dark:text-slate-950"
        >
          Refresh
        </button>
      </div>

      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        <KpiCard metric={{ label: 'Today', value: attendance?.status ?? 'No record', delta: attendance ? `${minutes(attendance.totalWorkedMinutes)} worked` : 'No punch processed', tone: attendance?.missingPunch ? 'rose' : 'blue' }} />
        <KpiCard metric={{ label: 'Leave Available', value: `${leaveAvailable.toFixed(1)} days`, delta: `${dashboard.leaveBalances.length} balance bucket(s)`, tone: 'emerald' }} />
        <KpiCard metric={{ label: 'Pending Requests', value: dashboard.pendingRequests.toString(), delta: 'Leave and HR requests', tone: 'amber' }} />
        <KpiCard metric={{ label: 'Document Alerts', value: dashboard.documentAlerts.length.toString(), delta: 'Expiring or action needed', tone: 'rose' }} />
      </div>

      <div className="grid gap-6 xl:grid-cols-[1.4fr_.8fr]">
        <DataPanel title="My Action Center">
          <div className="divide-y divide-slate-200 dark:divide-slate-800">
            {dashboard.actionItems.length === 0 && dashboard.documentAlerts.length === 0 ? (
              <p className="py-6 text-sm text-slate-500 dark:text-slate-400">No open action items from the live database.</p>
            ) : null}
            {dashboard.documentAlerts.map((doc) => (
              <div key={doc.id} className="flex items-center justify-between gap-4 py-3">
                <div>
                  <p className="text-sm font-medium text-slate-900 dark:text-white">{doc.documentType}</p>
                  <p className="text-xs text-slate-500 dark:text-slate-400">{doc.fileName || 'Document metadata'} · expires {doc.expiryDate ?? 'not set'}</p>
                </div>
                <StatusChip label={doc.approvalStatus} />
              </div>
            ))}
            {dashboard.actionItems.map((item) => (
              <div key={item.id} className="flex items-center justify-between gap-4 py-3">
                <div>
                  <p className="text-sm font-medium text-slate-900 dark:text-white">{item.title}</p>
                  <p className="text-xs text-slate-500 dark:text-slate-400">{item.category}</p>
                </div>
                <StatusChip label={item.dueAtUtc ? 'Due' : 'Open'} />
              </div>
            ))}
          </div>
        </DataPanel>

        <DataPanel title="AI HR Assistant">
          <div className="space-y-3">
            <textarea
              value={question}
              onChange={(event) => setQuestion(event.target.value)}
              className="min-h-24 w-full rounded-md border border-slate-200 bg-white px-3 py-2 text-sm text-slate-900 outline-none focus:border-sapphire-500 dark:border-slate-800 dark:bg-slate-950 dark:text-white"
            />
            <button
              type="button"
              onClick={askAi}
              disabled={asking}
              className="inline-flex items-center rounded-md bg-sapphire-600 px-4 py-2 text-sm font-medium text-white hover:bg-sapphire-500 disabled:opacity-60"
            >
              {asking ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Bot className="mr-2 h-4 w-4" />}
              Ask
            </button>
            {answer ? <p className="rounded-md bg-slate-100 p-3 text-sm text-slate-700 dark:bg-slate-900 dark:text-slate-200">{answer}</p> : null}
          </div>
        </DataPanel>
      </div>

      <div className="grid gap-6 xl:grid-cols-3">
        <DataPanel title="Leave Balances">
          <div className="space-y-3">
            {dashboard.leaveBalances.length === 0 ? <p className="text-sm text-slate-500 dark:text-slate-400">No leave balances found.</p> : null}
            {dashboard.leaveBalances.map((balance) => (
              <div key={balance.leaveTypeId} className="rounded-md border border-slate-200 p-3 dark:border-slate-800">
                <div className="flex items-center justify-between text-sm">
                  <span className="font-medium text-slate-900 dark:text-white">{balance.leaveTypeName}</span>
                  <span className="text-slate-500 dark:text-slate-400">{balance.available.toFixed(1)} available</span>
                </div>
              </div>
            ))}
          </div>
        </DataPanel>

        <DataPanel title="Announcements">
          <div className="space-y-3">
            {dashboard.announcements.length === 0 ? <p className="text-sm text-slate-500 dark:text-slate-400">No active announcements.</p> : null}
            {dashboard.announcements.map((item) => (
              <article key={item.id} className="rounded-md border border-slate-200 p-3 dark:border-slate-800">
                <p className="text-sm font-medium text-slate-900 dark:text-white">{item.title}</p>
                <p className="mt-1 line-clamp-2 text-xs text-slate-500 dark:text-slate-400">{item.body}</p>
              </article>
            ))}
          </div>
        </DataPanel>

        <DataPanel title="HR Request">
          <div className="space-y-3">
            <input
              value={ticketSubject}
              onChange={(event) => setTicketSubject(event.target.value)}
              placeholder="Subject"
              className="w-full rounded-md border border-slate-200 bg-white px-3 py-2 text-sm outline-none focus:border-sapphire-500 dark:border-slate-800 dark:bg-slate-950"
            />
            <textarea
              value={ticketMessage}
              onChange={(event) => setTicketMessage(event.target.value)}
              placeholder="What do you need from HR?"
              className="min-h-24 w-full rounded-md border border-slate-200 bg-white px-3 py-2 text-sm outline-none focus:border-sapphire-500 dark:border-slate-800 dark:bg-slate-950"
            />
            <button type="button" onClick={createTicket} className="rounded-md bg-slate-950 px-4 py-2 text-sm font-medium text-white dark:bg-white dark:text-slate-950">
              Submit request
            </button>
          </div>
        </DataPanel>
      </div>

      <DataPanel title="Payroll Shortcut">
        <p className="text-sm text-slate-500 dark:text-slate-400">Payslips are available through live `/api/ess/payslips` and downloads are logged as sensitive access.</p>
      </DataPanel>
    </div>
  );
}
