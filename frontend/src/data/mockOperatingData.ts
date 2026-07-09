import type { AnyRecord } from "@/types";

export const customers = [
  { id: "CUS-KSA-001", companyName: "Gulf Express Logistics", industry: "3PL", country: "Saudi Arabia", city: "Riyadh", primaryContact: "Noura Al-Faisal", activeContracts: 3, monthlyShipments: 420, revenueMtd: 1280000, currency: "SAR", healthScore: 91, status: "Healthy", accountManager: "Maya Patel", renewalDate: "2026-09-30" },
  { id: "CUS-KSA-002", companyName: "Riyadh Cold Chain", industry: "Cold Chain", country: "Saudi Arabia", city: "Riyadh", primaryContact: "Fahad Al-Otaibi", activeContracts: 2, monthlyShipments: 265, revenueMtd: 920000, currency: "SAR", healthScore: 86, status: "Growth Opportunity", accountManager: "Omar Khan", renewalDate: "2026-07-15" },
  { id: "CUS-AE-003", companyName: "DesertCart Fulfillment", industry: "E-commerce Fulfillment", country: "UAE", city: "Dubai", primaryContact: "Layla Haddad", activeContracts: 2, monthlyShipments: 610, revenueMtd: 780000, currency: "AED", healthScore: 79, status: "At Risk", accountManager: "Sofia Cruz", renewalDate: "2026-06-20" },
  { id: "CUS-KSA-004", companyName: "Al Noor Pharma Distribution", industry: "Pharma Distribution", country: "Saudi Arabia", city: "Jeddah", primaryContact: "Dr. Sami Rahman", activeContracts: 1, monthlyShipments: 180, revenueMtd: 610000, currency: "SAR", healthScore: 74, status: "High Risk", accountManager: "Maya Patel", renewalDate: "2026-05-31" },
  { id: "CUS-KSA-005", companyName: "Saudi FMCG Supply Co.", industry: "FMCG", country: "Saudi Arabia", city: "Dammam", primaryContact: "Abdulaziz Mansour", activeContracts: 4, monthlyShipments: 730, revenueMtd: 1660000, currency: "SAR", healthScore: 94, status: "Healthy", accountManager: "Omar Khan", renewalDate: "2027-01-15" },
  { id: "CUS-US-006", companyName: "UPS Contract Partner KSA", industry: "Contract Logistics", country: "United States", city: "Atlanta", primaryContact: "Erin Matthews", activeContracts: 2, monthlyShipments: 335, revenueMtd: 410000, currency: "USD", healthScore: 88, status: "Healthy", accountManager: "Avery Stone", renewalDate: "2026-11-01" },
];

export const leads = [
  { leadId: "LD-2401", company: "Jeddah Fresh Foods", contactPerson: "Hassan Bari", industry: "FMCG", source: "LinkedIn", estimatedMonthlyLoads: 96, requiredService: "Reefer FTL", cityCountry: "Jeddah, KSA", status: "Discovery Scheduled", assignedRep: "Maya Patel", nextFollowUp: "2026-05-28" },
  { leadId: "LD-2402", company: "Dammam Industrial Parts", contactPerson: "Aisha Noor", industry: "Manufacturing", source: "Referral", estimatedMonthlyLoads: 140, requiredService: "Flatbed", cityCountry: "Dammam, KSA", status: "Proposal Needed", assignedRep: "Omar Khan", nextFollowUp: "2026-05-29" },
  { leadId: "LD-2403", company: "Dubai Beauty Express", contactPerson: "Mariam Khalifa", industry: "E-commerce", source: "Campaign", estimatedMonthlyLoads: 220, requiredService: "Last Mile", cityCountry: "Dubai, UAE", status: "Qualified", assignedRep: "Sofia Cruz", nextFollowUp: "2026-06-02" },
  { leadId: "LD-2404", company: "Riyadh Medical Labs", contactPerson: "Yousef Saleh", industry: "Healthcare", source: "Cold Chain Promotion", estimatedMonthlyLoads: 70, requiredService: "Temperature Controlled", cityCountry: "Riyadh, KSA", status: "Contacted", assignedRep: "Maya Patel", nextFollowUp: "2026-05-30" },
];

