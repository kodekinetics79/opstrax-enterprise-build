import { useCallback, useEffect, useMemo, useState } from "react";
import { Alert, Text, View } from "react-native";
import {
  ActionButton,
  EmptyState,
  ErrorState,
  Field,
  Input,
  LoadingState,
  Panel,
  Row,
  Screen,
  SectionHeader,
  colors,
} from "@/components/ui";
import { DriverSceneHero, DriverStatusStrip, TelemetryRail } from "@/components/DriverExperience";
import { useSession } from "@/auth/SessionProvider";
import { asRecords, textOf } from "@/data/records";
import type { JsonRecord } from "@/types";

const QUICK_REPLIES = ["At pickup", "Loaded", "Running late", "Need assistance", "Delivered"];

function relativeTime(value: unknown) {
  if (!value) return "";
  const date = new Date(String(value));
  if (Number.isNaN(date.getTime())) return String(value);
  const delta = Date.now() - date.getTime();
  if (delta < 60_000) return "just now";
  if (delta < 3_600_000) return `${Math.floor(delta / 60_000)}m ago`;
  if (delta < 86_400_000) return `${Math.floor(delta / 3_600_000)}h ago`;
  return date.toLocaleDateString();
}

function recordId(value: JsonRecord | undefined | null) {
  const id = value?.id ?? value?.conversationId ?? value?.conversation_id;
  return id === null || id === undefined ? "" : String(id);
}

