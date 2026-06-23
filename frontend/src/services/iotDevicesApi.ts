import { telematicsService, type DeviceCommandRecord } from "@/services/telematicsService";
import type { AnyRecord } from "@/types";

type DeviceMutationPayload = Record<string, unknown>;

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
  const deltaMinutes = Math.max(1, Math.round((Date.now() - new Date(timestamp).getTime()) / 60000));
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

function recommendedAction(row: DeviceCommandRecord) {
  if (/offline/i.test(row.connectionStatus)) return "Dispatch a backup visibility path and inspect power, GNSS, and carrier continuity.";
  if (/attention/i.test(row.connectionStatus)) return "Run diagnostics, validate antenna placement, and confirm the next heartbeat before dispatch.";
  if (/provision/i.test(row.connectionStatus) || /awaiting/i.test(row.installStatus)) return "Finish installation, pair the provider feed, and verify assignment coverage.";
  return "Device is healthy. Keep firmware current and monitor assignment coverage.";
}

function toRecord(row: DeviceCommandRecord): IotDeviceRecord {
  return {
    ...row,
    model: row.deviceName,
    linkedVehicleCode: row.assignedVehicleCode || "Unassigned",
    driverName: row.assignedDriverName || "Unassigned",
    activeShipmentId: row.linkedShipmentId || "No active shipment",
    status: row.connectionStatus,
    signal: signalSeverity(row.signalStrength),
    battery: row.powerStatus,
    lastHeartbeat: row.lastCheckIn,
    heartbeatAge: relativeHeartbeat(row.lastCheckIn),
    dataQuality: `${row.dataHealthScore}%`,
    healthScore: row.dataHealthScore,
    approvalStatus: row.complianceStatus,
    alertCount: row.openAlertCount,
    recommendedAction: recommendedAction(row),
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

  telematicsSignals: async (): Promise<TelematicsSignalRecord[]> => {
    const devices = await telematicsService.getDevices();
    return devices.flatMap((device, index) => {
      const baseLat = 24.7136 + index * 0.085;
      const baseLng = 46.6753 + index * 0.11;
      const geofenceStatus = /offline/i.test(device.connectionStatus) ? "Last known" : /attention/i.test(device.connectionStatus) ? "Borderline" : "In corridor";
      const severity = /offline/i.test(device.connectionStatus) ? "Critical" : /attention|provision/i.test(device.connectionStatus) ? "High" : "Low";
      const engineStatus = device.linkedShipmentId ? "Running" : "Idle";
      const isColdChain = /temperature/i.test(device.deviceType);

      const common = {
        deviceId: device.deviceId,
        vehicleCode: device.assignedVehicleCode || "Unassigned",
        driverName: device.assignedDriverName || "Unassigned",
        shipmentId: device.linkedShipmentId || "No active shipment",
        status: device.connectionStatus,
        severity,
        signal: signalSeverity(device.signalStrength),
        lastHeartbeat: device.lastCheckIn,
        heartbeatAge: relativeHeartbeat(device.lastCheckIn),
        latitude: baseLat.toFixed(5),
        longitude: baseLng.toFixed(5),
        speedMph: device.linkedShipmentId ? (/attention/i.test(device.connectionStatus) ? "41" : "57") : "0",
        heading: device.linkedShipmentId ? "NE" : "Stationary",
        geofenceStatus,
        engineStatus,
        odometer: /box/i.test(device.assignedVehicleCode) ? "268,400 mi" : "164,920 km",
        fuelLevel: /fuel/i.test(device.deviceType) ? "68%" : "Vehicle bus",
        batteryVoltage: /offline/i.test(device.connectionStatus) ? "10.8V" : "13.6V",
        coolantTemp: /obd|j1939|eld/i.test(device.deviceType) ? "192 F" : "N/A",
        faultCode: /offline/i.test(device.connectionStatus) ? "SPN 639 FMI 2" : /attention/i.test(device.connectionStatus) ? "Signal degradation watch" : "None",
        temperature: isColdChain ? "4.3 C" : "Ambient",
        humidity: isColdChain ? "63%" : "N/A",
        setPoint: isColdChain ? "4.0 C" : "N/A",
        doorStatus: /door/i.test(device.deviceType) ? "Closed" : "N/A",
        dataQuality: `${device.dataHealthScore}%`,
        healthScore: device.dataHealthScore,
        recommendedAction: recommendedAction(device),
      };

      return [
        { id: `gps-${device.id}`, moduleKey: "gps-tracking" as const, ...common },
        { id: `obd-${device.id}`, moduleKey: "obd-j1939" as const, ...common },
        { id: `sensor-${device.id}`, moduleKey: "sensor-health" as const, ...common },
        { id: `cold-${device.id}`, moduleKey: "cold-chain" as const, ...common },
      ];
    });
  },
};
