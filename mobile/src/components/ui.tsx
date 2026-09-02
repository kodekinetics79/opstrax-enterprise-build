import { BlurView } from "expo-blur";
import { LinearGradient } from "expo-linear-gradient";
import * as Network from "expo-network";
import {
  ActivityIndicator,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
  type ViewStyle,
} from "react-native";

export const colors = {
  background: "#06111f",
  backgroundDeep: "#030a13",
  panel: "rgba(12, 26, 43, 0.80)",
  panelAlt: "rgba(255, 255, 255, 0.055)",
  border: "rgba(255, 255, 255, 0.10)",
  borderStrong: "rgba(112, 183, 255, 0.30)",
  text: "#f3f7fc",
  muted: "#a8b7c9",
  subtle: "#7f93aa",
  teal: "#27d3c2",
  blue: "#70b7ff",
  amber: "#f6b950",
  red: "#ff6d8b",
  green: "#52d190",
};

type Tone = "blue" | "teal" | "amber" | "red" | "green";

export function Screen({ children }: { children: React.ReactNode }) {
  return (
    <ScrollView keyboardShouldPersistTaps="handled" showsVerticalScrollIndicator={false} contentContainerStyle={styles.screen}>
      {children}
    </ScrollView>
  );
}

export function Shell({ children }: { children: React.ReactNode }) {
  return (
    <View style={styles.shell}>
      <LinearGradient colors={["#071827", colors.background, colors.backgroundDeep]} style={StyleSheet.absoluteFill} />
      <View pointerEvents="none" style={[styles.orb, styles.orbTop]} />
      <View pointerEvents="none" style={[styles.orb, styles.orbBottom]} />
      {children}
    </View>
  );
}

export function Panel({ children, style }: { children: React.ReactNode; style?: ViewStyle }) {
  if (Platform.OS === "ios") {
    return <BlurView intensity={36} tint="dark" style={[styles.panel, style]}>{children}</BlurView>;
  }
  return <View style={[styles.panel, style]}>{children}</View>;
}

export function NetworkBanner() {
  const network = Network.useNetworkState();
  if (network.isConnected !== false) return null;
  return (
    <View accessibilityRole="alert" style={styles.networkBanner}>
      <Text style={styles.networkTitle}>Offline</Text>
      <Text style={styles.networkText}>Live actions are paused. Reconnect before submitting operational changes.</Text>
    </View>
  );
}

export function SectionHeader({ eyebrow, title, description, right }: {
  eyebrow?: string;
  title: string;
  description?: string;
  right?: React.ReactNode;
}) {
  return (
    <View style={styles.sectionHeader}>
      <View style={{ flex: 1, gap: 5 }}>
        {eyebrow ? <Text style={styles.eyebrow}>{eyebrow}</Text> : null}
        <Text accessibilityRole="header" style={styles.sectionTitle}>{title}</Text>
        {description ? <Text style={styles.sectionDescription}>{description}</Text> : null}
      </View>
      {right}
    </View>
  );
}

export function MetricCard({ label, value, tone = "blue" }: { label: string; value: string; tone?: Tone }) {
  return (
    <View style={[styles.metricCard, { borderColor: toneColor(tone) + "55" }]}>
      <Text style={styles.metricLabel}>{label}</Text>
      <Text style={[styles.metricValue, { color: toneColor(tone) }]} numberOfLines={2}>{value}</Text>
    </View>
  );
}

export function Pill({ label, tone = "blue" }: { label: string; tone?: Tone }) {
  return (
    <View style={[styles.pill, { backgroundColor: toneColor(tone) + "1f", borderColor: toneColor(tone) + "66" }]}>
      <View style={[styles.pillDot, { backgroundColor: toneColor(tone) }]} />
      <Text style={[styles.pillText, { color: toneColor(tone) }]}>{label}</Text>
    </View>
  );
}

export function ActionButton({ label, onPress, variant = "primary", disabled }: {
  label: string;
  onPress: () => void;
  variant?: "primary" | "secondary" | "ghost" | "danger";
  disabled?: boolean;
}) {
  const button = (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityState={{ disabled: Boolean(disabled) }}
      onPress={onPress}
      disabled={disabled}
      style={({ pressed }) => [
        styles.button,
        buttonStyle(variant),
        disabled && styles.buttonDisabled,
        pressed && !disabled && styles.buttonPressed,
      ]}
    >
      <Text style={[styles.buttonText, variant !== "primary" && styles.buttonTextLight]}>{label}</Text>
    </Pressable>
  );
  return variant === "primary" ? (
    <LinearGradient colors={["#39e0cf", "#1eb7ae"]} style={styles.buttonGradient}>{button}</LinearGradient>
  ) : button;
}