export function DriverDispatchScreen() {
  const { api, session, refresh, logout } = useSession();
  const [conversations, setConversations] = useState<JsonRecord[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [activeId, setActiveId] = useState<string | null>(null);
  const [thread, setThread] = useState<JsonRecord | null>(null);
  const [threadLoading, setThreadLoading] = useState(false);
  const [draft, setDraft] = useState("");
  const [sending, setSending] = useState(false);

  const loadConversations = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const rows = await api.request.get<JsonRecord[]>("/api/messages/conversations");
      setConversations(asRecords(rows));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Dispatch conversations are unavailable.");
    } finally {
      setLoading(false);
    }
  }, [api]);

  useEffect(() => {
    const initial = setTimeout(() => void loadConversations(), 0);
    const timer = setInterval(() => void loadConversations(), 15_000);
    return () => { clearTimeout(initial); clearInterval(timer); };
  }, [loadConversations]);

  useEffect(() => {
    if (!activeId) return;
    let alive = true;
    const load = async () => {
      setThreadLoading(true);
      try {
        const result = await api.request.get<JsonRecord>(`/api/messages/conversations/${encodeURIComponent(activeId)}`);
        if (!alive) return;
        setThread(result);
        await api.request.post(`/api/messages/conversations/${encodeURIComponent(activeId)}/read`, {}).catch(() => undefined);
      } catch (err) {
        if (alive) Alert.alert("Conversation unavailable", err instanceof Error ? err.message : "Unable to load this conversation.");
      } finally {
        if (alive) setThreadLoading(false);
      }
    };
    const initial = setTimeout(() => void load(), 0);
    const timer = setInterval(() => void load(), 10_000);
    return () => { alive = false; clearTimeout(initial); clearInterval(timer); };
  }, [activeId, api]);

  const activeConversation = useMemo(
    () => conversations.find((item) => recordId(item) === activeId) ?? null,
    [activeId, conversations],
  );
  const messages = asRecords(thread?.messages);
  const unreadCount = conversations.filter((item) => Number(item.unreadCount ?? item.unread_count ?? 0) > 0).length;

  const send = async (value: string) => {
    const body = value.trim();
    if (!activeId || !body || sending) return;
    setSending(true);
    try {
      await api.request.post(`/api/messages/conversations/${encodeURIComponent(activeId)}/messages`, { body });
      setDraft("");
      const result = await api.request.get<JsonRecord>(`/api/messages/conversations/${encodeURIComponent(activeId)}`);
      setThread(result);
      await loadConversations();
    } catch (err) {
      Alert.alert("Message not sent", err instanceof Error ? err.message : "Dispatch could not receive the message.");
    } finally {
      setSending(false);
    }
  };

  const signOut = () => {
    Alert.alert("Sign out of OpsTrax Driver?", "The secure local session will be cleared from this device.", [
      { text: "Cancel", style: "cancel" },
      { text: "Sign out", style: "destructive", onPress: () => void logout() },
    ]);
  };

  return (
    <Screen>
      <DriverSceneHero
        eyebrow="Dispatch link"
        title={activeId ? "Stay in the loop." : "Your line to operations."}
        description={activeId
          ? "Reply inside the same tenant-scoped conversation used by dispatch."
          : "Live load conversations and operational messages, scoped to your authenticated driver identity."}
        tone={unreadCount > 0 ? "amber" : "teal"}
        status={unreadCount > 0 ? `${unreadCount} unread` : "Connected"}
      />
      <TelemetryRail tone={unreadCount > 0 ? "amber" : "teal"} />

      {activeId ? (
        <>
          <Panel variant="elevated" tone="teal">
            <SectionHeader
              eyebrow="Conversation"
              title={textOf(thread?.conversation && (thread.conversation as JsonRecord).subject, textOf(activeConversation?.subject, "Dispatch"))}
              description={textOf(
                (thread?.conversation as JsonRecord | undefined)?.dispatchAssignmentId ?? activeConversation?.dispatchAssignmentId,
                "Driver-to-dispatch secure thread",
              )}
              right={<ActionButton label="Back" onPress={() => { setActiveId(null); setThread(null); }} variant="ghost" />}
            />
            {threadLoading && messages.length === 0 ? <LoadingState label="Loading conversation…" /> : null}
            {!threadLoading && messages.length === 0 ? <EmptyState title="No messages yet" body="Send a message to dispatch when you need operational support." /> : null}
            <View style={{ gap: 9 }}>
              {messages.map((message, index) => {
                const mine = String(message.senderRole ?? message.sender_role ?? "").toLowerCase() === "driver";
                return (
                  <View
                    key={String(message.id ?? index)}
                    style={{
                      alignSelf: mine ? "flex-end" : "flex-start",
                      maxWidth: "86%",
                      paddingHorizontal: 13,
                      paddingVertical: 10,
                      borderRadius: 18,
                      borderWidth: 1,
                      borderColor: mine ? "rgba(66,223,207,0.34)" : colors.border,
                      backgroundColor: mine ? "rgba(66,223,207,0.10)" : "rgba(255,255,255,0.035)",
                    }}
                  >
                    {!mine ? <Text style={{ color: colors.teal, fontSize: 10.5, fontWeight: "900", marginBottom: 4 }}>{textOf(message.senderName ?? message.sender_name, "Dispatch")}</Text> : null}
                    <Text style={{ color: colors.text, fontSize: 14, lineHeight: 20 }}>{textOf(message.body)}</Text>
                    <Text style={{ color: colors.subtle, fontSize: 10.5, marginTop: 5 }}>{relativeTime(message.sentAt ?? message.sent_at)}</Text>
                  </View>
                );
              })}
            </View>
          </Panel>

          <Panel variant="solid" tone="blue">
            <SectionHeader eyebrow="Reply" title="Message dispatch" description="Use a quick status or type a concise operational update." />
            <Row>
              {QUICK_REPLIES.slice(0, 3).map((item) => (
                <ActionButton key={item} label={item} onPress={() => void send(item)} variant="ghost" disabled={sending} />
              ))}
            </Row>
            <Row>
              {QUICK_REPLIES.slice(3).map((item) => (
                <ActionButton key={item} label={item} onPress={() => void send(item)} variant="ghost" disabled={sending} />
              ))}
            </Row>
            <Input label="Message" value={draft} onChangeText={setDraft} placeholder="Message dispatch…" multiline autoCapitalize="sentences" />
            <ActionButton label={sending ? "Sending…" : "Send securely"} onPress={() => void send(draft)} disabled={sending || !draft.trim()} />
          </Panel>
        </>
      ) : (
        <Panel variant="elevated" tone={unreadCount > 0 ? "amber" : "teal"}>
          <SectionHeader
            eyebrow="Live conversations"
            title="Dispatch inbox"
            description="Threads are refreshed automatically. The driver can only see conversations granted by the server session."
          />
          {loading ? <LoadingState label="Syncing dispatch…" /> : null}
          {error ? <ErrorState title="Dispatch unavailable" body={error} onRetry={() => void loadConversations()} /> : null}
          {!loading && !error && conversations.length === 0 ? <EmptyState title="No conversations" body="Your dispatcher can start a load-linked conversation when contact is required." /> : null}
          {!loading && !error ? (
            <View style={{ gap: 10 }}>
              {conversations.map((item, index) => {
                const id = recordId(item);
                const unread = Number(item.unreadCount ?? item.unread_count ?? 0) > 0;
                const loadRef = item.dispatchAssignmentId ?? item.dispatch_assignment_id ?? item.tripId ?? item.trip_id;
                return (
                  <ActionButton
                    key={id || String(index)}
                    label={`${unread ? "● " : ""}${textOf(item.subject, "Dispatch message")}${loadRef ? ` · Load ${String(loadRef)}` : ""}`}
                    onPress={() => { if (id) { setThread(null); setActiveId(id); } }}
                    variant={unread ? "secondary" : "ghost"}
                    disabled={!id}
                  />
                );
              })}
            </View>
          ) : null}
        </Panel>
      )}

      <DriverStatusStrip
        items={[
          { label: "Identity", value: session?.user.name ?? "Driver", tone: "teal" },
          { label: "Organization", value: session?.company.name ?? session?.company.code ?? "Tenant", tone: "blue" },
          { label: "Session", value: "Server bound", tone: "green" },
        ]}
      />

      <Panel variant="quiet" tone="violet">
        <SectionHeader eyebrow="Account" title="Driver security" description="Revalidate your authenticated session or securely end access on this device." />
        <Field label="Work email" value={session?.user.email} />
        <Row>
          <ActionButton label="Revalidate session" onPress={() => void refresh().catch((err) => Alert.alert("Session refresh failed", err instanceof Error ? err.message : "Unable to refresh."))} variant="secondary" />
          <ActionButton label="Sign out" onPress={signOut} variant="danger" />
        </Row>
      </Panel>
    </Screen>
  );
}
