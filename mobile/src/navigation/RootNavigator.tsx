import { createBottomTabNavigator } from "@react-navigation/bottom-tabs";
import { createNativeStackNavigator } from "@react-navigation/native-stack";
import { NavigationContainer, DarkTheme } from "@react-navigation/native";
import { BlurView } from "expo-blur";
import { LinearGradient } from "expo-linear-gradient";
import { Platform, StyleSheet, Text, View } from "react-native";
import { LoginScreen } from "@/screens/LoginScreen";
import { DashboardScreen } from "@/screens/DashboardScreen";
import { WorkflowScreen } from "@/screens/WorkflowScreen";
import { ProofScreen } from "@/screens/ProofScreen";
import { TelemetryScreen } from "@/screens/TelemetryScreen";
import { SettingsScreen } from "@/screens/SettingsScreen";
import { DriverTodayScreen } from "@/screens/DriverTodayScreen";
import { DriverTripScreen } from "@/screens/DriverTripScreen";
import { DriverProofScreen } from "@/screens/DriverProofScreen";
import { DriverComplianceScreen } from "@/screens/DriverComplianceScreen";
import { CustomerHomeScreen } from "@/screens/CustomerHomeScreen";
import { CustomerShipmentsScreen } from "@/screens/CustomerShipmentsScreen";
import { CustomerBillingScreen } from "@/screens/CustomerBillingScreen";
import { useSession } from "@/auth/SessionProvider";
import { colors } from "@/components/ui";

const Stack = createNativeStackNavigator();
const Tabs = createBottomTabNavigator();

const tabIcons: Record<string, string> = {
  Today: "◉",
  Trip: "↗",
  Proof: "✓",
  Compliance: "◆",
  Home: "◉",
  Work: "↗",
  Fleet: "⌁",
  Shipments: "↗",
  Billing: "$",
  More: "•••",
};

function tabOptions(label: string) {
  return {
    tabBarLabel: label,
    tabBarIcon: ({ color }: { color: string }) => (
      <Text style={{ color, fontSize: 18, fontWeight: "900" }}>{tabIcons[label] ?? "•"}</Text>
    ),
  };
}

function GlassTabBarBackground() {
  return (
    <View pointerEvents="none" style={StyleSheet.absoluteFill}>
      {Platform.OS === "ios" ? (
        <BlurView intensity={48} tint="dark" style={StyleSheet.absoluteFill} />
      ) : (
        <View style={[StyleSheet.absoluteFill, styles.androidGlassFallback]} />
      )}
      <LinearGradient
        colors={["rgba(18,42,65,0.78)", "rgba(4,13,24,0.92)"]}
        start={{ x: 0, y: 0 }}
        end={{ x: 1, y: 1 }}
        style={StyleSheet.absoluteFill}
      />
      <LinearGradient
        colors={["rgba(112,183,255,0.24)", "rgba(39,211,194,0.05)", "rgba(255,255,255,0.02)"]}
        start={{ x: 0, y: 0 }}
        end={{ x: 1, y: 0 }}
        style={styles.glassHighlight}
      />
    </View>
  );
}

function DriverTabs() {
  return (
    <Tabs.Navigator screenOptions={screenOptions}>
      <Tabs.Screen name="Today" component={DriverTodayScreen} options={{ title: "Today", ...tabOptions("Today") }} />
      <Tabs.Screen name="Trip" component={DriverTripScreen} options={{ title: "Current trip", ...tabOptions("Trip") }} />
      <Tabs.Screen name="DriverProof" component={DriverProofScreen} options={{ title: "Proof", ...tabOptions("Proof") }} />
      <Tabs.Screen name="Compliance" component={DriverComplianceScreen} options={{ title: "Compliance", ...tabOptions("Compliance") }} />
      <Tabs.Screen name="DriverMore" component={SettingsScreen} options={{ title: "Profile & security", ...tabOptions("More") }} />
    </Tabs.Navigator>
  );
}

