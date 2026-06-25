// Fleet TMS (PR1) service layer — ported from the Zayra `src/api/fleet.ts` onto the
// OpsTrax service pattern. All access routes through the shared `apiClient` + `unwrap`
// (envelope-aware) instead of a bespoke axios client. Endpoints are additive and live
// under /api/fleet-tms/* (authenticated) and /api/public/shipments/* (anonymous).
import { apiClient, unwrap } from "@/services/apiClient";

// Lightweight error reporter (replaces Zayra's notifyApiError from api/client).
export function notifyApiError(error: unknown, fallback = "Request failed"): string {
  const anyErr = error as { response?: { data?: { message?: string } }; message?: string };
  const message = anyErr?.response?.data?.message || anyErr?.message || fallback;
  // eslint-disable-next-line no-console
  console.error("[fleet-tms]", message, error);
  return message;
}

export interface FleetSummary {
  activeShipments: number;
  enRoute: number;
  deliveredToday: number;
  activeVehicles: number;
  onTripVehicles: number;
  openMaintenance: number;
  fuelAlerts: number;
  trackingEvents: number;
  avgFuelLevel: number;
  deliveredRate: number;
}

export interface FleetOverview {
  generatedAtUtc: string;
  summary: FleetSummary;
  shipmentCards: FleetShipment[];
  vehicleCards: FleetVehicle[];
  trackingCards: FleetTrackingPoint[];
  maintenanceCards: FleetMaintenanceTicket[];
  fuelCards: FleetFuelEvent[];
  loadPlanCards: Array<{
    routeCode: string;
    shipmentCount: number;
    totalWeightKg: number;
    totalVolumeCbm: number;
    highPriority: number;
    delivered: number;
  }>;
}

export interface FleetShipment {
  id: string;
  shipmentNumber: string;
  customerName: string;
  customerSegment: string;
  origin: string;
  destination: string;
  city: string;
  status: string;
  priority: string;
  mode: string;
  pieceCount: number;
  weightKg: number;
  volumeCbm: number;
  declaredValue: number;
  carrierName: string;
  customerVATNumber: string;
  customerCommercialRegistrationNo: string;
  customerNationalAddressBuildingNo: string;
  customerNationalAddressAdditionalNo: string;
  customerNationalAddressDistrict: string;
  customerNationalAddressCity: string;
  customerNationalAddressRegion: string;
  customerNationalAddressPostalCode: string;
  customerNationalAddressCountry: string;
  driverName: string;
  vehicleNumber: string;
  routeCode: string;
  podStatus: string;
  temperatureRange: string;
  notes: string;
  isInvoiceReady?: boolean;
  invoiceReadyAtUtc?: string | null;
  invoiceReadinessNotes?: string;
  createdAtUtc: string;
  pickupScheduledAtUtc?: string;
  pickedUpAtUtc?: string;
  deliveredAtUtc?: string;
}

export interface FleetVehicle {
  id: string;
  vehicleNumber: string;
  plateNumber: string;
  type: string;
  status: string;
  driverName: string;
  capacityKg: number;
  capacityCbm: number;
  currentLoadKg: number;
  fuelLevelPercent: number;
  odometerKm: number;
  healthStatus: string;
  isRefrigerated: boolean;
  temperatureCelsius?: number;
  lastKnownLocation: string;
  lastPingAtUtc?: string;
  lastServiceAtUtc?: string;
  nextServiceAtUtc?: string;
  notes: string;
}

export interface FleetTrackingPoint {
  id: string;
  shipmentNumber: string;
  vehicleNumber: string;
  locationLabel: string;
  status: string;
  geofenceName: string;
  alertType: string;
  latitude: number;
  longitude: number;
  speedKph: number;
  recordedAtUtc: string;
  estimatedArrivalUtc?: string;
  notes: string;
}

export interface FleetMaintenanceTicket {
  id: string;
  workOrderNumber: string;
  vehicleNumber: string;
  type: string;
  status: string;
  priority: string;
  vendorName: string;
  description: string;
  estimatedCost: number;
  actualCost: number;
  downtimeHours: number;
  openedAtUtc: string;
  dueAtUtc?: string;
  closedAtUtc?: string;
  notes: string;
}

export interface FleetFuelEvent {
  id: string;
  vehicleNumber: string;
  fuelCardNumber: string;
  stationName: string;
  city: string;
  eventType: string;
  anomalyFlag: boolean;
  liters: number;
  cost: number;
  odometerKm: number;
  notes: string;
  recordedAtUtc: string;
}

