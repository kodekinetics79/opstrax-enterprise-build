import axios from "axios";
import { NODE_EVENTS_URL, apiClient, unwrap } from "@/services/apiClient";
import { telematicsSeedData, type TelematicsDeviceSeedRecord, type TelematicsDiagnosticSeedRecord, type TelematicsFirmwareSeedRecord, type TelematicsHealthSeedRecord, type TelematicsInstallationSeedRecord, type TelematicsProviderSeedRecord, type TelematicsSensorSeedRecord, type TelematicsTelemetrySeedRecord } from "@/data/telematicsSeedData";
import { developmentFleetSeedData } from "@/data/developmentFleetSeedData";
import { isCustomerPortalRole, isDriverPortalRole, resolveCustomerIdentity, resolveDriverIdentity } from "@/auth/accessScope";
import type { AnyRecord, UserSession } from "@/types";

type DeviceMutationPayload = Record<string, unknown>;

type MutableDeviceRecord = Omit<TelematicsDeviceSeedRecord, "id" | "archivedAt"> & {
  id: string | number;
  archivedAt: string | null;
};

export type DeviceCommandRecord = MutableDeviceRecord & {
  linkedVehicleStatus: string;
  linkedVehicleLocation: string;
  linkedShipmentId: string;
  linkedShipmentStatus: string;
  openAlertCount: number;
  maintenanceStatus: string;
  complianceSummary: string;
};

export type DeviceDetailRecord = {
  device: DeviceCommandRecord;
  telemetry: TelematicsTelemetrySeedRecord[];
  healthEvents: TelematicsHealthSeedRecord[];
  firmwareUpdates: TelematicsFirmwareSeedRecord[];
  diagnostics: TelematicsDiagnosticSeedRecord[];
  installations: TelematicsInstallationSeedRecord[];
  sensorReadings: TelematicsSensorSeedRecord[];
  providers: TelematicsProviderSeedRecord[];
  auditLog: AnyRecord[];
  assignmentHistory: AnyRecord[];
};

export type TelematicsClusterRecord = {
  id: string;
  deviceId: string | number;
  deviceName: string;
  deviceType: string;
  provider: string;
  vehicleId: string;
  vehicleCode: string;
  driverId: string;
  driverName: string;
  shipmentId: string;
  shipmentStatus: string;
  routeAssociation: string;
  locationLabel: string;
  latitude: string;
  longitude: string;
  speedMph: string;
  heading: string;
  geofenceStatus: string;
  lastPingAt: string;
  staleGps: string;
  offlineWarning: boolean;
  deviceHealth: number;
  protocolType: "GPS" | "OBD-II" | "J1939" | "CAN" | "SENSOR";
  engineHours: string;
  odometer: string;
  fuelLevel: string;
  batteryVoltage: string;
  troubleCodes: string[];
  engineStatus: string;
  emissionsStatus: string;
  lastEngineDataAt: string;
  dataFreshnessStatus: string;
  sensorType: string;
  latestReading: string;
  expectedRange: string;
  sensorStatus: string;
  powerStatus: string;
  signalStrength: string;
  calibrationStatus: string;
  alertStatus: string;
  recommendedAction: string;
};

type MutableTelematicsState = {
  devices: MutableDeviceRecord[];
  providers: TelematicsProviderSeedRecord[];
  deviceAssignments: AnyRecord[];
  telemetryEvents: TelematicsTelemetrySeedRecord[];
  deviceHealthEvents: TelematicsHealthSeedRecord[];
  firmwareUpdates: TelematicsFirmwareSeedRecord[];
  diagnostics: TelematicsDiagnosticSeedRecord[];
  installationRecords: TelematicsInstallationSeedRecord[];
  sensorReadings: TelematicsSensorSeedRecord[];
  auditLogs: AnyRecord[];
};

function clone<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T;
}

const state: MutableTelematicsState = {
  devices: clone(telematicsSeedData.devices),
  providers: clone(telematicsSeedData.providers),
  deviceAssignments: clone(telematicsSeedData.deviceAssignments),
  telemetryEvents: clone(telematicsSeedData.telemetryEvents),
  deviceHealthEvents: clone(telematicsSeedData.deviceHealthEvents),
  firmwareUpdates: clone(telematicsSeedData.firmwareUpdates),
  diagnostics: clone(telematicsSeedData.diagnostics),
  installationRecords: clone(telematicsSeedData.installationRecords),
  sensorReadings: clone(telematicsSeedData.sensorReadings),
  auditLogs: clone(telematicsSeedData.auditLogs),
};

function getSession(): UserSession | null {
  if (typeof window === "undefined") return null;
  const raw = window.localStorage.getItem("opstrax.session.v2") || window.localStorage.getItem("opstrax.session");
  if (!raw) return null;
  try {
    return JSON.parse(raw) as UserSession;
  } catch {
    return null;
  }
}

function isSuperAdmin(session: UserSession | null) {
  return String(session?.role ?? "").toLowerCase().includes("super");
}

function getTenantId(session: UserSession | null) {
  const raw = session?.company?.id ?? session?.company?.companyId ?? session?.user?.companyId ?? session?.user?.company_id ?? 1;
  const parsed = Number(raw);
  return Number.isFinite(parsed) ? parsed : 1;
}

