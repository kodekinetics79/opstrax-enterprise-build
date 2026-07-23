import { useEffect, useMemo, useRef, useState } from "react";
import { useLocation } from "react-router-dom";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { ChevronLeft, MessageSquare, Send } from "lucide-react";
import { messagesApi } from "@/services/messagesApi";
import { ErrorState } from "@/components/ui";
import type { AnyRecord } from "@/types";

// Driver-side conversation UI — a single mobile list→thread stack over the SAME messaging_conversations
// the dispatcher's MessageCenterPage uses. Read/reply only; the driver never targets a driver (driver_id
// is implicit from the session server-side). Quick-reply chips give Motive/Samsara-parity canned replies.

// Load-relevant canned replies (client-side; future: server-managed + status side-effects via P4 vocab).
const QUICK_REPLIES = ["At pickup", "Loaded", "Running late", "Need lumper", "Delivered"];

function formatTime(val: unknown): string {
  if (!val) return "";
  try {
    const d = new Date(String(val));
    const diff = Date.now() - d.getTime();
    if (diff < 60_000) return "just now";
    if (diff < 3_600_000) return `${Math.floor(diff / 60_000)}m ago`;
    if (diff < 86_400_000) return `${Math.floor(diff / 3_600_000)}h ago`;
    return d.toLocaleDateString();
  } catch { return String(val); }
}

export function DriverMessagesPage() {
  const qc = useQueryClient();
  const location = useLocation();
  // DriverAssignmentPage can deep-link a specific thread via navigate("/driver/messages", { state: { convId } }).
  const initialConv = (location.state as { convId?: number | string } | null)?.convId ?? null;
  const [activeConvId, setActiveConvId] = useState<number | string | null>(initialConv);

  const { data: list = [], isLoading, isError, error } = useQuery({
    queryKey: ["messages", "conversations"],
    queryFn: messagesApi.listConversations,
    refetchInterval: 15_000,
  });

  const conversations = list as AnyRecord[];

  if (isError) return <ErrorState message={(error as Error)?.message} />;

  if (activeConvId != null) {
    return (
      <ConversationThread
        convId={activeConvId}
        onBack={() => {
          setActiveConvId(null);
          void qc.invalidateQueries({ queryKey: ["messages", "conversations"] });
          void qc.invalidateQueries({ queryKey: ["messages", "unread-count"] });
        }}
      />
    );
  }

  return (
    <div className="min-h-screen bg-slate-50 pb-24">
      <div className="sticky top-0 z-10 border-b border-slate-200 bg-white px-4 py-3">
        <div className="flex items-center gap-2">
          <MessageSquare className="h-5 w-5 text-teal-600" />
          <h1 className="text-base font-extrabold text-slate-900">Messages</h1>
        </div>
      </div>

      <div className="space-y-2 px-4 py-3">
        {isLoading ? (
          <div className="py-8 text-center text-sm text-slate-400">Loading…</div>
        ) : conversations.length === 0 ? (
          <div className="py-12 text-center text-sm text-slate-400">
            <MessageSquare className="mx-auto mb-2 h-10 w-10 text-slate-300" />
            No messages yet — your dispatcher will reach out here.
          </div>
        ) : (
          conversations.map((c) => {
            const unread = Number(c.unreadCount ?? 0) > 0;
            const loadRef = c.dispatchAssignmentId ?? c.tripId;
            return (
              <button
                key={String(c.id)}
                type="button"
                onClick={() => setActiveConvId(c.id as number | string)}
                className={`flex w-full items-center gap-3 rounded-2xl border bg-white p-4 text-left active:bg-slate-50 ${
                  unread ? "border-teal-200" : "border-slate-200"
                }`}
              >
                {unread && <span className="h-2.5 w-2.5 shrink-0 rounded-full bg-teal-500" />}
                <div className="min-w-0 flex-1">
                  <div className="flex items-center justify-between gap-2">
                    <p className={`truncate text-sm ${unread ? "font-bold text-slate-900" : "font-semibold text-slate-700"}`}>
                      {String(c.subject ?? "Message")}
                    </p>
                    <span className="shrink-0 text-[11px] text-slate-400">{formatTime(c.lastMessageAt ?? c.updatedAt)}</span>
                  </div>
                  {loadRef != null && (
                    <p className="mt-0.5 text-[11px] text-slate-400">
                      {c.dispatchAssignmentId != null ? `Load #${String(c.dispatchAssignmentId)}` : `Trip #${String(c.tripId)}`}
                    </p>
                  )}
                </div>
              </button>
            );
          })
        )}
      </div>
    </div>
  );
}

