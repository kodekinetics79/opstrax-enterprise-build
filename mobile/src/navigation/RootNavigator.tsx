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
import { useSession } from "@/auth/SessionProvider";
import { colors } from "@/components/ui";

const Stack = createNativeStackNavigator();
const Tabs = createBottomTabNavigator();

function AppTabs() {
  const { roleModel, hasPermission } = useSession();
  const hasAnyPermission = (...permissions: string[]) => permissions.some(hasPermission);
  const canUseWorkflows = hasAnyPermission(
    "dispatch.smart_assign.read", "dispatch.smart_assign.recommend", "dispatch.smart_assign.accept", "dispatch.smart_assign.reject",
    "operations.site_access.read", "operations.site_access.create", "operations.site_access.update",
    "operations.pickup_authorization.read", "operations.pickup_authorization.create", "operations.pickup_authorization.update",
    "operations.warehouse_handover.read", "operations.warehouse_handover.create", "operations.warehouse_handover.update",
    "operations.proof.read", "operations.proof.create", "operations.proof.update", "operations.proof.submit", "operations.proof.validate",
    "dispatch:view", "dispatch:manage", "driver:self",
  );
  const canUseProof = hasAnyPermission("operations.proof.read", "operations.proof.create", "operations.proof.update", "operations.proof.submit", "operations.proof.validate");
  const canUseFleetHealth = hasAnyPermission(
    "telemetry.live_state.read", "telemetry.live-state.read", "telemetry.alerts.read", "dashboard:view", "map:view",
    "fleet:view", "telematics:gps:view", "safety:view", "maintenance:view",
  );

  return (
    <Tabs.Navigator
      screenOptions={{
        headerStyle: { backgroundColor: colors.background },
        headerTintColor: colors.text,
        tabBarStyle: { backgroundColor: "#071321", borderTopColor: colors.border, height: 64, paddingBottom: 8, paddingTop: 8 },
        tabBarActiveTintColor: colors.teal,
        tabBarInactiveTintColor: colors.muted,
        tabBarLabelStyle: { fontSize: 11, fontWeight: "700" },
      }}
    >
      <Tabs.Screen name="Dashboard" component={DashboardScreen} options={{ title: roleModel.title, tabBarLabel: "Dashboard" }} />
      {canUseWorkflows ? <Tabs.Screen name="Workflows" component={WorkflowScreen} options={{ title: "Workflows", tabBarLabel: "Work" }} /> : null}
      {canUseProof ? <Tabs.Screen name="Proof" component={ProofScreen} options={{ title: "Proof", tabBarLabel: "Proof" }} /> : null}
      {canUseFleetHealth ? <Tabs.Screen name="Telemetry" component={TelemetryScreen} options={{ title: "Telemetry", tabBarLabel: "Telemetry" }} /> : null}
      <Tabs.Screen name="Settings" component={SettingsScreen} options={{ title: "Settings", tabBarLabel: "Settings" }} />
    </Tabs.Navigator>
  );
}

function LoadingSplash() {
  return (
    <View style={{ flex: 1, alignItems: "center", justifyContent: "center", backgroundColor: colors.background, padding: 24 }}>
      <Text style={{ color: colors.teal, fontSize: 12, fontWeight: "800", letterSpacing: 2, textTransform: "uppercase" }}>OpsTrax Mobile</Text>
      <Text style={{ color: colors.text, fontSize: 20, fontWeight: "800", marginTop: 8 }}>Restoring secure session</Text>
      <Text style={{ color: colors.muted, marginTop: 8, textAlign: "center", lineHeight: 20 }}>
        The mobile shell is waiting for the authenticated backend session before showing role-based routes.
      </Text>
    </View>
  );
}

export function RootNavigator() {
  const { ready, session } = useSession();

  if (!ready) {
    return <LoadingSplash />;
  }

  return (
    <NavigationContainer theme={darkTheme}>
      <Stack.Navigator screenOptions={{ headerShown: false }}>
        {!session ? <Stack.Screen name="Login" component={LoginScreen} /> : <Stack.Screen name="Main" component={AppTabs} />}
      </Stack.Navigator>
    </NavigationContainer>
  );
}

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
