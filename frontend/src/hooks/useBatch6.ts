import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { complianceApi, eldApi, hosApi, localizationApi } from "@/services/complianceApi";

export function useComplianceSummary() {
  return useQuery({ queryKey: ["compliance-summary"], queryFn: complianceApi.summary, staleTime: 30_000 });
}

export function useComplianceProfiles() {
  return useQuery({ queryKey: ["compliance-profiles"], queryFn: complianceApi.profiles });
}

export function useComplianceViolations() {
  return useQuery({ queryKey: ["compliance-violations"], queryFn: complianceApi.violations, staleTime: 15_000 });
}

export function useComplianceDocuments() {
  return useQuery({ queryKey: ["compliance-docs"], queryFn: complianceApi.documents });
}

export function useAuditPackages() {
  return useQuery({ queryKey: ["audit-packages"], queryFn: complianceApi.auditPackages });
}

export function useCrossBorderWatch() {
  return useQuery({ queryKey: ["cross-border-watch"], queryFn: complianceApi.crossBorderWatch });
}

export function useDriverComplianceStatus() {
  return useQuery({ queryKey: ["driver-compliance-status"], queryFn: complianceApi.driverStatus });
}

export function useVehicleComplianceStatus() {
  return useQuery({ queryKey: ["vehicle-compliance-status"], queryFn: complianceApi.vehicleStatus });
}

export function useComplianceAiRecs() {
  return useQuery({ queryKey: ["compliance-ai-recs"], queryFn: complianceApi.aiRecommendations });
}

export function useAcknowledgeViolation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => complianceApi.acknowledgeViolation(id),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ["compliance-violations"] }); },
  });
}

export function useResolveViolation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => complianceApi.resolveViolation(id),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ["compliance-violations"] }); },
  });
}

export function useCreateAuditPackage() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: Record<string, unknown>) => complianceApi.createAuditPackage(body),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ["audit-packages"] }); },
  });
}

export function useFinalizeAuditPackage() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => complianceApi.finalizeAuditPackage(id),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ["audit-packages"] }); },
  });
}

// HOS
export function useHosSummary() {
  return useQuery({ queryKey: ["hos-summary"], queryFn: hosApi.summary, staleTime: 20_000 });
}

export function useHosDrivers() {
  return useQuery({ queryKey: ["hos-drivers"], queryFn: hosApi.drivers, staleTime: 20_000 });
}

export function useHosClocks() {
  return useQuery({ queryKey: ["hos-clocks"], queryFn: hosApi.clocks, staleTime: 20_000 });
}

export function useHosLogs() {
  return useQuery({ queryKey: ["hos-logs"], queryFn: hosApi.logs });
}

export function useHosAiRecs() {
  return useQuery({ queryKey: ["hos-ai-recs"], queryFn: hosApi.aiRecommendations });
}

export function useCertifyHosLog() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => hosApi.certifyLog(id),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ["hos-logs"] }); },
  });
}

// ELD
export function useEldDevices() {
  return useQuery({ queryKey: ["eld-devices"], queryFn: eldApi.devices, staleTime: 30_000 });
}

export function useMarkEldMalfunction() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: number; body: Record<string, unknown> }) => eldApi.markMalfunction(id, body),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ["eld-devices"] }); },
  });
}

export function useResolveEldMalfunction() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => eldApi.resolveMalfunction(id),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ["eld-devices"] }); },
  });
}

// Localization
export function useLocalizationSettings() {
  return useQuery({ queryKey: ["locale-settings"], queryFn: localizationApi.settings });
}

export function useCountries() {
  return useQuery({ queryKey: ["countries"], queryFn: localizationApi.countries });
}

export function useLanguages() {
  return useQuery({ queryKey: ["languages"], queryFn: localizationApi.languages });
}

export function useUpdateLocaleSettings() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: Record<string, unknown>) => localizationApi.updateSettings(body),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ["locale-settings"] }); },
  });
}

export function useUpdateUserPreferences() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: Record<string, unknown>) => localizationApi.updateUserPreferences(body),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ["user-locale-prefs"] }); },
  });
}