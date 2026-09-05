import { useMemo, useState } from "react";
import { Alert, Text, View } from "react-native";
import {
  ActionButton,
  EmptyState,
  ErrorState,
  Field,
  HeroPanel,
  LoadingState,
  Panel,
  Pill,
  Row,
  Screen,
  SectionHeader,
  colors,
  toneForStatus,
} from "@/components/ui";
import { useSession } from "@/auth/SessionProvider";
import { useWorkflow } from "@/workflow/WorkflowContext";
import { useAsyncResource } from "@/hooks/useAsyncResource";
import { asRecords, textOf } from "@/data/records";
import type { JsonRecord } from "@/types";
import { APP_NAME } from "@/config";

function notificationId(item: JsonRecord) {
  return String(item.id ?? item.notificationId ?? item.notification_id ?? "").trim();
}

function notificationStatus(item: JsonRecord) {
  return textOf(item.recipientStatus ?? item.recipient_status ?? item.status, "unread").toLowerCase();
}

export function SettingsScreen() {
  const { session, roleModel, logout, refresh, api } = useSession();
  const { selectedJobId } = useWorkflow();
  const [updatingNotificationId, setUpdatingNotificationId] = useState<string | null>(null);

  const notifications = useAsyncResource(
    async () => api.request.get<JsonRecord[]>("/api/notifications"),
    [api],
  );
  const unread = useAsyncResource(
    async () => api.request.get<{ count?: number }>("/api/notifications/unread-count"),
    [api],
  );
  const notificationRows = useMemo(() => asRecords(notifications.data).slice(0, 12), [notifications.data]);
  const unreadCount = Number(unread.data?.count ?? notificationRows.filter((item) => notificationStatus(item) === "unread").length) || 0;

  const signOut = () => {
    Alert.alert("Sign out of this device?", "Local tenant data and the saved secure session will be cleared.", [
      { text: "Cancel", style: "cancel" },
      { text: "Sign out", style: "destructive", onPress: () => void logout() },
    ]);
  };

  const markNotificationRead = async (item: JsonRecord) => {
    const id = notificationId(item);
    if (!id || updatingNotificationId) return;
    setUpdatingNotificationId(id);
    try {
      await api.request.post(`/api/notifications/${encodeURIComponent(id)}/read`, {});
      await Promise.all([notifications.refresh(), unread.refresh()]);
    } catch (error) {
      Alert.alert("Couldn’t update notification", error instanceof Error ? error.message : "Please try again.");
    } finally {
      setUpdatingNotificationId(null);
    }
  };

  return (
    <Screen>
      <HeroPanel tone={unreadCount > 0 ? "amber" : "blue"}>
        <SectionHeader
          eyebrow="Inbox & account"
          title={session?.user.name ?? APP_NAME}
          description="Operational notifications, your active organization, and device security in one place."
          right={<Pill label={unreadCount > 0 ? `${unreadCount} unread` : roleModel.title} tone={unreadCount > 0 ? "amber" : "teal"} />}
        />
        <Row>
          <View style={{ flex: 1 }}><Field label="Organization" value={session?.company.name} /></View>
          <View style={{ flex: 1 }}><Field label="Role" value={roleModel.title} /></View>
        </Row>
        <Field label="Work email" value={session?.user.email} />
        {selectedJobId ? <Field label="Selected work item" value={String(selectedJobId)} /> : null}
      </HeroPanel>

      <Panel variant="elevated" tone={unreadCount > 0 ? "amber" : "teal"}>
        <SectionHeader
          eyebrow="Notifications"
          title="Operational inbox"
          description="Only notifications targeted to this authenticated user, role, driver, and tenant are returned by the server."
          right={<Pill label={notifications.loading ? "Syncing" : unreadCount > 0 ? "Needs review" : "Caught up"} tone={notifications.loading ? "blue" : unreadCount > 0 ? "amber" : "green"} />}
        />

        {notifications.loading ? <LoadingState label="Loading notifications…" /> : null}
        {notifications.error ? <ErrorState title="Inbox unavailable" body={notifications.error} onRetry={notifications.refresh} /> : null}
        {!notifications.loading && !notifications.error && notificationRows.length === 0 ? (
          <EmptyState title="No notifications" body="Operational alerts and messages targeted to your account will appear here." />
        ) : null}

        {!notifications.loading && !notifications.error && notificationRows.length > 0 ? (
          <View style={{ gap: 10 }}>
            {notificationRows.map((item, index) => {
              const id = notificationId(item);
              const status = notificationStatus(item);
              const severity = textOf(item.severity, status === "unread" ? "Medium" : "Read");
              const title = textOf(item.title, textOf(item.eventType ?? item.event_type, `Notification ${index + 1}`));
              const body = textOf(item.message, "No additional detail was supplied.");
              const createdAt = textOf(item.createdAt ?? item.created_at ?? item.deliveredAt ?? item.delivered_at, "");
              const isUnread = status === "unread";
              return (
                <Panel key={id || `${title}-${index}`} variant="quiet" tone={toneForStatus(severity)}>
                  <Row>
                    <View style={{ flex: 1, gap: 3 }}>
                      <Text style={{ color: colors.text, fontSize: 15, fontWeight: "900" }}>{title}</Text>
                      <Text style={{ color: colors.muted, fontSize: 12, lineHeight: 18 }}>{body}</Text>
                    </View>
                    <Pill label={isUnread ? severity : status} tone={isUnread ? toneForStatus(severity) : "green"} />
                  </Row>
                  {createdAt ? <Text style={{ color: colors.subtle, fontSize: 11 }}>{createdAt}</Text> : null}
                  {isUnread && id ? (
                    <ActionButton
                      label={updatingNotificationId === id ? "Updating…" : "Mark as read"}
                      onPress={() => void markNotificationRead(item)}
                      variant="secondary"
                    />
                  ) : null}
                </Panel>
              );
            })}
          </View>
        ) : null}
      </Panel>

      <Panel variant="elevated" tone="teal">
        <SectionHeader
          eyebrow="Security"
          title="Session protection"
          description="Credentials are never stored. The server token is device-only and available only while the device is unlocked."
        />
        <View style={{ gap: 10 }}>
          <Field label="Organization code" value={session?.company.code} />
          <Field label="Tenant authority" value="Server-bound bearer session" />
          <Field label="Authorization" value="Backend role and permission grants" />
          <Field label="Local session" value="SecureStore · device only · when unlocked" />
          <Field label="Offline changes" value="Live mutations require a connection unless a workflow explicitly supports queued sync" />
        </View>
        <Text style={{ color: colors.muted, lineHeight: 19 }}>
          OpsTrax does not use a role picker, tenant override header, hardcoded account, or fabricated successful action.
        </Text>
        <ActionButton
          label="Revalidate session"
          onPress={() => void refresh().catch((error) => Alert.alert("Session refresh failed", error instanceof Error ? error.message : "Unable to refresh."))}
          variant="secondary"
        />
      </Panel>

      <Panel variant="quiet" tone="violet">
        <SectionHeader
          eyebrow="Access"
          title="Backend-granted permissions"
          description="These grants are informational; every API action is enforced again on the server."
        />
        <View style={{ gap: 8 }}>
          {session?.permissions?.length
            ? session.permissions.slice(0, 20).map((permission) => <Field key={permission} label="Permission" value={permission} />)
            : <Field label="Permission" value="No mobile permissions granted" />}
        </View>
      </Panel>

      <Panel variant="solid" tone="red">
        <SectionHeader
          eyebrow="Device"
          title="End this session"
          description="Local secure data is removed before server revocation is attempted, so an offline force-close cannot restore the account."
        />
        <ActionButton label="Sign out securely" onPress={signOut} variant="danger" />
      </Panel>
    </Screen>
  );
}
