import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { carriersApi } from "@/services/carriersApi";
import { contractsApi } from "@/services/contractsApi";
import { costLeakageApi } from "@/services/costLeakageApi";
import { costMarginApi } from "@/services/costMarginApi";
import { expensesApi } from "@/services/expensesApi";
import { fuelApi } from "@/services/fuelApi";

// Fuel & Idling
export const useFuelSummary = () => useQuery({ queryKey: ["fuel", "summary"], queryFn: fuelApi.summary });
export const useFuelTransactions = () => useQuery({ queryKey: ["fuel", "transactions"], queryFn: fuelApi.transactions });
export const useFuelTransaction = (id?: string | number) => useQuery({ queryKey: ["fuel", "transaction", id], queryFn: () => fuelApi.transaction(id!), enabled: Boolean(id) });
export const useFuelIdlingEvents = () => useQuery({ queryKey: ["fuel", "idling"], queryFn: fuelApi.idlingEvents });
export const useFuelVehicleSummary = () => useQuery({ queryKey: ["fuel", "vehicle-summary"], queryFn: fuelApi.vehicleSummary });
export const useFuelDriverSummary = () => useQuery({ queryKey: ["fuel", "driver-summary"], queryFn: fuelApi.driverSummary });
export const useFuelAnomalies = () => useQuery({ queryKey: ["fuel", "anomalies"], queryFn: fuelApi.anomalies });
export const useFuelRecommendations = () => useQuery({ queryKey: ["fuel", "recommendations"], queryFn: fuelApi.recommendations });

export const useCreateFuelTransaction = () => {
  const qc = useQueryClient();
  return useMutation({ mutationFn: fuelApi.createTransaction, onSuccess: () => qc.invalidateQueries({ queryKey: ["fuel"] }) });
};
export const useUpdateFuelTransaction = () => {
  const qc = useQueryClient();
  return useMutation({ mutationFn: ({ id, payload }: { id: string | number; payload: Record<string, unknown> }) => fuelApi.updateTransaction(id, payload), onSuccess: () => qc.invalidateQueries({ queryKey: ["fuel"] }) });
};
export const useDeleteFuelTransaction = () => {
  const qc = useQueryClient();
  return useMutation({ mutationFn: fuelApi.deleteTransaction, onSuccess: () => qc.invalidateQueries({ queryKey: ["fuel"] }) });
};

// Expenses
export const useExpensesSummary = () => useQuery({ queryKey: ["expenses", "summary"], queryFn: expensesApi.summary });
export const useExpenses = () => useQuery({ queryKey: ["expenses"], queryFn: expensesApi.list });
export const useExpenseDetail = (id?: string | number) => useQuery({ queryKey: ["expenses", "detail", id], queryFn: () => expensesApi.detail(id!), enabled: Boolean(id) });
export const useExpenseCategories = () => useQuery({ queryKey: ["expenses", "categories"], queryFn: expensesApi.categories });
export const useExpensesRecommendations = () => useQuery({ queryKey: ["expenses", "recommendations"], queryFn: expensesApi.recommendations });

export const useCreateExpense = () => {
  const qc = useQueryClient();
  return useMutation({ mutationFn: expensesApi.create, onSuccess: () => qc.invalidateQueries({ queryKey: ["expenses"] }) });
};
export const useUpdateExpense = () => {
  const qc = useQueryClient();
  return useMutation({ mutationFn: ({ id, payload }: { id: string | number; payload: Record<string, unknown> }) => expensesApi.update(id, payload), onSuccess: () => qc.invalidateQueries({ queryKey: ["expenses"] }) });
};
export const useDeleteExpense = () => {
  const qc = useQueryClient();
  return useMutation({ mutationFn: expensesApi.remove, onSuccess: () => qc.invalidateQueries({ queryKey: ["expenses"] }) });
};
export const useApproveExpense = () => {
  const qc = useQueryClient();
  return useMutation({ mutationFn: expensesApi.approve, onSuccess: () => qc.invalidateQueries({ queryKey: ["expenses"] }) });
};
export const useRejectExpense = () => {
  const qc = useQueryClient();
  return useMutation({ mutationFn: expensesApi.reject, onSuccess: () => qc.invalidateQueries({ queryKey: ["expenses"] }) });
};

// Contracts / Rates
export const useContractsSummary = () => useQuery({ queryKey: ["contracts", "summary"], queryFn: contractsApi.summary });
export const useContracts = () => useQuery({ queryKey: ["contracts"], queryFn: contractsApi.list });
export const useContractDetail = (id?: string | number) => useQuery({ queryKey: ["contracts", "detail", id], queryFn: () => contractsApi.detail(id!), enabled: Boolean(id) });
export const useContractsRecommendations = () => useQuery({ queryKey: ["contracts", "recommendations"], queryFn: contractsApi.recommendations });

