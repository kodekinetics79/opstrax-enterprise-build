import { FormEvent, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Download, MessageCircle, MessageSquare, Radio, Send, Users, X } from "lucide-react";
import { ErrorState, KpiCard, LoadingState, PageHeader, Select, StatusBadge, exportCsv } from "@/components/ui";
import { useHasPermission } from "@/hooks/usePermission";
import { apiClient } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

const TABS = ["Compose", "History", "Templates", "Broadcasts"] as const;
type Tab = typeof TABS[number];

const SEED_MESSAGES: AnyRecord[] = [
  { id: 1,  recipient: "Marcus Carter",    vehicleCode: "T-108", channel: "In-App",   subject: "Dispatch instructions — BOL-0891",       body: "Load is ready at dock 3. Proceed to FresCo Foods per route RT-0072.", status: "Delivered", sentAt: "10:42 AM", replies: 1, replyPreview: "Confirmed, en route." },
  { id: 2,  recipient: "James Williams",   vehicleCode: "T-104", channel: "SMS",      subject: "Safety coaching reminder",               body: "Your hard braking score needs improvement. Please review coaching brief in your driver app.", status: "Delivered", sentAt: "09:15 AM", replies: 0, replyPreview: "" },
  { id: 3,  recipient: "Ravi Patel",       vehicleCode: "T-110", channel: "In-App",   subject: "HOS approaching limit",                  body: "You have 48 minutes of drive time remaining. Plan your next break accordingly.", status: "Read",      sentAt: "08:55 AM", replies: 1, replyPreview: "I know, pulling off at next rest area." },
  { id: 4,  recipient: "All Active Drivers",vehicleCode: "—",    channel: "Broadcast",subject: "Weather alert — I-95 corridor",          body: "Heavy rain and reduced visibility on I-95 North. Slow down and increase following distance.", status: "Delivered", sentAt: "Yesterday", replies: 0, replyPreview: "" },
  { id: 5,  recipient: "Sarah Mitchell",   vehicleCode: "T-103", channel: "SMS",      subject: "Route change — stop resequenced",        body: "Stop 3 (Metro Logistics) moved to stop 4. FresCo Foods is now stop 3. Update your ETA.", status: "Delivered", sentAt: "Yesterday", replies: 1, replyPreview: "Got it, adjusting now." },
  { id: 6,  recipient: "Depot — Morning Shift", vehicleCode: "—", channel: "Broadcast", subject: "Pre-departure briefing",             body: "All trucks must complete DVIR before departure. No exceptions. Form available in driver app.", status: "Delivered", sentAt: "Yesterday", replies: 0, replyPreview: "" },
  { id: 7,  recipient: "Andre Thompson",   vehicleCode: "T-112", channel: "In-App",   subject: "Fuel card anomaly",                      body: "Your fuel transaction at Shell I-66 flagged as off-route. Please explain at check-in.", status: "Read",      sentAt: "2 days ago", replies: 1, replyPreview: "I detoured due to road closure." },
  { id: 8,  recipient: "Linda Chen",       vehicleCode: "T-107", channel: "SMS",      subject: "Cold chain alert — reefer temp",         body: "Reefer unit temp drift detected. Verify thermostat and report status to dispatch.", status: "Delivered", sentAt: "2 days ago", replies: 2, replyPreview: "Checked — unit recalibrated." },
];

