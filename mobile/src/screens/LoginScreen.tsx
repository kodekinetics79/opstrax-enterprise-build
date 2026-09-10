import { useEffect, useState } from "react";
import { Animated, Easing, KeyboardAvoidingView, Platform, StyleSheet, Text, View } from "react-native";
import { LinearGradient } from "expo-linear-gradient";
import {
  ActionButton,
  colors,
  ErrorState,
  Input,
  Panel,
  Screen,
  SectionHeader,
} from "@/components/ui";
import { APP_VARIANT } from "@/config";
import { useSession } from "@/auth/SessionProvider";

const featureCards = [
  { code: "A", title: "Assignments", subtitle: "Your routes" },
  { code: "D", title: "DVIR", subtitle: "Stay compliant" },
  { code: "P", title: "POD", subtitle: "Capture & submit" },
  { code: "↗", title: "Dispatch", subtitle: "Always connected" },
];

function EnterpriseMark({ pulse, tilt }: { pulse: Animated.Value; tilt: Animated.Value }) {
  const rotate = tilt.interpolate({ inputRange: [0, 1], outputRange: ["-4deg", "4deg"] });
  const scale = pulse.interpolate({ inputRange: [0, 1], outputRange: [1, 1.045] });
  const glowOpacity = pulse.interpolate({ inputRange: [0, 1], outputRange: [0.22, 0.52] });

  return (
    <Animated.View style={[styles.markStage, { transform: [{ perspective: 700 }, { rotateZ: rotate }, { scale }] }]}>
      <Animated.View style={[styles.markGlow, { opacity: glowOpacity }]} />
      <LinearGradient colors={["#67f4ea", "#19b6d8"]} style={[styles.markFace, styles.markFaceTop]} />
      <LinearGradient colors={["#1bb9b6", "#087986"]} style={[styles.markFace, styles.markFaceLeft]} />
      <LinearGradient colors={["#3e8dff", "#154b9c"]} style={[styles.markFace, styles.markFaceRight]} />
      <View style={styles.routeStem} />
      <View style={[styles.routeArm, styles.routeArmLeft]} />
      <View style={[styles.routeArm, styles.routeArmRight]} />
      <View style={[styles.routeNode, styles.routeNodeTop]} />
      <View style={[styles.routeNode, styles.routeNodeLeft]} />
      <View style={[styles.routeNode, styles.routeNodeRight]} />
    </Animated.View>
  );
}

function FeatureCard({ code, title, subtitle }: { code: string; title: string; subtitle: string }) {
  return (
    <View style={styles.featureCard}>
      <LinearGradient colors={["rgba(38,223,216,0.12)", "rgba(35,103,255,0.05)"]} style={StyleSheet.absoluteFill} />
      <View style={styles.featureIcon}>
        <Text style={styles.featureIconText}>{code}</Text>
      </View>
      <Text style={styles.featureTitle}>{title}</Text>
      <Text style={styles.featureSubtitle}>{subtitle}</Text>
    </View>
  );
}

