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
import { ErrorState, Input, Screen, colors } from "@/components/ui";
import { APP_VARIANT, STAGE_LABEL } from "@/config";
import { useSession } from "@/auth/SessionProvider";

const CAPABILITIES = [
  { icon: "▣", label: "Assignments", detail: "YOUR ROUTES" },
  { icon: "◆", label: "DVIR", detail: "STAY COMPLIANT" },
  { icon: "◉", label: "POD", detail: "CAPTURE & SUBMIT" },
  { icon: "➤", label: "Dispatch", detail: "ALWAYS CONNECTED" },
];

const BENEFITS = [
  { icon: "◇", top: "SAFER", bottom: "DRIVERS" },
  { icon: "▥", top: "HIGHER", bottom: "EFFICIENCY" },
  { icon: "◒", top: "LOWER", bottom: "EMISSIONS" },
  { icon: "●●", top: "STRONGER", bottom: "BUSINESSES" },
];

function OpsTraxMark({ pulse }: { pulse: Animated.Value }) {
  const scale = pulse.interpolate({ inputRange: [0, 1], outputRange: [1, 1.055] });
  const halo = pulse.interpolate({ inputRange: [0, 1], outputRange: [0.2, 0.56] });
  return (
    <Animated.View style={[styles.markStage, { transform: [{ scale }] }]}>
      <Animated.View style={[styles.markHalo, { opacity: halo }]} />
      <LinearGradient colors={["#64f5ec", "#18cdd0"]} style={[styles.markFace, styles.markTop]} />
      <LinearGradient colors={["#0f8d96", "#063c56"]} style={[styles.markFace, styles.markLeft]} />
      <LinearGradient colors={["#4c86ff", "#123c8f"]} style={[styles.markFace, styles.markRight]} />
      <View style={styles.markStem} />
      <View style={[styles.markArm, styles.markArmLeft]} />
      <View style={[styles.markArm, styles.markArmRight]} />
      <View style={[styles.markDot, styles.markDotTop]} />
      <View style={[styles.markDot, styles.markDotLeft]} />
      <View style={[styles.markDot, styles.markDotRight]} />
    </Animated.View>
  );
}

function CinematicScene({ drift }: { drift: Animated.Value }) {
  const roadShift = drift.interpolate({ inputRange: [0, 1], outputRange: [-8, 10] });
  const headlight = drift.interpolate({ inputRange: [0, 1], outputRange: [0.35, 0.75] });
  return (
    <View pointerEvents="none" style={styles.scene}>
      <LinearGradient
        colors={["#071a2e", "#14334a", "#1f4d63", "#092038"]}
        locations={[0, 0.4, 0.68, 1]}
        style={StyleSheet.absoluteFill}
      />
      <View style={styles.cloudOne} />
      <View style={styles.cloudTwo} />
      <View style={styles.sunGlow} />
      <View style={[styles.mountain, styles.mountainBackOne]} />
      <View style={[styles.mountain, styles.mountainBackTwo]} />
      <View style={[styles.mountain, styles.mountainFrontOne]} />
      <View style={[styles.mountain, styles.mountainFrontTwo]} />
      <View style={styles.cityLine}>
        {[18, 30, 46, 24, 60, 36, 50, 22, 42, 68, 28, 54, 34].map((height, index) => (
          <View key={`${height}-${index}`} style={[styles.cityBuilding, { height, opacity: 0.46 + (index % 3) * 0.12 }]} />
        ))}
      </View>
      <Animated.View style={[styles.road, { transform: [{ perspective: 700 }, { rotateX: "63deg" }, { translateX: roadShift }] }]}>
        <LinearGradient colors={["rgba(255,255,255,0.02)", "rgba(31,115,174,0.2)", "rgba(255,255,255,0.02)"]} style={StyleSheet.absoluteFill} />
        <View style={[styles.lane, { left: "31%" }]} />
        <View style={[styles.lane, { left: "49%" }]} />
        <View style={[styles.lane, { left: "67%" }]} />
      </Animated.View>
      <View style={styles.guardRail} />
      <View style={styles.truck}>
        <LinearGradient colors={["#173d53", "#06121e"]} style={styles.trailer} />
        <LinearGradient colors={["#1a536a", "#071724"]} style={styles.cab} />
        <View style={styles.windshield} />
        <View style={styles.grille} />
        <View style={[styles.wheel, styles.wheelFront]} />
        <View style={[styles.wheel, styles.wheelRearOne]} />
        <View style={[styles.wheel, styles.wheelRearTwo]} />
        <Animated.View style={[styles.headlight, styles.headlightLeft, { opacity: headlight }]} />
        <Animated.View style={[styles.headlight, styles.headlightRight, { opacity: headlight }]} />
      </View>
    </View>
  );
}