function ConversationThread({ convId, onBack }: { convId: number | string; onBack: () => void }) {
  const qc = useQueryClient();
  const [draft, setDraft] = useState("");
  const bottomRef = useRef<HTMLDivElement | null>(null);
  const markedRef = useRef(false);

  const { data, isLoading } = useQuery({
    queryKey: ["messages", "conversation", convId],
    queryFn: () => messagesApi.getConversation(convId),
    refetchInterval: 10_000,
  });

  const conversation = (data?.conversation as AnyRecord) ?? {};
  const messages = useMemo(() => (data?.messages as AnyRecord[]) ?? [], [data]);

  // Mark the thread read once on open, then refresh the list + badge so the unread dot clears.
  useEffect(() => {
    if (markedRef.current || isLoading) return;
    markedRef.current = true;
    void messagesApi.markRead(convId).then(() => {
      void qc.invalidateQueries({ queryKey: ["messages", "conversations"] });
      void qc.invalidateQueries({ queryKey: ["messages", "unread-count"] });
    });
  }, [convId, isLoading, qc]);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ block: "end" });
  }, [messages.length]);

  const send = useMutation({
    mutationFn: (body: string) => messagesApi.sendMessage(convId, body),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ["messages", "conversation", convId] });
      void qc.invalidateQueries({ queryKey: ["messages", "conversations"] });
    },
  });

  const submit = (text: string) => {
    const body = text.trim();
    if (!body || send.isPending) return;
    setDraft("");
    send.mutate(body);
  };

  const loadRef = conversation.dispatchAssignmentId ?? conversation.tripId;
  // The driver's own last sent message (senderRole 'Driver'), for the read receipt.
  const myLastSentId = [...messages].reverse().find((m) => String(m.senderRole) === "Driver")?.id;

  return (
    <div className="flex min-h-screen flex-col bg-slate-50">
      {/* Thread header with load context */}
      <div className="sticky top-0 z-10 border-b border-slate-200 bg-white px-2 py-2.5">
        <div className="flex items-center gap-1">
          <button type="button" onClick={onBack} className="rounded-full p-1.5 text-slate-500 active:bg-slate-100">
            <ChevronLeft className="h-5 w-5" />
          </button>
          <div className="min-w-0">
            <p className="truncate text-sm font-bold text-slate-900">Re: {String(conversation.subject ?? "Message")}</p>
            {loadRef != null && (
              <p className="text-[11px] text-slate-400">
                {conversation.dispatchAssignmentId != null ? `Load #${String(conversation.dispatchAssignmentId)}` : `Trip #${String(conversation.tripId)}`}
              </p>
            )}
          </div>
        </div>
      </div>

      {/* Messages */}
      <div className="flex-1 space-y-2 overflow-y-auto px-4 py-4 pb-40">
        {isLoading ? (
          <div className="py-8 text-center text-sm text-slate-400">Loading…</div>
        ) : messages.length === 0 ? (
          <div className="py-8 text-center text-sm text-slate-400">No messages yet — say hello.</div>
        ) : (
          messages.map((m) => {
            const mine = String(m.senderRole) === "Driver";
            return (
              <div key={String(m.id)} className={`flex flex-col ${mine ? "items-end" : "items-start"}`}>
                <div
                  className={`max-w-[80%] rounded-2xl px-3.5 py-2 text-sm ${
                    mine ? "rounded-br-sm bg-teal-500 text-white" : "rounded-bl-sm bg-white text-slate-800 border border-slate-200"
                  }`}
                >
                  {!mine && <p className="mb-0.5 text-[11px] font-semibold text-slate-500">{String(m.senderName ?? "Dispatch")}</p>}
                  <p className="whitespace-pre-wrap break-words">{String(m.body ?? "")}</p>
                </div>
                <div className="mt-0.5 flex items-center gap-1 px-1 text-[10px] text-slate-400">
                  <span>{formatTime(m.sentAt)}</span>
                  {mine && m.id === myLastSentId && m.readAt != null && <span className="text-teal-600">· Read</span>}
                </div>
              </div>
            );
          })
        )}
        <div ref={bottomRef} />
      </div>

      {/* Composer — sits above the fixed bottom nav (nav is ~64px; pb-24 clearance on the scroll area). */}
      <div className="fixed bottom-16 left-0 right-0 z-20 border-t border-slate-200 bg-white px-3 py-2">
        <div className="mb-2 flex gap-2 overflow-x-auto pb-1">
          {QUICK_REPLIES.map((q) => (
            <button
              key={q}
              type="button"
              onClick={() => submit(q)}
              disabled={send.isPending}
              className="shrink-0 rounded-full border border-slate-200 bg-slate-50 px-3 py-1.5 text-xs font-medium text-slate-600 active:bg-slate-100 disabled:opacity-50"
            >
              {q}
            </button>
          ))}
        </div>
        <div className="flex items-end gap-2">
          <textarea
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            rows={1}
            placeholder="Message dispatch…"
            className="max-h-28 flex-1 resize-none rounded-2xl border border-slate-200 px-4 py-2.5 text-sm focus:border-teal-400 focus:outline-none"
          />
          <button
            type="button"
            onClick={() => submit(draft)}
            disabled={!draft.trim() || send.isPending}
            className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-teal-500 text-white active:bg-teal-600 disabled:opacity-40"
          >
            <Send className="h-5 w-5" />
          </button>
        </div>
      </div>
    </div>
  );
}
