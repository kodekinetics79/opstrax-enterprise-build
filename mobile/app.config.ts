import type { ExpoConfig } from "expo/config";
import { existsSync } from "node:fs";
import { resolve } from "node:path";

const API_BASE_URL =
  process.env.EXPO_PUBLIC_API_BASE_URL?.trim() ||
  process.env.EXPO_PUBLIC_DOTNET_API_URL?.trim() ||
  "http://localhost:8088";
const STAGE = process.env.EXPO_PUBLIC_STAGE?.trim().toLowerCase() || "pilot";
const isProductionBuild = process.env.EAS_BUILD_PROFILE === "production" || STAGE === "production";
const allowedApiHosts = (process.env.EXPO_PUBLIC_ALLOWED_API_HOSTS ?? "")
  .split(",")
  .map((value: string) => value.trim().toLowerCase())
  .filter(Boolean);
const hasBundledAssets = existsSync(resolve(__dirname, "assets/icon.png"));

if (isProductionBuild) {
  const apiUrl = new URL(API_BASE_URL);
  if (apiUrl.protocol !== "https:" || ["localhost", "127.0.0.1", "::1"].includes(apiUrl.hostname)) {
    throw new Error("Production OpsTrax mobile builds require a non-loopback HTTPS API URL.");
  }
  if (!allowedApiHosts.length || !allowedApiHosts.includes(apiUrl.hostname.toLowerCase())) {
    throw new Error("Production API host must be listed in EXPO_PUBLIC_ALLOWED_API_HOSTS.");
  }
}

const config: ExpoConfig = {
  name: "OpsTrax Mobile",
  slug: "opstrax-mobile",
  version: "1.1.0",
  orientation: "portrait",
  ...(hasBundledAssets ? { icon: "./assets/icon.png" } : {}),
  userInterfaceStyle: "dark",
  scheme: STAGE === "production" ? "opstrax" : `opstrax-${STAGE.replace(/[^a-z0-9-]+/g, "-")}`,
  plugins: [
    "expo-secure-store",
    [
      "expo-image-picker",
      {
        cameraPermission: "Allow OpsTrax to capture delivery and inspection evidence.",
        photosPermission: "Allow OpsTrax to select delivery and inspection evidence.",
        microphonePermission: false,
      },
    ],
    "expo-location",
  ],
  ios: {
    supportsTablet: true,
    bundleIdentifier: process.env.EXPO_PUBLIC_IOS_BUNDLE_ID?.trim() || (STAGE === "production" ? "com.opstrax.mobile" : `com.opstrax.mobile.${STAGE.replace(/[^a-z0-9]+/g, "")}`),
    infoPlist: {
      NSLocationWhenInUseUsageDescription: "Allow OpsTrax to attach your current location to proof you choose to submit.",
    },
  },
  android: {
    ...(hasBundledAssets ? { adaptiveIcon: {
      backgroundColor: "#07111f",
      foregroundImage: "./assets/android-icon-foreground.png",
      backgroundImage: "./assets/android-icon-background.png",
      monochromeImage: "./assets/android-icon-monochrome.png",
    } } : {}),
    predictiveBackGestureEnabled: false,
    package: process.env.EXPO_PUBLIC_ANDROID_PACKAGE?.trim() || (STAGE === "production" ? "com.opstrax.mobile" : `com.opstrax.mobile.${STAGE.replace(/[^a-z0-9]+/g, "")}`),
  },
  web: {
    ...(hasBundledAssets ? { favicon: "./assets/favicon.png" } : {}),
  },
  extra: {
    apiBaseUrl: API_BASE_URL,
    stage: STAGE,
    appName: "OpsTrax",
  },
};

export default config;