export const opportunities = [
  { opportunityId: "OPP-9001", customerLead: "Jeddah Fresh Foods", estimatedContractValue: 2400000, currency: "SAR", expectedLoadsMonth: 96, probability: 62, expectedCloseDate: "2026-06-18", competitor: "Motive reseller", stage: "Rate Proposal Sent", owner: "Maya Patel" },
  { opportunityId: "OPP-9002", customerLead: "Dammam Industrial Parts", estimatedContractValue: 3100000, currency: "SAR", expectedLoadsMonth: 140, probability: 48, expectedCloseDate: "2026-07-03", competitor: "Local broker TMS", stage: "Requirements Collected", owner: "Omar Khan" },
  { opportunityId: "OPP-9003", customerLead: "Dubai Beauty Express", estimatedContractValue: 1280000, currency: "AED", expectedLoadsMonth: 220, probability: 72, expectedCloseDate: "2026-06-11", competitor: "In-house dispatch", stage: "Negotiation", owner: "Sofia Cruz" },
  { opportunityId: "OPP-9004", customerLead: "Riyadh Medical Labs", estimatedContractValue: 1850000, currency: "SAR", expectedLoadsMonth: 70, probability: 55, expectedCloseDate: "2026-06-25", competitor: "Cold chain boutique", stage: "Discovery", owner: "Maya Patel" },
];

export const campaigns = [
  { campaignName: "Cold Chain Summer Readiness", segment: "Pharma + Fresh Food", channel: "WhatsApp", status: "Active", audienceSize: 540, openRate: "72%", responseRate: "14%", leadsGenerated: 18, revenueInfluenced: 980000, currency: "SAR", startDate: "2026-05-01" },
  { campaignName: "New Riyadh-Dammam Lane Launch", segment: "FMCG", channel: "Email", status: "Active", audienceSize: 820, openRate: "46%", responseRate: "9%", leadsGenerated: 26, revenueInfluenced: 1220000, currency: "SAR", startDate: "2026-05-10" },
  { campaignName: "Dormant Account Win-back", segment: "Inactive Accounts", channel: "SMS", status: "Scheduled", audienceSize: 310, openRate: "0%", responseRate: "0%", leadsGenerated: 0, revenueInfluenced: 0, currency: "SAR", startDate: "2026-06-01" },
];

export const contracts = [
  { contractId: "CON-1001", customer: "Saudi FMCG Supply Co.", serviceType: "FTL + Cross Dock", startDate: "2026-01-01", endDate: "2027-01-15", sla: "98% on-time", currency: "SAR", billingTerms: "Net 30", renewalStatus: "Stable", status: "Active" },
  { contractId: "CON-1002", customer: "Al Noor Pharma Distribution", serviceType: "Cold Chain", startDate: "2025-06-01", endDate: "2026-05-31", sla: "99% temp compliant", currency: "SAR", billingTerms: "Net 15", renewalStatus: "At Risk", status: "Expiring Soon" },
  { contractId: "CON-1003", customer: "DesertCart Fulfillment", serviceType: "Last Mile", startDate: "2025-09-01", endDate: "2026-06-20", sla: "Same-day 92%", currency: "AED", billingTerms: "Weekly", renewalStatus: "Under Renewal", status: "Under Renewal" },
];

export const rateCards = [
  { rateCardId: "RC-7001", customerContract: "CON-1001 / Saudi FMCG", originZone: "Riyadh DC", destinationZone: "Dammam Retail", vehicleType: "Dry Van", pricingMethod: "Per KM", baseRate: 950, perKmRate: 5.8, fuelSurcharge: "9%", currency: "SAR", effectiveFrom: "2026-01-01", effectiveTo: "2027-01-15", status: "Active" },
  { rateCardId: "RC-7002", customerContract: "CON-1002 / Al Noor Pharma", originZone: "Jeddah Pharma Hub", destinationZone: "Riyadh Hospitals", vehicleType: "Reefer", pricingMethod: "Fixed Trip", baseRate: 4200, perKmRate: 0, fuelSurcharge: "12%", currency: "SAR", effectiveFrom: "2025-06-01", effectiveTo: "2026-05-31", status: "Expiring Soon" },
  { rateCardId: "RC-7003", customerContract: "Spot / DesertCart", originZone: "Dubai FC", destinationZone: "Abu Dhabi", vehicleType: "Last-mile Van", pricingMethod: "Zone Based", baseRate: 380, perKmRate: 2.4, fuelSurcharge: "7%", currency: "AED", effectiveFrom: "2026-04-01", effectiveTo: "2026-08-31", status: "Active" },
];

