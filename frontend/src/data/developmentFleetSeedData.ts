import type { AnyRecord } from "@/types";
import {
  bookings as seedBookings,
  campaigns as seedCampaigns,
  contracts as seedContracts,
  customers as seedCustomers,
  devices as seedDevices,
  drivers as seedDrivers,
  expenses as seedExpenses,
  incidents as seedIncidents,
  invoices as seedInvoices,
  leads as seedLeads,
  maintenance as seedMaintenance,
  opportunities as seedOpportunities,
  quotations as seedQuotations,
  rateCards as seedRateCards,
  shipments as seedShipments,
  vehicles as seedVehicles,
  supportTickets as seedSupportTickets,
} from "@/data/mockOperatingData";

function firstName(value: string) {
  return String(value).split(" ")[0] || value;
}

const vehicles = seedVehicles.map((vehicle) => ({
  ...vehicle,
  id: vehicle.vehicleId,
  vehicleCode: vehicle.vehicleId,
  assignedVehicle: vehicle.vehicleId,
  type: vehicle.vehicleType,
  make: firstName(vehicle.makeModel),
  model: String(vehicle.makeModel).replace(firstName(vehicle.makeModel), "").trim() || vehicle.vehicleType,
  plateNumber: vehicle.plateNumber,
  fleetReadinessScore: vehicle.status === "Active" ? 92 : vehicle.status === "Idle" ? 85 : 61,
  dataCompletenessScore: vehicle.complianceStatus === "Compliant" ? 96 : 78,
  riskHeatScore: vehicle.maintenanceStatus === "Critical" || vehicle.complianceStatus === "Review" ? 72 : 18,
  deviceStatus: vehicle.maintenanceStatus === "Critical" ? "Review" : "Online",
  cameraStatus: vehicle.status === "Active" ? "Recording" : "Standby",
}));

const drivers = seedDrivers.map((driver) => ({
  ...driver,
  id: driver.driverId,
  driverCode: driver.driverId,
  fullName: driver.name,
  complianceScore: driver.status === "Review" ? 76 : 92,
  driverReadinessScore: driver.status === "Review" ? 78 : 94,
  currentTrip: seedShipments.find((shipment) => shipment.driver === driver.name)?.shipmentId ?? "--",
  currentJob: seedShipments.find((shipment) => shipment.driver === driver.name)?.bookingId ?? "--",
  assignedVehicle: driver.assignedVehicle,
  licenseStatus: /2026/.test(driver.licenseExpiry) ? "Expiring Soon" : "Valid",
  incidents: seedIncidents.filter((incident) => incident.driver === driver.name).length,
  coachingStatus: seedIncidents.some((incident) => incident.driver === driver.name) ? "Queued" : "Clear",
}));

const shipments = seedShipments.map((shipment) => ({
  ...shipment,
  id: shipment.shipmentId,
  jobCode: shipment.bookingId,
  shipmentCode: shipment.shipmentId,
  customerName: shipment.customer,
  pickupAddress: shipment.origin,
  dropoffAddress: shipment.destination,
  vehicleCode: shipment.vehicle,
  driverName: shipment.driver,
  proofStatus: shipment.podStatus,
  invoiceReady: shipment.invoiceStatus === "Ready",
  statusTimeline: [
    { status: "Booked", at: "Created" },
    { status: shipment.currentStatus, at: shipment.eta },
  ],
}));

const jobs = seedBookings.map((booking) => ({
  id: booking.bookingId,
  jobNumber: booking.bookingId,
  jobCode: booking.bookingId,
  customerId: booking.customer,
  customerName: booking.customer,
  jobType: String(booking.cargoType).includes("Reefer") ? "Cold Chain" : "Delivery",
  priority: /Critical|Urgent/i.test(String(booking.status)) ? "High" : "Normal",
  pickupAddress: booking.pickup,
  dropoffAddress: booking.dropoff,
  scheduledStart: booking.pickupDateTime,
  scheduledEnd: booking.deliveryDeadline,
  slaWindowStart: booking.pickupDateTime,
  slaWindowEnd: booking.deliveryDeadline,
  requiredVehicleType: booking.vehicleRequired,
  requiredDriverCertification: booking.vehicleRequired.includes("Reefer") ? "Cold Chain" : "Standard",
  assignedDriverId: seedDrivers.find((driver) => driver.name === shipments.find((shipment) => shipment.bookingId === booking.bookingId)?.driver)?.driverId ?? "--",
  assignedVehicleId: shipments.find((shipment) => shipment.bookingId === booking.bookingId)?.vehicle ?? "--",
  driverName: shipments.find((shipment) => shipment.bookingId === booking.bookingId)?.driver ?? "--",
  vehicleCode: shipments.find((shipment) => shipment.bookingId === booking.bookingId)?.vehicle ?? "--",
  slaStatus: /Delayed|Awaiting|Quoted/i.test(String(booking.status)) ? "At Risk" : "On Track",
  routeId: booking.bookingId.replace("BK", "RTE"),
  notes: booking.status,
  status: booking.status === "Confirmed" ? "Assigned" : booking.status === "Awaiting Dispatch" ? "Unassigned" : booking.status,
}));

