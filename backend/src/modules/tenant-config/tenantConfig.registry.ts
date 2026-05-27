import {
  CountryCode,
  CompliancePackCode,
  DeviceType,
  IndustryCode,
  ModuleCode,
} from "./types";

export const countryDefaults: Record<
  CountryCode,
  {
    currency: string;
    timezone: string;
    languages: string[];
    distanceUnit: "miles" | "kilometers";
    fuelUnit: "gallon" | "liter";
    compliancePack: CompliancePackCode;
  }
> = {
  US: {
    currency: "USD",
    timezone: "America/New_York",
    languages: ["en"],
    distanceUnit: "miles",
    fuelUnit: "gallon",
    compliancePack: "USA_FMCSA_ELD",
  },

  CA: {
    currency: "CAD",
    timezone: "America/Toronto",
    languages: ["en", "fr"],
    distanceUnit: "kilometers",
    fuelUnit: "liter",
    compliancePack: "CANADA_ELD",
  },

  SA: {
    currency: "SAR",
    timezone: "Asia/Riyadh",
    languages: ["en", "ar"],
    distanceUnit: "kilometers",
    fuelUnit: "liter",
    compliancePack: "KSA_TGA_WASL_READY",
  },

  AE: {
    currency: "AED",
    timezone: "Asia/Dubai",
    languages: ["en", "ar"],
    distanceUnit: "kilometers",
    fuelUnit: "liter",
    compliancePack: "UAE_TRANSPORT_READY",
  },

  CUSTOM: {
    currency: "USD",
    timezone: "UTC",
    languages: ["en"],
    distanceUnit: "kilometers",
    fuelUnit: "liter",
    compliancePack: "CUSTOM_COUNTRY_RULES",
  },
};

export const countryModules: Record<CountryCode, ModuleCode[]> = {
  US: [
    "fleet_dashboard",
    "live_map",
    "driver_management",
    "vehicle_management",
    "device_management",
    "eld_hos",
    "dvir",
    "maintenance",
    "geofencing",
  ],

  CA: [
    "fleet_dashboard",
    "live_map",
    "driver_management",
    "vehicle_management",
    "device_management",
    "eld_hos",
    "dvir",
    "maintenance",
    "geofencing",
  ],

  SA: [
    "fleet_dashboard",
    "live_map",
    "driver_management",
    "vehicle_management",
    "device_management",
    "wasl_ready_reporting",
    "pdpl_privacy_controls",
    "cst_device_approval_tracker",
    "maintenance",
    "geofencing",
  ],

  AE: [
    "fleet_dashboard",
    "live_map",
    "driver_management",
    "vehicle_management",
    "device_management",
    "pdpl_privacy_controls",
    "maintenance",
    "geofencing",
  ],

  CUSTOM: [
    "fleet_dashboard",
    "live_map",
    "driver_management",
    "vehicle_management",
    "device_management",
    "maintenance",
    "geofencing",
  ],
};

export const industryModules: Record<IndustryCode, ModuleCode[]> = {
  logistics: [
    "delivery_dispatch",
    "fuel_monitoring",
    "route_optimization",
    "proof_of_delivery",
  ],

  cold_chain: ["cold_chain_monitoring", "temperature_alerts"],

  school_transport: ["school_transport_tracking", "geofencing"],

  construction: [
    "construction_equipment_tracking",
    "fuel_monitoring",
    "maintenance",
    "geofencing",
  ],

  oil_gas: [
    "oil_gas_journey_management",
    "dashcam_safety",
    "geofencing",
    "maintenance",
  ],

  rental_fleet: ["rental_fleet_management", "maintenance", "geofencing"],

  delivery_fleet: [
    "delivery_dispatch",
    "route_optimization",
    "proof_of_delivery",
  ],
};

export const deviceRequiredModules: Record<DeviceType, ModuleCode[]> = {
  obd_ii: ["device_management", "fuel_monitoring", "maintenance"],

  j1939_can: ["device_management", "fuel_monitoring", "maintenance"],

  gps_tracker: ["live_map", "device_management", "geofencing"],

  dashcam: ["dashcam_safety"],

  temperature_sensor: ["cold_chain_monitoring", "temperature_alerts"],

  fuel_sensor: ["fuel_monitoring"],

  ble_rfid_driver_id: ["driver_management"],

  tire_pressure_sensor: ["device_management", "maintenance"],
};
