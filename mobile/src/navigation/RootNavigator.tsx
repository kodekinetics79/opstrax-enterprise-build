import { createBottomTabNavigator } from "@react-navigation/bottom-tabs";
import { createNativeStackNavigator } from "@react-navigation/native-stack";
import { NavigationContainer, DarkTheme } from "@react-navigation/native";
import { Pressable, Text, View } from "react-native";
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
import { APP_NAME, APP_VARIANT } from "@/config";
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
    <View style={{ flex: 1, alignItems: "center", justifyContent: "center", backgroundColor: colors.background, padding: 24 }}>
      <Text style={{ color: colors.teal, fontSize: 12, fontWeight: "900", letterSpacing: 2, textTransform: "uppercase" }}>OpsTrax</Text>
      <Text style={{ color: colors.text, fontSize: 22, fontWeight: "900", marginTop: 9 }}>Securing your workspace</Text>
      <Text style={{ color: colors.muted, marginTop: 8, textAlign: "center", lineHeight: 20 }}>Validating the saved session before any tenant data is shown.</Text>
    </View>
  );
}

function ProductMismatchScreen() {
  const { logout } = useSession();
  return (
    <View style={{ flex: 1, alignItems: "center", justifyContent: "center", backgroundColor: colors.background, padding: 28 }}>
      <View style={{ width: "100%", maxWidth: 460, borderRadius: 24, borderWidth: 1, borderColor: colors.border, backgroundColor: colors.panel, padding: 22, gap: 14 }}>
        <Text style={{ color: colors.teal, fontSize: 11, fontWeight: "900", letterSpacing: 1.8, textTransform: "uppercase" }}>{APP_NAME}</Text>
        <Text style={{ color: colors.text, fontSize: 24, fontWeight: "900" }}>Use the OpsTrax app assigned to your role</Text>
        <Text style={{ color: colors.muted, fontSize: 14, lineHeight: 21 }}>
          This account is valid, but it is not authorized for the {APP_VARIANT} product experience. OpsTrax keeps Driver, Fleet, and Customer app boundaries separate even though they share the same secure platform.
        </Text>
        <Pressable
          accessibilityRole="button"
          accessibilityLabel="Sign out"
          onPress={() => { void logout(); }}
          style={({ pressed }) => ({
            minHeight: 50,
            borderRadius: 17,
            borderWidth: 1,
            borderColor: colors.borderStrong,
            backgroundColor: pressed ? "rgba(112,183,255,0.18)" : "rgba(112,183,255,0.10)",
            alignItems: "center",
            justifyContent: "center",
            paddingHorizontal: 16,
          })}
        >
          <Text style={{ color: colors.text, fontSize: 14, fontWeight: "900" }}>Sign out</Text>
        </Pressable>
      </View>
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
  const isFleetUser = Boolean(
    session
    && !isDriver
    && !isCustomer
    && normalizedRole !== "platformAdmin",
  );
  const productAccessAllowed = APP_VARIANT === "unified"
    || (APP_VARIANT === "driver" && isDriver)
    || (APP_VARIANT === "customer" && isCustomer)
    || (APP_VARIANT === "fleet" && isFleetUser);
  const MainComponent = !productAccessAllowed
    ? ProductMismatchScreen
    : isCustomer
      ? CustomerTabs
      : isDriver
        ? DriverTabs
        : OperationsTabs;

  return (
    <NavigationContainer theme={darkTheme}>
      <Stack.Navigator screenOptions={{ headerShown: false }}>
        {!session ? <Stack.Screen name="Login" component={LoginScreen} /> : (
          <Stack.Screen name="Main" component={MainComponent} />
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