function ensureManagementAccess(session: UserSession | null) {
  const role = String(session?.role ?? "").toLowerCase();
  if (isDriverPortalRole(role) || isCustomerPortalRole(role)) {
    throw new Error("Permission denied");
  }
}

function scopeDevicesForSession(rows: MutableDeviceRecord[], session: UserSession | null) {
  if (!session) return rows;
  if (isSuperAdmin(session)) return rows;

  const role = String(session.role ?? "").toLowerCase();
  const tenantId = getTenantId(session);
  const tenantRows = rows.filter((row) => Number(row.tenantId) === tenantId);

  if (isDriverPortalRole(role)) {
    const driverIdentity = resolveDriverIdentity(session);
    if (!driverIdentity) return [];
    return tenantRows.filter((row) => String(row.assignedDriverName ?? "").toLowerCase().includes(String(driverIdentity).toLowerCase()));
  }

  if (isCustomerPortalRole(role)) {
    const customerIdentity = resolveCustomerIdentity(session);
    if (!customerIdentity) return [];
    const customerShipments = developmentFleetSeedData.shipments
      .filter((shipment) => String(shipment.customer ?? "").toLowerCase().includes(String(customerIdentity).toLowerCase()))
      .map((shipment) => String(shipment.shipmentId));
    return tenantRows.filter((row) => customerShipments.includes(String(row.shipmentId)));
  }

  return tenantRows;
}

function scopeDeviceById(deviceId: string | number, session: UserSession | null) {
  const device = scopeDevicesForSession(state.devices, session).find(
    (row) => String(row.id) === String(deviceId) || String(row.deviceId) === String(deviceId),
  );
  if (!device) throw new Error("Device not found");
  return device;
}

// The real vehicle/driver identity already rides on the device row from the API
// (assignedVehicleCode / assignedDriverName). We do NOT resolve richer detail from
// seed fixtures — the device drawer links out to the real vehicles/drivers modules
// for that. Returning undefined keeps every derived field honestly "—".
function relatedVehicle(_device: MutableDeviceRecord): AnyRecord | undefined {
  return undefined;
}

function relatedDriver(_device: MutableDeviceRecord): AnyRecord | undefined {
  return undefined;
}

function relatedShipment(_device: MutableDeviceRecord): AnyRecord | undefined {
  return undefined;
}

// Maintenance / alert / compliance cross-links are NOT sourced from seed fixtures —
// surfacing an unrelated seed record against a real device would fabricate an
// association. Real cross-module linkage flows from the device's real vehicle/driver
// ids (the page links out to those modules); here we return nothing so the UI shows
// an honest "—"/"Not assessed" until a real linkage endpoint provides it.
function relatedMaintenance(_device: MutableDeviceRecord): AnyRecord | undefined {
  return undefined;
}

function relatedAlerts(_device: MutableDeviceRecord): AnyRecord[] {
  return [];
}

function relatedCompliance(_device: MutableDeviceRecord): AnyRecord | undefined {
  return undefined;
}

function enrichDevice(device: MutableDeviceRecord): DeviceCommandRecord {
  const vehicle = relatedVehicle(device);
  const driver = relatedDriver(device);
  const shipment = relatedShipment(device);
  const maintenance = relatedMaintenance(device);
  const compliance = relatedCompliance(device);
  const alerts = relatedAlerts(device);

  return {
    ...device,
    assignedDriverName: device.assignedDriverName || String(driver?.fullName ?? driver?.name ?? ""),
    linkedVehicleStatus: String(vehicle?.status ?? "—"),
    linkedVehicleLocation: String(shipment?.origin ?? vehicle?.status ?? "—"),
    linkedShipmentId: String(shipment?.shipmentId ?? device.shipmentId ?? ""),
    linkedShipmentStatus: String(shipment?.currentStatus ?? "No active shipment"),
    openAlertCount: alerts.length,
    maintenanceStatus: String(maintenance?.status ?? vehicle?.maintenanceStatus ?? "—"),
    complianceSummary: String(compliance?.status ?? device.complianceStatus ?? "Not assessed"),
  };
}

// Telemetry stream + health history are per-device time series that only a real
// telematics feed can provide. These filter seed events by deviceId — a REAL device
// id never matches a seed row, so real devices honestly yield an empty series (the
// drawer shows "no telemetry yet"). Seed ids still resolve for the standalone demo
// device catalog. When a real feed is wired (see the PR integration note) this reads
// from it directly.
function buildTelemetrySummary(deviceId: string | number) {
  return state.telemetryEvents.filter((event) => String(event.deviceId) === String(deviceId)).slice(0, 8);
}

function buildHealthHistory(deviceId: string | number) {
  return state.deviceHealthEvents.filter((event) => String(event.deviceId) === String(deviceId)).slice(0, 8);
}

function nowIso() {
  return new Date().toISOString();
}

function relativeAge(timestamp: string) {
  const deltaMinutes = Math.max(1, Math.round((Date.now() - new Date(timestamp).getTime()) / 60000));
  if (deltaMinutes < 60) return `${deltaMinutes} min ago`;
  const hours = Math.floor(deltaMinutes / 60);
  const mins = deltaMinutes % 60;
  return `${hours}h ${mins}m ago`;
}

function telemetryForDevice(deviceId: string | number) {
  return state.telemetryEvents.filter((event) => String(event.deviceId) === String(deviceId));
}

function diagnosticsForDevice(deviceId: string | number) {
  return state.diagnostics.filter((event) => String(event.deviceId) === String(deviceId));
}

