import type { ExpoConfig } from "expo/config";
import { existsSync } from "node:fs";
import { resolve } from "node:path";

const API_BASE_URL =
  process.env.EXPO_PUBLIC_API_BASE_URL?.trim() ||
  process.env.EXPO_PUBLIC_DOTNET_API_URL?.trim() ||
  "http://localhost:8088";
const STAGE = process.env.EXPO_PUBLIC_STAGE?.trim().toLowerCase() || "pilot";
const isProductionBuild = process.env.EAS_BUILD_PROFILE?.startsWith("production") || STAGE === "production";
const allowedApiHosts = (process.env.EXPO_PUBLIC_ALLOWED_API_HOSTS ?? "")
  .split(",")
  .map((value: string) => value.trim().toLowerCase())
  .filter(Boolean);
const hasBundledAssets = existsSync(resolve(__dirname, "assets/icon.png"));

type AppVariant = "driver" | "fleet" | "customer" | "unified";

function resolveVariant(): AppVariant {
  const raw = process.env.EXPO_PUBLIC_APP_VARIANT?.trim().toLowerCase() || "unified";
  if (["driver", "fleet", "customer", "unified"].includes(raw)) return raw as AppVariant;
  throw new Error(`Unsupported EXPO_PUBLIC_APP_VARIANT: ${raw}`);
}

const APP_VARIANT = resolveVariant();

const PRODUCTS: Record<AppVariant, { name: string; slug: string; bundle: string; locationPurpose?: string }> = {
  driver: {
    name: "OpsTrax Driver",
    slug: "opstrax-driver",
    bundle: "com.opstrax.driver",
    locationPurpose: "Allow OpsTrax Driver to attach your location to active-trip and delivery evidence you choose to submit.",
  },
  fleet: {
    name: "OpsTrax Fleet",
    slug: "opstrax-fleet",
    bundle: "com.opstrax.fleet",
    locationPurpose: "Allow OpsTrax Fleet to use your location only for an authorized operational workflow that needs the device location.",
  },
  customer: {
    name: "OpsTrax Customer",
    slug: "opstrax-customer",
    bundle: "com.opstrax.customer",
  },
  unified: {
    name: "OpsTrax Mobile",
    slug: "opstrax-mobile",
    bundle: "com.opstrax.mobile",
    locationPurpose: "Allow OpsTrax to attach your current location to proof you choose to submit.",
  },
};

const product = PRODUCTS[APP_VARIANT];
const stageSuffix = STAGE.replace(/[^a-z0-9]+/g, "");
const defaultBundle = STAGE === "production" ? product.bundle : `${product.bundle}.${stageSuffix}`;
const plugins: NonNullable<ExpoConfig["plugins"]> = [
  "expo-secure-store",
  [
    "expo-image-picker",
    {
      cameraPermission: `Allow ${product.name} to capture delivery and inspection evidence when the workflow requires it.`,
      photosPermission: `Allow ${product.name} to select delivery and inspection evidence when the workflow requires it.`,
      microphonePermission: false,
    },
  ],
  "./plugins/with-no-inbound-linking",
];
if (APP_VARIANT !== "customer") plugins.splice(2, 0, "expo-location");

if (isProductionBuild) {
  if (APP_VARIANT === "unified") {
    throw new Error("Production OpsTrax builds must set EXPO_PUBLIC_APP_VARIANT to driver, fleet, or customer.");
  }
  const apiUrl = new URL(API_BASE_URL);
  if (apiUrl.protocol !== "https:" || ["localhost", "127.0.0.1", "::1"].includes(apiUrl.hostname)) {
    throw new Error("Production OpsTrax mobile builds require a non-loopback HTTPS API URL.");
  }
  if (!allowedApiHosts.length || !allowedApiHosts.includes(apiUrl.hostname.toLowerCase())) {
    throw new Error("Production API host must be listed in EXPO_PUBLIC_ALLOWED_API_HOSTS.");
  }
}

const config: ExpoConfig = {
  name: product.name,
  slug: product.slug,
  version: "1.1.0",
  orientation: "portrait",
  ...(hasBundledAssets ? { icon: "./assets/icon.png" } : {}),
  userInterfaceStyle: "dark",
  plugins,
  ios: {
    supportsTablet: true,
    bundleIdentifier: process.env.EXPO_PUBLIC_IOS_BUNDLE_ID?.trim() || defaultBundle,
    ...(product.locationPurpose ? {
      infoPlist: {
        NSLocationWhenInUseUsageDescription: product.locationPurpose,
      },
    } : {}),
  },
  android: {
    ...(hasBundledAssets ? { adaptiveIcon: {
      backgroundColor: "#07111f",
      foregroundImage: "./assets/android-icon-foreground.png",
      backgroundImage: "./assets/android-icon-background.png",
      monochromeImage: "./assets/android-icon-monochrome.png",
    } } : {}),
    predictiveBackGestureEnabled: false,
    package: process.env.EXPO_PUBLIC_ANDROID_PACKAGE?.trim() || defaultBundle,
  },
  web: {
    ...(hasBundledAssets ? { favicon: "./assets/favicon.png" } : {}),
  },
  extra: {
    apiBaseUrl: API_BASE_URL,
    stage: STAGE,
    appName: product.name,
    appVariant: APP_VARIANT,
  },
};

export default config;
