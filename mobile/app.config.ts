import type { ExpoConfig } from "expo/config";
import { existsSync } from "node:fs";
import { resolve } from "node:path";

const API_BASE_URL =
  process.env.EXPO_PUBLIC_API_BASE_URL?.trim() ||
  process.env.EXPO_PUBLIC_DOTNET_API_URL?.trim() ||
  "http://localhost:8088";
const STAGE = process.env.EXPO_PUBLIC_STAGE?.trim().toLowerCase() || "pilot";
const rawProduct = process.env.EXPO_PUBLIC_PRODUCT?.trim().toLowerCase() || "unified";
const PRODUCT = (["driver", "fleet", "customer", "unified"] as const).includes(rawProduct as "driver" | "fleet" | "customer" | "unified")
  ? rawProduct as "driver" | "fleet" | "customer" | "unified"
  : "unified";
const isProductionBuild = process.env.EAS_BUILD_PROFILE?.endsWith("production") === true || STAGE === "production";
const allowedApiHosts = (process.env.EXPO_PUBLIC_ALLOWED_API_HOSTS ?? "")
  .split(",")
  .map((value: string) => value.trim().toLowerCase())
  .filter(Boolean);
const hasBundledAssets = existsSync(resolve(__dirname, "assets/icon.png"));

const productConfig = {
  driver: { name: "OpsTrax Driver", slug: "opstrax-driver", id: "driver" },
  fleet: { name: "OpsTrax Fleet", slug: "opstrax-fleet", id: "fleet" },
  customer: { name: "OpsTrax Customer", slug: "opstrax-customer", id: "customer" },
  unified: { name: "OpsTrax Mobile", slug: "opstrax-mobile", id: "mobile" },
}[PRODUCT];

if (isProductionBuild) {
  const apiUrl = new URL(API_BASE_URL);
  if (apiUrl.protocol !== "https:" || ["localhost", "127.0.0.1", "::1"].includes(apiUrl.hostname)) {
    throw new Error("Production OpsTrax mobile builds require a non-loopback HTTPS API URL.");
  }
  if (!allowedApiHosts.length || !allowedApiHosts.includes(apiUrl.hostname.toLowerCase())) {
    throw new Error("Production API host must be listed in EXPO_PUBLIC_ALLOWED_API_HOSTS.");
  }
  if (PRODUCT === "unified") {
    throw new Error("Production store builds must set EXPO_PUBLIC_PRODUCT to driver, fleet, or customer.");
  }
}

const normalizedStage = STAGE.replace(/[^a-z0-9]+/g, "");
const defaultBundleBase = `com.kodekinetics.opstrax.${productConfig.id}`;
const defaultBundleId = STAGE === "production" ? defaultBundleBase : `${defaultBundleBase}.${normalizedStage}`;

const config: ExpoConfig = {
  name: productConfig.name,
  slug: productConfig.slug,
  version: "1.1.0",
  orientation: "portrait",
  ...(hasBundledAssets ? { icon: "./assets/icon.png" } : {}),
  userInterfaceStyle: "dark",
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
    "./plugins/with-no-inbound-linking",
  ],
  ios: {
    supportsTablet: true,
    bundleIdentifier: process.env.EXPO_PUBLIC_IOS_BUNDLE_ID?.trim() || defaultBundleId,
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
    package: process.env.EXPO_PUBLIC_ANDROID_PACKAGE?.trim() || defaultBundleId,
  },
  web: {
    ...(hasBundledAssets ? { favicon: "./assets/favicon.png" } : {}),
  },
  extra: {
    apiBaseUrl: API_BASE_URL,
    stage: STAGE,
    product: PRODUCT,
    appName: productConfig.name,
  },
};

export default config;
