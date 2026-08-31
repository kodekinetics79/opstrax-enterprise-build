import { useQuery } from "@tanstack/react-query";
import { documentsApi } from "@/services/documentsApi";
import { dvirApi } from "@/services/dvirApi";
import { maintenanceApi } from "@/services/maintenanceApi";
import { workOrdersApi } from "@/services/workOrdersApi";
import { useAuth } from "@/hooks/useAuth";

export const useMaintenance = () => useQuery({ queryKey: ["maintenance"], queryFn: maintenanceApi.list });
export const useMaintenanceSummary = () => useQuery({ queryKey: ["maintenance", "summary"], queryFn: maintenanceApi.summary });
export const useMaintenanceDetail = (id?: string | number) => useQuery({ queryKey: ["maintenance", "detail", id], queryFn: () => maintenanceApi.detail(id!), enabled: Boolean(id) });

export const useWorkOrders = () => useQuery({ queryKey: ["workorders"], queryFn: workOrdersApi.list });
export const useWorkOrderSummary = () => useQuery({ queryKey: ["workorders", "summary"], queryFn: workOrdersApi.summary });
export const useWorkOrderDetail = (id?: string | number) => useQuery({ queryKey: ["workorders", "detail", id], queryFn: () => workOrdersApi.detail(id!), enabled: Boolean(id) });

export const useDvirReports = () => useQuery({ queryKey: ["dvir"], queryFn: dvirApi.list });
export const useDvirSummary = () => useQuery({ queryKey: ["dvir", "summary"], queryFn: dvirApi.summary });
export const useDvirDetail = (id?: string | number) => useQuery({ queryKey: ["dvir", "detail", id], queryFn: () => dvirApi.detail(id!), enabled: Boolean(id) });

function useDocumentScope() {
  const { session } = useAuth();
  const companyId = String(session?.company.id ?? "").trim();
  const userId = String(session?.user.id ?? "").trim();
  const branch = session?.user.branchId ?? null;
  const branchId = branch === null ? null : String(branch).trim();
  const role = String(session?.role ?? "").trim();
  const supportGrant = session?.supportAccess?.grantRef ?? null;
  const validSupportGrant = !session?.supportAccess || Boolean(String(supportGrant ?? "").trim());
  const enabled = Boolean(session?.token && companyId && userId && role && (branchId === null || branchId) && validSupportGrant);
  return { enabled, session, key: [companyId || "invalid-company", userId || "invalid-user", branchId, role || "invalid-role", supportGrant] };
}
export const useDocuments = () => {
  const scope = useDocumentScope();
  return useQuery({ queryKey: ["documents", ...scope.key], queryFn: () => documentsApi.list(scope.session!), enabled: scope.enabled });
};
export const useDocumentSummary = () => {
  const scope = useDocumentScope();
  return useQuery({ queryKey: ["documents", "summary", ...scope.key], queryFn: () => documentsApi.summary(scope.session!), enabled: scope.enabled });
};
export const useDocumentDetail = (id?: string | number) => {
  const scope = useDocumentScope();
  return useQuery({ queryKey: ["documents", "detail", id, ...scope.key], queryFn: () => documentsApi.detail(id!, scope.session!), enabled: scope.enabled && Boolean(id), staleTime: 0, refetchOnMount: "always" });
};
