import fs from "node:fs";
import path from "node:path";

const AUTH_KEYS = [
  "E2E_TENANT_AUTH_STATE",
  "E2E_DRIVER_AUTH_STATE",
  "E2E_CUSTOMER_AUTH_STATE",
  "E2E_PLATFORM_AUTH_STATE",
];

const LOOPBACK_HOSTS = new Set(["127.0.0.1", "localhost", "::1"]);
const VALID_ENVIRONMENTS = new Set(["local", "staging", "production"]);
const IOT_HARDWARE_ROLES = new Set([
  "gps", "eld", "dashcam", "obd-ii", "j1939/can", "temperature", "fuel", "tire", "ble gateway", "other",
]);

function nonBlank(value) {
  return typeof value === "string" && value.trim().length > 0;
}

function parseUrl(name, value) {
  let url;
  try {
    url = new URL(value);
  } catch {
    throw new Error(`${name} must be an absolute http(s) URL`);
  }
  if (!new Set(["http:", "https:"]).has(url.protocol)) {
    throw new Error(`${name} must use http or https`);
  }
  url.pathname = url.pathname.replace(/\/$/, "");
  return url;
}

function productionHosts(env) {
  return new Set(
    (env.E2E_PRODUCTION_HOSTS || "opstrax.vercel.app,osptrax-fleet-management.onrender.com")
      .split(",")
      .map((host) => host.trim().toLowerCase())
      .filter(Boolean),
  );
}

export function resolveTarget(env = process.env) {
  const ui = parseUrl("E2E_UI_BASE_URL", env.E2E_UI_BASE_URL || "http://127.0.0.1:4173");
  const api = parseUrl("E2E_API_BASE_URL", env.E2E_API_BASE_URL || ui.origin);
  const declared = (env.E2E_TARGET_ENV || (LOOPBACK_HOSTS.has(ui.hostname) ? "local" : "production")).toLowerCase();
  if (!VALID_ENVIRONMENTS.has(declared)) {
    throw new Error("E2E_TARGET_ENV must be local, staging, or production");
  }

  const knownProduction = productionHosts(env);
  const detectedProduction = knownProduction.has(ui.hostname.toLowerCase()) || knownProduction.has(api.hostname.toLowerCase());
  if (detectedProduction && declared !== "production") {
    throw new Error("A known production host cannot be declared local or staging");
  }
  if (declared === "local" && (!LOOPBACK_HOSTS.has(ui.hostname) || !LOOPBACK_HOSTS.has(api.hostname))) {
    throw new Error("Local mode only accepts loopback UI and API hosts");
  }
  if (declared === "staging" && (ui.protocol !== "https:" || api.protocol !== "https:")) {
    throw new Error("Staging UI and API URLs must use HTTPS");
  }

  const isProduction = declared === "production" || detectedProduction;
  const configuredAuth = AUTH_KEYS.filter((key) => nonBlank(env[key]));
  if (isProduction && configuredAuth.length > 0) {
    throw new Error(`Authenticated browser state is forbidden on production (${configuredAuth.join(", ")})`);
  }
  if (isProduction && env.E2E_ALLOW_STAGING_MUTATIONS === "true") {
    throw new Error("Mutation mode is forbidden on production");
  }

  return Object.freeze({
    environment: isProduction ? "production" : declared,
    isProduction,
    uiBaseUrl: ui.origin + ui.pathname,
    apiBaseUrl: api.origin + api.pathname,
    uiHostname: ui.hostname.toLowerCase(),
    apiHostname: api.hostname.toLowerCase(),
  });
}

export function assertRequestAllowed(target, method) {
  const normalized = String(method || "").toUpperCase();
  if (target.isProduction && normalized !== "GET") {
    throw new Error(`Production browser checks are GET-only; blocked ${normalized || "UNKNOWN"}`);
  }
}

