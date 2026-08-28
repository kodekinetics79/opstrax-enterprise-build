import { useQuery } from "@tanstack/react-query";
import { API_BASE_URL, apiClient } from "./apiClient";

type AnyRecord = Record<string, unknown>;

export type RuntimeState = "Live" | "Starting" | "Staging" | "Demo Data" | "Stale" | "Disconnected" | "Unavailable";

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

export function evaluateRuntimeTruth(readyValue: unknown, deepValue: unknown): RuntimeDiagnostics {
  const ready = record(readyValue);
  const deep = record(deepValue);
  const readyChecks = record(ready.checks);
  const database = record(readyChecks.database);
  const fleetContract = record(readyChecks.fleet_production_contract);

  const apiReady = String(ready.status).toLowerCase() === "ready";
  const databaseReady = String(database.status).toLowerCase() === "connected";
  const productionApi = String(ready.environment).toLowerCase() === "production";
  const databaseContractReady = !productionApi || String(fleetContract.status).toLowerCase() === "ready";
  const workerViolations = Number(fleetContract.critical_worker_violations ?? -1);
  const workerStartupGrace = fleetContract.critical_worker_startup_grace_active === true;
  const criticalWorkersFresh = databaseContractReady && workerViolations === 0 && !workerStartupGrace;
  const criticalWorkersStarting = databaseContractReady && workerViolations === 0 && workerStartupGrace;
  // The public readiness contract already aggregates every expected critical
  // worker, including TelemetryBackgroundService. Browser clients must not call
  // /health/deep because that operator endpoint deliberately requires a secret.
  const telemetryFresh = criticalWorkersFresh;
  const verifiedLive = apiReady && databaseReady && databaseContractReady && criticalWorkersFresh;
  const frontendEnvironment = frontendBuild.environment.toLowerCase();
  const demo = frontendEnvironment.includes("demo");
  const staging = frontendEnvironment.includes("stag") || frontendEnvironment === "preview" || frontendEnvironment === "development";

  let state: RuntimeState = "Unavailable";
  if (demo) state = "Demo Data";
  else if (verifiedLive && staging) state = "Staging";
  else if (verifiedLive) state = "Live";
  else if (apiReady && databaseReady && databaseContractReady && criticalWorkersStarting) state = "Starting";
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
  const readyResponse = await apiClient.get("/health/ready", { validateStatus: () => true });
  return evaluateRuntimeTruth(readyResponse.data, {});
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