function CapabilityTiles() {
  return (
    <View style={styles.capabilityRow}>
      {CAPABILITIES.map((item) => (
        <LinearGradient key={item.label} colors={["rgba(17,51,78,0.95)", "rgba(7,25,44,0.92)"]} style={styles.capabilityCard}>
          <Text style={styles.capabilityIcon}>{item.icon}</Text>
          <Text style={styles.capabilityLabel}>{item.label}</Text>
          <Text style={styles.capabilityDetail}>{item.detail}</Text>
        </LinearGradient>
      ))}
    </View>
  );
}

function BenefitsRow() {
  return (
    <View style={styles.benefitsRow}>
      {BENEFITS.map((item, index) => (
        <View key={item.top} style={styles.benefitWrap}>
          {index > 0 ? <View style={styles.benefitDivider} /> : null}
          <View style={styles.benefitItem}>
            <Text style={styles.benefitIcon}>{item.icon}</Text>
            <Text style={styles.benefitTop}>{item.top}</Text>
            <Text style={styles.benefitBottom}>{item.bottom}</Text>
          </View>
        </View>
      ))}
    </View>
  );
}

function TelemetryWave() {
  return (
    <View style={styles.waveWrap} pointerEvents="none">
      {[0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10].map((row) => (
        <View key={row} style={[styles.waveRow, { bottom: row * 6, opacity: 0.18 + row * 0.025 }]}>
          {[0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15].map((dot) => (
            <View key={dot} style={[styles.waveDot, { transform: [{ translateY: Math.sin((dot + row) * 0.8) * 7 }] }]} />
          ))}
        </View>
      ))}
    </View>
  );
}

