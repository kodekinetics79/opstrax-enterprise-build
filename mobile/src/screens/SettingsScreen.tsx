import { View, Text } from "react-native";
import { ActionButton, Field, Panel, Pill, Screen, SectionHeader, colors } from "@/components/ui";
import { useSession } from "@/auth/SessionProvider";
import { useWorkflow } from "@/workflow/WorkflowContext";
import { APP_NAME, API_BASE_URL, STAGE_LABEL } from "@/config";
import { ROLE_MODELS } from "@/data/roleModel";

export function SettingsScreen() {
  const { session, roleModel, normalizedRole, logout } = useSession();
  const { selectedJobId } = useWorkflow();
  const role = ROLE_MODELS.find((entry) => entry.role === normalizedRole) ?? roleModel;

  return (
    <Screen>
      <Panel>
        <SectionHeader eyebrow="Account" title={APP_NAME} description="Secure session, role visibility, and operational contract preview." right={<Pill label={role.title} tone="teal" />} />
        <Field label="Tenant / company" value={session?.company.name} />
        <Field label="Company code" value={session?.company.code} />
        <Field label="User" value={session?.user.name} />
        <Field label="Email" value={session?.user.email} />
        <Field label="Selected job" value={selectedJobId ? String(selectedJobId) : "None"} />
      </Panel>

      <Panel>
        <SectionHeader eyebrow="Security" title="How the mobile shell stays safe" description="The backend owns authentication, permissions, and tenant context. Mobile stores only the session token and metadata needed to keep using the same contract." />
        <View style={{ gap: 10 }}>
          <Field label="Stage" value={STAGE_LABEL} />
          <Field label="API base URL" value={API_BASE_URL} />
          <Field label="Session storage" value="Expo SecureStore" />
          <Field label="Offline sync contract" value="Preview only; no sync engine yet" />
          <Field label="Notification contract" value="Event mapping only; no push delivery yet" />
        </View>
        <Text style={{ color: colors.muted, lineHeight: 19 }}>
          The app does not hardcode tenant IDs, user IDs, tokens, or production URLs. It uses the authenticated backend session and the current environment variable only.
        </Text>
      </Panel>

      <Panel>
        <SectionHeader eyebrow="Permissions" title="Session grants" description="These are the backend-granted permissions visible to the mobile client." />
        <View style={{ gap: 8 }}>
          {session?.permissions?.length ? session.permissions.slice(0, 12).map((permission) => <Field key={permission} label="Permission" value={permission} />) : <Field label="Permission" value="No permissions loaded yet" />}
        </View>
      </Panel>

      <Panel>
        <SectionHeader eyebrow="Exit" title="End this session" description="Logout clears the local secure session and revokes the server session when possible." />
        <ActionButton label="Logout" onPress={() => void logout()} variant="secondary" />
      </Panel>
    </Screen>
  );
}