export interface ShipmentStop {
  id: string;
  shipmentId: string;
  stopType: string;
  sequenceNo: number;
  locationName: string;
  contactName: string;
  contactPhone: string;
  addressLine1: string;
  addressLine2: string;
  city: string;
  region: string;
  postalCode: string;
  country: string;
  saudiNationalAddressBuildingNo: string;
  saudiNationalAddressAdditionalNo: string;
  saudiNationalAddressDistrict: string;
  latitude?: number | null;
  longitude?: number | null;
  plannedArrivalAt: string;
  actualArrivalAt?: string | null;
  completedAt?: string | null;
  status: string;
  notes: string;
  createdAt: string;
  updatedAt: string;
}

export interface ProofOfDelivery {
  id: string;
  shipmentId: string;
  stopId: string;
  capturedByUserId?: string | null;
  driverId?: string | null;
  vehicleId?: string | null;
  recipientName: string;
  recipientPhone: string;
  signatureUrl: string;
  photoUrl: string;
  documentUrl: string;
  notes: string;
  deliveryCondition: string;
  capturedLatitude?: number | null;
  capturedLongitude?: number | null;
  capturedAt: string;
  verifiedAt?: string | null;
  verifiedByUserId?: string | null;
  status: string;
  createdAt: string;
  updatedAt: string;
}

export interface ShipmentEvent {
  id: string;
  shipmentId: string;
  eventType: string;
  message: string;
  actorName: string;
  visibility: string;
  occurredAtUtc: string;
}

export interface CustomerTrackingLink {
  id: string;
  shipmentId: string;
  token: string;
  expiresAtUtc: string;
  isRevoked: boolean;
  sharedBy: string;
  createdAtUtc: string;
  revokedAtUtc?: string | null;
  updatedAtUtc?: string | null;
}

export interface DriverTask {
  id: string;
  shipmentId: string;
  stopId?: string | null;
  taskType: string;
  title: string;
  description: string;
  status: string;
  driverName: string;
  vehicleNumber: string;
  dueAtUtc: string;
  completedAtUtc?: string | null;
  notes: string;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
}

export interface PublicTrackingSummary {
  shipmentNumber: string;
  status: string;
  origin: string;
  destination: string;
  pickupScheduledAtUtc?: string | null;
  deliveredAtUtc?: string | null;
  stops: Array<{
    sequenceNo: number;
    stopType: string;
    locationName: string;
    city: string;
    status: string;
    plannedArrivalAt: string;
    actualArrivalAt?: string | null;
    completedAt?: string | null;
  }>;
  publicEvents: Array<{
    eventType: string;
    message: string;
    occurredAtUtc: string;
  }>;
  pod: Array<{
    recipientName: string;
    deliveryCondition: string;
    status: string;
    capturedAt: string;
    verifiedAt?: string | null;
    signatureUrl: string;
    photoUrl: string;
    documentUrl: string;
  }>;
}

export const fleetApi = {
  overview: () => unwrap<FleetOverview>(apiClient.get("/api/fleet-tms/overview")),
  shipments: (params: { status?: string; page?: number; pageSize?: number } = {}) =>
    unwrap<{ total: number; page: number; pageSize: number; items: FleetShipment[] }>(apiClient.get("/api/fleet-tms/shipments", { params })),
  vehicles: (params: { status?: string } = {}) =>
    unwrap<{ items: FleetVehicle[] }>(apiClient.get("/api/fleet-tms/vehicles", { params })),
  tracking: (params: { shipmentNumber?: string; page?: number; pageSize?: number } = {}) =>
    unwrap<{ total: number; page: number; pageSize: number; items: FleetTrackingPoint[] }>(apiClient.get("/api/fleet-tms/tracking", { params })),
  maintenance: (params: { status?: string; page?: number; pageSize?: number } = {}) =>
    unwrap<{ total: number; page: number; pageSize: number; items: FleetMaintenanceTicket[] }>(apiClient.get("/api/fleet-tms/maintenance", { params })),
  fuel: (params: { anomaliesOnly?: boolean; page?: number; pageSize?: number } = {}) =>
    unwrap<{ total: number; page: number; pageSize: number; items: FleetFuelEvent[] }>(apiClient.get("/api/fleet-tms/fuel", { params })),
  dispatchShipment: (id: string, body: { vehicleNumber?: string; driverName?: string; routeCode?: string; notes?: string }) =>
    unwrap<FleetShipment>(apiClient.post(`/api/fleet-tms/shipments/${id}/dispatch`, body)),
  serviceVehicle: (id: string, body: { status?: string; healthStatus?: string; nextServiceAtUtc?: string; notes?: string }) =>
    unwrap<FleetVehicle>(apiClient.post(`/api/fleet-tms/vehicles/${id}/service`, body)),
  closeMaintenance: (id: string, body: { status?: string; actualCost?: number; notes?: string }) =>
    unwrap<FleetMaintenanceTicket>(apiClient.post(`/api/fleet-tms/maintenance/${id}/close`, body)),
  flagFuelEvent: (id: string, body: { anomalyFlag: boolean; notes?: string }) =>
    unwrap<FleetFuelEvent>(apiClient.post(`/api/fleet-tms/fuel/${id}/flag`, body)),
};

