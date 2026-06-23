import { developmentFleetSeedData } from "@/data/developmentFleetSeedData";
import type { AnyRecord } from "@/types";

const primaryTenant = { id: 1, name: "Northshore Fleet Logistics" };
const secondaryTenant = { id: 3, name: "Client Tenant" };

const vehicleLookup = new Map(
  developmentFleetSeedData.vehicles.map((vehicle) => [String(vehicle.vehicleId ?? vehicle.vehicleCode), vehicle]),
);
const driverLookup = new Map(
  developmentFleetSeedData.drivers.map((driver) => [String(driver.driverId ?? driver.driverCode), driver]),
);
const shipmentLookup = new Map(
  developmentFleetSeedData.shipments.map((shipment) => [String(shipment.shipmentId), shipment]),
);

const baseDevices = developmentFleetSeedData.devices.map((device, index) => {
  const vehicle = vehicleLookup.get(String(device.linkedVehicleTrailer));
  const driver = developmentFleetSeedData.drivers.find((item) => String(item.assignedVehicle) === String(device.linkedVehicleTrailer));
  const shipment = developmentFleetSeedData.shipments.find((item) => String(item.vehicle) === String(device.linkedVehicleTrailer));
  const tenant = shipment?.customer === "DesertCart Fulfillment" ? secondaryTenant : primaryTenant;
  const deviceType = normalizeDeviceType(String(device.type));
  const installStatus = /Camera Not Recording/i.test(String(device.status)) ? "Needs reinstall" : /Weak/i.test(String(device.status)) ? "Installed with warning" : "Installed";
  const complianceStatus = /Temperature Sensor|ELD Gateway|GPS Tracker/i.test(deviceType) ? "Approved" : "Review";
  return {
    id: index + 1,
    deviceId: String(device.deviceId),
    deviceName: `${String(device.vendor)} ${String(device.model)}`,
    deviceType,
    provider: String(device.vendor),
    providerCode: providerCodeFor(String(device.vendor)),
    serialNumber: String(device.deviceId),
    identifier: String(device.imei),
    imei: String(device.imei),
    simNumber: String(device.simIccid),
    assignedVehicleId: String(vehicle?.vehicleId ?? ""),
    vehicleId: String(vehicle?.vehicleId ?? ""),
    assignedVehicleCode: String(vehicle?.vehicleId ?? ""),
    assignedDriverId: String(driver?.driverId ?? ""),
    driverId: String(driver?.driverId ?? ""),
    assignedDriverName: String(driver?.fullName ?? driver?.name ?? ""),
    shipmentId: String(shipment?.shipmentId ?? ""),
    tenantId: tenant.id,
    tenantName: tenant.name,
    firmwareVersion: String(device.firmware),
    targetFirmwareVersion: bumpFirmware(String(device.firmware)),
    lastCheckIn: isoFromDisplay(String(device.lastHeartbeat), index),
    connectionStatus: normalizeConnectionStatus(String(device.status)),
    powerStatus: String(device.battery),
    signalStrength: normalizeSignal(String(device.signal)),
    dataHealthScore: healthScoreFor(String(device.status), String(device.dataQuality)),
    installStatus,
    complianceStatus,
    warrantyStatus: index === 2 ? "Warranty expiring" : "Covered",
    supportStatus: normalizeConnectionStatus(String(device.status)) === "Offline" ? "Escalated" : "In SLA",
    lifecycleStatus: "Active",
    archivedAt: null,
  };
});

