import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  AlertTriangle,
  Building2,
  Calculator,
  CheckCircle2,
  ClipboardCheck,
  DollarSign,
  Download,
  MapPinned,
  PackageCheck,
  Radar,
  Search,
  Send,
  Sparkles,
  X,
  Users,
} from "lucide-react";
import type { LucideIcon } from "lucide-react";
import {
  AiInsightCard,
  DataTable,
  DetailDrawer,
  EmptyState,
  KpiCard,
  PageHeader,
  RiskBadge,
  StatusBadge,
  LoadingState,
} from "@/components/ui";
import { alertsApi } from "@/services/alertsApi";
import { useHasPermission } from "@/hooks/usePermission";
import type { AnyRecord } from "@/types";
import { calculateCustomerHealth, calculateProfitability, formatCurrency, formatDate } from "@/utils/formatters";
import { useNavigate } from "react-router";

// This generic shell has no executable backend contract. It must remain empty in
// every environment: development convenience data previously leaked into the
// production bundle and made unwired modules look operational. The type query is
// erased by TypeScript; there is deliberately no runtime import of the seed module.
type DevelopmentSeedShape = typeof import("@/data/developmentFleetSeedData").developmentFleetSeedData;
const seed = ({
  bookings: [], campaigns: [], contracts: [], customers: [], devices: [],
  drivers: [], expenses: [], incidents: [], invoices: [], leads: [],
  maintenance: [], opportunities: [], quotations: [], rateCards: [],
  shipments: [], supportTickets: [], vehicles: [], alerts: [],
} as unknown as DevelopmentSeedShape);

const {
  bookings, campaigns, contracts, customers, devices, drivers, expenses,
  incidents, invoices, leads, maintenance, opportunities, quotations,
  rateCards, shipments, supportTickets, vehicles,
} = seed;

type ModuleDefinition = {
  title: string;
  eyebrow: string;
  description: string;
  rows: AnyRecord[];
  columns: string[];
  kpis: { label: string; value: string | number; status?: string; trend?: string }[];
  insight: string;
};

type AlertRecord = AnyRecord & {
  alertId: string;
  title?: string;
  body?: string;
  category?: string;
  type?: string;
  alertType?: string;
  entity?: string;
  entityType?: string;
  entityRoute?: string;
  customer?: string;
  severity?: string;
  owner?: string;
  location?: string;
  age?: string;
  recommendedAction?: string;
  status?: string;
  createdAt?: string;
  acknowledgedAt?: string;
  acknowledgedBy?: string;
  closedAt?: string;
};

function normalizeAlert(raw: AnyRecord): AlertRecord {
  return {
    ...raw,
    alertId: String(raw.alertId ?? raw.id ?? ""),
    title: String(raw.title ?? raw.type ?? "Alert"),
    body: String(raw.body ?? ""),
    category: String(raw.category ?? "Operations"),
    type: raw.type != null ? String(raw.type) : undefined,
    alertType: raw.alertType != null ? String(raw.alertType) : raw.alert_type != null ? String(raw.alert_type) : undefined,
    entity: raw.entity != null ? String(raw.entity) : undefined,
    entityType: raw.entityType != null ? String(raw.entityType) : raw.entity_type != null ? String(raw.entity_type) : undefined,
    entityRoute: raw.entityRoute != null ? String(raw.entityRoute) : raw.entity_route != null ? String(raw.entity_route) : undefined,
    customer: raw.customer != null ? String(raw.customer) : undefined,
    severity: String(raw.severity ?? "Info"),
    owner: raw.owner != null ? String(raw.owner) : undefined,
    location: raw.location != null ? String(raw.location) : undefined,
    age: raw.age != null ? String(raw.age) : undefined,
    recommendedAction: raw.recommendedAction != null ? String(raw.recommendedAction) : raw.recommended_action != null ? String(raw.recommended_action) : undefined,
    status: String(raw.status ?? "Open"),
    createdAt: raw.createdAt != null ? String(raw.createdAt) : raw.created_at != null ? String(raw.created_at) : undefined,
    acknowledgedAt: raw.acknowledgedAt != null ? String(raw.acknowledgedAt) : raw.acknowledged_at != null ? String(raw.acknowledged_at) : undefined,
    acknowledgedBy: raw.acknowledgedBy != null ? String(raw.acknowledgedBy) : raw.acknowledged_by != null ? String(raw.acknowledged_by) : undefined,
    closedAt: raw.closedAt != null ? String(raw.closedAt) : raw.closed_at != null ? String(raw.closed_at) : undefined,
  };
}

function ageHours(createdAt?: string) {
  if (!createdAt) return 0;
  const created = new Date(createdAt).getTime();
  if (Number.isNaN(created)) return 0;
  return Math.max(0, (Date.now() - created) / 3_600_000);
}

const routePlans: AnyRecord[] = [];

const proofOfDelivery = shipments.map((shipment, index) => ({
  podId: `POD-${9200 + index}`,
  shipment: shipment.shipmentId,
  customer: shipment.customer,
  deliveredAt: shipment.currentStatus === "Delivered" ? "2026-05-26 18:42" : "Pending",
  receiverName: shipment.currentStatus === "Delivered" ? "Warehouse receiving desk" : "Awaiting receiver",
  signature: shipment.currentStatus === "Delivered" ? "Captured" : "Pending",
  photo: shipment.currentStatus === "Delivered" ? "Attached" : "Pending",
  gpsVerified: shipment.currentStatus === "Delivered" ? "Yes" : "No",
  damageFlag: "No",
  status: shipment.podStatus,
}));

const followUps = leads.map((lead) => ({
  followUpId: `FU-${lead.leadId.replace("LD-", "")}`,
  account: lead.company,
  owner: lead.assignedRep,
  reason: lead.status,
  dueDate: lead.nextFollowUp,
  channel: lead.source === "LinkedIn" ? "LinkedIn" : "Phone",
  nextAction: lead.status === "Proposal Needed" ? "Send rate proposal" : "Confirm discovery notes",
  status: "Open",
}));

const renewals = contracts.map((contract) => ({
  contract: contract.contractId,
  customer: contract.customer,
  expiryDate: contract.endDate,
  revenue: contract.customer.includes("Pharma") ? 610000 : 1280000,
  margin: contract.customer.includes("Pharma") ? "14%" : "27%",
  renewalProbability: contract.renewalStatus === "At Risk" ? "42%" : "78%",
  customerHealth: contract.renewalStatus === "At Risk" ? "High Risk" : "Healthy",
  accountManager: contract.customer.includes("DesertCart") ? "Sofia Cruz" : "Maya Patel",
  status: contract.renewalStatus,
}));

