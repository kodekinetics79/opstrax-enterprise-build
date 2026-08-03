namespace Opstrax.Api.Services;

/// <summary>
/// Server-side release-control catalog for every tenant-visible UI module.
/// Platform snapshots derive ownership counts and the per-entitlement breakdown
/// from these records; tests reconcile this catalog with the SPA module catalog.
/// </summary>
public static class PlatformTenantModuleCatalog
{
    public sealed record Module(string Key, string? RequiredEntitlement);

    public static readonly IReadOnlyList<Module> Modules =
    [
        new("command-center", null), new("fleet-health", null), new("live-dashboard", null),
        new("map-view", "telematics"), new("fleet-live-wall", "telematics"), new("alerts", null),
        new("active-shipments", null), new("geofences", "telematics"), new("leads", "crm"),
        new("sales-pipeline", "crm"), new("opportunities", "crm"), new("campaigns", "crm"),
        new("account-health", null), new("follow-ups", null), new("support-tickets", null),
        new("renewals", null), new("upsell-opportunities", null), new("customers", "crm"),
        new("contracts", "crm"), new("rate-cards", "crm"), new("price-simulation", "crm"),
        new("quotations", "crm"), new("load-bookings", null), new("shipments", null),
        new("dispatch-board", "dispatch"), new("jobs", "dispatch"), new("trips", "dispatch"),
        new("route-plans", "dispatch"), new("proof-of-delivery", "dispatch"),
        new("last-mile-delivery", "dispatch"), new("operations-proof-center", null),
        new("logistics-workspace", null), new("customer-eta", "customer_portal"),
        new("customer-portal", "customer_portal"), new("customer-visibility", "customer_portal"),
        new("fleet-utilization", null), new("fleet-workspace", null), new("fleet-cold-chain", null),
        new("fleet-assets", null), new("fleet-saudi-readiness", null),
        new("fleet-compliance", "compliance"), new("vehicles", null), new("drivers", null),
        new("owners", null), new("assignments", null), new("documents", null),
        new("iot-devices", "telematics"), new("telematics-control-tower", "telematics"),
        new("gps-tracking", "telematics"), new("obd-j1939", "telematics"),
        new("cold-chain", "telematics"), new("dashcam", "safety"),
        new("sensor-health", "telematics"), new("driver-scorecards", "safety"),
        new("safety-center", "safety"), new("incidents", "safety"), new("coaching", "safety"),
        new("dvir-inspections", "maintenance"), new("hos-eld", "compliance"),
        new("traffic-violations", "safety"), new("evidence-packages", "safety"),
        new("work-orders", "maintenance"), new("maintenance-center", "maintenance"),
        new("service-history", "maintenance"), new("downtime", "maintenance"),
        new("preventive-maintenance", "maintenance"), new("fuel-idling", null),
        new("expenses", null), new("invoices", null), new("ar-aging", null), new("payments", null),
        new("profitability", null), new("tax-config", null), new("billing-consolidation", null),
        new("driver-pay", null), new("revenue-recognition", null), new("user-management", null),
        new("audit-logs", null), new("integrations", "integrations"), new("carbon-tracking", null),
        new("digital-forms", null), new("alert-rules", null), new("driver-messaging", null),
        new("workforce", null), new("feature-flags", null), new("ai-copilot", null),
        new("predictive-analytics", null), new("control-tower", null),
        new("reports-analytics", "reports"), new("compliance-center", "compliance"), new("about", null),
    ];
}