const extraDevices = [
  {
    id: 101,
    deviceId: "ELD-US-104",
    deviceName: "Motive ELD FMCSA Gateway",
    deviceType: "ELD Device",
    provider: "Motive",
    providerCode: "motive",
    serialNumber: "ELD-US-104",
    identifier: "357991040000104",
    imei: "357991040000104",
    simNumber: "89011703211104010401",
    assignedVehicleId: "BOX-106",
    vehicleId: "BOX-106",
    assignedVehicleCode: "BOX-106",
    assignedDriverId: "DRV-US-017",
    driverId: "DRV-US-017",
    assignedDriverName: "Ana Rivera",
    shipmentId: "SHP-6204",
    tenantId: primaryTenant.id,
    tenantName: primaryTenant.name,
    firmwareVersion: "6.2.1",
    targetFirmwareVersion: "6.3.0",
    lastCheckIn: new Date("2026-06-08T11:58:00Z").toISOString(),
    connectionStatus: "Offline",
    powerStatus: "Vehicle power",
    signalStrength: "No coverage",
    dataHealthScore: 41,
    installStatus: "Installed",
    complianceStatus: "Review",
    warrantyStatus: "Covered",
    supportStatus: "Escalated",
    lifecycleStatus: "Active",
    archivedAt: null,
  },
  {
    id: 102,
    deviceId: "TPMS-KSA-214",
    deviceName: "Continental Tire Pressure Node",
    deviceType: "Tire Pressure Sensor",
    provider: "Continental",
    providerCode: "continental",
    serialNumber: "TPMS-KSA-214",
    identifier: "TPMS21499831",
    imei: "TPMS21499831",
    simNumber: "Embedded",
    assignedVehicleId: "KSA-REEFER-214",
    vehicleId: "KSA-REEFER-214",
    assignedVehicleCode: "KSA-REEFER-214",
    assignedDriverId: "DRV-KSA-301",
    driverId: "DRV-KSA-301",
    assignedDriverName: "Salman Qureshi",
    shipmentId: "SHP-6201",
    tenantId: primaryTenant.id,
    tenantName: primaryTenant.name,
    firmwareVersion: "3.4.0",
    targetFirmwareVersion: "3.4.0",
    lastCheckIn: new Date("2026-06-08T12:06:00Z").toISOString(),
    connectionStatus: "Online",
    powerStatus: "Battery 78%",
    signalStrength: "Strong",
    dataHealthScore: 93,
    installStatus: "Installed",
    complianceStatus: "Approved",
    warrantyStatus: "Covered",
    supportStatus: "In SLA",
    lifecycleStatus: "Active",
    archivedAt: null,
  },
  {
    id: 103,
    deviceId: "ASSET-AE-900",
    deviceName: "Teltonika Asset Tracker",
    deviceType: "Asset Tracker",
    provider: "Teltonika",
    providerCode: "teltonika",
    serialNumber: "ASSET-AE-900",
    identifier: "359881220000900",
    imei: "359881220000900",
    simNumber: "899712220000900001",
    assignedVehicleId: "",
    vehicleId: "",
    assignedVehicleCode: "",
    assignedDriverId: "",
    driverId: "",
    assignedDriverName: "",
    shipmentId: "",
    tenantId: secondaryTenant.id,
    tenantName: secondaryTenant.name,
    firmwareVersion: "1.8.3",
    targetFirmwareVersion: "1.9.0",
    lastCheckIn: new Date("2026-06-08T10:12:00Z").toISOString(),
    connectionStatus: "Provisioning",
    powerStatus: "Battery 100%",
    signalStrength: "Ready to activate",
    dataHealthScore: 88,
    installStatus: "Awaiting installation",
    complianceStatus: "Pending",
    warrantyStatus: "Covered",
    supportStatus: "In SLA",
    lifecycleStatus: "Active",
    archivedAt: null,
  },
];