export function LoginScreen() {
  const { login, verifyMfa, cancelMfa, mfaChallenge, authError } = useSession();
  const [companyCode, setCompanyCode] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [mfaCode, setMfaCode] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(authError);

  const [brandIn] = useState(() => new Animated.Value(0));
  const [featuresIn] = useState(() => new Animated.Value(0));
  const [formIn] = useState(() => new Animated.Value(0));
  const [pulse] = useState(() => new Animated.Value(0));
  const [tilt] = useState(() => new Animated.Value(0));

  useEffect(() => {
    Animated.stagger(120, [
      Animated.timing(brandIn, { toValue: 1, duration: 620, easing: Easing.out(Easing.cubic), useNativeDriver: true }),
      Animated.timing(featuresIn, { toValue: 1, duration: 520, easing: Easing.out(Easing.cubic), useNativeDriver: true }),
      Animated.timing(formIn, { toValue: 1, duration: 560, easing: Easing.out(Easing.cubic), useNativeDriver: true }),
    ]).start();

    const pulseLoop = Animated.loop(Animated.sequence([
      Animated.timing(pulse, { toValue: 1, duration: 2100, easing: Easing.inOut(Easing.sin), useNativeDriver: true }),
      Animated.timing(pulse, { toValue: 0, duration: 2100, easing: Easing.inOut(Easing.sin), useNativeDriver: true }),
    ]));
    const tiltLoop = Animated.loop(Animated.sequence([
      Animated.timing(tilt, { toValue: 1, duration: 3200, easing: Easing.inOut(Easing.sin), useNativeDriver: true }),
      Animated.timing(tilt, { toValue: 0, duration: 3200, easing: Easing.inOut(Easing.sin), useNativeDriver: true }),
    ]));
    pulseLoop.start();
    tiltLoop.start();
    return () => { pulseLoop.stop(); tiltLoop.stop(); };
  }, [brandIn, featuresIn, formIn, pulse, tilt]);

  const reveal = (value: Animated.Value, distance = 16) => ({
    opacity: value,
    transform: [{ translateY: value.interpolate({ inputRange: [0, 1], outputRange: [distance, 0] }) }],
  });

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

  const isDriver = APP_VARIANT === "driver";

  return (
    <Screen>
      <KeyboardAvoidingView behavior={Platform.OS === "ios" ? "padding" : undefined} style={styles.page}>
        <Animated.View style={[styles.hero, reveal(brandIn, 10)]}>
          <LinearGradient colors={["rgba(16,50,84,0.96)", "rgba(6,22,39,0.98)", "rgba(3,13,24,1)"]} style={StyleSheet.absoluteFill} />
          <View style={styles.heroOrbOne} />
          <View style={styles.heroOrbTwo} />
          <View style={styles.heroTopRow}>
            <View style={styles.brandLockup}>
              <EnterpriseMark pulse={pulse} tilt={tilt} />
              <View>
                <Text style={styles.brandName}>OpsTrax</Text>
                <Text style={styles.brandProduct}>{isDriver ? "DRIVER" : "MOBILE"}</Text>
              </View>
            </View>
            <View style={styles.securePill}>
              <View style={styles.secureDot} />
              <Text style={styles.secureText}>SECURE FLEET ACCESS</Text>
            </View>
          </View>

          <View style={styles.heroCopy}>
            <Text style={styles.heroEyebrow}>CONNECTED FLEETS · STRONGER BUSINESSES</Text>
            <Text style={styles.heroTitle}>{isDriver ? "A smarter road ahead." : "Operate with clarity."}</Text>
            <Text style={styles.heroBody}>
              {isDriver
                ? "Assignments, inspections, proof of delivery and dispatch — one secure driver command surface built for the road."
                : "One secure operating network for field execution and fleet control."}
            </Text>
          </View>

          <View style={styles.routeLine}>
            <View style={[styles.routePoint, { left: "6%" }]} />
            <View style={[styles.routePoint, { left: "36%" }]} />
            <View style={[styles.routePoint, { left: "68%" }]} />
            <View style={[styles.routePoint, { right: "4%" }]} />
          </View>
        </Animated.View>

        <Animated.View style={[styles.featureGrid, reveal(featuresIn, 14)]}>
          {featureCards.map((item) => <FeatureCard key={item.title} {...item} />)}
        </Animated.View>

        <Animated.View style={reveal(formIn, 18)}>
          <Panel variant="elevated" style={styles.loginPanel}>
            <SectionHeader
              eyebrow={mfaChallenge ? "Second factor" : "OpsTrax Driver"}
              title={mfaChallenge ? "Verify it’s you" : "Welcome back"}
              description={mfaChallenge
                ? `Enter the authenticator code for ${mfaChallenge.email}.`
                : "Sign in to your secure driver workspace."}
            />

            <View style={styles.formStack}>
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
                  <ActionButton label={busy ? "Signing in…" : "Sign in securely"} onPress={submit} disabled={busy || !companyCode.trim() || !email.trim() || !password} />
                </>
              )}
              <View style={styles.trustStrip}>
                <View style={styles.trustShield}><Text style={styles.trustShieldText}>✓</Text></View>
                <Text style={styles.trustText}>Tenant-isolated access · encrypted session · server-bound permissions</Text>
              </View>
              <Text style={styles.releaseText}>Release: DRIVER-2026.09.10-C · ENTERPRISE LOGIN V2</Text>
            </View>
          </Panel>
        </Animated.View>

        {error ? <ErrorState title="Unable to sign in" body={error} /> : null}
      </KeyboardAvoidingView>
    </Screen>
  );
}