export const publicTrackingApi = {
  track: (token: string) => unwrap<PublicTrackingSummary>(apiClient.get(`/api/public/shipments/track/${token}`)),
  events: (token: string) => unwrap<{ items: PublicTrackingSummary["publicEvents"] }>(apiClient.get(`/api/public/shipments/track/${token}/events`)),
  pod: (token: string) => unwrap<{ items: PublicTrackingSummary["pod"] }>(apiClient.get(`/api/public/shipments/track/${token}/pod`)),
};

export const fleetLifecycleApi = {
  getStops: (shipmentId: string) => unwrap<{ items: ShipmentStop[] }>(apiClient.get(`/api/fleet-tms/shipments/${shipmentId}/stops`)),
  getShipmentEvents: (shipmentId: string) => unwrap<{ items: ShipmentEvent[] }>(apiClient.get(`/api/fleet-tms/shipments/${shipmentId}/events`)),
  getTrackingLinks: (shipmentId: string) => unwrap<{ items: CustomerTrackingLink[] }>(apiClient.get(`/api/fleet-tms/shipments/${shipmentId}/tracking-link`)),
  createStop: (shipmentId: string, body: Partial<ShipmentStop> & { stopType: string; sequenceNo: number; locationName: string; plannedArrivalAt: string }) =>
    unwrap<ShipmentStop>(apiClient.post(`/api/fleet-tms/shipments/${shipmentId}/stops`, body)),
  updateStop: (shipmentId: string, stopId: string, body: Partial<ShipmentStop> & { stopType: string; sequenceNo: number; plannedArrivalAt: string }) =>
    unwrap<ShipmentStop>(apiClient.put(`/api/fleet-tms/shipments/${shipmentId}/stops/${stopId}`, body)),
  arriveStop: (shipmentId: string, stopId: string, body: { notes?: string } = {}) =>
    unwrap<ShipmentStop>(apiClient.post(`/api/fleet-tms/shipments/${shipmentId}/stops/${stopId}/arrive`, body)),
  completeStop: (shipmentId: string, stopId: string, body: { notes?: string } = {}) =>
    unwrap<ShipmentStop>(apiClient.post(`/api/fleet-tms/shipments/${shipmentId}/stops/${stopId}/complete`, body)),
  getPod: (shipmentId: string) => unwrap<{ items: ProofOfDelivery[] }>(apiClient.get(`/api/fleet-tms/shipments/${shipmentId}/pod`)),
  createPod: (shipmentId: string, body: {
    stopId: string;
    recipientName?: string;
    recipientPhone?: string;
    signatureUrl?: string;
    photoUrl?: string;
    documentUrl?: string;
    notes?: string;
    deliveryCondition?: string;
    capturedLatitude?: number | null;
    capturedLongitude?: number | null;
  }) => unwrap<ProofOfDelivery>(apiClient.post(`/api/fleet-tms/shipments/${shipmentId}/pod`, body)),
  updatePod: (shipmentId: string, podId: string, body: {
    recipientName?: string;
    recipientPhone?: string;
    signatureUrl?: string;
    photoUrl?: string;
    documentUrl?: string;
    notes?: string;
    deliveryCondition?: string;
    capturedLatitude?: number | null;
    capturedLongitude?: number | null;
  }) => unwrap<ProofOfDelivery>(apiClient.put(`/api/fleet-tms/shipments/${shipmentId}/pod/${podId}`, body)),
  submitPod: (shipmentId: string, podId: string) => unwrap<ProofOfDelivery>(apiClient.post(`/api/fleet-tms/shipments/${shipmentId}/pod/${podId}/submit`, {})),
  verifyPod: (shipmentId: string, podId: string) => unwrap<ProofOfDelivery>(apiClient.post(`/api/fleet-tms/shipments/${shipmentId}/pod/${podId}/verify`, {})),
  rejectPod: (shipmentId: string, podId: string, body: { notes?: string } = {}) => unwrap<ProofOfDelivery>(apiClient.post(`/api/fleet-tms/shipments/${shipmentId}/pod/${podId}/reject`, body)),
  createTrackingLink: (shipmentId: string, body: { token?: string; expiresAtUtc?: string }) =>
    unwrap<CustomerTrackingLink>(apiClient.post(`/api/fleet-tms/shipments/${shipmentId}/tracking-link`, body)),
  revokeTrackingLink: (shipmentId: string, linkId: string) =>
    unwrap<unknown>(apiClient.delete(`/api/fleet-tms/shipments/${shipmentId}/tracking-link/${linkId}`)),
  getDriverTasks: (params: { driverName?: string } = {}) =>
    unwrap<{ items: DriverTask[] }>(apiClient.get("/api/fleet-tms/driver/tasks", { params })),
  getDriverTask: (taskId: string) => unwrap<DriverTask>(apiClient.get(`/api/fleet-tms/driver/tasks/${taskId}`)),
  arriveDriverTask: (taskId: string) => unwrap<DriverTask>(apiClient.post(`/api/fleet-tms/driver/tasks/${taskId}/arrive`, {})),
  completeDriverTask: (taskId: string, body: { notes?: string } = {}) => unwrap<DriverTask>(apiClient.post(`/api/fleet-tms/driver/tasks/${taskId}/complete`, body)),
  markInvoiceReady: (shipmentId: string, body: { override?: boolean; notes?: string } = {}) =>
    unwrap<FleetShipment>(apiClient.post(`/api/fleet-tms/shipments/${shipmentId}/mark-invoice-ready`, body)),
  invoiceReady: () => unwrap<{ items: FleetShipment[] }>(apiClient.get("/api/fleet-tms/shipments/invoice-ready")),
};

