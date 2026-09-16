export type GatedModule = { key: string; label: string; blurb: string };

export const GATED_MODULES: GatedModule[] = [
  { key: "safety",          label: "Safety",          blurb: "Safety events, dashcam, incidents, coaching, traffic violations" },
  { key: "maintenance",     label: "Maintenance",     blurb: "Work orders, PM schedules, DVIR, service history, downtime" },
  { key: "dispatch",        label: "Dispatch",        blurb: "Dispatch board, jobs, trips, routes, smart assign, last mile" },
  { key: "telematics",      label: "Telematics",      blurb: "Live telemetry, devices, ELD, geofences" },
  { key: "crm",             label: "CRM",             blurb: "Customers, contracts, leads, opportunities, quotations, rate cards" },
  { key: "customer_portal", label: "Customer Portal", blurb: "Customer-facing portal, ETA sharing, shipment visibility" },
  { key: "reports",         label: "Reports",         blurb: "Reporting and analytics" },
  { key: "compliance",      label: "Compliance",      blurb: "Compliance rules, HOS, audit packages" },
  { key: "integrations",    label: "Integrations",    blurb: "Third-party connectors, provider sync, tests and external actions" },
];

// The one-time activation artifact for an invited tenant administrator. Shown whenever an
// invite is issued: when SMTP delivered it this is the backup copy, and when it did not, this
// is the ONLY way the invited person can ever set a password. Previously the API returned
// neither the link nor the token, so an invite with email unconfigured left the user Pending
// with nothing to send them.
