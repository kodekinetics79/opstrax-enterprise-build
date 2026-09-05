import Constants from "expo-constants";

const extra = (Constants.expoConfig?.extra ?? {}) as Record<string, unknown>;

export type AppProduct = "driver" | "fleet" | "customer" | "unified";

const rawProduct = String(extra.product ?? process.env.EXPO_PUBLIC_PRODUCT ?? "unified").trim().toLowerCase();
export const APP_PRODUCT: AppProduct = (["driver", "fleet", "customer", "unified"] as const).includes(rawProduct as AppProduct)
  ? rawProduct as AppProduct
  : "unified";
export const APP_NAME = String(extra.appName ?? "OpsTrax Mobile");
export const STAGE_LABEL = String(extra.stage ?? "pilot");
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
const storageProduct = APP_PRODUCT.toLowerCase().replace(/[^a-z0-9._-]+/g, "-");

export const SECURE_SESSION_KEY = `opstrax.${storageProduct}.${storageStage}.session.v3`;
export const SECURE_WORKSPACE_JOB_KEY = `opstrax.${storageProduct}.${storageStage}.job.v3`;