export const useCreateContract = () => {
  const qc = useQueryClient();
  return useMutation({ mutationFn: contractsApi.create, onSuccess: () => qc.invalidateQueries({ queryKey: ["contracts"] }) });
};
export const useUpdateContract = () => {
  const qc = useQueryClient();
  return useMutation({ mutationFn: ({ id, payload }: { id: string | number; payload: Record<string, unknown> }) => contractsApi.update(id, payload), onSuccess: () => qc.invalidateQueries({ queryKey: ["contracts"] }) });
};
export const useDeleteContract = () => {
  const qc = useQueryClient();
  return useMutation({ mutationFn: contractsApi.remove, onSuccess: () => qc.invalidateQueries({ queryKey: ["contracts"] }) });
};
export const useActivateContract = () => {
  const qc = useQueryClient();
  return useMutation({ mutationFn: contractsApi.activate, onSuccess: () => qc.invalidateQueries({ queryKey: ["contracts"] }) });
};
export const useExpireContract = () => {
  const qc = useQueryClient();
  return useMutation({ mutationFn: contractsApi.expire, onSuccess: () => qc.invalidateQueries({ queryKey: ["contracts"] }) });
};

// Carrier Management
export const useCarriersSummary = () => useQuery({ queryKey: ["carriers", "summary"], queryFn: carriersApi.summary });
export const useCarriers = () => useQuery({ queryKey: ["carriers"], queryFn: carriersApi.list });
export const useCarrierDetail = (id?: string | number) => useQuery({ queryKey: ["carriers", "detail", id], queryFn: () => carriersApi.detail(id!), enabled: Boolean(id) });
export const useCarriersRecommendations = () => useQuery({ queryKey: ["carriers", "recommendations"], queryFn: carriersApi.recommendations });

export const useCreateCarrier = () => {
  const qc = useQueryClient();
  return useMutation({ mutationFn: carriersApi.create, onSuccess: () => qc.invalidateQueries({ queryKey: ["carriers"] }) });
};
export const useUpdateCarrier = () => {
  const qc = useQueryClient();
  return useMutation({ mutationFn: ({ id, payload }: { id: string | number; payload: Record<string, unknown> }) => carriersApi.update(id, payload), onSuccess: () => qc.invalidateQueries({ queryKey: ["carriers"] }) });
};
export const useDeleteCarrier = () => {
  const qc = useQueryClient();
  return useMutation({ mutationFn: carriersApi.remove, onSuccess: () => qc.invalidateQueries({ queryKey: ["carriers"] }) });
};
export const useSetCarrierStatus = () => {
  const qc = useQueryClient();
  return useMutation({ mutationFn: ({ id, payload }: { id: string | number; payload: Record<string, unknown> }) => carriersApi.setStatus(id, payload), onSuccess: () => qc.invalidateQueries({ queryKey: ["carriers"] }) });
};

// Cost Margin / Predictive
export const useCostMarginSummary = () => useQuery({ queryKey: ["cost-margin", "summary"], queryFn: costMarginApi.summary });
export const useCostMarginJobs = () => useQuery({ queryKey: ["cost-margin", "jobs"], queryFn: costMarginApi.jobs });
export const useCostMarginRoutes = () => useQuery({ queryKey: ["cost-margin", "routes"], queryFn: costMarginApi.routes });
export const useCostMarginVehicles = () => useQuery({ queryKey: ["cost-margin", "vehicles"], queryFn: costMarginApi.vehicles });
export const useCostMarginCustomers = () => useQuery({ queryKey: ["cost-margin", "customers"], queryFn: costMarginApi.customers });
export const useCostMarginPredictions = () => useQuery({ queryKey: ["cost-margin", "predictions"], queryFn: costMarginApi.predictions });
export const useCostMarginRecommendations = () => useQuery({ queryKey: ["cost-margin", "recommendations"], queryFn: costMarginApi.recommendations });

export const useRecalculateMargins = () => {
  const qc = useQueryClient();
  return useMutation({ mutationFn: costMarginApi.recalculate, onSuccess: () => qc.invalidateQueries({ queryKey: ["cost-margin"] }) });
};

// Cost Leakage
export const useCostLeakageSummary = () => useQuery({ queryKey: ["cost-leakage", "summary"], queryFn: costLeakageApi.summary });
export const useCostLeakageItems = () => useQuery({ queryKey: ["cost-leakage", "items"], queryFn: costLeakageApi.items });
export const useCostLeakageRecommendations = () => useQuery({ queryKey: ["cost-leakage", "recommendations"], queryFn: costLeakageApi.recommendations });

export const useAcknowledgeLeakage = () => {
  const qc = useQueryClient();
  return useMutation({ mutationFn: costLeakageApi.acknowledge, onSuccess: () => qc.invalidateQueries({ queryKey: ["cost-leakage"] }) });
};
export const useCreateLeakageAction = () => {
  const qc = useQueryClient();
  return useMutation({ mutationFn: ({ id, payload }: { id: string | number; payload: Record<string, unknown> }) => costLeakageApi.createAction(id, payload), onSuccess: () => qc.invalidateQueries({ queryKey: ["cost-leakage"] }) });
};

// Local-data detail hooks for analytics-only modules (no per-ID API endpoint needed)
export const useCostMarginJobDetail = (id?: string | number) => {
  const q = useCostMarginJobs();
  const row = q.data?.find((r) => String(r.id) === String(id));
  return { data: row ? { record: row, recommendations: [], auditTrail: [] } : undefined, isLoading: q.isLoading };
};

export const useCostLeakageItemDetail = (id?: string | number) => {
  const q = useCostLeakageItems();
  const row = q.data?.find((r) => String(r.id) === String(id));
  return { data: row ? { record: row, recommendations: [], auditTrail: [] } : undefined, isLoading: q.isLoading };
};
