import { apiClient, unwrap } from "@/services/apiClient";
import { isCustomerPortalRole, isDriverPortalRole, resolveCustomerIdentity, resolveDriverIdentity } from "@/auth/accessScope";
import { readRawSession } from "@/auth/sessionStorage";
import type { AnyRecord, UserSession } from "@/types";

type DeviceMutationPayload = Record<string, unknown>;

// ── Exported record shapes (names/fields preserved — pages depend on them) ──────────
// These used to be inferred from seed fixtures. They are now defined explicitly so the
// telematics layer imports nothing from @/data/*. The field names match exactly what
// IotDevicesPage / TelematicsCommandPage read.

export type TelematicsTelemetrySeedRecord = {
  id: string;
  deviceId: string | number;
  vehicleId: string;
  driverId: string;
  latitude: string;
  longitude: string;
  speedMph: string | number;
  heading: string;
  engineStatus: string;
  odometer: string;
  fuelLevel: string;
  geofenceStatus: string;
  eventAt: string;
};

export type TelematicsHealthSeedRecord = {
  id: string;
  deviceId: string | number;
  tenantId: number;
  score: number | string;
  status: string;
  signalStrength: string;
  eventAt: string;
  summary: string;
};

export type TelematicsFirmwareSeedRecord = {
  id: string;
  deviceId: string | number;
  deviceIdentifier: string;
  tenantId: number;
  currentVersion: string;
  targetVersion: string;
  scheduledFor: string | null;
  status: string;
  releaseNotes: string;
  createdBy: string;
};

export type TelematicsDiagnosticSeedRecord = {
  id: string;
  deviceId: string | number;
  tenantId: number;
  result: string;
  batteryVoltage: string;
  modemStatus: string;
  gnssStatus: string;
  faultCode: string;
  runAt: string;
  runBy: string;
};

export type TelematicsInstallationSeedRecord = {
  id: string;
  deviceId: string | number;
  tenantId: number;
  installStatus: string;
  installerName: string;
  installedAt: string | null;
  checklist: Array<{ item: string; status: string }>;
};

export type TelematicsSensorSeedRecord = {
  id: string;
  deviceId: string | number;
  tenantId: number;
  temperature: string;
  humidity: string;
  doorStatus: string;
  tirePressure: string;
  fuelLevel: string;
  recordedAt: string;
};

export type TelematicsProviderSeedRecord = {
  id: string;
  name: string;
  category: string;
  integrationStatus: string;
  tenantId: number;
  deviceCount: number;
  lastSyncAt: string;
  supportTier: string;
};

