// Market-pack + regional compliance service (tenant). Routes through the shared
// apiClient + unwrap (envelope-aware). Backend enforces market-pack entitlement
// (deny-by-default) and compliance permissions; a 403 here means the tenant's pack
// is disabled or the user lacks compliance access.
import { apiClient, unwrap } from "@/services/apiClient";

type AnyRecord = Record<string, any>;

export const marketPackApi = {
  // Catalog
  marketPacks: () => unwrap<AnyRecord>(apiClient.get("/api/market-packs")),
  canadaPack: () => unwrap<AnyRecord>(apiClient.get("/api/market-packs/canada-na")),
  canadaRequirements: () => unwrap<AnyRecord>(apiClient.get("/api/market-packs/canada-na/requirements")),
  saudiPack: () => unwrap<AnyRecord>(apiClient.get("/api/market-packs/saudi-gcc")),
  saudiRequirements: () => unwrap<AnyRecord>(apiClient.get("/api/market-packs/saudi-gcc/requirements")),

  // Canada / NA
  driverDocuments: () => unwrap<AnyRecord>(apiClient.get("/api/fleet-compliance/driver-documents")),
  createDriverDocument: (body: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/fleet-compliance/driver-documents", body)),
  updateDriverDocument: (id: number, body: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/fleet-compliance/driver-documents/${id}`, body)),
  vehicleInspections: () => unwrap<AnyRecord>(apiClient.get("/api/fleet-compliance/vehicle-inspections")),
  createVehicleInspection: (body: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/fleet-compliance/vehicle-inspections", body)),
  expiries: () => unwrap<AnyRecord>(apiClient.get("/api/fleet-compliance/expiries")),
  iftaReadiness: () => unwrap<AnyRecord>(apiClient.get("/api/fleet-compliance/ifta-readiness")),
  createJurisdictionMileage: (body: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/fleet-compliance/jurisdiction-mileage", body)),
  createJurisdictionFuel: (body: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/fleet-compliance/jurisdiction-fuel", body)),
  hosReadiness: () => unwrap<AnyRecord>(apiClient.get("/api/fleet-compliance/hos-readiness")),

  // Saudi / GCC
  saudiRegions: () => unwrap<AnyRecord>(apiClient.get("/api/fleet-compliance/saudi/regions")),
  saudiDocuments: () => unwrap<AnyRecord>(apiClient.get("/api/fleet-compliance/saudi/documents")),
  createSaudiDocument: (body: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/fleet-compliance/saudi/documents", body)),
  saudiExpiries: () => unwrap<AnyRecord>(apiClient.get("/api/fleet-compliance/saudi/expiries")),
  saudiVatReadiness: () => unwrap<AnyRecord>(apiClient.get("/api/fleet-compliance/saudi/vat-readiness")),
  setSaudiVatReadiness: (body: AnyRecord) => unwrap<AnyRecord>(apiClient.put("/api/fleet-compliance/saudi/vat-readiness", body)),
};
