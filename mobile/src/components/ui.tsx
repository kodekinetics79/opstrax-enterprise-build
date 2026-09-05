import { useState } from "react";
import { BlurView } from "expo-blur";
import { LinearGradient } from "expo-linear-gradient";
import * as Haptics from "expo-haptics";
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
  background: "#07111d",
  backgroundDeep: "#020812",
  backgroundRaised: "#0a1726",
  panel: "rgba(10, 24, 40, 0.74)",
  panelStrong: "rgba(8, 20, 34, 0.90)",
  panelAlt: "rgba(255, 255, 255, 0.052)",
  glassHighlight: "rgba(255,255,255,0.15)",
  glassLowlight: "rgba(255,255,255,0.035)",
  border: "rgba(255, 255, 255, 0.095)",
  borderStrong: "rgba(118, 192, 255, 0.34)",
  text: "#f7fbff",
  muted: "#a9b8c9",
  subtle: "#72869e",
  teal: "#42dfcf",
  tealDeep: "#18a99f",
  blue: "#78bdff",
  blueDeep: "#397dd6",
  violet: "#9f9cff",
  amber: "#f5be57",
  red: "#ff738f",
  green: "#5bd696",
};

export const radii = {
  sm: 14,
  md: 18,
  lg: 24,
  xl: 30,
  pill: 999,
};

export const spacing = {
  xs: 6,
  sm: 10,
  md: 14,
  lg: 18,
  xl: 24,
};

type Tone = "blue" | "teal" | "amber" | "red" | "green" | "violet";
type PanelVariant = "glass" | "elevated" | "solid" | "quiet";

export function Screen({ children }: { children: React.ReactNode }) {
  return (
    <ScrollView
      keyboardShouldPersistTaps="handled"
      showsVerticalScrollIndicator={false}
      contentContainerStyle={styles.screen}
    >
      {children}
    </ScrollView>
  );
}

export function Shell({ children }: { children: React.ReactNode }) {
  return (
    <View style={styles.shell}>
      <LinearGradient
        colors={["#091a2b", colors.background, colors.backgroundDeep]}
        locations={[0, 0.46, 1]}
        style={StyleSheet.absoluteFill}
      />
      <View pointerEvents="none" style={[styles.ambientGlow, styles.ambientGlowTop]} />
      <View pointerEvents="none" style={[styles.ambientGlow, styles.ambientGlowSide]} />
      <View pointerEvents="none" style={[styles.ambientGlow, styles.ambientGlowBottom]} />
      <LinearGradient
        pointerEvents="none"
        colors={["rgba(255,255,255,0.04)", "rgba(255,255,255,0)"]}
        style={styles.topSheen}
      />
      {children}
    </View>
  );
}

export function Panel({
  children,
  style,
  variant = "glass",
  tone,
}: {
  children: React.ReactNode;
  style?: ViewStyle;
  variant?: PanelVariant;
  tone?: Tone;
}) {
  const surfaceStyle = [
    styles.panel,
    variant === "solid" && styles.panelSolid,
    variant === "elevated" && styles.panelElevated,
    variant === "quiet" && styles.panelQuiet,
    tone ? { borderColor: `${toneColor(tone)}38` } : undefined,
    style,
  ];

  const content = (
    <>
      <LinearGradient
        pointerEvents="none"
        colors={[
          tone ? `${toneColor(tone)}12` : "rgba(255,255,255,0.045)",
          "rgba(255,255,255,0.008)",
          "rgba(0,0,0,0.055)",
        ]}
        locations={[0, 0.42, 1]}
        style={StyleSheet.absoluteFill}
      />
      <View pointerEvents="none" style={styles.panelHighlight} />
      {children}
    </>
  );

  if (Platform.OS === "ios" && variant !== "solid") {
    return (
      <BlurView intensity={variant === "quiet" ? 20 : 42} tint="dark" style={surfaceStyle}>
        {content}
      </BlurView>
    );
  }

  return <View style={surfaceStyle}>{content}</View>;
}

