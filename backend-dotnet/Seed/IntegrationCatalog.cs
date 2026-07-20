using System.Text.Json;
using Opstrax.Api.Data;

namespace Opstrax.Api.Seed;

// Canonical connector catalog for the Integrations module — the .NET port of the
// Node side-service registry (backend/src/modules/integrations/integrations.registry.ts).
//
// WHY THIS EXISTS: the frontend Integrations page was rendering empty because it
// called the Node :8090 side-service (which hydrates this catalog on read) while the
// primary .NET API on Render never seeded the `integrations` table — it just SELECTed
// an empty result. Porting the catalog here and hydrating on read from the .NET
// handler makes the module self-heal per tenant regardless of the Node service, so
// every real company sees the full connector marketplace, not a blank screen.
//
// Hydration is idempotent and tenant-scoped: for each catalog entry a tenant is
// missing (matched by integration_key), one row is inserted with is_custom=false.
// Existing rows (including a tenant's own connect/configure state and custom
// connectors) are never overwritten. This is CATALOG reference data — the list of
// connectors a fleet CAN wire up — not fabricated operational data.
public static class IntegrationCatalog
{
    public sealed record Entry(
        string Key,
        string Name,
        string Category,
        string Description,
        string Logo,
        string Status,
        string SyncLabel,
        string? LastSyncAt,
        string[] RelatedSystems,
        string[] ConnectedTo,
        string ManagedBy,
        object Config);