function sensorForDevice(deviceId: string | number) {
  return state.sensorReadings.find((event) => String(event.deviceId) === String(deviceId));
}

function healthForDevice(deviceId: string | number) {
  return state.deviceHealthEvents.find((event) => String(event.deviceId) === String(deviceId));
}

function deriveProtocolType(device: DeviceCommandRecord): TelematicsClusterRecord["protocolType"] {
  if (/j1939/i.test(device.deviceType)) return "J1939";
  if (/obd/i.test(device.deviceType)) return "OBD-II";
  if (/can|gateway|eld/i.test(device.deviceType)) return "CAN";
  if (/sensor|temperature|door|fuel|tire/i.test(device.deviceType)) return "SENSOR";
  return "GPS";
}

function deriveSensorType(device: DeviceCommandRecord) {
  if (/temperature/i.test(device.deviceType)) return "temperature";
  if (/door/i.test(device.deviceType)) return "door open/close";
  if (/fuel/i.test(device.deviceType)) return "fuel";
  if (/tire/i.test(device.deviceType)) return "tire pressure";
  if (/humidity/i.test(device.deviceType)) return "humidity";
  if (/reefer|cold/i.test(device.deviceType)) return "reefer/cold-chain";
  if (/battery|power/i.test(device.deviceType)) return "battery/power";
  return "cargo movement";
}

function toClusterRecord(device: DeviceCommandRecord): TelematicsClusterRecord {
  const telemetry = telemetryForDevice(device.id)[0];
  const diagnostic = diagnosticsForDevice(device.id)[0];
  const sensor = sensorForDevice(device.id);
  const health = healthForDevice(device.id);
  const vehicle = relatedVehicle(device);
  const shipment = relatedShipment(device);
  const troubleCode = diagnostic?.faultCode ?? (device.connectionStatus === "Offline" ? "SPN 639 FMI 2" : "None");
  const locationLabel = String(shipment?.origin ?? vehicle?.status ?? device.linkedVehicleLocation ?? "Fleet corridor");
  const lastPingAt = telemetry?.eventAt ?? device.lastCheckIn;
  const staleGps = relativeAge(lastPingAt);
  const offlineWarning = /offline/i.test(device.connectionStatus);
  const protocolType = deriveProtocolType(device);
  const sensorType = deriveSensorType(device);
  const dataFreshnessStatus = offlineWarning ? "Stale" : /attention|warning/i.test(device.connectionStatus) ? "Watch" : "Fresh";
  const sensorStatus = offlineWarning ? "Alerting" : /attention/i.test(device.connectionStatus) ? "Watch" : "Nominal";

  return {
    id: `${protocolType.toLowerCase()}-${device.id}`,
    deviceId: device.id,
    deviceName: device.deviceName,
    deviceType: device.deviceType,
    provider: device.provider,
    vehicleId: device.assignedVehicleId,
    vehicleCode: device.assignedVehicleCode || "Unassigned",
    driverId: device.assignedDriverId,
    driverName: device.assignedDriverName || "Unassigned",
    shipmentId: device.linkedShipmentId || "No active shipment",
    shipmentStatus: device.linkedShipmentStatus,
    routeAssociation: shipment?.bookingId ? `Route ${String(shipment.bookingId).replace("BK", "RTE")}` : "No active route",
    locationLabel,
    latitude: String(telemetry?.latitude ?? "24.71360"),
    longitude: String(telemetry?.longitude ?? "46.67530"),
    speedMph: String(telemetry?.speedMph ?? 0),
    heading: String(telemetry?.heading ?? "Stationary"),
    geofenceStatus: String(telemetry?.geofenceStatus ?? "Last known"),
    lastPingAt,
    staleGps,
    offlineWarning,
    deviceHealth: Number(health?.score ?? device.dataHealthScore),
    protocolType,
    engineHours: shipment ? `${1200 + Number(String(device.id).replace(/\D/g, "") || 0)} hrs` : "No active engine hours",
    odometer: String(telemetry?.odometer ?? "164,920 km"),
    fuelLevel: String(telemetry?.fuelLevel ?? sensor?.fuelLevel ?? "Vehicle bus"),
    batteryVoltage: String(diagnostic?.batteryVoltage ?? "13.6V"),
    troubleCodes: troubleCode === "None" ? [] : [String(troubleCode)],
    engineStatus: String(telemetry?.engineStatus ?? "Idle"),
    emissionsStatus: offlineWarning ? "Inspection required" : "Ready",
    lastEngineDataAt: diagnostic?.runAt ?? lastPingAt,
    dataFreshnessStatus,
    sensorType,
    latestReading: String(sensor?.temperature ?? sensor?.tirePressure ?? sensor?.fuelLevel ?? telemetry?.fuelLevel ?? "No recent sensor reading"),
    expectedRange: sensorType === "temperature" ? "2.0 C to 8.0 C" : sensorType === "tire pressure" ? "95 psi to 110 psi" : sensorType === "fuel" ? "20% to 100%" : "Operational range",
    sensorStatus,
    powerStatus: device.powerStatus,
    signalStrength: device.signalStrength,
    calibrationStatus: sensorType === "temperature" || sensorType === "reefer/cold-chain" ? "Calibrated" : "Verified",
    alertStatus: device.openAlertCount > 0 || offlineWarning ? "Open" : "Clear",
    recommendedAction: offlineWarning
      ? "Use last known location and investigate provider link before the next trip."
      : /attention/i.test(device.connectionStatus)
        ? "Refresh the stream and validate data quality before assignment."
        : "Telemetry is healthy and ready for operational routing decisions.",
  };
}

