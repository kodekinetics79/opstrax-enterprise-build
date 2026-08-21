import { useQuery } from "@tanstack/react-query";
import { API_BASE_URL, apiClient } from "./apiClient";

type AnyRecord = Record<string, unknown>;

export type RuntimeState = "Live" | "Staging" | "Demo Data" | "Stale" | "Disconnected" | "Unavailable";

export interface RuntimeDiagnostics {
  state: RuntimeState;
  frontendSha: string;
  frontendEnvironment: string;
  apiBaseUrl: string;
  apiSha: string;
  apiEnvironment: string;
  apiReady: boolean;
  databaseReady: boolean;
  workerContractReady: boolean;
  telemetryFresh: boolean;
  checkedAt: string;
  failureReason?: string;
}

const injectedApiBaseUrl = __OPSTRAX_API_BASE_URL__ || API_BASE_URL;

export const frontendBuild = Object.freeze({
  sha: __OPSTRAX_FRONTEND_SHA__,
  environment: __OPSTRAX_FRONTEND_ENVIRONMENT__,
  apiBaseUrl: injectedApiBaseUrl,
});

function record(value: unknown): AnyRecord {
  return value && typeof value === "object" ? value as AnyRecord : {};
}

function serviceRows(deep: AnyRecord): AnyRecord[] {
  const checks = record(deep.checks);
  const rows = checks.services;
  return Array.isArray(rows) ? rows.map(record) : [];
}

export function evaluateRuntimeTruth(readyValue: unknown, deepValue: unknown): RuntimeDiagnostics {
  const ready = record(readyValue);
  const deep = record(deepValue);
  const readyChecks = record(ready.checks);
  const deepChecks = record(deep.checks);
  const database = record(readyChecks.database);
  const fleetContract = record(readyChecks.fleet_production_contract);
  const workerContract = record(deepChecks.critical_worker_contract);
  const services = serviceRows(deep);
  const telemetryWorker = services.find((item) => String(item.name) === "TelemetryBackgroundService");
  const criticalServices = services.filter((item) => item.expected_critical === true);

  const apiReady = String(ready.status).toLowerCase() === "ready";
  const databaseReady = String(database.status).toLowerCase() === "connected";
  const productionApi = String(ready.environment).toLowerCase() === "production";
  const databaseContractReady = !productionApi || String(fleetContract.status).toLowerCase() === "ready";
  const criticalWorkersFresh =
    String(workerContract.status).toLowerCase() === "healthy" &&
    criticalServices.length > 0 &&
    criticalServices.every((item) => String(item.status).toLowerCase() === "healthy");
  const telemetryFresh =
    Boolean(telemetryWorker) &&
    String(telemetryWorker?.status).toLowerCase() === "healthy" &&
    !telemetryWorker?.reason;
  const deepHealthy = String(deep.status).toLowerCase() === "healthy";
  const verifiedLive = apiReady && databaseReady && databaseContractReady && criticalWorkersFresh && telemetryFresh && deepHealthy;
  const frontendEnvironment = frontendBuild.environment.toLowerCase();
  const demo = frontendEnvironment.includes("demo");
  const staging = frontendEnvironment.includes("stag") || frontendEnvironment === "preview" || frontendEnvironment === "development";

  let state: RuntimeState = "Unavailable";
  if (demo) state = "Demo Data";
  else if (verifiedLive && staging) state = "Staging";
  else if (verifiedLive) state = "Live";
  else if (!databaseReady) state = "Disconnected";
  else if (!criticalWorkersFresh || !telemetryFresh) state = "Stale";

  return {
    state,
    frontendSha: frontendBuild.sha,
    frontendEnvironment: frontendBuild.environment,
    apiBaseUrl: frontendBuild.apiBaseUrl || "same-origin",
    apiSha: String(ready.version || deep.version || "unknown"),
    apiEnvironment: String(ready.environment || deep.environment || "unknown"),
    apiReady,
    databaseReady: databaseReady && databaseContractReady,
    workerContractReady: criticalWorkersFresh,
    telemetryFresh,
    checkedAt: new Date().toISOString(),
    failureReason: String(ready.failure_reason || deep.failure_reason || "") || undefined,
  };
}

export async function fetchRuntimeDiagnostics(): Promise<RuntimeDiagnostics> {
  if (!frontendBuild.apiBaseUrl && typeof window !== "undefined" && !window.location.origin) {
    throw new Error("API base URL is not configured");
  }
  const [readyResponse, deepResponse] = await Promise.all([
    apiClient.get("/health/ready", { validateStatus: () => true }),
    apiClient.get("/health/deep", { validateStatus: () => true }),
  ]);
  return evaluateRuntimeTruth(readyResponse.data, deepResponse.data);
}

export function useRuntimeDiagnostics() {
  return useQuery({
    queryKey: ["runtime-diagnostics", frontendBuild.apiBaseUrl],
    queryFn: fetchRuntimeDiagnostics,
    staleTime: 15_000,
    refetchInterval: 30_000,
    retry: 1,
  });
}
