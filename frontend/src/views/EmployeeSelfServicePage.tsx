'use client';

import { useEffect, useState } from 'react';
import {
  Bot, Loader2, CalendarOff, Send, FileText, Clock,
  ChevronRight, Megaphone, CheckCircle2, AlertCircle,
  Zap, ClipboardList,
} from 'lucide-react';
import { useRouter } from 'next/navigation';
import { essApi, type EssDashboard } from '../api/ess';
import { useAuth } from '../contexts/AuthContext';
import { StatusChip } from '../components/StatusChip';

// ── helpers ───────────────────────────────────────────────────────────────────

function greeting(): string {
  const h = new Date().getHours();
  if (h < 12) return 'Good morning';
  if (h < 17) return 'Good afternoon';
  return 'Good evening';
}

function todayLabel(): string {
  return new Date().toLocaleDateString('en-GB', { weekday: 'long', day: 'numeric', month: 'long' });
}

function minutes(value?: number) {
  if (!value) return '0h 0m';
  const h = Math.floor(value / 60);
  const m = value % 60;
  return `${h}h ${m}m`;
}

// ── Leave balance bar ────────────────────────────────────────────────────────

function LeaveBar({ name, available, entitled }: { name: string; available: number; entitled: number }) {
  const pct = entitled > 0 ? Math.round((available / entitled) * 100) : 0;
  const color = pct >= 60 ? 'bg-emerald-500' : pct >= 30 ? 'bg-amber-500' : 'bg-rose-500';
  return (
    <div className="space-y-1.5">
      <div className="flex items-center justify-between text-xs">
        <span className="font-medium text-slate-700 dark:text-slate-200">{name}</span>
        <span className="tabular-nums text-slate-500 dark:text-slate-400">
          <span className="font-semibold text-slate-800 dark:text-white">{available.toFixed(1)}</span>
          {entitled > 0 && <span> / {entitled.toFixed(1)} days</span>}
        </span>
      </div>
      <div className="h-1.5 w-full overflow-hidden rounded-full bg-slate-100 dark:bg-white/[0.07]">
        {/* eslint-disable-next-line react/forbid-dom-props */}
        <div className={`h-full rounded-full transition-all duration-700 ${color}`} style={{ width: `${Math.min(100, pct)}%` }} />
      </div>
    </div>
  );
}

// ── Quick action card ─────────────────────────────────────────────────────────

function QuickCard({
  icon: Icon, label, description, onClick, accent = 'slate',
}: {
  icon: React.ElementType; label: string; description: string;
  onClick: () => void; accent?: 'blue' | 'emerald' | 'violet' | 'amber' | 'slate';
}) {
  const cls = {
    blue:    'bg-blue-500/[0.08] border-blue-500/20 text-blue-500 dark:text-blue-400',
    emerald: 'bg-emerald-500/[0.08] border-emerald-500/20 text-emerald-600 dark:text-emerald-400',
    violet:  'bg-violet-500/[0.08] border-violet-500/20 text-violet-600 dark:text-violet-400',
    amber:   'bg-amber-500/[0.08] border-amber-500/20 text-amber-600 dark:text-amber-400',
    slate:   'bg-slate-100 border-slate-200 text-slate-600 dark:bg-white/[0.05] dark:border-white/10 dark:text-slate-300',
  };
  return (
    <button
      type="button"
      onClick={onClick}
      className="group flex flex-col gap-3 rounded-xl border bg-white p-4 text-left transition-all hover:shadow-md hover:-translate-y-0.5 dark:bg-white/[0.03] dark:hover:bg-white/[0.06]"
    >
      <div className={`flex h-10 w-10 items-center justify-center rounded-xl border ${cls[accent]}`}>
        <Icon className="h-4.5 w-4.5 h-[18px] w-[18px]" />
      </div>
      <div>
        <p className="text-sm font-semibold text-slate-900 dark:text-white group-hover:text-sapphire dark:group-hover:text-cyanAccent transition-colors">{label}</p>
        <p className="mt-0.5 text-xs text-slate-500 dark:text-slate-400">{description}</p>
      </div>
      <ChevronRight className="h-3.5 w-3.5 text-slate-300 dark:text-slate-600 self-end mt-auto group-hover:text-sapphire dark:group-hover:text-cyanAccent transition-colors" />
    </button>
  );
}