export function Field({ label, value, placeholder = "Not available" }: {
  label: string;
  value?: string | number | null;
  placeholder?: string;
}) {
  return (
    <View style={styles.field}>
      <Text style={styles.fieldLabel}>{label}</Text>
      <Text style={styles.fieldValue}>{value === null || value === undefined || value === "" ? placeholder : String(value)}</Text>
    </View>
  );
}

export function Input({
  label,
  value,
  onChangeText,
  placeholder,
  secureTextEntry,
  keyboardType,
  autoCapitalize = "none",
  autoComplete,
  textContentType,
  multiline,
}: {
  label: string;
  value: string;
  onChangeText: (value: string) => void;
  placeholder?: string;
  secureTextEntry?: boolean;
  keyboardType?: "default" | "email-address" | "numeric";
  autoCapitalize?: "none" | "sentences" | "words" | "characters";
  multiline?: boolean;
  autoComplete?: "email" | "password" | "one-time-code" | "off";
  textContentType?: "emailAddress" | "password" | "oneTimeCode" | "none";
}) {
  return (
    <View style={{ gap: 7 }}>
      <Text style={styles.fieldLabel}>{label}</Text>
      <TextInput
        accessibilityLabel={label}
        value={value}
        onChangeText={onChangeText}
        placeholder={placeholder}
        placeholderTextColor={colors.subtle}
        secureTextEntry={secureTextEntry}
        keyboardType={keyboardType}
        autoCapitalize={autoCapitalize}
        multiline={multiline}
        textAlignVertical={multiline ? "top" : "center"}
        autoComplete={autoComplete}
        textContentType={textContentType}
        style={[styles.input, multiline && styles.inputMultiline]}
      />
    </View>
  );
}

export function EmptyState({ title, body }: { title: string; body: string }) {
  return <View style={styles.emptyState}><Text style={styles.emptyTitle}>{title}</Text><Text style={styles.emptyBody}>{body}</Text></View>;
}

export function ErrorState({ title, body, onRetry }: { title: string; body: string; onRetry?: () => void }) {
  return (
    <View accessibilityRole="alert" accessibilityLiveRegion="assertive" style={[styles.emptyState, { borderColor: colors.red + "66" }]}>
      <Text style={[styles.emptyTitle, { color: colors.red }]}>{title}</Text>
      <Text style={styles.emptyBody}>{body}</Text>
      {onRetry ? <ActionButton label="Retry" onPress={onRetry} variant="secondary" /> : null}
    </View>
  );
}

export function LoadingState({ label = "Loading…" }: { label?: string }) {
  return (
    <View accessibilityLabel={label} accessibilityLiveRegion="polite" style={styles.loadingState}>
      <ActivityIndicator color={colors.teal} />
      <Text style={styles.loadingLabel}>{label}</Text>
    </View>
  );
}

export function Row({ children }: { children: React.ReactNode }) {
  return <View style={styles.row}>{children}</View>;
}

export function MonoBlock({ children }: { children: React.ReactNode }) {
  return <View style={styles.monoBlock}>{children}</View>;
}

export function Divider() {
  return <View style={styles.divider} />;
}

export function toneForStatus(status?: string): Tone {
  const value = String(status ?? "").toLowerCase();
  if (/critical|blocked|failed|cancel|violation|out.of.service/.test(value)) return "red";
  if (/warning|risk|exception|delayed|stale|pending/.test(value)) return "amber";
  if (/complete|delivered|active|online|clear|accepted|validated/.test(value)) return "green";
  if (/transit|route|progress|pickup/.test(value)) return "teal";
  return "blue";
}

function toneColor(tone: Tone) {
  return colors[tone];
}

function buttonStyle(variant: "primary" | "secondary" | "ghost" | "danger") {
  switch (variant) {
    case "secondary": return { backgroundColor: "rgba(112,183,255,0.12)", borderColor: colors.borderStrong };
    case "ghost": return { backgroundColor: "transparent", borderColor: colors.border };
    case "danger": return { backgroundColor: colors.red + "1f", borderColor: colors.red + "66" };
    default: return { backgroundColor: "transparent", borderColor: "transparent" };
  }
}

