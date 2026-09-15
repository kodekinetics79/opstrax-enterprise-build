import { useEffect, useState } from "react";
import { Animated, Easing, StyleSheet, Text, View, type ViewStyle } from "react-native";
import { LinearGradient } from "expo-linear-gradient";
import { colors, radii } from "@/components/ui";

type DriverTone = "teal" | "blue" | "amber" | "red" | "green" | "violet";

function toneColor(tone: DriverTone) {
  if (tone === "blue") return colors.blue;
  if (tone === "amber") return colors.amber;
  if (tone === "red") return colors.red;
  if (tone === "green") return colors.green;
  if (tone === "violet") return colors.violet;
  return colors.teal;
}

export function EnterpriseMark({ compact = false }: { compact?: boolean }) {
  const [motion] = useState(() => new Animated.Value(0));
  useEffect(() => {
    const loop = Animated.loop(
      Animated.sequence([
        Animated.timing(motion, { toValue: 1, duration: 2400, easing: Easing.inOut(Easing.sin), useNativeDriver: true }),
        Animated.timing(motion, { toValue: 0, duration: 2400, easing: Easing.inOut(Easing.sin), useNativeDriver: true }),
      ]),
    );
    loop.start();
    return () => loop.stop();
  }, [motion]);

  const size = compact ? 48 : 68;
  return (
    <Animated.View
      accessibilityLabel="OpsTrax"
      style={{
        width: size,
        height: size,
        transform: [
          { perspective: 700 },
          { rotateZ: motion.interpolate({ inputRange: [0, 1], outputRange: ["-2deg", "2deg"] }) },
          { translateY: motion.interpolate({ inputRange: [0, 1], outputRange: [0, -3] }) },
          { scale: motion.interpolate({ inputRange: [0, 1], outputRange: [1, 1.025] }) },
        ],
      }}
    >
      <LinearGradient colors={["#48f1dd", "#3a8fe9", "#705cff"]} start={{ x: 0, y: 0 }} end={{ x: 1, y: 1 }} style={[styles.markFrame, { borderRadius: compact ? 16 : 22 }]}>
        <View style={styles.markTopPlane} />
        <View style={styles.markSidePlane} />
        <View style={styles.markRouteVertical} />
        <View style={[styles.markRouteArm, { transform: [{ rotate: "34deg" }] }]} />
        <View style={[styles.markRouteArm, { transform: [{ rotate: "-34deg" }] }]} />
        <View style={[styles.markNode, { top: "18%", left: "45%" }]} />
        <View style={[styles.markNode, { top: "59%", left: "21%" }]} />
        <View style={[styles.markNode, { top: "59%", right: "21%" }]} />
        <View pointerEvents="none" style={styles.markSpecular} />
      </LinearGradient>
    </Animated.View>
  );
}

export function TelemetryRail({ tone = "teal" }: { tone?: DriverTone }) {
  const [travel] = useState(() => new Animated.Value(0));
  useEffect(() => {
    const loop = Animated.loop(
      Animated.timing(travel, { toValue: 1, duration: 3200, easing: Easing.linear, useNativeDriver: true }),
    );
    loop.start();
    return () => loop.stop();
  }, [travel]);
  const color = toneColor(tone);
  return (
    <View style={styles.rail} pointerEvents="none">
      <View style={[styles.railBase, { backgroundColor: `${color}45` }]} />
      {[0, 1, 2, 3].map((index) => (
        <View key={index} style={[styles.railNode, { left: `${8 + index * 28}%`, backgroundColor: color, shadowColor: color }]} />
      ))}
      <Animated.View
        style={[
          styles.railPulse,
          {
            backgroundColor: color,
            shadowColor: color,
            transform: [{ translateX: travel.interpolate({ inputRange: [0, 1], outputRange: [-10, 260] }) }],
          },
        ]}
      />
    </View>
  );
}

