import { useState } from "react";
import { KeyboardAvoidingView, Platform, Text, View } from "react-native";
import {
  ActionButton,
  BrandMark,
  colors,
  ErrorState,
  HeroPanel,
  Input,
  Panel,
  Pill,
  Screen,
  SectionHeader,
} from "@/components/ui";
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
        <HeroPanel tone="teal">
          <View style={{ gap: 18 }}>
            <View style={{ flexDirection: "row", alignItems: "center", justifyContent: "space-between", gap: 14 }}>
              <BrandMark />
              <Pill label="Tenant-secured" tone="teal" />
            </View>
            <View style={{ gap: 10 }}>
              <Text style={{ color: colors.text, fontSize: 35, lineHeight: 39, fontWeight: "900", letterSpacing: -1.45 }}>
                Move with clarity.{"\n"}Operate with control.
              </Text>
              <Text style={{ color: colors.muted, fontSize: 15, lineHeight: 22, maxWidth: 520 }}>
                {APP_NAME} brings drivers, customers, and fleet teams into one secure operating network without exposing one tenant to another.
              </Text>
            </View>
            <View style={{ flexDirection: "row", flexWrap: "wrap", gap: 8 }}>
              <Pill label="Driver" tone="blue" />
              <Pill label="Fleet" tone="violet" />
              <Pill label="Customer" tone="green" />
            </View>
          </View>
        </HeroPanel>

        <Panel variant="elevated">
          <SectionHeader
            eyebrow={mfaChallenge ? "Second factor" : "Secure workspace"}
            title={mfaChallenge ? "Verify it’s you" : "Welcome back"}
            description={mfaChallenge
              ? `Enter the authenticator code for ${mfaChallenge.email}.`
              : "Your organization code resolves the tenant boundary before credentials are accepted."}
          />
          <View style={{ gap: 14 }}>
            {mfaChallenge ? (
              <>
                <Input
                  label="Authenticator code"
                  value={mfaCode}
                  onChangeText={setMfaCode}
                  placeholder="123456"
                  keyboardType="numeric"
                  autoComplete="one-time-code"
                  textContentType="oneTimeCode"
                />
                <ActionButton label={busy ? "Verifying…" : "Verify code"} onPress={submitMfa} disabled={busy || !/^\d{6}$/.test(mfaCode)} />
                <ActionButton
                  label="Use a different account"
                  onPress={() => { cancelMfa(); setMfaCode(""); }}
                  disabled={busy}
                  variant="ghost"
                />
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
                <Input
                  label="Work email"
                  value={email}
                  onChangeText={setEmail}
                  placeholder="name@company.com"
                  keyboardType="email-address"
                  autoComplete="email"
                  textContentType="emailAddress"
                />
                <Input
                  label="Password"
                  value={password}
                  onChangeText={setPassword}
                  placeholder="Enter password"
                  secureTextEntry
                  autoComplete="password"
                  textContentType="password"
                />
                <ActionButton
                  label={busy ? "Signing in…" : "Sign in securely"}
                  onPress={submit}
                  disabled={busy || !companyCode.trim() || !email.trim() || !password}
                />
              </>
            )}
            <Text style={{ color: colors.subtle, fontSize: 11.5, lineHeight: 17.5 }}>
              Tenant, branch, role, permissions, and account ownership are bound by the server session. The mobile client never chooses an authenticated tenant after sign-in.
            </Text>
          </View>
        </Panel>

        {error ? <ErrorState title="Unable to sign in" body={error} /> : null}
      </KeyboardAvoidingView>
    </Screen>
  );
}
