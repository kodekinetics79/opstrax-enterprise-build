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

const storageStage = STAGE_LABEL.toLowerCase().replace(/[^a-z0-9._-]+/g, "-");
const storageProduct = APP_PRODUCT.toLowerCase().replace(/[^a-z0-9._-]+/g, "-");

export const SECURE_SESSION_KEY = `opstrax.${storageProduct}.${storageStage}.session.v3`;
export const SECURE_WORKSPACE_JOB_KEY = `opstrax.${storageProduct}.${storageStage}.job.v3`;