export function DriverSceneHero({
  eyebrow,
  title,
  description,
  status,
  tone = "teal",
  children,
  style,
}: {
  eyebrow: string;
  title: string;
  description: string;
  status?: string;
  tone?: DriverTone;
  children?: React.ReactNode;
  style?: ViewStyle;
}) {
  const color = toneColor(tone);
  const [enter] = useState(() => new Animated.Value(0));
  useEffect(() => {
    Animated.timing(enter, { toValue: 1, duration: 560, easing: Easing.out(Easing.cubic), useNativeDriver: true }).start();
  }, [enter]);

  return (
    <Animated.View style={[styles.heroOuter, style, { opacity: enter, transform: [{ translateY: enter.interpolate({ inputRange: [0, 1], outputRange: [12, 0] }) }] }]}>
      <LinearGradient colors={[`${color}22`, "rgba(23,66,102,0.34)", "rgba(3,10,20,0.96)"]} start={{ x: 0, y: 0 }} end={{ x: 1, y: 1 }} style={styles.hero}>
        <View pointerEvents="none" style={[styles.heroOrb, { backgroundColor: color }]} />
        <View style={styles.brandRow}>
          <EnterpriseMark compact />
          <View style={{ flex: 1 }}>
            <Text style={styles.brandName}>OpsTrax</Text>
            <Text style={[styles.brandProduct, { color }]}>DRIVER</Text>
          </View>
          {status ? (
            <View style={[styles.statusPill, { borderColor: `${color}60`, backgroundColor: `${color}12` }]}>
              <View style={[styles.statusDot, { backgroundColor: color }]} />
              <Text numberOfLines={1} style={[styles.statusText, { color }]}>{status}</Text>
            </View>
          ) : null}
        </View>

        <View style={styles.heroCopy}>
          <Text style={[styles.eyebrow, { color }]}>{eyebrow}</Text>
          <Text style={styles.title}>{title}</Text>
          <Text style={styles.description}>{description}</Text>
        </View>
        <TelemetryRail tone={tone} />
        {children ? <View style={styles.heroChildren}>{children}</View> : null}
      </LinearGradient>
    </Animated.View>
  );
}

export function DriverStatusStrip({
  items,
}: {
  items: { label: string; value: string; tone?: DriverTone }[];
}) {
  return (
    <View style={styles.statusStrip}>
      {items.map((item) => {
        const color = toneColor(item.tone ?? "teal");
        return (
          <View key={`${item.label}:${item.value}`} style={[styles.statusItem, { borderColor: `${color}32` }]}>
            <View style={[styles.statusAccent, { backgroundColor: color }]} />
            <Text numberOfLines={1} style={[styles.statusLabel, { color }]}>{item.label}</Text>
            <Text numberOfLines={1} style={styles.statusValue}>{item.value}</Text>
          </View>
        );
      })}
    </View>
  );
}

export function DriverActionTile({
  code,
  title,
  subtitle,
  tone = "teal",
}: {
  code: string;
  title: string;
  subtitle: string;
  tone?: DriverTone;
}) {
  const color = toneColor(tone);
  return (
    <LinearGradient colors={[`${color}18`, "rgba(9,27,46,0.94)"]} style={[styles.actionTile, { borderColor: `${color}3c` }]}>
      <View style={[styles.actionCode, { borderColor: `${color}45`, backgroundColor: `${color}14` }]}>
        <Text style={[styles.actionCodeText, { color }]}>{code}</Text>
      </View>
      <Text style={styles.actionTitle}>{title}</Text>
      <Text style={styles.actionSubtitle}>{subtitle}</Text>
    </LinearGradient>
  );
}

