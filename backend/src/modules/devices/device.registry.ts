import { DeviceTypeDefinition } from "./device.types";

export const deviceTypes: DeviceTypeDefinition[] = [
  {
    code: "obd_ii",
    name: "OBD-II Device",
    category: "light_vehicle_telematics",
    description: "Used for light vehicles to read engine data, VIN, DTCs, speed, and odometer.",
    capabilities: ["vin", "speed", "rpm", "odometer", "fuel_level", "diagnostic_codes"],
  },
  {
    code: "j1939_can",
    name: "J1939/CAN Device",
    category: "heavy_vehicle_telematics",
    description: "Used for trucks, buses, and heavy equipment to read engine and vehicle data.",
    capabilities: ["engine_hours", "odometer", "fuel_rate", "torque", "diagnostic_codes"],
  },
  {
    code: "gps_tracker",
    name: "GPS Tracker",
    category: "location_tracking",
    description: "Tracks vehicle location, ignition, movement, speed, and geofence status.",
    capabilities: ["gps_location", "speed", "ignition_status", "geofence"],
  },
  {
    code: "dashcam",
    name: "AI Dashcam",
    category: "safety_video",
    description: "Captures road/driver video and safety events such as harsh braking or distraction.",
    capabilities: ["road_video", "driver_video", "harsh_braking", "distraction_detection"],
  },
  {
    code: "temperature_sensor",
    name: "Temperature Sensor",
    category: "cold_chain",
    description: "Monitors temperature and humidity for reefer and cold chain operations.",
    capabilities: ["temperature", "humidity", "door_open"],
  },
  {
    code: "fuel_sensor",
    name: "Fuel Sensor",
    category: "fuel_monitoring",
    description: "Monitors fuel level, refills, theft, and consumption patterns.",
    capabilities: ["fuel_level", "fuel_refill", "fuel_theft"],
  },
  {
    code: "ble_rfid_driver_id",
    name: "BLE/RFID Driver ID",
    category: "driver_identity",
    description: "Identifies assigned driver through BLE, RFID, or similar identity method.",
    capabilities: ["driver_id", "driver_authentication"],
  },
  {
    code: "tire_pressure_sensor",
    name: "Tire Pressure Sensor",
    category: "vehicle_health",
    description: "Monitors tire pressure and tire temperature.",
    capabilities: ["tire_pressure", "tire_temperature"],
  },
];