const upsell = customers.map((customer) => ({
  account: customer.companyName,
  currentService: customer.industry,
  signal: customer.status === "Growth Opportunity" ? "Lane expansion" : "Cross-sell watch",
  recommendedOffer: customer.industry.includes("Cold") || customer.industry.includes("Pharma") ? "Cold chain compliance pack" : "Control tower visibility add-on",
  expectedValue: formatCurrency(Math.round(Number(customer.revenueMtd) * 0.18), String(customer.currency)),
  owner: customer.accountManager,
  status: customer.status === "High Risk" ? "Retention First" : "Qualified",
}));

const alerts = seed.alerts;

const accountHealth = customers.map((customer) => ({
  customer: customer.companyName,
  healthScore: calculateCustomerHealth(customer),
  revenueTrend: customer.status === "High Risk" ? "Down 11%" : "Up 8%",
  shipmentVolumeTrend: customer.monthlyShipments > 400 ? "Growing" : "Stable",
  complaintCount: customer.status === "High Risk" ? 7 : 2,
  paymentRisk: customer.status === "High Risk" ? "High" : "Low",
  slaPerformance: customer.status === "High Risk" ? "91%" : "97%",
  renewalDate: customer.renewalDate,
  accountManager: customer.accountManager,
  riskLevel: customer.status,
  recommendedAction: customer.status === "High Risk" ? "Executive recovery call" : "Offer lane expansion",
}));

function valueSum(rows: AnyRecord[], key: string) {
  return rows.reduce((sum, row) => sum + Number(row[key] ?? 0), 0);
}

