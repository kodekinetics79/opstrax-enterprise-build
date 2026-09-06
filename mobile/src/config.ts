import Constants from "expo-constants";

const extra = (Constants.expoConfig?.extra ?? {}) as Record<string, unknown>;

export type AppVariant = "driver" | "fleet" | "customer" | "unified";

function parseAppVariant(value: unknown): AppVariant {
  const normalized = String(value ?? "unified").trim().toLowerCase();
  return normalized === "driver" || normalized === "fleet" || normalized === "customer" || normalized === "unified"
    ? normalized
    : "unified";
}

export const APP_NAME = String(extra.appName ?? "OpsTrax Mobile");
export const APP_VARIANT = parseAppVariant(extra.appVariant ?? process.env.EXPO_PUBLIC_APP_VARIANT);
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

const storageStage = STAGE_LABEL.toLowerCase().replace(/[^a-z0-9._-]+/g, "-");

export const SECURE_SESSION_KEY = `opstrax.${APP_VARIANT}.${storageStage}.session.v3`;
export const SECURE_WORKSPACE_JOB_KEY = `opstrax.${APP_VARIANT}.${storageStage}.job.v3`;
