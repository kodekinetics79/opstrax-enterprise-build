// Fleet TMS (PR3) logistics service — ported from Zayra src/api/logistics.ts onto
// the OpsTrax apiClient/unwrap pattern. Endpoints re-namespaced to /api/fleet-tms/logistics/*.
import { apiClient, unwrap } from "@/services/apiClient";

export interface LogisticsSummary {
  activeOrders: number;
  inTransit: number;
  deliveredToday: number;
  exceptionOrders: number;
  activeRoutes: number;
  onTimeRate: number;
}

export interface LogisticsOverview {
  generatedAtUtc: string;
  summary: LogisticsSummary;
  alerts: Array<{
    orderNumber: string;
    customerName: string;
    routeCode: string;
    status: string;
    exceptionReason: string;
    attemptCount: number;
    riderName: string;
    etaUtc: string;
  }>;
  routeCards: LogisticsRoute[];
  orderCards: LogisticsOrder[];
  liveStops: LogisticsStop[];
}

export interface LogisticsOrder {
  id: string;
  orderNumber: string;
  customerName: string;
  customerSegment: string;
  salesChannel: string;
  city: string;
  area: string;
  status: string;
  priority: string;
  itemCount: number;
  orderValue: number;
  routeCode: string;
  driverName: string;
  vehicleNumber: string;
  dispatchNotes: string;
  createdAtUtc: string;
  promisedAtUtc?: string;
  dispatchedAtUtc?: string;
  deliveredAtUtc?: string;
}

export interface LogisticsRoute {
  id: string;
  routeCode: string;
  hub: string;
  territory: string;
  driverName: string;
  vehicleNumber: string;
  status: string;
  plannedStops: number;
  completedStops: number;
  distanceKm: number;
  completionPercent: number;
  currentStop: string;
  nextStop: string;
  plannedForDate: string;
  departureTimeUtc: string;
  etaCompleteUtc?: string;
  notes: string;
}

export interface LogisticsStop {
  id: string;
  orderNumber: string;
  routeCode: string;
  customerName: string;
  addressLine: string;
  city: string;
  status: string;
  proofStatus: string;
  recipientName: string;
  attemptCount: number;
  riderName: string;
  timeWindow: string;
  etaUtc: string;
  deliveredAtUtc?: string;
  exceptionReason: string;
}

export const logisticsApi = {
  overview: () =>
    unwrap<LogisticsOverview>(apiClient.get('/api/fleet-tms/logistics/overview')),

  orders: (params: { status?: string; page?: number; pageSize?: number } = {}) =>
    unwrap<{ total: number; page: number; pageSize: number; items: LogisticsOrder[] }>(apiClient.get('/api/fleet-tms/logistics/orders', { params })),

  order: (id: string) =>
    unwrap<LogisticsOrder>(apiClient.get(`/api/fleet-tms/logistics/orders/${id}`)),

  createOrder: (body: Partial<LogisticsOrder> & { orderNumber: string; customerName: string }) =>
    unwrap<LogisticsOrder>(apiClient.post('/api/fleet-tms/logistics/orders', body)),

  updateOrder: (id: string, body: Partial<LogisticsOrder>) =>
    unwrap<LogisticsOrder>(apiClient.put(`/api/fleet-tms/logistics/orders/${id}`, body)),

  routes: (params: { status?: string } = {}) =>
    unwrap<{ items: LogisticsRoute[] }>(apiClient.get('/api/fleet-tms/logistics/routes', { params })),

  createRoute: (body: Partial<LogisticsRoute> & { routeCode: string }) =>
    unwrap<LogisticsRoute>(apiClient.post('/api/fleet-tms/logistics/routes', body)),

  updateRoute: (id: string, body: Partial<LogisticsRoute>) =>
    unwrap<LogisticsRoute>(apiClient.put(`/api/fleet-tms/logistics/routes/${id}`, body)),

  routeStops: (id: string) =>
    unwrap<{ items: LogisticsStop[] }>(apiClient.get(`/api/fleet-tms/logistics/routes/${id}/stops`)),

  lastMile: (params: { status?: string; page?: number; pageSize?: number } = {}) =>
    unwrap<{ total: number; page: number; pageSize: number; items: LogisticsStop[] }>(apiClient.get('/api/fleet-tms/logistics/last-mile', { params })),

  dispatchOrder: (id: string, body: { routeCode?: string; driverName?: string; vehicleNumber?: string; notes?: string }) =>
    unwrap<LogisticsOrder>(apiClient.post(`/api/fleet-tms/logistics/orders/${id}/dispatch`, body)),

  progressRoute: (id: string, body: { completedStopsDelta?: number; currentStop?: string; nextStop?: string; etaCompleteUtc?: string; notes?: string }) =>
    unwrap<LogisticsRoute>(apiClient.post(`/api/fleet-tms/logistics/routes/${id}/progress`, body)),

  confirmDelivery: (id: string, body: { recipientName?: string; proofStatus?: string; exceptionReason?: string }) =>
    unwrap<LogisticsStop>(apiClient.post(`/api/fleet-tms/logistics/stops/${id}/deliver`, body)),

  recordAttempt: (id: string, body: { status?: string; proofStatus?: string; exceptionReason?: string; nextEtaUtc?: string; nextStop?: string }) =>
    unwrap<LogisticsStop>(apiClient.post(`/api/fleet-tms/logistics/stops/${id}/attempt`, body)),

  rescheduleStop: (id: string, body: { nextEtaUtc?: string; timeWindow?: string; reason?: string }) =>
    unwrap<LogisticsStop>(apiClient.post(`/api/fleet-tms/logistics/stops/${id}/reschedule`, body)),
};
