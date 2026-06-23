import { apiClient } from "./apiClient";

// ── Types ─────────────────────────────────────────────────────────────────────

export interface ReportFieldDef {
  key: string;
  label: string;
  type: "string" | "number" | "date" | "boolean" | "enum";
  sensitive?: boolean;
  allowedOperators: string[];
  sortable?: boolean;
  groupable?: boolean;
  exportable?: boolean;
}

export interface ReportDatasetMeta {
  key: string;
  label: string;
  requiredPermission: string;
  fields: ReportFieldDef[];
}

export interface P8Filter {
  field: string;
  operator: string;
  value: string;
}

export interface P8Sort {
  field: string;
  direction: "asc" | "desc";
}

export interface P8QueryBody {
  datasetKey: string;
  fields: string[];
  filters?: P8Filter[];
  sort?: P8Sort;
  groupBy?: string;
  page?: number;
  pageSize?: number;
}

export interface P8QueryResult {
  rows: Record<string, unknown>[];
  meta: {
    total: number;
    page: number;
    pageSize: number;
    datasetKey: string;
    executionMs: number;
  };
}

export type ReportVisibility = "private" | "role_shared" | "tenant_shared";

export interface SavedReport {
  id: number;
  companyId: number;
  ownerUserId: number;
  name: string;
  description?: string;
  datasetKey: string;
  selectedFieldsJson: string;
  filtersJson?: string;
  sortJson?: string;
  groupByJson?: string;
  visibility: ReportVisibility;
  sharedRole?: string;
  lastRunAt?: string;
  createdAt: string;
  updatedAt?: string;
}

export interface SavedReportBody {
  name: string;
  description?: string;
  datasetKey: string;
  fields: string[];
  filters?: P8Filter[];
  sort?: P8Sort;
  groupBy?: string;
  visibility: ReportVisibility;
  sharedRole?: string;
}

export interface ScheduledReportBody {
  savedReportId: number;
  schedule: "daily" | "weekly" | "monthly";
  format: "csv" | "xlsx" | "pdf";
  recipientType: "roles" | "users";
  recipients: string;
}

// ── Datasets ─────────────────────────────────────────────────────────────────

export async function fetchDatasets(): Promise<ReportDatasetMeta[]> {
  const { data } = await apiClient.get<{ data: ReportDatasetMeta[] }>("/api/reports/datasets");
  return data.data;
}

// ── Saved Reports ─────────────────────────────────────────────────────────────

export async function fetchSavedReports(): Promise<SavedReport[]> {
  const { data } = await apiClient.get<{ data: SavedReport[] }>("/api/reports/saved");
  return data.data;
}

export async function fetchSavedReport(id: number): Promise<SavedReport> {
  const { data } = await apiClient.get<{ data: SavedReport }>(`/api/reports/saved/${id}`);
  return data.data;
}

export async function createSavedReport(body: SavedReportBody): Promise<{ id: number }> {
  const { data } = await apiClient.post<{ data: { id: number } }>("/api/reports/saved", body);
  return data.data;
}

export async function updateSavedReport(id: number, body: SavedReportBody): Promise<void> {
  await apiClient.put(`/api/reports/saved/${id}`, body);
}

export async function deleteSavedReport(id: number): Promise<void> {
  await apiClient.delete(`/api/reports/saved/${id}`);
}

// ── Query Execution ───────────────────────────────────────────────────────────

export async function runQuery(body: P8QueryBody): Promise<P8QueryResult> {
  const { data } = await apiClient.post<{ data: P8QueryResult }>("/api/reports/run", body);
  return data.data;
}

export async function runSavedReport(id: number): Promise<P8QueryResult> {
  const { data } = await apiClient.post<{ data: P8QueryResult }>(`/api/reports/saved/${id}/run`);
  return data.data;
}

// ── Exports ───────────────────────────────────────────────────────────────────

export async function exportCsv(body: P8QueryBody): Promise<void> {
  const response = await apiClient.post("/api/reports/export", body, {
    responseType: "blob",
  });
  const url = URL.createObjectURL(new Blob([response.data], { type: "text/csv" }));
  const a = document.createElement("a");
  a.href = url;
  a.download = response.headers["content-disposition"]
    ?.split("filename=")[1]
    ?.replace(/"/g, "") ?? "report.csv";
  a.click();
  URL.revokeObjectURL(url);
}

export async function exportSavedReportCsv(id: number): Promise<void> {
  const response = await apiClient.get(`/api/reports/saved/${id}/export`, {
    responseType: "blob",
  });
  const url = URL.createObjectURL(new Blob([response.data], { type: "text/csv" }));
  const a = document.createElement("a");
  a.href = url;
  a.download = response.headers["content-disposition"]
    ?.split("filename=")[1]
    ?.replace(/"/g, "") ?? "report.csv";
  a.click();
  URL.revokeObjectURL(url);
}

// ── Scheduled Reports ─────────────────────────────────────────────────────────

export async function createScheduledReport(body: ScheduledReportBody): Promise<{ id: number }> {
  const { data } = await apiClient.post<{ data: { id: number } }>("/api/reports/scheduled/p8", body);
  return data.data;
}

// ── Analytics KPI ─────────────────────────────────────────────────────────────

export async function fetchAnalyticsExecutive() {
  const { data } = await apiClient.get<{ data: unknown }>("/api/analytics/executive");
  return data.data as Record<string, unknown>;
}

export async function fetchAnalyticsOperations() {
  const { data } = await apiClient.get<{ data: unknown }>("/api/analytics/operations");
  return data.data as Record<string, unknown>;
}

export async function fetchAnalyticsDispatch() {
  const { data } = await apiClient.get<{ data: unknown }>("/api/analytics/dispatch");
  return data.data as Record<string, unknown>;
}

export async function fetchAnalyticsSafety() {
  const { data } = await apiClient.get<{ data: unknown }>("/api/analytics/safety");
  return data.data as Record<string, unknown>;
}

export async function fetchAnalyticsMaintenance() {
  const { data } = await apiClient.get<{ data: unknown }>("/api/analytics/maintenance");
  return data.data as Record<string, unknown>;
}

export async function fetchAnalyticsCustomer() {
  const { data } = await apiClient.get<{ data: unknown }>("/api/analytics/customer");
  return data.data as Record<string, unknown>;
}

export async function fetchAnalyticsTrends() {
  const { data } = await apiClient.get<{ data: unknown }>("/api/analytics/trends");
  return data.data as Record<string, unknown>;
}

export async function fetchAnalyticsInsights() {
  const { data } = await apiClient.get<{ data: unknown[] }>("/api/analytics/insights");
  return data.data;
}
