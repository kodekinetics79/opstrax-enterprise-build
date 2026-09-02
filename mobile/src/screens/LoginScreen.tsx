import { useState } from "react";
import { KeyboardAvoidingView, Platform, Text, View } from "react-native";
import { LinearGradient } from "expo-linear-gradient";
import { ActionButton, colors, Input, Panel, Screen, SectionHeader, ErrorState, Pill } from "@/components/ui";
import { APP_NAME } from "@/config";
import { useSession } from "@/auth/SessionProvider";

export function LoginScreen() {
  const { login, verifyMfa, cancelMfa, mfaChallenge, authError } = useSession();
  const [companyCode, setCompanyCode] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [mfaCode, setMfaCode] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(authError);

  const submit = async () => {
    setBusy(true);
    setError(null);
    try {
      await login(email, password, companyCode);
      setPassword("");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Sign in failed.");
    } finally {
      setBusy(false);
    }
  };

  const submitMfa = async () => {
    setBusy(true);
    setError(null);
    try {
      await verifyMfa(mfaCode);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Verification failed.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <Screen>
      <KeyboardAvoidingView behavior={Platform.OS === "ios" ? "padding" : undefined} style={{ gap: 16 }}>
        <LinearGradient colors={["rgba(39,211,194,0.22)", "rgba(112,183,255,0.08)"]} style={{ borderRadius: 28 }}>
          <Panel style={{ backgroundColor: "transparent" }}>
            <View style={{ gap: 12 }}>
              <Pill label="Secure operations" tone="teal" />
              <Text style={{ color: colors.text, fontSize: 34, lineHeight: 38, fontWeight: "900", letterSpacing: -1.4 }}>
                Work moves.{"\n"}You stay ahead.
              </Text>
              <Text style={{ color: colors.muted, fontSize: 15, lineHeight: 22 }}>
                {APP_NAME} connects drivers and operations to the same tenant-secured system of record.
              </Text>
            </View>
          </Panel>
        </LinearGradient>

        <Panel>
          <SectionHeader
            eyebrow={mfaChallenge ? "Second factor" : "Workspace sign in"}
            title={mfaChallenge ? "Verify it’s you" : "Welcome back"}
            description={mfaChallenge
              ? `Enter the authenticator code for ${mfaChallenge.email}.`
              : "Your organization code selects the correct tenant before credentials are verified."}
          />
          <View style={{ gap: 14 }}>
            {mfaChallenge ? (
              <>
                <Input label="Authenticator code" value={mfaCode} onChangeText={setMfaCode} placeholder="123456" keyboardType="numeric" autoComplete="one-time-code" textContentType="oneTimeCode" />
                <ActionButton label={busy ? "Verifying…" : "Verify code"} onPress={submitMfa} disabled={busy || !/^\d{6}$/.test(mfaCode)} />
                <ActionButton label="Use a different account" onPress={() => { cancelMfa(); setMfaCode(""); }} disabled={busy} variant="ghost" />
              </>
            ) : (
              <>
                <Input
                  label="Organization code"
                  value={companyCode}
                  onChangeText={(value) => setCompanyCode(value.trimStart().toUpperCase())}
                  placeholder="ACME-LOGISTICS"
                  autoCapitalize="characters"
                  autoComplete="off"
                  textContentType="none"
                />
                <Input label="Work email" value={email} onChangeText={setEmail} placeholder="name@company.com" keyboardType="email-address" autoComplete="email" textContentType="emailAddress" />
                <Input label="Password" value={password} onChangeText={setPassword} placeholder="Enter password" secureTextEntry autoComplete="password" textContentType="password" />
                <ActionButton
                  label={busy ? "Signing in…" : "Sign in securely"}
                  onPress={submit}
                  disabled={busy || !companyCode.trim() || !email.trim() || !password}
                />
              </>
            )}
            <Text style={{ color: colors.subtle, fontSize: 12, lineHeight: 18 }}>
              Tenant, branch, role, and permissions are bound by the server session. OpsTrax never lets the app choose an authenticated tenant after sign in.
            </Text>
          </View>
        </Panel>
        {error ? <ErrorState title="Unable to sign in" body={error} /> : null}
      </KeyboardAvoidingView>
    </Screen>
  );
}
