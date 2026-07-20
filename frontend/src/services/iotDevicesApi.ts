import { telematicsService, type DeviceCommandRecord, type TelematicsClusterRecord } from "@/services/telematicsService";
import type { AnyRecord } from "@/types";

type DeviceMutationPayload = Record<string, unknown>;

// Placeholder token used across the telematics layer whenever a device has no live
// value for a field. We render this verbatim rather than inventing a plausible number.
const NO_DATA = "—";

export type IotDeviceRecord = DeviceCommandRecord & {
  model: string;
  linkedVehicleCode: string;
  driverName: string;
  activeShipmentId: string;
  status: string;
  signal: string;
  battery: string;
  lastHeartbeat: string;
  heartbeatAge: string;
  dataQuality: string;
  healthScore: number;
  approvalStatus: string;
  alertCount: number;
  recommendedAction: string;
};

export type TelematicsSignalRecord = AnyRecord & {
  id: string;
  moduleKey: "gps-tracking" | "obd-j1939" | "sensor-health" | "cold-chain";
  deviceId: string;
  vehicleCode: string;
  driverName: string;
  shipmentId: string;
  status: string;
  severity: string;
  signal: string;
  lastHeartbeat: string;
  heartbeatAge: string;
  latitude: string;
  longitude: string;
  speedMph: string;
  heading: string;
  geofenceStatus: string;
  engineStatus: string;
  odometer: string;
  fuelLevel: string;
  batteryVoltage: string;
  coolantTemp: string;
  faultCode: string;
  temperature: string;
  humidity: string;
  setPoint: string;
  doorStatus: string;
  dataQuality: string;
  healthScore: number;
  recommendedAction: string;
};

function relativeHeartbeat(timestamp: string) {
  const parsed = new Date(timestamp).getTime();
  if (!timestamp || Number.isNaN(parsed)) return NO_DATA;
  const deltaMinutes = Math.max(1, Math.round((Date.now() - parsed) / 60000));
  if (deltaMinutes < 60) return `${deltaMinutes} min ago`;
  const hours = Math.floor(deltaMinutes / 60);
  const mins = deltaMinutes % 60;
  return `${hours}h ${mins}m ago`;
}

function signalSeverity(signal: string) {
  if (/strong/i.test(signal)) return "Strong";
  if (/weak/i.test(signal)) return "Weak";
  return "Offline";
}

// Any live field the backend could not supply arrives as an empty string or one of
// the honest "unknown/pending" markers the service uses. Collapse those to a single
// NO_DATA token so the UI never shows a stale placeholder as if it were a reading.
function orNoData(value: unknown): string {
  const text = value == null ? "" : String(value).trim();
  if (!text) return NO_DATA;
  if (/^(—|-|n\/?a|unknown|pending|not assessed|no data)$/i.test(text)) return NO_DATA;
  return text;
}

function recommendedAction(row: DeviceCommandRecord) {
  if (/offline/i.test(row.connectionStatus)) return "Dispatch a backup visibility path and inspect power, GNSS, and carrier continuity.";
  if (/attention/i.test(row.connectionStatus)) return "Run diagnostics, validate antenna placement, and confirm the next heartbeat before dispatch.";
  if (/provision/i.test(row.connectionStatus) || /awaiting/i.test(row.installStatus)) return "Finish installation, pair the provider feed, and verify assignment coverage.";
  return "Device is healthy. Keep firmware current and monitor assignment coverage.";
}

function toRecord(row: DeviceCommandRecord): IotDeviceRecord {
  // Every field here is projected from the (now live) DeviceCommandRecord. Where the
  // source is empty we surface the device's own honest marker, never a fabricated one.
  return {
    ...row,
    model: row.deviceName,
    linkedVehicleCode: row.assignedVehicleCode || "Unassigned",
    driverName: row.assignedDriverName || "Unassigned",
    activeShipmentId: row.linkedShipmentId || "No active shipment",
    status: row.connectionStatus,
    signal: signalSeverity(row.signalStrength),
    battery: orNoData(row.powerStatus),
    lastHeartbeat: row.lastCheckIn,
    heartbeatAge: relativeHeartbeat(row.lastCheckIn),
    dataQuality: Number.isFinite(row.dataHealthScore) && row.dataHealthScore > 0 ? `${row.dataHealthScore}%` : NO_DATA,
    healthScore: row.dataHealthScore,
    approvalStatus: row.complianceStatus,
    alertCount: row.openAlertCount,
    recommendedAction: recommendedAction(row),
  };
}

// Map a device's live state to the signal severity the module cards color by.
function clusterSeverity(cluster: TelematicsClusterRecord) {
  if (cluster.offlineWarning) return "Critical";
  if (/watch|stale/i.test(cluster.dataFreshnessStatus) || cluster.alertStatus === "Open") return "High";
  return "Low";
}