const customers = seedCustomers.map((customer) => ({
  ...customer,
  id: customer.id,
  customerCode: customer.id,
  name: customer.companyName,
  contactName: customer.primaryContact,
  activeJobs: Math.max(1, Math.round(Number(customer.monthlyShipments) / 140)),
  status: customer.status,
  revenueMtd: customer.revenueMtd,
  healthScore: customer.healthScore,
}));

const maintenanceRecords = seedMaintenance.map((record) => ({
  ...record,
  id: record.workOrderId,
  recordType: "Work Order",
  title: `${record.issueType} review`,
  vehicleCode: record.vehicle,
  riskLevel: record.priority,
  dueAt: record.dueDate,
}));

const safetyIncidents = seedIncidents.map((incident) => ({
  ...incident,
  id: incident.incidentId,
  incidentNumber: incident.incidentId,
  driverName: incident.driver,
  vehicleCode: incident.vehicle,
  incidentType: incident.incidentType,
  reviewStatus: incident.status,
  evidenceStatus: incident.evidenceAvailable,
}));

const alerts = [
  ...seedIncidents.map((incident) => ({
    alertId: `ALT-${incident.incidentId.replace("INC-", "")}`,
    category: "Safety",
    type: incident.incidentType,
    entity: incident.vehicle,
    customer: seedShipments.find((shipment) => shipment.shipmentId === incident.shipment)?.customer || "Fleet account",
    severity: incident.severity,
    owner: "Safety",
    location: incident.vehicle === "BOX-106" ? "Manassas yard" : "Live route",
    age: "Live",
    recommendedAction: incident.status === "Under Review" ? "Review evidence package" : "Create coaching task",
    status: incident.status === "Under Review" ? "Open" : "In Progress",
  })),
  ...seedMaintenance.map((record) => ({
    alertId: `ALT-${record.workOrderId.replace("WO-", "")}`,
    category: "Maintenance",
    type: record.issueType,
    entity: record.vehicle,
    customer: seedShipments.find((shipment) => shipment.vehicle === record.vehicle)?.customer || "Fleet account",
    severity: record.priority,
    owner: "Maintenance",
    location: record.assignedWorkshop,
    age: "Live",
    recommendedAction: record.priority === "Critical" ? "Block from dispatch" : "Schedule service",
    status: record.status,
  })),
  ...seedSupportTickets.map((ticket) => ({
    alertId: `ALT-${ticket.ticketId.replace("TCK-", "")}`,
    category: "Customer",
    type: ticket.issueType,
    entity: ticket.shipment,
    customer: ticket.customer,
    severity: ticket.priority,
    owner: ticket.assignedTeam,
    location: "Customer support",
    age: ticket.slaTimer,
    recommendedAction: "Acknowledge and update ETA",
    status: ticket.status,
  })),
];

const complianceRecords = [
  ...seedDrivers.map((driver) => ({
    id: `CMP-${driver.driverId}`,
    scope: "Driver",
    entityId: driver.driverId,
    entityName: driver.name,
    status: driver.status === "Review" ? "Warning" : "Compliant",
    authority: "HOS / License",
    expiryDate: driver.licenseExpiry,
    notes: driver.status === "Review" ? "License or HOS requires attention" : "Good standing",
  })),
  ...seedVehicles.map((vehicle) => ({
    id: `CMP-${vehicle.vehicleId}`,
    scope: "Vehicle",
    entityId: vehicle.vehicleId,
    entityName: vehicle.makeModel,
    status: vehicle.complianceStatus,
    authority: "Registration / Insurance",
    expiryDate: vehicle.registrationExpiry,
    notes: vehicle.maintenanceStatus === "Critical" ? "Vehicle blocked until maintenance closes" : "Compliant",
  })),
];

