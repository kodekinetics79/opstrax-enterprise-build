import Constants from "expo-constants";
import * as Notifications from "expo-notifications";
import * as SecureStore from "expo-secure-store";
import { Platform } from "react-native";
import { APP_VARIANT, EAS_PROJECT_ID, SECURE_PUSH_TOKEN_KEY } from "@/config";

type PushApi = {
  request: {
    post: <T>(path: string, body?: unknown, headers?: HeadersInit) => Promise<T>;
  };
};

export type PushRegistrationState =
  | "unsupported"
  | "unconfigured"
  | "not_requested"
  | "denied"
  | "registered"
  | "error";

export type PushRegistrationStatus = {
  state: PushRegistrationState;
  permission: string;
  registered: boolean;
  detail?: string;
};

const CHANNEL_ID = "operations";
let presentationConfigured = false;

function isNativePlatform() {
  return Platform.OS === "ios" || Platform.OS === "android";
}

function isExpoToken(value: string) {
  return (
    value.length >= 20
    && value.length <= 4096
    && !/\s/.test(value)
    && (value.startsWith("ExpoPushToken[") || value.startsWith("ExponentPushToken["))
    && value.endsWith("]")
  );
}

function permissionLabel(status: Notifications.NotificationPermissionsStatus) {
  if (status.granted) return "granted";
  return String(status.status ?? "undetermined").toLowerCase();
}

async function ensureAndroidChannel() {
  if (Platform.OS !== "android") return;
  await Notifications.setNotificationChannelAsync(CHANNEL_ID, {
    name: "OpsTrax operations",
    description: "Assignment, dispatch, proof, safety, and operational updates.",
    importance: Notifications.AndroidImportance.HIGH,
    vibrationPattern: [0, 180, 100, 180],
    lightColor: "#42dfcf",
  });
}

export function configureNotificationPresentation() {
  if (!isNativePlatform() || presentationConfigured) return;
  Notifications.setNotificationHandler({
    handleNotification: async () => ({
      shouldShowBanner: true,
      shouldShowList: true,
      shouldPlaySound: false,
      shouldSetBadge: false,
    }),
  });
  presentationConfigured = true;
}

export async function getPushRegistrationStatus(): Promise<PushRegistrationStatus> {
  if (!isNativePlatform()) {
    return { state: "unsupported", permission: "unsupported", registered: false };
  }
  if (APP_VARIANT === "unified" || !EAS_PROJECT_ID) {
    return {
      state: "unconfigured",
      permission: "unknown",
      registered: false,
      detail: APP_VARIANT === "unified" ? "Push is disabled for the unified development binary." : "EAS project ID is not configured.",
    };
  }
  try {
    const permissions = await Notifications.getPermissionsAsync();
    const storedToken = await SecureStore.getItemAsync(SECURE_PUSH_TOKEN_KEY);
    const registered = Boolean(storedToken && isExpoToken(storedToken));
    if (!permissions.granted) {
      const notRequested = String(permissions.status).toLowerCase() === "undetermined";
      return {
        state: notRequested ? "not_requested" : "denied",
        permission: permissionLabel(permissions),
        registered: false,
      };
    }
    return {
      state: registered ? "registered" : "not_requested",
      permission: permissionLabel(permissions),
      registered,
      detail: registered ? undefined : "Permission is granted; device registration will complete when a native push token is available.",
    };
  } catch (error) {
    return {
      state: "error",
      permission: "unknown",
      registered: false,
      detail: error instanceof Error ? error.message : "Unable to read notification status.",
    };
  }
}

export async function registerNativePush(
  api: PushApi,
  options: { requestPermission: boolean } = { requestPermission: false },
): Promise<PushRegistrationStatus> {
  if (!isNativePlatform()) {
    return { state: "unsupported", permission: "unsupported", registered: false };
  }
  if (APP_VARIANT === "unified" || !EAS_PROJECT_ID) {
    return {
      state: "unconfigured",
      permission: "unknown",
      registered: false,
      detail: APP_VARIANT === "unified" ? "Use a Driver, Fleet, or Customer binary for native push." : "Configure EXPO_PUBLIC_EAS_PROJECT_ID before enabling native push.",
    };
  }

  try {
    configureNotificationPresentation();
    await ensureAndroidChannel();

    let permissions = await Notifications.getPermissionsAsync();
    if (!permissions.granted && options.requestPermission) {
      permissions = await Notifications.requestPermissionsAsync();
    }
    if (!permissions.granted) {
      const notRequested = String(permissions.status).toLowerCase() === "undetermined";
      return {
        state: notRequested ? "not_requested" : "denied",
        permission: permissionLabel(permissions),
        registered: false,
      };
    }

    const expoToken = await Notifications.getExpoPushTokenAsync({ projectId: EAS_PROJECT_ID });
    const token = expoToken.data.trim();
    if (!isExpoToken(token)) throw new Error("Expo returned an invalid push token.");

    await api.request.post("/api/mobile/devices/register", {
      token,
      product: APP_VARIANT,
      platform: Platform.OS,
      appVersion: Constants.expoConfig?.version ?? "unknown",
      deviceOsVersion: String(Platform.Version),
    });

    await SecureStore.setItemAsync(SECURE_PUSH_TOKEN_KEY, token, {
      keychainAccessible: SecureStore.WHEN_UNLOCKED_THIS_DEVICE_ONLY,
    });

    return { state: "registered", permission: permissionLabel(permissions), registered: true };
  } catch (error) {
    return {
      state: "error",
      permission: "unknown",
      registered: false,
      detail: error instanceof Error ? error.message : "Unable to register this device for notifications.",
    };
  }
}

export async function revokeStoredNativePush(api: PushApi) {
  if (!isNativePlatform()) return;
  const token = await SecureStore.getItemAsync(SECURE_PUSH_TOKEN_KEY);
  if (!token || !isExpoToken(token)) {
    await SecureStore.deleteItemAsync(SECURE_PUSH_TOKEN_KEY);
    return;
  }
  try {
    await api.request.post("/api/mobile/devices/revoke", { token });
  } finally {
    await SecureStore.deleteItemAsync(SECURE_PUSH_TOKEN_KEY);
  }
}

export function watchNativePushTokenChanges(api: PushApi) {
  if (!isNativePlatform() || APP_VARIANT === "unified" || !EAS_PROJECT_ID) return null;
  return Notifications.addPushTokenListener(() => {
    void registerNativePush(api, { requestPermission: false });
  });
}

export function watchNotificationOpens(onOpen: () => void) {
  if (!isNativePlatform()) return null;
  return Notifications.addNotificationResponseReceivedListener(() => {
    // Notification payloads never choose a tenant, URL, or privileged destination.
    // Opening the app only refreshes authenticated server-scoped data.
    onOpen();
  });
}