export const telematicsSeedData = {
  providers: [
    { id: "queclink", name: "Queclink", category: "GPS / gateway", integrationStatus: "Connected", tenantId: 1, deviceCount: 1, lastSyncAt: "2026-06-08T12:04:00Z", supportTier: "Enterprise" },
    { id: "sensitech", name: "Sensitech", category: "Cold chain", integrationStatus: "Connected", tenantId: 1, deviceCount: 1, lastSyncAt: "2026-06-08T11:58:00Z", supportTier: "Enterprise" },
    { id: "motive", name: "Motive", category: "ELD / dashcam", integrationStatus: "Attention required", tenantId: 1, deviceCount: 1, lastSyncAt: "2026-06-08T11:49:00Z", supportTier: "Priority" },
    { id: "continental", name: "Continental", category: "TPMS", integrationStatus: "Connected", tenantId: 1, deviceCount: 1, lastSyncAt: "2026-06-08T12:06:00Z", supportTier: "Standard" },
    { id: "teltonika", name: "Teltonika", category: "Asset tracking", integrationStatus: "Provisioning", tenantId: 3, deviceCount: 1, lastSyncAt: "2026-06-08T10:12:00Z", supportTier: "Standard" },
  ],
  devices: [...baseDevices, ...extraDevices],
  deviceAssignments: buildAssignments([...baseDevices, ...extraDevices]),
  telemetryEvents: buildTelemetry([...baseDevices, ...extraDevices]),
  deviceHealthEvents: buildHealthEvents([...baseDevices, ...extraDevices]),
  firmwareUpdates: buildFirmwareUpdates([...baseDevices, ...extraDevices]),
  diagnostics: buildDiagnostics([...baseDevices, ...extraDevices]),
  installationRecords: buildInstallations([...baseDevices, ...extraDevices]),
  sensorReadings: buildSensorReadings([...baseDevices, ...extraDevices]),
  auditLogs: buildAuditLogs([...baseDevices, ...extraDevices]),
};

function normalizeDeviceType(type: string) {
  if (/gps/i.test(type)) return "GPS Tracker";
  if (/temperature/i.test(type)) return "Temperature Sensor";
  if (/dashcam|camera/i.test(type)) return "Dashcam Device";
  return type;
}

function providerCodeFor(provider: string) {
  return provider.toLowerCase().replace(/[^a-z0-9]+/g, "-");
}

function normalizeConnectionStatus(status: string) {
  if (/Camera Not Recording|No Upload/i.test(status)) return "Offline";
  if (/Weak/i.test(status)) return "Needs attention";
  return "Online";
}

function normalizeSignal(signal: string) {
  if (/No Upload/i.test(signal)) return "No coverage";
  if (/Weak/i.test(signal)) return "Weak";
  return signal;
}

function healthScoreFor(status: string, quality: string) {
  const value = Number.parseInt(String(quality).replace("%", ""), 10) || 0;
  if (/Camera Not Recording|No Upload/i.test(status)) return Math.max(34, value - 20);
  if (/Weak/i.test(status)) return Math.max(62, value - 8);
  return Math.max(88, value);
}

function bumpFirmware(version: string) {
  const parts = version.split(".");
  const last = Number(parts.at(-1) ?? "0");
  parts[parts.length - 1] = String(last + 1);
  return parts.join(".");
}

function isoFromDisplay(raw: string, offset: number) {
  const base = new Date("2026-06-08T12:10:00Z");
  base.setMinutes(base.getMinutes() - offset * 11);
  return base.toISOString();
}

function buildAssignments(devices: AnyRecord[]) {
  return devices.flatMap((device) => {
    const records = [];
    if (device.assignedVehicleId) {
      records.push({
        id: `assign-${device.deviceId}-current`,
        deviceId: device.id,
        vehicleId: device.assignedVehicleId,
        vehicleCode: device.assignedVehicleCode,
        driverId: device.assignedDriverId,
        driverName: device.assignedDriverName,
        assignedAt: "2026-05-20T08:00:00Z",
        assignedBy: "Fleet Ops",
        status: "Current",
      });
      records.push({
        id: `assign-${device.deviceId}-prior`,
        deviceId: device.id,
        vehicleId: device.assignedVehicleId,
        vehicleCode: device.assignedVehicleCode,
        driverId: device.assignedDriverId,
        driverName: device.assignedDriverName,
        assignedAt: "2026-03-11T09:20:00Z",
        unassignedAt: "2026-04-02T15:00:00Z",
        assignedBy: "Telematics Lead",
        status: "Historical",
      });
    }
    return records;
  });
}

