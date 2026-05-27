import { useQuery } from "@tanstack/react-query";
import { documentsApi } from "@/services/documentsApi";
import { dvirApi } from "@/services/dvirApi";
import { maintenanceApi } from "@/services/maintenanceApi";
import { workOrdersApi } from "@/services/workOrdersApi";

export const useMaintenance = () => useQuery({ queryKey: ["maintenance"], queryFn: maintenanceApi.list });
export const useMaintenanceSummary = () => useQuery({ queryKey: ["maintenance", "summary"], queryFn: maintenanceApi.summary });
export const useMaintenanceDetail = (id?: string | number) => useQuery({ queryKey: ["maintenance", "detail", id], queryFn: () => maintenanceApi.detail(id!), enabled: Boolean(id) });

export const useWorkOrders = () => useQuery({ queryKey: ["workorders"], queryFn: workOrdersApi.list });
export const useWorkOrderSummary = () => useQuery({ queryKey: ["workorders", "summary"], queryFn: workOrdersApi.summary });
export const useWorkOrderDetail = (id?: string | number) => useQuery({ queryKey: ["workorders", "detail", id], queryFn: () => workOrdersApi.detail(id!), enabled: Boolean(id) });

export const useDvirReports = () => useQuery({ queryKey: ["dvir"], queryFn: dvirApi.list });
export const useDvirSummary = () => useQuery({ queryKey: ["dvir", "summary"], queryFn: dvirApi.summary });
export const useDvirDetail = (id?: string | number) => useQuery({ queryKey: ["dvir", "detail", id], queryFn: () => dvirApi.detail(id!), enabled: Boolean(id) });

export const useDocuments = () => useQuery({ queryKey: ["documents"], queryFn: documentsApi.list });
export const useDocumentSummary = () => useQuery({ queryKey: ["documents", "summary"], queryFn: documentsApi.summary });
export const useDocumentDetail = (id?: string | number) => useQuery({ queryKey: ["documents", "detail", id], queryFn: () => documentsApi.detail(id!), enabled: Boolean(id) });