export function HeroPanel({
  children,
  tone = "teal",
  style,
}: {
  children: React.ReactNode;
  tone?: Tone;
  style?: ViewStyle;
}) {
  return (
    <LinearGradient
      colors={[`${toneColor(tone)}32`, `${colors.blue}13`, "rgba(255,255,255,0.025)"]}
      locations={[0, 0.52, 1]}
      style={[styles.heroFrame, style]}
    >
      <Panel style={styles.heroPanel} tone={tone}>
        <View pointerEvents="none" style={[styles.heroGlow, { backgroundColor: `${toneColor(tone)}20` }]} />
        {children}
      </Panel>
    </LinearGradient>
  );
}

export function BrandMark({ label = "OT" }: { label?: string }) {
  return (
    <LinearGradient colors={[colors.teal, colors.blue]} style={styles.brandMarkFrame}>
      <View style={styles.brandMarkInner}>
        <Text style={styles.brandMarkText}>{label}</Text>
      </View>
    </LinearGradient>
  );
}

export function NetworkBanner() {
  const network = Network.useNetworkState();
  if (network.isConnected !== false) return null;
  return (
    <View accessibilityRole="alert" style={styles.networkBanner}>
      <View style={styles.networkDot} />
      <View style={{ flex: 1, gap: 2 }}>
        <Text style={styles.networkTitle}>Offline mode</Text>
        <Text style={styles.networkText}>Some live data may be stale. Connection-required actions stay protected until service returns.</Text>
      </View>
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

export function MetricCard({
  label,
  value,
  tone = "blue",
  helper,
}: {
  label: string;
  value: string;
  tone?: Tone;
  helper?: string;
}) {
  return (
    <LinearGradient
      colors={[`${toneColor(tone)}18`, "rgba(255,255,255,0.035)", "rgba(255,255,255,0.018)"]}
      style={[styles.metricCard, { borderColor: `${toneColor(tone)}42` }]}
    >
      <View style={[styles.metricAccent, { backgroundColor: toneColor(tone) }]} />
      <Text style={styles.metricLabel}>{label}</Text>
      <Text style={[styles.metricValue, { color: toneColor(tone) }]} numberOfLines={2}>{value}</Text>
      {helper ? <Text style={styles.metricHelper}>{helper}</Text> : null}
    </LinearGradient>
  );
}

export function Pill({ label, tone = "blue" }: { label: string; tone?: Tone }) {
  return (
    <View style={[styles.pill, { backgroundColor: `${toneColor(tone)}18`, borderColor: `${toneColor(tone)}58` }]}>
      <View style={[styles.pillDotHalo, { borderColor: `${toneColor(tone)}44` }]}>
        <View style={[styles.pillDot, { backgroundColor: toneColor(tone) }]} />
      </View>
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
  const handlePress = () => {
    if (disabled) return;
    if (variant === "danger") {
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Warning);
    } else {
      void Haptics.selectionAsync();
    }
    onPress();
  };

  const button = (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityState={{ disabled: Boolean(disabled) }}
      onPress={handlePress}
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
    <LinearGradient colors={["#56ead9", "#27c7bd", "#18aaa1"]} style={styles.buttonGradient}>{button}</LinearGradient>
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
  const [focused, setFocused] = useState(false);
  return (
    <View style={{ gap: 7 }}>
      <Text style={styles.fieldLabel}>{label}</Text>
      <TextInput
        accessibilityLabel={label}
        value={value}
        onChangeText={onChangeText}
        onFocus={() => setFocused(true)}
        onBlur={() => setFocused(false)}
        placeholder={placeholder}
        placeholderTextColor={colors.subtle}
        secureTextEntry={secureTextEntry}
        keyboardType={keyboardType}
        autoCapitalize={autoCapitalize}
        multiline={multiline}
        textAlignVertical={multiline ? "top" : "center"}
        autoComplete={autoComplete}
        textContentType={textContentType}
        style={[
          styles.input,
          multiline && styles.inputMultiline,
          focused && styles.inputFocused,
        ]}
      />
    </View>
  );
}

export function ProgressBar({ value, tone = "teal", label }: { value: number; tone?: Tone; label?: string }) {
  const normalized = Math.max(0, Math.min(1, Number.isFinite(value) ? value : 0));
  return (
    <View style={{ gap: 7 }}>
      {label ? <Text style={styles.progressLabel}>{label}</Text> : null}
      <View style={styles.progressTrack}>
        <LinearGradient
          colors={[toneColor(tone), `${toneColor(tone)}b8`]}
          style={[styles.progressFill, { width: `${Math.round(normalized * 100)}%` }]}
        />
      </View>
    </View>
  );
}

export function QuickAction({
  title,
  subtitle,
  onPress,
  tone = "blue",
  disabled,
}: {
  title: string;
  subtitle?: string;
  onPress: () => void;
  tone?: Tone;
  disabled?: boolean;
}) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={title}
      disabled={disabled}
      onPress={() => {
        if (disabled) return;
        void Haptics.selectionAsync();
        onPress();
      }}
      style={({ pressed }) => [
        styles.quickAction,
        { borderColor: `${toneColor(tone)}38` },
        pressed && !disabled && styles.quickActionPressed,
        disabled && styles.buttonDisabled,
      ]}
    >
      <View style={[styles.quickActionIcon, { backgroundColor: `${toneColor(tone)}16`, borderColor: `${toneColor(tone)}3c` }]}>
        <View style={[styles.quickActionDot, { backgroundColor: toneColor(tone) }]} />
      </View>
      <View style={{ flex: 1, gap: 3 }}>
        <Text style={styles.quickActionTitle}>{title}</Text>
        {subtitle ? <Text style={styles.quickActionSubtitle}>{subtitle}</Text> : null}
      </View>
      <Text style={[styles.quickActionChevron, { color: toneColor(tone) }]}>›</Text>
    </Pressable>
  );
}

export function EmptyState({ title, body }: { title: string; body: string }) {
  return (
    <View style={styles.emptyState}>
      <View style={styles.emptyGlyph}><Text style={styles.emptyGlyphText}>·</Text></View>
      <View style={{ flex: 1, gap: 5 }}>
        <Text style={styles.emptyTitle}>{title}</Text>
        <Text style={styles.emptyBody}>{body}</Text>
      </View>
    </View>
  );
}

export function ErrorState({ title, body, onRetry }: { title: string; body: string; onRetry?: () => void }) {
  return (
    <View accessibilityRole="alert" accessibilityLiveRegion="assertive" style={[styles.emptyState, styles.errorState]}>
      <View style={[styles.emptyGlyph, { borderColor: `${colors.red}55`, backgroundColor: `${colors.red}12` }]}>
        <Text style={[styles.emptyGlyphText, { color: colors.red }]}>!</Text>
      </View>
      <View style={{ flex: 1, gap: 7 }}>
        <Text style={[styles.emptyTitle, { color: colors.red }]}>{title}</Text>
        <Text style={styles.emptyBody}>{body}</Text>
        {onRetry ? <ActionButton label="Retry" onPress={onRetry} variant="secondary" /> : null}
      </View>
    </View>
  );
}

export function LoadingState({ label = "Loading…" }: { label?: string }) {
  return (
    <View accessibilityLabel={label} accessibilityLiveRegion="polite" style={styles.loadingState}>
      <View style={styles.loadingOrb}><ActivityIndicator color={colors.teal} /></View>
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
    case "secondary": return { backgroundColor: "rgba(120,189,255,0.10)", borderColor: colors.borderStrong };
    case "ghost": return { backgroundColor: "rgba(255,255,255,0.025)", borderColor: colors.border };
    case "danger": return { backgroundColor: `${colors.red}16`, borderColor: `${colors.red}62` };
    default: return { backgroundColor: "transparent", borderColor: "transparent" };
  }
}

const styles = StyleSheet.create({
  screen: { paddingHorizontal: 16, paddingTop: 14, paddingBottom: 116, gap: 16 },
  shell: { flex: 1, backgroundColor: colors.background, overflow: "hidden" },
  ambientGlow: { position: "absolute", borderRadius: 999 },
  ambientGlowTop: { width: 360, height: 360, top: -210, right: -110, backgroundColor: colors.blue, opacity: 0.095 },
  ambientGlowSide: { width: 260, height: 260, top: "38%", right: -210, backgroundColor: colors.violet, opacity: 0.055 },
  ambientGlowBottom: { width: 410, height: 410, bottom: -300, left: -180, backgroundColor: colors.teal, opacity: 0.09 },
  topSheen: { position: "absolute", left: 0, right: 0, top: 0, height: 110 },
  panel: {
    borderRadius: radii.lg,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.panel,
    padding: 17,
    gap: 15,
    overflow: "hidden",
    shadowColor: "#000",
    shadowOpacity: 0.24,
    shadowRadius: 24,
    shadowOffset: { width: 0, height: 14 },
    elevation: 8,
  },
  panelStrong: { backgroundColor: colors.panelStrong },
  panelSolid: { backgroundColor: colors.backgroundRaised, borderColor: "rgba(255,255,255,0.08)" },
  panelElevated: { backgroundColor: "rgba(11,27,45,0.84)", shadowOpacity: 0.32, elevation: 12 },
  panelQuiet: { backgroundColor: "rgba(255,255,255,0.028)", shadowOpacity: 0.08, elevation: 1 },
  panelHighlight: { position: "absolute", height: 1, left: 22, right: 22, top: 0, backgroundColor: colors.glassHighlight, opacity: 0.72 },
  heroFrame: { borderRadius: radii.xl, padding: 1, overflow: "hidden" },
  heroPanel: { borderRadius: radii.xl, backgroundColor: "rgba(6,18,31,0.64)", minHeight: 180 },
  heroGlow: { position: "absolute", width: 220, height: 220, borderRadius: 220, right: -90, top: -110 },
  brandMarkFrame: { width: 52, height: 52, borderRadius: 18, padding: 1, shadowColor: colors.teal, shadowOpacity: 0.22, shadowRadius: 18, elevation: 6 },
  brandMarkInner: { flex: 1, borderRadius: 17, backgroundColor: "rgba(3,10,19,0.84)", alignItems: "center", justifyContent: "center" },
  brandMarkText: { color: colors.text, fontWeight: "900", fontSize: 14, letterSpacing: 0.4 },
  networkBanner: { marginHorizontal: 16, marginTop: 8, borderRadius: 18, borderWidth: 1, borderColor: `${colors.amber}62`, backgroundColor: `${colors.amber}12`, paddingHorizontal: 14, paddingVertical: 11, flexDirection: "row", alignItems: "center", gap: 10 },
  networkDot: { width: 9, height: 9, borderRadius: 9, backgroundColor: colors.amber, shadowColor: colors.amber, shadowOpacity: 0.45, shadowRadius: 8 },
  networkTitle: { color: colors.amber, fontSize: 11, fontWeight: "900", textTransform: "uppercase", letterSpacing: 1.05 },
  networkText: { color: colors.text, fontSize: 12, lineHeight: 17 },
  sectionHeader: { flexDirection: "row", gap: 12, alignItems: "flex-start", justifyContent: "space-between" },
  eyebrow: { color: colors.teal, fontSize: 10, fontWeight: "900", letterSpacing: 1.8, textTransform: "uppercase" },
  sectionTitle: { color: colors.text, fontSize: 21, lineHeight: 25, fontWeight: "900", letterSpacing: -0.55 },
  sectionDescription: { color: colors.muted, fontSize: 13, lineHeight: 19 },
  metricCard: { flex: 1, minWidth: 105, borderWidth: 1, borderRadius: 20, padding: 14, gap: 7, overflow: "hidden" },
  metricAccent: { position: "absolute", left: 0, top: 16, bottom: 16, width: 2, borderRadius: 2, opacity: 0.78 },
  metricLabel: { color: colors.muted, fontSize: 9.5, fontWeight: "800", textTransform: "uppercase", letterSpacing: 1.05 },
  metricValue: { fontSize: 19, lineHeight: 23, fontWeight: "900", letterSpacing: -0.25 },
  metricHelper: { color: colors.subtle, fontSize: 10.5, lineHeight: 15 },
  pill: { alignSelf: "flex-start", flexDirection: "row", alignItems: "center", gap: 7, borderRadius: radii.pill, borderWidth: 1, paddingHorizontal: 10, paddingVertical: 6 },
  pillDotHalo: { width: 11, height: 11, borderRadius: 11, borderWidth: 1, alignItems: "center", justifyContent: "center" },
  pillDot: { width: 5, height: 5, borderRadius: 5 },
  pillText: { fontSize: 10, fontWeight: "900", letterSpacing: 0.45, textTransform: "uppercase" },
  buttonGradient: { minHeight: 52, borderRadius: 18, overflow: "hidden", flex: 1, shadowColor: colors.teal, shadowOpacity: 0.18, shadowRadius: 16, elevation: 5 },
  button: { minHeight: 52, borderRadius: 18, borderWidth: 1, paddingHorizontal: 16, paddingVertical: 13, alignItems: "center", justifyContent: "center", flex: 1 },
  buttonDisabled: { opacity: 0.42 },
  buttonPressed: { opacity: 0.86, transform: [{ scale: 0.985 }] },
  buttonText: { color: colors.backgroundDeep, fontSize: 14, fontWeight: "900", letterSpacing: 0.15 },
  buttonTextLight: { color: colors.text },
  field: { gap: 4, paddingHorizontal: 14, paddingVertical: 13, borderRadius: 17, borderWidth: 1, borderColor: colors.border, backgroundColor: "rgba(255,255,255,0.036)" },
  fieldLabel: { color: colors.subtle, fontSize: 9.5, fontWeight: "800", letterSpacing: 1.05, textTransform: "uppercase" },
  fieldValue: { color: colors.text, fontSize: 14, lineHeight: 20, fontWeight: "600" },
  input: { minHeight: 52, borderWidth: 1, borderColor: colors.borderStrong, backgroundColor: "rgba(2,8,18,0.62)", borderRadius: 17, color: colors.text, paddingHorizontal: 14, paddingVertical: 12, fontSize: 15 },
  inputFocused: { borderColor: `${colors.teal}a8`, backgroundColor: "rgba(5,18,29,0.88)", shadowColor: colors.teal, shadowOpacity: 0.14, shadowRadius: 10 },
  inputMultiline: { minHeight: 104 },
  progressLabel: { color: colors.muted, fontSize: 11, fontWeight: "700" },
  progressTrack: { height: 8, borderRadius: 8, overflow: "hidden", backgroundColor: "rgba(255,255,255,0.065)", borderWidth: 1, borderColor: colors.border },
  progressFill: { height: "100%", borderRadius: 8 },
  quickAction: { minHeight: 72, borderRadius: 20, borderWidth: 1, backgroundColor: "rgba(255,255,255,0.032)", padding: 13, flexDirection: "row", alignItems: "center", gap: 12 },
  quickActionPressed: { transform: [{ scale: 0.99 }], backgroundColor: "rgba(255,255,255,0.055)" },
  quickActionIcon: { width: 42, height: 42, borderRadius: 15, borderWidth: 1, alignItems: "center", justifyContent: "center" },
  quickActionDot: { width: 10, height: 10, borderRadius: 10 },
  quickActionTitle: { color: colors.text, fontSize: 14, fontWeight: "800" },
  quickActionSubtitle: { color: colors.muted, fontSize: 11.5, lineHeight: 16 },
  quickActionChevron: { fontSize: 25, fontWeight: "300", marginRight: 2 },
  emptyState: { padding: 16, borderRadius: 20, borderWidth: 1, borderColor: colors.border, backgroundColor: "rgba(255,255,255,0.03)", gap: 12, flexDirection: "row", alignItems: "flex-start" },
  errorState: { borderColor: `${colors.red}58`, backgroundColor: `${colors.red}0d` },
  emptyGlyph: { width: 34, height: 34, borderRadius: 13, borderWidth: 1, borderColor: colors.border, backgroundColor: "rgba(255,255,255,0.035)", alignItems: "center", justifyContent: "center" },
  emptyGlyphText: { color: colors.blue, fontSize: 20, fontWeight: "900", lineHeight: 22 },
  emptyTitle: { color: colors.text, fontSize: 15, fontWeight: "900" },
  emptyBody: { color: colors.muted, lineHeight: 19, fontSize: 13 },
  loadingState: { flexDirection: "row", alignItems: "center", gap: 10, paddingVertical: 14 },
  loadingOrb: { width: 34, height: 34, borderRadius: 13, borderWidth: 1, borderColor: `${colors.teal}34`, backgroundColor: `${colors.teal}0d`, alignItems: "center", justifyContent: "center" },
  loadingLabel: { color: colors.muted, fontSize: 13, fontWeight: "600" },
  row: { flexDirection: "row", flexWrap: "wrap", gap: 10, alignItems: "stretch" },
  monoBlock: { padding: 14, borderRadius: 16, backgroundColor: "rgba(2,8,18,0.74)", borderWidth: 1, borderColor: colors.border, gap: 8 },
  divider: { height: 1, backgroundColor: colors.border, marginVertical: 2 },
});
