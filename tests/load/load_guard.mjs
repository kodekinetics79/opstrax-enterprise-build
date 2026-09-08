import fs from "node:fs";

const KNOWN_PRODUCTION_HOSTS = "opstrax.vercel.app,osptrax-fleet-management.onrender.com";
const ACKNOWLEDGEMENT = "I_UNDERSTAND_THIS_IS_AN_ISOLATED_STAGING_TENANT";

export const PROFILE_LIMITS = Object.freeze({
  smoke: Object.freeze({ iterationsPerSecond: 1, durationSeconds: 30, maxVus: 4 }),
  load: Object.freeze({ iterationsPerSecond: 5, durationSeconds: 300, maxVus: 20 }),
  stress: Object.freeze({ iterationsPerSecond: 10, durationSeconds: 600, maxVus: 50 }),
});

// Dataset size is a different axis from request intensity. A 50K-fleet acceptance
// run means the isolated staging tenant contains at least 50,000 vehicles/assets in
// the declared evidence population; it does NOT authorize 50,000 VUs or unbounded RPS.
export const DATASET_TIERS = Object.freeze({
  "1k": 1_000,
  "2_5k": 2_500,
  "5k": 5_000,
  "10k": 10_000,
  "25k": 25_000,
  "50k": 50_000,
});

function splitHosts(value) {
  return new Set(String(value || "").split(",").map((item) => item.trim().toLowerCase()).filter(Boolean));
}

function boundedInteger(name, value, maximum) {
  if (!/^\d+$/.test(String(value)) || Number(value) <= 0 || !Number.isSafeInteger(Number(value))) {
    throw new Error(`${name} must be a positive integer`);
  }
  if (Number(value) > maximum) throw new Error(`${name} exceeds the ${maximum} safety cap`);
  return Number(value);
}

function safeGetPath(name, value) {
  const path = String(value || "");
  if (!path.startsWith("/") || path.startsWith("//") || path.includes("..") || path.includes("?") || path.includes("#")) {
    throw new Error(`${name} must be a same-origin absolute path without query or traversal`);
  }
  return path;
}

export function loadSecureEnvFile(filePath, env = process.env) {
  if (!fs.existsSync(filePath)) return env;
  if ((fs.statSync(filePath).mode & 0o077) !== 0) throw new Error(`${filePath} must have mode 0600 (or stricter)`);
  for (const rawLine of fs.readFileSync(filePath, "utf8").split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line || line.startsWith("#")) continue;
    const separator = line.indexOf("=");
    if (separator <= 0) throw new Error(`Invalid environment line in ${filePath}`);
    const key = line.slice(0, separator).trim();
    let value = line.slice(separator + 1).trim();
    if ((value.startsWith('"') && value.endsWith('"')) || (value.startsWith("'") && value.endsWith("'"))) value = value.slice(1, -1);
    if (!(key in env)) env[key] = value;
  }
  return env;
}

export function resolveLoadConfig(env = process.env) {
  if (env.LOAD_TARGET_ENV !== "staging") throw new Error("LOAD_TARGET_ENV must be exactly staging");
  let url;
  try {
    url = new URL(env.LOAD_API_BASE_URL || "");
  } catch {
    throw new Error("LOAD_API_BASE_URL must be an absolute HTTPS URL");
  }
  if (url.protocol !== "https:") throw new Error("LOAD_API_BASE_URL must use HTTPS");
  const hostname = url.hostname.toLowerCase();
  if (splitHosts(env.LOAD_PRODUCTION_HOSTS || KNOWN_PRODUCTION_HOSTS).has(hostname)) {
    throw new Error("Load tooling refuses a known production host");
  }
  if (!splitHosts(env.LOAD_STAGING_HOSTS).has(hostname)) {
    throw new Error("LOAD_API_BASE_URL host must be explicitly listed in LOAD_STAGING_HOSTS");
  }
  if (env.LOAD_ISOLATED_STAGING_ACK !== ACKNOWLEDGEMENT) {
    throw new Error("Isolated staging tenant acknowledgement is absent");
  }
  if (!env.LOAD_BEARER_TOKEN?.trim()) throw new Error("LOAD_BEARER_TOKEN is required");

  const profile = String(env.LOAD_PROFILE || "smoke").toLowerCase();
  const limits = PROFILE_LIMITS[profile];
  if (!limits) throw new Error("LOAD_PROFILE must be smoke, load, or stress");
  const iterationsPerSecond = boundedInteger(
    "LOAD_ITERATIONS_PER_SECOND",
    env.LOAD_ITERATIONS_PER_SECOND || limits.iterationsPerSecond,
    limits.iterationsPerSecond,
  );
  const durationSeconds = boundedInteger(
    "LOAD_DURATION_SECONDS",
    env.LOAD_DURATION_SECONDS || limits.durationSeconds,
    limits.durationSeconds,
  );
  const maxVus = boundedInteger("LOAD_MAX_VUS", env.LOAD_MAX_VUS || limits.maxVus, limits.maxVus);
  if (maxVus > 50 || durationSeconds > 600 || iterationsPerSecond > 10) throw new Error("Load hard cap exceeded");

  const datasetTier = String(env.LOAD_DATASET_TIER || "1k").toLowerCase();
  const minimumVehicles = DATASET_TIERS[datasetTier];
  if (!minimumVehicles) throw new Error("LOAD_DATASET_TIER must be one of 1k, 2_5k, 5k, 10k, 25k, 50k");
  const datasetVehicleCount = boundedInteger(
    "LOAD_DATASET_VEHICLE_COUNT",
    env.LOAD_DATASET_VEHICLE_COUNT || minimumVehicles,
    250_000,
  );
  if (datasetVehicleCount < minimumVehicles) {
    throw new Error(`LOAD_DATASET_VEHICLE_COUNT is below the ${datasetTier} tier minimum of ${minimumVehicles}`);
  }

  return Object.freeze({
    profile,
    apiBaseUrl: `${url.origin}${url.pathname.replace(/\/$/, "")}`,
    hostname,
    bearerToken: env.LOAD_BEARER_TOKEN.trim(),
    publicPath: safeGetPath("LOAD_PUBLIC_PATH", env.LOAD_PUBLIC_PATH || "/health/live"),
    authenticatedPath: safeGetPath("LOAD_AUTHENTICATED_PATH", env.LOAD_AUTHENTICATED_PATH || "/api/vehicles/summary"),
    iterationsPerSecond,
    maximumRequestsPerSecond: iterationsPerSecond * 2,
    durationSeconds,
    maxVus,
    datasetTier,
    datasetMinimumVehicles: minimumVehicles,
    datasetVehicleCount,
  });
}

export function sanitizedConfig(config) {
  return Object.freeze({
    profile: config.profile,
    apiBaseUrl: config.apiBaseUrl,
    publicPath: config.publicPath,
    authenticatedPath: config.authenticatedPath,
    iterationsPerSecond: config.iterationsPerSecond,
    maximumRequestsPerSecond: config.maximumRequestsPerSecond,
    durationSeconds: config.durationSeconds,
    maxVus: config.maxVus,
    datasetTier: config.datasetTier,
    datasetMinimumVehicles: config.datasetMinimumVehicles,
    datasetVehicleCount: config.datasetVehicleCount,
    methods: ["GET"],
  });
}
