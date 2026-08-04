import { apiClient, unwrap } from '@/services/apiClient';
import { downloadServerExport } from '@/services/fleetDomainApi';

export const ACTIVE_LIFECYCLES = ['Unassigned', 'Scheduled', 'Assigned', 'En Route', 'In Progress', 'At Stop', 'Completed', 'Delayed', 'At Risk', 'Exception'] as const;

export interface ActiveShipment {
  id: string;
  branchId?: string | null;
  shipmentNumber: string;
  customerName: string;
  origin: string;
  destination: string;
  city: string;
  status: string;
  priority: string;
  mode: string;
  pieceCount?: number | null;
  weightKg?: number | null;
  volumeCbm?: number | null;
  carrierName: string;
  driverName: string;
  vehicleNumber: string;
  routeCode: string;
  podStatus: string;
  podReady: boolean;
  isInvoiceReady: boolean;
  pickupScheduledAtUtc?: string | null;
  pickedUpAtUtc?: string | null;
  etaUtc?: string | null;
  etaSource: string;
  riskStatus: 'OnTrack' | 'AtRisk' | 'Breached' | 'NoEta';
  minutesToEta?: number | null;
  assignmentStatus: 'FullyAssigned' | 'Partial' | 'Unassigned';
  latestLocation: string;
  lastTrackingAtUtc?: string | null;
  trackingFreshness: 'Live' | 'Delayed' | 'Stale' | 'NoTracking';
  deliveryStops: number;
  verifiedPods: number;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
}

export interface ActiveShipmentSummary {
  activeTotal: number;
  breached: number;
  atRisk: number;
  noEta: number;
  needsResources: number;
  podPending: number;
  invoiceBlocked: number;
}

export interface ActiveShipmentFilters {
  lifecycle?: string;
  risk?: string;
  assignment?: string;
  readiness?: string;
  search?: string;
  sort?: 'risk' | 'eta' | 'created' | 'priority';
  direction?: 'asc' | 'desc';
}

export interface ActiveShipmentPage extends ActiveShipmentFilters {
  page?: number;
  pageSize?: number;
  signal?: AbortSignal;
}

export interface ActiveShipmentResponse {
  total: number;
  page: number;
  pageSize: number;
  generatedAtUtc: string;
  summary: ActiveShipmentSummary;
  items: ActiveShipment[];
}

function params(input: ActiveShipmentFilters & { page?: number; pageSize?: number }) {
  return Object.fromEntries(Object.entries(input).filter(([, value]) => value !== undefined && value !== '' && value !== 'All'));
}

export const activeShipmentsApi = {
  list: ({ signal, ...input }: ActiveShipmentPage = {}) =>
    unwrap<ActiveShipmentResponse>(apiClient.get('/api/fleet-tms/active-shipments', { params: params(input), signal })),

  export: (filters: ActiveShipmentFilters = {}) => {
    const query = new URLSearchParams(params(filters) as Record<string, string>).toString();
    return downloadServerExport(
      `/api/fleet-tms/active-shipments/export${query ? `?${query}` : ''}`,
      `active-shipments-${new Date().toISOString().slice(0, 10)}.csv`,
    );
  },
};
