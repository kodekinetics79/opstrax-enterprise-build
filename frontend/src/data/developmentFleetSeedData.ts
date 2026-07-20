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
    "telematics:devices:view", "telematics:devices:create", "telematics:devices:update", "telematics:devices:delete",
    "telematics:devices:assign", "telematics:devices:diagnostics", "telematics:devices:firmware", "telematics:devices:export",
    "telematics:providers:manage",
    "telematics:gps:view", "telematics:gps:export",
    "telematics:diagnostics:view", "telematics:diagnostics:update", "telematics:diagnostics:export",
    "telematics:sensors:view", "telematics:sensors:update", "telematics:sensors:export",
  ],
  adminUsers: [
    { id: 1, fullName: "Mason Lee", email: "superadmin@opstrax.com", companyId: 1, companyName: "OpsTrax Demo Logistics", roleId: 1, roleName: "Super Admin", status: "Active", lastLoginAt: "2026-06-08T12:10:00Z", mfaStatus: "Enabled" },
    { id: 2, fullName: "Avery Stone", email: "admin@opstrax.com", companyId: 1, companyName: "OpsTrax Demo Logistics", roleId: 2, roleName: "Company Admin", status: "Active", lastLoginAt: "2026-06-08T11:40:00Z", mfaStatus: "Enabled" },
    { id: 4, fullName: "Erin Parker", email: "operations@demo-fleet.com", companyId: 1, companyName: "Northshore Fleet Logistics", roleId: 13, roleName: "Operations Manager", status: "Active", lastLoginAt: "2026-06-08T10:45:00Z", mfaStatus: "Enabled" },
    { id: 5, fullName: "Maya Patel", email: "dispatcher@demo-fleet.com", companyId: 1, companyName: "Northshore Fleet Logistics", roleId: 4, roleName: "Dispatcher", status: "Active", lastLoginAt: "2026-06-08T09:55:00Z", mfaStatus: "Enabled" },
    { id: 7, fullName: "Omar Ali", email: "driver@demo-fleet.com", companyId: 1, companyName: "Northshore Fleet Logistics", roleId: 5, roleName: "Driver", status: "Active", lastLoginAt: "2026-06-07T18:20:00Z", mfaStatus: "Disabled" },
    { id: 8, fullName: "Sofia Ramirez", email: "safety@demo-fleet.com", companyId: 1, companyName: "Northshore Fleet Logistics", roleId: 7, roleName: "Safety & Compliance Manager", status: "Active", lastLoginAt: "2026-06-08T08:33:00Z", mfaStatus: "Enabled" },
    { id: 11, fullName: "Jordan Reyes", email: "maintenance@demo-fleet.com", companyId: 1, companyName: "Northshore Fleet Logistics", roleId: 6, roleName: "Maintenance Manager", status: "Active", lastLoginAt: "2026-06-08T07:10:00Z", mfaStatus: "Enabled" },
    { id: 12, fullName: "Erin Matthews", email: "customer@client.com", companyId: 3, companyName: "Client Tenant", roleId: 10, roleName: "Customer Portal User", status: "Active", lastLoginAt: "2026-06-07T16:45:00Z", mfaStatus: "Disabled" },
  ],
  adminRoles: [
    { id: 1, name: "Super Admin", permissions: ["*"], userCount: 1, scope: "Platform" },
    { id: 2, name: "Company Admin", permissions: ["*"], userCount: 1, scope: "Tenant" },
    { id: 3, name: "Fleet Manager", permissions: ["dashboard:view", "vehicles:view", "vehicles:create", "vehicles:update", "vehicles:delete", "vehicles:assign", "vehicles:export", "drivers:view", "drivers:create", "drivers:update", "drivers:delete", "drivers:assign", "drivers:export", "shipments:view", "shipments:create", "shipments:update", "shipments:delete", "shipments:export", "dispatch:view", "dispatch:create", "dispatch:update", "dispatch:assign", "dispatch:cancel", "alerts:view", "alerts:acknowledge", "alerts:close", "maintenance:view", "maintenance:create", "maintenance:update", "maintenance:close", "compliance:view", "compliance:update", "compliance:export", "reports:view", "reports:export", "telematics:devices:view", "telematics:devices:create", "telematics:devices:update", "telematics:devices:delete", "telematics:devices:assign", "telematics:devices:diagnostics", "telematics:devices:firmware", "telematics:devices:export", "telematics:gps:view", "telematics:gps:export", "telematics:diagnostics:view", "telematics:diagnostics:update", "telematics:diagnostics:export", "telematics:sensors:view", "telematics:sensors:update", "telematics:sensors:export"], userCount: 1, scope: "Tenant" },
    { id: 4, name: "Dispatcher", permissions: ["dashboard:view", "vehicles:view", "drivers:view", "shipments:view", "shipments:create", "shipments:update", "shipments:export", "dispatch:view", "dispatch:create", "dispatch:update", "dispatch:assign", "dispatch:cancel", "alerts:view", "alerts:acknowledge", "customers:view", "reports:view", "telematics:devices:view", "telematics:gps:view"], userCount: 1, scope: "Tenant" },
    { id: 5, name: "Driver", permissions: ["shipments:view", "vehicles:view", "drivers:view", "safety:view", "compliance:view", "alerts:view"], userCount: 1, scope: "Tenant" },
    { id: 6, name: "Maintenance Manager", permissions: ["dashboard:view", "vehicles:view", "maintenance:view", "maintenance:create", "maintenance:update", "maintenance:close", "alerts:view", "alerts:acknowledge", "alerts:close", "compliance:view", "reports:view", "telematics:devices:view", "telematics:devices:update", "telematics:devices:diagnostics", "telematics:devices:firmware", "telematics:gps:view", "telematics:diagnostics:view", "telematics:diagnostics:update", "telematics:diagnostics:export", "telematics:sensors:view", "telematics:sensors:update", "telematics:sensors:export"], userCount: 1, scope: "Tenant" },
    { id: 7, name: "Safety & Compliance Manager", permissions: ["dashboard:view", "safety:view", "safety:create", "safety:update", "safety:review", "safety:evidence:view", "safety:evidence:export", "alerts:view", "alerts:acknowledge", "alerts:close", "compliance:view", "compliance:update", "compliance:export", "reports:view", "telematics:devices:view", "telematics:devices:diagnostics", "telematics:devices:export", "telematics:gps:view", "telematics:gps:export", "telematics:diagnostics:view", "telematics:diagnostics:export", "telematics:sensors:view", "telematics:sensors:export"], userCount: 1, scope: "Tenant" },
    { id: 10, name: "Customer Portal User", permissions: ["shipments:view", "customer_portal:view", "alerts:view"], userCount: 1, scope: "Customer" },
  ],
  adminSettings: {
    tenantName: "Northshore Fleet Logistics",
    defaultLanguage: "en-US",
    defaultCountry: "US",
    timezone: "America/New_York",
    dateFormat: "MM/DD/YYYY",
    currency: "USD",
    distanceUnit: "Miles",
    volumeUnit: "Gallons",
    branding: "Enterprise",
  },
  adminAuditLogs: [
    { id: 9001, createdAt: "2026-06-08T12:12:00Z", actorName: "Mason Lee", actionName: "user.created", entityName: "User", entityId: 101, moduleKey: "user-management", severity: "Info", detailsJson: { target: "new.driver@demo-fleet.com" } },
    { id: 9002, createdAt: "2026-06-08T12:04:00Z", actorName: "Avery Stone", actionName: "role.updated", entityName: "Role", entityId: 3, moduleKey: "user-management", severity: "Info", detailsJson: { permissionCount: 26 } },
    { id: 9003, createdAt: "2026-06-08T11:58:00Z", actorName: "Sofia Ramirez", actionName: "permission.denied", entityName: "Admin", entityId: null, moduleKey: "user-management", severity: "Warning", detailsJson: { permission: "users:delete" } },
    { id: 9004, createdAt: "2026-06-08T11:51:00Z", actorName: "Maya Patel", actionName: "export.requested", entityName: "Users", entityId: null, moduleKey: "user-management", severity: "Info", detailsJson: { format: "CSV" } },
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
  const drivingCount = vehicles.filter((v) => String(v.status) === "Active").length;
  const idlingCount  = vehicles.filter((v) => String(v.status) === "Idle").length;
  const parkedCount  = vehicles.filter((v) => /Parked|Standby/.test(String(v.status))).length;
  const offlineCount = vehicles.filter((v) => /Offline|Inactive|Unknown/.test(String(v.status))).length;
  const activeShipments = shipments.filter((s) => s.currentStatus !== "Delivered").length;
  const slaExceptions   = shipments.filter((s) => /Critical|High/.test(String(s.slaRisk)) && s.currentStatus !== "Delivered").length;
  const fleetOnRoad     = Math.max(drivingCount + idlingCount, 9);
  const openAlerts      = alerts.filter((a) => String(a.status).match(/Open|In Progress|Review/i)).length;

  return {
    kpis: [
      {
        id: "shipments", label: "Active Shipments",
        valueText: String(activeShipments || 14),
        status: "Active",
        trend: "9 in transit · 5 at stops",
        delta: "+12%",
      },
      {
        id: "sla", label: "SLA Exceptions",
        valueText: String(slaExceptions || 3),
        status: slaExceptions > 0 ? "Risk" : "On Track",
        trend: slaExceptions > 0 ? `${slaExceptions} behind committed ETA` : "All shipments on time",
        delta: "-8%",
      },
      {
        id: "assignments", label: "Overdue Assignments",
        valueText: "2",
        status: "Critical",
        trend: "Jobs past dispatch window",
        delta: "+1",
      },
      {
        id: "fleet", label: "Fleet On Road",
        valueText: String(fleetOnRoad),
        status: "Active",
        trend: `${Math.max(drivingCount, 6)} driving · ${Math.max(idlingCount, 3)} idling`,
        delta: "+3",
      },
      {
        id: "safety", label: "Safety Events (24h)",
        valueText: String(Math.max(openAlerts, 2)),
        status: openAlerts > 3 ? "Warning" : "Active",
        trend: openAlerts > 0 ? "Require acknowledgement" : "No events — all clear",
        delta: "-2",
      },
      {
        id: "readiness", label: "Fleet Readiness",
        valueText: "94%",
        status: "Healthy",
        trend: "Above 90% target threshold",
        delta: "+2%",
      },
      {
        id: "fuel", label: "Fuel Efficiency",
        valueText: "8.2",
        status: "Active",
        trend: "km/L fleet average · 4% improvement",
        delta: "+4%",
      },
    ],
    fleetStatus: {
      driving: Math.max(drivingCount, 6),
      idling:  Math.max(idlingCount,  3),
      parked:  Math.max(parkedCount,  4),
      offline: Math.max(offlineCount, 2),
    },
    charts: {
      weeklyJobs:   [18, 22, 24, 19, 26, 29, 31],
      costLeakage:  [14, 18, 12, 20, 17, 15, 11],
      safetyScore:  [78, 80, 82, 79, 84, 86, 87],
      monthlyVolume: [142, 156, 168, 174, 189, 201, 218, 225, 234, 248, 256, 271],
      routeEfficiency: [87, 89, 86, 91, 88, 92, 90, 93, 91, 94, 92, 95],
    },
    mapPreview: vehicles.slice(0, 12).map((v) => ({ id: v.vehicleId, vehicleCode: v.vehicleId, status: v.status })),
    exceptions: [
      {
        id: "exc-1", severity: "Critical",
        vehicle: "TRK-114", driver: "Mohammed Al-Zahrani",
        event: "HOS Breach Risk",
        slaImpact: "Driver at 9h 12min — SABIC Logistics delivery now 2h+ overdue",
        actionLabel: "Open HOS", actionRoute: "/hos-eld",
        timestamp: "4 min ago",
      },
      {
        id: "exc-2", severity: "Critical",
        vehicle: "VAN-207", driver: null,
        event: "Vehicle Out of Service",
        slaImpact: "Coolant leak on pre-trip DVIR — load must be reassigned immediately",
        actionLabel: "Create WO", actionRoute: "/work-orders",
        timestamp: "11 min ago",
      },
      {
        id: "exc-3", severity: "Warning",
        vehicle: "TRK-108", driver: "Khaled Al-Rashidi",
        event: "Fuel Anomaly",
        slaImpact: "Off-route purchase SAR 420 at Shell Dhahran — approval required",
        actionLabel: "Review", actionRoute: "/fuel-idling",
        timestamp: "18 min ago",
      },
      {
        id: "exc-4", severity: "Warning",
        vehicle: "SHP-0041", driver: null,
        event: "SLA at Risk",
        slaImpact: "Al-Marai Distribution ETA now 47 min behind committed window",
        actionLabel: "Update ETA", actionRoute: "/active-shipments",
        timestamp: "22 min ago",
      },
      {
        id: "exc-5", severity: "Warning",
        vehicle: "RFG-302", driver: null,
        event: "Cold Chain Excursion",
        slaImpact: "Temperature +3°C above set point for 22 min — confirm load integrity",
        actionLabel: "View Device", actionRoute: "/iot-devices",
        timestamp: "35 min ago",
      },
      {
        id: "exc-6", severity: "Warning",
        vehicle: "TRK-106", driver: null,
        event: "Maintenance Overdue",
        slaImpact: "PM-A service 320 km overdue — do not dispatch until resolved",
        actionLabel: "Schedule", actionRoute: "/preventive-maintenance",
        timestamp: "1 hr ago",
      },
      {
        id: "exc-7", severity: "Info",
        vehicle: null, driver: "Ahmed Al-Dosari",
        event: "Coaching Overdue",
        slaImpact: "Harsh braking coaching task pending 4 days — safety score at risk",
        actionLabel: "Open Coaching", actionRoute: "/coaching",
        timestamp: "2 hr ago",
      },
      {
        id: "exc-8", severity: "Info",
        vehicle: "TRK-119", driver: "Faisal Al-Harthi",
        event: "Route Deviation Detected",
        slaImpact: "Vehicle deviated 2.3 km from planned route — Riyadh Eastern Ring",
        actionLabel: "View Route", actionRoute: "/gps-tracking",
        timestamp: "2 hr ago",
      },
    ],
    briefItems: [
      `${fleetOnRoad} vehicles on road — TRK-114 and VAN-207 showing operational risk. Review before next dispatch window.`,
      "SHP-0041 (Al-Marai Distribution) is 47 min behind committed ETA. Customer notification required.",
      "RFG-302 cold chain excursion logged. Confirm load integrity before KAIA Cargo delivery appointment.",
      "Fuel card anomaly on TRK-108 at Dhahran — SAR 420 off-route transaction pending approval.",
      "TRK-106 PM-A service overdue by 320 km. Do not dispatch from Riyadh Industrial City until cleared.",
    ],
    priorityActions: [
      {
        id: "act-1", title: "Resolve HOS risk — Mohammed Al-Zahrani",
        body: "Driver at 9h 12min drive time. Swap or re-route before SABIC delivery.",
        actionLabel: "Open HOS", entityRoute: "/hos-eld",
      },
      {
        id: "act-2", title: "Send ETA update — SHP-0041",
        body: "Al-Marai dispatch team requires updated ETA. Shipment 47 min behind.",
        actionLabel: "Update ETA", entityRoute: "/active-shipments",
      },
      {
        id: "act-3", title: "Address OOS vehicle — VAN-207",
        body: "Pre-trip defect reported. Create work order and reassign the load.",
        actionLabel: "Create WO", entityRoute: "/work-orders",
      },
      {
        id: "act-4", title: "Approve fuel transaction — TRK-108",
        body: "SAR 420 off-route purchase at Dhahran. Confirm or reject the charge.",
        actionLabel: "Review", entityRoute: "/fuel-idling",
      },
      {
        id: "act-5", title: "Schedule PM service — TRK-106",
        body: "320 km overdue. Block from dispatch queue until service is complete.",
        actionLabel: "Schedule", entityRoute: "/preventive-maintenance",
      },
    ],
    aiBrief: `Fleet readiness is ${fleetOnRoad >= 8 ? "strong" : "moderate"} with ${Math.max(drivingCount, 6)} vehicles in active transit. ${openAlerts > 0 ? `${openAlerts} open alerts require attention before the next dispatch window.` : "No critical alerts — proceed with normal dispatch."}`,
    liveFeed: [
      { id: "f1", category: "dispatch",    title: "SHP-0041 dispatch window missed",         body: "Al-Marai Distribution — TRK-114 delayed",                   time: "4 min ago" },
      { id: "f2", category: "safety",      title: "VAN-207 pre-trip defect reported",        body: "Coolant leak — vehicle flagged OOS",                         time: "11 min ago" },
      { id: "f3", category: "fuel",        title: "Fuel anomaly — TRK-108",                 body: "SAR 420 off-route at Shell Dhahran",                         time: "18 min ago" },
      { id: "f4", category: "maintenance", title: "PM-A overdue — TRK-106",                 body: "320 km past service trigger",                                time: "1 hr ago" },
      { id: "f5", category: "compliance",  title: "HOS risk — Mohammed Al-Zahrani",         body: "9h 12min drive time — approaching limit",                    time: "4 min ago" },
      { id: "f6", category: "safety",      title: "Cold chain excursion — RFG-302",         body: "+3°C for 22 min — load integrity check required",            time: "35 min ago" },
      { id: "f7", category: "dispatch",    title: "TRK-119 assigned — JOB-0488",            body: "Faisal Al-Harthi · Riyadh → Dammam",                         time: "52 min ago" },
      { id: "f8", category: "safety",      title: "DVIR passed — TRK-121",                  body: "Pre-trip inspection clear — no defects",                     time: "1 hr ago" },
    ],
    timeline: [
      { id: "t1", title: "HOS risk — Mohammed Al-Zahrani",   eventType: "compliance.alert",    eventTime: "4 min ago" },
      { id: "t2", title: "VAN-207 OOS — pre-trip defect",     eventType: "safety.event",        eventTime: "11 min ago" },
      { id: "t3", title: "Fuel anomaly — TRK-108",            eventType: "fuel.anomaly",        eventTime: "18 min ago" },
      { id: "t4", title: "Cold chain excursion — RFG-302",    eventType: "safety.event",        eventTime: "35 min ago" },
      { id: "t5", title: "PM-A overdue — TRK-106",            eventType: "maintenance.created", eventTime: "1 hr ago" },
      { id: "t6", title: "TRK-119 dispatched — JOB-0488",     eventType: "dispatch.assigned",   eventTime: "52 min ago" },
    ],
    fleetHealthRisks: [
      { id: 1, entityCode: "KSA-REEFER-119", riskLabel: "Coolant Leak — Breakdown Risk", severity: "Critical", score: 91 },
      { id: 2, entityCode: "BOX-106", riskLabel: "Brake defect — unresolved 18h", severity: "High", score: 78 },
      { id: 3, entityCode: "TRK-114", riskLabel: "PM overdue — 2,400 km past interval", severity: "Medium", score: 61 },
      { id: 4, entityCode: "VAN-207", riskLabel: "GPS offline 8h — Riyadh Ring Road", severity: "Medium", score: 48 },
    ],
    readinessTrend: [88, 89, 91, 90, 92, 93, 94],
  };
}

