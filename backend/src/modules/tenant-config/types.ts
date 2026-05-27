export type CountryCode = "US" | "CA" | "SA" | "AE" | "CUSTOM";

export type IndustryCode =
  | "logistics"
  | "cold_chain"
  | "school_transport"
  | "construction"
  | "oil_gas"
  | "rental_fleet"
  | "delivery_fleet";

export type DeviceType =
  | "obd_ii"
  | "j1939_can"
  | "gps_tracker"
  | "dashcam"
  | "temperature_sensor"
  | "fuel_sensor"
  | "ble_rfid_driver_id"
  | "tire_pressure_sensor";

export type CompliancePackCode =
  | "USA_FMCSA_ELD"
  | "CANADA_ELD"
  | "KSA_TGA_WASL_READY"
  | "UAE_TRANSPORT_READY"
  | "CUSTOM_COUNTRY_RULES";

export type ModuleCode =
  | "fleet_dashboard"
  | "live_map"
  | "driver_management"
  | "vehicle_management"
  | "device_management"
  | "eld_hos"
  | "dvir"
  | "wasl_ready_reporting"
  | "pdpl_privacy_controls"
  | "cst_device_approval_tracker"
  | "cold_chain_monitoring"
  | "temperature_alerts"
  | "fuel_monitoring"
  | "dashcam_safety"
  | "school_transport_tracking"
  | "construction_equipment_tracking"
  | "oil_gas_journey_management"
  | "rental_fleet_management"
  | "delivery_dispatch"
  | "proof_of_delivery"
  | "maintenance"
  | "route_optimization"
  | "geofencing";

export interface TenantRuntimeConfig {
  tenantId: string;
  operatingCountries: CountryCode[];
  primaryCountry: CountryCode;
  industries: IndustryCode[];
  enabledDeviceTypes: DeviceType[];
  enabledCompliancePacks: CompliancePackCode[];
  enabledModules: ModuleCode[];
  languages: string[];
  currency: string;
  timezone: string;
  distanceUnit: "miles" | "kilometers";
  fuelUnit: "gallon" | "liter";
}