// ── Commercial slice (carriers / bookings / quotes) — DEFERRED ─────────────────
// The commercial carrier surface overlaps the existing OpsTrax `carriers` table and
// is intentionally NOT part of PR1. These types + stub keep the ported Fleet TMS
// pages/drawer compiling and rendering an honest empty state; reads resolve empty and
// writes reject with a clear message. No /api/fleet-tms/carriers* backend exists yet.
export interface Carrier {
  id: string;
  name: string;
  code: string;
  status: string;
  region: string;
  serviceType: string;
  vatNumber: string;
  commercialRegistrationNo: string;
  transportDocumentNo: string;
  permitNo: string;
  nationalAddressBuildingNo: string;
  nationalAddressAdditionalNo: string;
  district: string;
  city: string;
  postalCode: string;
  country: string;
  documentStatus: string;
  expiryStatus: string;
  hijriExpiryDate?: string | null;
  gregorianExpiryDate?: string | null;
  onTimeScore: number;
  damageScore: number;
  costScore: number;
  notes: string;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
}

export interface ShipmentCarrierAssignment {
  id: string;
  shipmentId: string;
  carrierId: string;
  status: string;
  quotedAmount: number;
  agreedAmount: number;
  notes: string;
  assignedAtUtc: string;
  updatedAtUtc?: string | null;
}

export interface BookingRequest {
  id: string;
  requestNumber: string;
  customerName: string;
  origin: string;
  destination: string;
  status: string;
  estimatedWeightKg: number;
  estimatedVolumeCbm: number;
  requestedAtUtc: string;
  notes: string;
}

export interface QuoteRequest {
  id: string;
  quoteNumber: string;
  customerName: string;
  origin: string;
  destination: string;
  status: string;
  estimatedAmount: number;
  marginPct: number;
  requestedAtUtc: string;
  notes: string;
}

const DEFERRED = "Carrier & booking management ships in a later Fleet TMS release.";

export const fleetCommercialApi = {
  carriers: () => Promise.resolve({ items: [] as Carrier[] }),
  carrier: (_id: string): Promise<Carrier> => Promise.reject(new Error(DEFERRED)),
  createCarrier: (_body: Partial<Carrier> & { name: string; code: string }): Promise<Carrier> => Promise.reject(new Error(DEFERRED)),
  updateCarrier: (_id: string, _body: Partial<Carrier>): Promise<Carrier> => Promise.reject(new Error(DEFERRED)),
  assignShipmentCarrier: (_shipmentId: string, _body: { carrierId: string; quotedAmount?: number; agreedAmount?: number; notes?: string }): Promise<ShipmentCarrierAssignment> => Promise.reject(new Error(DEFERRED)),
  bookingRequests: () => Promise.resolve({ items: [] as BookingRequest[] }),
  createBookingRequest: (_body: Partial<BookingRequest> & { requestNumber: string; customerName: string; origin: string; destination: string }): Promise<BookingRequest> => Promise.reject(new Error(DEFERRED)),
  quoteRequests: () => Promise.resolve({ items: [] as QuoteRequest[] }),
  createQuoteRequest: (_body: Partial<QuoteRequest> & { quoteNumber: string; customerName: string; origin: string; destination: string }): Promise<QuoteRequest> => Promise.reject(new Error(DEFERRED)),
};