export const quotations = [
  { quoteId: "QT-5001", customer: "Jeddah Fresh Foods", origin: "Jeddah", destination: "Riyadh", cargo: "Fresh produce", quoteAmount: 3900, currency: "SAR", margin: "24%", validUntil: "2026-06-05", status: "Sent" },
  { quoteId: "QT-5002", customer: "Riyadh Medical Labs", origin: "Riyadh", destination: "Dammam", cargo: "Lab samples", quoteAmount: 5200, currency: "SAR", margin: "31%", validUntil: "2026-06-03", status: "Draft" },
  { quoteId: "QT-5003", customer: "Dubai Beauty Express", origin: "Dubai", destination: "Abu Dhabi", cargo: "Cosmetics parcels", quoteAmount: 860, currency: "AED", margin: "18%", validUntil: "2026-05-31", status: "Accepted" },
];

export const bookings = [
  { bookingId: "BK-8101", customer: "Saudi FMCG Supply Co.", contract: "CON-1001", pickup: "Riyadh DC", dropoff: "Dammam Hypermarket Cluster", cargoType: "Packaged FMCG", weight: "18,400 kg", vehicleRequired: "Dry Van", pickupDateTime: "2026-05-26 07:30", deliveryDeadline: "2026-05-26 16:30", quoteAmount: 6200, currency: "SAR", status: "Awaiting Dispatch" },
  { bookingId: "BK-8102", customer: "Al Noor Pharma Distribution", contract: "CON-1002", pickup: "Jeddah Pharma Hub", dropoff: "Riyadh Hospital Network", cargoType: "Vaccines", weight: "4,200 kg", vehicleRequired: "Reefer", pickupDateTime: "2026-05-26 09:00", deliveryDeadline: "2026-05-27 08:00", quoteAmount: 9800, currency: "SAR", status: "Confirmed" },
  { bookingId: "BK-8103", customer: "DesertCart Fulfillment", contract: "CON-1003", pickup: "Dubai FC", dropoff: "Abu Dhabi Zone 3", cargoType: "Parcel bags", weight: "1,100 kg", vehicleRequired: "Last-mile Van", pickupDateTime: "2026-05-26 12:00", deliveryDeadline: "2026-05-26 20:00", quoteAmount: 2100, currency: "AED", status: "Assigned" },
  { bookingId: "BK-8104", customer: "Gulf Express Logistics", contract: "CON-1004", pickup: "Manassas, VA", dropoff: "Washington DC", cargoType: "Retail replenishment", weight: "7,900 kg", vehicleRequired: "Box Truck", pickupDateTime: "2026-05-26 08:30", deliveryDeadline: "2026-05-26 13:00", quoteAmount: 2400, currency: "USD", status: "Quoted" },
];

export const shipments = [
  { shipmentId: "SHP-6201", bookingId: "BK-8101", customer: "Saudi FMCG Supply Co.", origin: "Riyadh", destination: "Dammam", currentStatus: "In Transit", vehicle: "KSA-REEFER-214", driver: "Salman Qureshi", eta: "2026-05-26 15:55", delayRisk: "Low", podStatus: "Pending", invoiceStatus: "Not Invoiced", cargoType: "Packaged FMCG", revenue: 6200, cost: 4700, currency: "SAR", slaRisk: "Low" },
  { shipmentId: "SHP-6202", bookingId: "BK-8102", customer: "Al Noor Pharma Distribution", origin: "Jeddah", destination: "Riyadh", currentStatus: "At Pickup", vehicle: "KSA-REEFER-119", driver: "Bilal Ansari", eta: "2026-05-27 07:45", delayRisk: "Medium", podStatus: "Pending", invoiceStatus: "Not Invoiced", cargoType: "Vaccines", revenue: 9800, cost: 7200, currency: "SAR", slaRisk: "Temperature Watch" },
  { shipmentId: "SHP-6203", bookingId: "BK-8103", customer: "DesertCart Fulfillment", origin: "Dubai", destination: "Abu Dhabi", currentStatus: "Delivered", vehicle: "DXB-VAN-045", driver: "Imran Sheikh", eta: "Delivered 18:42", delayRisk: "Low", podStatus: "Uploaded", invoiceStatus: "Ready", cargoType: "Parcels", revenue: 2100, cost: 1720, currency: "AED", slaRisk: "Low" },
  { shipmentId: "SHP-6204", bookingId: "BK-8104", customer: "Gulf Express Logistics", origin: "Manassas", destination: "Washington DC", currentStatus: "Delayed", vehicle: "BOX-106", driver: "Ana Rivera", eta: "2026-05-26 14:10", delayRisk: "High", podStatus: "Pending", invoiceStatus: "Not Invoiced", cargoType: "Retail", revenue: 2400, cost: 2210, currency: "USD", slaRisk: "High" },
];