function CustomerTabs() {
  return (
    <Tabs.Navigator screenOptions={screenOptions}>
      <Tabs.Screen name="CustomerHome" component={CustomerHomeScreen} options={{ title: "Your account", ...tabOptions("Home") }} />
      <Tabs.Screen name="CustomerShipments" component={CustomerShipmentsScreen} options={{ title: "Shipments", ...tabOptions("Shipments") }} />
      <Tabs.Screen name="CustomerBilling" component={CustomerBillingScreen} options={{ title: "Billing", ...tabOptions("Billing") }} />
      <Tabs.Screen name="CustomerMore" component={SettingsScreen} options={{ title: "Profile & security", ...tabOptions("More") }} />
    </Tabs.Navigator>
  );
}

function OperationsTabs() {
  const { session, hasPermission } = useSession();
  const hasAnyPermission = (...permissions: string[]) => permissions.some(hasPermission);
  const directPermissions = new Set((session?.permissions ?? []).map((permission) => permission.trim().toLowerCase()));
  const canWork = hasAnyPermission(
    "dispatch.smart_assign.read", "dispatch.smart_assign.recommend", "dispatch.smart_assign.accept", "dispatch.smart_assign.reject",
    "operations.site_access.read", "operations.site_access.create", "operations.site_access.update",
    "operations.pickup_authorization.read", "operations.pickup_authorization.create", "operations.pickup_authorization.update",
    "operations.warehouse_handover.read", "operations.warehouse_handover.create", "operations.warehouse_handover.update",
    "dispatch:view", "dispatch:manage",
  );
  const hasProofWorkflowPermission = directPermissions.has("*") || [
    "operations.proof.create", "operations.proof.update", "operations.proof.submit", "operations.proof.validate",
    "dispatch:view", "dispatch:manage",
  ].some((permission) => directPermissions.has(permission));
  const isCustomerProofReader = directPermissions.has("customer_portal:view") && !hasProofWorkflowPermission;
  const canProof = !isCustomerProofReader && hasAnyPermission(
    "operations.proof.read", "operations.proof.create", "operations.proof.update", "operations.proof.submit", "operations.proof.validate",
  );
  const canFleet = hasAnyPermission(
    "telemetry.live_state.read", "telemetry.live-state.read", "telemetry.alerts.read", "telemetry:view",
    "dashboard:view", "map:view", "fleet:view", "telematics:gps:view", "safety:view", "maintenance:view",
  );

  return (
    <Tabs.Navigator screenOptions={screenOptions}>
      <Tabs.Screen name="Home" component={DashboardScreen} options={{ title: "Operations", ...tabOptions("Home") }} />
      {canWork ? <Tabs.Screen name="Work" component={WorkflowScreen} options={{ title: "Work", ...tabOptions("Work") }} /> : null}
      {canProof ? <Tabs.Screen name="Proof" component={ProofScreen} options={{ title: "Proof", ...tabOptions("Proof") }} /> : null}
      {canFleet ? <Tabs.Screen name="Fleet" component={TelemetryScreen} options={{ title: "Fleet health", ...tabOptions("Fleet") }} /> : null}
      <Tabs.Screen name="More" component={SettingsScreen} options={{ title: "Profile & security", ...tabOptions("More") }} />
    </Tabs.Navigator>
  );
}

function LoadingSplash() {
  return (
    <View style={styles.loadingSplash}>
      <LinearGradient colors={["#0d2940", colors.background, colors.backgroundDeep]} style={StyleSheet.absoluteFill} />
      <View pointerEvents="none" style={styles.loadingGlow} />
      <Text style={styles.loadingBrand}>OpsTrax</Text>
      <Text style={styles.loadingTitle}>Securing your workspace</Text>
      <Text style={styles.loadingBody}>Validating the saved session before any tenant data is shown.</Text>
    </View>
  );
}

