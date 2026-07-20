import { StatusBar } from "expo-status-bar";
import { SafeAreaProvider } from "react-native-safe-area-context";
import { enableScreens } from "react-native-screens";
import { SessionProvider } from "@/auth/SessionProvider";
import { WorkflowProvider } from "@/workflow/WorkflowContext";
import { RootNavigator } from "@/navigation/RootNavigator";
import { Shell } from "@/components/ui";

enableScreens();

export function AppShell() {
  return (
    <SafeAreaProvider>
      <SessionProvider>
        <WorkflowProvider>
          <Shell>
            <StatusBar style="light" />
            <RootNavigator />
          </Shell>
        </WorkflowProvider>
      </SessionProvider>
    </SafeAreaProvider>
  );
}

