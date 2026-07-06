import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  AlertTriangle,
  BarChart3,
  Building2,
  Calculator,
  CheckCircle2,
  ClipboardCheck,
  DollarSign,
  Download,
  FileText,
  MapPinned,
  PackageCheck,
  Radar,
  Route,
  Search,
  Send,
  Sparkles,
  X,
  Truck,
  Users,
} from "lucide-react";
import type { LucideIcon } from "lucide-react";
import { Bar, BarChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
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
import { developmentFleetSeedData } from "@/data/developmentFleetSeedData";
import { alertsApi } from "@/services/alertsApi";
import { useHasPermission } from "@/hooks/usePermission";
import type { AnyRecord } from "@/types";
import { calculateCustomerHealth, calculateProfitability, calculateShipmentDelay, formatCurrency, formatDate } from "@/utils/formatters";
import { useNavigate } from "react-router-dom";

// developmentFleetSeedData is LOCAL-DEV-ONLY scaffolding. The `import.meta.env.DEV`
// flag is statically inlined by Vite (true in `vite dev`, false in `vite build`),
// so this conditional is dead-code-eliminated in production and the seed module is
// tree-shaken out of the bundle entirely. In a production build the page renders
// honest empty states instead of synthetic data.
const seed = (import.meta.env.DEV
  ? developmentFleetSeedData
  : ({
      bookings: [], campaigns: [], contracts: [], customers: [], devices: [],
      drivers: [], expenses: [], incidents: [], invoices: [], leads: [],
      maintenance: [], opportunities: [], quotations: [], rateCards: [],
      shipments: [], supportTickets: [], vehicles: [], alerts: [],
    } as unknown as typeof developmentFleetSeedData));

const {
  bookings,
  campaigns,
  contracts,
  customers,
  devices,
  drivers,
  expenses,
  incidents,
  invoices,
  leads,
  maintenance,
  opportunities,
  quotations,
  rateCards,
  shipments,
  supportTickets,
  vehicles,
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

const routePlans = [
  { routeId: "RTE-KSA-018", origin: "Riyadh", destination: "Dammam", distance: "414 km", estimatedDuration: "4h 50m", tollEstimate: "SAR 86", preferredVehicleType: "Dry Van", riskLevel: "Low", active: "Yes", status: "Active" },
  { routeId: "RTE-KSA-027", origin: "Jeddah", destination: "Riyadh", distance: "949 km", estimatedDuration: "10h 35m", tollEstimate: "SAR 140", preferredVehicleType: "Reefer", riskLevel: "Medium", active: "Yes", status: "Temperature Watch" },
  { routeId: "RTE-US-DC-006", origin: "Manassas", destination: "Washington DC", distance: "34 mi", estimatedDuration: "1h 05m", tollEstimate: "USD 14", preferredVehicleType: "Box Truck", riskLevel: "High", active: "Yes", status: "Delay Hotspot" },
];

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
      { label: "Customer Impact", value: "3 accounts", status: "Risk" },
      { label: "Triage Confidence", value: "92%", status: "AI" },
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

const vehiclePositions: Record<string, { left: string; top: string; heading: string; speed: string; lat: number; lng: number }> = {
  "KSA-REEFER-214": { left: "72%", top: "58%", heading: "E", speed: "82 km/h", lat: 24.7136, lng: 46.6753 },
  "KSA-REEFER-119": { left: "46%", top: "31%", heading: "NE", speed: "0 km/h", lat: 21.4858, lng: 39.1925 },
  "DXB-VAN-045": { left: "24%", top: "71%", heading: "SW", speed: "0 km/h", lat: 25.2048, lng: 55.2708 },
  "BOX-106": { left: "77%", top: "59%", heading: "NE", speed: "18 mph", lat: 38.9072, lng: -77.0369 },
};

function vehicleMonitor(vehicle: AnyRecord) {
  const vehicleId = String(vehicle.vehicleId);
  const shipment = shipments.find((item) => item.vehicle === vehicleId);
  const driver = drivers.find((item) => item.name === vehicle.assignedDriver || item.assignedVehicle === vehicleId);
  const device = devices.find((item) => item.linkedVehicleTrailer === vehicleId || item.deviceId === vehicle.assignedDevice);
  const position = vehiclePositions[vehicleId] ?? { left: "50%", top: "50%", heading: "N", speed: "0", lat: 0, lng: 0 };
  const monitorStatus = shipment?.currentStatus ?? vehicle.status;
  const risk = /Critical|Maintenance|Delayed|Review/i.test(`${vehicle.maintenanceStatus} ${vehicle.status} ${shipment?.delayRisk ?? ""}`) ? "High" : /Due|Medium|Watch/i.test(`${vehicle.maintenanceStatus} ${shipment?.slaRisk ?? ""}`) ? "Medium" : "Low";
  const violations = [
    ...(shipment?.delayRisk === "High" ? ["SLA delay risk", "Late delivery exception"] : []),
    ...(String(vehicle.maintenanceStatus).match(/Critical|Due/i) ? ["Maintenance dispatch block"] : []),
    ...(device && !String(device.status).match(/Online/i) ? [`Device visibility issue: ${device.status}`] : []),
    ...(String(driver?.hosStatus ?? "").match(/Risk/i) ? ["Driver HOS risk"] : []),
    ...(shipment?.slaRisk === "Temperature Watch" ? ["Temperature compliance watch"] : []),
  ];
  const stops = shipment
    ? [
        { stop: "Pickup", location: shipment.origin, window: "08:00-09:30", status: shipment.currentStatus === "At Pickup" ? "Active" : "Completed" },
        { stop: "Checkpoint", location: shipment.origin === "Jeddah" ? "Taif corridor" : shipment.origin === "Manassas" ? "I-395 corridor" : "Route midpoint", window: "Live", status: shipment.currentStatus === "In Transit" || shipment.currentStatus === "Delayed" ? "Active" : "Pending" },
        { stop: "Delivery", location: shipment.destination, window: shipment.eta, status: shipment.currentStatus === "Delivered" ? "Completed" : "Pending" },
      ]
    : [
        { stop: "Current yard", location: String(vehicle.currentLocation), window: "Now", status: "Idle" },
        { stop: "Next assignment", location: "Unassigned", window: "Pending", status: "Pending" },
      ];
  return { vehicle, shipment, driver, device, position, monitorStatus, risk, violations, stops };
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

function RevenueSnapshot() {
  const revenue = valueSum(shipments, "revenue");
  const cost = valueSum(shipments, "cost");
  const profit = calculateProfitability(revenue, cost);
  const chartData = customers.slice(0, 5).map((customer) => ({ name: String(customer.companyName).split(" ")[0], revenue: Number(customer.revenueMtd) / 1000 }));
  return (
    <div className="panel p-5">
      <div className="flex items-center justify-between">
        <div>
          <p className="section-title">Revenue / Cost Snapshot</p>
          <h2 className="mt-2 text-lg font-bold text-slate-900">Margin protection view</h2>
        </div>
        <StatusBadge status={profit.status} />
      </div>
      <div className="mt-4 grid gap-3 sm:grid-cols-3">
        <div className="rounded-xl border border-slate-100 bg-slate-50 p-3">
          <p className="text-xs text-slate-500">Revenue MTD</p>
          <p className="mt-1 text-xl font-bold text-slate-900">{formatCurrency(revenue, "SAR")}</p>
        </div>
        <div className="rounded-xl border border-slate-100 bg-slate-50 p-3">
          <p className="text-xs text-slate-500">Cost MTD</p>
          <p className="mt-1 text-xl font-bold text-slate-900">{formatCurrency(cost, "SAR")}</p>
        </div>
        <div className="rounded-xl border border-slate-100 bg-slate-50 p-3">
          <p className="text-xs text-slate-500">Gross Margin</p>
          <p className="mt-1 text-xl font-bold text-slate-900">{profit.marginText}</p>
        </div>
      </div>
      <div className="mt-4 h-44">
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={chartData}>
            <XAxis dataKey="name" fontSize={11} tickLine={false} axisLine={false} />
            <YAxis fontSize={11} tickLine={false} axisLine={false} />
            <Tooltip formatter={(value) => [`${value}k`, "Revenue"]} />
            <Bar dataKey="revenue" fill="#2563eb" radius={[8, 8, 0, 0]} />
          </BarChart>
        </ResponsiveContainer>
      </div>
      <div className="mt-3 grid gap-2 text-sm text-slate-600 md:grid-cols-2">
        <p><span className="font-semibold text-slate-900">Top customer:</span> Saudi FMCG Supply Co.</p>
        <p><span className="font-semibold text-slate-900">Worst margin lane:</span> Manassas → Washington DC</p>
      </div>
    </div>
  );
}

function MapPreview() {
  return (
    <div className="map-surface min-h-[320px] p-5">
      <svg className="absolute inset-0 h-full w-full" viewBox="0 0 900 420" preserveAspectRatio="none">
        <path d="M72 300 C210 190 285 230 405 130 S650 95 810 250" fill="none" stroke="#2563eb" strokeWidth="4" strokeDasharray="10 10" opacity=".75" />
        <path d="M90 105 C250 150 315 260 505 230 S660 320 820 150" fill="none" stroke="#0d9488" strokeWidth="4" opacity=".65" />
        <circle cx="420" cy="134" r="42" fill="#f59e0b" opacity=".12" stroke="#f59e0b" />
        <circle cx="705" cy="244" r="58" fill="#ef4444" opacity=".10" stroke="#ef4444" />
      </svg>
      {[
        ["KSA-REEFER-214", "72%", "58%", "Active"],
        ["KSA-REEFER-119", "46%", "31%", "Temp Watch"],
        ["BOX-106", "77%", "59%", "Delayed"],
        ["DXB-VAN-045", "24%", "71%", "Delivered"],
      ].map(([label, left, top, status]) => (
        <div key={label} className="absolute" style={{ left, top }}>
          <div className="rounded-full border border-white bg-blue-600 p-2 shadow-lg">
            <Truck className="h-4 w-4 text-white" />
          </div>
          <div className="mt-2 rounded-lg border border-slate-200 bg-white/95 px-2 py-1 text-xs font-semibold text-slate-700 shadow-sm">
            {label} · {status}
          </div>
        </div>
      ))}
      <div className="relative z-10 max-w-sm rounded-2xl border border-white/70 bg-white/90 p-4 shadow-sm">
        <p className="section-title text-blue-700">Map-ready architecture</p>
        <h3 className="mt-2 text-lg font-bold text-slate-900">Live routing without paid map dependency</h3>
        <p className="mt-2 text-sm leading-6 text-slate-600">Route lines, vehicle pins, geofence zones, SLA hotspots and exceptions are structured so a real provider can be dropped in later.</p>
      </div>
    </div>
  );
}

function Fleet360MapPage() {
  const monitors = vehicles.map(vehicleMonitor);
  const [selectedId, setSelectedId] = useState(String(monitors[0]?.vehicle.vehicleId ?? ""));
  const [mode, setMode] = useState("All");
  const selected = monitors.find((item) => String(item.vehicle.vehicleId) === selectedId) ?? monitors[0];
  const filteredMonitors = monitors.filter((item) => {
    if (mode === "All") return true;
    if (mode === "On Job") return Boolean(item.shipment && item.shipment.currentStatus !== "Delivered");
    if (mode === "Violations") return item.violations.length > 0;
    if (mode === "Device Risk") return Boolean(item.device && !String(item.device.status).match(/Online/i));
    if (mode === "Maintenance Risk") return /Critical|Due|Maintenance/i.test(String(item.vehicle.maintenanceStatus));
    return true;
  });

  return (
    <div className="alerts-command-room space-y-5">
      <PageHeader
        eyebrow="Control Tower"
        title="Map View"
        description="360-degree fleet monitoring from total fleet posture down to individual vehicle job, stops, violations, telemetry, device health and dispatch action."
        actions={
          <>
            <button
              className="btn-ghost"
              onClick={() => exportCsv("vehicle-trail", filteredMonitors.map((item) => ({
                vehicleId: item.vehicle.vehicleId,
                driver: item.driver?.name ?? item.vehicle.assignedDriver,
                location: item.vehicle.currentLocation,
                speed: item.position.speed,
                heading: item.position.heading,
                risk: item.risk,
                shipmentId: item.shipment?.shipmentId ?? "",
              })))}
            >
              <Download className="h-4 w-4" /> Export Vehicle Trail
            </button>
            <button className="btn-primary" onClick={() => setMode("Violations")}>
              <Sparkles className="h-4 w-4" /> Run Fleet AI Scan
            </button>
          </>
        }
      />

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
        <KpiCard label="Fleet Monitored" value={vehicles.length} status="Active" />
        <KpiCard label="Vehicles On Job" value={monitors.filter((item) => item.shipment && item.shipment.currentStatus !== "Delivered").length} status="Active" />
        <KpiCard label="Open Violations" value={monitors.reduce((sum, item) => sum + item.violations.length, 0)} status="Risk" />
        <KpiCard label="Device Blind Spots" value={monitors.filter((item) => item.device && !String(item.device.status).match(/Online/i)).length} status="Risk" />
        <KpiCard label="Dispatch Blocks" value={monitors.filter((item) => /High/.test(item.risk)).length} status="Critical" />
      </div>

      <div className="panel flex flex-wrap gap-2 p-3">
        {["All", "On Job", "Violations", "Device Risk", "Maintenance Risk"].map((item) => (
          <button key={item} className={mode === item ? "btn-primary py-2 text-xs" : "btn-ghost py-2 text-xs"} onClick={() => setMode(item)}>
            {item}
          </button>
        ))}
      </div>

      <div className="grid gap-5 2xl:grid-cols-[280px_1fr_420px]">
        <div className="panel overflow-hidden">
          <div className="border-b border-slate-100 p-4">
            <p className="section-title">Fleet Roster</p>
            <p className="mt-1 text-sm text-slate-500">Click any unit for granular monitoring.</p>
          </div>
          <div className="max-h-[620px] overflow-y-auto p-3">
            {filteredMonitors.map((item) => (
              <button
                key={String(item.vehicle.vehicleId)}
                className={`mb-2 w-full rounded-xl border p-3 text-left transition ${selectedId === item.vehicle.vehicleId ? "border-blue-300 bg-blue-50" : "border-slate-100 bg-white hover:bg-slate-50"}`}
                onClick={() => setSelectedId(String(item.vehicle.vehicleId))}
              >
                <div className="flex items-start justify-between gap-2">
                  <div>
                    <p className="font-bold text-slate-900">{String(item.vehicle.vehicleId)}</p>
                    <p className="mt-1 text-xs text-slate-500">{String(item.vehicle.vehicleType)} · {String(item.vehicle.currentLocation)}</p>
                  </div>
                  <RiskBadge risk={item.risk} />
                </div>
                <div className="mt-3 flex flex-wrap gap-1.5">
                  <StatusBadge status={item.monitorStatus} />
                  {item.shipment && <span className="rounded-full border border-blue-100 bg-blue-50 px-2 py-0.5 text-[10px] font-bold text-blue-700">{String(item.shipment.shipmentId)}</span>}
                </div>
              </button>
            ))}
          </div>
        </div>

        <div className="map-surface min-h-[660px] p-5">
          <svg className="absolute inset-0 h-full w-full" viewBox="0 0 1100 700" preserveAspectRatio="none">
            <path d="M65 520 C220 360 335 440 480 260 S785 145 1010 390" fill="none" stroke="#2563eb" strokeWidth="5" strokeDasharray="12 12" opacity=".75" />
            <path d="M130 160 C310 190 410 390 620 360 S820 515 1015 180" fill="none" stroke="#0d9488" strokeWidth="5" opacity=".65" />
            <path d="M180 575 C330 510 395 560 520 490 S760 430 910 545" fill="none" stroke="#7c3aed" strokeWidth="4" opacity=".45" />
            <circle cx="505" cy="238" r="70" fill="#f59e0b" opacity=".12" stroke="#f59e0b" strokeWidth="2" />
            <circle cx="846" cy="395" r="86" fill="#ef4444" opacity=".10" stroke="#ef4444" strokeWidth="2" />
            <circle cx="292" cy="505" r="58" fill="#0d9488" opacity=".10" stroke="#0d9488" strokeWidth="2" />
          </svg>
          <div className="relative z-10 flex flex-wrap items-center gap-2">
            {["Route line", "Geofence", "Delay zone", "Violation zone", "Cold chain"].map((item) => (
              <span key={item} className="rounded-full border border-white/80 bg-white/90 px-3 py-1 text-xs font-semibold text-slate-600 shadow-sm">{item}</span>
            ))}
          </div>
          {monitors.map((item) => {
            const isSelected = selectedId === item.vehicle.vehicleId;
            const isRisk = item.risk === "High";
            return (
              <button
                key={String(item.vehicle.vehicleId)}
                className="absolute text-left"
                style={{ left: item.position.left, top: item.position.top }}
                onClick={() => setSelectedId(String(item.vehicle.vehicleId))}
              >
                <div className={`rounded-full border-2 border-white p-2 shadow-xl ${isSelected ? "bg-blue-700 ring-4 ring-blue-200" : isRisk ? "bg-red-600" : "bg-teal-600"}`}>
                  <Truck className="h-5 w-5 text-white" />
                </div>
                <div className={`mt-2 min-w-[170px] rounded-xl border bg-white/95 px-3 py-2 shadow-lg ${isSelected ? "border-blue-300" : "border-slate-200"}`}>
                  <div className="flex items-center justify-between gap-2">
                    <p className="text-xs font-extrabold text-slate-900">{String(item.vehicle.vehicleId)}</p>
                    <span className={`h-2 w-2 rounded-full ${item.risk === "High" ? "bg-red-500" : item.risk === "Medium" ? "bg-amber-400" : "bg-emerald-500"}`} />
                  </div>
                  <p className="mt-1 text-[11px] text-slate-500">{String(item.monitorStatus)} · {item.position.speed}</p>
                </div>
              </button>
            );
          })}
          <div className="absolute bottom-5 left-5 right-5 grid gap-3 lg:grid-cols-3">
            {alerts.slice(0, 3).map((alert) => (
              <div key={String(alert.alertId)} className="rounded-xl border border-white/80 bg-white/95 p-3 shadow-sm">
                <div className="flex items-start justify-between gap-2">
                  <p className="text-sm font-bold text-slate-900">{alert.type}</p>
                  <RiskBadge risk={alert.severity} />
                </div>
                <p className="mt-1 text-xs text-slate-500">{alert.entity ?? "Unmapped entity"} · {alert.location ?? "Live backend"}</p>
              </div>
            ))}
          </div>
        </div>

        {selected && (
          <div className="space-y-5">
            <div className="panel p-5">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <p className="section-title">Selected Vehicle</p>
                  <h2 className="mt-2 text-2xl font-bold text-slate-900">{String(selected.vehicle.vehicleId)}</h2>
                  <p className="mt-1 text-sm text-slate-500">{String(selected.vehicle.makeModel)} · {String(selected.vehicle.plateNumber)}</p>
                </div>
                <RiskBadge risk={selected.risk} />
              </div>
              <div className="mt-4 grid gap-3 sm:grid-cols-2">
                {[
                  ["Status", selected.monitorStatus],
                  ["Driver", selected.driver?.name ?? selected.vehicle.assignedDriver],
                  ["Location", selected.vehicle.currentLocation],
                  ["Speed / Heading", `${selected.position.speed} · ${selected.position.heading}`],
                  ["Lat / Lng", `${selected.position.lat}, ${selected.position.lng}`],
                  ["Odometer", Number(selected.vehicle.odometer).toLocaleString()],
                  ["Device", selected.device?.deviceId ?? selected.vehicle.assignedDevice],
                  ["Device Health", selected.device?.status ?? "Unknown"],
                ].map(([label, value]) => (
                  <div key={String(label)} className="rounded-xl border border-slate-100 bg-slate-50 p-3">
                    <p className="text-xs text-slate-500">{String(label)}</p>
                    <p className="mt-1 font-bold text-slate-900">{String(value)}</p>
                  </div>
                ))}
              </div>
            </div>

            <div className="panel p-5">
              <p className="section-title">Active Job</p>
              {selected.shipment ? (
                <div className="mt-4 space-y-3">
                  <div className="flex items-start justify-between gap-3">
                    <div>
                      <p className="text-lg font-bold text-slate-900">{String(selected.shipment.shipmentId)}</p>
                      <p className="text-sm text-slate-500">{String(selected.shipment.customer)}</p>
                    </div>
                    <StatusBadge status={selected.shipment.currentStatus} />
                  </div>
                  <div className="rounded-xl border border-blue-100 bg-blue-50 p-3 text-sm text-blue-900">
                    {String(selected.shipment.origin)} → {String(selected.shipment.destination)} · ETA {String(selected.shipment.eta)}
                  </div>
                  <div className="grid gap-2 text-sm sm:grid-cols-2">
                    <p><span className="font-semibold text-slate-900">Cargo:</span> {String(selected.shipment.cargoType)}</p>
                    <p><span className="font-semibold text-slate-900">SLA:</span> {String(selected.shipment.slaRisk)}</p>
                    <p><span className="font-semibold text-slate-900">POD:</span> {String(selected.shipment.podStatus)}</p>
                    <p><span className="font-semibold text-slate-900">Invoice:</span> {String(selected.shipment.invoiceStatus)}</p>
                  </div>
                </div>
              ) : (
                <EmptyState title="No active job" subtitle="This unit is available for planning or staging." />
              )}
            </div>

            <div className="panel p-5">
              <p className="section-title">Stops & Timeline</p>
              <div className="mt-4 space-y-3">
                {selected.stops.map((stop) => (
                  <div key={`${stop.stop}-${stop.location}`} className="flex gap-3 rounded-xl border border-slate-100 bg-white p-3">
                    <div className="mt-1 h-3 w-3 rounded-full bg-blue-600" />
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center justify-between gap-2">
                        <p className="font-semibold text-slate-900">{stop.stop}</p>
                        <StatusBadge status={stop.status} />
                      </div>
                      <p className="mt-1 text-sm text-slate-500">{stop.location} · {stop.window}</p>
                    </div>
                  </div>
                ))}
              </div>
            </div>

            <div className="panel p-5">
              <p className="section-title">Violations & Exceptions</p>
              <div className="mt-4 space-y-2">
                {selected.violations.length ? selected.violations.map((violation) => (
                  <div key={violation} className="flex items-center gap-2 rounded-xl border border-red-100 bg-red-50 px-3 py-2 text-sm font-semibold text-red-700">
                    <AlertTriangle className="h-4 w-4" />
                    {violation}
                  </div>
                )) : (
                  <div className="rounded-xl border border-emerald-100 bg-emerald-50 px-3 py-2 text-sm font-semibold text-emerald-700">No active violations for this unit.</div>
                )}
              </div>
            </div>

            <AiInsightCard insight={{ title: "Fleet monitoring recommendation", body: selected.risk === "High" ? "Do not assign additional work to this vehicle until maintenance/device exceptions are resolved. Send ETA update if customer-facing SLA is affected." : "Vehicle is suitable for dispatch. Continue monitoring device heartbeat and next stop ETA.", score: selected.risk === "High" ? 94 : 87, moduleKey: "map-view" }} />
          </div>
        )}
      </div>
    </div>
  );
}

function LiveDashboardPage() {
  const activeShipments = shipments.filter((s) => s.currentStatus !== "Delivered");
  const delayed = shipments.filter((s) => calculateShipmentDelay(s).risk === "High");
  const alertsQuery = useQuery({
    queryKey: ["alerts"],
    queryFn: () => alertsApi.list(),
    staleTime: 15_000,
    refetchInterval: 15_000,
  });
  const liveAlerts = useMemo(
    () => (Array.isArray(alertsQuery.data) ? (alertsQuery.data as AnyRecord[]).map((alert) => normalizeAlert(alert)) : []),
    [alertsQuery.data],
  );
  const criticalAlerts = liveAlerts.filter((alert) => alert.severity === "Critical");
  const topAlerts = liveAlerts.slice(0, 4);
  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Control Tower"
        title="Live Dashboard"
        description="A real operating cockpit for shipments, fleet availability, exceptions, customer risk, revenue and margin."
        actions={
          <>
            <button className="btn-ghost" onClick={() => exportCsv("watchlist", activeShipments)}>
              <Download className="h-4 w-4" /> Export Watchlist
            </button>
            <button className="btn-primary" onClick={() => window.location.assign("/reports")}>
              <Sparkles className="h-4 w-4" /> Generate Operations Brief
            </button>
          </>
        }
      />
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        {[
          ["Active Shipments", activeShipments.length, "Active"],
          ["Pending Load Bookings", bookings.filter((b) => /Draft|Submitted|Quoted/.test(String(b.status))).length, "Pending"],
          ["Loads Awaiting Dispatch", bookings.filter((b) => b.status === "Awaiting Dispatch").length, "Pending"],
          ["Available Vehicles", vehicles.filter((v) => /Active|Idle/.test(String(v.status))).length, "Healthy"],
          ["Available Drivers", drivers.filter((d) => /Available|Idle/.test(String(d.availability))).length, "Healthy"],
          ["Delayed Shipments", delayed.length, "Risk"],
          ["Critical Alerts", criticalAlerts.length, "Critical"],
          ["Temperature Breaches", incidents.filter((i) => String(i.incidentType).includes("Temperature")).length, "Critical"],
          ["Safety Events", incidents.length, "Review"],
          ["Revenue This Month", formatCurrency(valueSum(customers, "revenueMtd"), "SAR"), "Healthy"],
          ["Cost This Month", formatCurrency(valueSum(shipments, "cost"), "SAR"), "Watch"],
          ["On-Time Delivery %", "94.6%", "Healthy"],
        ].map(([label, value, status]) => <KpiCard key={label} label={String(label)} value={String(value)} status={String(status)} />)}
      </div>
      <div className="grid gap-5 xl:grid-cols-[1.25fr_.75fr]">
        <DataTable rows={enrichRows(shipments)} columns={["shipmentId", "customer", "origin", "destination", "currentStatus", "vehicle", "driver", "eta", "slaRisk"]} />
        <div className="space-y-5">
          <div className="panel p-5">
            <p className="section-title">Critical Alerts</p>
            <div className="mt-4 space-y-3">
              {topAlerts.length ? topAlerts.map((alert) => (
                <div key={String(alert.id)} className="rounded-xl border border-slate-100 bg-slate-50 p-3">
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
                <div className="rounded-xl border border-emerald-100 bg-emerald-50 px-3 py-2 text-sm font-medium text-emerald-800">
                  No open alerts are currently returned by the live backend.
                </div>
              )}
            </div>
          </div>
          <div className="panel p-5">
            <p className="section-title">Fleet Availability</p>
            <div className="mt-4 grid grid-cols-2 gap-3">
              {["Active", "Idle", "In Maintenance", "Out of Service", "Unassigned"].map((status) => (
                <div key={status} className="rounded-xl border border-slate-100 bg-white p-3">
                  <p className="text-xl font-bold text-slate-900">{vehicles.filter((v) => String(v.status).includes(status)).length}</p>
                  <p className="text-xs text-slate-500">{status}</p>
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>
      <div className="grid gap-5 xl:grid-cols-[.9fr_1.1fr]">
        <RevenueSnapshot />
        <MapPreview />
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
        topAction: laneAlerts[0]?.recommendedAction || laneAlerts[0]?.body || "Waiting for live data",
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
        description="A live alert register backed by the backend ai_insights table. The queue, details, and actions all come from tenant data rather than a fixed demo feed."
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
              <Sparkles className="h-4 w-4" /> Refresh Live Queue
            </button>
          </>
        }
      />

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <KpiCard label="Open Alerts" value={summary.open} status="Open" />
        <KpiCard label="Critical Alerts" value={summary.critical} status="Critical" />
        <KpiCard label="Acknowledged" value={summary.acknowledged} status="Review" />
        <KpiCard label="Closed" value={summary.closed} status="Healthy" />
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
                    <span className="text-xs font-semibold text-slate-400">{alert.age ?? "Live"}</span>
                  </div>
                  <h2 className="mt-2 text-[1.02rem] font-bold text-slate-900">{alert.title}</h2>
                  <p className="mt-1 text-sm text-slate-600">
                    {alert.entity ?? alert.entityType ?? "Unmapped entity"} · {alert.customer ?? "Tenant scoped"} · {alert.location ?? "Live backend"}
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
          {!visibleAlerts.length && <EmptyState title="No alerts in this lane" subtitle="Try another tab, clear the search, or wait for the live backend to surface new rows." />}
        </div>

        <div className="space-y-4 xl:sticky xl:top-4 xl:self-start">
          <div className="panel p-4">
            <p className="section-title">Live triage guidance</p>
            <div className="mt-3 space-y-2.5">
              <div className="rounded-xl border border-red-100 bg-gradient-to-br from-red-50 to-rose-50 px-3 py-2 text-sm text-red-800 shadow-inner">
                <span className="font-bold">Criticals:</span> {summary.critical} open alert{summary.critical === 1 ? "" : "s"} need immediate review.
              </div>
              <div className="rounded-xl border border-amber-100 bg-gradient-to-br from-amber-50 to-orange-50 px-3 py-2 text-sm text-amber-800 shadow-inner">
                <span className="font-bold">Queue depth:</span> {summary.total} total live alert{summary.total === 1 ? "" : "s"} are visible for this tenant.
              </div>
              <div className="rounded-xl border border-slate-100 bg-gradient-to-br from-white to-stone-50 px-3 py-2 text-sm text-slate-700 shadow-inner">
                <span className="font-bold">Next move:</span> clear the top critical item, then work down by age and category.
              </div>
            </div>
          </div>

          <div className="panel p-4">
            <p className="section-title">Recent Live Signals</p>
            <div className="mt-3 space-y-2.5">
              {recentSignals.length ? recentSignals.map((alert) => (
                <div key={String(alert.id)} className="rounded-xl border border-slate-100 bg-gradient-to-br from-[#fffdf8] to-[#f7efe2] p-3 shadow-sm">
                  <div className="flex items-center justify-between">
                    <p className="text-xs font-bold uppercase tracking-[0.14em] text-slate-500">
                      {String(alert.category)} · {String(alert.severity)}
                    </p>
                    <span className="text-xs text-slate-400">{alert.age ?? "Live"}</span>
                  </div>
                  <p className="mt-1 text-sm font-semibold text-slate-900">{alert.title}</p>
                  <p className="mt-1 text-sm text-slate-700">{alert.recommendedAction || alert.body || "No recommended action recorded."}</p>
                </div>
              )) : (
                <div className="rounded-xl border border-emerald-100 bg-emerald-50 px-3 py-2 text-sm text-emerald-800">
                  No live alert activity is available yet.
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
  const [urgency, setUrgency] = useState("Standard");
  const [vehicleType, setVehicleType] = useState("Reefer");
  const base = vehicleType === "Reefer" ? 4200 : 2900;
  const urgencyFee = urgency === "Express" ? 850 : 0;
  const fuel = Math.round(base * 0.12);
  const toll = 135;
  const waiting = 220;
  const internalCost = Math.round((base + urgencyFee) * 0.72 + fuel + toll);
  const finalQuote = base + urgencyFee + fuel + toll + waiting;
  const profit = calculateProfitability(finalQuote, internalCost);

  return (
    <div className="space-y-6">
      <PageHeader eyebrow="Commercial" title="Price Simulation" description="Simulate lane pricing from contract, cargo, vehicle requirement, temperature control, urgency, fuel and margin." />
      <div className="grid gap-5 lg:grid-cols-[.85fr_1.15fr]">
        <div className="panel p-5">
          <p className="section-title">Quote Inputs</p>
          <div className="mt-4 grid gap-3">
            {[
              ["Customer", "Al Noor Pharma Distribution"],
              ["Contract", "CON-1002"],
              ["Origin", "Jeddah Pharma Hub"],
              ["Destination", "Riyadh Hospital Network"],
              ["Cargo Type", "Temperature-controlled vaccines"],
              ["Weight", "4,200 kg"],
            ].map(([label, value]) => (
              <label key={label} className="text-sm font-semibold text-slate-700">
                {label}
                <input className="field mt-1.5" defaultValue={value} />
              </label>
            ))}
            <label className="text-sm font-semibold text-slate-700">
              Vehicle Type
              <select className="field mt-1.5" value={vehicleType} onChange={(event) => setVehicleType(event.target.value)}>
                <option>Reefer</option>
                <option>Dry Van</option>
                <option>Flatbed</option>
                <option>Last-mile Van</option>
              </select>
            </label>
            <label className="text-sm font-semibold text-slate-700">
              Urgency
              <select className="field mt-1.5" value={urgency} onChange={(event) => setUrgency(event.target.value)}>
                <option>Standard</option>
                <option>Express</option>
              </select>
            </label>
          </div>
        </div>
        <div className="panel p-5">
          <div className="flex items-center justify-between">
            <div>
              <p className="section-title">Calculated Quote</p>
              <h2 className="mt-2 text-2xl font-bold text-slate-900">{formatCurrency(finalQuote, "SAR")}</h2>
            </div>
            <StatusBadge status={profit.status} />
          </div>
          <div className="mt-5 grid gap-3 sm:grid-cols-2">
            {[
              ["Estimated distance", "949 km"],
              ["Base rate", formatCurrency(base, "SAR")],
              ["Fuel surcharge", formatCurrency(fuel, "SAR")],
              ["Toll estimate", formatCurrency(toll, "SAR")],
              ["Waiting/loading", formatCurrency(waiting, "SAR")],
              ["Internal cost", formatCurrency(internalCost, "SAR")],
              ["Gross margin", profit.marginText],
              ["VAT/tax", formatCurrency(Math.round(finalQuote * 0.15), "SAR")],
            ].map(([label, value]) => (
              <div key={label} className="rounded-xl border border-slate-100 bg-slate-50 p-3">
                <p className="text-xs text-slate-500">{label}</p>
                <p className="mt-1 font-bold text-slate-900">{value}</p>
              </div>
            ))}
          </div>
          <div className="mt-5 flex flex-wrap gap-2">
            <button className="btn-primary" onClick={() => window.location.assign("/quotations")}><FileText className="h-4 w-4" /> Convert to Quotation</button>
            <button className="btn-ghost" onClick={() => window.location.assign("/load-bookings")}><PackageCheck className="h-4 w-4" /> Convert to Load Booking</button>
          </div>
          <AiInsightCard insight={{ title: "Margin guardrail", body: "The reefer lane remains profitable only if waiting time stays under 90 minutes. Add detention terms to the quote.", score: 91 }} />
        </div>
      </div>
    </div>
  );
}

function DispatchBoardPage() {
  const unassigned = bookings.filter((booking) => /Awaiting Dispatch|Confirmed/.test(String(booking.status)));
  return (
    <div className="space-y-6">
      <PageHeader eyebrow="Transport Operations" title="Dispatch Board" description="Match confirmed bookings to available vehicles and drivers using vehicle type, location, maintenance, cold chain and safety fit." />
      <div className="grid gap-3 md:grid-cols-4">
        <KpiCard label="Awaiting Dispatch" value={unassigned.length} status="Pending" />
        <KpiCard label="Recommended Matches" value="3" status="AI" />
        <KpiCard label="Blocked Vehicles" value={vehicles.filter((v) => /Maintenance|Service/.test(String(v.status))).length} status="Risk" />
        <KpiCard label="Dispatch Readiness" value="87%" status="Healthy" />
      </div>
      <div className="grid gap-5 xl:grid-cols-[1fr_1fr]">
        <div className="panel p-5">
          <p className="section-title">Unassigned confirmed bookings</p>
          <div className="mt-4 space-y-3">
            {unassigned.map((booking) => (
              <div key={booking.bookingId} className="rounded-2xl border border-slate-100 bg-white p-4 shadow-sm">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <p className="font-bold text-slate-900">{booking.bookingId} · {booking.customer}</p>
                    <p className="mt-1 text-sm text-slate-500">{booking.pickup} → {booking.dropoff}</p>
                  </div>
                  <StatusBadge status={booking.status} />
                </div>
                <div className="mt-3 grid gap-2 text-xs text-slate-600 sm:grid-cols-3">
                  <span>Vehicle: {booking.vehicleRequired}</span>
                  <span>Pickup: {booking.pickupDateTime}</span>
                  <span>Deadline: {booking.deliveryDeadline}</span>
                </div>
                <div className="mt-3 flex flex-wrap gap-2">
                  <button className="btn-primary py-2 text-xs" onClick={() => window.location.assign("/jobs")}><CheckCircle2 className="h-3.5 w-3.5" /> Assign recommended</button>
                  <button className="btn-ghost py-2 text-xs" onClick={() => window.location.assign("/vehicles")}>Override</button>
                  <button className="btn-ghost py-2 text-xs" onClick={() => window.location.assign("/route-planning")}>View route</button>
                </div>
              </div>
            ))}
          </div>
        </div>
        <div className="space-y-5">
          <div className="panel p-5">
            <p className="section-title">Available vehicles</p>
            <div className="mt-4 grid gap-3">
              {vehicles.map((vehicle) => (
                <div key={vehicle.vehicleId} className="rounded-xl border border-slate-100 bg-slate-50 p-3">
                  <div className="flex items-center justify-between">
                    <p className="font-semibold text-slate-900">{vehicle.vehicleId} · {vehicle.vehicleType}</p>
                    <StatusBadge status={vehicle.status} />
                  </div>
                  <p className="mt-1 text-xs text-slate-500">{vehicle.currentLocation} · {vehicle.maintenanceStatus} · Device {vehicle.assignedDevice}</p>
                </div>
              ))}
            </div>
          </div>
          <div className="panel p-5">
            <p className="section-title">Recommended driver match</p>
            <div className="mt-4 space-y-3">
              {drivers.slice(0, 3).map((driver, index) => (
                <div key={driver.driverId} className="flex items-center justify-between rounded-xl border border-slate-100 bg-white p-3">
                  <div>
                    <p className="font-semibold text-slate-900">{driver.name}</p>
                    <p className="text-xs text-slate-500">{driver.currentCity} · HOS {driver.hosStatus} · Safety {driver.safetyScore}</p>
                  </div>
                  <RiskBadge risk={index === 0 ? "Low" : index === 1 ? "Medium" : "High"} />
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>
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
          <button className="btn-ghost" type="button" onClick={() => exportCsv(definition.title, rows)}>
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
        {definition.kpis.map((kpi) => <KpiCard key={kpi.label} label={kpi.label} value={kpi.value} status={kpi.status} trend={kpi.trend} />)}
      </div>
      <ModuleToolbar search={search} setSearch={setSearch} filter={filter} setFilter={setFilter} />
      <div className="grid gap-5 xl:grid-cols-[1fr_360px]">
        <DataTable rows={rows} columns={definition.columns} onSelect={setSelected} />
        <div className="space-y-5">
          <AiInsightCard insight={{ title: `${definition.title} recommendation`, body: definition.insight, score: 89, moduleKey }} />
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