const moduleDefinitions: Record<string, ModuleDefinition> = {
  "active-shipments": {
    title: "Active Shipments",
    eyebrow: "Control Tower",
    description: "Live customer freight movement with SLA, ETA, proof, invoice and margin posture in one operating board.",
    rows: shipments,
    columns: ["shipmentId", "customer", "origin", "destination", "currentStatus", "vehicle", "driver", "eta", "delayRisk", "podStatus", "invoiceStatus"],
    kpis: [
      { label: "Active Shipments", value: shipments.filter((s) => s.currentStatus !== "Delivered").length, status: "Active" },
      { label: "Delayed Shipments", value: shipments.filter((s) => s.currentStatus === "Delayed").length, status: "Risk" },
      { label: "POD Pending", value: shipments.filter((s) => s.podStatus === "Pending").length, status: "Pending" },
      { label: "Revenue In Motion", value: formatCurrency(valueSum(shipments, "revenue"), "SAR"), status: "Healthy" },
    ],
    insight: "SHP-6204 has high SLA risk and low margin. Send revised ETA and protect BOX-106 from additional dispatch until brake work is cleared.",
  },
  alerts: {
    title: "Alerts",
    eyebrow: "Control Tower",
    description: "Cross-module exceptions across temperature, telematics, late freight, maintenance, safety and fuel leakage.",
    rows: alerts,
    columns: ["alertId", "type", "entity", "customer", "severity", "owner", "recommendedAction", "status"],
    kpis: [
      { label: "Critical Alerts", value: alerts.filter((a) => a.severity === "Critical").length, status: "Critical" },
      { label: "Open Alerts", value: alerts.filter((a) => a.status === "Open").length, status: "Open" },
      { label: "Customer Impact", value: `${new Set(alerts.map((a) => a.customer).filter(Boolean)).size} accounts`, status: "Risk" },
      { label: "Triage Confidence", value: "—", status: "AI" },
    ],
    insight: "Temperature breach and camera outage are linked to customer-sensitive lanes. Prioritize customer communication before internal investigation closes.",
  },
  leads: {
    title: "Leads",
    eyebrow: "CRM & Growth",
    description: "New prospect demand, service fit, sales ownership and next follow-up discipline.",
    rows: leads,
    columns: ["leadId", "company", "contactPerson", "industry", "source", "estimatedMonthlyLoads", "requiredService", "cityCountry", "status", "assignedRep", "nextFollowUp"],
    kpis: [
      { label: "Open Leads", value: leads.length, status: "Active" },
      { label: "Qualified Pipeline", value: leads.filter((l) => /Qualified|Proposal/.test(l.status)).length, status: "Healthy" },
      { label: "Monthly Loads Potential", value: valueSum(leads, "estimatedMonthlyLoads"), status: "Growth" },
      { label: "Follow-ups Due", value: leads.length, status: "Pending" },
    ],
    insight: "Cold chain leads have the highest conversion signal because OPSTRAX can combine reefer visibility, compliance documents and customer ETA into one offer.",
  },
  "sales-pipeline": {
    title: "Sales Pipeline",
    eyebrow: "CRM & Growth",
    description: "Opportunity stages from new lead through contract drafting and won/lost outcomes.",
    rows: opportunities,
    columns: ["opportunityId", "customerLead", "estimatedContractValue", "expectedLoadsMonth", "probability", "expectedCloseDate", "competitor", "stage", "owner"],
    kpis: [
      { label: "Pipeline Value", value: formatCurrency(valueSum(opportunities, "estimatedContractValue"), "SAR"), status: "Healthy" },
      { label: "Weighted Value", value: formatCurrency(opportunities.reduce((s, o) => s + Number(o.estimatedContractValue) * Number(o.probability) / 100, 0), "SAR"), status: "Active" },
      { label: "Negotiations", value: opportunities.filter((o) => o.stage === "Negotiation").length, status: "Pending" },
      { label: "Competitor Mentions", value: opportunities.filter((o) => o.competitor).length, status: "Review" },
    ],
    insight: "Dubai Beauty Express is closest to conversion. Use last-mile proof of delivery and customer ETA portal as the differentiator against in-house dispatch.",
  },
  opportunities: {
    title: "Opportunities",
    eyebrow: "CRM & Growth",
    description: "Commercial deal register with value, probability, expected load volume and competitor context.",
    rows: opportunities,
    columns: ["opportunityId", "customerLead", "estimatedContractValue", "currency", "expectedLoadsMonth", "probability", "expectedCloseDate", "competitor", "stage", "owner"],
    kpis: [
      { label: "Opportunities", value: opportunities.length, status: "Active" },
      { label: "Avg Probability", value: `${Math.round(valueSum(opportunities, "probability") / opportunities.length)}%`, status: "Healthy" },
      { label: "Expected Loads", value: valueSum(opportunities, "expectedLoadsMonth"), status: "Growth" },
      { label: "Close This Month", value: opportunities.filter((o) => String(o.expectedCloseDate).startsWith("2026-06")).length, status: "Pending" },
    ],
    insight: "Rate proposal speed is the bottleneck. Link quote simulation directly to opportunity conversion for faster sales velocity.",
  },
  campaigns: {
    title: "Campaigns",
    eyebrow: "CRM & Growth",
    description: "Renewal, lane launch, win-back and cross-sell campaign performance for logistics growth teams.",
    rows: campaigns,
    columns: ["campaignName", "segment", "channel", "status", "audienceSize", "openRate", "responseRate", "leadsGenerated", "revenueInfluenced", "startDate"],
    kpis: [
      { label: "Active Campaigns", value: campaigns.filter((c) => c.status === "Active").length, status: "Active" },
      { label: "Leads Generated", value: valueSum(campaigns, "leadsGenerated"), status: "Healthy" },
      { label: "Revenue Influenced", value: formatCurrency(valueSum(campaigns, "revenueInfluenced"), "SAR"), status: "Growth" },
      { label: "Best Channel", value: "WhatsApp", status: "Healthy" },
    ],
    insight: "WhatsApp cold-chain outreach is outperforming email. Move pharma renewal reminders into the same channel mix.",
  },
  "account-health": {
    title: "Account Health",
    eyebrow: "CRM & Growth",
    description: "Customer health, SLA performance, complaint burden, renewal exposure and account action planning.",
    rows: accountHealth,
    columns: ["customer", "healthScore", "revenueTrend", "shipmentVolumeTrend", "complaintCount", "paymentRisk", "slaPerformance", "renewalDate", "riskLevel", "recommendedAction"],
    kpis: [
      { label: "Healthy Accounts", value: accountHealth.filter((a) => String(a.riskLevel).includes("Healthy")).length, status: "Healthy" },
      { label: "At Risk Accounts", value: accountHealth.filter((a) => String(a.riskLevel).includes("Risk")).length, status: "Risk" },
      { label: "Avg Health", value: `${Math.round(valueSum(accountHealth, "healthScore") / accountHealth.length)}%`, status: "Healthy" },
      { label: "Renewals Next 60d", value: renewals.filter((r) => String(r.status).includes("Risk") || String(r.status).includes("Renewal")).length, status: "Pending" },
    ],
    insight: "Al Noor Pharma needs a service recovery plan before contract expiry. Combine temp-breach evidence, revised SLA plan and executive outreach.",
  },
  "follow-ups": {
    title: "Follow-ups",
    eyebrow: "CRM & Growth",
    description: "Sales and customer success follow-up queue with next best action.",
    rows: followUps,
    columns: ["followUpId", "account", "owner", "reason", "dueDate", "channel", "nextAction", "status"],
    kpis: [
      { label: "Open Follow-ups", value: followUps.length, status: "Pending" },
      { label: "Proposal Follow-ups", value: followUps.filter((f) => String(f.nextAction).includes("proposal")).length, status: "Review" },
      { label: "Owners Active", value: new Set(followUps.map((f) => f.owner)).size, status: "Active" },
      { label: "SLA Discipline", value: "94%", status: "Healthy" },
    ],
    insight: "Proposal-needed follow-ups should auto-open price simulation with the lead’s required service and lane.",
  },
  "support-tickets": {
    title: "Support Tickets",
    eyebrow: "CRM & Growth",
    description: "Customer issue queue connected to shipment, SLA timer and assigned support team.",
    rows: supportTickets,
    columns: ["ticketId", "customer", "shipment", "issueType", "priority", "slaTimer", "assignedTeam", "status", "createdDate"],
    kpis: [
      { label: "Open Tickets", value: supportTickets.length, status: "Open" },
      { label: "Critical Tickets", value: supportTickets.filter((t) => t.priority === "Critical").length, status: "Critical" },
      { label: "SLA Timer Risk", value: "1", status: "Risk" },
      { label: "Customer Ops Load", value: "2 teams", status: "Active" },
    ],
    insight: "Resolve Al Noor ticket first; it overlaps with contract renewal and temperature compliance evidence.",
  },
  renewals: {
    title: "Renewals",
    eyebrow: "CRM & Growth",
    description: "Contract renewal cockpit with revenue, margin, health, probability and next action.",
    rows: renewals,
    columns: ["contract", "customer", "expiryDate", "revenue", "margin", "renewalProbability", "customerHealth", "accountManager", "status"],
    kpis: [
      { label: "Renewals", value: renewals.length, status: "Active" },
      { label: "At Risk Revenue", value: formatCurrency(610000, "SAR"), status: "Risk" },
      { label: "Under Renewal", value: renewals.filter((r) => String(r.status).includes("Renewal")).length, status: "Pending" },
      { label: "Best Probability", value: "78%", status: "Healthy" },
    ],
    insight: "Renewal health is being pulled down by operational exceptions. Surface exact SLA proof before pricing discussion.",
  },
  "upsell-opportunities": {
    title: "Upsell Opportunities",
    eyebrow: "CRM & Growth",
    description: "AI-assisted expansion plays from account health, lane demand, service failures and volume growth.",
    rows: upsell,
    columns: ["account", "currentService", "signal", "recommendedOffer", "expectedValue", "owner", "status"],
    kpis: [
      { label: "Qualified Upsells", value: upsell.filter((u) => u.status === "Qualified").length, status: "Healthy" },
      { label: "Expansion Plays", value: upsell.length, status: "Active" },
      { label: "Retention First", value: upsell.filter((u) => u.status === "Retention First").length, status: "Risk" },
      { label: "Best Offer", value: "Visibility add-on", status: "AI" },
    ],
    insight: "Visibility add-on has the broadest fit. Cold-chain accounts need compliance-first packaging.",
  },
  customers: {
    title: "Customers",
    eyebrow: "Commercial",
    description: "Commercial account view with contracts, revenue, shipment volume, health and service ownership.",
    rows: customers,
    columns: ["id", "companyName", "industry", "country", "primaryContact", "activeContracts", "monthlyShipments", "revenueMtd", "healthScore", "status"],
    kpis: [
      { label: "Customers", value: customers.length, status: "Active" },
      { label: "Revenue MTD", value: formatCurrency(valueSum(customers, "revenueMtd"), "SAR"), status: "Healthy" },
      { label: "Monthly Shipments", value: valueSum(customers, "monthlyShipments"), status: "Active" },
      { label: "High Risk Accounts", value: customers.filter((c) => String(c.status).includes("Risk")).length, status: "Risk" },
    ],
    insight: "Saudi FMCG Supply Co. is the strongest volume account. Protect margin through route and vehicle cost controls.",
  },
  contracts: {
    title: "Contracts",
    eyebrow: "Commercial",
    description: "Customer contract terms, SLA promises, renewal risk, service type and billing governance.",
    rows: contracts,
    columns: ["contractId", "customer", "serviceType", "startDate", "endDate", "sla", "currency", "billingTerms", "renewalStatus", "status"],
    kpis: [
      { label: "Active Contracts", value: contracts.filter((c) => c.status === "Active").length, status: "Active" },
      { label: "Expiring Soon", value: contracts.filter((c) => String(c.status).includes("Expiring")).length, status: "Pending" },
      { label: "Under Renewal", value: contracts.filter((c) => String(c.status).includes("Renewal")).length, status: "Review" },
      { label: "SLA Coverage", value: "100%", status: "Healthy" },
    ],
    insight: "Expiring pharma contract has SLA sensitivity. Attach temp compliance evidence and route improvement plan to renewal.",
  },
  "rate-cards": {
    title: "Rate Cards",
    eyebrow: "Commercial",
    description: "Contract pricing methods by zone, lane, vehicle type, fuel surcharge and effective period.",
    rows: rateCards,
    columns: ["rateCardId", "customerContract", "originZone", "destinationZone", "vehicleType", "pricingMethod", "baseRate", "perKmRate", "fuelSurcharge", "currency", "status"],
    kpis: [
      { label: "Active Rate Cards", value: rateCards.filter((r) => r.status === "Active").length, status: "Active" },
      { label: "Expiring Rates", value: rateCards.filter((r) => String(r.status).includes("Expiring")).length, status: "Pending" },
      { label: "Pricing Methods", value: new Set(rateCards.map((r) => r.pricingMethod)).size, status: "Healthy" },
      { label: "Avg Base Rate", value: formatCurrency(Math.round(valueSum(rateCards, "baseRate") / rateCards.length), "SAR"), status: "Active" },
    ],
    insight: "Per-KM lanes need fuel surcharge guardrails; spot quotes should inherit current fuel and toll assumptions automatically.",
  },
  quotations: {
    title: "Quotations",
    eyebrow: "Commercial",
    description: "Quote lifecycle from draft through sent, accepted, rejected and converted to booking.",
    rows: quotations,
    columns: ["quoteId", "customer", "origin", "destination", "cargo", "quoteAmount", "margin", "validUntil", "status"],
    kpis: [
      { label: "Open Quotes", value: quotations.length, status: "Active" },
      { label: "Accepted", value: quotations.filter((q) => q.status === "Accepted").length, status: "Healthy" },
      { label: "Quote Value", value: formatCurrency(valueSum(quotations, "quoteAmount"), "SAR"), status: "Active" },
      { label: "Margin Watch", value: "1 lane", status: "Review" },
    ],
    insight: "Accepted quotes should convert directly into load bookings while preserving quoted margin and rate card lineage.",
  },
  "load-bookings": {
    title: "Load Bookings",
    eyebrow: "Transport Operations",
    description: "The operating spine from customer demand to assignment, pickup, delivery, POD and invoice readiness.",
    rows: bookings,
    columns: ["bookingId", "customer", "contract", "pickup", "dropoff", "cargoType", "weight", "vehicleRequired", "pickupDateTime", "deliveryDeadline", "quoteAmount", "status"],
    kpis: [
      { label: "Pending Bookings", value: bookings.length, status: "Active" },
      { label: "Awaiting Dispatch", value: bookings.filter((b) => b.status === "Awaiting Dispatch").length, status: "Pending" },
      { label: "Confirmed", value: bookings.filter((b) => b.status === "Confirmed").length, status: "Healthy" },
      { label: "Booking Value", value: formatCurrency(valueSum(bookings, "quoteAmount"), "SAR"), status: "Healthy" },
    ],
    insight: "Bookings awaiting dispatch should be matched by vehicle type, location, maintenance status and customer SLA risk before manual assignment.",
  },
  shipments: {
    title: "Shipments",
    eyebrow: "Transport Operations",
    description: "Execution register for active freight, status timeline, delay risk, POD and invoice state.",
    rows: shipments,
    columns: ["shipmentId", "bookingId", "customer", "origin", "destination", "currentStatus", "vehicle", "driver", "eta", "delayRisk", "podStatus", "invoiceStatus"],
    kpis: [
      { label: "In Transit", value: shipments.filter((s) => s.currentStatus === "In Transit").length, status: "Active" },
      { label: "Delayed", value: shipments.filter((s) => s.currentStatus === "Delayed").length, status: "Risk" },
      { label: "Delivered", value: shipments.filter((s) => s.currentStatus === "Delivered").length, status: "Healthy" },
      { label: "Avg Margin", value: `${Math.round(shipments.reduce((sum, s) => sum + calculateProfitability(Number(s.revenue), Number(s.cost)).margin, 0) / shipments.length)}%`, status: "Healthy" },
    ],
    insight: "BOX-106 is creating delay and margin pressure. Reassign any future DC work until maintenance closes.",
  },
  "route-plans": {
    title: "Route Plans",
    eyebrow: "Transport Operations",
    description: "Lane-level route plan library with distance, duration, toll, risk and vehicle fit.",
    rows: routePlans,
    columns: ["routeId", "origin", "destination", "distance", "estimatedDuration", "tollEstimate", "preferredVehicleType", "riskLevel", "active", "status"],
    kpis: [
      { label: "Active Routes", value: routePlans.filter((r) => r.active === "Yes").length, status: "Active" },
      { label: "High Risk Routes", value: routePlans.filter((r) => r.riskLevel === "High").length, status: "Risk" },
      { label: "Cold Chain Routes", value: routePlans.filter((r) => String(r.preferredVehicleType).includes("Reefer")).length, status: "Review" },
      { label: "Route Library", value: routePlans.length, status: "Healthy" },
    ],
    insight: "Manassas to Washington DC is a delay hotspot. Use it for SLA pricing and dispatch buffer recommendations.",
  },
  "proof-of-delivery": {
    title: "Proof of Delivery",
    eyebrow: "Transport Operations",
    description: "POD capture, GPS verification, photos, receiver signature, damage flags and invoice readiness.",
    rows: proofOfDelivery,
    columns: ["podId", "shipment", "customer", "deliveredAt", "receiverName", "signature", "photo", "gpsVerified", "damageFlag", "status"],
    kpis: [
      { label: "POD Records", value: proofOfDelivery.length, status: "Active" },
      { label: "Uploaded", value: proofOfDelivery.filter((p) => p.status === "Uploaded").length, status: "Healthy" },
      { label: "Pending", value: proofOfDelivery.filter((p) => p.status === "Pending").length, status: "Pending" },
      { label: "Invoice Ready", value: invoices.length, status: "Healthy" },
    ],
    insight: "POD upload is the handoff to invoice generation. Pending POD records should trigger driver reminders and customer service watch.",
  },
  "last-mile-delivery": {
    title: "Last Mile Delivery",
    eyebrow: "Transport Operations",
    description: "Parcel and final-mile runs with delivery windows, customer ETA confidence and proof capture.",
    rows: shipments.filter((s) => String(s.cargoType).includes("Parcel") || String(s.vehicle).includes("VAN")),
    columns: ["shipmentId", "customer", "origin", "destination", "currentStatus", "vehicle", "driver", "eta", "delayRisk", "podStatus"],
    kpis: [
      { label: "Last Mile Runs", value: 1, status: "Active" },
      { label: "Completed", value: 1, status: "Healthy" },
      { label: "ETA Confidence", value: "93%", status: "Healthy" },
      { label: "Customer Rating", value: "4.7", status: "Healthy" },
    ],
    insight: "DesertCart final-mile work is a growth candidate if proof capture stays clean and ETA transparency remains high.",
  },
};