export const vehicles = [
  { vehicleId: "KSA-REEFER-214", vin: "3AKJGLDR9KSK21490", plateNumber: "RHD-2147", makeModel: "Freightliner M2 Reefer", year: 2023, vehicleType: "Reefer Truck", owner: "Company Owned", assignedDriver: "Salman Qureshi", assignedDevice: "GPS-994102", currentLocation: "Riyadh East", odometer: 84210, fuelType: "Diesel", reefer: "Yes", registrationExpiry: "2027-03-14", insuranceExpiry: "2027-01-31", maintenanceStatus: "Healthy", complianceStatus: "Compliant", status: "Active" },
  { vehicleId: "KSA-REEFER-119", vin: "1FUJGLDR7KSK11988", plateNumber: "JED-1192", makeModel: "Mercedes Actros Reefer", year: 2021, vehicleType: "Reefer Truck", owner: "Lease", assignedDriver: "Bilal Ansari", assignedDevice: "GPS-994118", currentLocation: "Jeddah Pharma Hub", odometer: 164920, fuelType: "Diesel", reefer: "Yes", registrationExpiry: "2026-07-02", insuranceExpiry: "2026-08-19", maintenanceStatus: "Due Soon", complianceStatus: "Compliant", status: "Active" },
  { vehicleId: "DXB-VAN-045", vin: "WD3PE8CD2KP045103", plateNumber: "DXB-045", makeModel: "Mercedes Sprinter", year: 2022, vehicleType: "Last-mile Van", owner: "Company Owned", assignedDriver: "Imran Sheikh", assignedDevice: "GPS-884501", currentLocation: "Dubai FC", odometer: 98140, fuelType: "Diesel", reefer: "No", registrationExpiry: "2026-12-01", insuranceExpiry: "2026-12-01", maintenanceStatus: "Healthy", complianceStatus: "Compliant", status: "Idle" },
  { vehicleId: "BOX-106", vin: "VINOPSTRAX000106", plateNumber: "VA-106", makeModel: "Ford Transit Box", year: 2015, vehicleType: "Box Truck", owner: "Company Owned", assignedDriver: "Ana Rivera", assignedDevice: "GPS-554106", currentLocation: "Washington DC", odometer: 268400, fuelType: "Diesel", reefer: "No", registrationExpiry: "2026-06-20", insuranceExpiry: "2026-07-15", maintenanceStatus: "Critical", complianceStatus: "Review", status: "In Maintenance" },
];

export const drivers = [
  { driverId: "DRV-KSA-301", name: "Salman Qureshi", phone: "+966 55 410 3011", licenseNumber: "KSA-DL-301", licenseExpiry: "2028-02-14", assignedVehicle: "KSA-REEFER-214", availability: "Available", hosStatus: "OK", safetyScore: 94, activeShipments: 1, currentCity: "Riyadh", status: "Active" },
  { driverId: "DRV-KSA-302", name: "Bilal Ansari", phone: "+966 56 220 3022", licenseNumber: "KSA-DL-302", licenseExpiry: "2026-06-05", assignedVehicle: "KSA-REEFER-119", availability: "At Pickup", hosStatus: "Risk", safetyScore: 88, activeShipments: 1, currentCity: "Jeddah", status: "Review" },
  { driverId: "DRV-AE-141", name: "Imran Sheikh", phone: "+971 50 410 0141", licenseNumber: "UAE-DL-141", licenseExpiry: "2027-11-10", assignedVehicle: "DXB-VAN-045", availability: "Idle", hosStatus: "OK", safetyScore: 91, activeShipments: 0, currentCity: "Dubai", status: "Active" },
  { driverId: "DRV-US-017", name: "Ana Rivera", phone: "+1 571 430 5317", licenseNumber: "VA-D0017", licenseExpiry: "2027-01-11", assignedVehicle: "BOX-106", availability: "Blocked", hosStatus: "OK", safetyScore: 91, activeShipments: 1, currentCity: "Washington DC", status: "Vehicle Blocked" },
];

