import { ActivityIndicator, Pressable, ScrollView, StyleSheet, Text, TextInput, View } from "react-native";

export const colors = {
  background: "#08111f",
  panel: "#0f1b2d",
  panelAlt: "#12233a",
  border: "#20354f",
  borderStrong: "#2b476a",
  text: "#edf4ff",
  muted: "#9db0c9",
  subtle: "#6d8098",
  teal: "#34d1bf",
  blue: "#7cb8ff",
  amber: "#f5b84d",
  red: "#ff7c96",
  green: "#57d18d",
};

export function Screen({ children }: { children: React.ReactNode }) {
  return <ScrollView contentContainerStyle={styles.screen}>{children}</ScrollView>;
}

export function Shell({ children }: { children: React.ReactNode }) {
  return <View style={styles.shell}>{children}</View>;
}

export function Panel({ children }: { children: React.ReactNode }) {
  return <View style={styles.panel}>{children}</View>;
}

export function SectionHeader({
  eyebrow,
  title,
  description,
  right,
}: {
  eyebrow?: string;
  title: string;
  description?: string;
  right?: React.ReactNode;
}) {
  return (
    <View style={styles.sectionHeader}>
      <View style={{ flex: 1, gap: 4 }}>
        {eyebrow ? <Text style={styles.eyebrow}>{eyebrow}</Text> : null}
        <Text style={styles.sectionTitle}>{title}</Text>
        {description ? <Text style={styles.sectionDescription}>{description}</Text> : null}
      </View>
      {right}
    </View>
  );
}

export function MetricCard({ label, value, tone = "blue" }: { label: string; value: string; tone?: "blue" | "teal" | "amber" | "red" | "green" }) {
  return (
    <View style={[styles.metricCard, { borderColor: toneColor(tone) + "44" }]}>
      <Text style={styles.metricLabel}>{label}</Text>
      <Text style={[styles.metricValue, { color: toneColor(tone) }]}>{value}</Text>
    </View>
  );
}

export function Pill({ label, tone = "blue" }: { label: string; tone?: "blue" | "teal" | "amber" | "red" | "green" }) {
  return <Text style={[styles.pill, { backgroundColor: toneColor(tone) + "1f", borderColor: toneColor(tone) + "55", color: toneColor(tone) }]}>{label}</Text>;
}

export function ActionButton({
  label,
  onPress,
  variant = "primary",
  disabled,
}: {
  label: string;
  onPress: () => void;
  variant?: "primary" | "secondary" | "ghost";
  disabled?: boolean;
}) {
  return (
    <Pressable onPress={onPress} disabled={disabled} style={({ pressed }) => [styles.button, buttonStyle(variant), disabled && styles.buttonDisabled, pressed && !disabled && styles.buttonPressed]}>
      <Text style={[styles.buttonText, variant === "ghost" && styles.buttonTextGhost]}>{label}</Text>
    </Pressable>
  );
}

export function Field({
  label,
  value,
  placeholder = "No data yet",
}: {
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
}: {
  label: string;
  value: string;
  onChangeText: (value: string) => void;
  placeholder?: string;
  secureTextEntry?: boolean;
  keyboardType?: "default" | "email-address" | "numeric";
  autoCapitalize?: "none" | "sentences" | "words" | "characters";
}) {
  return (
    <View style={{ gap: 6 }}>
      <Text style={styles.fieldLabel}>{label}</Text>
      <TextInput
        value={value}
        onChangeText={onChangeText}
        placeholder={placeholder}
        placeholderTextColor={colors.subtle}
        secureTextEntry={secureTextEntry}
        keyboardType={keyboardType}
        autoCapitalize={autoCapitalize}
        style={styles.input}
      />
    </View>
  );
}

export function EmptyState({ title, body }: { title: string; body: string }) {
  return (
    <View style={styles.emptyState}>
      <Text style={styles.emptyTitle}>{title}</Text>
      <Text style={styles.emptyBody}>{body}</Text>
    </View>
  );
}

export function ErrorState({ title, body }: { title: string; body: string }) {
  return (
    <View style={[styles.emptyState, { borderColor: colors.red + "66" }]}>
      <Text style={[styles.emptyTitle, { color: colors.red }]}>{title}</Text>
      <Text style={styles.emptyBody}>{body}</Text>
    </View>
  );
}