// ── Announcement card ─────────────────────────────────────────────────────────

function AnnouncementCard({ title, body }: { title: string; body: string }) {
  return (
    <div className="flex gap-3 rounded-xl border border-slate-100 bg-white p-4 dark:border-white/[0.07] dark:bg-white/[0.03]">
      <div className="mt-0.5 flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-blue-500/10">
        <Megaphone className="h-3.5 w-3.5 text-blue-500 dark:text-blue-400" />
      </div>
      <div className="min-w-0">
        <p className="text-sm font-semibold text-slate-900 dark:text-white">{title}</p>
        <p className="mt-0.5 line-clamp-2 text-xs leading-relaxed text-slate-500 dark:text-slate-400">{body}</p>
      </div>
    </div>
  );
}

// ── Attendance status pill ────────────────────────────────────────────────────

function AttendancePill({ status, worked, missing }: { status?: string; worked?: number; missing?: boolean }) {
  if (!status) return <span className="rounded-full bg-slate-100 px-3 py-1 text-xs text-slate-500 dark:bg-white/[0.07] dark:text-slate-400">No record today</span>;
  return (
    <span className={`inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-xs font-semibold ${
      missing ? 'bg-rose-500/10 text-rose-600 dark:text-rose-400' : 'bg-emerald-500/10 text-emerald-600 dark:text-emerald-400'
    }`}>
      {missing ? <AlertCircle className="h-3.5 w-3.5" /> : <CheckCircle2 className="h-3.5 w-3.5" />}
      {status}{worked ? ` · ${minutes(worked)}` : ''}
    </span>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

export function EmployeeSelfServicePage() {
  const { user } = useAuth();
  const router = useRouter();
  const [dashboard, setDashboard] = useState<EssDashboard | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);

  // AI assistant mini-widget
  const [question, setQuestion] = useState('');
  const [answer, setAnswer] = useState('');
  const [asking, setAsking] = useState(false);

  // HR request
  const [ticketSubject, setTicketSubject] = useState('');
  const [ticketMessage, setTicketMessage] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [submitted, setSubmitted] = useState(false);

  const load = async () => {
    setLoading(true); setError('');
    try { setDashboard(await essApi.dashboard()); }
    catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      setError(e.response?.data?.message ?? 'Unable to load your workspace.');
    } finally { setLoading(false); }
  };

  useEffect(() => { load(); }, []);

  const askAi = async () => {
    if (!question.trim()) return;
    setAsking(true); setAnswer('');
    try { const res = await essApi.askAi(question); setAnswer(res.answer); }
    catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      setAnswer(e.response?.data?.message ?? 'The assistant could not answer right now.');
    } finally { setAsking(false); }
  };

  const createTicket = async () => {
    if (!ticketSubject.trim() || !ticketMessage.trim()) return;
    setSubmitting(true);
    try {
      await essApi.createHrRequest({ categoryName: 'General HR', subject: ticketSubject, description: ticketMessage, priority: 'Normal' });
      setTicketSubject(''); setTicketMessage(''); setSubmitted(true);
      setTimeout(() => setSubmitted(false), 4000);
      await load();
    } finally { setSubmitting(false); }
  };

  if (loading) {
    return (
      <div className="flex h-96 items-center justify-center">
        <div className="flex flex-col items-center gap-3 text-slate-400">
          <Loader2 className="h-6 w-6 animate-spin text-sapphire dark:text-cyanAccent" />
          <p className="text-sm">Loading your workspace…</p>
        </div>
      </div>
    );
  }

  if (error || !dashboard) {
    return (
      <div className="rounded-xl border border-rose-200 bg-rose-50 p-6 text-sm text-rose-700 dark:border-rose-500/30 dark:bg-rose-500/10 dark:text-rose-200">
        {error || 'ESS dashboard was empty.'}
      </div>
    );
  }

  const attendance = dashboard.attendanceToday;
  const leaveAvailable = dashboard.leaveBalances.reduce((s, b) => s + b.available, 0);
  const firstName = (dashboard.profile.fullName ?? user?.fullName ?? 'there').split(' ')[0];

  return (
    <div className="mx-auto max-w-[1400px] space-y-6">

      {/* ── Hero ─────────────────────────────────────────────────────────────── */}
      <div className="relative overflow-hidden rounded-2xl border border-slate-100 bg-gradient-to-br from-white via-blue-50/40 to-indigo-50/30 p-6 dark:border-white/[0.07] dark:from-[#0f1729] dark:via-[#101e36] dark:to-[#0d1525]">
        {/* Decorative blob */}
        <div className="pointer-events-none absolute right-0 top-0 h-48 w-48 translate-x-12 -translate-y-12 rounded-full bg-sapphire/[0.06] blur-3xl dark:bg-blue-500/[0.12]" />
        <div className="pointer-events-none absolute bottom-0 left-1/2 h-32 w-64 -translate-y-4 rounded-full bg-indigo-400/[0.04] blur-2xl dark:bg-indigo-500/[0.07]" />

        <div className="relative flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <p className="text-[11px] font-bold uppercase tracking-widest text-sapphire dark:text-cyanAccent">{todayLabel()}</p>
            <h1 className="mt-1 text-2xl font-extrabold tracking-tight text-slate-900 dark:text-white">
              {greeting()}, {firstName} 👋
            </h1>
            <p className="mt-1 text-sm text-slate-500 dark:text-slate-400">
              {dashboard.profile.jobTitle || 'Employee'} · {dashboard.profile.department || 'Unassigned'}
            </p>
            <div className="mt-3">
              <AttendancePill
                status={attendance?.status}
                worked={attendance?.totalWorkedMinutes}
                missing={attendance?.missingPunch}
              />
            </div>
          </div>

          {/* Stats strip */}
          <div className="flex shrink-0 gap-3">
            {[
              { label: 'Leave available', value: `${leaveAvailable.toFixed(1)}d`, accent: 'text-emerald-600 dark:text-emerald-400' },
              { label: 'Pending requests', value: dashboard.pendingRequests.toString(), accent: 'text-amber-600 dark:text-amber-400' },
              { label: 'Document alerts', value: dashboard.documentAlerts.length.toString(), accent: dashboard.documentAlerts.length > 0 ? 'text-rose-600 dark:text-rose-400' : 'text-slate-600 dark:text-slate-300' },
            ].map(({ label, value, accent }) => (
              <div key={label} className="min-w-[72px] rounded-xl border border-white/60 bg-white/80 p-3 text-center shadow-sm dark:border-white/[0.07] dark:bg-white/[0.04]">
                <p className={`text-xl font-extrabold leading-none ${accent}`}>{value}</p>
                <p className="mt-1 text-[10px] leading-tight text-slate-400 dark:text-slate-500">{label}</p>
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* ── Quick actions ─────────────────────────────────────────────────────── */}
      <section>
        <p className="mb-3 text-[11px] font-bold uppercase tracking-widest text-slate-400 dark:text-slate-600">Quick Actions</p>
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
          <QuickCard icon={CalendarOff} label="Request Leave" description="Submit time off" onClick={() => router.push('/leave')} accent="emerald" />
          <QuickCard icon={ClipboardList} label="My Requests" description="Track your HR requests" onClick={() => router.push('/hr-requests')} accent="blue" />
          <QuickCard icon={FileText} label="View Payslips" description="Download payroll slips" onClick={() => router.push('/payroll')} accent="violet" />
          <QuickCard icon={Zap} label="Overtime" description="Log overtime hours" onClick={() => router.push('/overtime')} accent="amber" />
        </div>
      </section>

      {/* ── Two-column content ────────────────────────────────────────────────── */}
      <div className="grid gap-5 xl:grid-cols-[1.5fr_1fr]">

        {/* Left col */}
        <div className="space-y-5">

          {/* Action center */}
          {(dashboard.actionItems.length > 0 || dashboard.documentAlerts.length > 0) && (
            <section className="rounded-xl border border-slate-100 bg-white dark:border-white/[0.07] dark:bg-white/[0.03]">
              <div className="flex items-center justify-between border-b border-slate-100 px-5 py-3.5 dark:border-white/[0.07]">
                <p className="text-sm font-semibold text-slate-900 dark:text-white">Action Center</p>
                <span className="rounded-full bg-rose-500/10 px-2 py-0.5 text-[10px] font-bold text-rose-600 dark:text-rose-400">
                  {dashboard.actionItems.length + dashboard.documentAlerts.length} open
                </span>
              </div>
              <div className="divide-y divide-slate-50 dark:divide-white/[0.04]">
                {dashboard.documentAlerts.map((doc) => (
                  <div key={doc.id} className="flex items-center justify-between gap-4 px-5 py-3.5">
                    <div className="flex items-start gap-3 min-w-0">
                      <div className="mt-0.5 flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-rose-500/10">
                        <AlertCircle className="h-3.5 w-3.5 text-rose-500" />
                      </div>
                      <div className="min-w-0">
                        <p className="text-sm font-medium text-slate-900 dark:text-white truncate">{doc.documentType}</p>
                        <p className="text-xs text-slate-500 dark:text-slate-400 truncate">{doc.fileName || 'Document'} · expires {doc.expiryDate ?? 'not set'}</p>
                      </div>
                    </div>
                    <StatusChip label={doc.approvalStatus} />
                  </div>
                ))}
                {dashboard.actionItems.map((item) => (
                  <div key={item.id} className="flex items-center justify-between gap-4 px-5 py-3.5">
                    <div className="flex items-start gap-3 min-w-0">
                      <div className="mt-0.5 flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-amber-500/10">
                        <Clock className="h-3.5 w-3.5 text-amber-500" />
                      </div>
                      <div className="min-w-0">
                        <p className="text-sm font-medium text-slate-900 dark:text-white truncate">{item.title}</p>
                        <p className="text-xs text-slate-500 dark:text-slate-400">{item.category}</p>
                      </div>
                    </div>
                    <StatusChip label={item.dueAtUtc ? 'Due' : 'Open'} />
                  </div>
                ))}
              </div>
            </section>
          )}

          {/* AI assistant */}
          <section className="rounded-xl border border-slate-100 bg-white dark:border-white/[0.07] dark:bg-white/[0.03]">
            <div className="flex items-center gap-2.5 border-b border-slate-100 px-5 py-3.5 dark:border-white/[0.07]">
              <div className="flex h-7 w-7 items-center justify-center rounded-lg bg-sapphire/10 dark:bg-cyanAccent/10">
                <Bot className="h-4 w-4 text-sapphire dark:text-cyanAccent" />
              </div>
              <p className="text-sm font-semibold text-slate-900 dark:text-white">AI HR Assistant</p>
            </div>
            <div className="space-y-3 p-5">
              <textarea
                value={question}
                onChange={(e) => setQuestion(e.target.value)}
                onKeyDown={(e) => { if (e.key === 'Enter' && (e.metaKey || e.ctrlKey)) askAi(); }}
                placeholder="Ask anything — leave balance, policies, payslip dates…"
                rows={3}
                className="w-full resize-none rounded-xl border border-slate-200 bg-slate-50/80 px-4 py-3 text-sm text-slate-900 placeholder-slate-400 outline-none transition focus:border-sapphire/50 focus:ring-2 focus:ring-sapphire/10 dark:border-white/[0.08] dark:bg-white/[0.04] dark:text-white dark:placeholder-slate-600 dark:focus:border-cyanAccent/40"
              />
              <div className="flex items-center justify-between gap-3">
                <p className="text-[10px] text-slate-400 dark:text-slate-600">⌘ + Enter to send</p>
                <button
                  type="button"
                  onClick={askAi}
                  disabled={asking || !question.trim()}
                  className="inline-flex items-center gap-1.5 rounded-lg bg-sapphire px-4 py-2 text-sm font-semibold text-white transition hover:bg-sapphire/90 disabled:opacity-50 dark:bg-cyanAccent dark:text-slate-900"
                >
                  {asking ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Send className="h-3.5 w-3.5" />}
                  {asking ? 'Thinking…' : 'Ask'}
                </button>
              </div>
              {answer && (
                <div className="rounded-xl border border-sapphire/15 bg-sapphire/[0.04] p-4 text-sm leading-relaxed text-slate-700 dark:border-cyanAccent/15 dark:bg-cyanAccent/[0.04] dark:text-slate-200">
                  {answer}
                </div>
              )}
            </div>
          </section>
        </div>

        {/* Right col */}
        <div className="space-y-5">

          {/* Leave balances */}
          <section className="rounded-xl border border-slate-100 bg-white dark:border-white/[0.07] dark:bg-white/[0.03]">
            <div className="flex items-center justify-between border-b border-slate-100 px-5 py-3.5 dark:border-white/[0.07]">
              <p className="text-sm font-semibold text-slate-900 dark:text-white">Leave Balances</p>
              <button type="button" onClick={() => router.push('/leave')} className="text-[11px] font-medium text-sapphire hover:underline dark:text-cyanAccent">
                Request leave
              </button>
            </div>
            <div className="space-y-4 p-5">
              {dashboard.leaveBalances.length === 0 ? (
                <p className="text-sm text-slate-400 dark:text-slate-500">No leave balances configured.</p>
              ) : dashboard.leaveBalances.map((b) => (
                <LeaveBar
                  key={b.leaveTypeId}
                  name={b.leaveTypeName}
                  available={b.available}
                  entitled={b.entitled ?? b.available}
                />
              ))}
            </div>
          </section>

          {/* Announcements */}
          <section className="rounded-xl border border-slate-100 bg-white dark:border-white/[0.07] dark:bg-white/[0.03]">
            <div className="border-b border-slate-100 px-5 py-3.5 dark:border-white/[0.07]">
              <p className="text-sm font-semibold text-slate-900 dark:text-white">Announcements</p>
            </div>
            <div className="space-y-2.5 p-5">
              {dashboard.announcements.length === 0 ? (
                <p className="text-sm text-slate-400 dark:text-slate-500">No active announcements.</p>
              ) : dashboard.announcements.map((a) => (
                <AnnouncementCard key={a.id} title={a.title} body={a.body} />
              ))}
            </div>
          </section>

          {/* HR request */}
          <section className="rounded-xl border border-slate-100 bg-white dark:border-white/[0.07] dark:bg-white/[0.03]">
            <div className="border-b border-slate-100 px-5 py-3.5 dark:border-white/[0.07]">
              <p className="text-sm font-semibold text-slate-900 dark:text-white">Submit HR Request</p>
            </div>
            <div className="space-y-3 p-5">
              {submitted && (
                <div className="flex items-center gap-2 rounded-lg bg-emerald-500/10 px-3 py-2 text-sm font-medium text-emerald-600 dark:text-emerald-400">
                  <CheckCircle2 className="h-4 w-4 shrink-0" /> Request submitted successfully!
                </div>
              )}
              <input
                value={ticketSubject}
                onChange={(e) => setTicketSubject(e.target.value)}
                placeholder="Subject"
                className="w-full rounded-xl border border-slate-200 bg-slate-50/80 px-4 py-2.5 text-sm text-slate-900 placeholder-slate-400 outline-none transition focus:border-sapphire/50 focus:ring-2 focus:ring-sapphire/10 dark:border-white/[0.08] dark:bg-white/[0.04] dark:text-white dark:placeholder-slate-600"
              />
              <textarea
                value={ticketMessage}
                onChange={(e) => setTicketMessage(e.target.value)}
                placeholder="What do you need from HR?"
                rows={3}
                className="w-full resize-none rounded-xl border border-slate-200 bg-slate-50/80 px-4 py-2.5 text-sm text-slate-900 placeholder-slate-400 outline-none transition focus:border-sapphire/50 focus:ring-2 focus:ring-sapphire/10 dark:border-white/[0.08] dark:bg-white/[0.04] dark:text-white dark:placeholder-slate-600"
              />
              <button
                type="button"
                onClick={createTicket}
                disabled={submitting || !ticketSubject.trim() || !ticketMessage.trim()}
                className="inline-flex w-full items-center justify-center gap-1.5 rounded-xl bg-slate-900 py-2.5 text-sm font-semibold text-white transition hover:bg-slate-700 disabled:opacity-50 dark:bg-white dark:text-slate-900 dark:hover:bg-slate-100"
              >
                {submitting ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Send className="h-3.5 w-3.5" />}
                {submitting ? 'Submitting…' : 'Submit request'}
              </button>
            </div>
          </section>

        </div>
      </div>
    </div>
  );
}