function buildTelemetry(devices: AnyRecord[]) {
  return devices.flatMap((device, index) => {
    const vehicle = vehicleLookup.get(String(device.assignedVehicleCode));
    const baseLat = 24.7136 + index * 0.084;
    const baseLng = 46.6753 + index * 0.11;
    return [
      {
        id: `telemetry-${device.deviceId}-1`,
        deviceId: device.id,
        vehicleId: device.assignedVehicleId,
        driverId: device.assignedDriverId,
        latitude: baseLat.toFixed(5),
        longitude: baseLng.toFixed(5),
        speedMph: device.connectionStatus === "Offline" ? 0 : device.connectionStatus === "Needs attention" ? 43 : 58,
        heading: device.connectionStatus === "Offline" ? "Stationary" : "NE",
        engineStatus: device.connectionStatus === "Offline" ? "Visibility lost" : vehicle?.status === "Idle" ? "Idle" : "Running",
        odometer: vehicle?.odometer ?? "0",
        fuelLevel: String(device.assignedVehicleCode).includes("BOX") ? "29%" : "68%",
        coolantTemp: String(device.assignedVehicleCode).includes("BOX") ? "221 F" : "193 F",
        geofenceStatus: device.connectionStatus === "Offline" ? "Last known" : "In corridor",
        eventAt: "2026-06-08T12:05:00Z",
      },
      {
        id: `telemetry-${device.deviceId}-2`,
        deviceId: device.id,
        vehicleId: device.assignedVehicleId,
        driverId: device.assignedDriverId,
        latitude: (baseLat - 0.014).toFixed(5),
        longitude: (baseLng - 0.011).toFixed(5),
        speedMph: device.connectionStatus === "Offline" ? 0 : 51,
        heading: device.connectionStatus === "Offline" ? "Stationary" : "NE",
        engineStatus: device.connectionStatus === "Offline" ? "Visibility lost" : "Running",
        odometer: vehicle?.odometer ?? "0",
        fuelLevel: String(device.assignedVehicleCode).includes("BOX") ? "31%" : "70%",
        coolantTemp: String(device.assignedVehicleCode).includes("BOX") ? "217 F" : "191 F",
        geofenceStatus: device.connectionStatus === "Offline" ? "Last known" : "In corridor",
        eventAt: "2026-06-08T11:42:00Z",
      },
    ];
  });
}

function buildHealthEvents(devices: AnyRecord[]) {
  return devices.flatMap((device) => [
    {
      id: `health-${device.deviceId}-1`,
      deviceId: device.id,
      tenantId: device.tenantId,
      score: device.dataHealthScore,
      status: device.connectionStatus,
      signalStrength: device.signalStrength,
      eventAt: "2026-06-08T12:05:00Z",
      summary: device.connectionStatus === "Online" ? "Heartbeat healthy" : "Recovery watch required",
    },
    {
      id: `health-${device.deviceId}-2`,
      deviceId: device.id,
      tenantId: device.tenantId,
      score: Math.max(35, Number(device.dataHealthScore) - 6),
      status: device.connectionStatus === "Online" ? "Watch" : device.connectionStatus,
      signalStrength: device.signalStrength,
      eventAt: "2026-06-08T10:05:00Z",
      summary: "Recent health trend",
    },
  ]);
}

function buildFirmwareUpdates(devices: AnyRecord[]) {
  return devices.map((device, index) => ({
    id: `fw-${device.deviceId}`,
    deviceId: device.id,
    deviceIdentifier: device.deviceId,
    tenantId: device.tenantId,
    currentVersion: device.firmwareVersion,
    targetVersion: device.targetFirmwareVersion,
    scheduledFor: index % 2 === 0 ? "2026-06-11T02:00:00Z" : null,
    status: index % 2 === 0 ? "Scheduled" : "Available",
    releaseNotes: "Connectivity stability and diagnostics enhancements.",
    createdBy: "Telematics Ops",
  }));
}