function statusish(value: unknown) {
  return /status|risk|priority|severity|health|availability|compliance|renewal/i.test(String(value));
}

function enrichRows(rows: AnyRecord[]) {
  return rows.map((row) => {
    const next = { ...row };
    for (const key of Object.keys(next)) {
      if (/revenue|amount|cost|rate|value/i.test(key) && typeof next[key] === "number") {
        const currency = String(next.currency ?? "SAR");
        next[key] = formatCurrency(Number(next[key]), currency);
      }
      if (/date|expiry|until|start|end/i.test(key) && typeof next[key] === "string" && /^\d{4}-\d{2}-\d{2}$/.test(String(next[key]))) {
        next[key] = formatDate(String(next[key]));
      }
    }
    return next;
  });
}

function ModuleToolbar({ search, setSearch, filter, setFilter }: { search: string; setSearch: (value: string) => void; filter: string; setFilter: (value: string) => void }) {
  return (
    <div className="panel flex flex-col gap-3 p-3.5 lg:flex-row lg:items-center lg:justify-between">
      <div className="relative min-w-0 flex-1 lg:max-w-md">
        <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
        <input className="field pl-10" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search accounts, loads, routes, IDs..." />
      </div>
      <div className="flex flex-wrap items-center gap-2">
        {["All", "Active", "Pending", "At Risk", "Completed"].map((item) => (
          <button key={item} className={filter === item ? "btn-primary py-2 text-xs" : "btn-ghost py-2 text-xs"} onClick={() => setFilter(item)}>
            {item}
          </button>
        ))}
      </div>
    </div>
  );
}