export function LoginScreen() {
  const { width } = useWindowDimensions();
  const compact = width < 760;
  const { login, verifyMfa, cancelMfa, mfaChallenge, authError } = useSession();
  const previewDefault = ["preview", "pilot"].includes(STAGE_LABEL.toLowerCase()) ? "OPX-DEMO" : "";
  const [companyCode, setCompanyCode] = useState(previewDefault);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [mfaCode, setMfaCode] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(authError);
  const [intro] = useState(() => new Animated.Value(0));
  const [panelIn] = useState(() => new Animated.Value(0));
  const [pulse] = useState(() => new Animated.Value(0));
  const [drift] = useState(() => new Animated.Value(0));
  const isDriver = APP_VARIANT === "driver";

  useEffect(() => {
    Animated.parallel([
      Animated.timing(intro, { toValue: 1, duration: 680, easing: Easing.out(Easing.cubic), useNativeDriver: true }),
      Animated.timing(panelIn, { toValue: 1, duration: 620, delay: 180, easing: Easing.out(Easing.cubic), useNativeDriver: true }),
    ]).start();
    const pulseLoop = Animated.loop(Animated.sequence([
      Animated.timing(pulse, { toValue: 1, duration: 2200, easing: Easing.inOut(Easing.sin), useNativeDriver: true }),
      Animated.timing(pulse, { toValue: 0, duration: 2200, easing: Easing.inOut(Easing.sin), useNativeDriver: true }),
    ]));
    const driftLoop = Animated.loop(Animated.sequence([
      Animated.timing(drift, { toValue: 1, duration: 4400, easing: Easing.inOut(Easing.sin), useNativeDriver: true }),
      Animated.timing(drift, { toValue: 0, duration: 4400, easing: Easing.inOut(Easing.sin), useNativeDriver: true }),
    ]));
    pulseLoop.start();
    driftLoop.start();
    return () => { pulseLoop.stop(); driftLoop.stop(); };
  }, [drift, intro, panelIn, pulse]);

  const heroMotion = useMemo(() => ({
    opacity: intro,
    transform: [{ translateY: intro.interpolate({ inputRange: [0, 1], outputRange: [12, 0] }) }],
  }), [intro]);
  const panelMotion = useMemo(() => ({
    opacity: panelIn,
    transform: [{ translateY: panelIn.interpolate({ inputRange: [0, 1], outputRange: [22, 0] }) }],
  }), [panelIn]);

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
      <KeyboardAvoidingView behavior={Platform.OS === "ios" ? "padding" : undefined} style={styles.page}>
        <Animated.View style={[styles.shell, heroMotion]}>
          <LinearGradient colors={["#061524", "#03111e", "#020a12"]} locations={[0, 0.46, 1]} style={StyleSheet.absoluteFill} />

          <View style={[styles.hero, compact && styles.heroCompact]}>
            <CinematicScene drift={drift} />
            <View style={styles.heroShade} />
            <View style={styles.heroTopCopy}>
              <View style={styles.brandBlock}>
                <OpsTraxMark pulse={pulse} />
                <View style={{ marginTop: -8 }}>
                  <Text style={styles.brandName}>OpsTrax</Text>
                  <Text style={styles.brandProduct}>{isDriver ? "DRIVER" : "MOBILE"}</Text>
                </View>
                <Text style={styles.tagline}>Connected Fleets.{"\n"}Stronger Businesses.</Text>
              </View>

              <View style={styles.promiseBlock}>
                <Text style={styles.promiseKicker}>DRIVE{"\n"}DELIVER</Text>
                <Text style={styles.promiseTitle}>A SMARTER TOMORROW</Text>
                <BlurView intensity={28} tint="dark" style={styles.promiseGlass}>
                  <Text style={styles.promiseSmall}>REAL-TIME{"\n"}VISIBILITY</Text>
                  <Text style={styles.promiseStrong}>SAFER ROADS{"\n"}GREATER OPPORTUNITIES</Text>
                </BlurView>
              </View>
            </View>
          </View>

          <View style={styles.tilesWrap}><CapabilityTiles /></View>

          <Animated.View style={[styles.loginWrap, panelMotion]}>
            {Platform.OS === "ios" ? (
              <BlurView intensity={46} tint="dark" style={styles.loginGlass}>
                <SignInPanel
                  mfaChallenge={mfaChallenge}
                  companyCode={companyCode}
                  setCompanyCode={setCompanyCode}
                  email={email}
                  setEmail={setEmail}
                  password={password}
                  setPassword={setPassword}
                  mfaCode={mfaCode}
                  setMfaCode={setMfaCode}
                  busy={busy}
                  submit={submit}
                  submitMfa={submitMfa}
                  cancelMfa={cancelMfa}
                />
              </BlurView>
            ) : (
              <View style={styles.loginGlass}>
                <SignInPanel
                  mfaChallenge={mfaChallenge}
                  companyCode={companyCode}
                  setCompanyCode={setCompanyCode}
                  email={email}
                  setEmail={setEmail}
                  password={password}
                  setPassword={setPassword}
                  mfaCode={mfaCode}
                  setMfaCode={setMfaCode}
                  busy={busy}
                  submit={submit}
                  submitMfa={submitMfa}
                  cancelMfa={cancelMfa}
                />
              </View>
            )}
          </Animated.View>

          <BenefitsRow />
          <View style={styles.footerBrandRow}>
            <Text style={styles.footerBrand}>OpsTrax</Text>
            <View style={styles.footerDivider} />
            <Text style={styles.footerCopy}>POWERING A CONNECTED TOMORROW</Text>
          </View>
          <TelemetryWave />
        </Animated.View>
        {error ? <ErrorState title="Unable to sign in" body={error} /> : null}
      </KeyboardAvoidingView>
    </Screen>
  );
}

type SignInPanelProps = {
  mfaChallenge: { email: string } | null;
  companyCode: string;
  setCompanyCode: (value: string) => void;
  email: string;
  setEmail: (value: string) => void;
  password: string;
  setPassword: (value: string) => void;
  mfaCode: string;
  setMfaCode: (value: string) => void;
  busy: boolean;
  submit: () => Promise<void>;
  submitMfa: () => Promise<void>;
  cancelMfa: () => void;
};

