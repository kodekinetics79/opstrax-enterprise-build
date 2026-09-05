import { Component, type ErrorInfo, type ReactNode } from "react";
import { Pressable, StyleSheet, Text, View } from "react-native";
import { LinearGradient } from "expo-linear-gradient";
import { colors } from "@/components/ui";

type Props = { children: ReactNode };
type State = { failed: boolean };

export class AppErrorBoundary extends Component<Props, State> {
  state: State = { failed: false };

  static getDerivedStateFromError(): State {
    return { failed: true };
  }

  componentDidCatch(_error: Error, _info: ErrorInfo) {
    // Intentionally do not render or persist exception details here. A production
    // crash-reporting provider can be attached later with tenant-safe redaction.
  }

  private retry = () => {
    this.setState({ failed: false });
  };

  render() {
    if (!this.state.failed) return this.props.children;

    return (
      <View accessibilityRole="alert" style={styles.root}>
        <LinearGradient colors={["#102941", colors.background, colors.backgroundDeep]} style={StyleSheet.absoluteFill} />
        <View pointerEvents="none" style={styles.glow} />
        <View style={styles.card}>
          <Text style={styles.eyebrow}>OpsTrax mobile</Text>
          <Text accessibilityRole="header" style={styles.title}>This screen couldn’t finish loading</Text>
          <Text style={styles.body}>
            Your account data has not been changed. Try rendering the workspace again. If the problem continues, close and reopen the app or contact your organization’s support team.
          </Text>
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Try loading the workspace again"
            onPress={this.retry}
            style={({ pressed }) => [styles.buttonFrame, pressed && styles.buttonPressed]}
          >
            <LinearGradient colors={["#56ead9", "#27c7bd", "#18aaa1"]} style={styles.button}>
              <Text style={styles.buttonText}>Try again</Text>
            </LinearGradient>
          </Pressable>
          <Text style={styles.privacy}>Technical exception details are not displayed on this device.</Text>
        </View>
      </View>
    );
  }
}

const styles = StyleSheet.create({
  root: { flex: 1, alignItems: "center", justifyContent: "center", padding: 22, backgroundColor: colors.background, overflow: "hidden" },
  glow: { position: "absolute", width: 320, height: 320, borderRadius: 320, backgroundColor: colors.teal, opacity: 0.08, top: -140, right: -120 },
  card: { width: "100%", maxWidth: 520, borderRadius: 28, borderWidth: 1, borderColor: colors.borderStrong, backgroundColor: "rgba(7,18,31,0.90)", padding: 22, gap: 13, shadowColor: "#000", shadowOpacity: 0.28, shadowRadius: 24, shadowOffset: { width: 0, height: 14 }, elevation: 10 },
  eyebrow: { color: colors.teal, fontSize: 10, fontWeight: "900", letterSpacing: 1.8, textTransform: "uppercase" },
  title: { color: colors.text, fontSize: 25, lineHeight: 30, fontWeight: "900", letterSpacing: -0.65 },
  body: { color: colors.muted, fontSize: 14, lineHeight: 21 },
  buttonFrame: { borderRadius: 18, overflow: "hidden", marginTop: 4 },
  button: { minHeight: 52, alignItems: "center", justifyContent: "center", paddingHorizontal: 18 },
  buttonText: { color: colors.backgroundDeep, fontSize: 14, fontWeight: "900" },
  buttonPressed: { opacity: 0.86, transform: [{ scale: 0.99 }] },
  privacy: { color: colors.subtle, fontSize: 11, lineHeight: 16 },
});