const SEED_TEMPLATES: AnyRecord[] = [
  { id: 1,  title: "Dispatch Instructions",    category: "Operations",  body: "Load is ready at {dock}. Proceed to {customer} per route {route}. Estimated travel time: {eta}.",  usageCount: 28 },
  { id: 2,  title: "Route Change Alert",       category: "Operations",  body: "Route change: Stop {stop_number} ({location}) has been resequenced. Please update your ETA accordingly.", usageCount: 14 },
  { id: 3,  title: "Safety Coaching Reminder", category: "Safety",      body: "Your {event_type} score requires improvement. A coaching brief has been assigned. Please review in your driver app.", usageCount: 42 },
  { id: 4,  title: "HOS Warning",              category: "Compliance",  body: "You have {minutes} minutes of drive time remaining. Plan your break per DOT regulations.", usageCount: 67 },
  { id: 5,  title: "Weather Alert",            category: "Safety",      body: "Weather advisory: {conditions} reported on {road}. Reduce speed and increase following distance.", usageCount: 19 },
  { id: 6,  title: "DVIR Reminder",            category: "Compliance",  body: "Your pre-trip DVIR is required before departure. Complete the inspection form in your driver app.", usageCount: 88 },
  { id: 7,  title: "ETA Update Request",       category: "Customer",    body: "Customer {customer} is requesting an updated ETA for delivery {shipment}. Please confirm your current position.", usageCount: 31 },
  { id: 8,  title: "Fuel Anomaly Notice",      category: "Finance",     body: "A fuel transaction at {location} was flagged. Please explain the purchase at your next check-in.", usageCount: 9 },
  { id: 9,  title: "Cold Chain Check",         category: "Safety",      body: "Temperature deviation detected on Reefer {unit}. Verify thermostat and report status to dispatch immediately.", usageCount: 7 },
  { id: 10, title: "End of Day Summary",       category: "Operations",  body: "Please confirm: (1) DVIR complete (2) Load secured (3) Next day start time. Reply with confirmation.", usageCount: 55 },
];

const SEED_BROADCASTS: AnyRecord[] = [
  { id: 1, subject: "Weather alert — I-95 corridor", recipients: "All Active Drivers (12)", sentAt: "Yesterday 08:30", status: "Delivered", reads: 12, replies: 0 },
  { id: 2, subject: "Pre-departure DVIR briefing",   recipients: "Depot — Morning Shift (8)", sentAt: "Yesterday 06:00", status: "Delivered", reads: 8, replies: 0 },
  { id: 3, subject: "Fuel card policy update",       recipients: "All Drivers (22)",          sentAt: "3 days ago",      status: "Delivered", reads: 19, replies: 3 },
  { id: 4, subject: "HOS audit next week",           recipients: "All Drivers (22)",          sentAt: "1 week ago",      status: "Delivered", reads: 21, replies: 2 },
];

const MOCK_DRIVERS = [
  "Marcus Carter — T-108", "James Williams — T-104", "Ravi Patel — T-110",
  "Sarah Mitchell — T-103", "Andre Thompson — T-112", "Linda Chen — T-107",
  "Robert Kim — T-106", "Maria Gonzalez — T-109", "David Park — T-111",
];

async function fetchMessages(): Promise<AnyRecord[]> {
  try {
    const res = await apiClient.get("/api/driver-messages");
    const data = (res.data as AnyRecord[]) ?? [];
    return data.length ? data : SEED_MESSAGES;
  } catch {
    return SEED_MESSAGES;
  }
}

async function sendMessage(payload: AnyRecord) { return apiClient.post("/api/driver-messages", payload); }
async function broadcastMessage(payload: AnyRecord) { return apiClient.post("/api/driver-messages/broadcast", payload); }

const CHANNEL_COLOR: Record<string, string> = {
  "In-App":    "border-blue-200 bg-blue-50 text-blue-700",
  "SMS":       "border-green-200 bg-green-50 text-green-700",
  "Broadcast": "border-violet-200 bg-violet-50 text-violet-700",
};

const TEMPLATE_CAT_COLOR: Record<string, string> = {
  Operations:  "border-teal-200 bg-teal-50 text-teal-700",
  Safety:      "border-red-200 bg-red-50 text-red-700",
  Compliance:  "border-amber-200 bg-amber-50 text-amber-700",
  Customer:    "border-blue-200 bg-blue-50 text-blue-700",
  Finance:     "border-green-200 bg-green-50 text-green-700",
};

