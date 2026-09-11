import Constants from "expo-constants";

const extra = (Constants.expoConfig?.extra ?? {}) as Record<string, unknown>;
const easExtra = extra.eas && typeof extra.eas === "object" ? extra.eas as Record<string, unknown> : {};

export type AppVariant = "driver" | "fleet" | "customer" | "unified";

function normalizeVariant(value: unknown): AppVariant | null {
  const normalized = String(value ?? "").trim().toLowerCase();
  return normalized === "driver" || normalized === "fleet" || normalized === "customer" || normalized === "unified"
    ? normalized
    : null;
}

function inferVariant(): AppVariant {
  const explicit = normalizeVariant(extra.appVariant)
    ?? normalizeVariant(process.env.EXPO_PUBLIC_APP_VARIANT)
    ?? normalizeVariant(process.env.EXPO_PUBLIC_PRODUCT);
  if (explicit && explicit !== "unified") return explicit;

  const nativeName = String(extra.appName ?? Constants.expoConfig?.name ?? "").trim().toLowerCase();
  const bundleId = String(Constants.expoConfig?.ios?.bundleIdentifier ?? Constants.expoConfig?.android?.package ?? "")
    .trim()
    .toLowerCase();
  const identity = `${nativeName} ${bundleId}`;

  if (/\bdriver\b/.test(identity) || bundleId.endsWith(".driver") || bundleId.includes(".driver.")) return "driver";
  if (/\bfleet\b/.test(identity) || bundleId.endsWith(".fleet") || bundleId.includes(".fleet.")) return "fleet";
  if (/\bcustomer\b/.test(identity) || bundleId.endsWith(".customer") || bundleId.includes(".customer.")) return "customer";

  return explicit ?? "unified";
}

export const APP_NAME = String(extra.appName ?? Constants.expoConfig?.name ?? "OpsTrax Mobile");
export const APP_VARIANT = inferVariant();
export const STAGE_LABEL = String(extra.stage ?? "14A");
export const API_BASE_URL =
  String(extra.apiBaseUrl ?? process.env.EXPO_PUBLIC_API_BASE_URL ?? process.env.EXPO_PUBLIC_DOTNET_API_URL ?? "http://localhost:8088")
    .trim()
    .replace(/\/+$/, "");
export const PRIVACY_URL = String(extra.privacyUrl ?? process.env.EXPO_PUBLIC_PRIVACY_URL ?? "").trim();
export const SUPPORT_URL = String(extra.supportUrl ?? process.env.EXPO_PUBLIC_SUPPORT_URL ?? "").trim();
export const ACCOUNT_DELETION_URL = String(extra.accountDeletionUrl ?? process.env.EXPO_PUBLIC_ACCOUNT_DELETION_URL ?? "").trim();
export const ACCOUNT_CREATION_ENABLED = Boolean(
  extra.accountCreationEnabled === true || String(process.env.EXPO_PUBLIC_ACCOUNT_CREATION_ENABLED ?? "").trim().toLowerCase() === "true",
);
export const EAS_PROJECT_ID = String(
  extra.easProjectId ?? easExtra.projectId ?? process.env.EXPO_PUBLIC_EAS_PROJECT_ID ?? process.env.EAS_PROJECT_ID ?? "",
).trim();

const storageStage = STAGE_LABEL.toLowerCase().replace(/[^a-z0-9._-]+/g, "-");

export const SECURE_SESSION_KEY = `opstrax.${APP_VARIANT}.${storageStage}.session.v3`;
export const SECURE_WORKSPACE_JOB_KEY = `opstrax.${APP_VARIANT}.${storageStage}.job.v3`;
export const SECURE_PUSH_TOKEN_KEY = `opstrax.${APP_VARIANT}.${storageStage}.push-token.v1`;
