import { createBottomTabNavigator } from "@react-navigation/bottom-tabs";
import { createNativeStackNavigator } from "@react-navigation/native-stack";
import { NavigationContainer, DarkTheme } from "@react-navigation/native";
import { Text, View } from "react-native";
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
  More: "•••",
};

function tabOptions(label: string) {
  return {
    tabBarLabel: label,
    tabBarIcon: ({ color }: { color: string }) => <Text style={{ color, fontSize: 18, fontWeight: "900" }}>{tabIcons[label] ?? "•"}</Text>,
  };
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

function OperationsTabs() {
  const { hasPermission } = useSession();
  const hasAnyPermission = (...permissions: string[]) => permissions.some(hasPermission);
  const canWork = hasAnyPermission(
    "dispatch.smart_assign.read", "dispatch.smart_assign.recommend", "dispatch.smart_assign.accept", "dispatch.smart_assign.reject",
    "operations.site_access.read", "operations.site_access.create", "operations.site_access.update",
    "operations.pickup_authorization.read", "operations.pickup_authorization.create", "operations.pickup_authorization.update",
    "operations.warehouse_handover.read", "operations.warehouse_handover.create", "operations.warehouse_handover.update",
    "dispatch:view", "dispatch:manage",
  );
  const canProof = hasAnyPermission(
    "operations.proof.read", "operations.proof.create", "operations.proof.update", "operations.proof.submit", "operations.proof.validate",
    "customer_portal:view",
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
    <View style={{ flex: 1, alignItems: "center", justifyContent: "center", backgroundColor: colors.background, padding: 24 }}>
      <Text style={{ color: colors.teal, fontSize: 12, fontWeight: "900", letterSpacing: 2, textTransform: "uppercase" }}>OpsTrax</Text>
      <Text style={{ color: colors.text, fontSize: 22, fontWeight: "900", marginTop: 9 }}>Securing your workspace</Text>
      <Text style={{ color: colors.muted, marginTop: 8, textAlign: "center", lineHeight: 20 }}>Validating the saved session before any tenant data is shown.</Text>
    </View>
  );
}

export function RootNavigator() {
  const { ready, session } = useSession();
  if (!ready) return <LoadingSplash />;
  const directPermissions = new Set((session?.permissions ?? []).map((permission) => permission.trim().toLowerCase()));
  const isDriver = Boolean(
    session
    && directPermissions.has("driver:self")
    && !directPermissions.has("*")
    && !directPermissions.has("dashboard:view")
    && !directPermissions.has("dashboard.view"),
  );

  return (
    <NavigationContainer theme={darkTheme}>
      <Stack.Navigator screenOptions={{ headerShown: false }}>
        {!session ? <Stack.Screen name="Login" component={LoginScreen} /> : (
          <Stack.Screen name="Main" component={isDriver ? DriverTabs : OperationsTabs} />
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
  tabBarStyle: {
    position: "absolute" as const,
    left: 12,
    right: 12,
    bottom: 10,
    height: 72,
    borderRadius: 24,
    borderTopWidth: 1,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: "rgba(6,17,31,0.96)",
    paddingBottom: 9,
    paddingTop: 8,
  },
  tabBarActiveTintColor: colors.teal,
  tabBarInactiveTintColor: colors.subtle,
  tabBarLabelStyle: { fontSize: 10, fontWeight: "800" as const },
};

const darkTheme = {
  ...DarkTheme,
  colors: {
    ...DarkTheme.colors,
    background: colors.background,
    card: colors.background,
    border: colors.border,
    text: colors.text,
    primary: colors.teal,
    notification: colors.red,
  },
};