export function DriverMessagingPage() {
  const hasPermission = useHasPermission();
  const canSend = hasPermission("dispatch:update") || hasPermission("dispatch:assign") || hasPermission("users:manage");
  const [tab, setTab] = useState<Tab>("Compose");
  const [composeForm, setComposeForm] = useState({ recipient: "", channel: "In-App", subject: "", body: "" });
  const [broadcastForm, setBroadcastForm] = useState({ group: "All Active Drivers", subject: "", body: "" });
  const [sent, setSent] = useState(false);
  const [sentBroadcast, setSentBroadcast] = useState(false);
  const qc = useQueryClient();

  const messagesQ = useQuery({ queryKey: ["driver-messages"], queryFn: fetchMessages });
  const messages = (messagesQ.data ?? []) as AnyRecord[];

  const sendMut = useMutation({
    mutationFn: sendMessage,
    onSuccess: () => {
      setComposeForm({ recipient: "", channel: "In-App", subject: "", body: "" });
      setSent(true);
      setTimeout(() => setSent(false), 3000);
      qc.invalidateQueries({ queryKey: ["driver-messages"] });
    },
  });
  const broadcastMut = useMutation({
    mutationFn: broadcastMessage,
    onSuccess: () => {
      setBroadcastForm({ group: "All Active Drivers", subject: "", body: "" });
      setSentBroadcast(true);
      setTimeout(() => setSentBroadcast(false), 3000);
      qc.invalidateQueries({ queryKey: ["driver-messages"] });
    },
  });

  const deliveredCount = messages.filter((m) => String(m.status) === "Delivered").length;
  const readCount      = messages.filter((m) => String(m.status) === "Read").length;
  const replyCount     = messages.reduce((s, m) => s + Number(m.replies ?? 0), 0);
  const broadcastCount = messages.filter((m) => String(m.channel) === "Broadcast").length;

  if (messagesQ.isLoading) return <LoadingState />;
  if (messagesQ.isError) return <ErrorState message="Unable to load driver messages." />;

  function handleSend(e: FormEvent) {
    e.preventDefault();
    sendMut.mutate(composeForm);
  }
  function handleBroadcast(e: FormEvent) {
    e.preventDefault();
    broadcastMut.mutate(broadcastForm);
  }

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Driver Messaging"
        title="Direct communication with your fleet"
        description="Send individual messages, dispatch instructions, safety alerts and broadcast announcements to drivers via in-app, SMS and push channels."
        actions={
          <>
            <button type="button" className="btn-primary" onClick={() => setTab("Compose")}><Send className="h-4 w-4" /> New Message</button>
            <button type="button" className="btn-ghost" onClick={() => exportCsv("driver-messages", messages)}><Download className="h-4 w-4" /> Export</button>
          </>
        }
      />

      {/* KPIs */}
      <div className="grid gap-4 md:grid-cols-4">
        <KpiCard label="Delivered"       value={String(deliveredCount)} icon={<MessageCircle />} status="Healthy" />
        <KpiCard label="Read"            value={String(readCount)}      icon={<MessageSquare />} status="Active"  />
        <KpiCard label="Driver Replies"  value={String(replyCount)}     icon={<Users />}         status="Healthy" />
        <KpiCard label="Broadcasts Sent" value={String(broadcastCount)} icon={<Radio />}         status="Healthy" />
      </div>

      {/* Tabs */}
      <div className="flex gap-1.5 p-2">
        {TABS.map((t) => (
          <button key={t} type="button" onClick={() => setTab(t)}
            className={`control-tab ${tab === t ? "control-tab-active" : ""}`}>
            {t}
          </button>
        ))}
      </div>

      {/* Compose */}
      {tab === "Compose" && (
        <div className="grid gap-6 xl:grid-cols-[1fr_320px]">
          <form className="panel space-y-5 p-6" onSubmit={handleSend}>
            <h2 className="text-lg font-semibold text-slate-900">Compose Message</h2>
            <label>
              <span className="field-label">Recipient Driver</span>
              <Select className="mt-1 w-full" value={composeForm.recipient} onChange={(e) => setComposeForm((f) => ({ ...f, recipient: e.target.value }))} required>
                <option value="">Select driver…</option>
                {MOCK_DRIVERS.map((d) => <option key={d}>{d}</option>)}
              </Select>
            </label>
            <label>
              <span className="field-label">Channel</span>
              <Select className="mt-1 w-full" value={composeForm.channel} onChange={(e) => setComposeForm((f) => ({ ...f, channel: e.target.value }))}>
                <option>In-App</option><option>SMS</option>
              </Select>
            </label>
            <label>
              <span className="field-label">Subject</span>
              <input className="field mt-1" value={composeForm.subject} onChange={(e) => setComposeForm((f) => ({ ...f, subject: e.target.value }))} placeholder="e.g. Dispatch instructions — BOL-0891" required />
            </label>
            <label>
              <span className="field-label">Message</span>
              <textarea className="field mt-1 min-h-30 resize-y" value={composeForm.body} onChange={(e) => setComposeForm((f) => ({ ...f, body: e.target.value }))} placeholder="Type your message to the driver…" required />
            </label>
            <div className="flex items-center gap-3">
              <button type="submit" className="btn-primary" disabled={sendMut.isPending || !canSend} title={!canSend ? "You do not have permission to send messages." : undefined}>
                <Send className="h-4 w-4" /> {sendMut.isPending ? "Sending…" : "Send Message"}
              </button>
              {sent && <span className="text-sm font-semibold text-emerald-600">Message sent!</span>}
            </div>
          </form>

          <div className="panel p-5 space-y-4">
            <h3 className="section-title">Quick Templates</h3>
            {SEED_TEMPLATES.slice(0, 6).map((t) => (
              <button key={String(t.id)} type="button" className="w-full rounded-xl border border-slate-200 p-3 text-left transition hover:border-teal-300 hover:bg-teal-50"
                onClick={() => setComposeForm((f) => ({ ...f, subject: String(t.title), body: String(t.body) }))}>
                <p className="text-xs font-semibold text-slate-900">{String(t.title)}</p>
                <p className="mt-0.5 line-clamp-2 text-xs text-slate-400">{String(t.body)}</p>
              </button>
            ))}
          </div>
        </div>
      )}

      {/* History */}
      {tab === "History" && (
        <div className="panel overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50">
                  {["Recipient", "Subject", "Channel", "Status", "Replies", "Sent"].map((h) => (
                    <th key={h} className="px-4 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-500">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {messages.map((m) => (
                  <tr key={String(m.id)} className="transition hover:bg-slate-50">
                    <td className="px-4 py-3">
                      <p className="font-medium text-slate-900">{String(m.recipient)}</p>
                      <p className="text-xs text-slate-400">{String(m.vehicleCode ?? "")}</p>
                    </td>
                    <td className="px-4 py-3">
                      <p className="text-slate-700">{String(m.subject)}</p>
                      {!!m.replyPreview && <p className="mt-0.5 text-xs text-teal-600 italic">Reply: "{String(m.replyPreview)}"</p>}
                    </td>
                    <td className="px-4 py-3">
                      <span className={`inline-flex items-center rounded-full border px-2 py-0.5 text-[10px] font-bold ${CHANNEL_COLOR[String(m.channel)] ?? "border-slate-200 bg-slate-50 text-slate-600"}`}>{String(m.channel)}</span>
                    </td>
                    <td className="px-4 py-3"><StatusBadge status={m.status} /></td>
                    <td className="px-4 py-3 text-center">
                      {Number(m.replies) > 0
                        ? <span className="inline-flex h-5 w-5 items-center justify-center rounded-full bg-teal-100 text-[10px] font-bold text-teal-700">{Number(m.replies)}</span>
                        : <span className="text-slate-300">—</span>
                      }
                    </td>
                    <td className="px-4 py-3 text-xs text-slate-400 whitespace-nowrap">{String(m.sentAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Templates */}
      {tab === "Templates" && (
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
          {SEED_TEMPLATES.map((t) => {
            const catColor = TEMPLATE_CAT_COLOR[String(t.category)] ?? "border-slate-200 bg-slate-50 text-slate-600";
            return (
              <div key={String(t.id)} className="panel flex flex-col gap-3 p-4">
                <div className="flex items-start justify-between gap-2">
                  <p className="font-semibold text-slate-900">{String(t.title)}</p>
                  <span className={`inline-flex shrink-0 items-center rounded-full border px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide ${catColor}`}>{String(t.category)}</span>
                </div>
                <p className="text-xs text-slate-500 leading-relaxed">{String(t.body)}</p>
                <div className="flex items-center justify-between mt-auto pt-2 border-t border-slate-100">
                  <span className="text-xs text-slate-400">Used {Number(t.usageCount)}×</span>
                  <button type="button" className="text-xs font-medium text-teal-600 hover:underline"
                    onClick={() => { setTab("Compose"); setComposeForm((f) => ({ ...f, subject: String(t.title), body: String(t.body) })); }}>
                    Use template →
                  </button>
                </div>
              </div>
            );
          })}
        </div>
      )}

      {/* Broadcasts */}
      {tab === "Broadcasts" && (
        <div className="grid gap-6 xl:grid-cols-[1fr_360px]">
          <div className="panel overflow-hidden">
            <div className="border-b border-slate-200 px-5 py-4">
              <h2 className="section-title">Broadcast History</h2>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-slate-200 bg-slate-50">
                    {["Subject", "Recipients", "Status", "Reads", "Replies", "Sent"].map((h) => (
                      <th key={h} className="px-4 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-500">{h}</th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {SEED_BROADCASTS.map((b) => (
                    <tr key={String(b.id)} className="transition hover:bg-slate-50">
                      <td className="px-4 py-3 font-medium text-slate-900">{String(b.subject)}</td>
                      <td className="px-4 py-3 text-slate-500">{String(b.recipients)}</td>
                      <td className="px-4 py-3"><StatusBadge status={b.status} /></td>
                      <td className="px-4 py-3 text-center text-slate-700">{Number(b.reads)}</td>
                      <td className="px-4 py-3 text-center">{Number(b.replies) > 0 ? <span className="text-teal-700 font-semibold">{Number(b.replies)}</span> : <span className="text-slate-300">—</span>}</td>
                      <td className="px-4 py-3 text-xs text-slate-400 whitespace-nowrap">{String(b.sentAt)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          <form className="panel space-y-4 p-5" onSubmit={handleBroadcast}>
            <h3 className="section-title">Send Broadcast</h3>
            <label>
              <span className="field-label">Recipient Group</span>
              <Select className="mt-1 w-full" value={broadcastForm.group} onChange={(e) => setBroadcastForm((f) => ({ ...f, group: e.target.value }))}>
                <option>All Active Drivers</option>
                <option>All Drivers</option>
                <option>Depot — Morning Shift</option>
                <option>Depot — Afternoon Shift</option>
                <option>Long-Haul Drivers</option>
                <option>Local Delivery Drivers</option>
              </Select>
            </label>
            <label>
              <span className="field-label">Subject</span>
              <input className="field mt-1" value={broadcastForm.subject} onChange={(e) => setBroadcastForm((f) => ({ ...f, subject: e.target.value }))} placeholder="Broadcast subject…" required />
            </label>
            <label>
              <span className="field-label">Message</span>
              <textarea className="field mt-1 min-h-25 resize-y" value={broadcastForm.body} onChange={(e) => setBroadcastForm((f) => ({ ...f, body: e.target.value }))} placeholder="Message to all selected drivers…" required />
            </label>
            <div className="flex items-center gap-3">
              <button type="submit" className="btn-primary w-full" disabled={broadcastMut.isPending || !canSend} title={!canSend ? "You do not have permission." : undefined}>
                <Radio className="h-4 w-4 mr-1.5" /> {broadcastMut.isPending ? "Broadcasting…" : "Send Broadcast"}
              </button>
            </div>
            {sentBroadcast && <p className="text-center text-sm font-semibold text-emerald-600">Broadcast sent to all drivers!</p>}
          </form>
        </div>
      )}
    </div>
  );
}