    public static readonly IReadOnlyList<Entry> Entries = new List<Entry>
    {
        new("sap-s4hana", "SAP S/4HANA", "ERP & Accounting",
            "Order, invoice, GL posting, and cost-center synchronization for enterprise finance.",
            "SAP", "Connected", "2 min ago", "2026-06-24T14:11:00Z",
            new[]{"orders","invoices","ledger"}, new[]{"ERP","Accounting"}, "Finance Ops",
            new { baseUrl = "https://sap.example.com", companyCode = "NSFL" }),
        new("oracle-netsuite", "Oracle NetSuite", "ERP & Accounting",
            "Cloud ERP for financials, inventory, and multi-subsidiary accounting.",
            "ORA", "Disconnected", "—", null,
            new[]{"ledger","inventory"}, new[]{"ERP"}, "Finance Ops", new { }),
        new("microsoft-dynamics", "Microsoft Dynamics 365", "ERP & Accounting",
            "ERP + CRM synchronization for finance, orders, and service workflows.",
            "MS", "Pending", "—", null,
            new[]{"crm","orders","billing"}, new[]{"ERP","CRM"}, "Finance Ops", new { }),
        new("quickbooks-online", "QuickBooks Online", "ERP & Accounting",
            "SMB accounting, expense sync, and invoice posting for smaller fleets.",
            "QB", "Connected", "5 min ago", "2026-06-24T14:08:00Z",
            new[]{"invoices","expenses"}, new[]{"Accounting"}, "Finance Ops",
            new { companyId = "qb-northshore" }),
        new("xero", "Xero", "ERP & Accounting",
            "Online accounting with bank reconciliation and multi-currency reporting.",
            "XER", "Error", "14h ago", "2026-06-23T23:18:00Z",
            new[]{"invoices","payments"}, new[]{"Accounting"}, "Finance Ops", new { }),
        new("sage-intacct", "Sage Intacct", "ERP & Accounting",
            "Cloud financial management for revenue recognition and project accounting.",
            "SGE", "Disconnected", "—", null,
            new[]{"finance"}, new[]{"Accounting"}, "Finance Ops", new { }),
        new("samsara", "Samsara", "Telematics & ELD",
            "GPS tracking, ELD, and AI dashcam event integration for live fleet operations.",
            "SAM", "Connected", "Real-time", "2026-06-24T14:14:00Z",
            new[]{"vehicles","drivers","safety"}, new[]{"GPS","ELD","Dashcam"}, "Fleet Ops",
            new { providerAccountId = "sam-1001" }),
        new("geotab", "Geotab", "Telematics & ELD",
            "Fleet telematics and open SDK for GPS, diagnostics, and log data.",
            "GEO", "Disconnected", "—", null,
            new[]{"vehicles","diagnostics"}, new[]{"GPS","Diagnostics"}, "Fleet Ops", new { }),
        new("verizon-connect", "Verizon Connect", "Telematics & ELD",
            "Fleet tracking and dispatch telemetry for location and duty status.",
            "VZN", "Disconnected", "—", null,
            new[]{"dispatch","location"}, new[]{"GPS"}, "Fleet Ops", new { }),
        new("motive", "Motive", "Telematics & ELD",
            "ELD, DVIR, and driver safety event integration for regulated fleets.",
            "MOT", "Pending", "—", null,
            new[]{"eld","dvir","safety"}, new[]{"ELD","Dashcam"}, "Fleet Ops",
            new { webhookUrl = "" }),
        new("platform-science", "Platform Science", "Telematics & ELD",
            "Carrier platform for driver apps, dispatch, and compliance workflows.",
            "PS", "Disconnected", "—", null,
            new[]{"driver-app","dispatch"}, new[]{"ELD"}, "Fleet Ops", new { }),
        new("wex-fuel-card", "WEX Fuel Card", "Fuel Cards",
            "Fuel transactions, odometer capture, and anomaly detection sync.",
            "WEX", "Connected", "1 min ago", "2026-06-24T14:15:00Z",
            new[]{"fuel","cost"}, new[]{"Fuel"}, "Finance Ops",
            new { accountId = "wex-88231" }),
        new("fleetcor", "Fleetcor / Corpay", "Fuel Cards",
            "Fuel and spend controls with vehicle-level transaction visibility.",
            "FLC", "Pending", "—", null,
            new[]{"fuel","spend-controls"}, new[]{"Fuel"}, "Finance Ops", new { }),
        new("comdata", "Comdata", "Fuel Cards",
            "Fuel, toll, and cash card transaction network integration.",
            "CMD", "Disconnected", "—", null,
            new[]{"fuel","tolls"}, new[]{"Fuel"}, "Finance Ops", new { }),
        new("shell-fleet", "Shell Fleet", "Fuel Cards",
            "Global fuel card support with international fleet transaction feeds.",
            "SHL", "Disconnected", "—", null,
            new[]{"fuel","international"}, new[]{"Fuel"}, "Finance Ops", new { }),
        new("google-maps-platform", "Google Maps Platform", "Maps & Routing",
            "Routing, geocoding, distance matrix, and live traffic for dispatch planning.",
            "GGL", "Connected", "Active", "2026-06-24T14:13:00Z",
            new[]{"routes","eta","geocoding"}, new[]{"Maps","Routing"}, "Dispatch Ops",
            new { apiKey = "maps-***" }),
        new("here-maps", "HERE Maps", "Maps & Routing",
            "Truck routing and coverage for regional operations.",
            "HRE", "Disconnected", "—", null,
            new[]{"routes","eta"}, new[]{"Maps"}, "Dispatch Ops", new { }),
        new("mapbox", "Mapbox", "Maps & Routing",
            "Customizable live maps for vehicle tracking and geo overlays.",
            "MPB", "Disconnected", "—", null,
            new[]{"live-map","geofences"}, new[]{"Maps"}, "Dispatch Ops", new { }),
        new("ptv-route-optimiser", "PTV Route Optimiser", "Maps & Routing",
            "Multi-stop route optimization with vehicle and time-window constraints.",
            "PTV", "Pending", "—", null,
            new[]{"route-plans","dispatch"}, new[]{"Routing"}, "Dispatch Ops", new { }),
        new("whatsapp-business", "WhatsApp Business API", "Messaging & Notifications",
            "Customer ETA, delay, and delivery notifications via WhatsApp.",
            "WA", "Connected", "Just now", "2026-06-24T14:16:00Z",
            new[]{"customer-notifications","eta"}, new[]{"Messaging"}, "Ops Communications",
            new { sender = "OpsTrax" }),
        new("twilio-sms", "Twilio SMS", "Messaging & Notifications",
            "SMS delivery for driver and customer communications.",
            "TWL", "Connected", "8 min ago", "2026-06-24T14:07:00Z",
            new[]{"sms","driver-alerts"}, new[]{"Messaging"}, "Ops Communications",
            new { sender = "OpsTrax" }),
        new("slack", "Slack", "Messaging & Notifications",
            "Critical alerting and operational notifications into Slack channels.",
            "SLK", "Connected", "3 min ago", "2026-06-24T14:12:00Z",
            new[]{"alerts","escalations"}, new[]{"Messaging"}, "Ops Communications",
            new { channel = "#ops" }),
        new("microsoft-teams", "Microsoft Teams", "Messaging & Notifications",
            "Enterprise channel alerts for operations and compliance teams.",
            "MST", "Disconnected", "—", null,
            new[]{"alerts","compliance"}, new[]{"Messaging"}, "Ops Communications", new { }),
        new("sendgrid-email", "SendGrid Email", "Messaging & Notifications",
            "Transactional email for invoices, reports, and compliance notices.",
            "SG", "Connected", "12 min ago", "2026-06-24T14:04:00Z",
            new[]{"reports","notices","invoices"}, new[]{"Messaging"}, "Ops Communications",
            new { sender = "ops@opstrax.com" }),
        new("sap-extended-wms", "SAP Extended WMS", "WMS & Shipment Ops",
            "Warehouse pick, pack, and carrier booking synchronization.",
            "SWMS", "Connected", "4 min ago", "2026-06-24T14:09:00Z",
            new[]{"warehouse","shipments"}, new[]{"WMS"}, "Supply Chain Ops",
            new { warehouseCode = "DC-01" }),
        new("manhattan-wms", "Manhattan WMS", "WMS & Shipment Ops",
            "Enterprise warehouse execution and labor management integration.",
            "MWM", "Disconnected", "—", null,
            new[]{"warehouse","labor"}, new[]{"WMS"}, "Supply Chain Ops", new { }),
        new("aws-iot-core", "AWS IoT Core", "IoT & Sensors",
            "Message broker for sensor telemetry and device shadow state.",
            "AWS", "Connected", "Real-time", "2026-06-24T14:16:00Z",
            new[]{"telemetry","sensors"}, new[]{"IoT"}, "Telematics Ops",
            new { topicPrefix = "opstrax/fleet" }),
        new("azure-iot-hub", "Azure IoT Hub", "IoT & Sensors",
            "Enterprise IoT gateway with device management and stream analytics.",
            "AZR", "Disconnected", "—", null,
            new[]{"telemetry","devices"}, new[]{"IoT"}, "Telematics Ops", new { }),
        new("trimble-tmt", "Trimble TMT", "IoT & Sensors",
            "Maintenance and device telemetry feed for fleet health workflows.",
            "TMB", "Pending", "—", null,
            new[]{"maintenance","telemetry"}, new[]{"IoT","Maintenance"}, "Telematics Ops", new { }),
        new("fmcsa-portal", "FMCSA Portal", "Compliance",
            "Compliance data exchange for carrier authority and safety visibility.",
            "FMC", "Connected", "Daily", "2026-06-24T09:00:00Z",
            new[]{"compliance","safety"}, new[]{"Compliance"}, "Compliance Ops",
            new { profile = "US FMCSA" }),
        new("ifta-reporting", "IFTA Reporting", "Compliance",
            "Mileage reconciliation and quarterly IFTA filing support.",
            "IFT", "Pending", "Weekly", null,
            new[]{"compliance","fuel"}, new[]{"Compliance"}, "Compliance Ops", new { }),
    };

