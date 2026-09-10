import { useEffect, useRef, useState } from "react";
import { Animated, Easing, KeyboardAvoidingView, Platform, Text, View } from "react-native";
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
import { APP_NAME, APP_VARIANT } from "@/config";
import { useSession } from "@/auth/SessionProvider";

export function LoginScreen() {
  const { login, verifyMfa, cancelMfa, mfaChallenge, authError } = useSession();
  const [companyCode, setCompanyCode] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [mfaCode, setMfaCode] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(authError);

  const logoEntrance = useRef(new Animated.Value(0)).current;
  const heroEntrance = useRef(new Animated.Value(0)).current;
  const formEntrance = useRef(new Animated.Value(0)).current;
  const glowPulse = useRef(new Animated.Value(0)).current;

  useEffect(() => {
    Animated.stagger(110, [
      Animated.timing(logoEntrance, {
        toValue: 1,
        duration: 520,
        easing: Easing.out(Easing.cubic),
        useNativeDriver: true,
      }),
      Animated.timing(heroEntrance, {
        toValue: 1,
        duration: 520,
        easing: Easing.out(Easing.cubic),
        useNativeDriver: true,
      }),
      Animated.timing(formEntrance, {
        toValue: 1,
        duration: 520,
        easing: Easing.out(Easing.cubic),
        useNativeDriver: true,
      }),
    ]).start();

    const pulse = Animated.loop(
      Animated.sequence([
        Animated.timing(glowPulse, {
          toValue: 1,
          duration: 1800,
          easing: Easing.inOut(Easing.sin),
          useNativeDriver: true,
        }),
        Animated.timing(glowPulse, {
          toValue: 0,
          duration: 1800,
          easing: Easing.inOut(Easing.sin),
          useNativeDriver: true,
        }),
      ]),
    );
    pulse.start();
    return () => pulse.stop();
  }, [formEntrance, glowPulse, heroEntrance, logoEntrance]);

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

  const entranceStyle = (value: Animated.Value, distance = 12) => ({
    opacity: value,
    transform: [
      {
        translateY: value.interpolate({
          inputRange: [0, 1],
          outputRange: [distance, 0],
        }),
      },
    ],
  });

  const isDriver = APP_VARIANT === "driver";

  return (
    <Screen>
      <KeyboardAvoidingView behavior={Platform.OS === "ios" ? "padding" : undefined} style={{ gap: 16 }}>
        <Animated.View style={entranceStyle(logoEntrance, 8)}>
          <View style={{ flexDirection: "row", alignItems: "center", justifyContent: "space-between", gap: 14, paddingHorizontal: 2 }}>
            <View style={{ flexDirection: "row", alignItems: "center", gap: 12 }}>
              <Animated.View
                style={{
                  transform: [
                    {
                      scale: glowPulse.interpolate({
                        inputRange: [0, 1],
                        outputRange: [1, 1.055],
                      }),
                    },
                  ],
                  opacity: glowPulse.interpolate({ inputRange: [0, 1], outputRange: [0.92, 1] }),
                }}
              >
                <BrandMark label="OT" />
              </Animated.View>
              <View style={{ gap: 1 }}>
                <Text style={{ color: colors.text, fontSize: 20, fontWeight: "900", letterSpacing: -0.55 }}>OpsTrax</Text>
                <Text style={{ color: isDriver ? colors.teal : colors.blue, fontSize: 10.5, fontWeight: "800", letterSpacing: 1.9 }}>
                  {isDriver ? "DRIVER" : APP_NAME.toUpperCase()}
                </Text>
              </View>
            </View>
            <Pill label="Secure fleet access" tone="teal" />
          </View>
        </Animated.View>

        <Animated.View style={entranceStyle(heroEntrance)}>
          <HeroPanel tone="teal">
            <View style={{ gap: 18 }}>
              <View style={{ gap: 10 }}>
                <Text style={{ color: colors.text, fontSize: 35, lineHeight: 39, fontWeight: "900", letterSpacing: -1.45 }}>
                  {isDriver ? "Your route. Your proof.\nYour day in control." : "Move with clarity.\nOperate with control."}
                </Text>
                <Text style={{ color: colors.muted, fontSize: 15, lineHeight: 22, maxWidth: 520 }}>
                  {isDriver
                    ? "Stay connected to assignments, stops, inspections, proof of delivery, and dispatch from one secure driver workspace."
                    : `${APP_NAME} brings drivers, customers, and fleet teams into one secure operating network without exposing one tenant to another.`}
                </Text>
              </View>
              <View style={{ flexDirection: "row", flexWrap: "wrap", gap: 8 }}>
                {isDriver ? (
                  <>
                    <Pill label="Assignments" tone="blue" />
                    <Pill label="DVIR" tone="violet" />
                    <Pill label="POD" tone="green" />
                    <Pill label="Dispatch" tone="teal" />
                  </>
                ) : (
                  <>
                    <Pill label="Driver" tone="blue" />
                    <Pill label="Fleet" tone="violet" />
                    <Pill label="Customer" tone="green" />
                  </>
                )}
              </View>
            </View>
          </HeroPanel>
        </Animated.View>

        <Animated.View style={entranceStyle(formEntrance)}>
          <Panel variant="elevated">
            <SectionHeader
              eyebrow={mfaChallenge ? "Second factor" : isDriver ? "Driver sign-in" : "Secure workspace"}
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
              <Text style={{ color: colors.subtle, fontSize: 10.5, lineHeight: 15, textAlign: "center" }}>
                Release: DRIVER-2026.09.10-B
              </Text>
            </View>
          </Panel>
        </Animated.View>

        {error ? <ErrorState title="Unable to sign in" body={error} /> : null}
      </KeyboardAvoidingView>
    </Screen>
  );
}