const styles = StyleSheet.create({
  screen: { paddingHorizontal: 16, paddingTop: 14, paddingBottom: 112, gap: 16 },
  shell: { flex: 1, backgroundColor: colors.background, overflow: "hidden" },
  orb: { position: "absolute", width: 300, height: 300, borderRadius: 300, opacity: 0.12 },
  orbTop: { backgroundColor: colors.blue, top: -150, right: -110 },
  orbBottom: { backgroundColor: colors.teal, bottom: -190, left: -130 },
  panel: { borderRadius: 24, borderWidth: 1, borderColor: colors.border, backgroundColor: colors.panel, padding: 17, gap: 15, overflow: "hidden" },
  networkBanner: { marginHorizontal: 16, marginTop: 8, borderRadius: 16, borderWidth: 1, borderColor: colors.amber + "66", backgroundColor: colors.amber + "18", paddingHorizontal: 14, paddingVertical: 10, gap: 2 },
  networkTitle: { color: colors.amber, fontSize: 12, fontWeight: "900", textTransform: "uppercase", letterSpacing: 1 },
  networkText: { color: colors.text, fontSize: 12, lineHeight: 17 },
  sectionHeader: { flexDirection: "row", gap: 12, alignItems: "flex-start", justifyContent: "space-between" },
  eyebrow: { color: colors.teal, fontSize: 10, fontWeight: "900", letterSpacing: 1.8, textTransform: "uppercase" },
  sectionTitle: { color: colors.text, fontSize: 21, fontWeight: "900", letterSpacing: -0.5 },
  sectionDescription: { color: colors.muted, fontSize: 13, lineHeight: 19 },
  metricCard: { flex: 1, minWidth: 105, borderWidth: 1, borderRadius: 18, padding: 14, backgroundColor: colors.panelAlt, gap: 7 },
  metricLabel: { color: colors.muted, fontSize: 10, fontWeight: "800", textTransform: "uppercase", letterSpacing: 1.1 },
  metricValue: { fontSize: 18, fontWeight: "900" },
  pill: { alignSelf: "flex-start", flexDirection: "row", alignItems: "center", gap: 6, borderRadius: 999, borderWidth: 1, paddingHorizontal: 10, paddingVertical: 6 },
  pillDot: { width: 6, height: 6, borderRadius: 6 },
  pillText: { fontSize: 10, fontWeight: "900", letterSpacing: 0.45, textTransform: "uppercase" },
  buttonGradient: { minHeight: 50, borderRadius: 17, overflow: "hidden", flex: 1 },
  button: { minHeight: 50, borderRadius: 17, borderWidth: 1, paddingHorizontal: 16, paddingVertical: 13, alignItems: "center", justifyContent: "center", flex: 1 },
  buttonDisabled: { opacity: 0.42 },
  buttonPressed: { opacity: 0.82, transform: [{ scale: 0.985 }] },
  buttonText: { color: colors.backgroundDeep, fontSize: 14, fontWeight: "900", letterSpacing: 0.2 },
  buttonTextLight: { color: colors.text },
  field: { gap: 4, padding: 13, borderRadius: 16, borderWidth: 1, borderColor: colors.border, backgroundColor: colors.panelAlt },
  fieldLabel: { color: colors.subtle, fontSize: 10, fontWeight: "800", letterSpacing: 1.05, textTransform: "uppercase" },
  fieldValue: { color: colors.text, fontSize: 14, lineHeight: 20, fontWeight: "600" },
  input: { minHeight: 50, borderWidth: 1, borderColor: colors.borderStrong, backgroundColor: "rgba(3,10,19,0.64)", borderRadius: 16, color: colors.text, paddingHorizontal: 14, paddingVertical: 12, fontSize: 15 },
  inputMultiline: { minHeight: 104 },
  emptyState: { padding: 18, borderRadius: 20, borderWidth: 1, borderColor: colors.border, backgroundColor: colors.panelAlt, gap: 7 },
  emptyTitle: { color: colors.text, fontSize: 16, fontWeight: "900" },
  emptyBody: { color: colors.muted, lineHeight: 19, fontSize: 13 },
  loadingState: { flexDirection: "row", alignItems: "center", gap: 10, paddingVertical: 14 },
  loadingLabel: { color: colors.muted, fontSize: 13, fontWeight: "600" },
  row: { flexDirection: "row", flexWrap: "wrap", gap: 10, alignItems: "stretch" },
  monoBlock: { padding: 14, borderRadius: 16, backgroundColor: "rgba(3,10,19,0.72)", borderWidth: 1, borderColor: colors.border, gap: 8 },
  divider: { height: 1, backgroundColor: colors.border, marginVertical: 2 },
});
