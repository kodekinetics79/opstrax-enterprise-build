import { FormEvent, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Download, MessageCircle, MessageSquare, Radio, Send, Users, X } from "lucide-react";
import { ErrorState, KpiCard, LoadingState, PageHeader, StatusBadge, exportCsv } from "@/components/ui";
import { useHasPermission } from "@/hooks/usePermission";
import { apiClient, unwrap } from "@/services/apiClient";
import { apiErrorMessage } from "@/utils/apiErrorMessage";
import type { AnyRecord } from "@/types";

const TABS = ["Compose", "History", "Templates", "Broadcasts"] as const;
type Tab = typeof TABS[number];

async function fetchMessages(): Promise<AnyRecord[]> {
  return unwrap<AnyRecord[]>(apiClient.get("/api/driver-messages"));
}

async function fetchDrivers(): Promise<AnyRecord[]> {
  return unwrap<AnyRecord[]>(apiClient.get("/api/drivers"));
}

// App-provided message presets the dispatcher can drop into the composer. This is
// form configuration (the templates a fleet actually sends), not synthetic data.
const MESSAGE_TEMPLATES = [
  { id: "dispatch", category: "Operations", title: "Dispatch instructions", body: "Your next assignment is ready — please review the pickup and delivery details in the app and confirm acceptance." },
  { id: "eta",      category: "Operations", title: "ETA request",           body: "Please share your current ETA for the active delivery, and notify dispatch immediately if you expect any delay." },
  { id: "safety",   category: "Safety",     title: "Safety alert",          body: "Road/weather conditions have changed on your route. Reduce speed, keep a safe following distance, and check in at your next stop." },
  { id: "hos",      category: "Compliance", title: "HOS reminder",          body: "You are approaching your Hours-of-Service limit. Plan your next rest break now and make sure your duty status is logged." },
  { id: "pod",      category: "Customer",   title: "Proof of delivery",     body: "Please capture the signature/photo proof of delivery at drop-off and submit it in the app before leaving the site." },
  { id: "docs",     category: "Compliance", title: "Document request",      body: "We need updated documents on file. Please upload your latest license and medical certificate in the driver app." },
];