function LiveDashboardPage() {
  const alertsQuery = useQuery({
    queryKey: ["alerts"],
    queryFn: () => alertsApi.list(),
    staleTime: 15_000,
    refetchInterval: 15_000,
  });
  const alertRows = useMemo(
    () => (Array.isArray(alertsQuery.data) ? (alertsQuery.data as AnyRecord[]).map((alert) => normalizeAlert(alert)) : []),
    [alertsQuery.data],
  );
  const openAlerts = alertRows.filter((alert) => !/closed/i.test(String(alert.status)));
  const criticalAlerts = openAlerts.filter((alert) => /critical/i.test(String(alert.severity)));
  const acknowledgedAlerts = openAlerts.filter((alert) => /acknowledged/i.test(String(alert.status)));
  const topAlerts = openAlerts.slice(0, 6);

  if (alertsQuery.isLoading) return <LoadingState />;
  if (alertsQuery.isError) {
    return <EmptyState title="Operations dashboard unavailable" subtitle="The persisted alert register could not be loaded. No operating status is inferred while the source is unavailable." />;
  }

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Control Tower"
        title="Operations Dashboard"
        description="Current tenant alert records. Shipment, dispatch, fleet position, revenue, cost and service-level feeds are shown only after their production sources are connected."
        actions={
          <button className="btn-ghost" disabled={!openAlerts.length} onClick={() => exportCsv("open-alerts", openAlerts)}>
            <Download className="h-4 w-4" /> Export Open Alerts
          </button>
        }
      />
      <div className="grid gap-3 sm:grid-cols-3">
        <KpiCard label="Open Alert Records" value={openAlerts.length} status="Open" />
        <KpiCard label="Critical Alert Records" value={criticalAlerts.length} status={criticalAlerts.length ? "Critical" : undefined} />
        <KpiCard label="Acknowledged Open Records" value={acknowledgedAlerts.length} status="Recorded" />
      </div>
      <div className="grid gap-5 xl:grid-cols-[1.15fr_.85fr]">
        <div className="panel p-5">
          <p className="section-title">Open Alert Register</p>
          <div className="mt-4 space-y-3">
            {topAlerts.length ? topAlerts.map((alert) => (
              <div key={alert.alertId} className="rounded-xl border border-slate-100 bg-slate-50 p-3">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <p className="font-semibold text-slate-900">{alert.title}</p>
                    <p className="mt-1 text-xs text-slate-500">{alert.entity ?? alert.entityType ?? "Unmapped entity"} · {alert.category}</p>
                  </div>
                  <RiskBadge risk={alert.severity} />
                </div>
                <p className="mt-2 text-xs text-slate-600">{alert.recommendedAction || alert.body || "No recommended action recorded."}</p>
              </div>
            )) : (
              <EmptyState title="No open alert records" subtitle="The current query returned no open records. This does not establish that every operation or service is healthy." />
            )}
          </div>
        </div>
        <EmptyState
          title="Operational feeds not connected"
          subtitle="Shipment, booking, vehicle position, driver availability, revenue, cost, temperature, safety and on-time delivery values remain unavailable on this page until backed by tenant-scoped production records."
        />
      </div>
    </div>
  );
}

