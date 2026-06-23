import { useState } from "react";
import { MessageSquare, Plus, RefreshCw, Send, X } from "lucide-react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { messagesApi } from "@/services/messagesApi";

type AnyRecord = Record<string, unknown>;

function formatTime(val: unknown): string {
  if (!val) return "";
  try { return new Date(String(val)).toLocaleString(); } catch { return String(val); }
}

function ConversationStatusBadge({ status }: { status: string }) {
  const cls = status === "open"
    ? "border-emerald-400/30 bg-emerald-50 text-emerald-700"
    : "border-slate-300/30 bg-slate-50 text-slate-500";
  return (
    <span className={`inline-flex items-center rounded-full border px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide ${cls}`}>
      {status}
    </span>
  );
}

export function MessageCenterPage() {
  const qc = useQueryClient();
  const [activeConvId, setActiveConvId] = useState<number | null>(null);
  const [newMsg, setNewMsg]             = useState("");
  const [showCreate, setShowCreate]     = useState(false);
  const [createSubject, setCreateSubject] = useState("");
  const [createDriverId, setCreateDriverId] = useState("");

  const { data: convs = [], isLoading, refetch } = useQuery({
    queryKey: ["conversations"],
    queryFn:  messagesApi.listConversations,
    refetchInterval: 15_000,
  });

  const { data: detail } = useQuery({
    queryKey: ["conversation", activeConvId],
    queryFn:  () => activeConvId ? messagesApi.getConversation(activeConvId) : null,
    enabled:  !!activeConvId,
    refetchInterval: 10_000,
  });

  const createConv = useMutation({
    mutationFn: () => messagesApi.createConversation({
      subject: createSubject || undefined,
      driverId: createDriverId ? Number(createDriverId) : undefined,
    }),
    onSuccess: (data) => {
      qc.invalidateQueries({ queryKey: ["conversations"] });
      setShowCreate(false);
      setCreateSubject("");
      setCreateDriverId("");
      if (data && typeof data === "object" && "id" in data) {
        setActiveConvId(Number((data as AnyRecord).id));
      }
    },
  });

  const sendMsg = useMutation({
    mutationFn: () => messagesApi.sendMessage(activeConvId!, newMsg),
    onSuccess:  () => {
      qc.invalidateQueries({ queryKey: ["conversation", activeConvId] });
      setNewMsg("");
    },
  });

  const markRead = useMutation({
    mutationFn: (id: number) => messagesApi.markRead(id),
    onSuccess:  () => qc.invalidateQueries({ queryKey: ["conversations"] }),
  });

  const convList = convs as AnyRecord[];
  const activeConv = detail as AnyRecord | null;

  return (
    <div className="flex h-[calc(100vh-8rem)] rounded-xl border border-slate-200 bg-white shadow-sm overflow-hidden">
      {/* Sidebar */}
      <aside className="w-72 border-r border-slate-100 flex flex-col">
        <div className="flex items-center justify-between p-4 border-b border-slate-100">
          <h2 className="text-sm font-extrabold text-slate-900 flex items-center gap-1.5">
            <MessageSquare className="h-4 w-4 text-indigo-500" />
            Conversations
          </h2>
          <div className="flex gap-1">
            <button onClick={() => refetch()} className="rounded p-1 hover:bg-slate-50">
              <RefreshCw className="h-3.5 w-3.5 text-slate-400" />
            </button>
            <button onClick={() => setShowCreate(true)} className="rounded p-1 hover:bg-slate-50">
              <Plus className="h-3.5 w-3.5 text-indigo-500" />
            </button>
          </div>
        </div>

        <div className="flex-1 overflow-y-auto">
          {isLoading ? (
            <div className="p-4 text-xs text-slate-400">Loading...</div>
          ) : convList.length === 0 ? (
            <div className="p-4 text-xs text-slate-400">No conversations yet</div>
          ) : (
            convList.map((c) => (
              <button
                key={String(c.id)}
                onClick={() => {
                  setActiveConvId(Number(c.id));
                  markRead.mutate(Number(c.id));
                }}
                className={`w-full text-left p-3 border-b border-slate-50 hover:bg-slate-50 ${activeConvId === Number(c.id) ? "bg-indigo-50" : ""}`}
              >
                <div className="flex items-center justify-between mb-0.5">
                  <span className="text-xs font-semibold text-slate-800 truncate max-w-[160px]">
                    {String(c.subject ?? "Conversation")}
                  </span>
                  <ConversationStatusBadge status={String(c.status ?? "open")} />
                </div>
                <div className="text-[10px] text-slate-400">
                  {String(c.messageCount ?? 0)} messages • {formatTime(c.lastMessageAt || c.updatedAt)}
                </div>
              </button>
            ))
          )}
        </div>
      </aside>

      {/* Message area */}
      <div className="flex-1 flex flex-col">
        {!activeConvId ? (
          <div className="flex-1 flex items-center justify-center text-sm text-slate-400">
            Select a conversation or create a new one
          </div>
        ) : (
          <>
            {/* Conv header */}
            <div className="p-4 border-b border-slate-100 flex items-center justify-between">
              <div>
                <h3 className="text-sm font-bold text-slate-900">
                  {String((activeConv?.conversation as AnyRecord)?.subject ?? "Conversation")}
                </h3>
                <p className="text-xs text-slate-400">
                  {String((activeConv?.conversation as AnyRecord)?.messageCount ?? 0)} messages
                </p>
              </div>
              <button onClick={() => setActiveConvId(null)} className="text-slate-300 hover:text-slate-500">
                <X className="h-4 w-4" />
              </button>
            </div>

            {/* Messages */}
            <div className="flex-1 overflow-y-auto p-4 space-y-3">
              {((activeConv?.messages ?? []) as AnyRecord[]).map((m) => (
                <div key={String(m.id)} className="flex gap-2">
                  <div className="flex-shrink-0 w-7 h-7 rounded-full bg-indigo-100 flex items-center justify-center text-indigo-700 text-[10px] font-bold">
                    {String(m.senderRole ?? "U")[0].toUpperCase()}
                  </div>
                  <div>
                    <div className="flex items-center gap-1.5 mb-0.5">
                      <span className="text-[10px] font-semibold text-slate-700">{String(m.senderName ?? m.senderRole ?? "User")}</span>
                      <span className="text-[10px] text-slate-400">{formatTime(m.sentAt)}</span>
                    </div>
                    <div className="rounded-lg bg-slate-50 px-3 py-2 text-xs text-slate-700">
                      {String(m.body ?? "")}
                    </div>
                  </div>
                </div>
              ))}
            </div>

            {/* Compose */}
            <div className="p-3 border-t border-slate-100 flex gap-2">
              <input
                value={newMsg}
                onChange={(e) => setNewMsg(e.target.value)}
                onKeyDown={(e) => { if (e.key === "Enter" && !e.shiftKey && newMsg.trim()) { e.preventDefault(); sendMsg.mutate(); } }}
                placeholder="Type a message..."
                className="flex-1 rounded-lg border border-slate-200 px-3 py-2 text-sm"
              />
              <button
                onClick={() => sendMsg.mutate()}
                disabled={!newMsg.trim() || sendMsg.isPending}
                className="btn-primary flex items-center gap-1.5"
              >
                <Send className="h-3.5 w-3.5" />
                Send
              </button>
            </div>
          </>
        )}
      </div>

      {/* Create conversation modal */}
      {showCreate && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/30">
          <div className="w-full max-w-sm rounded-2xl bg-white shadow-2xl p-6 space-y-4">
            <div className="flex items-center justify-between">
              <h3 className="font-extrabold text-slate-900">New Conversation</h3>
              <button onClick={() => setShowCreate(false)} className="text-slate-400 hover:text-slate-600">
                <X className="h-4 w-4" />
              </button>
            </div>
            <div className="space-y-3">
              <div>
                <label className="block text-xs font-semibold text-slate-600 mb-1">Subject</label>
                <input
                  value={createSubject}
                  onChange={(e) => setCreateSubject(e.target.value)}
                  placeholder="e.g., Assignment #42 question"
                  className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm"
                />
              </div>
              <div>
                <label className="block text-xs font-semibold text-slate-600 mb-1">Driver ID (optional)</label>
                <input
                  value={createDriverId}
                  onChange={(e) => setCreateDriverId(e.target.value)}
                  placeholder="e.g., 3"
                  type="number"
                  className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm"
                />
              </div>
            </div>
            <button
              onClick={() => createConv.mutate()}
              disabled={createConv.isPending}
              className="btn-primary w-full"
            >
              Create Conversation
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
