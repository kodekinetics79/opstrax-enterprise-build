export const industryModuleDefinitions = [
  {
    code: "logistics",
    name: "Logistics",
    description:
      "Shipment tracking, dispatching, route optimization, proof of delivery, and ETA monitoring.",
    modules: ["delivery_dispatch", "route_optimization", "proof_of_delivery", "fuel_monitoring"],
  },
  {
    code: "cold_chain",
    name: "Cold Chain",
    description:
      "Temperature monitoring, humidity monitoring, reefer status, door-open alerts, and excursion reporting.",
    modules: ["cold_chain_monitoring", "temperature_alerts"],
  },
  {
    code: "school_transport",
    name: "School Transport",
    description:
      "Student boarding, route safety, bus attendance, geofence alerts, and emergency workflows.",
    modules: ["school_transport_tracking", "geofencing"],
  },
  {
    code: "construction",
    name: "Construction",
    description:
      "Equipment tracking, job site geofencing, idle time, asset utilization, and maintenance.",
    modules: ["construction_equipment_tracking", "fuel_monitoring", "maintenance"],
  },
  {
    code: "oil_gas",
    name: "Oil & Gas",
    description:
      "Journey management, remote-area tracking, driver fatigue monitoring, emergency alerts, and route compliance.",
    modules: ["oil_gas_journey_management", "dashcam_safety", "geofencing"],
  },
  {
    code: "rental_fleet",
    name: "Rental Fleet",
    description:
      "Vehicle availability, rental status, mileage tracking, return inspection, and geo-restriction.",
    modules: ["rental_fleet_management", "maintenance", "geofencing"],
  },
  {
    code: "delivery_fleet",
    name: "Delivery Fleet",
    description:
      "Multi-stop dispatch, customer ETA, proof of delivery, failed delivery reason, and delivery performance.",
    modules: ["delivery_dispatch", "route_optimization", "proof_of_delivery"],
  },
];