export const devices = [
  { deviceId: "GPS-994102", type: "GPS Tracker", vendor: "Queclink", model: "GV75", imei: "352099410299410", simIccid: "899661102000994102", firmware: "4.18.2", linkedVehicleTrailer: "KSA-REEFER-214", lastHeartbeat: "2026-05-26 10:44", battery: "91%", signal: "Strong", gpsStatus: "Locked", dataQuality: "98%", status: "Online" },
  { deviceId: "REEFER-TS-119", type: "Temperature Sensor", vendor: "Sensitech", model: "TempTale Ultra", imei: "359881190109112", simIccid: "899661102000119882", firmware: "2.9.1", linkedVehicleTrailer: "KSA-REEFER-119", lastHeartbeat: "2026-05-26 10:37", battery: "47%", signal: "Weak", gpsStatus: "Linked", dataQuality: "84%", status: "Weak Signal" },
  { deviceId: "CAM-VA-106", type: "AI Dashcam", vendor: "Lytx-ready", model: "AI DualCam", imei: "359881060010600", simIccid: "89014103211118510720", firmware: "1.6.0", linkedVehicleTrailer: "BOX-106", lastHeartbeat: "2026-05-26 08:10", battery: "Vehicle", signal: "No Upload", gpsStatus: "Last known", dataQuality: "52%", status: "Camera Not Recording" },
];

export const expenses = [
  { expenseId: "EXP-3001", date: "2026-05-20", vehicle: "KSA-REEFER-214", driver: "Salman Qureshi", shipmentTrip: "SHP-6201", category: "Fuel", vendor: "Aldrees", description: "Diesel refill", amount: 1180, currency: "SAR", taxVat: 177, receipt: "Attached", paymentMethod: "Fuel Card", approvalStatus: "Approved", approvedBy: "Finance" },
  { expenseId: "EXP-3002", date: "2026-05-21", vehicle: "BOX-106", driver: "Ana Rivera", shipmentTrip: "SHP-6204", category: "Maintenance", vendor: "DC Fleet Repair", description: "Brake diagnosis", amount: 740, currency: "USD", taxVat: 44, receipt: "Attached", paymentMethod: "Corporate Card", approvalStatus: "Pending Approval", approvedBy: "--" },
];

export const invoices = [
  { invoiceId: "INV-8801", customer: "DesertCart Fulfillment", shipmentBooking: "SHP-6203", invoiceDate: "2026-05-26", dueDate: "2026-06-02", amount: 2100, tax: 105, total: 2205, currency: "AED", paymentStatus: "Ready", aging: "0 days" },
  { invoiceId: "INV-8802", customer: "Saudi FMCG Supply Co.", shipmentBooking: "SHP-6201", invoiceDate: "2026-05-26", dueDate: "2026-06-25", amount: 6200, tax: 930, total: 7130, currency: "SAR", paymentStatus: "Draft", aging: "0 days" },
];

export const incidents = [
  { incidentId: "INC-4101", dateTime: "2026-05-26 08:42", vehicle: "BOX-106", driver: "Ana Rivera", shipment: "SHP-6204", incidentType: "Late Delivery", severity: "High", evidenceAvailable: "GPS + telematics", coachingRequired: "No", status: "Under Review" },
  { incidentId: "INC-4102", dateTime: "2026-05-26 09:20", vehicle: "KSA-REEFER-119", driver: "Bilal Ansari", shipment: "SHP-6202", incidentType: "Temperature Breach", severity: "Critical", evidenceAvailable: "Sensor + route", coachingRequired: "Yes", status: "Evidence Collected" },
];

export const maintenance = [
  { workOrderId: "WO-7101", vehicle: "BOX-106", issueType: "Brake system", priority: "Critical", assignedWorkshop: "DC Fleet Repair", estimatedCost: 1800, currency: "USD", status: "In Progress", createdDate: "2026-05-23", dueDate: "2026-05-27" },
  { workOrderId: "WO-7102", vehicle: "KSA-REEFER-119", issueType: "Reefer calibration", priority: "High", assignedWorkshop: "Jeddah Cold Service", estimatedCost: 2400, currency: "SAR", status: "Scheduled", createdDate: "2026-05-25", dueDate: "2026-05-29" },
];

