import { useState } from "react";
import { KeyboardAvoidingView, Platform, Text, View } from "react-native";
import { ActionButton, colors, Input, Panel, Screen, SectionHeader, Field, EmptyState } from "@/components/ui";
import { API_BASE_URL, APP_NAME, STAGE_LABEL } from "@/config";
import { useSession } from "@/auth/SessionProvider";

export function LoginScreen() {
  const { login, authError, roleModel } = useSession();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(authError);

  const submit = async () => {
    setBusy(true);
    setError(null);
    try {
      await login(email, password);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Login failed.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <Screen>
      <KeyboardAvoidingView behavior={Platform.OS === "ios" ? "padding" : undefined} style={{ gap: 16 }}>
        <Panel>
          <SectionHeader
            eyebrow="Secure entry"
            title={APP_NAME}
            description="This is the first mobile shell for OpsTrax. It uses the same backend login and tenant-scoped authorization model as web."
          />
          <View style={{ gap: 10 }}>
            <Field label="Stage" value={STAGE_LABEL} />
            <Field label="Backend API" value={API_BASE_URL} />
            <Field label="Role model" value={roleModel.title} />
          </View>
        </Panel>

        <Panel>
          <SectionHeader
            eyebrow="Authentication"
            title="Sign in with your OpsTrax account"
            description="No visible role picker, no mobile-only auth shortcut, and no hardcoded tenant identifiers."
          />
          <View style={{ gap: 14 }}>
            <Input label="Email" value={email} onChangeText={setEmail} placeholder="name@company.com" keyboardType="email-address" />
            <Input label="Password" value={password} onChangeText={setPassword} placeholder="Enter password" secureTextEntry />
            <ActionButton label={busy ? "Signing in..." : "Sign in"} onPress={submit} disabled={busy || !email || !password} />
            <Text style={{ color: colors.muted, fontSize: 12, lineHeight: 18 }}>
              The backend determines tenant, role, and permissions from the authenticated session. The mobile client only renders what the session allows.
            </Text>
          </View>
        </Panel>

        {error ? <EmptyState title="Login error" body={error} /> : null}
      </KeyboardAvoidingView>
    </Screen>
  );
}