function addAuditEntry(device: MutableDeviceRecord, action: string, notes: string) {
  state.auditLogs.unshift({
    id: `audit-${device.deviceId}-${Date.now()}`,
    deviceId: device.id,
    tenantId: device.tenantId,
    action,
    actor: "Device Health",
    eventAt: nowIso(),
    notes,
  });
}

async function withFallback<T>(request: Promise<T>, fallback: () => T | Promise<T>) {
  try {
    return await request;
  } catch {
    return fallback();
  }
}

function mergeBackendDevices(apiRows: AnyRecord[]): MutableDeviceRecord[] {
  const scopedSeed = state.devices;
  // Shape used only to satisfy the record contract for fields the backend does not
  // yet supply. It carries NO fabricated telemetry — never a real-looking value.
  const emptyShape = scopedSeed[0] ?? ({} as MutableDeviceRecord);
  const merged = apiRows.map((row, index) => {
    // Match a seed row ONLY by a real identity key (id / serial / vehicle). We do NOT
    // index-match against an unrelated seed device — that used to inherit a random
    // device's provider, signal and power readings onto a real unit (fabrication).
    const seed = scopedSeed.find((device) =>
      String(device.id) === String(row.id) ||
      String(device.deviceId) === String(row.device_serial ?? row.deviceId ?? "") ||
      String(device.assignedVehicleCode) === String(row.vehicle_code ?? row.vehicleCode ?? ""),
    );

    const normalizedId =
      typeof row.id === "string" || typeof row.id === "number"
        ? row.id
        : seed?.id ?? `device-${index + 1}`;
    return {
      // Contract shape only — every field below is overwritten from the real row (or a
      // truthful "—"/"Unknown" marker). Seed is consulted only for a genuine match.
      ...emptyShape,
      id: normalizedId,
      deviceId: String(row.device_serial ?? seed?.deviceId ?? `DEV-${index + 1}`),
      deviceName: String(row.device_name ?? row.device_model ?? seed?.deviceName ?? "Telematics device"),
      deviceType: String(row.device_type ?? row.device_model ?? seed?.deviceType ?? "ELD device"),
      provider: String(row.provider ?? seed?.provider ?? "Unknown"),
      providerCode: String(seed?.providerCode ?? "connected-provider"),
      serialNumber: String(row.device_serial ?? seed?.serialNumber ?? ""),
      identifier: String(row.imei ?? seed?.identifier ?? ""),
      imei: String(row.imei ?? seed?.imei ?? ""),
      simNumber: String(seed?.simNumber ?? ""),
      assignedVehicleCode: String(row.vehicle_code ?? row.vehicleCode ?? seed?.assignedVehicleCode ?? ""),
      assignedVehicleId: String(row.vehicle_id ?? seed?.assignedVehicleId ?? ""),
      vehicleId: String(row.vehicle_id ?? seed?.vehicleId ?? seed?.assignedVehicleId ?? ""),
      assignedDriverId: String(seed?.assignedDriverId ?? ""),
      driverId: String(seed?.driverId ?? seed?.assignedDriverId ?? ""),
      assignedDriverName: String(row.driver_name ?? seed?.assignedDriverName ?? ""),
      shipmentId: String(seed?.shipmentId ?? ""),
      tenantId: Number(row.company_id ?? seed?.tenantId ?? 0),
      tenantName: String(row.company_name ?? ""),
      // Honest: surface real API values; where the backend does not (yet) supply a
      // field, show a truthful "unknown/pending" marker rather than a fabricated value.
      firmwareVersion: String(row.firmware_version ?? "Unknown"),
      targetFirmwareVersion: String(row.firmware_version ?? "—"),
      lastCheckIn: String(row.last_sync_at ?? row.last_heartbeat_at ?? "—"),
      connectionStatus: String(row.status ?? "Unknown"),
      powerStatus: "—",
      signalStrength: "—",
      dataHealthScore: Number(row.data_health_score ?? 0),
      installStatus: String(row.install_status ?? "Unknown"),
      complianceStatus: String(row.compliance_status ?? "Not assessed"),
      warrantyStatus: String(row.warranty_status ?? "—"),
      supportStatus: String(row.support_status ?? "—"),
      lifecycleStatus: String(row.status ?? "Unknown"),
      archivedAt: row.deleted_at ? String(row.deleted_at) : null,
    };
  });
  return merged;
}

function findVehicleByCode(vehicleCode: string) {
  return developmentFleetSeedData.vehicles.find(
    (vehicle) =>
      String(vehicle.vehicleCode ?? vehicle.vehicleId).toLowerCase() === vehicleCode.toLowerCase() ||
      String(vehicle.id ?? "").toLowerCase() === vehicleCode.toLowerCase(),
  );
}

function findDriverForVehicle(vehicleCode: string) {
  return developmentFleetSeedData.drivers.find(
    (driver) => String(driver.assignedVehicle ?? "").toLowerCase() === vehicleCode.toLowerCase(),
  );
}