export function LoadingState({ label = "Loading..." }: { label?: string }) {
  return (
    <View style={styles.loadingState}>
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

function toneColor(tone: "blue" | "teal" | "amber" | "red" | "green") {
  switch (tone) {
    case "teal":
      return colors.teal;
    case "amber":
      return colors.amber;
    case "red":
      return colors.red;
    case "green":
      return colors.green;
    default:
      return colors.blue;
  }
}

function buttonStyle(variant: "primary" | "secondary" | "ghost") {
  switch (variant) {
    case "secondary":
      return { backgroundColor: colors.panelAlt, borderColor: colors.borderStrong };
    case "ghost":
      return { backgroundColor: "transparent", borderColor: colors.border };
    default:
      return { backgroundColor: colors.teal, borderColor: colors.teal };
  }
}

const styles = StyleSheet.create({
  screen: {
    padding: 16,
    gap: 16,
    backgroundColor: colors.background,
  },
  shell: {
    flex: 1,
    backgroundColor: colors.background,
  },
  panel: {
    borderRadius: 24,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.panel,
    padding: 16,
    gap: 14,
  },
  sectionHeader: {
    flexDirection: "row",
    gap: 12,
    alignItems: "flex-start",
    justifyContent: "space-between",
  },
  eyebrow: {
    color: colors.teal,
    fontSize: 11,
    fontWeight: "800",
    letterSpacing: 1.8,
    textTransform: "uppercase",
  },
  sectionTitle: {
    color: colors.text,
    fontSize: 20,
    fontWeight: "800",
    letterSpacing: -0.4,
  },
  sectionDescription: {
    color: colors.muted,
    fontSize: 13,
    lineHeight: 18,
  },
  metricCard: {
    flex: 1,
    minWidth: 120,
    borderWidth: 1,
    borderRadius: 18,
    padding: 14,
    backgroundColor: colors.panelAlt,
    gap: 6,
  },
  metricLabel: {
    color: colors.muted,
    fontSize: 11,
    fontWeight: "700",
    textTransform: "uppercase",
    letterSpacing: 1.2,
  },
  metricValue: {
    color: colors.text,
    fontSize: 18,
    fontWeight: "800",
  },
  pill: {
    alignSelf: "flex-start",
    borderRadius: 999,
    borderWidth: 1,
    paddingHorizontal: 10,
    paddingVertical: 5,
    fontSize: 11,
    fontWeight: "800",
    letterSpacing: 0.4,
    textTransform: "uppercase",
  },
  button: {
    borderRadius: 16,
    borderWidth: 1,
    paddingHorizontal: 14,
    paddingVertical: 12,
    alignItems: "center",
    justifyContent: "center",
  },
  buttonDisabled: {
    opacity: 0.45,
  },
  buttonPressed: {
    transform: [{ scale: 0.98 }],
  },
  buttonText: {
    color: colors.background,
    fontSize: 13,
    fontWeight: "800",
    letterSpacing: 0.4,
  },
  buttonTextGhost: {
    color: colors.text,
  },
  field: {
    gap: 4,
    padding: 12,
    borderRadius: 16,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: "rgba(255,255,255,0.02)",
  },
  fieldLabel: {
    color: colors.subtle,
    fontSize: 11,
    fontWeight: "700",
    letterSpacing: 1,
    textTransform: "uppercase",
  },
  fieldValue: {
    color: colors.text,
    fontSize: 14,
    lineHeight: 19,
    fontWeight: "600",
  },
  input: {
    borderWidth: 1,
    borderColor: colors.borderStrong,
    backgroundColor: "#071321",
    borderRadius: 16,
    color: colors.text,
    paddingHorizontal: 14,
    paddingVertical: 12,
    fontSize: 15,
  },
  emptyState: {
    padding: 18,
    borderRadius: 20,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.panelAlt,
    gap: 8,
  },
  emptyTitle: {
    color: colors.text,
    fontSize: 16,
    fontWeight: "800",
  },
  emptyBody: {
    color: colors.muted,
    lineHeight: 19,
    fontSize: 13,
  },
  loadingState: {
    flexDirection: "row",
    alignItems: "center",
    gap: 10,
    paddingVertical: 14,
  },
  loadingLabel: {
    color: colors.muted,
    fontSize: 13,
    fontWeight: "600",
  },
  row: {
    flexDirection: "row",
    gap: 10,
    flexWrap: "wrap",
  },
  monoBlock: {
    padding: 14,
    borderRadius: 18,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: "#091424",
  },
  divider: {
    height: 1,
    backgroundColor: colors.border,
  },
});

