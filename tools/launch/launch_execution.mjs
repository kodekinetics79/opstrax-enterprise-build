import fs from "node:fs";
import { captureOperationResult, materializeOperation, validateOperationContract } from "./launch_plan.mjs";

const PRODUCTION_HOSTS = "opstrax.vercel.app,osptrax-fleet-management.onrender.com";
const ACKNOWLEDGEMENT = "I_UNDERSTAND_THIS_WRITES_TEST_DATA";

function hostSet(value) {
  return new Set(String(value || "").split(",").map((item) => item.trim().toLowerCase()).filter(Boolean));
}

function positiveInteger(name, value) {
  if (!/^\d+$/.test(String(value || "")) || Number(value) <= 0 || !Number.isSafeInteger(Number(value))) {
    throw new Error(`${name} must be a positive integer`);
  }
  return Number(value);
}

export function loadSecureEnvFile(filePath, env = process.env) {
  if (!fs.existsSync(filePath)) return env;
  const mode = fs.statSync(filePath).mode & 0o777;
  if ((mode & 0o077) !== 0) throw new Error(`${filePath} must have mode 0600 (or stricter)`);
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

export function resolveLaunchExecution(env = process.env) {
  if (env.LAUNCH_TARGET_ENV !== "staging") throw new Error("LAUNCH_TARGET_ENV must be exactly staging");
  let api;
  try {
    api = new URL(env.LAUNCH_API_URL || "");
  } catch {
    throw new Error("LAUNCH_API_URL must be an absolute HTTPS URL");
  }
  if (api.protocol !== "https:") throw new Error("LAUNCH_API_URL must use HTTPS");
  const hostname = api.hostname.toLowerCase();
  if (hostSet(env.LAUNCH_PRODUCTION_HOSTS || PRODUCTION_HOSTS).has(hostname)) {
    throw new Error("Launch execution refuses a known production host");
  }
  if (!hostSet(env.LAUNCH_STAGING_HOSTS).has(hostname)) {
    throw new Error("LAUNCH_API_URL host must be explicitly listed in LAUNCH_STAGING_HOSTS");
  }
  if (env.LAUNCH_DISPOSABLE_TENANT_ACK !== ACKNOWLEDGEMENT) {
    throw new Error("Disposable staging tenant acknowledgement is absent");
  }
  if (!env.LAUNCH_BEARER_TOKEN?.trim()) throw new Error("LAUNCH_BEARER_TOKEN is required");
  const customerId = positiveInteger("LAUNCH_CUSTOMER_ID", env.LAUNCH_CUSTOMER_ID);
  const operationCap = positiveInteger("LAUNCH_OPERATION_CAP", env.LAUNCH_OPERATION_CAP || "10000");
  if (operationCap > 20_000) throw new Error("LAUNCH_OPERATION_CAP cannot exceed the hard cap of 20000");
  return Object.freeze({
    apiBaseUrl: `${api.origin}${api.pathname.replace(/\/$/, "")}`,
    bearerToken: env.LAUNCH_BEARER_TOKEN.trim(),
    fixtures: Object.freeze({ customerId }),
    operationCap,
    runId: String(env.LAUNCH_RUN_ID || `launch-${Date.now()}`).replace(/[^a-zA-Z0-9-]/g, "-").slice(0, 48),
  });
}

export function validateExecutablePlan(plan) {
  if (!plan || !Array.isArray(plan.operations)) throw new Error("Plan operations are missing");
  if (plan.operationCount !== plan.operations.length || plan.operations.length < 10_000) {
    throw new Error("Executable launch plan must contain at least 10000 reconciled operations");
  }
  if (plan.authorization?.productionAllowed !== false) throw new Error("Plan must explicitly forbid production");
  for (const operation of plan.operations) validateOperationContract(operation);
  return true;
}

export function dryRunExecutablePlan(plan) {
  validateExecutablePlan(plan);
  const resources = {};
  const fixtures = { customerId: 1 };
  for (let index = 0; index < plan.operations.length; index += 1) {
    const operation = plan.operations[index];
    materializeOperation(operation, { resources, fixtures, timestamp: 1_700_000_000, runId: "dry-run" });
    resources[operation.clientReference] = operation.kind === "device"
      ? { id: index + 1, apiKey: "dry-run-key", hmacSecret: "dry-run-secret" }
      : { id: index + 1 };
  }
  return Object.freeze({ materializedOperations: plan.operations.length, networkCalls: 0, unresolvedReferences: 0 });
}

async function parseResponse(response) {
  const text = await response.text();
  if (!text) return {};
  try {
    return JSON.parse(text);
  } catch {
    throw new Error(`HTTP ${response.status} returned non-JSON content`);
  }
}

export async function executeLaunchPlan(plan, config, { fetchImpl = globalThis.fetch, onProgress = () => {} } = {}) {
  validateExecutablePlan(plan);
  if (plan.operations.length > config.operationCap) {
    throw new Error(`Plan contains ${plan.operations.length} operations, above LAUNCH_OPERATION_CAP=${config.operationCap}`);
  }
  if (typeof fetchImpl !== "function") throw new Error("A fetch implementation is required");
  const resources = {};
  let completed = 0;
  for (const operation of plan.operations) {
    const request = materializeOperation(operation, {
      resources,
      fixtures: config.fixtures,
      bearerToken: config.bearerToken,
      runId: config.runId,
    });
    const response = await fetchImpl(new URL(request.path, `${config.apiBaseUrl}/`), {
      method: request.method,
      headers: request.headers,
      body: request.rawBody,
      redirect: "error",
      signal: AbortSignal.timeout(30_000),
    });
    const payload = await parseResponse(response);
    if (!response.ok) throw new Error(`${operation.operationId} ${operation.kind} failed with HTTP ${response.status}`);
    captureOperationResult(operation, payload, resources);
    completed += 1;
    if (completed % 250 === 0 || completed === plan.operations.length) onProgress({ completed, total: plan.operations.length });
  }
  return Object.freeze({ completed, failed: 0, networkCalls: completed });
}
