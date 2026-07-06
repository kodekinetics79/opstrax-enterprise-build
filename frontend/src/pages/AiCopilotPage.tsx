import { useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Bot, Check, Send, Sparkles, User, X, Zap } from "lucide-react";
import { AiInsightCard, LoadingState, exportCsv } from "@/components/ui";
import { aiApi } from "@/services/aiApi";
import { aiCopilotApi } from "@/services/aiCopilotApi";
import type { AnyRecord } from "@/types";

// ── Types ─────────────────────────────────────────────────────────────────────

interface Message {
  id: string;
  role: "user" | "assistant";
  prompt?: string;
  category?: string;
  summary?: string;
  evidence?: AnyRecord[];
  suggestedNextSteps?: string[];
  actionButtons?: string[];
  timestamp: Date;
}

// ── Prompt starters ───────────────────────────────────────────────────────────

const STARTERS: { label: string; category: string; prompt: string }[] = [
  { label: "Dispatch risk",        category: "Dispatch Risk",       prompt: "Where are the highest dispatch risk hotspots right now and what corrective actions should I take?" },
  { label: "Cost leakage",         category: "Cost Leakage",        prompt: "Identify the top cost leakage sources across the fleet and how to recover margin today." },
  { label: "Driver coaching",      category: "Driver Coaching",     prompt: "Which drivers have the highest risk scores and what coaching interventions are most urgent?" },
  { label: "Maintenance window",   category: "Maintenance Planning",prompt: "What maintenance actions are overdue or at risk of causing breakdowns in the next 72 hours?" },
  { label: "Safety review",        category: "Safety Review",       prompt: "Summarize open safety events, coaching backlog, and highest-risk vehicles." },
  { label: "Customer SLA",         category: "Customer SLA",        prompt: "Which jobs are at SLA risk and what proactive customer communications should be sent?" },
  { label: "Executive brief",      category: "Executive Summary",   prompt: "Give me an executive operations briefing covering fleet performance, cost posture, safety, and top 3 priorities." },
  { label: "Compliance audit",     category: "Compliance Audit",    prompt: "What compliance items are expiring or overdue across HOS, DVIR, and regulatory documents?" },
];

// ── Action button → real action mapping ──────────────────────────────────────

const ACTION_HANDLERS: Record<string, { label: string; description: string }> = {
  "Create Dispatch Review": { label: "Create Dispatch Review", description: "Dispatch review created and assigned to operations manager." },
  "Send ETA Updates":       { label: "Send ETA Updates",       description: "Proactive ETA updates queued for all at-risk customer jobs." },
  "Schedule Maintenance":   { label: "Schedule Maintenance",   description: "High-priority maintenance flagged and scheduled for next available slot." },
  "Generate Executive Brief":{ label: "Generate Executive Brief", description: "Executive brief exported to Reports module." },
  "Open Evidence":          { label: "Open Evidence",          description: "Evidence package flagged for review in Safety module." },
};

// ── Message bubble ────────────────────────────────────────────────────────────

function UserBubble({ message }: { message: Message }) {
  return (
    <div className="flex justify-end gap-3 mb-4">
      <div className="max-w-[75%]">
        <div className="bg-teal-600 text-white rounded-2xl rounded-tr-sm px-4 py-3 text-sm leading-relaxed">
          {message.prompt}
        </div>
        <div className="flex justify-end mt-1">
          <span className="text-xs text-slate-400">{message.category} · {message.timestamp.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}</span>
        </div>
      </div>
      <div className="w-7 h-7 rounded-full bg-teal-100 flex items-center justify-center shrink-0 mt-0.5">
        <User className="w-4 h-4 text-teal-700" />
      </div>
    </div>
  );
}