const styles = StyleSheet.create({
  markFrame: { flex: 1, overflow: "hidden", borderWidth: 1, borderColor: "rgba(255,255,255,0.28)", shadowColor: "#43e6d3", shadowOpacity: 0.32, shadowRadius: 20, shadowOffset: { width: 0, height: 10 }, elevation: 14 },
  markTopPlane: { position: "absolute", top: -8, left: 6, right: 6, height: "48%", borderRadius: 18, backgroundColor: "rgba(255,255,255,0.13)", transform: [{ rotate: "8deg" }] },
  markSidePlane: { position: "absolute", right: -8, top: 12, bottom: 3, width: "45%", backgroundColor: "rgba(3,12,30,0.23)", transform: [{ rotate: "-8deg" }] },
  markRouteVertical: { position: "absolute", width: 3, height: "45%", backgroundColor: "#f7ffff", left: "49%", top: "20%", borderRadius: 8 },
  markRouteArm: { position: "absolute", height: 3, width: "33%", backgroundColor: "#f7ffff", left: "34%", top: "56%", borderRadius: 8 },
  markNode: { position: "absolute", width: 7, height: 7, borderRadius: 8, backgroundColor: "#ffffff", shadowColor: "#ffffff", shadowOpacity: 0.9, shadowRadius: 8 },
  markSpecular: { position: "absolute", width: "72%", height: 1, top: 8, left: 8, backgroundColor: "rgba(255,255,255,0.55)" },
  rail: { height: 22, overflow: "hidden", justifyContent: "center", marginTop: 8 },
  railBase: { height: 1, left: 10, right: 10, position: "absolute" },
  railNode: { position: "absolute", width: 7, height: 7, borderRadius: 8, top: 7.5, shadowOpacity: 0.75, shadowRadius: 8 },
  railPulse: { width: 34, height: 2, borderRadius: 4, position: "absolute", left: 0, top: 10, shadowOpacity: 0.9, shadowRadius: 10 },
  heroOuter: { borderRadius: radii.xl, shadowColor: "#000000", shadowOpacity: 0.42, shadowRadius: 30, shadowOffset: { width: 0, height: 18 }, elevation: 20 },
  hero: { borderRadius: radii.xl, overflow: "hidden", borderWidth: 1, borderColor: "rgba(94,220,214,0.23)", padding: 18, gap: 12 },
  heroOrb: { position: "absolute", width: 180, height: 180, borderRadius: 180, opacity: 0.11, right: -76, top: -86 },
  brandRow: { flexDirection: "row", alignItems: "center", gap: 11 },
  brandName: { color: colors.text, fontSize: 22, lineHeight: 24, fontWeight: "900", letterSpacing: -0.7 },
  brandProduct: { fontSize: 9.5, lineHeight: 13, fontWeight: "900", letterSpacing: 3 },
  statusPill: { maxWidth: "38%", flexDirection: "row", alignItems: "center", gap: 6, borderWidth: 1, borderRadius: 999, paddingHorizontal: 9, paddingVertical: 6 },
  statusDot: { width: 5, height: 5, borderRadius: 5 },
  statusText: { fontSize: 9, fontWeight: "900", letterSpacing: 0.9, textTransform: "uppercase" },
  heroCopy: { gap: 7, marginTop: 2 },
  eyebrow: { fontSize: 10, lineHeight: 14, fontWeight: "900", letterSpacing: 2.1, textTransform: "uppercase" },
  title: { color: colors.text, fontSize: 34, lineHeight: 37, fontWeight: "900", letterSpacing: -1.45, maxWidth: 560 },
  description: { color: colors.muted, fontSize: 14.5, lineHeight: 21, maxWidth: 560 },
  heroChildren: { marginTop: 4 },
  statusStrip: { flexDirection: "row", gap: 8 },
  statusItem: { flex: 1, minWidth: 0, overflow: "hidden", borderWidth: 1, borderRadius: 14, backgroundColor: "rgba(8,24,42,0.88)", paddingHorizontal: 11, paddingVertical: 9 },
  statusAccent: { position: "absolute", top: 0, bottom: 0, left: 0, width: 2 },
  statusLabel: { fontSize: 8.5, lineHeight: 11, fontWeight: "900", letterSpacing: 1.1, textTransform: "uppercase" },
  statusValue: { color: colors.text, fontSize: 11.5, lineHeight: 16, fontWeight: "800", marginTop: 2 },
  actionTile: { minWidth: 140, flex: 1, minHeight: 110, borderWidth: 1, borderRadius: 22, padding: 14, justifyContent: "center", alignItems: "center", gap: 5 },
  actionCode: { width: 36, height: 36, borderRadius: 12, borderWidth: 1, alignItems: "center", justifyContent: "center", marginBottom: 2 },
  actionCodeText: { fontSize: 16, fontWeight: "900" },
  actionTitle: { color: colors.text, fontSize: 14, fontWeight: "900" },
  actionSubtitle: { color: colors.subtle, fontSize: 9.5, lineHeight: 13, fontWeight: "800", letterSpacing: 0.8, textAlign: "center", textTransform: "uppercase" },
});