export function getDispatchBoardData() {
  const mkJob = (
    id: number,
    jobNumber: string,
    customer: string,
    origin: string,
    dest: string,
    vehicle: string,
    riskHeat: string,
    slaWindow: string,
    priority: string,
    extra: Record<string, unknown> = {},
  ) => ({
    id,
    jobNumber,
    customerName: customer,
    pickupAddress: origin,
    dropoffAddress: dest,
    requiredVehicleType: vehicle,
    riskHeat,
    riskHeatScore: riskHeat === "Critical" ? 88 : riskHeat === "High" ? 65 : riskHeat === "Medium" ? 40 : 15,
    slaWindowEnd: slaWindow,
    priority,
    assignmentStatus: "unassigned",
    driverName: null,
    vehicleCode: null,
    matchScore: null,
    ...extra,
  });

  return {
    stageMap: {
      Unassigned: [
        mkJob(1, "JOB-0512", "SABIC Logistics", "Riyadh DC", "Jubail Industrial City", "Semi Truck", "Critical", "14:30 today", "Urgent",
          { slaRisk: "2 h window — driver swap required for TRK-114 HOS breach", flagLabel: "HOS risk" }),
        mkJob(2, "JOB-0514", "Al-Marai Distribution", "Dammam Cold Store", "Riyadh Hypermarket Cluster", "Reefer Truck", "High", "16:00 today", "High",
          { slaRisk: "SHP-0041 already 47 min behind ETA", flagLabel: "SLA breach" }),
        mkJob(3, "JOB-0517", "Gulf Express Logistics", "Jeddah Pharma Hub", "Riyadh Hospital Network", "Reefer Truck", "Medium", "09:00 tomorrow", "Normal", {}),
      ],
      Assigned: [
        mkJob(4, "JOB-0488", "Saudi FMCG Supply Co.", "Riyadh DC", "Dammam Hypermarket Cluster", "Dry Van", "Low", "16:30 today", "Normal",
          { assignmentStatus: "assigned", driverName: "Salman Qureshi", vehicleCode: "KSA-REEFER-214", matchScore: 92 }),
        mkJob(5, "JOB-0491", "DesertCart Fulfillment", "Dubai FC", "Abu Dhabi Zone 3", "Last-mile Van", "Low", "20:00 today", "Normal",
          { assignmentStatus: "assigned", driverName: "Imran Sheikh", vehicleCode: "DXB-VAN-045", matchScore: 88 }),
        mkJob(6, "JOB-0493", "Al Noor Pharma", "Jeddah Pharma Hub", "Riyadh Hospital Network", "Reefer Truck", "Medium", "08:00 tomorrow", "High",
          { assignmentStatus: "assigned", driverName: "Bilal Ansari", vehicleCode: "KSA-REEFER-119", matchScore: 79 }),
      ],
      "En Route Pickup": [
        mkJob(7, "JOB-0478", "Saudi Aramco Supply", "Dammam Port", "SABIC Jubail Plant", "Semi Truck", "Low", "13:00 today", "Normal",
          { assignmentStatus: "en_route_pickup", driverName: "Faisal Al-Mutairi", vehicleCode: "TRK-109", matchScore: 95 }),
        mkJob(8, "JOB-0481", "Al-Marai Distribution", "Jeddah Cold DC", "Makkah Distribution Hub", "Reefer Truck", "Low", "15:00 today", "Normal",
          { assignmentStatus: "en_route_pickup", driverName: "Saeed Al-Ghamdi", vehicleCode: "RFG-302", matchScore: 91 }),
      ],
      "In Transit": [
        mkJob(9,  "JOB-0461", "SABIC Logistics",         "Riyadh DC",        "Jubail Industrial City",      "Semi Truck",    "Low",    "12:00 today", "Normal",
          { assignmentStatus: "in_transit", driverName: "Mohammed Al-Zahrani", vehicleCode: "TRK-114", matchScore: 87, etaMinutes: 82 }),
        mkJob(10, "JOB-0463", "Saudi FMCG",              "Riyadh DC",         "Qassim DC",                  "Dry Van",       "Low",    "14:00 today", "Normal",
          { assignmentStatus: "in_transit", driverName: "Ahmed Al-Dosari",    vehicleCode: "TRK-103", matchScore: 93, etaMinutes: 47 }),
        mkJob(11, "JOB-0467", "Al Noor Pharma",          "Jeddah Pharma Hub", "Riyadh Hospital Network",    "Reefer Truck",  "Medium", "11:30 today", "High",
          { assignmentStatus: "in_transit", driverName: "Khaled Al-Rashidi",  vehicleCode: "TRK-108", matchScore: 81, etaMinutes: 14, slaRisk: "Approaching SLA window" }),
        mkJob(12, "JOB-0471", "DesertCart Fulfillment",  "Dubai FC",          "Sharjah Zone 2",             "Last-mile Van", "Low",    "18:00 today", "Normal",
          { assignmentStatus: "in_transit", driverName: "Omar Al-Harbi",      vehicleCode: "VAN-204", matchScore: 96, etaMinutes: 110 }),
      ],
      "At Delivery": [
        mkJob(13, "JOB-0454", "Gulf Express Logistics", "Manassas VA", "Washington DC", "Box Truck", "Low", "13:00 today", "Normal",
          { assignmentStatus: "arrived_delivery", driverName: "Ana Rivera", vehicleCode: "BOX-106", matchScore: 84 }),
      ],
      Exception: [
        mkJob(14, "JOB-0441", "KAIA Cargo Express", "Jeddah Airport", "Riyadh Cold Hub", "Reefer Truck", "Critical", "09:00 today", "Urgent",
          { assignmentStatus: "exception", driverName: "Saeed Al-Ghamdi", vehicleCode: "RFG-302", exceptionType: "cold_chain_breach", exceptionNote: "Temperature +3°C above set point for 22 min — load integrity at risk" }),
        mkJob(15, "JOB-0445", "Saudi Aramco", "Ras Tanura Refinery", "Jubail Plant", "Semi Truck", "High", "11:00 today", "High",
          { assignmentStatus: "exception", driverName: null, vehicleCode: "VAN-207", exceptionType: "vehicle_breakdown", exceptionNote: "Coolant leak on DVIR — load must be reassigned" }),
      ],
    },
    insights: [
      { type: "warning", message: "3 unassigned loads have SLA windows closing within 3 hours. Auto-suggest available." },
      { type: "info",    message: "TRK-114 driver (Mohammed Al-Zahrani) approaching HOS limit. Consider driver swap for JOB-0461." },
      { type: "danger",  message: "RFG-302 cold chain excursion — confirm load integrity before continuing JOB-0441." },
    ],
  };
}

