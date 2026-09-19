import { useEffect, useMemo, useState } from "react";
import {
  Animated,
  Easing,
  KeyboardAvoidingView,
  Platform,
  Pressable,
  StyleSheet,
  Text,
  TextInput,
  View,
  useWindowDimensions,
} from "react-native";
import { BlurView } from "expo-blur";
import { LinearGradient } from "expo-linear-gradient";
import { ErrorState, Screen } from "@/components/ui";
import { EnterpriseMark } from "@/components/DriverExperience";
import { STAGE_LABEL } from "@/config";
import { useSession } from "@/auth/SessionProvider";

const SIGNALS = [
  { code: "LD", label: "Live load", value: "Dispatch linked", tone: "#67f5df" },
  { code: "RT", label: "Route", value: "Turn-by-turn ready", tone: "#65b7ff" },
  { code: "VH", label: "Vehicle", value: "DVIR + telemetry", tone: "#a691ff" },
  { code: "PD", label: "Proof", value: "Photo • GPS • POD", tone: "#73e9ff" },
];

const NETWORK = [
  { label: "DISPATCH", value: "ONLINE" },
  { label: "GPS", value: "LIVE" },
  { label: "SAFETY", value: "SYNCED" },
];

function NetworkPill({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.networkPill}>
      <View style={styles.networkDot} />
      <View>
        <Text style={styles.networkLabel}>{label}</Text>
        <Text style={styles.networkValue}>{value}</Text>
      </View>
    </View>
  );
}

function LiquidFrame({ children, style }: { children: React.ReactNode; style?: object }) {
  const content = (
    <LinearGradient
      colors={["rgba(19,50,78,0.82)", "rgba(7,23,41,0.68)", "rgba(5,16,31,0.92)"]}
      start={{ x: 0, y: 0 }}
      end={{ x: 1, y: 1 }}
      style={styles.glassFill}
    >
      <View pointerEvents="none" style={styles.glassSpecular} />
      {children}
    </LinearGradient>
  );

  return (
    <LinearGradient
      colors={["rgba(117,242,255,0.92)", "rgba(63,132,255,0.48)", "rgba(161,112,255,0.58)", "rgba(117,242,255,0.24)"]}
      start={{ x: 0, y: 0 }}
      end={{ x: 1, y: 1 }}
      style={[styles.glassBorder, style]}
    >
      {Platform.OS === "ios" ? (
        <BlurView intensity={54} tint="dark" style={styles.glassBlur}>
          {content}
        </BlurView>
      ) : (
        <View style={styles.glassBlur}>{content}</View>
      )}
    </LinearGradient>
  );
}

function TmsWorld({ travel }: { travel: Animated.Value }) {
  const markerX = travel.interpolate({ inputRange: [0, 1], outputRange: [-18, 230] });
  const truckFloat = travel.interpolate({ inputRange: [0, 1], outputRange: [0, -6] });
  return (
    <View pointerEvents="none" style={styles.world}>
      <LinearGradient
        colors={["#07162a", "#0a2740", "#06121f", "#020812"]}
        locations={[0, 0.38, 0.72, 1]}
        style={StyleSheet.absoluteFill}
      />
      <View style={styles.auroraOne} />
      <View style={styles.auroraTwo} />

      <View style={styles.gridPlane}>
        {[0, 1, 2, 3, 4, 5].map((row) => (
          <View key={"gh" + row} style={[styles.gridH, { top: 18 + row * 40 }]} />
        ))}
        {[0, 1, 2, 3, 4, 5, 6].map((col) => (
          <View key={"gv" + col} style={[styles.gridV, { left: 18 + col * 44 }]} />
        ))}
      </View>

      <View style={[styles.routeBeam, styles.routeA]} />
      <View style={[styles.routeBeam, styles.routeB]} />
      <View style={[styles.routeBeam, styles.routeC]} />
      <View style={[styles.routeNode, { left: "12%", top: "54%" }]} />
      <View style={[styles.routeNode, { left: "39%", top: "39%" }]} />
      <View style={[styles.routeNode, { left: "68%", top: "50%" }]} />
      <View style={[styles.routeNode, { right: "8%", top: "30%" }]} />
      <Animated.View style={[styles.routeRunner, { transform: [{ translateX: markerX }] }]} />

      <View style={styles.roadDeck}>
        <LinearGradient
          colors={["rgba(36,78,111,0.04)", "rgba(12,30,50,0.96)", "rgba(2,8,16,1)"]}
          style={StyleSheet.absoluteFill}
        />
        <View style={[styles.roadStripe, { left: "36%" }]} />
        <View style={[styles.roadStripe, { left: "63%" }]} />
      </View>

      <Animated.View style={[styles.truckStage, { transform: [{ translateY: truckFloat }] }]}>
        <View style={styles.truckShadow} />
        <LinearGradient colors={["#2c7f9e", "#12334b", "#06131f"]} style={styles.truckTrailer}>
          <View style={styles.trailerEdge} />
          <View style={styles.trailerBadge}><Text style={styles.trailerBadgeText}>OPSTRAX</Text></View>
        </LinearGradient>
        <LinearGradient colors={["#36a2bd", "#15445b", "#071721"]} style={styles.truckCab}>
          <View style={styles.cabRoof} />
          <LinearGradient colors={["rgba(132,224,255,0.64)", "rgba(29,72,99,0.56)"]} style={styles.cabGlass} />
          <View style={styles.cabGrille} />
        </LinearGradient>
        <View style={[styles.wheel, { left: 34 }]} />
        <View style={[styles.wheel, { right: 62 }]} />
        <View style={[styles.wheel, { right: 18 }]} />
        <View style={[styles.light, { left: 8 }]} />
        <View style={[styles.light, { left: 72 }]} />
      </Animated.View>

      <View style={styles.hudCorner}>
        <Text style={styles.hudKicker}>LIVE TMS</Text>
        <Text style={styles.hudTitle}>Driver command layer</Text>
        <Text style={styles.hudBody}>Load • Route • Vehicle • Proof</Text>
      </View>
    </View>
  );
}