    // Idempotent, tenant-scoped hydration. Inserts only the catalog entries a tenant
    // is missing (by integration_key); never overwrites existing rows or a tenant's
    // own connect/configure state. Safe to call on every list read.
    public static async Task EnsureTenantAsync(Database db, long companyId, CancellationToken ct)
    {
        foreach (var e in Entries)
        {
            await db.ExecuteAsync(
                @"INSERT INTO integrations
                      (company_id, provider_name, category, status, integration_key, description,
                       logo, sync_label, last_sync_at, related_systems_json, connected_to_json,
                       managed_by, scope, config_json, is_custom, updated_at)
                  SELECT @cid, @name, @cat, @status, @key, @desc, @logo, @sync,
                         @lastSync::timestamptz, @related::jsonb, @connected::jsonb,
                         @managed, 'tenant', @config::jsonb, false, NOW()
                  WHERE NOT EXISTS (
                      SELECT 1 FROM integrations
                      WHERE company_id=@cid AND integration_key=@key)",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@name", e.Name);
                    c.Parameters.AddWithValue("@cat", e.Category);
                    c.Parameters.AddWithValue("@status", e.Status);
                    c.Parameters.AddWithValue("@key", e.Key);
                    c.Parameters.AddWithValue("@desc", e.Description);
                    c.Parameters.AddWithValue("@logo", e.Logo);
                    c.Parameters.AddWithValue("@sync", e.SyncLabel);
                    c.Parameters.AddWithValue("@lastSync", (object?)e.LastSyncAt ?? DBNull.Value);
                    c.Parameters.AddWithValue("@related", JsonSerializer.Serialize(e.RelatedSystems));
                    c.Parameters.AddWithValue("@connected", JsonSerializer.Serialize(e.ConnectedTo));
                    c.Parameters.AddWithValue("@managed", e.ManagedBy);
                    c.Parameters.AddWithValue("@config", JsonSerializer.Serialize(e.Config));
                }, ct);
        }
    }
}