export const developmentFleetSeedData = {
  tenants: [
    { id: "tenant-northshore", name: "Northshore Fleet Logistics", status: "Active" },
    { id: "tenant-client", name: "Client Tenant", status: "Active" },
    { id: "tenant-vendor", name: "Vendor Services", status: "Active" },
  ],
  roles: [
    { id: "super_admin", name: "Super Admin" },
    { id: "tenant_admin", name: "Tenant Admin" },
    { id: "fleet_manager", name: "Fleet Manager" },
    { id: "dispatcher", name: "Dispatcher" },
    { id: "driver", name: "Driver" },
    { id: "safety_manager", name: "Safety Manager" },
    { id: "maintenance_manager", name: "Maintenance Manager" },
    { id: "customer", name: "Customer" },
  ],
  permissions: [
    "dashboard:view",
    "vehicles:view", "vehicles:create", "vehicles:update", "vehicles:delete", "vehicles:assign", "vehicles:export",
    "drivers:view", "drivers:create", "drivers:update", "drivers:delete", "drivers:assign", "drivers:export",
    "shipments:view", "shipments:create", "shipments:update", "shipments:delete", "shipments:export",
    "dispatch:view", "dispatch:create", "dispatch:update", "dispatch:assign", "dispatch:cancel",
    "customers:view", "customers:create", "customers:update", "customers:delete",
    "alerts:view", "alerts:acknowledge", "alerts:close",
    "maintenance:view", "maintenance:create", "maintenance:update", "maintenance:close",
    "safety:view", "safety:create", "safety:update", "safety:review", "safety:evidence:view", "safety:evidence:export",
    "compliance:view", "compliance:update", "compliance:export",
    "reports:view", "reports:export",
    "users:view", "users:create", "users:update", "users:delete",
    "roles:view", "roles:update",
    "settings:view", "settings:update",
    "audit:view",
  ],
  bookings: seedBookings,
  campaigns: seedCampaigns,
  contracts: seedContracts,
  vehicles,
  drivers,
  shipments,
  jobs,
  customers,
  devices: seedDevices,
  expenses: seedExpenses,
  leads: seedLeads,
  opportunities: seedOpportunities,
  quotations: seedQuotations,
  rateCards: seedRateCards,
  alerts,
  maintenanceRecords,
  safetyIncidents,
  complianceRecords,
  invoices: seedInvoices,
  maintenance: seedMaintenance,
  incidents: seedIncidents,
  supportTickets: seedSupportTickets,
};

export function getDevelopmentDashboardSummary() {
  const activeVehicles = vehicles.filter((vehicle) => /Active|Idle/.test(String(vehicle.status))).length;
  const openAlerts = alerts.filter((alert) => String(alert.status).match(/Open|In Progress|Review/i)).length;
  const pendingMaintenance = maintenanceRecords.filter((record) => /Critical|High|Scheduled|In Progress/i.test(String(record.status))).length;
  const activeShipments = shipments.filter((shipment) => shipment.currentStatus !== "Delivered").length;

  return {
    kpis: [
      { id: "vehicles", label: "Active Vehicles", valueText: String(activeVehicles), status: "Healthy" },
      { id: "alerts", label: "Open Alerts", valueText: String(openAlerts), status: "Risk" },
      { id: "maintenance", label: "Pending Maintenance", valueText: String(pendingMaintenance), status: "Review" },
      { id: "shipments", label: "Active Shipments", valueText: String(activeShipments), status: "Active" },
    ],
    charts: {
      weeklyJobs: [18, 22, 24, 19, 26, 29, 31],
      costLeakage: [14, 18, 12, 20, 17, 15, 11],
    },
    mapPreview: vehicles.slice(0, 8).map((vehicle) => ({ id: vehicle.vehicleId, vehicleCode: vehicle.vehicleId })),
    priorityActions: [
      { id: "act-1", title: "Review open alerts", body: "Safety and maintenance signals are blocking dispatch readiness." },
      { id: "act-2", title: "Push ETA updates", body: "Active shipments with delay risk should trigger customer communication." },
      { id: "act-3", title: "Clear maintenance queue", body: "Critical and due work orders should be resolved before the next dispatch cycle." },
    ],
    aiBrief: "Fleet readiness is stable but maintenance and customer alert resolution should be prioritized before dispatching additional freight.",
    timeline: [
      { id: "t1", title: "Vehicle assigned to route", eventType: "dispatch.assigned", eventTime: "Live" },
      { id: "t2", title: "Maintenance watch opened", eventType: "maintenance.created", eventTime: "Live" },
      { id: "t3", title: "Customer ETA sent", eventType: "eta.sent", eventTime: "Live" },
    ],
  };
}

export function findDevelopmentVehicle(id: string | number) {
  return vehicles.find((vehicle) => String(vehicle.id) === String(id) || String(vehicle.vehicleId) === String(id));
}

export function findDevelopmentDriver(id: string | number) {
  return drivers.find((driver) => String(driver.id) === String(id) || String(driver.driverId) === String(id));
}

export function findDevelopmentShipment(id: string | number) {
  return shipments.find((shipment) => String(shipment.id) === String(id) || String(shipment.shipmentId) === String(id));
}

export function findDevelopmentCustomer(id: string | number) {
  return customers.find((customer) => String(customer.id) === String(id) || String(customer.customerCode) === String(id));
}

export function findDevelopmentMaintenanceRecord(id: string | number) {
  return maintenanceRecords.find((record) => String(record.id) === String(id) || String(record.workOrderId) === String(id));
}

export function findDevelopmentSafetyIncident(id: string | number) {
  return safetyIncidents.find((incident) => String(incident.id) === String(id) || String(incident.incidentId) === String(id));
}
