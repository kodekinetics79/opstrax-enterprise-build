import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { AnyRecord } from "@/types";
import { reportsApi, kpiApi, slaApi, auditApi, executiveApi } from "@/services/batch7Api";
import { tripApi } from "@/services/tripApi";

// ── Reports ──────────────────────────────────────────────────────────────────
export function useReportCatalog() {
  return useQuery({ queryKey: ["reports-catalog"], queryFn: reportsApi.catalog, staleTime: 60_000 });
}
export function useReportsSummary() {
  return useQuery({ queryKey: ["reports-summary"], queryFn: reportsApi.summary, staleTime: 30_000 });
}
export function useReportRuns() {
  return useQuery({ queryKey: ["reports-runs"], queryFn: reportsApi.runs, staleTime: 15_000 });
}
export function useRunReport() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ key, filters }: { key: string; filters?: Record<string, unknown> }) =>
      reportsApi.runReport(key, filters),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["reports-runs"] }); qc.invalidateQueries({ queryKey: ["reports-summary"] }); },
  });
}
export function useScheduledReports() {
  return useQuery({ queryKey: ["reports-scheduled"], queryFn: reportsApi.scheduled, staleTime: 30_000 });
}
export function useCreateScheduledReport() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: Record<string, unknown>) => reportsApi.createScheduled(body),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["reports-scheduled"] }),
  });
}
export function usePauseScheduledReport() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => reportsApi.pauseScheduled(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["reports-scheduled"] }),
  });
}
export function useResumeScheduledReport() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => reportsApi.resumeScheduled(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["reports-scheduled"] }),
  });
}
export function useReportExports() {
  return useQuery({ queryKey: ["reports-exports"], queryFn: reportsApi.exports, staleTime: 15_000 });
}
export function useCreateReportExport() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: Record<string, unknown>) => reportsApi.createExport(body),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["reports-exports"] }),
  });
}
export function useReportsAiRecs() {
  return useQuery({ queryKey: ["reports-ai-recs"], queryFn: reportsApi.aiRecommendations, staleTime: 60_000 });
}

// ── KPI ───────────────────────────────────────────────────────────────────────
export function useKpiMetrics() {
  return useQuery({ queryKey: ["kpi-metrics"], queryFn: kpiApi.metrics, staleTime: 30_000 });
}
export function useKpiSummary() {
  return useQuery({ queryKey: ["kpi-summary"], queryFn: kpiApi.summary, staleTime: 30_000 });
}
export function useKpiTargets() {
  return useQuery({ queryKey: ["kpi-targets"], queryFn: kpiApi.targets, staleTime: 60_000 });
}
export function useKpiAiRecs() {
  return useQuery({ queryKey: ["kpi-ai-recs"], queryFn: kpiApi.aiRecommendations, staleTime: 60_000 });
}

// ── SLA ───────────────────────────────────────────────────────────────────────
export function useSlaRecords() {
  return useQuery({ queryKey: ["sla-records"], queryFn: slaApi.records, staleTime: 15_000 });
}
export function useSlaSummary() {
  return useQuery({ queryKey: ["sla-summary"], queryFn: slaApi.summary, staleTime: 15_000 });
}
export function useSlaBreaches() {
  return useQuery({ queryKey: ["sla-breaches"], queryFn: slaApi.breaches, staleTime: 15_000 });
}
export function useAcknowledgeSlaBreache() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => slaApi.acknowledgeBreache(id),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["sla-breaches"] }); qc.invalidateQueries({ queryKey: ["sla-summary"] }); },
  });
}
export function useResolveSlaBreache() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => slaApi.resolveBreache(id),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["sla-breaches"] }); qc.invalidateQueries({ queryKey: ["sla-summary"] }); },
  });
}

// ── Audit ─────────────────────────────────────────────────────────────────────
export function useAuditLogs(params?: Record<string, string>, enabled = true) {
  return useQuery({
    queryKey: ["audit-logs", params],
    queryFn: () => auditApi.logs(params),
    staleTime: 10_000,
    enabled,
  });
}
export function useAuditExportRequests(enabled = true) {
  return useQuery({ queryKey: ["audit-exports"], queryFn: auditApi.exportRequests, staleTime: 15_000, enabled });
}
export function useCreateAuditExport() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: Record<string, unknown>) => auditApi.createExport(body),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["audit-exports"] }),
  });
}
export function useAuditAiRecs(enabled = true) {
  return useQuery({ queryKey: ["audit-ai-recs"], queryFn: auditApi.aiRecommendations, staleTime: 60_000, enabled });
}

// ── Executive ─────────────────────────────────────────────────────────────────
export function useExecutiveSnapshots() {
  return useQuery({ queryKey: ["executive-snapshots"], queryFn: executiveApi.snapshots, staleTime: 60_000 });
}
export function useExecutiveSummary() {
  return useQuery({ queryKey: ["executive-summary"], queryFn: executiveApi.summary, staleTime: 30_000 });
}
export function useExecutiveAiRecs() {
  return useQuery({ queryKey: ["executive-ai-recs"], queryFn: executiveApi.aiRecommendations, staleTime: 60_000 });
}

// ── Trips ─────────────────────────────────────────────────────────────────────
export function useTrips(params?: { status?: string; vehicleId?: number; driverId?: number }) {
  return useQuery<AnyRecord[]>({
    queryKey: ["trips", params],
    queryFn: () => tripApi.list(params),
    staleTime: 15_000,
    refetchInterval: 30_000,
  });
}
export function useTripDetail(id?: number | string) {
  return useQuery<AnyRecord>({
    queryKey: ["trips", "detail", id],
    queryFn: () => tripApi.detail(id!),
    enabled: Boolean(id),
  });
}
export function useTripBreadcrumbs(id?: number | string) {
  return useQuery<AnyRecord[]>({
    queryKey: ["trips", "breadcrumbs", id],
    queryFn: () => tripApi.breadcrumbs(id!),
    enabled: Boolean(id),
    staleTime: 10_000,
  });
}
export function useTripCompliance(id?: number | string) {
  return useQuery<AnyRecord>({
    queryKey: ["trips", "compliance", id],
    queryFn: () => tripApi.compliance(id!),
    enabled: Boolean(id),
    staleTime: 30_000,
  });
}
export function useTripActions() {
  const qc = useQueryClient();
  const invalidate = () => qc.invalidateQueries({ queryKey: ["trips"] });
  const start     = useMutation({ mutationFn: (id: number) => tripApi.start(id),     onSuccess: invalidate });
  const complete  = useMutation({ mutationFn: (id: number) => tripApi.complete(id),  onSuccess: invalidate });
  const exception = useMutation({ mutationFn: ({ id, notes }: { id: number; notes?: string }) => tripApi.exception(id, notes), onSuccess: invalidate });
  return { start, complete, exception };
}
