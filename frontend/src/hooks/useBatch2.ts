import { useQuery } from "@tanstack/react-query";
import { customerEtaApi } from "@/services/customerEtaApi";
import { dispatchApi } from "@/services/dispatchApi";
import { jobsApi } from "@/services/jobsApi";
import { routesApi } from "@/services/routesApi";

export const useJobs = () => useQuery({ queryKey: ["jobs"], queryFn: jobsApi.list });
export const useJobDetail = (id?: string | number) => useQuery({ queryKey: ["jobs", "detail", id], queryFn: () => jobsApi.detail(id!), enabled: Boolean(id) });
export const useJobSummary = () => useQuery({ queryKey: ["jobs", "summary"], queryFn: jobsApi.summary });
export const useDispatchBoard = () => useQuery({ queryKey: ["dispatch", "board"], queryFn: dispatchApi.board, refetchInterval: 15000 });
export const useDispatchSummary = () => useQuery({ queryKey: ["dispatch", "summary"], queryFn: dispatchApi.summary });
export const useDispatchRecommendations = () => useQuery({ queryKey: ["dispatch", "recommendations"], queryFn: dispatchApi.recommendations });
export const useAvailableDrivers = () => useQuery({ queryKey: ["dispatch", "drivers"], queryFn: dispatchApi.availableDrivers });
export const useAvailableVehicles = () => useQuery({ queryKey: ["dispatch", "vehicles"], queryFn: dispatchApi.availableVehicles });
export const useRoutes = (opts?: { limit?: number; offset?: number; search?: string; status?: string }) => useQuery({
  queryKey: ["routes", "paged", opts?.search ?? "", opts?.status ?? "All", opts?.offset ?? 0, opts?.limit ?? 50],
  queryFn: () => routesApi.listPaged(opts),
});
export const useRouteDetail = (id?: string | number) => useQuery({ queryKey: ["routes", "detail", id], queryFn: () => routesApi.detail(id!), enabled: Boolean(id) });
export const useRouteSummary = () => useQuery({ queryKey: ["routes", "summary"], queryFn: routesApi.summary });
export const useCustomerEtaSummary = () => useQuery({ queryKey: ["customer-eta", "summary"], queryFn: customerEtaApi.summary });
export const useCustomerTracking = (trackingCode?: string) => useQuery({ queryKey: ["customer-eta", "track", trackingCode], queryFn: () => customerEtaApi.track(trackingCode!), enabled: Boolean(trackingCode) });
export const useCustomerEtaRecommendations = () => useQuery({ queryKey: ["customer-eta", "recommendations"], queryFn: customerEtaApi.recommendations });
