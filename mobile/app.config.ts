import type { ExpoConfig } from "expo/config";

const API_BASE_URL =
  process.env.EXPO_PUBLIC_API_BASE_URL?.trim() ||
  process.env.EXPO_PUBLIC_DOTNET_API_URL?.trim() ||
  "http://localhost:8088";

const config: ExpoConfig = {
  name: "OpsTrax Mobile",
  slug: "opstrax-mobile",
  version: "1.0.0",
  orientation: "portrait",
  icon: "./assets/icon.png",
  userInterfaceStyle: "dark",
  scheme: "opstrax-mobile",
  plugins: ["expo-secure-store"],
  ios: {
    supportsTablet: true,
  },
  android: {
    adaptiveIcon: {
      backgroundColor: "#07111f",
      foregroundImage: "./assets/android-icon-foreground.png",
      backgroundImage: "./assets/android-icon-background.png",
      monochromeImage: "./assets/android-icon-monochrome.png",
    },
    predictiveBackGestureEnabled: false,
  },
  web: {
    favicon: "./assets/favicon.png",
  },
  extra: {
    apiBaseUrl: API_BASE_URL,
    stage: "14A",
    appName: "OpsTrax Mobile",
  },
};

export default config;