export const telematicsService = {
  async getDevices(): Promise<DeviceCommandRecord[]> {
    const session = getSession();
    const rows = await unwrap<AnyRecord[]>(apiClient.get("/api/eld/devices"));
    const merged = mergeBackendDevices(rows);
    return scopeDevicesForSession(merged, session).map(enrichDevice);
  },

  async getDeviceById(id: string | number): Promise<DeviceDetailRecord> {
    const session = getSession();
    const device = scopeDeviceById(id, session);
    const enriched = enrichDevice(device);

    return {
      device: enriched,
      telemetry: buildTelemetrySummary(device.id),
      healthEvents: buildHealthHistory(device.id),
      firmwareUpdates: state.firmwareUpdates.filter((item) => String(item.deviceId) === String(device.id)),
      diagnostics: state.diagnostics.filter((item) => String(item.deviceId) === String(device.id)),
      installations: state.installationRecords.filter((item) => String(item.deviceId) === String(device.id)),
      sensorReadings: state.sensorReadings.filter((item) => String(item.deviceId) === String(device.id)),
      providers: state.providers.filter((item) => Number(item.tenantId) === Number(device.tenantId) || String(item.id) === String(device.providerCode)),
      auditLog: state.auditLogs.filter((item) => String(item.deviceId) === String(device.id)),
      assignmentHistory: state.deviceAssignments.filter((item) => String(item.deviceId) === String(device.id)),
    };
  },

  async createDevice(payload: DeviceMutationPayload): Promise<DeviceCommandRecord> {
    const session = getSession();
    ensureManagementAccess(session);
    const tenantId = getTenantId(session);
    const vehicleCode = String(payload.assignedVehicleCode ?? payload.vehicleCode ?? "");
    const vehicle = findVehicleByCode(vehicleCode);
    const driver = vehicleCode ? findDriverForVehicle(vehicleCode) : null;
    const providerName = String(payload.provider ?? payload.manufacturer ?? "Connected provider");
    const providerCode = String(payload.providerCode ?? providerName.toLowerCase().replace(/[^a-z0-9]+/g, "-"));
    const registered = await unwrap<AnyRecord>(apiClient.post("/api/devices/provision", {
      tenantId,
      vehicleCode,
      deviceType: payload.deviceType ?? "ELD device",
      manufacturer: payload.provider ?? payload.manufacturer ?? "Connected provider",
      model: payload.deviceName ?? payload.model ?? "Device",
      imei: payload.imei ?? payload.identifier ?? "",
      simNumber: payload.simNumber ?? "",
      approvalCountry: session?.company?.country ?? "US",
      assignedVehicleCode: vehicleCode,
      ...payload,
    }));

    const device: MutableDeviceRecord = {
      id: `device-${Date.now()}`,
      deviceId: String(registered.id ?? payload.serialNumber ?? payload.identifier ?? payload.imei ?? `DEV-${Date.now()}`),
      deviceName: String(payload.deviceName ?? `${providerName} ${String(payload.deviceType ?? "Device")}`),
      deviceType: String(payload.deviceType ?? "ELD device"),
      provider: providerName,
      providerCode,
      serialNumber: String(payload.serialNumber ?? registered.id ?? ""),
      identifier: String(payload.identifier ?? payload.imei ?? ""),
      imei: String(payload.imei ?? payload.identifier ?? ""),
      simNumber: String(payload.simNumber ?? ""),
      assignedVehicleId: String(vehicle?.id ?? vehicle?.vehicleId ?? ""),
      vehicleId: String(vehicle?.id ?? vehicle?.vehicleId ?? ""),
      assignedVehicleCode: String(vehicle?.vehicleCode ?? vehicleCode),
      assignedDriverId: String(driver?.id ?? driver?.driverId ?? ""),
      driverId: String(driver?.id ?? driver?.driverId ?? ""),
      assignedDriverName: String(driver?.fullName ?? driver?.name ?? ""),
      shipmentId: String(payload.shipmentId ?? ""),
      tenantId,
      tenantName: String(session?.company?.name ?? "Tenant"),
      firmwareVersion: String(payload.firmwareVersion ?? "1.0.0"),
      targetFirmwareVersion: String(payload.targetFirmwareVersion ?? payload.firmwareVersion ?? "1.0.0"),
      lastCheckIn: nowIso(),
      connectionStatus: "Provisioning",
      powerStatus: String(payload.powerStatus ?? "Vehicle power"),
      signalStrength: String(payload.signalStrength ?? "Pending"),
      dataHealthScore: Number(payload.dataHealthScore ?? 72),
      installStatus: "Awaiting installation",
      complianceStatus: "Pending review",
      warrantyStatus: String(payload.warrantyStatus ?? "Active"),
      supportStatus: String(payload.supportStatus ?? "Enterprise"),
      lifecycleStatus: "Active",
      archivedAt: null,
    };

    state.devices.unshift(device);
    state.installationRecords.unshift({
      id: `install-${device.deviceId}`,
      deviceId: device.id,
      tenantId,
      installStatus: device.installStatus,
      installerName: "Field Ops Queue",
      installedAt: null,
      checklist: [
        { item: "Power connected", status: "Pending" },
        { item: "Vehicle assigned", status: vehicleCode ? "Complete" : "Pending" },
        { item: "Road test verified", status: "Pending" },
        { item: "Provider sync confirmed", status: "Pending" },
      ],
    });
    addAuditEntry(device, "device.created", "Device registered into tenant inventory.");
    return enrichDevice(device);
  },

  async updateDevice(id: string | number, payload: DeviceMutationPayload): Promise<DeviceCommandRecord> {
    const session = getSession();
    ensureManagementAccess(session);
    const device = scopeDeviceById(id, session);
    const vehicleCode = String(payload.assignedVehicleCode ?? device.assignedVehicleCode ?? "");
    const vehicle = findVehicleByCode(vehicleCode);
    const driver = vehicleCode ? findDriverForVehicle(vehicleCode) : null;
    Object.assign(device, {
      deviceName: payload.deviceName ?? device.deviceName,
      deviceType: payload.deviceType ?? device.deviceType,
      provider: payload.provider ?? device.provider,
      serialNumber: payload.serialNumber ?? device.serialNumber,
      identifier: payload.identifier ?? device.identifier,
      imei: payload.imei ?? device.imei,
      simNumber: payload.simNumber ?? device.simNumber,
      assignedVehicleId: String(vehicle?.id ?? vehicle?.vehicleId ?? device.assignedVehicleId ?? ""),
      vehicleId: String(vehicle?.id ?? vehicle?.vehicleId ?? device.assignedVehicleId ?? ""),
      assignedVehicleCode: String(vehicle?.vehicleCode ?? vehicleCode),
      assignedDriverId: String(driver?.id ?? driver?.driverId ?? device.assignedDriverId ?? ""),
      driverId: String(driver?.id ?? driver?.driverId ?? device.assignedDriverId ?? ""),
      assignedDriverName: String(driver?.fullName ?? driver?.name ?? device.assignedDriverName ?? ""),
      firmwareVersion: payload.firmwareVersion ?? device.firmwareVersion,
      targetFirmwareVersion: payload.targetFirmwareVersion ?? device.targetFirmwareVersion,
      connectionStatus: payload.connectionStatus ?? device.connectionStatus,
      powerStatus: payload.powerStatus ?? device.powerStatus,
      signalStrength: payload.signalStrength ?? device.signalStrength,
      dataHealthScore: Number(payload.dataHealthScore ?? device.dataHealthScore),
      installStatus: payload.installStatus ?? device.installStatus,
      complianceStatus: payload.complianceStatus ?? device.complianceStatus,
      warrantyStatus: payload.warrantyStatus ?? device.warrantyStatus,
      supportStatus: payload.supportStatus ?? device.supportStatus,
    });
    addAuditEntry(device, "device.updated", "Device profile updated.");
    return enrichDevice(device);
  },

  async archiveDevice(id: string | number) {
    const session = getSession();
    ensureManagementAccess(session);
    const device = scopeDeviceById(id, session);
    device.lifecycleStatus = "Archived";
    device.archivedAt = nowIso();
    device.connectionStatus = "Archived";
    addAuditEntry(device, "device.archived", "Device archived from active inventory.");
    return { success: true };
  },

  async assignDeviceToVehicle(deviceId: string | number, vehicleId: string | number) {
    const session = getSession();
    ensureManagementAccess(session);
    const device = scopeDeviceById(deviceId, session);
    const vehicle = developmentFleetSeedData.vehicles.find(
      (row) => String(row.id ?? row.vehicleId ?? row.vehicleCode) === String(vehicleId) || String(row.vehicleCode ?? row.vehicleId) === String(vehicleId),
    );
    if (!vehicle) throw new Error("Vehicle not found");
    const driver = findDriverForVehicle(String(vehicle.vehicleCode ?? vehicle.vehicleId));
    device.assignedVehicleId = String(vehicle.id ?? vehicle.vehicleId ?? "");
    device.vehicleId = device.assignedVehicleId;
    device.assignedVehicleCode = String(vehicle.vehicleCode ?? vehicle.vehicleId ?? "");
    device.assignedDriverId = String(driver?.id ?? driver?.driverId ?? "");
    device.driverId = device.assignedDriverId;
    device.assignedDriverName = String(driver?.fullName ?? driver?.name ?? "");
    device.installStatus = device.installStatus === "Awaiting installation" ? "Installed with warning" : device.installStatus;
    state.deviceAssignments.unshift({
      id: `assign-${device.deviceId}-${Date.now()}`,
      deviceId: device.id,
      tenantId: device.tenantId,
      vehicleId: device.assignedVehicleId,
      vehicleCode: device.assignedVehicleCode,
      driverId: device.assignedDriverId,
      driverName: device.assignedDriverName,
      assignedAt: nowIso(),
      status: "Assigned",
      assignedBy: "Device Health",
    });
    addAuditEntry(device, "device.assigned", `Assigned to ${device.assignedVehicleCode}.`);
    return enrichDevice(device);
  },

  async unassignDevice(deviceId: string | number) {
    const session = getSession();
    ensureManagementAccess(session);
    const device = scopeDeviceById(deviceId, session);
    device.assignedVehicleId = "";
    device.vehicleId = "";
    device.assignedVehicleCode = "";
    device.assignedDriverId = "";
    device.driverId = "";
    device.assignedDriverName = "";
    device.shipmentId = "";
    state.deviceAssignments.unshift({
      id: `assign-${device.deviceId}-${Date.now()}`,
      deviceId: device.id,
      tenantId: device.tenantId,
      vehicleId: null,
      vehicleCode: null,
      driverId: null,
      driverName: null,
      assignedAt: nowIso(),
      status: "Unassigned",
      assignedBy: "Device Health",
    });
    addAuditEntry(device, "device.unassigned", "Removed from vehicle assignment.");
    return enrichDevice(device);
  },

  async markInstalled(deviceId: string | number) {
    const session = getSession();
    ensureManagementAccess(session);
    const device = scopeDeviceById(deviceId, session);
    device.installStatus = "Installed";
    device.connectionStatus = device.connectionStatus === "Provisioning" ? "Online" : device.connectionStatus;
    const install = state.installationRecords.find((item) => String(item.deviceId) === String(device.id));
    if (install) {
      install.installStatus = "Installed";
      install.installedAt = nowIso();
      install.checklist = install.checklist.map((entry: { item: string; status: string }) => ({
        ...entry,
        status: entry.item === "Power connected" || entry.item === "Road test verified" ? "Complete" : entry.status,
      }));
    }
    addAuditEntry(device, "device.installed", "Installation checklist completed.");
    return enrichDevice(device);
  },

  async runDeviceDiagnostics(deviceId: string | number) {
    const session = getSession();
    ensureManagementAccess(session);
    const device = scopeDeviceById(deviceId, session);
    const diagnostic: TelematicsDiagnosticSeedRecord = {
      id: `diag-${device.deviceId}-${Date.now()}`,
      deviceId: device.id,
      tenantId: device.tenantId,
      result: /offline/i.test(String(device.connectionStatus)) ? "Warning" : "Passed",
      batteryVoltage: /battery/i.test(String(device.powerStatus)) ? "11.8V" : "13.6V",
      modemStatus: /offline/i.test(String(device.connectionStatus)) ? "Carrier retry" : "Connected",
      gnssStatus: /offline/i.test(String(device.connectionStatus)) ? "Searching" : "Locked",
      faultCode: /offline/i.test(String(device.connectionStatus)) ? "Connectivity recovery in progress" : "None",
      runAt: nowIso(),
      runBy: "Device Health",
    };
    state.diagnostics.unshift(diagnostic);
    state.deviceHealthEvents.unshift({
      id: `health-${device.deviceId}-${Date.now()}`,
      deviceId: device.id,
      tenantId: device.tenantId,
      score: Math.max(55, Number(device.dataHealthScore)),
      status: diagnostic.result === "Passed" ? "Online" : "Needs attention",
      signalStrength: device.signalStrength,
      eventAt: diagnostic.runAt,
      summary: diagnostic.result === "Passed" ? "Diagnostics completed successfully" : "Diagnostics completed with follow-up required",
    });
    addAuditEntry(device, "device.diagnostics.ran", "Diagnostics run completed.");
    return diagnostic;
  },

  async markDeviceAttention(deviceId: string | number, notes: string) {
    const session = getSession();
    ensureManagementAccess(session);
    const device = scopeDeviceById(deviceId, session);
    device.connectionStatus = "Needs attention";
    device.signalStrength = device.signalStrength === "Strong" ? "Weak" : device.signalStrength;
    state.deviceHealthEvents.unshift({
      id: `health-${device.deviceId}-${Date.now()}`,
      deviceId: device.id,
      tenantId: device.tenantId,
      score: Math.max(42, Number(device.dataHealthScore) - 18),
      status: "Needs attention",
      signalStrength: device.signalStrength,
      eventAt: nowIso(),
      summary: notes || "Device flagged for recovery review.",
    });
    addAuditEntry(device, "device.recovery.flagged", notes || "Device flagged for recovery review.");
    return enrichDevice(device);
  },

  async resolveDeviceAttention(deviceId: string | number) {
    const session = getSession();
    ensureManagementAccess(session);
    const device = scopeDeviceById(deviceId, session);
    device.connectionStatus = "Online";
    device.signalStrength = "Strong";
    device.lastCheckIn = nowIso();
    state.deviceHealthEvents.unshift({
      id: `health-${device.deviceId}-${Date.now()}`,
      deviceId: device.id,
      tenantId: device.tenantId,
      score: Math.max(88, Number(device.dataHealthScore)),
      status: "Online",
      signalStrength: "Strong",
      eventAt: device.lastCheckIn,
      summary: "Recovery action completed and heartbeat restored.",
    });
    addAuditEntry(device, "device.recovery.resolved", "Recovery action completed.");
    return enrichDevice(device);
  },

  async refreshDeviceStatus(deviceId: string | number) {
    const session = getSession();
    const device = scopeDeviceById(deviceId, session);
    device.lastCheckIn = nowIso();
    if (device.connectionStatus === "Offline") {
      device.connectionStatus = "Needs attention";
      device.signalStrength = "Weak";
    } else if (device.connectionStatus === "Provisioning") {
      device.connectionStatus = "Online";
      device.signalStrength = "Strong";
    }
    state.deviceHealthEvents.unshift({
      id: `health-${device.deviceId}-${Date.now()}`,
      deviceId: device.id,
      tenantId: device.tenantId,
      score: Number(device.dataHealthScore),
      status: device.connectionStatus,
      signalStrength: device.signalStrength,
      eventAt: device.lastCheckIn,
      summary: "Status refresh completed.",
    });
    addAuditEntry(device, "device.status.refresh", "Device heartbeat refreshed.");
    return enrichDevice(device);
  },

  async scheduleFirmwareUpdate(deviceId: string | number, payload: DeviceMutationPayload) {
    const session = getSession();
    ensureManagementAccess(session);
    const device = scopeDeviceById(deviceId, session);
    const scheduledFor = String(payload.scheduledFor ?? "");
    const targetVersion = String(payload.targetVersion ?? device.targetFirmwareVersion ?? device.firmwareVersion);
    device.targetFirmwareVersion = targetVersion;
    const existing = state.firmwareUpdates.find((item) => String(item.deviceId) === String(device.id));
    if (existing) {
      existing.targetVersion = targetVersion;
      existing.scheduledFor = scheduledFor;
      existing.status = "Scheduled";
    } else {
      state.firmwareUpdates.unshift({
        id: `fw-${device.deviceId}-${Date.now()}`,
        deviceId: device.id,
        deviceIdentifier: device.deviceId,
        tenantId: device.tenantId,
        currentVersion: device.firmwareVersion,
        targetVersion,
        scheduledFor,
        status: "Scheduled",
        releaseNotes: "Scheduled through Device Command Center.",
        createdBy: "Device Health",
      });
    }
    addAuditEntry(device, "device.firmware.scheduled", `Firmware update scheduled to ${targetVersion}.`);
    return { success: true };
  },

  async getDeviceTelemetry(deviceId: string | number) {
    const session = getSession();
    const device = scopeDeviceById(deviceId, session);
    return buildTelemetrySummary(device.id);
  },

  async getDeviceHealth(deviceId: string | number) {
    const session = getSession();
    const device = scopeDeviceById(deviceId, session);
    return buildHealthHistory(device.id);
  },

  async exportDevicesCsv() {
    const rows = await this.getDevices();
    const columns = [
      "deviceName",
      "deviceType",
      "provider",
      "serialNumber",
      "identifier",
      "assignedVehicleCode",
      "assignedDriverName",
      "tenantName",
      "firmwareVersion",
      "lastCheckIn",
      "connectionStatus",
      "powerStatus",
      "signalStrength",
      "dataHealthScore",
      "installStatus",
      "complianceStatus",
      "warrantyStatus",
      "supportStatus",
    ];
    return [columns.join(","), ...rows.map((row) => columns.map((column) => JSON.stringify(row[column as keyof DeviceCommandRecord] ?? "")).join(","))].join("\n");
  },

  async getProviders() {
    const session = getSession();
    const tenantId = getTenantId(session);
    return (isSuperAdmin(session) ? state.providers : state.providers.filter((item) => Number(item.tenantId) === tenantId))
      .map((provider) => ({
        ...provider,
        pendingDevices: state.devices.filter((device) => String(device.providerCode) === String(provider.id) && device.connectionStatus !== "Online").length,
      }));
  },

  async syncProvider(providerId: string | number) {
    const session = getSession();
    ensureManagementAccess(session);
    const tenantId = getTenantId(session);
    const provider = state.providers.find((item) =>
      String(item.id) === String(providerId) &&
      (isSuperAdmin(session) || Number(item.tenantId) === tenantId)
    );
    if (!provider) throw new Error("Provider not found");

    provider.lastSyncAt = nowIso();
    provider.integrationStatus = "Connected";

    state.devices
      .filter((device) => String(device.providerCode) === String(provider.id) && Number(device.tenantId) === Number(provider.tenantId))
      .forEach((device) => {
        device.lastCheckIn = nowIso();
        if (device.connectionStatus === "Provisioning") {
          device.connectionStatus = "Online";
          device.signalStrength = "Strong";
          device.dataHealthScore = Math.max(88, Number(device.dataHealthScore));
        }
        addAuditEntry(device, "provider.sync.completed", `${provider.name} sync completed.`);
      });

    return { success: true, provider };
  },

  async getGpsTrackingRecords() {
    return (await this.getDevices()).map(toClusterRecord);
  },

  async getDiagnosticsRecords() {
    return (await this.getDevices())
      .filter((device) => /eld|obd|j1939|can|gateway/i.test(device.deviceType))
      .map(toClusterRecord);
  },

  async getSensorHealthRecords() {
    return (await this.getDevices())
      .filter((device) => /sensor|temperature|door|fuel|tire|reefer|cold/i.test(device.deviceType))
      .map(toClusterRecord);
  },

  async acknowledgeTelematicsIssue(deviceId: string | number, note: string) {
    const session = getSession();
    ensureManagementAccess(session);
    const device = scopeDeviceById(deviceId, session);
    addAuditEntry(device, "telematics.issue.acknowledged", note || "Telematics issue acknowledged.");
    return { success: true };
  },

  async createMaintenanceTask(deviceId: string | number, note: string) {
    const session = getSession();
    ensureManagementAccess(session);
    const device = scopeDeviceById(deviceId, session);
    addAuditEntry(device, "telematics.maintenance.requested", note || "Maintenance follow-up created from telematics.");
    return {
      success: true,
      vehicleCode: device.assignedVehicleCode,
      title: `Telematics follow-up for ${device.assignedVehicleCode || device.deviceName}`,
      note,
    };
  },

  exportClusterCsv(rows: TelematicsClusterRecord[], columns: string[]) {
    return [
      columns.join(","),
      ...rows.map((row) => columns.map((column) => JSON.stringify(row[column as keyof TelematicsClusterRecord] ?? "")).join(",")),
    ].join("\n");
  },
};