export function RootNavigator() {
  const { ready, session, normalizedRole, hasPermission } = useSession();
  if (!ready) return <LoadingSplash />;
  const directPermissions = new Set((session?.permissions ?? []).map((permission) => permission.trim().toLowerCase()));
  const isDriver = Boolean(
    session
    && directPermissions.has("driver:self")
    && !directPermissions.has("*")
    && !directPermissions.has("dashboard:view")
    && !directPermissions.has("dashboard.view"),
  );
  // Customer mobile is a separate product experience. Requiring both the customer
  // role model and the portal permission prevents a broad internal role that merely
  // happens to carry a portal-related permission from being routed into this shell.
  // The /api/portal/* backend remains the authoritative customer_id ownership gate.
  const isCustomer = Boolean(
    session
    && normalizedRole === "customerClient"
    && hasPermission("customer_portal:view"),
  );

  return (
    <NavigationContainer theme={darkTheme}>
      <Stack.Navigator screenOptions={{ headerShown: false }}>
        {!session ? <Stack.Screen name="Login" component={LoginScreen} /> : (
          <Stack.Screen name="Main" component={isCustomer ? CustomerTabs : isDriver ? DriverTabs : OperationsTabs} />
        )}
      </Stack.Navigator>
    </NavigationContainer>
  );
}

const screenOptions = {
  headerStyle: { backgroundColor: colors.background },
  headerShadowVisible: false,
  headerTintColor: colors.text,
  headerTitleStyle: { fontSize: 17, fontWeight: "900" as const },
  tabBarHideOnKeyboard: true,
  tabBarStyle: {
    position: "absolute" as const,
    left: 12,
    right: 12,
    bottom: 10,
    height: 74,
    borderRadius: 26,
    borderTopWidth: 1,
    borderWidth: 1,
    borderColor: "rgba(170,218,255,0.18)",
    backgroundColor: "transparent",
    paddingBottom: 9,
    paddingTop: 8,
    overflow: "hidden" as const,
    shadowColor: "#000000",
    shadowOffset: { width: 0, height: 12 },
    shadowOpacity: 0.32,
    shadowRadius: 24,
    elevation: 18,
  },
  tabBarBackground: () => <GlassTabBarBackground />,
  tabBarItemStyle: {
    borderRadius: 18,
    marginHorizontal: 3,
  },
  tabBarActiveBackgroundColor: "rgba(39,211,194,0.10)",
  tabBarActiveTintColor: colors.teal,
  tabBarInactiveTintColor: "rgba(168,183,201,0.72)",
  tabBarLabelStyle: { fontSize: 10, fontWeight: "800" as const, letterSpacing: 0.15 },
};

const darkTheme = {
  ...DarkTheme,
  colors: {
    ...DarkTheme.colors,
    background: colors.background,
    card: "rgba(6,17,31,0.82)",
    border: colors.border,
    text: colors.text,
    primary: colors.teal,
    notification: colors.red,
  },
};

const styles = StyleSheet.create({
  androidGlassFallback: {
    backgroundColor: "rgba(6,17,31,0.90)",
  },
  glassHighlight: {
    position: "absolute",
    top: 0,
    left: 12,
    right: 12,
    height: 1,
  },
  loadingSplash: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: colors.background,
    padding: 24,
    overflow: "hidden",
  },
  loadingGlow: {
    position: "absolute",
    width: 320,
    height: 320,
    borderRadius: 320,
    backgroundColor: colors.teal,
    opacity: 0.08,
    top: -120,
    right: -100,
  },
  loadingBrand: {
    color: colors.teal,
    fontSize: 12,
    fontWeight: "900",
    letterSpacing: 2.4,
    textTransform: "uppercase",
  },
  loadingTitle: {
    color: colors.text,
    fontSize: 24,
    fontWeight: "900",
    letterSpacing: -0.5,
    marginTop: 10,
  },
  loadingBody: {
    color: colors.muted,
    marginTop: 8,
    textAlign: "center",
    lineHeight: 20,
    maxWidth: 320,
  },
});