export const serviceHistory = [
  { id: 1, workOrderCode: "WO-6801", vehicleCode: "BOX-106", serviceType: "Brake pad replacement", vendorName: "DC Fleet Repair", priority: "Critical", cost: 1820, currency: "USD", downtimeHours: 6.0, completedAt: "2026-05-10", issueType: "Worn brake pads detected during DVIR", technicianName: "James Holden", partsReplaced: "Brake pads (front axle)" },
  { id: 2, workOrderCode: "WO-6802", vehicleCode: "KSA-REEFER-119", serviceType: "Reefer unit calibration", vendorName: "Jeddah Cold Service", priority: "High", cost: 2400, currency: "SAR", downtimeHours: 4.5, completedAt: "2026-05-12", issueType: "Temperature deviation on cold chain run", technicianName: "Tariq Al-Saud", partsReplaced: "Thermostat sensor" },
  { id: 3, workOrderCode: "WO-6803", vehicleCode: "DXB-VAN-045", serviceType: "Oil change + filter", vendorName: "Internal", priority: "Normal", cost: 180, currency: "AED", downtimeHours: 1.5, completedAt: "2026-05-14", issueType: "Scheduled 10K service", technicianName: "Ravi Kumar", partsReplaced: "Oil filter, engine oil" },
  { id: 4, workOrderCode: "WO-6804", vehicleCode: "KSA-REEFER-214", serviceType: "Tire rotation and alignment", vendorName: "Riyadh Tire Center", priority: "Medium", cost: 640, currency: "SAR", downtimeHours: 2.0, completedAt: "2026-05-16", issueType: "Uneven tire wear noticed on inspection", technicianName: "Khalid Omar", partsReplaced: "—" },
  { id: 5, workOrderCode: "WO-6805", vehicleCode: "BOX-106", serviceType: "Transmission fluid flush", vendorName: "DC Fleet Repair", priority: "Medium", cost: 420, currency: "USD", downtimeHours: 3.0, completedAt: "2026-05-18", issueType: "Hard shifting reported by driver", technicianName: "James Holden", partsReplaced: "Transmission fluid" },
  { id: 6, workOrderCode: "WO-6806", vehicleCode: "KSA-REEFER-119", serviceType: "Reefer compressor overhaul", vendorName: "Jeddah Cold Service", priority: "Critical", cost: 4800, currency: "SAR", downtimeHours: 8.0, completedAt: "2026-05-20", issueType: "Compressor failure mid-route", technicianName: "Tariq Al-Saud", partsReplaced: "Compressor clutch, relay" },
  { id: 7, workOrderCode: "WO-6807", vehicleCode: "DXB-VAN-045", serviceType: "Battery replacement", vendorName: "Internal", priority: "High", cost: 320, currency: "AED", downtimeHours: 1.0, completedAt: "2026-05-21", issueType: "Slow crank, battery voltage low", technicianName: "Ravi Kumar", partsReplaced: "12V battery" },
  { id: 8, workOrderCode: "WO-6808", vehicleCode: "KSA-REEFER-214", serviceType: "AC system recharge", vendorName: "Riyadh Tire Center", priority: "Normal", cost: 280, currency: "SAR", downtimeHours: 1.5, completedAt: "2026-05-22", issueType: "Cabin AC not cooling adequately", technicianName: "Khalid Omar", partsReplaced: "Refrigerant R134a" },
  { id: 9, workOrderCode: "WO-6809", vehicleCode: "BOX-106", serviceType: "Engine diagnostic scan", vendorName: "DC Fleet Repair", priority: "High", cost: 350, currency: "USD", downtimeHours: 2.5, completedAt: "2026-05-23", issueType: "Check engine light on", technicianName: "James Holden", partsReplaced: "—" },
  { id: 10, workOrderCode: "WO-6810", vehicleCode: "KSA-REEFER-119", serviceType: "Coolant system flush", vendorName: "Jeddah Cold Service", priority: "Medium", cost: 520, currency: "SAR", downtimeHours: 2.0, completedAt: "2026-05-24", issueType: "Coolant level dropping slowly", technicianName: "Tariq Al-Saud", partsReplaced: "Coolant, hose clamp" },
];

