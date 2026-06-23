import { apiClient } from "./apiClient";

// ── Types ─────────────────────────────────────────────────────────────────────

export interface TelemetryMetrics {
  total: number;
  accepted: number;
  rejected: number;
  authFailed: number;
  replayDetected: number;
}

export interface AlertMetrics {
  total24h: number;
  openCount: number;
  criticalCount: number;
}

export interface SafetyMetrics {
  generated24h: number;
  openReview: number;
}

export interface DispatchMetrics {
  active: number;
  withExceptions: number;
  openExceptions: number;
}

export interface NotificationMetrics {
  pending: number;
  failed: number;
  acknowledged24h: number;
  notConfigured: number;
}

export interface ReportMetrics {
  succeeded: number;
  failed: number;
  activeSchedules: number;
  runs24h: number;
}

export interface ServiceStatusEntry {
  serviceName: string;
  lastHeartbeatUtc: string | null;
  lastRunUtc: string | null;
  lastRunStatus: string | null;
  consecutiveFailures: number;
  lastErrorSafe: string | null;
}

export interface IncidentSummary {
  openCount: number;
  criticalOpen: number;
}

export interface DbStatus {
  connected: boolean;
  latencyMs: number;
}

export interface OpsMetricsSnapshot {
  telemetry: TelemetryMetrics;
  alerts: AlertMetrics;
  safety: SafetyMetrics;
  dispatch: DispatchMetrics;
  notifications: NotificationMetrics;
  reports: ReportMetrics;
  services: ServiceStatusEntry[];
  incidents: IncidentSummary;
  database: DbStatus;
  capturedUtc: string;
}

export interface ServiceRunEntry {
  serviceName: string;
  lastHeartbeatAt: string | null;
  lastRunAt: string | null;
  lastRunStatus: string | null;
  consecutiveFailures: number;
  lastErrorSafe: string | null;
  lastRunId: number | null;
  lastStartedAt: string | null;
  lastFinishedAt: string | null;
  lastDurationMs: number | null;
  processedCount: number;
  failedCount: number;
  errorCode: string | null;
}

export interface ServiceRunHistoryEntry {
  id: number;
  serviceName: string;
  status: "running" | "succeeded" | "failed" | "degraded";
  startedAt: string;
  finishedAt: string | null;
  durationMs: number | null;
  processedCount: number;
  failedCount: number;
  errorCode: string | null;
  errorMessageSafe: string | null;
  heartbeatAt: string | null;
}

export interface PlatformIncident {
  id: number;
  companyId: number | null;
  severity: "critical" | "high" | "medium" | "low" | "info";
  sourceService: string;
  sourceEvent: string;
  status: "open" | "investigating" | "mitigated" | "resolved";
  title: string;
  safeDescription: string | null;
  openedAt: string;
  resolvedAt: string | null;
  assignedTo: string | null;
}

export interface IncidentsResponse {
  open: PlatformIncident[];
  recent: PlatformIncident[];
}

export interface ConfigIssue {
  check: string;
  level: "pass" | "warn" | "fail" | "info";
  message: string;
}

export interface ConfigCheckResult {
  status: "valid" | "warnings" | "invalid";
  failCount: number;
  warnCount: number;
  issues: ConfigIssue[];
}

export interface DeepHealthCheck {
  status: "healthy" | "degraded" | "unhealthy";
  service: string;
  utc: string;
  checks: {
    database: { status: string; latency_ms: number };
    services: Array<{
      name: string;
      status: string;
      last_heartbeat_utc: string | null;
      consecutive_failures: number;
    }>;
    config: {
      status: string;
      warnings: number;
      failures: number;
      issues: Array<{ Check: string; Level: string; Message: string }>;
    };
  };
}

// ── API functions ─────────────────────────────────────────────────────────────

export async function fetchOpsMetrics(): Promise<OpsMetricsSnapshot> {
  const res = await apiClient.get<{ data: OpsMetricsSnapshot }>("/api/ops/metrics");
  return res.data.data;
}

export async function fetchServiceRuns(): Promise<ServiceRunEntry[]> {
  const res = await apiClient.get<{ data: ServiceRunEntry[] }>("/api/ops/services");
  return res.data.data ?? [];
}

export async function fetchServiceRunHistory(
  serviceName: string
): Promise<ServiceRunHistoryEntry[]> {
  const res = await apiClient.get<{ data: ServiceRunHistoryEntry[] }>(
    `/api/ops/services/${encodeURIComponent(serviceName)}`
  );
  return res.data.data ?? [];
}

export async function fetchIncidents(): Promise<IncidentsResponse> {
  const res = await apiClient.get<{ data: IncidentsResponse }>("/api/ops/incidents");
  return res.data.data;
}

export async function updateIncidentStatus(
  id: number,
  status: string,
  assignedTo?: string
): Promise<void> {
  await apiClient.patch(`/api/ops/incidents/${id}/status`, { status, assignedTo });
}

export async function fetchConfigCheck(): Promise<ConfigCheckResult> {
  const res = await apiClient.get<{ data: ConfigCheckResult }>("/api/ops/config/check");
  return res.data.data;
}

export async function fetchDeepHealth(): Promise<DeepHealthCheck> {
  const res = await apiClient.get<DeepHealthCheck>("/health/deep");
  return res.data;
}