export type DeviceCommandRecord = {
  id: string | number;
  deviceId: string;
  deviceName: string;
  deviceType: string;
  provider: string;
  providerCode: string;
  serialNumber: string;
  identifier: string;
  imei: string;
  simNumber: string;
  assignedVehicleId: string;
  vehicleId: string;
  assignedVehicleCode: string;
  assignedDriverId: string;
  driverId: string;
  assignedDriverName: string;
  shipmentId: string;
  tenantId: number;
  tenantName: string;
  firmwareVersion: string;
  targetFirmwareVersion: string;
  lastCheckIn: string;
  connectionStatus: string;
  powerStatus: string;
  signalStrength: string;
  dataHealthScore: number;
  installStatus: string;
  complianceStatus: string;
  warrantyStatus: string;
  supportStatus: string;
  lifecycleStatus: string;
  archivedAt: string | null;
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

// The one-time secrets a provisioned device uses to authenticate its live telemetry
// stream — the equivalent of a Render/Vercel deploy token. Shown once, never again.
export type DeviceConnectionCredentials = {
  deviceId: string;
  deviceSerial: string;
  apiKey: string;
  hmacSecret: string;
  note: string;
};

export type DeviceProvisionResult = {
  device: DeviceCommandRecord;
  credentials: DeviceConnectionCredentials;
  ingestUrl: string;
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

// ── Session / scoping helpers ───────────────────────────────────────────────────────

function getSession(): UserSession | null {
  if (typeof window === "undefined") return null;
  const raw = readRawSession();
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

// The backend already scopes /api/telemetry/devices by tenant via the auth token.
// Here we apply the remaining PORTAL narrowing (driver / customer) using real row
// fields only — never seed lookups. A driver portal sees only devices whose real
// driver_name matches their identity; a customer portal has no device-level linkage
// field in the contract, so it honestly sees nothing.
function scopeDevicesForSession(rows: DeviceCommandRecord[], session: UserSession | null) {
  if (!session) return rows;
  if (isSuperAdmin(session)) return rows;

  const role = String(session.role ?? "").toLowerCase();

  if (isDriverPortalRole(role)) {
    const driverIdentity = resolveDriverIdentity(session);
    if (!driverIdentity) return [];
    return rows.filter((row) =>
      String(row.assignedDriverName ?? "").toLowerCase().includes(String(driverIdentity).toLowerCase()),
    );
  }

  if (isCustomerPortalRole(role)) {
    // No device→customer linkage exists in the verified device contract; surfacing
    // any device to a customer portal would be a fabricated association.
    const customerIdentity = resolveCustomerIdentity(session);
    if (!customerIdentity) return [];
    return [];
  }

  return rows;
}

function relativeAge(timestamp: string | null | undefined) {
  if (!timestamp) return "—";
  const parsed = new Date(timestamp).getTime();
  if (!Number.isFinite(parsed)) return "—";
  const deltaMinutes = Math.max(1, Math.round((Date.now() - parsed) / 60000));
  if (deltaMinutes < 60) return `${deltaMinutes} min ago`;
  const hours = Math.floor(deltaMinutes / 60);
  const mins = deltaMinutes % 60;
  return `${hours}h ${mins}m ago`;
}

// ── Honest device-health derivation (no dedicated table) ─────────────────────────────
// Derived from the real signals only: eld_devices.status + seconds_since_ping (>900 =
// stale) + open telemetry_alerts + active fault_codes. Start at 100 and subtract for
// each real degradation signal; never invent a number where no signal exists.
type HealthSignals = {
  status: string;
  secondsSincePing: number | null;
  revoked: boolean;
  openAlerts: number;
  activeFaults: number;
};

function isStale(secondsSincePing: number | null) {
  return secondsSincePing != null && secondsSincePing > 900;
}

function deriveConnectionStatus(signals: HealthSignals): string {
  const status = String(signals.status ?? "").toLowerCase();
  if (signals.revoked || /revoked|suspend/i.test(status)) return "Offline";
  if (isStale(signals.secondsSincePing)) return "Offline";
  if (/malfunction|diagnostic/i.test(status) || signals.openAlerts > 0 || signals.activeFaults > 0) {
    return "Needs attention";
  }
  if (/active|online/i.test(status) || status === "") return "Online";
  // Fall back to the raw backend token (e.g. "Provisioning") rather than guessing.
  return String(signals.status ?? "Unknown");
}

function deriveHealthScore(signals: HealthSignals): number {
  let score = 100;
  if (isStale(signals.secondsSincePing)) score -= 40;
  if (signals.revoked || /revoked|suspend/i.test(String(signals.status ?? ""))) score -= 60;
  if (/malfunction|diagnostic/i.test(String(signals.status ?? ""))) score -= 25;
  score -= Math.min(30, signals.openAlerts * 10);
  score -= Math.min(30, signals.activeFaults * 8);
  return Math.max(0, Math.min(100, score));
}

// ── Backend row → DeviceCommandRecord mapping ────────────────────────────────────────
// Every field is sourced from the real /api/telemetry/devices row (snake_case) or an
// honest "—"/"Unknown" marker. No seed consultation, no fabricated telemetry defaults.
function mapDeviceRow(
  row: AnyRecord,
  faultCountBySerial: Map<string, number>,
  alertCountBySerial: Map<string, number>,
  session: UserSession | null,
): DeviceCommandRecord {
  const serial = String(row.device_serial ?? "");
  const secondsSincePing = row.seconds_since_ping == null ? null : Number(row.seconds_since_ping);
  const revoked = Boolean(row.revoked_at);
  const openAlerts = alertCountBySerial.get(serial) ?? 0;
  const activeFaults = faultCountBySerial.get(serial) ?? 0;

  const signals: HealthSignals = {
    status: String(row.status ?? ""),
    secondsSincePing,
    revoked,
    openAlerts,
    activeFaults,
  };
  const connectionStatus = deriveConnectionStatus(signals);
  const healthScore = deriveHealthScore(signals);

  const firmware = row.firmware_version == null ? "Unknown" : String(row.firmware_version);

  return {
    id: (typeof row.id === "string" || typeof row.id === "number") ? row.id : serial,
    deviceId: serial,
    deviceName: String(row.device_model ?? serial ?? "Telematics device"),
    deviceType: String(row.device_model ?? "ELD device"),
    provider: String(row.provider ?? "Unknown"),
    // No provider registry endpoint — derive a stable code from the real provider name.
    providerCode: String(row.provider ?? "").toLowerCase().replace(/[^a-z0-9]+/g, "-"),
    serialNumber: serial,
    // No IMEI in the device contract — show honest empty rather than a fake identifier.
    identifier: serial,
    imei: "",
    simNumber: "",
    assignedVehicleId: row.vehicle_id == null ? "" : String(row.vehicle_id),
    vehicleId: row.vehicle_id == null ? "" : String(row.vehicle_id),
    assignedVehicleCode: String(row.vehicle_code ?? ""),
    assignedDriverId: row.driver_id == null ? "" : String(row.driver_id),
    driverId: row.driver_id == null ? "" : String(row.driver_id),
    assignedDriverName: String(row.driver_name ?? ""),
    // No shipment linkage in the device contract.
    shipmentId: "",
    tenantId: getTenantId(session),
    tenantName: String(session?.company?.name ?? ""),
    firmwareVersion: firmware,
    // No OTA/target-firmware endpoint — equal to current so the "firmware pending" tab
    // never flags a fabricated pending update.
    targetFirmwareVersion: firmware,
    lastCheckIn: row.last_seen_at ? String(row.last_seen_at) : "—",
    connectionStatus,
    // No power/signal telemetry in the device contract — honest "—".
    powerStatus: "—",
    signalStrength: isStale(secondsSincePing) ? "No coverage" : "—",
    dataHealthScore: healthScore,
    // No installation table — the raw device status is the closest honest signal.
    installStatus: String(row.status ?? "Unknown"),
    complianceStatus: "Not assessed",
    warrantyStatus: "—",
    supportStatus: "—",
    lifecycleStatus: revoked ? "Archived" : String(row.status ?? "Unknown"),
    archivedAt: row.revoked_at ? String(row.revoked_at) : null,
    // Cross-links: only real fault/alert counts are honest here.
    linkedVehicleStatus: String(row.vehicle_status ?? "—"),
    linkedVehicleLocation: "—",
    linkedShipmentId: "",
    linkedShipmentStatus: "No active shipment",
    openAlertCount: openAlerts,
    maintenanceStatus: activeFaults > 0 ? `${activeFaults} active fault${activeFaults === 1 ? "" : "s"}` : "—",
    complianceSummary: "Not assessed",
  };
}

// Group active fault codes by device SERIAL (fault_codes.device_id is the serial string).
function countFaultsBySerial(faultRows: AnyRecord[]): Map<string, number> {
  const map = new Map<string, number>();
  for (const fault of faultRows) {
    const serial = String(fault.device_id ?? "");
    if (!serial) continue;
    map.set(serial, (map.get(serial) ?? 0) + 1);
  }
  return map;
}

// Group open alerts by device serial (telemetry_alerts expose device_serial).
function countAlertsBySerial(alertRows: AnyRecord[]): Map<string, number> {
  const map = new Map<string, number>();
  for (const alert of alertRows) {
    if (String(alert.status ?? "").toLowerCase() !== "open") continue;
    const serial = String(alert.device_serial ?? "");
    if (!serial) continue;
    map.set(serial, (map.get(serial) ?? 0) + 1);
  }
  return map;
}

// ── Protocol / sensor classification (from real device_model text) ───────────────────

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

// Match a live position snapshot to a device by real ids (device_id numeric, or the
// shared vehicle_id). Returns undefined when no live position exists for the device.
function positionForDevice(device: DeviceCommandRecord, positions: AnyRecord[]): AnyRecord | undefined {
  return positions.find((pos) => {
    if (device.id != null && pos.device_id != null && String(pos.device_id) === String(device.id)) return true;
    if (device.assignedVehicleId && pos.vehicle_id != null && String(pos.vehicle_id) === device.assignedVehicleId) return true;
    return false;
  });
}

// Build a cluster row from a real device + its real live position + real fault codes.
// Every value is either a live field or an honest "—"/empty marker — no fake defaults.
function toClusterRecord(device: DeviceCommandRecord, positions: AnyRecord[], faultRows: AnyRecord[]): TelematicsClusterRecord {
  const position = positionForDevice(device, positions);
  const deviceFaults = faultRows.filter((fault) => String(fault.device_id ?? "") === device.serialNumber);
  const troubleCodes = deviceFaults.map((fault) => String(fault.code ?? "")).filter(Boolean);
  const protocolType = deriveProtocolType(device);
  const sensorType = deriveSensorType(device);

  const isStalePosition = position ? String(position.is_stale) === "1" : false;
  const offlineWarning = /offline/i.test(device.connectionStatus) || isStalePosition;
  const lastPingAt = position?.event_time ? String(position.event_time) : device.lastCheckIn;
  const engineStatus = position?.engine_status ? String(position.engine_status) : "—";
  const dataFreshnessStatus = offlineWarning ? "Stale" : /attention|warning/i.test(device.connectionStatus) ? "Watch" : position ? "Fresh" : "No data";
  const sensorStatus = offlineWarning ? "Alerting" : /attention/i.test(device.connectionStatus) ? "Watch" : position ? "Nominal" : "No data";

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
    routeAssociation: "No active route",
    locationLabel: device.assignedVehicleCode || "—",
    latitude: position?.lat != null ? String(position.lat) : "—",
    longitude: position?.lng != null ? String(position.lng) : "—",
    speedMph: position?.speed_mph != null ? String(position.speed_mph) : "—",
    heading: position?.heading != null ? String(position.heading) : "—",
    geofenceStatus: position ? (isStalePosition ? "Last known" : "Live") : "No fix",
    lastPingAt,
    staleGps: relativeAge(lastPingAt),
    offlineWarning,
    deviceHealth: device.dataHealthScore,
    protocolType,
    engineHours: "—",
    odometer: position?.odometer_miles != null ? String(position.odometer_miles) : "—",
    fuelLevel: position?.fuel_level != null ? String(position.fuel_level) : "—",
    batteryVoltage: position?.battery_voltage != null ? String(position.battery_voltage) : "—",
    troubleCodes,
    engineStatus,
    emissionsStatus: troubleCodes.length > 0 ? "Inspection required" : "—",
    lastEngineDataAt: lastPingAt,
    dataFreshnessStatus,
    sensorType,
    // No standalone sensor-reading feed in the verified backend contract, so we
    // NEVER fabricate a reading or an expected-range setpoint. Both stay honest "—".
    latestReading: "—",
    expectedRange: "—",
    sensorStatus,
    powerStatus: device.powerStatus,
    signalStrength: device.signalStrength,
    calibrationStatus: "—",
    alertStatus: device.openAlertCount > 0 || offlineWarning || troubleCodes.length > 0 ? "Open" : "Clear",
    recommendedAction: offlineWarning
      ? "Use last known location and investigate the device link before the next trip."
      : troubleCodes.length > 0
        ? "Active fault codes present — review diagnostics before assignment."
        : /attention/i.test(device.connectionStatus)
          ? "Refresh the stream and validate data quality before assignment."
          : "Telemetry is healthy and ready for operational routing decisions.",
  };
}

// ── Shared reads ─────────────────────────────────────────────────────────────────────

async function fetchDeviceRows(): Promise<AnyRecord[]> {
  return unwrap<AnyRecord[]>(apiClient.get("/api/telemetry/devices"));
}

async function fetchActiveFaults(): Promise<AnyRecord[]> {
  return unwrap<AnyRecord[]>(apiClient.get("/api/maintenance/fault-codes", { params: { status: "active" } }));
}

async function fetchOpenAlerts(): Promise<AnyRecord[]> {
  return unwrap<AnyRecord[]>(apiClient.get("/api/telemetry/alerts", { params: { status: "Open" } }));
}

async function fetchPositions(): Promise<AnyRecord[]> {
  return unwrap<AnyRecord[]>(apiClient.get("/api/telemetry/positions"));
}

// Assemble scoped DeviceCommandRecord[] from the live device + fault + alert feeds.
async function loadScopedDevices(session: UserSession | null): Promise<DeviceCommandRecord[]> {
  const [rows, faults, alerts] = await Promise.all([fetchDeviceRows(), fetchActiveFaults(), fetchOpenAlerts()]);
  const faultCounts = countFaultsBySerial(faults);
  const alertCounts = countAlertsBySerial(alerts);
  const mapped = rows.map((row) => mapDeviceRow(row, faultCounts, alertCounts, session));
  return scopeDevicesForSession(mapped, session);
}

export const telematicsService = {
  async getDevices(): Promise<DeviceCommandRecord[]> {
    const session = getSession();
    return loadScopedDevices(session);
  },

  async getDeviceById(id: string | number): Promise<DeviceDetailRecord> {
    const session = getSession();
    // Real single-device read + the cross-feeds needed to populate the detail drawer.
    const [row, faults, alerts, positions] = await Promise.all([
      unwrap<AnyRecord>(apiClient.get(`/api/telemetry/devices/${id}`)),
      fetchActiveFaults(),
      unwrap<AnyRecord[]>(apiClient.get("/api/telemetry/alerts", { params: { status: "All" } })),
      fetchPositions(),
    ]);

    const faultCounts = countFaultsBySerial(faults);
    const openAlertCounts = countAlertsBySerial(alerts);
    const device = mapDeviceRow(row, faultCounts, openAlertCounts, session);

    // Enforce portal scoping on the single-device read too.
    const [scoped] = scopeDevicesForSession([device], session);
    if (!scoped) throw new Error("Device not found");

    const serial = scoped.serialNumber;
    const deviceFaults = faults.filter((fault) => String(fault.device_id ?? "") === serial);
    const deviceAlerts = alerts.filter((alert) => String(alert.device_serial ?? "") === serial);
    const position = positionForDevice(scoped, positions);

    // Telemetry: derived from the single live position snapshot (one point, or none).
    const telemetry: TelematicsTelemetrySeedRecord[] = position
      ? [{
          id: `position-${scoped.id}`,
          deviceId: scoped.id,
          vehicleId: scoped.assignedVehicleId,
          driverId: scoped.assignedDriverId,
          latitude: position.lat != null ? String(position.lat) : "—",
          longitude: position.lng != null ? String(position.lng) : "—",
          speedMph: position.speed_mph != null ? String(position.speed_mph) : "—",
          heading: position.heading != null ? String(position.heading) : "—",
          engineStatus: position.engine_status ? String(position.engine_status) : "—",
          odometer: position.odometer_miles != null ? String(position.odometer_miles) : "—",
          fuelLevel: position.fuel_level != null ? String(position.fuel_level) : "—",
          geofenceStatus: String(position.is_stale) === "1" ? "Last known" : "Live",
          eventAt: position.event_time ? String(position.event_time) : scoped.lastCheckIn,
        }]
      : [];

    // Diagnostics: real active fault codes for this device (by serial).
    const diagnostics: TelematicsDiagnosticSeedRecord[] = deviceFaults.map((fault) => ({
      id: String(fault.id),
      deviceId: scoped.id,
      tenantId: scoped.tenantId,
      result: String(fault.severity ?? "Info"),
      // These voltage/modem/gnss channels have no source in the fault-code contract.
      batteryVoltage: "—",
      modemStatus: "—",
      gnssStatus: "—",
      faultCode: `${String(fault.code_type ?? "")} ${String(fault.code ?? "")}`.trim(),
      runAt: String(fault.last_seen_at ?? fault.first_seen_at ?? ""),
      runBy: String(fault.description ?? ""),
    }));

    // Health timeline: real telemetry alerts for this device.
    const healthEvents: TelematicsHealthSeedRecord[] = deviceAlerts.map((alert) => ({
      id: String(alert.id),
      deviceId: scoped.id,
      tenantId: scoped.tenantId,
      score: scoped.dataHealthScore,
      status: String(alert.status ?? "Open"),
      signalStrength: scoped.signalStrength,
      eventAt: String(alert.created_at ?? ""),
      summary: `${String(alert.severity ?? "")} · ${String(alert.message ?? alert.alert_type ?? "")}`.trim(),
    }));

    return {
      device: scoped,
      telemetry,
      healthEvents,
      diagnostics,
      // No live source for these sub-lists in the verified contract — return [] rather
      // than fabricate. The UI already renders honest empty states for each.
      firmwareUpdates: [], // no OTA/firmware-schedule endpoint
      installations: [], // no installation-records endpoint
      sensorReadings: [], // no standalone sensor-reading endpoint
      providers: [], // no provider-registry endpoint
      auditLog: [], // no device audit-log endpoint
      assignmentHistory: [], // no assignment-history endpoint
    };
  },

  async getGpsTrackingRecords(): Promise<TelematicsClusterRecord[]> {
    const session = getSession();
    const [devices, positions, faults] = await Promise.all([
      loadScopedDevices(session),
      fetchPositions(),
      fetchActiveFaults(),
    ]);
    return devices.map((device) => toClusterRecord(device, positions, faults));
  },

  async getDiagnosticsRecords(): Promise<TelematicsClusterRecord[]> {
    const session = getSession();
    const [devices, positions, faults] = await Promise.all([
      loadScopedDevices(session),
      fetchPositions(),
      fetchActiveFaults(),
    ]);
    return devices
      .filter((device) => /eld|obd|j1939|can|gateway/i.test(device.deviceType))
      .map((device) => toClusterRecord(device, positions, faults));
  },

  async getSensorHealthRecords(): Promise<TelematicsClusterRecord[]> {
    const session = getSession();
    const [devices, positions, faults] = await Promise.all([
      loadScopedDevices(session),
      fetchPositions(),
      fetchActiveFaults(),
    ]);
    return devices
      .filter((device) => /sensor|temperature|door|fuel|tire|reefer|cold/i.test(device.deviceType))
      .map((device) => toClusterRecord(device, positions, faults));
  },

  // ── Mutations backed by real endpoints ────────────────────────────────────────────

  // Provision a device = INITIATE A REAL CONNECTION (the Render/Vercel model), not a
  // data save. The backend generates a real apiKey + HMAC secret that authenticate the
  // physical device's telemetry POSTs to /api/telemetry/ingest. Those credentials are
  // returned to the caller ONCE and never retrievable again — exactly like a platform
  // deploy token — so the connect dialog can display them and the device can start
  // streaming. Returns the credentials + the live device record + the ingest endpoint.
  async provisionDevice(payload: DeviceMutationPayload): Promise<DeviceProvisionResult> {
    const session = getSession();
    ensureManagementAccess(session);
    // IMEI is its own field for hardware GPS trackers (GT06/Concox/PT40-class). It falls
    // back to being the serial only when no separate serial was given, so device_serial
    // (NOT NULL) is always populated and the device resolves by either key at ingest.
    const imei = String(payload.imei ?? "").trim();
    const serial = String(payload.serialNumber ?? payload.identifier ?? imei ?? "").trim();
    if (!serial) throw new Error("A device serial or IMEI is required to establish a connection.");
    // POST /api/telemetry/devices/provision -> {id, deviceSerial, apiKey, hmacSecret, note}
    const provisioned = await unwrap<AnyRecord>(apiClient.post("/api/telemetry/devices/provision", {
      deviceSerial: serial,
      imei: imei || null,
      deviceModel: payload.deviceName ?? payload.deviceType ?? "Device",
      provider: payload.provider ?? "",
      vehicleId: payload.assignedVehicleId ?? payload.vehicleId ?? null,
      driverId: payload.assignedDriverId ?? payload.driverId ?? null,
      firmwareVersion: payload.firmwareVersion ?? "",
      notes: payload.notes ?? "",
    }));

    // Re-read the freshly provisioned device so the returned record is fully live.
    const created = await unwrap<AnyRecord>(apiClient.get(`/api/telemetry/devices/${provisioned.id}`));
    const device = mapDeviceRow(created, new Map(), new Map(), session);
    return {
      device,
      credentials: {
        deviceId: String(provisioned.id ?? device.id),
        deviceSerial: String(provisioned.deviceSerial ?? serial),
        apiKey: String(provisioned.apiKey ?? ""),
        hmacSecret: String(provisioned.hmacSecret ?? ""),
        note: String(provisioned.note ?? "Store these credentials securely — they will not be shown again."),
      },
      // The endpoint the device authenticates to and streams telemetry into. Built from
      // the same base the app talks to so it is correct in every environment (localhost
      // in dev, the Render URL in prod). Trailing slash on the base is normalized away.
      ingestUrl: `${String(apiClient.defaults.baseURL ?? "").replace(/\/+$/, "")}/api/telemetry/ingest`,
    };
  },

  // Backward-compatible wrapper: some callers only need the device record.
  async createDevice(payload: DeviceMutationPayload): Promise<DeviceCommandRecord> {
    return (await this.provisionDevice(payload)).device;
  },

  // Poll whether a freshly provisioned device has streamed its first heartbeat yet
  // (last_seen_at set / a live position exists). Drives the "Waiting for first
  // heartbeat…" → "Connected" pairing state in the connect dialog.
  async getDeviceConnectionState(deviceId: string | number): Promise<{ connected: boolean; lastSeenAt: string | null; status: string }> {
    const row = await unwrap<AnyRecord>(apiClient.get(`/api/telemetry/devices/${deviceId}`));
    const lastSeenAt = row.last_seen_at ? String(row.last_seen_at) : null;
    const status = String(row.status ?? "Unknown");
    // Connected once the device has checked in at least once and is not revoked/suspended.
    const connected = Boolean(lastSeenAt) && !/revoked|suspended/i.test(status);
    return { connected, lastSeenAt, status };
  },

  async assignDeviceToVehicle(deviceId: string | number, vehicleId: string | number): Promise<DeviceCommandRecord> {
    const session = getSession();
    ensureManagementAccess(session);
    // POST /api/telemetry/devices/{id}/assign {vehicleId, driverId}
    await unwrap<AnyRecord>(apiClient.post(`/api/telemetry/devices/${deviceId}/assign`, {
      vehicleId,
      driverId: null,
    }));
    const updated = await unwrap<AnyRecord>(apiClient.get(`/api/telemetry/devices/${deviceId}`));
    return mapDeviceRow(updated, new Map(), new Map(), session);
  },

  async markDeviceAttention(deviceId: string | number, _notes: string): Promise<DeviceCommandRecord> {
    const session = getSession();
    ensureManagementAccess(session);
    // POST /api/eld/devices/{id}/mark-malfunction (notes have no backend field here).
    await unwrap<AnyRecord>(apiClient.post(`/api/eld/devices/${deviceId}/mark-malfunction`, {}));
    const updated = await unwrap<AnyRecord>(apiClient.get(`/api/telemetry/devices/${deviceId}`));
    return mapDeviceRow(updated, new Map(), new Map(), session);
  },

  async resolveDeviceAttention(deviceId: string | number): Promise<DeviceCommandRecord> {
    const session = getSession();
    ensureManagementAccess(session);
    // POST /api/eld/devices/{id}/resolve-malfunction
    await unwrap<AnyRecord>(apiClient.post(`/api/eld/devices/${deviceId}/resolve-malfunction`, {}));
    const updated = await unwrap<AnyRecord>(apiClient.get(`/api/telemetry/devices/${deviceId}`));
    return mapDeviceRow(updated, new Map(), new Map(), session);
  },

  async archiveDevice(id: string | number) {
    const session = getSession();
    ensureManagementAccess(session);
    // POST /api/telemetry/devices/{id}/revoke (revocation is the real "archive").
    await unwrap<AnyRecord>(apiClient.post(`/api/telemetry/devices/${id}/revoke`, {}));
    return { success: true };
  },

  async updateDevice(id: string | number, payload: DeviceMutationPayload): Promise<DeviceCommandRecord> {
    const session = getSession();
    ensureManagementAccess(session);
    // No general device-update endpoint exists; the closest real mutation is re-assign
    // (vehicle/driver). Apply it when the payload changes assignment, then re-read.
    if (payload.assignedVehicleId != null || payload.vehicleId != null || payload.assignedDriverId != null) {
      await unwrap<AnyRecord>(apiClient.post(`/api/telemetry/devices/${id}/assign`, {
        vehicleId: payload.assignedVehicleId ?? payload.vehicleId ?? null,
        driverId: payload.assignedDriverId ?? payload.driverId ?? null,
      }));
    }
    // TODO: no endpoint persists deviceName/type/provider/firmware edits; those fields
    // are ignored rather than mutated into fake local state.
    const updated = await unwrap<AnyRecord>(apiClient.get(`/api/telemetry/devices/${id}`));
    return mapDeviceRow(updated, new Map(), new Map(), session);
  },

  // ── Mutations with NO backend endpoint — honest no-ops (no seed mutation) ───────────

  async unassignDevice(deviceId: string | number) {
    const session = getSession();
    ensureManagementAccess(session);
    // The assign endpoint requires a vehicleId; there is no verified "unassign" contract.
    // TODO: expose a real unassign endpoint (POST assign with null vehicle) once defined.
    void deviceId;
    return { success: false, reason: "not supported" as const };
  },

  async markInstalled(deviceId: string | number) {
    const session = getSession();
    ensureManagementAccess(session);
    // No installation-tracking endpoint exists.
    // TODO: wire to a real installation-status endpoint when available.
    void deviceId;
    return { success: false, reason: "not supported" as const };
  },

  async runDeviceDiagnostics(deviceId: string | number) {
    const session = getSession();
    ensureManagementAccess(session);
    // No on-demand diagnostics-run endpoint; fault codes are read via /maintenance/fault-codes.
    // TODO: wire to a real diagnostics-trigger endpoint when available.
    void deviceId;
    return { success: false, reason: "not supported" as const };
  },

  async refreshDeviceStatus(deviceId: string | number) {
    const session = getSession();
    // No status-refresh endpoint; callers should re-query getDevices/getDeviceById.
    // TODO: wire to a real refresh/ping endpoint when available.
    void deviceId;
    void session;
    return { success: false, reason: "not supported" as const };
  },

  async scheduleFirmwareUpdate(deviceId: string | number, _payload: DeviceMutationPayload) {
    const session = getSession();
    ensureManagementAccess(session);
    // No OTA/firmware-schedule endpoint exists.
    // TODO: wire to a real firmware-schedule endpoint when available.
    void deviceId;
    return { success: false, reason: "not supported" as const };
  },

  async acknowledgeTelematicsIssue(deviceId: string | number, _note: string) {
    const session = getSession();
    ensureManagementAccess(session);
    // Alerts are acknowledged per-alert (POST /api/telemetry/alerts/{id}/acknowledge),
    // not per-device. There is no device-level acknowledge, so this is a no-op.
    // TODO: acknowledge the specific alert id via the alerts endpoint from the caller.
    void deviceId;
    return { success: false, reason: "not supported" as const };
  },

  async createMaintenanceTask(deviceId: string | number, note: string) {
    const session = getSession();
    ensureManagementAccess(session);
    // The telematics layer has no maintenance-task endpoint of its own; the CALLER
    // persists the task via maintenanceApi.create. Here we resolve the real device so
    // the returned title/note reference the actual unit (no fabricated data). The task
    // itself is created against the real maintenance API downstream.
    const device = await unwrap<AnyRecord>(apiClient.get(`/api/telemetry/devices/${deviceId}`));
    const label = String(device.vehicle_code ?? device.device_serial ?? deviceId);
    return {
      success: true as const,
      vehicleCode: String(device.vehicle_code ?? ""),
      title: `Telematics follow-up for ${label}`,
      note,
    };
  },

  async syncProvider(providerId: string | number) {
    const session = getSession();
    ensureManagementAccess(session);
    // No provider-registry or provider-sync endpoint exists.
    // TODO: wire to a real provider-sync endpoint when available.
    void providerId;
    return { success: false, reason: "not supported" as const };
  },

  async getProviders(): Promise<TelematicsProviderSeedRecord[]> {
    // No provider-registry endpoint in the verified contract — honest empty list.
    // TODO: wire to a real providers endpoint when available.
    return [];
  },

  async getDeviceTelemetry(deviceId: string | number) {
    // Derives the single live position point from the device detail read.
    const detail = await this.getDeviceById(deviceId);
    return detail.telemetry;
  },

  async getDeviceHealth(deviceId: string | number) {
    // Derives the health timeline (real alerts) from the device detail read.
    const detail = await this.getDeviceById(deviceId);
    return detail.healthEvents;
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

  exportClusterCsv(rows: TelematicsClusterRecord[], columns: string[]) {
    return [
      columns.join(","),
      ...rows.map((row) => columns.map((column) => JSON.stringify(row[column as keyof TelematicsClusterRecord] ?? "")).join(",")),
    ].join("\n");
  },
};