const styles = StyleSheet.create({
  page: { gap: 14 },
  hero: { minHeight: 330, borderRadius: 30, overflow: "hidden", borderWidth: 1, borderColor: "rgba(91,222,218,0.28)", padding: 22 },
  heroOrbOne: { position: "absolute", width: 190, height: 190, borderRadius: 95, backgroundColor: "rgba(33,214,210,0.10)", right: -58, top: -64 },
  heroOrbTwo: { position: "absolute", width: 150, height: 150, borderRadius: 75, backgroundColor: "rgba(53,105,255,0.09)", left: -54, bottom: -68 },
  heroTopRow: { flexDirection: "row", alignItems: "center", justifyContent: "space-between", gap: 14 },
  brandLockup: { flexDirection: "row", alignItems: "center", gap: 14 },
  brandName: { color: colors.text, fontSize: 29, fontWeight: "900", letterSpacing: -1.1 },
  brandProduct: { color: colors.teal, fontSize: 11, fontWeight: "900", letterSpacing: 4.3, marginTop: 1 },
  markStage: { width: 78, height: 78, alignItems: "center", justifyContent: "center" },
  markGlow: { position: "absolute", width: 78, height: 78, borderRadius: 22, backgroundColor: colors.teal, transform: [{ scale: 1.18 }] },
  markFace: { position: "absolute", width: 42, height: 42, borderRadius: 8, borderWidth: 1, borderColor: "rgba(255,255,255,0.25)" },
  markFaceTop: { transform: [{ rotate: "45deg" }, { translateY: -13 }], opacity: 0.95 },
  markFaceLeft: { transform: [{ rotate: "45deg" }, { translateX: -13 }, { translateY: 8 }], opacity: 0.82 },
  markFaceRight: { transform: [{ rotate: "45deg" }, { translateX: 13 }, { translateY: 8 }], opacity: 0.82 },
  routeStem: { position: "absolute", width: 4, height: 32, borderRadius: 2, backgroundColor: "white", top: 20 },
  routeArm: { position: "absolute", width: 25, height: 4, borderRadius: 2, backgroundColor: "white", top: 30 },
  routeArmLeft: { left: 19, transform: [{ rotate: "-34deg" }] },
  routeArmRight: { right: 19, transform: [{ rotate: "34deg" }] },
  routeNode: { position: "absolute", width: 9, height: 9, borderRadius: 5, backgroundColor: "white", borderWidth: 2, borderColor: "#4ae8e0" },
  routeNodeTop: { top: 15, left: 35 },
  routeNodeLeft: { top: 35, left: 15 },
  routeNodeRight: { top: 35, right: 15 },
  securePill: { flexDirection: "row", alignItems: "center", gap: 6, paddingHorizontal: 10, paddingVertical: 7, borderRadius: 999, borderWidth: 1, borderColor: "rgba(67,225,210,0.34)", backgroundColor: "rgba(22,150,142,0.14)" },
  secureDot: { width: 6, height: 6, borderRadius: 3, backgroundColor: colors.teal },
  secureText: { color: colors.teal, fontSize: 8.5, fontWeight: "900", letterSpacing: 1.15 },
  heroCopy: { marginTop: 44, maxWidth: 570, gap: 8 },
  heroEyebrow: { color: colors.teal, fontSize: 10.5, fontWeight: "900", letterSpacing: 1.6 },
  heroTitle: { color: colors.text, fontSize: 42, lineHeight: 45, fontWeight: "900", letterSpacing: -1.8 },
  heroBody: { color: colors.muted, fontSize: 15, lineHeight: 22, maxWidth: 520 },
  routeLine: { position: "absolute", left: 22, right: 22, bottom: 22, height: 2, backgroundColor: "rgba(79,193,255,0.28)" },
  routePoint: { position: "absolute", width: 8, height: 8, borderRadius: 4, backgroundColor: colors.teal, top: -3, shadowColor: colors.teal, shadowOpacity: 0.8, shadowRadius: 8 },
  featureGrid: { flexDirection: "row", flexWrap: "wrap", gap: 10 },
  featureCard: { flexGrow: 1, flexBasis: "22%", minWidth: 135, minHeight: 112, borderRadius: 22, overflow: "hidden", borderWidth: 1, borderColor: "rgba(118,192,255,0.22)", padding: 14, justifyContent: "center", alignItems: "center", gap: 5, backgroundColor: "rgba(8,22,38,0.86)" },
  featureIcon: { width: 36, height: 36, borderRadius: 13, alignItems: "center", justifyContent: "center", backgroundColor: "rgba(58,216,223,0.14)", borderWidth: 1, borderColor: "rgba(83,225,219,0.24)" },
  featureIconText: { color: colors.teal, fontSize: 17, fontWeight: "900" },
  featureTitle: { color: colors.text, fontSize: 13, fontWeight: "800" },
  featureSubtitle: { color: colors.subtle, fontSize: 9.5, fontWeight: "700", textTransform: "uppercase", letterSpacing: 0.6 },
  loginPanel: { borderColor: "rgba(77,206,255,0.30)" },
  formStack: { gap: 14 },
  trustStrip: { flexDirection: "row", alignItems: "center", justifyContent: "center", gap: 8, paddingTop: 2 },
  trustShield: { width: 20, height: 20, borderRadius: 8, borderWidth: 1, borderColor: "rgba(65,222,210,0.45)", alignItems: "center", justifyContent: "center", backgroundColor: "rgba(65,222,210,0.10)" },
  trustShieldText: { color: colors.teal, fontSize: 11, fontWeight: "900" },
  trustText: { color: colors.subtle, fontSize: 10.5, lineHeight: 15, textAlign: "center", flexShrink: 1 },
  releaseText: { color: colors.teal, opacity: 0.72, fontSize: 9.5, lineHeight: 14, textAlign: "center", fontWeight: "800", letterSpacing: 0.7 },
});