export function getDevelopmentAvailableDrivers() {
  return [
    { id: 101, driverId: "DRV-KSA-301", fullName: "Salman Qureshi",    availability: "Available",   hosStatus: "OK",   safetyScore: 94, currentCity: "Riyadh",  assignedVehicle: "KSA-REEFER-214", licenseStatus: "Valid" },
    { id: 102, driverId: "DRV-KSA-303", fullName: "Yusuf Al-Qahtani",  availability: "Available",   hosStatus: "OK",   safetyScore: 91, currentCity: "Riyadh",  assignedVehicle: null,              licenseStatus: "Valid" },
    { id: 103, driverId: "DRV-KSA-308", fullName: "Nasser Al-Shehri",  availability: "Available",   hosStatus: "OK",   safetyScore: 89, currentCity: "Madinah", assignedVehicle: null,              licenseStatus: "Valid" },
    { id: 104, driverId: "DRV-KSA-302", fullName: "Bilal Ansari",      availability: "At Pickup",   hosStatus: "Risk", safetyScore: 88, currentCity: "Jeddah",  assignedVehicle: "KSA-REEFER-119",  licenseStatus: "Expiring Soon" },
  ];
}

export function getDevelopmentAvailableVehicles() {
  return [
    { id: 201, vehicleId: "TRK-106",        vehicleCode: "TRK-106",        vehicleType: "Semi Truck",   status: "Parked",  maintenanceStatus: "Overdue",  deviceStatus: "Online",  currentLocation: "Riyadh Industrial City" },
    { id: 202, vehicleId: "VAN-211",        vehicleCode: "VAN-211",        vehicleType: "Van",          status: "Parked",  maintenanceStatus: "Healthy",  deviceStatus: "Online",  currentLocation: "Madinah DC" },
    { id: 203, vehicleId: "TRK-117",        vehicleCode: "TRK-117",        vehicleType: "Semi Truck",   status: "Offline", maintenanceStatus: "Healthy",  deviceStatus: "Offline", currentLocation: "Last: Tabuk Yard" },
    { id: 204, vehicleId: "KSA-REEFER-214", vehicleCode: "KSA-REEFER-214", vehicleType: "Reefer Truck", status: "Driving", maintenanceStatus: "Healthy",  deviceStatus: "Online",  currentLocation: "Riyadh East Ring Road" },
  ];
}

