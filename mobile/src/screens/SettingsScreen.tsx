import { Alert, Text, View } from "react-native";
import { ActionButton, Field, Panel, Pill, Screen, SectionHeader, colors } from "@/components/ui";
import { useSession } from "@/auth/SessionProvider";
import { useWorkflow } from "@/workflow/WorkflowContext";
import { APP_NAME } from "@/config";

export function SettingsScreen() {
  const { session, roleModel, logout, refresh } = useSession();
  const { selectedJobId } = useWorkflow();

  const signOut = () => {
    Alert.alert("Sign out of this device?", "Local tenant data and the saved secure session will be cleared.", [
      { text: "Cancel", style: "cancel" },
      { text: "Sign out", style: "destructive", onPress: () => void logout() },
    ]);
  };

  return (
    <Screen>
      <Panel>
        <SectionHeader eyebrow="Account" title={session?.user.name ?? APP_NAME} description="Your active organization is bound by the authenticated server session." right={<Pill label={roleModel.title} tone="teal" />} />
        <Field label="Organization" value={session?.company.name} />
        <Field label="Organization code" value={session?.company.code} />
        <Field label="Work email" value={session?.user.email} />
        {selectedJobId ? <Field label="Selected work item" value={String(selectedJobId)} /> : null}
      </Panel>

      <Panel>
        <SectionHeader eyebrow="Security" title="Session protection" description="Credentials are never stored. The server token is device-only and available only while the device is unlocked." />
        <View style={{ gap: 10 }}>
          <Field label="Tenant authority" value="Server-bound bearer session" />
          <Field label="Authorization" value="Backend role and permission grants" />
          <Field label="Local session" value="SecureStore · device only · when unlocked" />
          <Field label="Offline changes" value="Live mutations require a connection" />
        </View>
        <Text style={{ color: colors.muted, lineHeight: 19 }}>
          OpsTrax does not use a role picker, tenant override header, hardcoded account, or fabricated successful action.
        </Text>
        <ActionButton label="Revalidate session" onPress={() => void refresh().catch((error) => Alert.alert("Session refresh failed", error instanceof Error ? error.message : "Unable to refresh."))} variant="secondary" />
      </Panel>

      <Panel>
        <SectionHeader eyebrow="Access" title="Backend-granted permissions" description="These grants are informational; every API action is enforced again on the server." />
        <View style={{ gap: 8 }}>
          {session?.permissions?.length
            ? session.permissions.slice(0, 20).map((permission) => <Field key={permission} label="Permission" value={permission} />)
            : <Field label="Permission" value="No mobile permissions granted" />}
        </View>
      </Panel>

      <Panel>
        <SectionHeader eyebrow="Device" title="End this session" description="Local secure data is removed before server revocation is attempted, so an offline force-close cannot restore the account." />
        <ActionButton label="Sign out securely" onPress={signOut} variant="danger" />
      </Panel>
    </Screen>
  );
}
