import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getReportDatasets } from "@/data/developmentFleetSeedData";
import {
  fetchDatasets,
  fetchSavedReports,
  fetchSavedReport,
  createSavedReport,
  updateSavedReport,
  deleteSavedReport,
  runQuery,
  runSavedReport,
  exportCsv,
  exportSavedReportCsv,
  createScheduledReport,
  fetchAnalyticsExecutive,
  fetchAnalyticsOperations,
  fetchAnalyticsDispatch,
  fetchAnalyticsSafety,
  fetchAnalyticsMaintenance,
  fetchAnalyticsCustomer,
  fetchAnalyticsTrends,
  fetchAnalyticsInsights,
  type P8QueryBody,
  type SavedReportBody,
  type ScheduledReportBody,
} from "@/services/reportingApi";

// ── Dataset Registry ──────────────────────────────────────────────────────────

export function useDatasets() {
  return useQuery({
    queryKey: ["reporting", "datasets"],
    queryFn: async () => {
      try {
        return await fetchDatasets();
      } catch {
        return getReportDatasets() as Awaited<ReturnType<typeof fetchDatasets>>;
      }
    },
    staleTime: 5 * 60 * 1000,
  });
}

// ── Saved Reports ─────────────────────────────────────────────────────────────

export function useSavedReports() {
  return useQuery({
    queryKey: ["reporting", "saved"],
    queryFn: fetchSavedReports,
  });
}

export function useSavedReport(id: number | null) {
  return useQuery({
    queryKey: ["reporting", "saved", id],
    queryFn: () => fetchSavedReport(id!),
    enabled: id != null,
  });
}

export function useCreateSavedReport() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: SavedReportBody) => createSavedReport(body),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["reporting", "saved"] }),
  });
}

export function useUpdateSavedReport() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: number; body: SavedReportBody }) =>
      updateSavedReport(id, body),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["reporting", "saved"] }),
  });
}

export function useDeleteSavedReport() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => deleteSavedReport(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["reporting", "saved"] }),
  });
}

// ── Query Execution ───────────────────────────────────────────────────────────

export function useRunQuery(body: P8QueryBody | null) {
  return useQuery({
    queryKey: ["reporting", "run", body],
    queryFn: () => runQuery(body!),
    enabled:
      body != null &&
      body.fields.length > 0 &&
      body.datasetKey.length > 0,
    retry: false,
  });
}

export function useRunSavedReport() {
  return useMutation({
    mutationFn: (id: number) => runSavedReport(id),
  });
}

// ── Exports ───────────────────────────────────────────────────────────────────

export function useExportCsv() {
  return useMutation({ mutationFn: (body: P8QueryBody) => exportCsv(body) });
}

export function useExportSavedReportCsv() {
  return useMutation({ mutationFn: (id: number) => exportSavedReportCsv(id) });
}

// ── Scheduled Reports ─────────────────────────────────────────────────────────

export function useCreateScheduledReport() {
  return useMutation({
    mutationFn: (body: ScheduledReportBody) => createScheduledReport(body),
  });
}

// ── Analytics ─────────────────────────────────────────────────────────────────

export function useAnalyticsExecutive() {
  return useQuery({ queryKey: ["analytics", "executive"], queryFn: fetchAnalyticsExecutive });
}

export function useAnalyticsOperations() {
  return useQuery({ queryKey: ["analytics", "operations"], queryFn: fetchAnalyticsOperations });
}

export function useAnalyticsDispatch() {
  return useQuery({ queryKey: ["analytics", "dispatch"], queryFn: fetchAnalyticsDispatch });
}

export function useAnalyticsSafety() {
  return useQuery({ queryKey: ["analytics", "safety"], queryFn: fetchAnalyticsSafety });
}

export function useAnalyticsMaintenance() {
  return useQuery({ queryKey: ["analytics", "maintenance"], queryFn: fetchAnalyticsMaintenance });
}

export function useAnalyticsCustomer() {
  return useQuery({ queryKey: ["analytics", "customer"], queryFn: fetchAnalyticsCustomer });
}

export function useAnalyticsTrends() {
  return useQuery({ queryKey: ["analytics", "trends"], queryFn: fetchAnalyticsTrends });
}

export function useAnalyticsInsights() {
  return useQuery({ queryKey: ["analytics", "insights"], queryFn: fetchAnalyticsInsights });
}