export function getControlTowerData() {
  return {
    generatedAt: new Date().toISOString(),
    kpis: {
      trackedEntities: 14,
      onlineDevices: 12,
      onlineCameras: 8,
      telemetryQuality: "97%",
      highRiskUnits: 3,
      speedAlerts: 2,
    },
    entities: [
      { id: 1,  vehicleCode: "TRK-114",        label: "TRK-114",        status: "Active",          riskLevel: "Critical", liveAlert: "HOS Breach Risk",        lat: 27.0114,  lng: 49.6574,  speedMph: 58,  heading: 45  },
      { id: 2,  vehicleCode: "VAN-207",         label: "VAN-207",        status: "Out of Service",  riskLevel: "Critical", liveAlert: "Coolant Leak",           lat: 24.7136,  lng: 46.6753,  speedMph: 0,   heading: 0   },
      { id: 3,  vehicleCode: "TRK-108",         label: "TRK-108",        status: "Active",          riskLevel: "High",     liveAlert: "Fuel Anomaly",           lat: 26.2945,  lng: 50.2083,  speedMph: 0,   heading: 180 },
      { id: 4,  vehicleCode: "RFG-302",         label: "RFG-302",        status: "Active",          riskLevel: "High",     liveAlert: "Cold Chain Excursion",   lat: 21.6796,  lng: 39.1565,  speedMph: 50,  heading: 90  },
      { id: 5,  vehicleCode: "TRK-106",         label: "TRK-106",        status: "Parked",          riskLevel: "Medium",   liveAlert: "PM Overdue",             lat: 24.7500,  lng: 46.7200,  speedMph: 0,   heading: 0   },
      { id: 6,  vehicleCode: "KSA-REEFER-214",  label: "KSA-REEFER-214", status: "Active",          riskLevel: "Low",      liveAlert: null,                     lat: 24.8200,  lng: 46.7400,  speedMph: 47,  heading: 270 },
      { id: 7,  vehicleCode: "KSA-REEFER-119",  label: "KSA-REEFER-119", status: "Active",          riskLevel: "Low",      liveAlert: null,                     lat: 21.4858,  lng: 39.1925,  speedMph: 42,  heading: 0   },
      { id: 8,  vehicleCode: "TRK-109",         label: "TRK-109",        status: "Active",          riskLevel: "Low",      liveAlert: null,                     lat: 26.3927,  lng: 49.9777,  speedMph: 38,  heading: 135 },
      { id: 9,  vehicleCode: "DXB-VAN-045",     label: "DXB-VAN-045",    status: "Idle",            riskLevel: "Low",      liveAlert: null,                     lat: 25.2048,  lng: 55.2708,  speedMph: 0,   heading: 0   },
      { id: 10, vehicleCode: "VAN-204",          label: "VAN-204",        status: "Idle",            riskLevel: "Low",      liveAlert: null,                     lat: 24.6900,  lng: 46.6600,  speedMph: 0,   heading: 0   },
      { id: 11, vehicleCode: "TRK-103",          label: "TRK-103",        status: "Parked",          riskLevel: "Low",      liveAlert: "Coaching Overdue",       lat: 26.3264,  lng: 43.9750,  speedMph: 0,   heading: 0   },
      { id: 12, vehicleCode: "VAN-211",          label: "VAN-211",        status: "Parked",          riskLevel: "Low",      liveAlert: null,                     lat: 24.4683,  lng: 39.6142,  speedMph: 0,   heading: 0   },
      { id: 13, vehicleCode: "TRK-117",          label: "TRK-117",        status: "Offline",         riskLevel: "Medium",   liveAlert: "No Heartbeat 4h",        lat: 28.3835,  lng: 36.5662,  speedMph: 0,   heading: 0   },
      { id: 14, vehicleCode: "BOX-106",          label: "BOX-106",        status: "Out of Service",  riskLevel: "Medium",   liveAlert: "Camera Offline",         lat: 38.9072,  lng: -77.0369, speedMph: 0,   heading: 0   },
    ],
    geofences: [
      { id: "gf-1", name: "Riyadh DC",          type: "depot",   lat: 24.7136, lng: 46.6753, radius: 0.5 },
      { id: "gf-2", name: "Dammam Port",        type: "port",    lat: 26.3927, lng: 49.9777, radius: 0.8 },
      { id: "gf-3", name: "Jeddah Pharma Hub",  type: "depot",   lat: 21.4858, lng: 39.1925, radius: 0.4 },
      { id: "gf-4", name: "Dubai FC",           type: "depot",   lat: 25.2048, lng: 55.2708, radius: 0.3 },
    ],
    recommendations: [],
    events: [
      { id: "e1", timestamp: "< 1 min ago", title: "HOS Risk — TRK-114",        body: "Mohammed Al-Zahrani at 9h 12min drive time",  severity: "Critical" },
      { id: "e2", timestamp: "11 min ago",  title: "OOS — VAN-207",             body: "Coolant leak on pre-trip DVIR",               severity: "Critical" },
      { id: "e3", timestamp: "18 min ago",  title: "Fuel Anomaly — TRK-108",    body: "Off-route purchase SAR 420 at Dhahran",       severity: "Warning"  },
      { id: "e4", timestamp: "35 min ago",  title: "Cold Chain — RFG-302",      body: "Temperature +3°C above set point 22 min",     severity: "Warning"  },
      { id: "e5", timestamp: "8 min ago",   title: "Speed Alert — KSA-REEFER-214", body: "94 km/h in 80 zone near Riyadh East",    severity: "Warning"  },
      { id: "e6", timestamp: "52 min ago",  title: "Dispatched — TRK-109 / JOB-0488", body: "Faisal Al-Mutairi en route to Dammam Port", severity: "Info" },
    ],
    actionQueue: [
      { id: "a1", title: "Resolve HOS — TRK-114",          priority: "Critical", body: "Driver swap or re-route before SABIC delivery." },
      { id: "a2", title: "Reassign load — VAN-207",         priority: "Critical", body: "Vehicle OOS — coolant defect on DVIR." },
      { id: "a3", title: "Approve fuel txn — TRK-108",      priority: "High",     body: "SAR 420 off-route purchase needs confirmation." },
      { id: "a4", title: "Confirm cold chain — RFG-302",    priority: "High",     body: "Excursion +3°C. Verify load integrity." },
      { id: "a5", title: "Schedule PM — TRK-106",           priority: "Medium",   body: "320 km overdue. Block from dispatch queue." },
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

// ── Fleet Health seed ─────────────────────────────────────────────────────────

export function getFleetHealthSummary() {
  return {
    overallHealthScore: 78,
    vehicleHealthScore: 82,
    driverHealthScore: 74,
    totalVehicles: 14,
    vehiclesAtRisk: 3,
    totalDrivers: 8,
    driversAtRisk: 2,
    openDefects: 4,
    pmDueCount: 2,
    openFaultCodes: 6,
    pendingWorkOrders: 3,
    generatedAt: new Date().toISOString(),
    insights: [
      "KSA-REEFER-119 has an active coolant fault — pull from dispatch queue until resolved.",
      "TRK-114 driver is within 4h of 70-hr HOS cycle — pre-assign relief driver for tomorrow.",
      "BOX-106 brake defect is unacknowledged for 18h — escalate to maintenance team.",
    ],
  };
}

export function getFleetHealthRisks() {
  return [
    { id: 1, entityType: "vehicle", entityCode: "KSA-REEFER-119", entityLabel: "KSA-REEFER-119 · Refrigerated Truck", riskCategory: "Mechanical", severity: "Critical", riskLabel: "Coolant Leak — Imminent Breakdown Risk", score: 91, recommendedAction: "Pull from service immediately", status: "Active" },
    { id: 2, entityType: "vehicle", entityCode: "BOX-106", entityLabel: "BOX-106 · Box Truck", riskCategory: "Safety Defect", severity: "High", riskLabel: "Brake system defect — unresolved 18h", score: 78, recommendedAction: "Block from dispatch until inspected", status: "Active" },
    { id: 3, entityType: "driver", entityCode: "DRV-001", entityLabel: "Salman Qureshi", riskCategory: "HOS", severity: "High", riskLabel: "70-hr cycle: 66.5h used — 3.5h remaining", score: 74, recommendedAction: "Reassign tomorrow load to available driver", status: "Active" },
    { id: 4, entityType: "vehicle", entityCode: "TRK-114", entityLabel: "TRK-114 · Heavy Truck", riskCategory: "Maintenance", severity: "Medium", riskLabel: "PM overdue — 2,400 km past service interval", score: 61, recommendedAction: "Schedule oil change and safety inspection this week", status: "Active" },
    { id: 5, entityType: "driver", entityCode: "DRV-004", entityLabel: "Bilal Ansari", riskCategory: "Compliance", severity: "Medium", riskLabel: "Medical certificate expires in 12 days", score: 55, recommendedAction: "Book DOT medical exam before expiry", status: "Watch" },
    { id: 6, entityType: "vehicle", entityCode: "VAN-207", entityLabel: "VAN-207 · Cargo Van", riskCategory: "Telematics", severity: "Medium", riskLabel: "GPS device offline 8h — last known Riyadh Ring Road", score: 48, recommendedAction: "Dispatch driver check-in request", status: "Watch" },
  ];
}

// ── Maintenance dashboard seed ────────────────────────────────────────────────

export function getMaintenanceDashboard() {
  return {
    kpis: {
      fleetAvailabilityPct: 79,
      vehiclesOutOfService: 2,
      criticalOpenDefects: 2,
      openWorkOrders: 3,
      overduePm: 2,
      estimatedBacklogCost: 12400,
      avgResolutionHours: 18.4,
    },
    openDefects: [
      { id: 1, vehicleCode: "BOX-106", defectCode: "DEF-701", category: "Brakes", severity: "Critical", description: "Brake system fault — driver reported grinding noise", reportedAt: "2026-06-21T08:30:00Z", status: "Open", hoursOpen: 18 },
      { id: 2, vehicleCode: "KSA-REEFER-119", defectCode: "DEF-702", category: "Engine", severity: "High", description: "Coolant temperature high — possible leak", reportedAt: "2026-06-21T14:15:00Z", status: "Open", hoursOpen: 12 },
      { id: 3, vehicleCode: "TRK-114", defectCode: "DEF-703", category: "Tires", severity: "Medium", description: "Front left tire wear at 15% tread — replacement due", reportedAt: "2026-06-20T09:00:00Z", status: "Acknowledged", hoursOpen: 36 },
      { id: 4, vehicleCode: "VAN-207", defectCode: "DEF-704", category: "Electrical", severity: "Low", description: "Interior dome light inoperative", reportedAt: "2026-06-19T16:00:00Z", status: "Scheduled", hoursOpen: 54 },
    ],
    duePm: [
      { id: 1, vehicleCode: "TRK-114", pmType: "Oil & Filter Change", triggerType: "mileage", overdueMiles: 2400, scheduledAt: "2026-06-24T09:00:00Z", estimatedCost: 650 },
      { id: 2, vehicleCode: "DHS-VAN-045", pmType: "Annual Safety Inspection", triggerType: "calendar", daysOverdue: 5, scheduledAt: "2026-06-23T08:00:00Z", estimatedCost: 280 },
    ],
    recentWorkOrders: [
      { id: 1, woCode: "WO-3301", vehicleCode: "BOX-106", serviceType: "Brake Inspection & Repair", priority: "Critical", status: "In Progress", assignedTo: "Ali Technician", estimatedCost: 3200, scheduledAt: "2026-06-22T08:00:00Z" },
      { id: 2, woCode: "WO-3302", vehicleCode: "KSA-REEFER-119", serviceType: "Cooling System Flush", priority: "High", status: "Pending Parts", assignedTo: "Khalid Technician", estimatedCost: 1800, scheduledAt: "2026-06-22T10:00:00Z" },
      { id: 3, woCode: "WO-3303", vehicleCode: "TRK-114", serviceType: "Oil Change + PM Service", priority: "Medium", status: "Scheduled", assignedTo: null, estimatedCost: 650, scheduledAt: "2026-06-24T09:00:00Z" },
    ],
    recentInspections: [
      { id: 1, vehicleCode: "KSA-REEFER-214", driverName: "Salman Qureshi", inspectionType: "Pre-trip", result: "Pass", dvirStatus: "Satisfactory", completedAt: "2026-06-22T05:30:00Z" },
      { id: 2, vehicleCode: "BOX-106", driverName: "Imran Sheikh", inspectionType: "Pre-trip", result: "Fail", dvirStatus: "Defect Reported", completedAt: "2026-06-21T06:00:00Z" },
      { id: 3, vehicleCode: "VAN-211", driverName: "Yusuf Al-Qahtani", inspectionType: "Post-trip", result: "Pass", dvirStatus: "Satisfactory", completedAt: "2026-06-21T18:45:00Z" },
    ],
    insights: [
      { type: "danger", message: "BOX-106 brake defect has been open 18h without a work order — assign immediately to avoid HOS violation." },
      { type: "warning", message: "KSA-REEFER-119 coolant fault may trigger cold chain excursion if not resolved before next load." },
      { type: "info", message: "TRK-114 PM is 2,400 km overdue — schedule during next available slot to avoid voiding warranty." },
    ],
    faultCodes: [
      { id: 1, vehicleCode: "KSA-REEFER-119", dtcCode: "P0128", description: "Coolant Temperature Below Thermostat Regulating Temperature", severity: "High", status: "Active", firstSeenAt: "2026-06-21T14:10:00Z" },
      { id: 2, vehicleCode: "BOX-106", dtcCode: "C0035", description: "Left Front Wheel Speed Sensor Circuit", severity: "Critical", status: "Active", firstSeenAt: "2026-06-21T07:55:00Z" },
    ],
  };
}

// ── Routes seed ───────────────────────────────────────────────────────────────

export function getRoutesListData() {
  return [
    { id: 1, routeCode: "RTE-8101", routeName: "Riyadh DC → Jubail Industrial City", region: "Eastern Province, KSA", driverName: "Salman Qureshi", vehicleCode: "KSA-REEFER-214", stops: 3, plannedStart: "2026-06-22T06:00:00Z", plannedEnd: "2026-06-22T14:00:00Z", status: "Active", estimatedDurationMinutes: 480, efficiency: 91, highRisk: false, costEstimate: 1400 },
    { id: 2, routeCode: "RTE-8102", routeName: "Jeddah Port → Makkah Distribution Hub", region: "Western Province, KSA", driverName: "Yusuf Al-Qahtani", vehicleCode: "VAN-211", stops: 2, plannedStart: "2026-06-22T07:30:00Z", plannedEnd: "2026-06-22T11:00:00Z", status: "Active", estimatedDurationMinutes: 210, efficiency: 87, highRisk: false, costEstimate: 820 },
    { id: 3, routeCode: "RTE-8103", routeName: "Dubai Logistics Village → Abu Dhabi Free Zone", region: "UAE", driverName: "Nasser Al-Shehri", vehicleCode: "TRK-114", stops: 4, plannedStart: "2026-06-22T08:00:00Z", plannedEnd: "2026-06-22T16:30:00Z", status: "Planned", estimatedDurationMinutes: 510, efficiency: 84, highRisk: true, costEstimate: 1950 },
    { id: 4, routeCode: "RTE-8104", routeName: "Manassas Distribution → Northern Virginia Stops", region: "Virginia, USA", driverName: "Ana Rivera", vehicleCode: "BOX-106", stops: 6, plannedStart: "2026-06-22T05:00:00Z", plannedEnd: "2026-06-22T13:00:00Z", status: "Completed", estimatedDurationMinutes: 480, efficiency: 95, highRisk: false, costEstimate: 1100 },
  ];
}

export function getRoutesSummary() {
  return {
    totalRoutesToday: 4,
    activeRoutes: 2,
    plannedRoutes: 1,
    completedRoutes: 1,
    delayedRoutes: 0,
    avgStops: 3.75,
    avgRouteEta: "7h 45m",
    efficiency: 89,
    highRisk: 1,
    costEstimate: 5270,
  };
}

// ── Fuel & Expenses seed ─────────────────────────────────────────────────────

export function getFuelSummary() {
  return {
    fuel_spend_today: 4820, fuel_spend_this_month: 87340,
    idle_cost_today: 390, fuel_transactions: 14,
    fuel_anomalies: 2, high_idle_vehicles: 3,
    cost_per_gallon: 3.89, estimated_savings_opportunity: 6200,
  };
}

export function getFuelTransactions() {
  return [
    { id: 1, transactionNumber: "FUL-3301", vehicleCode: "TRK-114", driverName: "Nasser Al-Shehri", fuelType: "Diesel", quantity: 120, unitPrice: 3.89, totalCost: 466.80, odometer: 142800, fuelStation: "ADNOC Station 7 — Abu Dhabi", paymentMethod: "Fleet Card", anomalyStatus: "Normal", fuelDate: "2026-06-22" },
    { id: 2, transactionNumber: "FUL-3302", vehicleCode: "KSA-REEFER-119", driverName: "Bilal Ansari", fuelType: "Diesel", quantity: 180, unitPrice: 3.89, totalCost: 700.20, odometer: 98400, fuelStation: "Aramco Fill Center — Riyadh N", paymentMethod: "Fleet Card", anomalyStatus: "Anomaly", fuelDate: "2026-06-22" },
    { id: 3, transactionNumber: "FUL-3303", vehicleCode: "VAN-211", driverName: "Yusuf Al-Qahtani", fuelType: "Gasoline", quantity: 55, unitPrice: 3.75, totalCost: 206.25, odometer: 67300, fuelStation: "Shell Jeddah Ring Road", paymentMethod: "Driver Card", anomalyStatus: "Normal", fuelDate: "2026-06-21" },
    { id: 4, transactionNumber: "FUL-3304", vehicleCode: "BOX-106", driverName: "Ana Rivera", fuelType: "Diesel", quantity: 90, unitPrice: 3.85, totalCost: 346.50, odometer: 213700, fuelStation: "Pilot Travel Center — Manassas VA", paymentMethod: "Fleet Card", anomalyStatus: "Normal", fuelDate: "2026-06-21" },
    { id: 5, transactionNumber: "FUL-3305", vehicleCode: "KSA-REEFER-214", driverName: "Salman Qureshi", fuelType: "Diesel", quantity: 150, unitPrice: 3.89, totalCost: 583.50, odometer: 115200, fuelStation: "Aramco Fill Center — Jubail", paymentMethod: "Fleet Card", anomalyStatus: "Normal", fuelDate: "2026-06-21" },
  ];
}

export function getExpensesSummary() {
  return {
    total_expenses_this_month: 14, pending_approval: 5,
    approved_expenses: 6, rejected_expenses: 1,
    unusual_expenses: 2, missing_receipts: 3,
    average_expense_amount: 487, total: 14,
  };
}

export function getExpensesList() {
  return [
    { id: 1, expenseNumber: "EXP-7701", categoryName: "Tolls & Road Fees", amount: 120, approvalStatus: "Approved", receiptStatus: "Attached", vendorName: "NHAI Toll", vehicleCode: "TRK-114", driverName: "Nasser Al-Shehri", riskScore: 10, expenseDate: "2026-06-20", recommendedAction: null },
    { id: 2, expenseNumber: "EXP-7702", categoryName: "Maintenance Parts", amount: 890, approvalStatus: "Pending", receiptStatus: "Missing", vendorName: "Bin Saedan AutoParts", vehicleCode: "KSA-REEFER-119", driverName: null, riskScore: 62, expenseDate: "2026-06-21", recommendedAction: "Request receipt from vendor" },
    { id: 3, expenseNumber: "EXP-7703", categoryName: "Driver Allowance", amount: 300, approvalStatus: "Approved", receiptStatus: "Attached", vendorName: null, vehicleCode: null, driverName: "Bilal Ansari", riskScore: 5, expenseDate: "2026-06-19", recommendedAction: null },
    { id: 4, expenseNumber: "EXP-7704", categoryName: "Fuel Advance", amount: 700, approvalStatus: "Pending", receiptStatus: "Missing", vendorName: null, vehicleCode: "VAN-211", driverName: "Yusuf Al-Qahtani", riskScore: 74, expenseDate: "2026-06-22", recommendedAction: "Unusual amount — review before approval" },
    { id: 5, expenseNumber: "EXP-7705", categoryName: "Parking Fees", amount: 45, approvalStatus: "Approved", receiptStatus: "Attached", vendorName: "Parking Corp", vehicleCode: "BOX-106", driverName: "Ana Rivera", riskScore: 8, expenseDate: "2026-06-20", recommendedAction: null },
    { id: 6, expenseNumber: "EXP-7706", categoryName: "Loading / Unloading", amount: 250, approvalStatus: "Pending", receiptStatus: "Attached", vendorName: "Jubail Port Services", vehicleCode: "KSA-REEFER-214", driverName: "Salman Qureshi", riskScore: 22, expenseDate: "2026-06-21", recommendedAction: null },
  ];
}

// ── Safety seed ──────────────────────────────────────────────────────────────

export function getSafetySummary() {
  return {
    fleetSafetyScore: 84, safetyEventsToday: 3, criticalEvents: 1,
    harshBraking: 18, harshAcceleration: 14, speedingEvents: 9,
    routeDeviation: 2, distractedDrivingEvents: 5,
    coachingNeeded: 6, openIncidents: 2, reviewedEvents: 11, preventableRiskScore: 72,
  };
}

export function getSafetyEvents() {
  return [
    { id: 1, eventNumber: "EVT-5501", eventType: "Harsh Braking", severity: "High", driverName: "Yusuf Al-Qahtani", vehicleCode: "VAN-211", jobNumber: "JOB-0512", routeCode: "RTE-8102", locationDescription: "Jeddah Ring Road, Exit 12", speed: 78, occurredAt: "2026-06-22T07:14:00Z", reviewStatus: "Under Review", coachingStatus: "Pending", incidentStatus: null, riskScore: 72, recommendedAction: "Review dashcam clip and assign coaching task" },
    { id: 2, eventNumber: "EVT-5502", eventType: "Speeding", severity: "Critical", driverName: "Bilal Ansari", vehicleCode: "KSA-REEFER-119", jobNumber: "JOB-0514", routeCode: "RTE-8102", locationDescription: "Riyadh–Dammam Expressway KM 142", speed: 112, occurredAt: "2026-06-22T09:30:00Z", reviewStatus: "Open", coachingStatus: null, incidentStatus: null, riskScore: 91, recommendedAction: "Immediate speed policy review with driver" },
    { id: 3, eventNumber: "EVT-5503", eventType: "Distracted Driving", severity: "High", driverName: "Salman Qureshi", vehicleCode: "KSA-REEFER-214", jobNumber: "JOB-0517", routeCode: "RTE-8101", locationDescription: "Jubail Industrial City Gate 3", speed: 55, occurredAt: "2026-06-21T14:22:00Z", reviewStatus: "Reviewed", coachingStatus: "Assigned", incidentStatus: null, riskScore: 65, recommendedAction: "Coaching task assigned — follow up in 7 days" },
    { id: 4, eventNumber: "EVT-5504", eventType: "Late Delivery", severity: "Medium", driverName: "Ana Rivera", vehicleCode: "BOX-106", jobNumber: "JOB-0519", routeCode: "RTE-8104", locationDescription: "Manassas Distribution Center", speed: 0, occurredAt: "2026-06-22T11:45:00Z", reviewStatus: "Open", coachingStatus: null, incidentStatus: "Under Review", riskScore: 42, recommendedAction: "Acknowledge and update customer ETA" },
  ];
}

export function getDashcamSummary() {
  return {
    dashcamEventsToday: 4, criticalVideoEvents: 1, pendingReview: 3,
    reviewedEvents: 8, falsePositives: 1, coachingCreated: 2,
    evidencePackages: 1, collisionNearMiss: 0,
    distractedDrivingEvents: 2, tailgatingEvents: 1,
    speedingVideoEvents: 3, driverExonerations: 0,
  };
}

export function getDashcamEvents() {
  return [
    { id: 1, eventNumber: "CAM-2201", eventType: "Distracted Driving", severity: "High", driverName: "Salman Qureshi", vehicleCode: "KSA-REEFER-214", jobNumber: "JOB-0517", routeCode: "RTE-8101", locationDescription: "Jubail Industrial Gate 3", occurredAt: "2026-06-21T14:22:00Z", videoProvider: "Samsara", aiConfidence: "94%", reviewStatus: "Reviewed", evidenceStatus: "Collected", recommendedAction: "Coaching task created — clip saved" },
    { id: 2, eventNumber: "CAM-2202", eventType: "Harsh Braking", severity: "High", driverName: "Yusuf Al-Qahtani", vehicleCode: "VAN-211", jobNumber: "JOB-0512", routeCode: "RTE-8102", locationDescription: "Jeddah Ring Road Exit 12", occurredAt: "2026-06-22T07:14:00Z", videoProvider: "Motive", aiConfidence: "88%", reviewStatus: "Pending Review", evidenceStatus: "Pending", recommendedAction: "Review clip and assign coaching" },
    { id: 3, eventNumber: "CAM-2203", eventType: "Speeding", severity: "Critical", driverName: "Bilal Ansari", vehicleCode: "KSA-REEFER-119", jobNumber: "JOB-0514", routeCode: "RTE-8102", locationDescription: "Riyadh–Dammam Expressway KM 142", occurredAt: "2026-06-22T09:30:00Z", videoProvider: "Samsara", aiConfidence: "97%", reviewStatus: "Pending Review", evidenceStatus: "Pending", recommendedAction: "Escalate to safety manager — critical speed violation" },
    { id: 4, eventNumber: "CAM-2204", eventType: "Tailgating", severity: "Medium", driverName: "Nasser Al-Shehri", vehicleCode: "TRK-114", jobNumber: "JOB-0516", routeCode: "RTE-8103", locationDescription: "Dubai–Abu Dhabi Highway E11", occurredAt: "2026-06-22T08:55:00Z", videoProvider: "Lytx", aiConfidence: "79%", reviewStatus: "Pending Review", evidenceStatus: "Pending", recommendedAction: "Review clip — possible false positive" },
  ];
}

export function getCoachingSummary() {
  return {
    openCoachingTasks: 6, criticalCoaching: 1, assignedTasks: 4,
    driverAcknowledged: 2, completedThisMonth: 8,
    overdueCoaching: 1, repeatCoachingDrivers: 2,
    safetyScoreImproved: 3, escalatedCoaching: 1, averageCompletionTime: "4.2 days",
  };
}

export function getCoachingTasks() {
  return [
    { id: 1, taskNumber: "COACH-801", driverName: "Bilal Ansari", coachingType: "Speed Policy", priority: "Critical", status: "Open", assignedToName: "Safety Manager", driverAcknowledged: false, beforeSafetyScore: 68, afterSafetyScore: null, effectivenessScore: null, dueAt: "2026-06-24T09:00:00Z" },
    { id: 2, taskNumber: "COACH-802", driverName: "Yusuf Al-Qahtani", coachingType: "Braking Behavior", priority: "High", status: "Assigned", assignedToName: "Safety Manager", driverAcknowledged: false, beforeSafetyScore: 74, afterSafetyScore: null, effectivenessScore: null, dueAt: "2026-06-25T09:00:00Z" },
    { id: 3, taskNumber: "COACH-803", driverName: "Salman Qureshi", coachingType: "Distracted Driving", priority: "High", status: "Acknowledged", assignedToName: "Safety Manager", driverAcknowledged: true, beforeSafetyScore: 82, afterSafetyScore: null, effectivenessScore: null, dueAt: "2026-06-27T09:00:00Z" },
    { id: 4, taskNumber: "COACH-804", driverName: "Nasser Al-Shehri", coachingType: "Following Distance", priority: "Medium", status: "Assigned", assignedToName: "Safety Manager", driverAcknowledged: false, beforeSafetyScore: 79, afterSafetyScore: null, effectivenessScore: null, dueAt: "2026-06-28T09:00:00Z" },
  ];
}

// ── HOS driver clocks seed ───────────────────────────────────────────────────

export function getHosDriverClocks() {
  return [
    { id: 1, driver_name: "Salman Qureshi",    driver_code: "DRV-KSA-301", vehicle_code: "KSA-REEFER-214", cycle_type: "70h/8-day", country_code: "SA", status: "OK",        drive_time_remaining_minutes: 396, shift_time_remaining_minutes: 504, cycle_time_remaining_minutes: 3570, hos_warning: null },
    { id: 2, driver_name: "Yusuf Al-Qahtani",  driver_code: "DRV-KSA-302", vehicle_code: "VAN-211",         cycle_type: "70h/8-day", country_code: "SA", status: "Warning",   drive_time_remaining_minutes: 120, shift_time_remaining_minutes: 180, cycle_time_remaining_minutes: 540,  hos_warning: "Approaching 70-hr cycle limit — 9h remaining" },
    { id: 3, driver_name: "Nasser Al-Shehri",  driver_code: "DRV-UAE-141", vehicle_code: "TRK-114",         cycle_type: "60h/7-day", country_code: "AE", status: "OK",        drive_time_remaining_minutes: 528, shift_time_remaining_minutes: 660, cycle_time_remaining_minutes: 2400, hos_warning: null },
    { id: 4, driver_name: "Bilal Ansari",       driver_code: "DRV-KSA-302", vehicle_code: "KSA-REEFER-119", cycle_type: "70h/8-day", country_code: "SA", status: "Vehicle Blocked", drive_time_remaining_minutes: 0,   shift_time_remaining_minutes: 0,   cycle_time_remaining_minutes: 0,    hos_warning: "Vehicle blocked — HOS limit exceeded. Must rest before next dispatch." },
  ];
}

// ── Report datasets seed ──────────────────────────────────────────────────────

export function getReportDatasets() {
  const ops = ["eq", "neq", "contains", "gt", "lt", "gte", "lte", "in"];
  const str = ["eq", "neq", "contains", "in"];
  const num = ["eq", "neq", "gt", "lt", "gte", "lte"];
  const dt  = ["eq", "gt", "lt", "gte", "lte", "between"];

  const field = (key: string, label: string, type: "string" | "number" | "date" | "boolean" | "enum", allowedOperators = ops, sortable = true, exportable = true) =>
    ({ key, label, type, allowedOperators, sortable, groupable: type !== "date", exportable });

  return [
    {
      key: "fleet_vehicles",
      label: "Fleet Vehicles",
      requiredPermission: "vehicles:view",
      fields: [
        field("vehicleCode", "Vehicle Code", "string", str),
        field("type", "Type", "enum", str),
        field("status", "Status", "enum", str),
        field("fleetReadinessScore", "Readiness Score", "number", num),
        field("riskHeatScore", "Risk Score", "number", num),
        field("assignedDriver", "Assigned Driver", "string", str),
        field("lastPmDate", "Last PM Date", "date", dt),
        field("odometerMiles", "Odometer (miles)", "number", num),
      ],
    },
    {
      key: "drivers",
      label: "Drivers",
      requiredPermission: "drivers:view",
      fields: [
        field("driverCode", "Driver Code", "string", str),
        field("fullName", "Full Name", "string", str),
        field("status", "Status", "enum", str),
        field("driverReadinessScore", "Readiness Score", "number", num),
        field("safetyScore", "Safety Score", "number", num),
        field("complianceScore", "Compliance Score", "number", num),
        field("riskHeatScore", "Risk Score", "number", num),
        field("hosHoursRemaining", "HOS Hours Remaining", "number", num),
        field("assignedVehicle", "Assigned Vehicle", "string", str),
      ],
    },
    {
      key: "dispatch_jobs",
      label: "Dispatch Jobs",
      requiredPermission: "dispatch:view",
      fields: [
        field("jobNumber", "Job Number", "string", str),
        field("customerName", "Customer", "string", str),
        field("assignmentStatus", "Stage", "enum", str),
        field("priority", "Priority", "enum", str),
        field("riskHeat", "Risk Heat", "enum", str),
        field("slaWindowEnd", "SLA Window End", "date", dt),
        field("driverName", "Driver", "string", str),
        field("vehicleCode", "Vehicle", "string", str),
        field("pickupAddress", "Pickup", "string", str),
        field("dropoffAddress", "Dropoff", "string", str),
      ],
    },
    {
      key: "maintenance_events",
      label: "Maintenance Events",
      requiredPermission: "maintenance:view",
      fields: [
        field("vehicleCode", "Vehicle", "string", str),
        field("serviceType", "Service Type", "string", str),
        field("status", "Status", "enum", str),
        field("priority", "Priority", "enum", str),
        field("estimatedCost", "Est. Cost", "number", num),
        field("scheduledAt", "Scheduled Date", "date", dt),
        field("completedAt", "Completed Date", "date", dt),
        field("assignedTo", "Assigned To", "string", str),
      ],
    },
    {
      key: "compliance_violations",
      label: "Compliance Violations",
      requiredPermission: "compliance:view",
      fields: [
        field("violationCode", "Violation Code", "string", str),
        field("driverCode", "Driver", "string", str),
        field("vehicleCode", "Vehicle", "string", str),
        field("violationType", "Type", "enum", str),
        field("severity", "Severity", "enum", str),
        field("status", "Status", "enum", str),
        field("country", "Country", "enum", str),
        field("reportedAt", "Reported Date", "date", dt),
      ],
    },
    {
      key: "telematics_gps",
      label: "GPS & Telematics",
      requiredPermission: "telematics:gps:view",
      fields: [
        field("vehicleCode", "Vehicle", "string", str),
        field("driverName", "Driver", "string", str),
        field("lat", "Latitude", "number", num),
        field("lng", "Longitude", "number", num),
        field("speedMph", "Speed (mph)", "number", num),
        field("engineStatus", "Engine Status", "enum", str),
        field("signalStrength", "Signal Strength", "number", num),
        field("lastPing", "Last Ping", "date", dt),
        field("routeCode", "Route", "string", str),
      ],
    },
  ];
}

// ── Audit log seed ────────────────────────────────────────────────────────────

export function getAuditLogs() {
  return [
    { id: 1, created_at: "2026-06-22T11:45:00Z", actor_name: "Super Admin", action_name: "user.login", entity_name: "User", entity_id: 1, module_key: "command-center", severity: "Info" },
    { id: 2, created_at: "2026-06-22T11:30:00Z", actor_name: "Safety Manager", action_name: "safety.event.review", entity_name: "EVT-5501", entity_id: 5501, module_key: "safety", severity: "High" },
    { id: 3, created_at: "2026-06-22T11:15:00Z", actor_name: "Fleet Manager", action_name: "maintenance.work_order.update", entity_name: "WO-4410", entity_id: 4410, module_key: "maintenance", severity: "Info" },
    { id: 4, created_at: "2026-06-22T10:58:00Z", actor_name: "CRM & Sales Ma...", action_name: "contract.status.update", entity_name: "CON-1002", entity_id: 1002, module_key: "finance", severity: "Warning" },
    { id: 5, created_at: "2026-06-22T10:44:00Z", actor_name: "Super Admin", action_name: "user.role.assign", entity_name: "Fleet Manager", entity_id: 4, module_key: "admin", severity: "High" },
    { id: 6, created_at: "2026-06-22T09:30:00Z", actor_name: "Operations User", action_name: "dispatch.job.create", entity_name: "JOB-0520", entity_id: 520, module_key: "dispatch", severity: "Info" },
    { id: 7, created_at: "2026-06-22T09:00:00Z", actor_name: "Safety Manager", action_name: "coaching.task.create", entity_name: "COACH-801", entity_id: 801, module_key: "safety", severity: "Info" },
    { id: 8, created_at: "2026-06-21T18:22:00Z", actor_name: "Fleet Manager", action_name: "vehicle.status.update", entity_name: "TRK-114", entity_id: 114, module_key: "fleet", severity: "Warning" },
    { id: 9, created_at: "2026-06-21T17:55:00Z", actor_name: "Tenant Admin", action_name: "settings.integration.connect", entity_name: "QuickBooks Online", entity_id: null, module_key: "admin", severity: "Info" },
    { id: 10, created_at: "2026-06-21T16:40:00Z", actor_name: "Safety Manager", action_name: "incident.create", entity_name: "INC-4101", entity_id: 4101, module_key: "safety", severity: "Critical" },
  ];
}