function SignInPanel(props: SignInPanelProps) {
  const {
    mfaChallenge,
    companyCode,
    setCompanyCode,
    email,
    setEmail,
    password,
    setPassword,
    mfaCode,
    setMfaCode,
    busy,
    submit,
    submitMfa,
    cancelMfa,
  } = props;

  if (mfaChallenge) {
    return (
      <View style={styles.loginInner}>
        <Text style={styles.loginTitle}>Verify <Text style={styles.loginAccent}>Access</Text></Text>
        <Text style={styles.loginSubtitle}>Enter the authenticator code for {mfaChallenge.email}.</Text>
        <Input label="Authenticator code" value={mfaCode} onChangeText={setMfaCode} placeholder="123456" keyboardType="numeric" autoComplete="one-time-code" textContentType="oneTimeCode" />
        <Pressable accessibilityRole="button" onPress={() => void submitMfa()} disabled={busy || !/^\d{6}$/.test(mfaCode)} style={({ pressed }) => [styles.signButton, pressed && { opacity: 0.9 }]}>
          <LinearGradient colors={["#26d9f0", "#1683ff", "#1c54ed"]} style={StyleSheet.absoluteFill} />
          <Text style={styles.signButtonText}>{busy ? "Verifying…" : "Verify & Continue"}</Text>
          <Text style={styles.signButtonArrow}>→</Text>
        </Pressable>
        <Pressable onPress={() => { cancelMfa(); setMfaCode(""); }}><Text style={styles.secondaryAction}>Use another account</Text></Pressable>
      </View>
    );
  }

  return (
    <View style={styles.loginInner}>
      <View style={styles.loginHeaderRow}>
        <View style={{ flex: 1 }}>
          <Text style={styles.loginTitle}>Welcome <Text style={styles.loginAccent}>Back</Text></Text>
          <Text style={styles.loginSubtitle}>Sign in to your OpsTrax Driver account</Text>
        </View>
        <View style={styles.companyPill}>
          <Text style={styles.companyPillLabel}>FLEET</Text>
          <TextInput
            value={companyCode}
            onChangeText={(value) => setCompanyCode(value.trimStart().toUpperCase())}
            placeholder="OPX-DEMO"
            placeholderTextColor="#7f95aa"
            autoCapitalize="characters"
            autoCorrect={false}
            style={styles.companyPillInput}
          />
        </View>
      </View>

      <View style={styles.fieldWrap}>
        <Text style={styles.fieldIcon}>✉</Text>
        <TextInput
          value={email}
          onChangeText={setEmail}
          placeholder="Email address"
          placeholderTextColor="#91a6bb"
          keyboardType="email-address"
          autoCapitalize="none"
          autoComplete="email"
          textContentType="emailAddress"
          style={styles.fieldInput}
        />
      </View>
      <View style={styles.fieldWrap}>
        <Text style={styles.fieldIcon}>▣</Text>
        <TextInput
          value={password}
          onChangeText={setPassword}
          placeholder="Password"
          placeholderTextColor="#91a6bb"
          secureTextEntry
          autoComplete="password"
          textContentType="password"
          style={styles.fieldInput}
        />
      </View>

      <View style={styles.securityRow}>
        <View style={styles.securityBox} />
        <Text style={styles.securityText}>Secure encrypted session</Text>
        <Text style={styles.securityLink}>Fleet-isolated access</Text>
      </View>

      <Pressable
        accessibilityRole="button"
        onPress={() => void submit()}
        disabled={busy || !companyCode.trim() || !email.trim() || !password}
        style={({ pressed }) => [styles.signButton, pressed && { opacity: 0.9 }, (busy || !companyCode.trim() || !email.trim() || !password) && styles.signButtonDisabled]}
      >
        <LinearGradient colors={["#23dbef", "#1779ff", "#1856ea"]} start={{ x: 0, y: 0.5 }} end={{ x: 1, y: 0.5 }} style={StyleSheet.absoluteFill} />
        <Text style={styles.signButtonText}>{busy ? "Signing In…" : "Sign In"}</Text>
        <View style={styles.signButtonCircle}><Text style={styles.signButtonArrow}>→</Text></View>
      </Pressable>

      <View style={styles.trustRow}>
        <Text style={styles.trustIcon}>◇</Text>
        <Text style={styles.trustText}>Secure fleet access. Your data stays protected.</Text>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  page: { paddingBottom: 22 },
  shell: { overflow: "hidden", borderRadius: 32, borderWidth: 1, borderColor: "rgba(67,188,255,0.18)", backgroundColor: "#03101c", paddingBottom: 118 },
  hero: { minHeight: 520, overflow: "hidden", position: "relative" },
  heroCompact: { minHeight: 490 },
  scene: { position: "absolute", left: 0, right: 0, top: 0, bottom: 0, overflow: "hidden" },
  heroShade: { position: "absolute", left: 0, right: 0, top: 0, bottom: 0, backgroundColor: "rgba(0,12,24,0.22)" },
  cloudOne: { position: "absolute", width: 390, height: 100, borderRadius: 80, backgroundColor: "rgba(36,59,77,0.42)", left: -40, top: 24, transform: [{ rotate: "-5deg" }] },
  cloudTwo: { position: "absolute", width: 280, height: 78, borderRadius: 70, backgroundColor: "rgba(46,65,82,0.34)", right: -30, top: 84, transform: [{ rotate: "7deg" }] },
  sunGlow: { position: "absolute", width: 220, height: 220, borderRadius: 110, backgroundColor: "rgba(255,191,108,0.23)", right: -40, top: 152, shadowColor: "#ffc477", shadowOpacity: 0.7, shadowRadius: 55 },
  mountain: { position: "absolute", width: 320, height: 190, backgroundColor: "#0b2134", transform: [{ rotate: "45deg" }] },
  mountainBackOne: { left: -80, top: 180, opacity: 0.8 },
  mountainBackTwo: { left: 120, top: 165, opacity: 0.84 },
  mountainFrontOne: { left: 260, top: 198, backgroundColor: "#081929" },
  mountainFrontTwo: { right: -100, top: 184, backgroundColor: "#071522" },
  cityLine: { position: "absolute", left: 28, right: 28, bottom: 122, height: 80, flexDirection: "row", alignItems: "flex-end", gap: 5 },
  cityBuilding: { width: 9, borderTopLeftRadius: 2, borderTopRightRadius: 2, backgroundColor: "#6bb7d6", shadowColor: "#5fdcf1", shadowOpacity: 0.38, shadowRadius: 6 },
  road: { position: "absolute", width: "120%", height: 330, left: "-10%", bottom: -138, borderTopLeftRadius: 90, borderTopRightRadius: 90, overflow: "hidden", backgroundColor: "rgba(3,11,20,0.88)" },
  lane: { position: "absolute", top: 10, bottom: 0, width: 4, backgroundColor: "rgba(130,217,255,0.34)" },
  guardRail: { position: "absolute", left: 0, right: 0, bottom: 112, height: 4, backgroundColor: "rgba(155,215,240,0.34)" },
  truck: { position: "absolute", right: "7%", bottom: 92, width: 265, height: 170 },
  trailer: { position: "absolute", right: 0, top: 18, width: 168, height: 110, borderWidth: 1, borderColor: "rgba(116,201,232,0.28)", borderRadius: 8 },
  cab: { position: "absolute", left: 0, top: 45, width: 118, height: 102, borderTopLeftRadius: 34, borderTopRightRadius: 20, borderBottomLeftRadius: 18, borderBottomRightRadius: 16, borderWidth: 1, borderColor: "rgba(98,211,241,0.26)" },
  windshield: { position: "absolute", left: 19, top: 59, width: 66, height: 28, borderRadius: 8, backgroundColor: "rgba(76,145,175,0.36)" },
  grille: { position: "absolute", left: 20, top: 97, width: 58, height: 26, borderRadius: 8, borderWidth: 2, borderColor: "rgba(157,220,239,0.28)" },
  wheel: { position: "absolute", width: 32, height: 32, borderRadius: 16, backgroundColor: "#02070c", borderWidth: 5, borderColor: "#142636", bottom: 6 },
  wheelFront: { left: 28 },
  wheelRearOne: { right: 66 },
  wheelRearTwo: { right: 20 },
  headlight: { position: "absolute", width: 18, height: 8, borderRadius: 5, backgroundColor: "#7ceeff", shadowColor: "#4eeaff", shadowOpacity: 1, shadowRadius: 12, top: 106 },
  headlightLeft: { left: 9 },
  headlightRight: { left: 72 },
  heroTopCopy: { position: "absolute", left: 0, right: 0, top: 0, bottom: 0, paddingHorizontal: 30, paddingTop: 28, flexDirection: "row", justifyContent: "space-between", alignItems: "flex-start" },
  brandBlock: { width: "55%" },
  brandName: { color: "#f6fbff", fontSize: 49, lineHeight: 52, fontWeight: "900", letterSpacing: -2.1, textShadowColor: "rgba(0,0,0,0.45)", textShadowRadius: 8 },
  brandProduct: { color: "#e6f5ff", fontSize: 18, fontWeight: "900", letterSpacing: 8.5, marginLeft: 45, marginTop: -2 },
  tagline: { color: "#d7e7f5", fontSize: 18, lineHeight: 24, fontWeight: "500", marginTop: 16 },
  promiseBlock: { width: "39%", alignItems: "flex-end", gap: 8 },
  promiseKicker: { color: "#d2e5f5", textAlign: "right", fontSize: 10, lineHeight: 16, letterSpacing: 2.8, fontWeight: "700" },
  promiseTitle: { color: "#d6ecff", textAlign: "right", fontSize: 11, letterSpacing: 2.4 },
  promiseGlass: { marginTop: 6, width: "100%", minHeight: 96, borderRadius: 16, borderWidth: 1, borderColor: "rgba(104,207,244,0.42)", backgroundColor: "rgba(17,41,62,0.24)", padding: 15, overflow: "hidden" },
  promiseSmall: { color: "#8fd7ff", fontSize: 10, letterSpacing: 1.8, lineHeight: 17, fontWeight: "700" },
  promiseStrong: { color: "white", fontSize: 11, letterSpacing: 1.55, lineHeight: 20, marginTop: 2 },
  markStage: { width: 138, height: 138, alignItems: "center", justifyContent: "center", marginLeft: 8 },
  markHalo: { position: "absolute", width: 116, height: 116, borderRadius: 30, backgroundColor: "#37dff0", shadowColor: "#37dff0", shadowOpacity: 0.88, shadowRadius: 28 },
  markFace: { position: "absolute", width: 72, height: 72, borderRadius: 12, borderWidth: 1, borderColor: "rgba(255,255,255,0.25)" },
  markTop: { transform: [{ rotate: "45deg" }, { translateY: -24 }] },
  markLeft: { transform: [{ rotate: "45deg" }, { translateX: -24 }, { translateY: 14 }], opacity: 0.9 },
  markRight: { transform: [{ rotate: "45deg" }, { translateX: 24 }, { translateY: 14 }], opacity: 0.9 },
  markStem: { position: "absolute", width: 8, height: 58, borderRadius: 4, backgroundColor: "white", top: 38 },
  markArm: { position: "absolute", width: 43, height: 8, borderRadius: 4, backgroundColor: "white", top: 58 },
  markArmLeft: { left: 26, transform: [{ rotate: "-34deg" }] },
  markArmRight: { right: 26, transform: [{ rotate: "34deg" }] },
  markDot: { position: "absolute", width: 15, height: 15, borderRadius: 8, backgroundColor: "#f7fbff", borderWidth: 3, borderColor: "#77f5ef" },
  markDotTop: { top: 27, left: 62 },
  markDotLeft: { top: 64, left: 22 },
  markDotRight: { top: 64, right: 22 },
  tilesWrap: { paddingHorizontal: 22, marginTop: -6 },
  capabilityRow: { flexDirection: "row", gap: 10 },
  capabilityCard: { flex: 1, minHeight: 122, borderRadius: 20, borderWidth: 1, borderColor: "rgba(100,196,245,0.34)", alignItems: "center", justifyContent: "center", overflow: "hidden" },
  capabilityIcon: { color: "#37e3f2", fontSize: 28, fontWeight: "900", marginBottom: 7, textShadowColor: "rgba(55,227,242,0.42)", textShadowRadius: 8 },
  capabilityLabel: { color: "#f3f8ff", fontSize: 14, fontWeight: "800" },
  capabilityDetail: { color: "#91a9be", fontSize: 8.5, letterSpacing: 1.4, marginTop: 8, fontWeight: "700" },
  loginWrap: { paddingHorizontal: 22, marginTop: 22 },
  loginGlass: { borderRadius: 28, borderWidth: 1.2, borderColor: "rgba(92,193,255,0.72)", backgroundColor: "rgba(10,31,49,0.62)", overflow: "hidden", shadowColor: "#2aa8ff", shadowOpacity: 0.24, shadowRadius: 24, shadowOffset: { width: 0, height: 10 } },
  loginInner: { paddingHorizontal: 24, paddingVertical: 26, gap: 16 },
  loginHeaderRow: { flexDirection: "row", alignItems: "flex-start", gap: 12 },
  loginTitle: { color: "#f7fbff", fontSize: 34, lineHeight: 38, fontWeight: "900", letterSpacing: -1.2 },
  loginAccent: { color: "#27dfe8" },
  loginSubtitle: { color: "#c1d5e6", fontSize: 15, marginTop: 4 },
  companyPill: { width: 112, borderRadius: 18, borderWidth: 1, borderColor: "rgba(115,177,219,0.38)", backgroundColor: "rgba(255,255,255,0.05)", paddingHorizontal: 12, paddingVertical: 9 },
  companyPillLabel: { color: "#72dbe9", fontSize: 7, letterSpacing: 1.4, fontWeight: "800" },
  companyPillInput: { color: "#f7fbff", fontSize: 11, fontWeight: "800", padding: 0, marginTop: 3 },
  fieldWrap: { minHeight: 64, borderRadius: 18, borderWidth: 1, borderColor: "rgba(105,162,204,0.36)", backgroundColor: "rgba(255,255,255,0.055)", flexDirection: "row", alignItems: "center", paddingHorizontal: 16, gap: 13 },
  fieldIcon: { color: "#d7ecfb", fontSize: 20, width: 24, textAlign: "center" },
  fieldInput: { flex: 1, color: "#f6fbff", fontSize: 17, paddingVertical: 15 },
  securityRow: { flexDirection: "row", alignItems: "center", gap: 9 },
  securityBox: { width: 22, height: 22, borderRadius: 6, borderWidth: 2, borderColor: "#29dfe9", backgroundColor: "rgba(41,223,233,0.08)" },
  securityText: { color: "#d7e8f5", fontSize: 12 },
  securityLink: { color: "#31e3f0", fontSize: 12, marginLeft: "auto" },
  signButton: { height: 66, borderRadius: 34, overflow: "hidden", alignItems: "center", justifyContent: "center", flexDirection: "row", shadowColor: "#1683ff", shadowOpacity: 0.42, shadowRadius: 18 },
  signButtonDisabled: { opacity: 0.48 },
  signButtonText: { color: "white", fontSize: 18, fontWeight: "800" },
  signButtonCircle: { position: "absolute", right: 8, width: 50, height: 50, borderRadius: 25, backgroundColor: "rgba(180,220,255,0.28)", alignItems: "center", justifyContent: "center" },
  signButtonArrow: { color: "white", fontSize: 27, fontWeight: "300" },
  secondaryAction: { color: "#50dff0", textAlign: "center", fontSize: 13, fontWeight: "700" },
  trustRow: { flexDirection: "row", justifyContent: "center", alignItems: "center", gap: 8, paddingTop: 2 },
  trustIcon: { color: "#31e4ed", fontSize: 18 },
  trustText: { color: "#cad9e6", fontSize: 11.5 },
  benefitsRow: { marginTop: 26, marginHorizontal: 22, flexDirection: "row", alignItems: "stretch", justifyContent: "space-between" },
  benefitWrap: { flex: 1, flexDirection: "row", alignItems: "center" },
  benefitDivider: { width: 1, height: 56, backgroundColor: "rgba(126,176,211,0.28)" },
  benefitItem: { flex: 1, alignItems: "center" },
  benefitIcon: { color: "#2edfea", fontSize: 22, fontWeight: "800", marginBottom: 7 },
  benefitTop: { color: "#f2f7fd", fontSize: 9, letterSpacing: 1.6, fontWeight: "800" },
  benefitBottom: { color: "#a2b5c8", fontSize: 9, letterSpacing: 1.6, marginTop: 4 },
  footerBrandRow: { marginTop: 28, marginHorizontal: 34, flexDirection: "row", alignItems: "center", gap: 16, zIndex: 2 },
  footerBrand: { color: "#f2f7fb", fontSize: 21, fontWeight: "900" },
  footerDivider: { width: 1, height: 24, backgroundColor: "rgba(255,255,255,0.34)" },
  footerCopy: { color: "#9cb2c7", fontSize: 8.5, letterSpacing: 2.5, fontWeight: "700" },
  waveWrap: { position: "absolute", left: -20, right: -20, bottom: -10, height: 116, overflow: "hidden" },
  waveRow: { position: "absolute", left: 0, right: 0, flexDirection: "row", justifyContent: "space-around" },
  waveDot: { width: 3, height: 3, borderRadius: 2, backgroundColor: "#20d8e7", shadowColor: "#20d8e7", shadowOpacity: 0.65, shadowRadius: 4 },
});