function buildDiagnostics(devices: AnyRecord[]) {
  return devices.map((device, index) => ({
    id: `diag-${device.deviceId}`,
    deviceId: device.id,
    tenantId: device.tenantId,
    result: device.connectionStatus === "Offline" ? "Failed" : device.connectionStatus === "Needs attention" ? "Warning" : "Passed",
    batteryVoltage: device.connectionStatus === "Offline" ? "10.8V" : "13.6V",
    modemStatus: device.connectionStatus === "Offline" ? "Disconnected" : "Connected",
    gnssStatus: device.connectionStatus === "Offline" ? "Unavailable" : "Locked",
    faultCode: device.connectionStatus === "Offline" ? "SPN 639 FMI 2" : device.connectionStatus === "Needs attention" ? "Carrier degradation watch" : "None",
    runAt: `2026-06-08T1${Math.min(index, 9)}:15:00Z`,
    runBy: "Device Health",
  }));
}

function buildInstallations(devices: AnyRecord[]) {
  return devices.map((device, index) => ({
    id: `install-${device.deviceId}`,
    deviceId: device.id,
    tenantId: device.tenantId,
    installStatus: device.installStatus,
    installerName: index % 2 === 0 ? "Field Ops Crew A" : "Field Ops Crew B",
    installedAt: index === 4 ? null : `2026-05-${20 + Math.min(index, 8)}T09:00:00Z`,
    checklist: [
      { item: "Power connected", status: device.installStatus === "Awaiting installation" ? "Pending" : "Complete" },
      { item: "Vehicle assigned", status: device.assignedVehicleId ? "Complete" : "Pending" },
      { item: "Road test verified", status: device.connectionStatus === "Offline" ? "Failed" : "Complete" },
      { item: "Provider sync confirmed", status: /Provisioning|Pending/.test(String(device.complianceStatus)) ? "Pending" : "Complete" },
    ],
  }));
}

function buildSensorReadings(devices: AnyRecord[]) {
  return devices.map((device) => ({
    id: `sensor-${device.deviceId}`,
    deviceId: device.id,
    tenantId: device.tenantId,
    temperature: String(device.deviceType).includes("Temperature") ? "4.3 C" : "Ambient",
    humidity: String(device.deviceType).includes("Temperature") ? "63%" : "No humidity channel",
    doorStatus: String(device.deviceType).includes("Temperature") ? "Closed" : "No door channel",
    tirePressure: String(device.deviceType).includes("Tire") ? "102 psi" : "No tire channel",
    fuelLevel: String(device.deviceType).includes("Fuel") ? "68%" : "Vehicle bus",
    recordedAt: "2026-06-08T12:04:00Z",
  }));
}

function buildAuditLogs(devices: AnyRecord[]) {
  return devices.flatMap((device) => [
    {
      id: `audit-${device.deviceId}-1`,
      deviceId: device.id,
      tenantId: device.tenantId,
      action: "device.status.refresh",
      actor: "Device Health",
      eventAt: "2026-06-08T12:05:00Z",
      notes: "Heartbeat state synchronized.",
    },
    {
      id: `audit-${device.deviceId}-2`,
      deviceId: device.id,
      tenantId: device.tenantId,
      action: "device.assignment.confirmed",
      actor: "Fleet Ops",
      eventAt: "2026-05-20T08:00:00Z",
      notes: "Device mapped to active fleet asset.",
    },
  ]);
}

export type TelematicsSeed = typeof telematicsSeedData;
export type TelematicsDeviceSeedRecord = (typeof telematicsSeedData.devices)[number];
export type TelematicsProviderSeedRecord = (typeof telematicsSeedData.providers)[number];
export type TelematicsTelemetrySeedRecord = (typeof telematicsSeedData.telemetryEvents)[number];
export type TelematicsHealthSeedRecord = (typeof telematicsSeedData.deviceHealthEvents)[number];
export type TelematicsFirmwareSeedRecord = (typeof telematicsSeedData.firmwareUpdates)[number];
export type TelematicsDiagnosticSeedRecord = (typeof telematicsSeedData.diagnostics)[number];
export type TelematicsInstallationSeedRecord = (typeof telematicsSeedData.installationRecords)[number];
export type TelematicsSensorSeedRecord = (typeof telematicsSeedData.sensorReadings)[number];