function AlertsPage() {
  const hasPermission = useHasPermission();
  const canExport = hasPermission("alerts:view");
  const canAct = hasPermission("alerts:acknowledge");
  const canClose = hasPermission("alerts:close");
  const { data: alertRowsRaw = [], isLoading, isError } = useQuery<AlertRecord[]>({
    queryKey: ["alerts"],
    queryFn: () => alertsApi.list() as Promise<AlertRecord[]>,
    staleTime: 15_000,
    refetchInterval: 15_000,
  });
  const { data: summaryRaw } = useQuery<AnyRecord>({
    queryKey: ["alerts", "summary"],
    queryFn: () => alertsApi.summary(),
    staleTime: 15_000,
    refetchInterval: 15_000,
  });
  const alertRows = alertRowsRaw as AlertRecord[];
  const qc = useQueryClient();
  const [selected, setSelected] = useState<AlertRecord | null>(null);
  const [activeTab, setActiveTab] = useState("All");
  const [taskFor, setTaskFor] = useState<AlertRecord | null>(null);
  const [search, setSearch] = useState("");
  const tabs = ["All", "Critical", "High", "Warning", "Info", "Safety", "Maintenance", "Telematics", "Customer", "Compliance", "Operations"];

  const updateCachedAlert = (updated: AlertRecord) => {
    qc.setQueryData<AlertRecord[]>(["alerts"], (current = []) =>
      current.map((alert) => (String(alert.alertId ?? alert.id) === String(updated.alertId ?? updated.id) ? { ...alert, ...updated } : alert)),
    );
    setSelected((current) => (current && String(current.alertId ?? current.id) === String(updated.alertId ?? updated.id) ? { ...current, ...updated } : current));
  };

  const ackMut = useMutation<AlertRecord, Error, string>({
    mutationFn: async (alertId: string) => alertsApi.acknowledge(alertId, { acknowledgedAt: new Date().toISOString() }) as Promise<AlertRecord>,
    onSuccess: (updated) => updateCachedAlert(updated),
  });
  const closeMut = useMutation<AlertRecord, Error, string>({
    mutationFn: async (alertId: string) => alertsApi.close(alertId, { closedAt: new Date().toISOString() }) as Promise<AlertRecord>,
    onSuccess: (updated) => updateCachedAlert(updated),
  });
  const taskMut = useMutation<AlertRecord, Error, { alertId: string; title: string; owner: string }>({
    mutationFn: async ({ alertId, title, owner }: { alertId: string; title: string; owner: string }) =>
      alertsApi.createTask(alertId, { title, owner, createdAt: new Date().toISOString() }) as Promise<AlertRecord>,
    onSuccess: (updated) => {
      updateCachedAlert(updated);
      setTaskFor(null);
    },
  });

  const summary = useMemo(() => {
    const live = summaryRaw ?? {};
    return {
      total: Number(live.total ?? alertRows.length),
      critical: Number(live.critical ?? alertRows.filter((alert) => alert.severity === "Critical").length),
      high: Number(live.high ?? alertRows.filter((alert) => alert.severity === "High").length),
      open: Number(live.open ?? alertRows.filter((alert) => /open/i.test(String(alert.status))).length),
      acknowledged: Number(live.acknowledged ?? alertRows.filter((alert) => /ack/i.test(String(alert.status))).length),
      closed: Number(live.closed ?? alertRows.filter((alert) => /closed/i.test(String(alert.status))).length),
    };
  }, [alertRows, summaryRaw]);

  const visibleAlerts = useMemo<AlertRecord[]>(() => {
    const severityRank = (severity: string) => ({ Critical: 4, High: 3, Warning: 2, Medium: 2, Low: 1, Info: 0 }[severity] ?? 0);
    const query = search.trim().toLowerCase();
    return [...alertRows]
      .map((alert): AlertRecord => ({
        ...alert,
        status: String(alert.status ?? "").toLowerCase() === "closed" ? "Closed" : String(alert.status ?? ""),
      }))
      .filter((alert) => activeTab === "All" || String(alert.severity) === activeTab || String(alert.category) === activeTab || String(alert.status) === activeTab)
      .filter((alert) => {
        if (!query) return true;
        return [
          alert.title,
          alert.alertId,
          alert.entity,
          alert.entityType,
          alert.category,
          alert.recommendedAction,
          alert.owner,
          alert.location,
        ].some((value) => String(value ?? "").toLowerCase().includes(query));
      })
      .sort((a, b) => severityRank(String(b.severity)) - severityRank(String(a.severity)) || ageHours(b.createdAt) - ageHours(a.createdAt));
  }, [alertRows, activeTab, search]);

  const categoryLanes = useMemo(() => {
    const laneOrder = ["Safety", "Maintenance", "Telematics", "Customer", "Compliance", "Operations"];
    return laneOrder.map((category) => {
      const laneAlerts = alertRows.filter((alert) => String(alert.category) === category);
      return {
        category,
        count: laneAlerts.length,
        openCount: laneAlerts.filter((alert) => !/closed/i.test(String(alert.status))).length,
        criticalCount: laneAlerts.filter((alert) => String(alert.severity) === "Critical").length,
        topAction: laneAlerts[0]?.recommendedAction || laneAlerts[0]?.body || "No action recorded",
      };
    }).filter((lane) => lane.count > 0 || lane.openCount > 0);
  }, [alertRows]);

  const recentSignals = useMemo(() => visibleAlerts.slice(0, 4), [visibleAlerts]);

  useEffect(() => {
    if (!selected && visibleAlerts.length) {
      setSelected(visibleAlerts[0]);
    }
  }, [selected, visibleAlerts]);

  if (isLoading) return <LoadingState />;
  if (isError) return <EmptyState title="Alerts unavailable" subtitle="Unable to load the alert register right now. Refresh to try again." />;

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Control Tower"
        title="Alerts"
        description="Tenant-scoped alert records returned by the operations service. Missing records and unavailable sources remain explicit."
        actions={
          <>
            <button
              className="btn-ghost"
              disabled={!canExport}
              title={!canExport ? "You do not have permission to perform this action." : "Export the current alert register."}
              onClick={() => exportCsv("alerts", visibleAlerts)}
            >
              <Download className="h-4 w-4" /> Export Alert Register
            </button>
            <button
              className="btn-primary"
              onClick={() => {
                void qc.invalidateQueries({ queryKey: ["alerts"] });
                void qc.invalidateQueries({ queryKey: ["alerts", "summary"] });
              }}
            >
              <Sparkles className="h-4 w-4" /> Refresh Alert Records
            </button>
          </>
        }
      />

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <KpiCard label="Open Alerts" value={summary.open} status="Open" />
        <KpiCard label="Critical Alerts" value={summary.critical} status="Critical" />
        <KpiCard label="Acknowledged" value={summary.acknowledged} status="Review" />
        <KpiCard label="Closed" value={summary.closed} status="Recorded" />
      </div>

      <div className="panel flex flex-col gap-3 p-4 lg:flex-row lg:items-center lg:justify-between">
        <div className="relative w-full lg:max-w-sm">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <input
            type="search"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Search alert, entity, owner or action…"
            className="w-full rounded-xl border border-slate-200 bg-white py-2.5 pl-10 pr-3 text-sm text-slate-900 outline-none transition placeholder:text-slate-400 focus:border-teal-400 focus:ring-2 focus:ring-teal-100"
          />
        </div>
        <div className="flex flex-wrap gap-2">
          {tabs.map((tab) => (
            <button key={tab} className={activeTab === tab ? "btn-primary py-2 text-xs" : "btn-ghost py-2 text-xs"} onClick={() => setActiveTab(tab)}>
              {tab}
            </button>
          ))}
        </div>
      </div>

      <div className="grid gap-4 xl:grid-cols-[1.18fr_0.82fr]">
        <div className="space-y-3">
          {visibleAlerts.map((alert: AlertRecord) => (
            <div key={alert.alertId} className="panel p-3.5 transition hover:border-amber-300 hover:shadow-lg">
              <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
                <div className="min-w-0">
                  <div className="flex flex-wrap items-center gap-2">
                    <RiskBadge risk={alert.severity} />
                    <StatusBadge status={alert.status} />
                    <span className="rounded-full border border-slate-200 bg-slate-50 px-2.5 py-1 text-[11px] font-bold text-slate-500">{alert.category}</span>
                    <span className="text-xs font-semibold text-slate-400">{alert.age ?? "Age unavailable"}</span>
                  </div>
                  <h2 className="mt-2 text-[1.02rem] font-bold text-slate-900">{alert.title}</h2>
                  <p className="mt-1 text-sm text-slate-600">
                    {alert.entity ?? alert.entityType ?? "Unmapped entity"} · {alert.customer ?? "Customer unavailable"} · {alert.location ?? "Location unavailable"}
                  </p>
                  <div className="mt-2 rounded-xl border border-amber-100 bg-[#fff6ea] px-3 py-2 text-sm text-amber-900">
                    <span className="font-bold">Recommended action:</span> {alert.recommendedAction || alert.body || "No recommended action recorded."}
                  </div>
                </div>
                <div className="flex flex-wrap gap-2 lg:justify-end lg:max-w-[240px]">
                  <button
                    className="btn-primary h-9 px-3 text-[11px]"
                    disabled={!canAct}
                    title={!canAct ? "You do not have permission to perform this action." : "Acknowledge this alert."}
                    onClick={() => ackMut.mutate(alert.alertId)}
                  >
                    <CheckCircle2 className="h-3.5 w-3.5" /> Ack
                  </button>
                  <button className="btn-ghost h-9 px-3 text-[11px]" onClick={() => setSelected(alert)}>Details</button>
                  <button className="btn-ghost h-9 px-3 text-[11px]" onClick={() => setTaskFor(alert)}>Task</button>
                  <button
                    className="btn-ghost h-9 px-3 text-[11px]"
                    disabled={!canClose}
                    title={!canClose ? "You do not have permission to perform this action." : "Close this alert."}
                    onClick={() => closeMut.mutate(alert.alertId)}
                  >
                    Close
                  </button>
                </div>
              </div>
            </div>
          ))}
          {!visibleAlerts.length && <EmptyState title="No alerts in this lane" subtitle="Try another tab or clear the search. The current persisted result contains no matching records." />}
        </div>

        <div className="space-y-4 xl:sticky xl:top-4 xl:self-start">
          <div className="panel p-4">
            <p className="section-title">Recorded Alert Triage</p>
            <div className="mt-3 space-y-2.5">
              <div className="rounded-xl border border-red-100 bg-gradient-to-br from-red-50 to-rose-50 px-3 py-2 text-sm text-red-800 shadow-inner">
                <span className="font-bold">Criticals:</span> {summary.critical} open alert{summary.critical === 1 ? "" : "s"} need immediate review.
              </div>
              <div className="rounded-xl border border-amber-100 bg-gradient-to-br from-amber-50 to-orange-50 px-3 py-2 text-sm text-amber-800 shadow-inner">
                <span className="font-bold">Queue depth:</span> {summary.total} total persisted alert{summary.total === 1 ? "" : "s"} are visible for this tenant.
              </div>
              <div className="rounded-xl border border-slate-100 bg-gradient-to-br from-white to-stone-50 px-3 py-2 text-sm text-slate-700 shadow-inner">
                <span className="font-bold">Next move:</span> clear the top critical item, then work down by age and category.
              </div>
            </div>
          </div>

          <div className="panel p-4">
            <p className="section-title">Recent Recorded Alerts</p>
            <div className="mt-3 space-y-2.5">
              {recentSignals.length ? recentSignals.map((alert) => (
                <div key={String(alert.id)} className="rounded-xl border border-slate-100 bg-gradient-to-br from-[#fffdf8] to-[#f7efe2] p-3 shadow-sm">
                  <div className="flex items-center justify-between">
                    <p className="text-xs font-bold uppercase tracking-[0.14em] text-slate-500">
                      {String(alert.category)} · {String(alert.severity)}
                    </p>
                    <span className="text-xs text-slate-400">{alert.age ?? "Age unavailable"}</span>
                  </div>
                  <p className="mt-1 text-sm font-semibold text-slate-900">{alert.title}</p>
                  <p className="mt-1 text-sm text-slate-700">{alert.recommendedAction || alert.body || "No recommended action recorded."}</p>
                </div>
              )) : (
                <div className="rounded-xl border border-emerald-100 bg-emerald-50 px-3 py-2 text-sm text-emerald-800">
                  The current result contains no alert records. This does not establish an all-clear state.
                </div>
              )}
            </div>
          </div>
          <div className="panel p-4">
            <p className="section-title">Operational Lanes</p>
            <div className="mt-3 space-y-2">
              {categoryLanes.map((lane) => (
                <div key={lane.category} className="rounded-xl border border-slate-100 bg-gradient-to-br from-white to-stone-50 px-3 py-2 shadow-sm">
                  <div className="flex items-center justify-between gap-3">
                    <span className="text-sm font-semibold text-slate-700">{lane.category}</span>
                    <span className="text-xs font-bold uppercase tracking-[0.14em] text-slate-400">{lane.openCount} open</span>
                  </div>
                  <p className="mt-1 text-xs text-slate-500">{lane.criticalCount} critical · {lane.topAction}</p>
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>
      <DetailDrawer record={selected} onClose={() => setSelected(null)} />
      {taskFor && (
        <AlertTaskModal
          alertRecord={taskFor}
          saving={taskMut.isPending}
          onClose={() => setTaskFor(null)}
          onSave={(payload) => taskMut.mutate({ alertId: String(taskFor.alertId), title: payload.title, owner: payload.owner })}
        />
      )}
    </div>
  );
}

function AlertTaskModal({ alertRecord, saving, onClose, onSave }: { alertRecord: AlertRecord; saving: boolean; onClose: () => void; onSave: (payload: { title: string; owner: string }) => void }) {
  const [title, setTitle] = useState(`Task for ${String(alertRecord.title ?? alertRecord.type ?? alertRecord.alertId)}`);
  const [owner, setOwner] = useState(String(alertRecord.owner ?? "Dispatch"));
  return (
    <div className="fixed inset-0 z-[60] grid place-items-center bg-black/60 p-4">
      <form
        className="panel w-full max-w-md p-6"
        onSubmit={(e) => {
          e.preventDefault();
          onSave({ title, owner });
        }}
      >
        <div className="flex items-center justify-between">
          <h2 className="text-xl font-semibold text-white">Create Alert Task</h2>
          <button type="button" className="icon-btn" onClick={onClose}><X className="h-4 w-4" /></button>
        </div>
        <p className="mt-2 text-sm text-slate-400">{String(alertRecord.title ?? alertRecord.type ?? alertRecord.alertId)} · {String(alertRecord.entity ?? "")}</p>
        <div className="mt-5 space-y-4">
          <label className="block">
            <span className="mb-2 block text-xs font-bold uppercase tracking-[0.16em] text-slate-500">Task Title</span>
            <input className="field w-full" value={title} onChange={(e) => setTitle(e.target.value)} />
          </label>
          <label className="block">
            <span className="mb-2 block text-xs font-bold uppercase tracking-[0.16em] text-slate-500">Owner</span>
            <input className="field w-full" value={owner} onChange={(e) => setOwner(e.target.value)} />
          </label>
        </div>
        <div className="mt-6 flex justify-end gap-3">
          <button type="button" className="btn-ghost" onClick={onClose}>Cancel</button>
          <button className="btn-primary" disabled={saving}>Create Task</button>
        </div>
      </form>
    </div>
  );
}

function exportCsv(name: string, rows: AnyRecord[]) {
  if (!rows.length) return;
  const cols = Array.from(new Set(rows.flatMap((row) => Object.keys(row)))).slice(0, 24);
  const csv = [cols.join(","), ...rows.map((row) => cols.map((c) => JSON.stringify(row[c] ?? "")).join(","))].join("\n");
  const a = document.createElement("a");
  a.href = URL.createObjectURL(new Blob([csv], { type: "text/csv" }));
  a.download = `opstrax-${name}.csv`;
  a.click();
}

function PriceSimulationPage() {
  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Commercial"
        title="Price Simulation"
        description="A production quote requires persisted customer, contract, lane, rate-card, fuel, toll, tax and cost inputs."
      />
      <EmptyState
        title="Pricing source not connected"
        subtitle="No quote is calculated or convertible until those tenant-scoped production inputs and a persisted quote workflow are available."
      />
    </div>
  );
}

function DispatchBoardPage() {
  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Transport Operations"
        title="Dispatch Board"
        description="Dispatch recommendations require persisted bookings plus verified vehicle, driver, maintenance, cold-chain and safety eligibility records."
      />
      <EmptyState
        title="Dispatch source not connected"
        subtitle="No booking count, availability rate, eligibility match or dispatch recommendation is shown until the required tenant-scoped production records are connected."
      />
    </div>
  );
}