async function sendMessage(payload: AnyRecord) { return unwrap<AnyRecord>(apiClient.post("/api/driver-messages", payload)); }
async function broadcastMessage(payload: AnyRecord) { return unwrap<AnyRecord>(apiClient.post("/api/driver-messages/broadcast", payload)); }

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
  const canSend = hasPermission("dispatch:manage");
  const [tab, setTab] = useState<Tab>("Compose");
  const [composeForm, setComposeForm] = useState({ recipientId: "", channel: "In-App", subject: "", body: "" });
  const [broadcastForm, setBroadcastForm] = useState({ group: "All Active Drivers", subject: "", body: "" });
  const [sendNotice, setSendNotice] = useState("");
  const [broadcastNotice, setBroadcastNotice] = useState("");
  const qc = useQueryClient();

  const messagesQ = useQuery({ queryKey: ["driver-messages"], queryFn: fetchMessages });
  const messages = (messagesQ.data ?? []) as AnyRecord[];
  const driversQ = useQuery({ queryKey: ["driver-messaging", "driver-options"], queryFn: fetchDrivers });
  const drivers = (driversQ.data ?? []) as AnyRecord[];

  const sendMut = useMutation({
    mutationFn: sendMessage,
    onSuccess: (result) => {
      setComposeForm({ recipientId: "", channel: "In-App", subject: "", body: "" });
      setSendNotice(`${String(result.recordStatus ?? "Recorded In-App")}. Delivery outside the in-app conversation is not claimed.`);
      qc.invalidateQueries({ queryKey: ["driver-messages"] });
    },
    onError: () => setSendNotice(""),
  });
  const broadcastMut = useMutation({
    mutationFn: broadcastMessage,
    onSuccess: (result) => {
      setBroadcastForm({ group: "All Active Drivers", subject: "", body: "" });
      setBroadcastNotice(`Recorded in ${String(result.recipientCount ?? 0)} authorized in-app inboxes.`);
      qc.invalidateQueries({ queryKey: ["driver-messages"] });
    },
    onError: () => setBroadcastNotice(""),
  });

  const recipientCount = messages.reduce((sum, m) => sum + Number(m.recipients ?? 0), 0);
  const readCount      = messages.reduce((sum, m) => sum + Number(m.reads ?? 0), 0);
  const replyCount     = messages.reduce((s, m) => s + Number(m.replies ?? 0), 0);
  const broadcasts = messages.filter((m) => String(m.channel) === "Broadcast");
  const broadcastCount = broadcasts.filter((m) => Number(m.recipients ?? 0) > 0).length;

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
    <div className="page-stack h-full overflow-y-auto">
      <PageHeader
        eyebrow="Driver Messaging"
        title="Direct communication with your fleet"
        description="Record individual conversations and broadcasts in authorized driver in-app inboxes, with persisted read and reply evidence."
        actions={
          <>
            <button type="button" className="btn-primary" onClick={() => setTab("Compose")}><Send className="h-4 w-4" /> New Message</button>
            <button type="button" className="btn-ghost" onClick={() => exportCsv("driver-messages", messages)}><Download className="h-4 w-4" /> Export</button>
          </>
        }
      />

      {/* KPIs */}
      <div className="grid gap-4 md:grid-cols-4">
        <KpiCard label="In-App Recipients" value={String(recipientCount)} icon={<MessageCircle />} />
        <KpiCard label="Read Receipts" value={String(readCount)} icon={<MessageSquare />} />
        <KpiCard label="Recorded Replies" value={String(replyCount)} icon={<Users />} />
        <KpiCard label="Recorded Broadcasts" value={String(broadcastCount)} icon={<Radio />} />
      </div>

      {/* Tabs */}
      <div className="flex gap-1 border-b border-slate-200 pb-px">
        {TABS.map((t) => (
          <button key={t} type="button" onClick={() => setTab(t)}
            className={`rounded-t-lg px-4 py-2 text-sm font-semibold transition ${tab === t ? "bg-teal-50 text-teal-700 border border-b-0 border-teal-300" : "text-slate-500 hover:text-slate-700"}`}>
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
              <select className="field mt-1" value={composeForm.recipientId} onChange={(e) => setComposeForm((f) => ({ ...f, recipientId: e.target.value }))} required disabled={driversQ.isLoading}>
                <option value="">{driversQ.isLoading ? "Loading drivers…" : drivers.length ? "Select driver…" : "No drivers found"}</option>
                {drivers.map((d) => {
                  const name = String(d.fullName ?? d.full_name ?? d.name ?? d.driverCode ?? d.id);
                  const code = d.driverCode ?? d.driver_code;
                  const hasAccount = d.userId != null || d.user_id != null;
                  return <option key={String(d.id)} value={String(d.id)} disabled={!hasAccount}>{name}{code ? ` · ${String(code)}` : ""}{hasAccount ? "" : " · no in-app account"}</option>;
                })}
              </select>
            </label>
            <label>
              <span className="field-label">Channel</span>
              <input className="field mt-1 bg-slate-50" value="In-App" readOnly aria-readonly="true" />
              <span className="mt-1 block text-xs text-slate-500">SMS and push require a configured provider before they can be offered.</span>
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
                <Send className="h-4 w-4" /> {sendMut.isPending ? "Recording…" : "Record In-App Message"}
              </button>
              {sendNotice && <span className="text-sm font-semibold text-emerald-700">{sendNotice}</span>}
            </div>
            {sendMut.isError && <p className="text-sm text-red-700" role="alert">{apiErrorMessage(sendMut.error, "The message was not recorded.")}</p>}
          </form>

          <div className="panel p-5 space-y-4">
            <h3 className="section-title">Quick Templates</h3>
            {MESSAGE_TEMPLATES.slice(0, 6).map((t) => (
              <button key={t.id} type="button" className="w-full rounded-xl border border-slate-200 p-3 text-left transition hover:border-teal-300 hover:bg-teal-50"
                onClick={() => setComposeForm((f) => ({ ...f, subject: t.title, body: t.body }))}>
                <p className="text-xs font-semibold text-slate-900">{t.title}</p>
                <p className="mt-0.5 line-clamp-2 text-xs text-slate-400">{t.body}</p>
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
                  {["Recipient", "Subject", "Channel", "Status", "Reads", "Replies", "Recorded"].map((h) => (
                    <th key={h} className="px-4 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-500">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {messages.filter((m) => String(m.channel) !== "Broadcast").map((m) => (
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
                    <td className="px-4 py-3 text-center text-slate-600">{Number(m.reads ?? 0)}</td>
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
          {MESSAGE_TEMPLATES.map((t) => {
            const catColor = TEMPLATE_CAT_COLOR[t.category] ?? "border-slate-200 bg-slate-50 text-slate-600";
            return (
              <div key={t.id} className="panel flex flex-col gap-3 p-4">
                <div className="flex items-start justify-between gap-2">
                  <p className="font-semibold text-slate-900">{t.title}</p>
                  <span className={`inline-flex shrink-0 items-center rounded-full border px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide ${catColor}`}>{t.category}</span>
                </div>
                <p className="text-xs text-slate-500 leading-relaxed">{t.body}</p>
                <div className="flex items-center justify-end mt-auto pt-2 border-t border-slate-100">
                  <button type="button" className="text-xs font-medium text-teal-600 hover:underline"
                    onClick={() => { setTab("Compose"); setComposeForm((f) => ({ ...f, subject: t.title, body: t.body })); }}>
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
                    {["Subject", "Recipients", "Status", "Reads", "Recorded"].map((h) => (
                      <th key={h} className="px-4 py-3 text-left text-[10px] font-bold uppercase tracking-widest text-slate-500">{h}</th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {broadcasts.length === 0 ? (
                    <tr><td colSpan={5} className="px-4 py-10 text-center text-sm text-slate-400">No evidence-backed broadcasts recorded yet.</td></tr>
                  ) : broadcasts.map((b) => (
                    <tr key={String(b.id)} className="transition hover:bg-slate-50">
                      <td className="px-4 py-3 font-medium text-slate-900">{String(b.subject)}</td>
                      <td className="px-4 py-3 text-slate-500">{String(b.recipients ?? 0)}</td>
                      <td className="px-4 py-3"><StatusBadge status={b.status} /></td>
                      <td className="px-4 py-3 text-center">{Number(b.reads ?? 0)}</td>
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
              <select className="field mt-1" value={broadcastForm.group} onChange={(e) => setBroadcastForm((f) => ({ ...f, group: e.target.value }))}>
                <option>All Active Drivers</option>
              </select>
              <span className="mt-1 block text-xs text-slate-500">Recipients are active driver accounts in your authorized branch scope.</span>
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
                <Radio className="h-4 w-4 mr-1.5" /> {broadcastMut.isPending ? "Recording…" : "Record In-App Broadcast"}
              </button>
            </div>
            {broadcastNotice && <p className="text-center text-sm font-semibold text-emerald-700">{broadcastNotice}</p>}
            {broadcastMut.isError && <p className="text-center text-sm text-red-700" role="alert">{apiErrorMessage(broadcastMut.error, "The broadcast was not recorded.")}</p>}
          </form>
        </div>
      )}
    </div>
  );
}