// Build the shared, live-sourced payload for one device's signal record. Every value
// comes off the cluster record — which the telematics service derives from the real
// /telemetry/positions + /maintenance/fault-codes feeds — or is an honest NO_DATA.
function signalFromCluster(cluster: TelematicsClusterRecord) {
  const faultCode = cluster.troubleCodes.length > 0 ? cluster.troubleCodes.join(", ") : "None";
  return {
    deviceId: String(cluster.deviceId),
    vehicleCode: cluster.vehicleCode || "Unassigned",
    driverName: cluster.driverName || "Unassigned",
    shipmentId: cluster.shipmentId || "No active shipment",
    status: cluster.dataFreshnessStatus === "Stale" ? "Offline" : cluster.dataFreshnessStatus === "Watch" ? "Needs attention" : "Online",
    severity: clusterSeverity(cluster),
    signal: signalSeverity(cluster.signalStrength),
    lastHeartbeat: cluster.lastPingAt,
    heartbeatAge: relativeHeartbeat(cluster.lastPingAt),
    latitude: orNoData(cluster.latitude),
    longitude: orNoData(cluster.longitude),
    speedMph: orNoData(cluster.speedMph),
    heading: orNoData(cluster.heading),
    geofenceStatus: orNoData(cluster.geofenceStatus),
    engineStatus: orNoData(cluster.engineStatus),
    odometer: orNoData(cluster.odometer),
    fuelLevel: orNoData(cluster.fuelLevel),
    batteryVoltage: orNoData(cluster.batteryVoltage),
    coolantTemp: NO_DATA,
    faultCode,
    temperature: orNoData(cluster.latestReading),
    humidity: NO_DATA,
    setPoint: orNoData(cluster.expectedRange),
    doorStatus: NO_DATA,
    dataQuality: Number.isFinite(cluster.deviceHealth) && cluster.deviceHealth > 0 ? `${cluster.deviceHealth}%` : NO_DATA,
    healthScore: Number(cluster.deviceHealth) || 0,
    recommendedAction: cluster.recommendedAction,
  };
}

export const iotDevicesApi = {
  list: async (): Promise<IotDeviceRecord[]> => (await telematicsService.getDevices()).map(toRecord),

  detail: async (id: string | number): Promise<IotDeviceRecord> => toRecord((await telematicsService.getDeviceById(id)).device),

  register: async (payload: DeviceMutationPayload): Promise<IotDeviceRecord> => toRecord(await telematicsService.createDevice(payload)),

  markMalfunction: async (id: number, body: DeviceMutationPayload) => {
    const updated = await telematicsService.markDeviceAttention(id, String(body.desc ?? body.code ?? "Recovery review opened."));
    return { id, status: updated.connectionStatus, success: true };
  },

  resolveMalfunction: async (id: number) => {
    const updated = await telematicsService.resolveDeviceAttention(id);
    return { id, status: updated.connectionStatus, success: true };
  },

  // One signal record per device per module, each derived from the live cluster feed
  // for that module. No index math, no fabricated coordinates, no hardcoded fault /
  // temperature / fuel / voltage. A device that a module's live query does not cover
  // simply produces no row for that module.
  telematicsSignals: async (): Promise<TelematicsSignalRecord[]> => {
    const [gps, diagnostics, sensors] = await Promise.all([
      telematicsService.getGpsTrackingRecords(),
      telematicsService.getDiagnosticsRecords(),
      telematicsService.getSensorHealthRecords(),
    ]);

    const signals: TelematicsSignalRecord[] = [];

    // GPS tracking — real lat/lng/speed/heading from latest_vehicle_positions.
    for (const cluster of gps) {
      signals.push({
        id: `gps-${cluster.deviceId}`,
        moduleKey: "gps-tracking",
        ...signalFromCluster(cluster),
      });
    }

    // OBD / J1939 diagnostics — real fault codes, engine + fuel + battery telemetry.
    for (const cluster of diagnostics) {
      signals.push({
        id: `obd-${cluster.deviceId}`,
        moduleKey: "obd-j1939",
        ...signalFromCluster(cluster),
      });
    }

    // Sensor health + cold-chain — real sensor readings / expected ranges. Cold-chain
    // records are the subset of sensor devices reporting a temperature/reefer sensor.
    for (const cluster of sensors) {
      const base = signalFromCluster(cluster);
      const isColdChain = /temperature|reefer|cold/i.test(cluster.sensorType);
      signals.push({
        id: `sensor-${cluster.deviceId}`,
        moduleKey: "sensor-health",
        ...base,
      });
      if (isColdChain) {
        signals.push({
          id: `cold-${cluster.deviceId}`,
          moduleKey: "cold-chain",
          ...base,
        });
      }
    }

    return signals;
  },
};