const operatingSpineSteps: { label: string; body: string; Icon: LucideIcon }[] = [
  { label: "Lead", body: "CRM demand captured", Icon: Users },
  { label: "Quote", body: "Price and margin simulated", Icon: Calculator },
  { label: "Booking", body: "Customer load confirmed", Icon: PackageCheck },
  { label: "Dispatch", body: "Driver and vehicle assigned", Icon: Radar },
  { label: "Shipment", body: "Live tracking and ETA", Icon: MapPinned },
  { label: "POD", body: "Proof captured", Icon: ClipboardCheck },
  { label: "Invoice", body: "Billing ready", Icon: DollarSign },
];

export function OperatingModulePage({ moduleKey }: { moduleKey: string }) {
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const [search, setSearch] = useState("");
  const [filter, setFilter] = useState("All");
  const navigate = useNavigate();

  if (moduleKey === "live-dashboard") return <LiveDashboardPage />;
  if (moduleKey === "alerts") return <AlertsPage />;
  if (moduleKey === "price-simulation") return <PriceSimulationPage />;
  if (moduleKey === "dispatch-board") return <DispatchBoardPage />;

  const definition = moduleDefinitions[moduleKey];
  const rows = useMemo(() => {
    const sourceRows = definition?.rows ?? [];
    const query = search.trim().toLowerCase();
    const enriched = enrichRows(sourceRows);
    return enriched.filter((row) => {
      const textMatch = !query || Object.values(row).some((val) => String(val).toLowerCase().includes(query));
      
      // Intelligent status detection for CRM and Operations modules
      const statusVal = String(row.status || row.currentStatus || row.current_status || row.renewalStatus || row.stage || row.reason || "").toLowerCase();
      const filterLower = filter.toLowerCase().replace("at risk", "risk");
      const filterMatch = filter === "All" || statusVal.includes(filterLower) || (filter === "Active" && !/closed|delivered|completed|lost/i.test(statusVal));

      return textMatch && filterMatch;
    });
  }, [definition, filter, search]);
  const hasConnectedRecords = rows.length > 0;

  if (!definition) {
    return (
      <div className="control-tower space-y-6">
        <PageHeader
          eyebrow="OpsTrax"
          title="Module Workspace"
          description="This module is available in navigation and ready for deeper workflow configuration."
          actions={<>
            <button type="button" className="btn-ghost" onClick={() => navigate("/command-center")}>Open Dashboard</button>
            <button type="button" className="btn-primary" onClick={() => navigate("/operations/proof-center")}>Open Proof Center</button>
          </>}
        />
        <EmptyState
          title="Integration required"
          subtitle="This module is available in navigation, but the specific workflow is not configured yet. Use the connected operational hubs instead of a dead-end panel."
        />
      </div>
    );
  }

  return (
    <div className="control-tower space-y-6">
      <PageHeader
        eyebrow={definition.eyebrow}
        title={definition.title}
        description={definition.description}
        actions={<>
          <button className="btn-ghost" type="button" disabled={!hasConnectedRecords} onClick={() => exportCsv(definition.title, rows)}>
            <Download className="h-4 w-4" />
            Export
          </button>
          <button className="btn-primary" type="button" onClick={() => navigate("/control-tower")}>
            <Send className="h-4 w-4" />
            Open Control Tower
          </button>
        </>}
      />
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        {definition.kpis.map((kpi) => <KpiCard key={kpi.label} label={kpi.label} value={hasConnectedRecords ? kpi.value : "—"} status={hasConnectedRecords ? kpi.status : "Unavailable"} trend={hasConnectedRecords ? kpi.trend : undefined} />)}
      </div>
      <ModuleToolbar search={search} setSearch={setSearch} filter={filter} setFilter={setFilter} />
      <div className="grid gap-5 xl:grid-cols-[1fr_360px]">
        {hasConnectedRecords
          ? <DataTable rows={rows} columns={definition.columns} onSelect={setSelected} />
          : <EmptyState title="No connected production records" subtitle="This workspace has no production data source yet. Fixed example records, percentages, and recommendations are not shown." />}
        <div className="space-y-5">
          {hasConnectedRecords
            ? <AiInsightCard insight={{ title: `${definition.title} recommendation`, body: definition.insight, score: 89, moduleKey }} />
            : <div className="panel p-5 text-sm text-slate-500">No evidence-linked recommendation is available for this workspace.</div>}
          <div className="panel p-5">
            <p className="section-title">Operating Spine</p>
            <div className="mt-4 space-y-3">
              {operatingSpineSteps.map(({ label, body, Icon }) => (
                <div key={label} className="flex gap-3 rounded-xl border border-slate-100 bg-slate-50 p-3">
                  <div className="flex h-9 w-9 items-center justify-center rounded-lg bg-blue-50 text-blue-700">
                    <Icon className="h-4 w-4" />
                  </div>
                  <div>
                    <p className="font-semibold text-slate-900">{label}</p>
                    <p className="text-xs text-slate-500">{body}</p>
                  </div>
                </div>
              ))}
            </div>
          </div>
          <div className="panel p-5">
            <p className="section-title">Decision Signals</p>
            <div className="mt-4 flex flex-wrap gap-2">
              {["SLA risk", "Margin leakage", "Customer retention", "Dispatch fit", "Document readiness", "Proof-to-cash"].map((item) => (
                <span key={item} className="rounded-full border border-blue-100 bg-blue-50 px-3 py-1 text-xs font-semibold text-blue-700">{item}</span>
              ))}
            </div>
          </div>
        </div>
      </div>
      <DetailDrawer record={selected} onClose={() => setSelected(null)} />
    </div>
  );
}
