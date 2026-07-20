import Constants from "expo-constants";

const extra = (Constants.expoConfig?.extra ?? {}) as Record<string, unknown>;

export const APP_NAME = String(extra.appName ?? "OpsTrax Mobile");
export const STAGE_LABEL = String(extra.stage ?? "14A");
export const API_BASE_URL =
  String(extra.apiBaseUrl ?? process.env.EXPO_PUBLIC_API_BASE_URL ?? process.env.EXPO_PUBLIC_DOTNET_API_URL ?? "http://localhost:8088")
    .trim()
    .replace(/\/+$/, "");

export const SECURE_SESSION_KEY = "opstrax.mobile.session.v1";
export const SECURE_WORKSPACE_JOB_KEY = "opstrax.mobile.job.v1";

