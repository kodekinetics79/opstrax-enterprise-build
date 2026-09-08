import { StatusBar } from "expo-status-bar";
import { SafeAreaProvider } from "react-native-safe-area-context";
import { enableScreens } from "react-native-screens";
import { SessionProvider } from "@/auth/SessionProvider";
import { WorkflowProvider } from "@/workflow/WorkflowContext";
import { RootNavigator } from "@/navigation/RootNavigator";
import { AppErrorBoundary } from "@/components/AppErrorBoundary";
import { NetworkBanner, Shell } from "@/components/ui";

enableScreens();

export function AppShell() {
  return (
    <SafeAreaProvider>
      <AppErrorBoundary>
        <SessionProvider>
          <WorkflowProvider>
            <Shell>
              <StatusBar style="light" />
              <NetworkBanner />
              <RootNavigator />
            </Shell>
          </WorkflowProvider>
        </SessionProvider>
      </AppErrorBoundary>
    </SafeAreaProvider>
  );
}
