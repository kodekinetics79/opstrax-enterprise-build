import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  fetchOpsMetrics,
  fetchServiceRuns,
  fetchServiceRunHistory,
  fetchIncidents,
  updateIncidentStatus,
  fetchConfigCheck,
  fetchDeepHealth,
} from "@/services/opsApi";

// ── Health ────────────────────────────────────────────────────────────────────

export function useDeepHealth() {
  return useQuery({
    queryKey: ["ops", "health", "deep"],
    queryFn:  fetchDeepHealth,
    refetchInterval: 30_000,
    retry: false,
  });
}

// ── Ops Metrics ───────────────────────────────────────────────────────────────

export function useOpsMetrics() {
  return useQuery({
    queryKey: ["ops", "metrics"],
    queryFn:  fetchOpsMetrics,
    refetchInterval: 30_000,
  });
}

// ── Service Runs ──────────────────────────────────────────────────────────────

export function useServiceRuns() {
  return useQuery({
    queryKey: ["ops", "services"],
    queryFn:  fetchServiceRuns,
    refetchInterval: 30_000,
  });
}

export function useServiceRunHistory(serviceName: string | null) {
  return useQuery({
    queryKey: ["ops", "services", serviceName],
    queryFn:  () => fetchServiceRunHistory(serviceName!),
    enabled:  !!serviceName,
  });
}

// ── Incidents ─────────────────────────────────────────────────────────────────

export function useIncidents() {
  return useQuery({
    queryKey: ["ops", "incidents"],
    queryFn:  fetchIncidents,
    refetchInterval: 60_000,
  });
}

export function useUpdateIncidentStatus() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      id,
      status,
      assignedTo,
    }: {
      id: number;
      status: string;
      assignedTo?: string;
    }) => updateIncidentStatus(id, status, assignedTo),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["ops", "incidents"] }),
  });
}

// ── Config Validation ─────────────────────────────────────────────────────────

export function useConfigCheck() {
  return useQuery({
    queryKey: ["ops", "config"],
    queryFn:  fetchConfigCheck,
    staleTime: 5 * 60 * 1000,
  });
}