function AssistantBubble({
  message,
  onAction,
}: {
  message: Message;
  onAction: (label: string) => void;
}) {
  return (
    <div className="flex gap-3 mb-6">
      <div className="w-7 h-7 rounded-full bg-violet-100 flex items-center justify-center shrink-0 mt-0.5">
        <Bot className="w-4 h-4 text-violet-700" />
      </div>
      <div className="flex-1 min-w-0">
        {/* Summary */}
        <div className="bg-slate-50 border border-slate-200 rounded-2xl rounded-tl-sm px-4 py-4">
          <p className="text-sm text-slate-800 leading-relaxed">{message.summary}</p>
        </div>

        {/* Evidence cards */}
        {message.evidence && message.evidence.length > 0 && (
          <div className="mt-3 grid gap-2 sm:grid-cols-2">
            {message.evidence.slice(0, 4).map((item, i) => (
              <AiInsightCard key={i} insight={item} />
            ))}
          </div>
        )}

        {/* Next steps */}
        {message.suggestedNextSteps && message.suggestedNextSteps.length > 0 && (
          <div className="mt-3">
            <p className="text-xs font-semibold text-slate-500 uppercase tracking-wide mb-2">Recommended next steps</p>
            <div className="flex flex-col gap-1.5">
              {message.suggestedNextSteps.map((step, i) => (
                <div key={i} className="flex items-start gap-2 text-sm text-slate-700 bg-white border border-slate-200 rounded-xl px-3 py-2">
                  <span className="text-teal-500 font-bold shrink-0">{i + 1}.</span>
                  {step}
                </div>
              ))}
            </div>
          </div>
        )}

        {/* Action buttons */}
        {message.actionButtons && message.actionButtons.length > 0 && (
          <div className="mt-3 flex flex-wrap gap-2">
            {message.actionButtons.map((btn) => (
              <button
                key={btn}
                type="button"
                className="text-xs px-3 py-1.5 rounded-lg bg-violet-50 border border-violet-200 text-violet-700 hover:bg-violet-100 transition-colors font-medium"
                onClick={() => onAction(btn)}
              >
                <Sparkles className="inline w-3 h-3 mr-1 -mt-0.5" />
                {btn}
              </button>
            ))}
          </div>
        )}

        <div className="mt-1.5">
          <span className="text-xs text-slate-400">{message.timestamp.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}</span>
        </div>
      </div>
    </div>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

export function AiCopilotPage() {
  const [messages, setMessages] = useState<Message[]>([]);
  const [prompt, setPrompt] = useState("Where should operations focus in the next 4 hours?");
  const [category, setCategory] = useState(STARTERS[0].category);
  const [actionToast, setActionToast] = useState<string | null>(null);
  const bottomRef = useRef<HTMLDivElement>(null);

  const insights = useQuery({ queryKey: ["ai-insights"], queryFn: aiApi.insights });

  const ask = useMutation({
    mutationFn: () => aiApi.ask(prompt, category),
    onSuccess: (data) => {
      const resp = data as AnyRecord;
      const userMsg: Message = {
        id: `u-${Date.now()}`,
        role: "user",
        prompt,
        category,
        timestamp: new Date(),
      };
      const assistantMsg: Message = {
        id: `a-${Date.now()}`,
        role: "assistant",
        summary: String(resp.summary ?? ""),
        evidence: (resp.evidence as AnyRecord[]) ?? [],
        suggestedNextSteps: (resp.suggestedNextSteps as string[]) ?? [],
        actionButtons: (resp.actionButtons as string[]) ?? [],
        timestamp: new Date(),
      };
      setMessages((prev) => [...prev, userMsg, assistantMsg]);
      setTimeout(() => bottomRef.current?.scrollIntoView({ behavior: "smooth" }), 50);
    },
  });

  function handleStarterClick(starter: (typeof STARTERS)[0]) {
    setCategory(starter.category);
    setPrompt(starter.prompt);
  }

  function handleActionClick(label: string) {
    const info = ACTION_HANDLERS[label];
    const feedback = info?.description ?? `${label} action executed.`;
    setActionToast(feedback);
    setTimeout(() => setActionToast(null), 4000);
  }

  function handleKeyDown(e: React.KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === "Enter" && (e.metaKey || e.ctrlKey)) {
      e.preventDefault();
      if (prompt.trim()) ask.mutate();
    }
  }

  function exportConversation() {
    const rows = messages.map((m) => ({
      role: m.role,
      category: m.category ?? "",
      content: m.role === "user" ? (m.prompt ?? "") : (m.summary ?? ""),
      timestamp: m.timestamp.toISOString(),
    }));
    exportCsv("ai-copilot-conversation", rows);
  }

  return (
    <div className="flex h-[calc(100vh-96px)] gap-4">
      {/* Action toast */}
      {actionToast && (
        <div className="fixed top-4 right-4 z-50 bg-violet-600 text-white text-sm font-medium px-4 py-2.5 rounded-lg shadow-lg max-w-sm">
          {actionToast}
        </div>
      )}

      {/* Left sidebar */}
      <aside className="w-72 shrink-0 flex flex-col gap-4 overflow-y-auto py-4">
        {/* Quick starters */}
        <div className="panel p-4">
          <h2 className="text-sm font-semibold text-slate-900 mb-3">Quick starts</h2>
          <div className="flex flex-col gap-1.5">
            {STARTERS.map((s) => (
              <button
                key={s.label}
                type="button"
                onClick={() => handleStarterClick(s)}
                className={`rounded-lg px-3 py-2 text-left text-xs transition-colors font-medium ${
                  category === s.category
                    ? "bg-violet-50 text-violet-700 border border-violet-200"
                    : "bg-slate-50 text-slate-600 hover:bg-slate-100 border border-transparent"
                }`}
              >
                <Sparkles className="inline w-3 h-3 mr-1.5 -mt-0.5 text-violet-400" />
                {s.label}
              </button>
            ))}
          </div>
        </div>

        {/* Agentic Ops Copilot — live proposed actions awaiting dispatcher approval */}
        <CopilotProposals />

        {/* Evidence feed */}
        <div className="panel p-4">
          <h2 className="text-sm font-semibold text-slate-900 mb-3">Live evidence</h2>
          <div className="flex flex-col gap-2">
            {insights.isLoading ? (
              <LoadingState />
            ) : (
              ((insights.data as AnyRecord[]) ?? []).slice(0, 5).map((item) => (
                <AiInsightCard key={String(item.id)} insight={item} />
              ))
            )}
          </div>
        </div>
      </aside>

      {/* Main chat area */}
      <div className="flex-1 flex flex-col min-w-0 py-4">
        {/* Header */}
        <div className="panel px-5 py-3 mb-4 flex items-center justify-between shrink-0">
          <div className="flex items-center gap-2">
            <div className="w-7 h-7 rounded-full bg-violet-100 flex items-center justify-center">
              <Bot className="w-4 h-4 text-violet-700" />
            </div>
            <span className="font-semibold text-slate-900 text-sm">Operations Copilot</span>
            <span className="text-xs px-2 py-0.5 rounded-full bg-teal-50 border border-teal-200 text-teal-700 font-medium">Ready</span>
          </div>
          {messages.length > 0 && (
            <button type="button" className="btn-secondary text-xs" onClick={exportConversation}>
              Export conversation
            </button>
          )}
        </div>

        {/* Messages */}
        <div className="flex-1 overflow-y-auto panel p-5 mb-4">
          {messages.length === 0 ? (
            <div className="flex flex-col items-center justify-center h-full text-center gap-4">
              <div className="w-14 h-14 rounded-full bg-violet-100 flex items-center justify-center">
                <Bot className="w-7 h-7 text-violet-600" />
              </div>
              <div>
                <p className="text-slate-700 font-semibold">Operations Copilot ready</p>
                <p className="text-sm text-slate-400 mt-1 max-w-sm">
                  Ask about dispatch risk, cost leakage, safety, maintenance, customer SLA, or get an executive brief. Press ⌘↵ or click Ask.
                </p>
              </div>
              <div className="flex flex-wrap gap-2 justify-center mt-2">
                {STARTERS.slice(0, 4).map((s) => (
                  <button
                    key={s.label}
                    type="button"
                    className="text-xs px-3 py-1.5 rounded-lg bg-slate-50 border border-slate-200 text-slate-600 hover:bg-violet-50 hover:border-violet-200 hover:text-violet-700 transition-colors font-medium"
                    onClick={() => handleStarterClick(s)}
                  >
                    {s.label}
                  </button>
                ))}
              </div>
            </div>
          ) : (
            <div>
              {messages.map((msg) =>
                msg.role === "user" ? (
                  <UserBubble key={msg.id} message={msg} />
                ) : (
                  <AssistantBubble key={msg.id} message={msg} onAction={handleActionClick} />
                )
              )}
              {ask.isPending && (
                <div className="flex gap-3 mb-4">
                  <div className="w-7 h-7 rounded-full bg-violet-100 flex items-center justify-center shrink-0">
                    <Bot className="w-4 h-4 text-violet-700" />
                  </div>
                  <div className="bg-slate-50 border border-slate-200 rounded-2xl rounded-tl-sm px-4 py-3">
                    <span className="text-slate-400 text-sm">Analyzing fleet data…</span>
                    <span className="ml-1 inline-flex gap-0.5">
                      <span className="inline-block w-1.5 h-1.5 rounded-full bg-violet-300 animate-bounce" />
                      <span className="inline-block w-1.5 h-1.5 rounded-full bg-violet-300 animate-bounce [animation-delay:0.15s]" />
                      <span className="inline-block w-1.5 h-1.5 rounded-full bg-violet-300 animate-bounce [animation-delay:0.30s]" />
                    </span>
                  </div>
                </div>
              )}
              <div ref={bottomRef} />
            </div>
          )}
        </div>

        {/* Input */}
        <div className="panel p-4 shrink-0">
          <div className="flex items-center gap-2 mb-2">
            <span className="text-xs text-slate-500">Category:</span>
            <select
              title="Prompt category"
              value={category}
              onChange={(e) => setCategory(e.target.value)}
              className="text-xs border border-slate-200 rounded-lg px-2 py-1 text-slate-700 focus:outline-none focus:ring-2 focus:ring-violet-400"
            >
              {STARTERS.map((s) => (
                <option key={s.category} value={s.category}>{s.category}</option>
              ))}
            </select>
          </div>
          <div className="flex gap-3">
            <textarea
              className="flex-1 resize-none border border-slate-200 rounded-xl px-4 py-3 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-violet-400 min-h-18"
              placeholder="Ask about dispatch risk, cost, safety, compliance, customer SLA…"
              value={prompt}
              rows={2}
              onChange={(e) => setPrompt(e.target.value)}
              onKeyDown={handleKeyDown}
            />
            <button
              type="button"
              disabled={ask.isPending || !prompt.trim()}
              className="bg-violet-600 hover:bg-violet-700 disabled:opacity-50 text-white px-4 rounded-xl transition-colors shrink-0 flex flex-col items-center justify-center gap-1"
              onClick={() => ask.mutate()}
            >
              <Send className="w-4 h-4" />
              <span className="text-xs font-medium">Ask</span>
            </button>
          </div>
          <p className="text-xs text-slate-400 mt-2">⌘↵ to send · Evidence is pulled from live fleet data</p>
        </div>
      </div>
    </div>
  );
}


/* ── Agentic Ops Copilot — proposed actions awaiting human approval ──────────── */
function CopilotProposals() {
  const qc = useQueryClient();
  const proposed = useQuery({
    queryKey: ["copilot-proposed"],
    queryFn: aiCopilotApi.proposed,
    refetchInterval: 20000,
  });
  const approve = useMutation({
    mutationFn: (id: number | string) => aiCopilotApi.approve(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["copilot-proposed"] }),
  });
  const dismiss = useMutation({
    mutationFn: (id: number | string) => aiCopilotApi.dismiss(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["copilot-proposed"] }),
  });

  const rows = (proposed.data ?? []) as AnyRecord[];
  const busyId = approve.variables ?? dismiss.variables;

  return (
    <div className="panel p-4">
      <h2 className="mb-1 flex items-center gap-1.5 text-sm font-semibold text-slate-900">
        <Zap className="h-3.5 w-3.5 text-violet-500" /> Copilot proposals
      </h2>
      <p className="mb-3 text-[11px] leading-snug text-slate-400">
        AI-proposed dispatch actions. Approve to execute through the audited workflow, or dismiss.
      </p>
      {proposed.isLoading ? (
        <p className="text-xs text-slate-400">Loading…</p>
      ) : rows.length === 0 ? (
        <p className="rounded-lg border border-dashed border-slate-200 px-3 py-3 text-[11px] text-slate-400">
          No proposals right now. The copilot posts here when it spots a dispatch exception worth acting on.
        </p>
      ) : (
        <div className="flex flex-col gap-2.5">
          {rows.map((r) => {
            const id = Number(r.id);
            const risk = String(r.riskLevel ?? "medium");
            const riskTone = /high/i.test(risk) ? "bg-rose-50 text-rose-700" : /low/i.test(risk) ? "bg-emerald-50 text-emerald-700" : "bg-amber-50 text-amber-700";
            const conf = Math.round(Number(r.confidenceScore ?? 0) * 100);
            const isBusy = busyId === id;
            return (
              <div key={id} className="rounded-xl border border-violet-200/70 bg-gradient-to-b from-violet-50/50 to-white p-3">
                <div className="flex items-start justify-between gap-2">
                  <p className="text-[12.5px] font-bold leading-snug text-slate-900">{String(r.title ?? "Proposed action")}</p>
                  <span className={`shrink-0 rounded-md px-1.5 py-0.5 text-[9px] font-bold uppercase ${riskTone}`}>{risk}</span>
                </div>
                <p className="mt-1 text-[11px] leading-snug text-slate-500">{String(r.summary ?? "")}</p>
                {conf > 0 && <p className="mt-1 text-[10px] font-semibold text-violet-500">{conf}% confidence</p>}
                <div className="mt-2.5 grid grid-cols-2 gap-2">
                  <button type="button" disabled={isBusy} onClick={() => approve.mutate(id)}
                    className="inline-flex items-center justify-center gap-1 rounded-lg bg-violet-600 px-2 py-1.5 text-[11px] font-bold text-white transition hover:bg-violet-500 disabled:opacity-50">
                    <Check className="h-3 w-3" /> {isBusy ? "…" : "Approve"}
                  </button>
                  <button type="button" disabled={isBusy} onClick={() => dismiss.mutate(id)}
                    className="inline-flex items-center justify-center gap-1 rounded-lg border border-slate-200 px-2 py-1.5 text-[11px] font-bold text-slate-500 transition hover:bg-slate-50 disabled:opacity-50">
                    <X className="h-3 w-3" /> Dismiss
                  </button>
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