export const downtimeEvents = [
  { id: 1, vehicleCode: "BOX-106", downtimeReason: "Brake failure — rear axle", startDate: "2026-05-10 06:00", endDate: "2026-05-10 12:00", durationHours: 6.0, affectedSystem: "Braking", resolutionDescription: "Replaced rear brake pads and resurfaced rotors", costImpact: 1820, revenueLoss: 1680, priority: "Critical", status: "Resolved" },
  { id: 2, vehicleCode: "KSA-REEFER-119", downtimeReason: "Reefer compressor malfunction", startDate: "2026-05-12 08:30", endDate: "2026-05-12 16:30", durationHours: 8.0, affectedSystem: "Refrigeration", resolutionDescription: "Overhauled compressor, replaced clutch and relay", costImpact: 4800, revenueLoss: 4200, priority: "Critical", status: "Resolved" },
  { id: 3, vehicleCode: "DXB-VAN-045", downtimeReason: "Battery depletion", startDate: "2026-05-14 07:00", endDate: "2026-05-14 08:30", durationHours: 1.5, affectedSystem: "Electrical", resolutionDescription: "Replaced 12V battery and tested charging system", costImpact: 320, revenueLoss: 420, priority: "High", status: "Resolved" },
  { id: 4, vehicleCode: "KSA-REEFER-214", downtimeReason: "Tire blowout — steer axle", startDate: "2026-05-16 14:00", endDate: "2026-05-16 16:00", durationHours: 2.0, affectedSystem: "Tires / Wheels", resolutionDescription: "Replaced steer tire, checked alignment", costImpact: 640, revenueLoss: 560, priority: "High", status: "Resolved" },
  { id: 5, vehicleCode: "BOX-106", downtimeReason: "Transmission slipping", startDate: "2026-05-18 09:00", endDate: "2026-05-18 12:00", durationHours: 3.0, affectedSystem: "Powertrain", resolutionDescription: "Flushed transmission fluid, replaced filter", costImpact: 420, revenueLoss: 840, priority: "Medium", status: "Resolved" },
  { id: 6, vehicleCode: "KSA-REEFER-119", downtimeReason: "Engine overheating", startDate: "2026-05-20 11:00", endDate: "2026-05-20 13:00", durationHours: 2.0, affectedSystem: "Cooling", resolutionDescription: "Flushed coolant system, replaced thermostat", costImpact: 520, revenueLoss: 1100, priority: "High", status: "Resolved" },
  { id: 7, vehicleCode: "BOX-106", downtimeReason: "Electrical short — tail lights", startDate: "2026-05-23 07:30", endDate: "2026-05-23 10:00", durationHours: 2.5, affectedSystem: "Electrical", resolutionDescription: "Repaired wiring harness, replaced fuse", costImpact: 350, revenueLoss: 700, priority: "Medium", status: "Resolved" },
  { id: 8, vehicleCode: "KSA-REEFER-214", downtimeReason: "GPS module failure", startDate: "2026-05-24 10:00", endDate: "2026-05-24 11:30", durationHours: 1.5, affectedSystem: "Telematics", resolutionDescription: "Replaced GPS antenna and rebooted module", costImpact: 180, revenueLoss: 420, priority: "Normal", status: "Resolved" },
];

export const supportTickets = [
  { ticketId: "TCK-2201", customer: "Al Noor Pharma Distribution", shipment: "SHP-6202", issueType: "Temperature Breach", priority: "Critical", slaTimer: "01:42 remaining", assignedTeam: "Cold Chain Support", status: "Open", createdDate: "2026-05-26" },
  { ticketId: "TCK-2202", customer: "DesertCart Fulfillment", shipment: "SHP-6203", issueType: "Missing POD", priority: "Medium", slaTimer: "05:10 remaining", assignedTeam: "Customer Ops", status: "In Progress", createdDate: "2026-05-26" },
];

export const mockData: Record<string, AnyRecord[]> = {
  customers,
  leads,
  opportunities,
  campaigns,
  contracts,
  rateCards,
  quotations,
  bookings,
  shipments,
  vehicles,
  drivers,
  devices,
  expenses,
  invoices,
  incidents,
  maintenance,
  serviceHistory,
  downtimeEvents,
  supportTickets,
};