export function mutationGate(target, env = process.env) {
  const reasons = [];
  const stagingHosts = new Set(
    (env.E2E_STAGING_HOSTS || "")
      .split(",")
      .map((host) => host.trim().toLowerCase())
      .filter(Boolean),
  );
  if (target.environment !== "staging" || target.isProduction) reasons.push("target is not isolated staging");
  if (!stagingHosts.has(target.uiHostname) || !stagingHosts.has(target.apiHostname)) {
    reasons.push("UI and API hosts are not both in E2E_STAGING_HOSTS");
  }
  if (env.E2E_ALLOW_STAGING_MUTATIONS !== "true") reasons.push("E2E_ALLOW_STAGING_MUTATIONS is not true");
  if (env.E2E_DISPOSABLE_TENANT_ACK !== "I_UNDERSTAND_THIS_WRITES_TEST_DATA") reasons.push("disposable-tenant acknowledgement is absent");
  if (!nonBlank(env.E2E_TENANT_AUTH_STATE)) reasons.push("tenant auth state is absent");
  if (!/^\d+$/.test(env.E2E_CANARY_VEHICLE_ID || "")) reasons.push("numeric canary vehicle id is absent");
  return Object.freeze({ enabled: reasons.length === 0, reasons });
}

export function iotLifecycleGate(target, env = process.env) {
  const base = mutationGate(target, env);
  const reasons = [...base.reasons];
  if (env.E2E_IOT_LIFECYCLE_ACK !== "I_UNDERSTAND_THIS_PROVISIONS_AND_REVOKES_A_REAL_DEVICE") {
    reasons.push("IoT lifecycle acknowledgement is absent");
  }
  if (!/^\d+$/.test(env.E2E_IOT_SOURCE_VEHICLE_ID || "")) reasons.push("numeric IoT source vehicle id is absent");
  if (!/^\d+$/.test(env.E2E_IOT_TARGET_VEHICLE_ID || "")) reasons.push("numeric IoT target vehicle id is absent");
  if (!/^\d+$/.test(env.E2E_IOT_OOS_VEHICLE_ID || "")) reasons.push("numeric IoT out-of-service vehicle id is absent");
  if (env.E2E_IOT_SOURCE_VEHICLE_ID === env.E2E_IOT_TARGET_VEHICLE_ID) {
    reasons.push("IoT source and target vehicles must differ");
  }
  if ([env.E2E_IOT_SOURCE_VEHICLE_ID, env.E2E_IOT_TARGET_VEHICLE_ID].includes(env.E2E_IOT_OOS_VEHICLE_ID)) {
    reasons.push("IoT out-of-service vehicle must differ from source and target vehicles");
  }
  const category = env.E2E_IOT_DEVICE_CATEGORY?.trim().toLowerCase();
  const role = env.E2E_IOT_DEVICE_ROLE?.trim().toLowerCase();
  if (!category || !IOT_HARDWARE_ROLES.has(category)) reasons.push("IoT device category is absent or unsupported");
  if (!role || !IOT_HARDWARE_ROLES.has(role)) reasons.push("IoT installation role is absent or unsupported");
  if (category && role && category !== role) reasons.push("IoT device category and installation role must match");
  if (!nonBlank(env.E2E_CROSS_TENANT_AUTH_STATE)) reasons.push("cross-tenant auth state is absent");
  else if (!fs.existsSync(path.resolve(env.E2E_CROSS_TENANT_AUTH_STATE.trim()))) {
    reasons.push("cross-tenant auth state file is missing");
  }
  if (!nonBlank(env.E2E_DRIVER_AUTH_STATE)) reasons.push("driver auth state is absent");
  else if (!fs.existsSync(path.resolve(env.E2E_DRIVER_AUTH_STATE.trim()))) reasons.push("driver auth state file is missing");
  for (const [key, label] of [
    ["E2E_IOT_DRIVER_ID", "driver"], ["E2E_IOT_JOB_ID", "job"],
    ["E2E_IOT_ROUTE_ID", "route"], ["E2E_IOT_TRIP_ID", "trip"],
  ]) if (!/^\d+$/.test(env[key] || "")) reasons.push(`numeric IoT ${label} id is absent`);
  return Object.freeze({ enabled: reasons.length === 0, reasons });
}

export function authStateFor(role, parallelIndex, env = process.env) {
  const key = `E2E_${role.toUpperCase()}_AUTH_STATE`;
  const configured = env[key]?.trim();
  if (!configured) return undefined;
  const resolved = path.resolve(configured.replaceAll("{worker}", String(parallelIndex)));
  return fs.existsSync(resolved) ? resolved : undefined;
}