function SignalDeck() {
  return (
    <View style={styles.signalDeck}>
      {SIGNALS.map((item) => (
        <LiquidFrame key={item.code} style={styles.signalFrame}>
          <View style={styles.signalInner}>
            <View style={[styles.signalCode, { borderColor: item.tone + "66", backgroundColor: item.tone + "12" }]}>
              <Text style={[styles.signalCodeText, { color: item.tone }]}>{item.code}</Text>
            </View>
            <Text numberOfLines={1} style={styles.signalLabel}>{item.label}</Text>
            <Text numberOfLines={2} style={styles.signalValue}>{item.value}</Text>
          </View>
        </LiquidFrame>
      ))}
    </View>
  );
}

export function DriverCommandLoginScreen() {
  const { width } = useWindowDimensions();
  const compact = width < 600;
  const { login, verifyMfa, cancelMfa, mfaChallenge, authError } = useSession();
  const previewDefault = ["preview", "pilot"].includes(STAGE_LABEL.toLowerCase()) ? "OPX-DEMO" : "";
  const [companyCode, setCompanyCode] = useState(previewDefault);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [mfaCode, setMfaCode] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(authError);
  const [enter] = useState(() => new Animated.Value(0));
  const [cardEnter] = useState(() => new Animated.Value(0));
  const [travel] = useState(() => new Animated.Value(0));

  useEffect(() => {
    Animated.parallel([
      Animated.timing(enter, { toValue: 1, duration: 650, easing: Easing.out(Easing.cubic), useNativeDriver: true }),
      Animated.timing(cardEnter, { toValue: 1, duration: 560, delay: 160, easing: Easing.out(Easing.cubic), useNativeDriver: true }),
    ]).start();
    const loop = Animated.loop(
      Animated.sequence([
        Animated.timing(travel, { toValue: 1, duration: 4200, easing: Easing.inOut(Easing.sin), useNativeDriver: true }),
        Animated.timing(travel, { toValue: 0, duration: 4200, easing: Easing.inOut(Easing.sin), useNativeDriver: true }),
      ]),
    );
    loop.start();
    return () => loop.stop();
  }, [cardEnter, enter, travel]);

  const heroMotion = useMemo(() => ({
    opacity: enter,
    transform: [{ translateY: enter.interpolate({ inputRange: [0, 1], outputRange: [14, 0] }) }],
  }), [enter]);

  const cardMotion = useMemo(() => ({
    opacity: cardEnter,
    transform: [{ translateY: cardEnter.interpolate({ inputRange: [0, 1], outputRange: [24, 0] }) }],
  }), [cardEnter]);

  const submit = async () => {
    setBusy(true);
    setError(null);
    try {
      await login(email.trim(), password, companyCode);
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
      await verifyMfa(mfaCode.trim());
    } catch (err) {
      setError(err instanceof Error ? err.message : "Verification failed.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <Screen>
      <KeyboardAvoidingView behavior={Platform.OS === "ios" ? "padding" : undefined} style={styles.page}>
        <View style={styles.shell}>
          <TmsWorld travel={travel} />
          <LinearGradient
            colors={["rgba(2,8,16,0.04)", "rgba(2,8,16,0.12)", "rgba(2,8,16,0.94)"]}
            locations={[0, 0.46, 0.88]}
            style={StyleSheet.absoluteFill}
          />

          <Animated.View style={[styles.topLayer, heroMotion]}>
            <View style={styles.brandRow}>
              <EnterpriseMark />
              <View style={styles.brandCopy}>
                <Text style={styles.brandName}>OpsTrax</Text>
                <Text style={styles.brandProduct}>DRIVER • TMS COMMAND</Text>
              </View>
              <View style={styles.versionPill}>
                <Text style={styles.versionText}>LIVE</Text>
              </View>
            </View>

            <View style={[styles.networkRow, compact && styles.networkRowCompact]}>
              {NETWORK.map((item) => <NetworkPill key={item.label} {...item} />)}
            </View>

            <View style={styles.heroCopy}>
              <Text style={styles.heroEyebrow}>ONE DRIVER VIEW • EVERY OPERATING SIGNAL</Text>
              <Text style={styles.heroTitle}>Your load.{"\n"}Your route.{"\n"}Your command.</Text>
              <Text style={styles.heroBody}>
                A live transportation cockpit for dispatch, vehicle readiness, route execution, compliance and delivery proof.
              </Text>
            </View>

            <SignalDeck />
          </Animated.View>

          <Animated.View style={[styles.loginZone, cardMotion]}>
            <LiquidFrame style={styles.loginFrame}>
              <View style={styles.loginInner}>
                {mfaChallenge ? (
                  <>
                    <View style={styles.loginHeader}>
                      <View>
                        <Text style={styles.loginKicker}>SECURE ACCESS</Text>
                        <Text style={styles.loginTitle}>Verify your session</Text>
                      </View>
                      <View style={styles.lockPill}><Text style={styles.lockPillText}>MFA</Text></View>
                    </View>
                    <Text style={styles.loginSubtitle}>Enter the authenticator code for {mfaChallenge.email}.</Text>
                    <FieldBox
                      icon="••"
                      value={mfaCode}
                      onChangeText={setMfaCode}
                      placeholder="6-digit code"
                      keyboardType="numeric"
                      autoComplete="one-time-code"
                      textContentType="oneTimeCode"
                    />
                    <PrimaryButton label={busy ? "Verifying…" : "Verify & Continue"} disabled={busy || !/^\d{6}$/.test(mfaCode)} onPress={() => void submitMfa()} />
                    <Pressable onPress={() => { cancelMfa(); setMfaCode(""); }}>
                      <Text style={styles.secondaryAction}>Use another account</Text>
                    </Pressable>
                  </>
                ) : (
                  <>
                    <View style={styles.loginHeader}>
                      <View style={{ flex: 1 }}>
                        <Text style={styles.loginKicker}>DRIVER COMMAND CENTER</Text>
                        <Text style={styles.loginTitle}>Welcome back</Text>
                        <Text style={styles.loginSubtitle}>Sign in to your live OpsTrax workspace.</Text>
                      </View>
                      <View style={styles.companyBox}>
                        <Text style={styles.companyLabel}>FLEET CODE</Text>
                        <TextInput
                          accessibilityLabel="Organization code"
                          value={companyCode}
                          onChangeText={(value) => setCompanyCode(value.trimStart().toUpperCase())}
                          placeholder="OPX-DEMO"
                          placeholderTextColor="#7890a8"
                          autoCapitalize="characters"
                          autoCorrect={false}
                          style={styles.companyInput}
                        />
                      </View>
                    </View>

                    <FieldBox
                      icon="@"
                      value={email}
                      onChangeText={setEmail}
                      placeholder="Driver email"
                      keyboardType="email-address"
                      autoCapitalize="none"
                      autoComplete="email"
                      textContentType="emailAddress"
                    />
                    <FieldBox
                      icon="◆"
                      value={password}
                      onChangeText={setPassword}
                      placeholder="Password"
                      secureTextEntry
                      autoComplete="password"
                      textContentType="password"
                    />

                    <View style={styles.securityRail}>
                      <View style={styles.securityBadge}><Text style={styles.securityBadgeText}>AES</Text></View>
                      <View style={{ flex: 1 }}>
                        <Text style={styles.securityTitle}>Tenant-isolated secure session</Text>
                        <Text style={styles.securityBody}>Credentials and operational data stay scoped to your fleet.</Text>
                      </View>
                    </View>

                    <PrimaryButton
                      label={busy ? "Connecting to OpsTrax…" : "Enter Driver Command"}
                      disabled={busy || !companyCode.trim() || !email.trim() || !password}
                      onPress={() => void submit()}
                    />
                  </>
                )}
              </View>
            </LiquidFrame>
          </Animated.View>

          <View style={styles.bottomBrand}>
            <Text style={styles.bottomBrandStrong}>OpsTrax</Text>
            <Text style={styles.bottomBrandCopy}>CONNECTED FLEETS • SAFER ROADS • STRONGER OPERATIONS</Text>
          </View>
        </View>
        {error ? <ErrorState title="Unable to sign in" body={error} /> : null}
      </KeyboardAvoidingView>
    </Screen>
  );
}

type FieldBoxProps = {
  icon: string;
  value: string;
  onChangeText: (value: string) => void;
  placeholder: string;
  secureTextEntry?: boolean;
  keyboardType?: "default" | "email-address" | "numeric";
  autoCapitalize?: "none" | "sentences" | "words" | "characters";
  autoComplete?: "email" | "password" | "one-time-code";
  textContentType?: "emailAddress" | "password" | "oneTimeCode";
};

function FieldBox(props: FieldBoxProps) {
  const { icon, ...input } = props;
  return (
    <View style={styles.fieldBox}>
      <LinearGradient colors={["rgba(93,232,245,0.18)", "rgba(89,110,255,0.08)"]} style={styles.fieldIcon}>
        <Text style={styles.fieldIconText}>{icon}</Text>
      </LinearGradient>
      <TextInput
        {...input}
        placeholderTextColor="#8da5bc"
        autoCorrect={false}
        style={styles.fieldInput}
      />
    </View>
  );
}

function PrimaryButton({ label, disabled, onPress }: { label: string; disabled: boolean; onPress: () => void }) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={label}
      onPress={onPress}
      disabled={disabled}
      style={({ pressed }) => [styles.primaryButton, pressed && !disabled && { transform: [{ scale: 0.992 }] }, disabled && styles.primaryDisabled]}
    >
      <LinearGradient
        colors={["#50e8ef", "#2388ff", "#6359ff"]}
        start={{ x: 0, y: 0.5 }}
        end={{ x: 1, y: 0.5 }}
        style={StyleSheet.absoluteFill}
      />
      <View pointerEvents="none" style={styles.primarySheen} />
      <Text style={styles.primaryText}>{label}</Text>
      <View style={styles.primaryArrow}><Text style={styles.primaryArrowText}>→</Text></View>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  page: { paddingBottom: 18 },
  shell: {
    minHeight: 1050,
    overflow: "hidden",
    borderRadius: 34,
    borderWidth: 1,
    borderColor: "rgba(115,215,255,0.20)",
    backgroundColor: "#020914",
    paddingBottom: 52,
  },
  world: { position: "absolute", left: 0, right: 0, top: 0, height: 690, overflow: "hidden" },
  auroraOne: { position: "absolute", width: 360, height: 360, borderRadius: 360, backgroundColor: "#1b8fae", opacity: 0.17, left: -120, top: -160, shadowColor: "#55ecff", shadowOpacity: 0.42, shadowRadius: 90 },
  auroraTwo: { position: "absolute", width: 320, height: 320, borderRadius: 320, backgroundColor: "#684dff", opacity: 0.12, right: -140, top: 40, shadowColor: "#8e79ff", shadowOpacity: 0.35, shadowRadius: 86 },
  gridPlane: { position: "absolute", left: "7%", right: "7%", top: 220, height: 290, opacity: 0.22, transform: [{ perspective: 420 }, { rotateX: "61deg" }, { scaleX: 1.2 }] },
  gridH: { position: "absolute", left: 0, right: 0, height: 1, backgroundColor: "#5edce7" },
  gridV: { position: "absolute", top: 0, bottom: 0, width: 1, backgroundColor: "#5b9cff" },
  routeBeam: { position: "absolute", height: 4, borderRadius: 9, backgroundColor: "rgba(88,241,228,0.78)", shadowColor: "#5af8ed", shadowOpacity: 0.8, shadowRadius: 14 },
  routeA: { width: "35%", left: "9%", top: 370, transform: [{ rotate: "-25deg" }] },
  routeB: { width: "33%", left: "36%", top: 342, transform: [{ rotate: "19deg" }] },
  routeC: { width: "31%", left: "62%", top: 318, transform: [{ rotate: "-28deg" }] },
  routeNode: { position: "absolute", width: 14, height: 14, borderRadius: 14, backgroundColor: "#f9ffff", borderWidth: 3, borderColor: "#65f3e4", shadowColor: "#65f3e4", shadowOpacity: 1, shadowRadius: 13 },
  routeRunner: { position: "absolute", left: "24%", top: 354, width: 54, height: 4, borderRadius: 8, backgroundColor: "#ffffff", shadowColor: "#74f5ed", shadowOpacity: 1, shadowRadius: 14 },
  roadDeck: { position: "absolute", left: "-12%", right: "-12%", height: 250, bottom: -58, transform: [{ perspective: 420 }, { rotateX: "60deg" }], borderTopLeftRadius: 80, borderTopRightRadius: 80, overflow: "hidden" },
  roadStripe: { position: "absolute", top: 24, bottom: 0, width: 3, backgroundColor: "rgba(128,213,255,0.34)" },
  truckStage: { position: "absolute", right: 26, top: 300, width: 300, height: 200 },
  truckShadow: { position: "absolute", left: 14, right: 8, bottom: 22, height: 24, borderRadius: 40, backgroundColor: "rgba(0,0,0,0.46)", transform: [{ skewX: "-18deg" }] },
  truckTrailer: { position: "absolute", right: 0, top: 24, width: 188, height: 116, borderRadius: 13, borderWidth: 1, borderColor: "rgba(139,222,248,0.38)", overflow: "hidden", shadowColor: "#2d9bc3", shadowOpacity: 0.28, shadowRadius: 22 },
  trailerEdge: { position: "absolute", top: 6, bottom: 6, left: 9, width: 2, backgroundColor: "rgba(255,255,255,0.28)" },
  trailerBadge: { position: "absolute", right: 14, top: 16, borderRadius: 10, borderWidth: 1, borderColor: "rgba(132,242,245,0.30)", paddingHorizontal: 8, paddingVertical: 5, backgroundColor: "rgba(1,11,20,0.24)" },
  trailerBadgeText: { color: "#a8f8f2", fontSize: 7.5, fontWeight: "900", letterSpacing: 1.5 },
  truckCab: { position: "absolute", left: 0, top: 58, width: 124, height: 104, borderTopLeftRadius: 38, borderTopRightRadius: 24, borderBottomLeftRadius: 19, borderBottomRightRadius: 13, borderWidth: 1, borderColor: "rgba(125,227,245,0.40)", overflow: "hidden", shadowColor: "#4cd9eb", shadowOpacity: 0.28, shadowRadius: 18 },
  cabRoof: { position: "absolute", left: 20, top: 8, width: 76, height: 8, borderRadius: 8, backgroundColor: "rgba(129,240,245,0.28)" },
  cabGlass: { position: "absolute", left: 18, right: 22, top: 25, height: 32, borderRadius: 8, borderWidth: 1, borderColor: "rgba(165,237,255,0.26)" },
  cabGrille: { position: "absolute", left: 18, bottom: 18, width: 62, height: 25, borderRadius: 9, borderWidth: 2, borderColor: "rgba(178,235,247,0.26)" },
  wheel: { position: "absolute", width: 36, height: 36, borderRadius: 36, bottom: 8, backgroundColor: "#010409", borderWidth: 7, borderColor: "#16293a", shadowColor: "#000", shadowOpacity: 0.8, shadowRadius: 8 },
  light: { position: "absolute", top: 124, width: 20, height: 9, borderRadius: 6, backgroundColor: "#81f9ff", shadowColor: "#81f9ff", shadowOpacity: 1, shadowRadius: 14 },
  hudCorner: { position: "absolute", left: 26, top: 305, width: 172 },
  hudKicker: { color: "#66f1df", fontSize: 8, fontWeight: "900", letterSpacing: 2.1 },
  hudTitle: { color: "#f6fbff", fontSize: 18, fontWeight: "900", letterSpacing: -0.4, marginTop: 4 },
  hudBody: { color: "#8ea9bd", fontSize: 10, lineHeight: 15, marginTop: 4 },
  topLayer: { paddingHorizontal: 22, paddingTop: 24, zIndex: 2 },
  brandRow: { flexDirection: "row", alignItems: "center", gap: 12 },
  brandCopy: { flex: 1 },
  brandName: { color: "#f8fcff", fontSize: 28, lineHeight: 30, fontWeight: "900", letterSpacing: -1.2 },
  brandProduct: { color: "#68efe2", fontSize: 8.5, fontWeight: "900", letterSpacing: 2.4, marginTop: 3 },
  versionPill: { borderRadius: 999, borderWidth: 1, borderColor: "rgba(104,239,226,0.34)", backgroundColor: "rgba(104,239,226,0.09)", paddingHorizontal: 10, paddingVertical: 6 },
  versionText: { color: "#76f4e8", fontSize: 8, fontWeight: "900", letterSpacing: 1.6 },
  networkRow: { flexDirection: "row", gap: 8, marginTop: 17 },
  networkRowCompact: { flexWrap: "wrap" },
  networkPill: { flex: 1, minWidth: 96, flexDirection: "row", alignItems: "center", gap: 7, borderRadius: 15, borderWidth: 1, borderColor: "rgba(119,186,231,0.20)", backgroundColor: "rgba(8,25,43,0.58)", paddingHorizontal: 9, paddingVertical: 8 },
  networkDot: { width: 7, height: 7, borderRadius: 7, backgroundColor: "#5ff3dd", shadowColor: "#5ff3dd", shadowOpacity: 0.9, shadowRadius: 7 },
  networkLabel: { color: "#7f99ad", fontSize: 7, fontWeight: "800", letterSpacing: 1.1 },
  networkValue: { color: "#e8f6ff", fontSize: 9, fontWeight: "900", letterSpacing: 0.5, marginTop: 1 },
  heroCopy: { marginTop: 34, maxWidth: 410 },
  heroEyebrow: { color: "#64eede", fontSize: 9, fontWeight: "900", letterSpacing: 2.0 },
  heroTitle: { color: "#f8fcff", fontSize: 44, lineHeight: 45, fontWeight: "900", letterSpacing: -2.0, marginTop: 8, textShadowColor: "rgba(0,0,0,0.55)", textShadowRadius: 10 },
  heroBody: { color: "#b4c9da", fontSize: 13.5, lineHeight: 20, marginTop: 11, maxWidth: 370 },
  signalDeck: { flexDirection: "row", gap: 8, marginTop: 300 },
  signalFrame: { flex: 1, minWidth: 0, borderRadius: 20 },
  signalInner: { minHeight: 112, alignItems: "center", justifyContent: "center", padding: 10 },
  signalCode: { width: 34, height: 34, borderRadius: 12, borderWidth: 1, alignItems: "center", justifyContent: "center", marginBottom: 8 },
  signalCodeText: { fontSize: 12, fontWeight: "900", letterSpacing: 0.5 },
  signalLabel: { color: "#f5fbff", fontSize: 11.5, fontWeight: "900" },
  signalValue: { color: "#8199ae", fontSize: 8.5, lineHeight: 12, textAlign: "center", fontWeight: "700", marginTop: 4 },
  glassBorder: { padding: 1, borderRadius: 28, overflow: "hidden", shadowColor: "#2fa9ff", shadowOpacity: 0.25, shadowRadius: 24, shadowOffset: { width: 0, height: 12 } },
  glassBlur: { flex: 1, borderRadius: 27, overflow: "hidden" },
  glassFill: { flex: 1, borderRadius: 27, overflow: "hidden" },
  glassSpecular: { position: "absolute", top: 1, left: 22, right: 22, height: 1, backgroundColor: "rgba(255,255,255,0.62)" },
  loginZone: { marginTop: 18, paddingHorizontal: 18, zIndex: 3 },
  loginFrame: { minHeight: 420 },
  loginInner: { paddingHorizontal: 20, paddingVertical: 22, gap: 14 },
  loginHeader: { flexDirection: "row", alignItems: "flex-start", gap: 14 },
  loginKicker: { color: "#66f0e0", fontSize: 8, fontWeight: "900", letterSpacing: 2.0 },
  loginTitle: { color: "#f8fcff", fontSize: 28, lineHeight: 31, fontWeight: "900", letterSpacing: -1.0, marginTop: 4 },
  loginSubtitle: { color: "#aabfd1", fontSize: 12.5, lineHeight: 18, marginTop: 4 },
  companyBox: { width: 112, borderRadius: 17, borderWidth: 1, borderColor: "rgba(101,221,235,0.28)", backgroundColor: "rgba(255,255,255,0.045)", paddingHorizontal: 11, paddingVertical: 9 },
  companyLabel: { color: "#69dfe3", fontSize: 7, fontWeight: "900", letterSpacing: 1.3 },
  companyInput: { color: "#f5fbff", fontSize: 11, fontWeight: "900", padding: 0, marginTop: 4 },
  lockPill: { borderRadius: 15, borderWidth: 1, borderColor: "rgba(121,222,241,0.26)", backgroundColor: "rgba(121,222,241,0.08)", paddingHorizontal: 12, paddingVertical: 8 },
  lockPillText: { color: "#7de6ee", fontSize: 9, fontWeight: "900", letterSpacing: 1.5 },
  fieldBox: { minHeight: 60, flexDirection: "row", alignItems: "center", gap: 11, borderRadius: 18, borderWidth: 1, borderColor: "rgba(116,177,217,0.23)", backgroundColor: "rgba(255,255,255,0.052)", paddingHorizontal: 11 },
  fieldIcon: { width: 38, height: 38, borderRadius: 13, alignItems: "center", justifyContent: "center", borderWidth: 1, borderColor: "rgba(111,224,240,0.18)" },
  fieldIconText: { color: "#bff9ff", fontSize: 13, fontWeight: "900" },
  fieldInput: { flex: 1, color: "#f7fbff", fontSize: 16, paddingVertical: 14 },
  securityRail: { flexDirection: "row", gap: 10, alignItems: "center", borderRadius: 17, borderWidth: 1, borderColor: "rgba(103,168,219,0.16)", backgroundColor: "rgba(5,16,28,0.38)", padding: 11 },
  securityBadge: { width: 38, height: 38, borderRadius: 13, borderWidth: 1, borderColor: "rgba(98,240,223,0.32)", backgroundColor: "rgba(98,240,223,0.08)", alignItems: "center", justifyContent: "center" },
  securityBadgeText: { color: "#6cf1df", fontSize: 9, fontWeight: "900", letterSpacing: 0.9 },
  securityTitle: { color: "#e7f4fc", fontSize: 10.5, fontWeight: "900" },
  securityBody: { color: "#819aaf", fontSize: 8.5, lineHeight: 12, marginTop: 2 },
  primaryButton: { height: 62, borderRadius: 31, overflow: "hidden", alignItems: "center", justifyContent: "center", shadowColor: "#297dff", shadowOpacity: 0.42, shadowRadius: 18, shadowOffset: { width: 0, height: 10 } },
  primaryDisabled: { opacity: 0.45 },
  primarySheen: { position: "absolute", top: 0, left: 28, right: 28, height: 1, backgroundColor: "rgba(255,255,255,0.72)" },
  primaryText: { color: "#ffffff", fontSize: 15.5, fontWeight: "900", letterSpacing: -0.2 },
  primaryArrow: { position: "absolute", right: 7, width: 48, height: 48, borderRadius: 24, backgroundColor: "rgba(255,255,255,0.18)", alignItems: "center", justifyContent: "center" },
  primaryArrowText: { color: "#ffffff", fontSize: 24, fontWeight: "400" },
  secondaryAction: { color: "#6ee8ed", textAlign: "center", fontSize: 12, fontWeight: "800" },
  bottomBrand: { alignItems: "center", gap: 5, marginTop: 24, paddingHorizontal: 22, zIndex: 3 },
  bottomBrandStrong: { color: "#f1f8fc", fontSize: 17, fontWeight: "900", letterSpacing: -0.3 },
  bottomBrandCopy: { color: "#718ba1", fontSize: 7.5, fontWeight: "800", letterSpacing: 1.7, textAlign: "center" },
});
