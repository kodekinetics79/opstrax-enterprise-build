using System.Text;
using MySqlConnector;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// P8 Reporting + Analytics — Dataset Registry and Secure Query Builder
//
// SECURITY MODEL
// ──────────────
// 1. Only datasets defined here can be queried. No user-supplied table names.
// 2. Only fields in AllowedFields can be selected. No user-supplied column names.
// 3. Only operators in AllowedOperators per field. No raw SQL fragments.
// 4. Every filter value is bound as a parameterized MySqlCommand parameter.
// 5. Tenant scope (company_id) is always injected server-side from the auth
//    context; it cannot be overridden by the request body.
// 6. Sensitive fields are excluded unless explicitly granted by permission.
// 7. Max 20 fields, 10 filters, 5 000 rows per query enforced here.
//
// The SecureQueryBuilder's Validate() method must be called before Build().
// Build() assumes validation has passed.
// ─────────────────────────────────────────────────────────────────────────────

// ── Field definition ─────────────────────────────────────────────────────────

public sealed class ReportFieldDef
{
    public string   Key              { get; init; } = "";
    public string   Label            { get; init; } = "";
    /// <summary>string | number | date | boolean | enum</summary>
    public string   Type             { get; init; } = "string";
    /// <summary>Sensitive fields are hidden unless callerHasSensitivePermission.</summary>
    public bool     Sensitive        { get; init; }
    public bool     Exportable       { get; init; } = true;
    public bool     Sortable         { get; init; } = true;
    public bool     Groupable        { get; init; }
    public string[] AllowedOperators { get; init; } = DefaultStringOps;

    internal static readonly string[] DefaultStringOps =
        ["equals","not_equals","contains","starts_with","in","is_empty","is_not_empty"];
}

// ── Dataset definition ───────────────────────────────────────────────────────

public sealed class ReportDatasetDef
{
    public string         Key                 { get; init; } = "";
    public string         Label               { get; init; } = "";
    /// <summary>
    /// Full SELECT … FROM … [JOIN …] query, no WHERE/ORDER/LIMIT.
    /// Must include "{TenantAlias}.company_id" so the outer WHERE can scope it.
    /// </summary>
    public string         BaseQuery           { get; init; } = "";
    /// <summary>Alias prefix that qualifies company_id in BaseQuery, e.g. "da".</summary>
    public string         TenantTableAlias    { get; init; } = "";
    /// <summary>Required permission to access this dataset.</summary>
    public string         RequiredPermission  { get; init; } = "reports:view";
    /// <summary>Additional permission needed to see Sensitive fields (null = not available).</summary>
    public string?        SensitivePermission { get; init; }
    public ReportFieldDef[] Fields            { get; init; } = [];

    public ReportFieldDef? GetField(string key) =>
        Array.Find(Fields, f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
}

// ── Validation result ─────────────────────────────────────────────────────────

public sealed class ValidationResult
{
    public bool   IsValid { get; private init; }
    public string? Error  { get; private init; }

    public static readonly ValidationResult Ok = new() { IsValid = true };
    public static ValidationResult Fail(string error) => new() { IsValid = false, Error = error };
}

// ── Query body DTOs (used by endpoint handlers) ───────────────────────────────

public sealed record P8FilterBody(
    string  Field,
    string  Op,
    string? Val  = null,
    string? Val2 = null);

public sealed record P8SortBody(string Field, string Dir = "asc");

public sealed record P8QueryBody(
    string          DatasetKey,
    string[]        Fields,
    P8FilterBody[]? Filters  = null,
    P8SortBody?     Sort     = null,
    string?         GroupBy  = null,
    int             Page     = 1,
    int             PageSize = 100);

public sealed record P8SavedReportBody(
    string          Name,
    string          DatasetKey,
    string[]        Fields,
    P8FilterBody[]? Filters     = null,
    P8SortBody?     Sort        = null,
    string?         GroupBy     = null,
    string          Visibility  = "private",
    string?         SharedRole  = null,
    string?         Description = null);

public sealed record P8ScheduledReportBody(
    long   SavedReportId,
    string Schedule,
    string Format          = "csv",
    string RecipientType   = "roles",
    string Recipients      = "Fleet Manager,Tenant Admin");

// ── Dataset registry ──────────────────────────────────────────────────────────

public static class ReportingDatasetRegistry
{
    private static readonly string[] StrOps  = ["equals","not_equals","contains","starts_with","in","is_empty","is_not_empty"];
    private static readonly string[] NumOps  = ["equals","not_equals","greater_than","less_than","number_range","is_empty","is_not_empty"];
    private static readonly string[] DateOps = ["date_range","equals","greater_than","less_than","is_empty","is_not_empty"];
    private static readonly string[] BoolOps = ["boolean","equals"];
    private static readonly string[] EnumOps = ["equals","not_equals","in","is_empty","is_not_empty"];

    private static readonly Dictionary<string, ReportDatasetDef> _registry = BuildRegistry();

    public static IReadOnlyDictionary<string, ReportDatasetDef> All => _registry;

    public static ReportDatasetDef? Get(string key) =>
        _registry.TryGetValue(key, out var d) ? d : null;

    private static Dictionary<string, ReportDatasetDef> BuildRegistry()
    {
        var list = new[]
        {
            new ReportDatasetDef
            {
                Key = "dispatch_assignments", Label = "Dispatch Assignments",
                TenantTableAlias = "da",
                BaseQuery = @"
                    SELECT da.id, da.assignment_number, da.assignment_status,
                           da.planned_pickup_at, da.planned_delivery_at,
                           da.actual_pickup_at,  da.actual_delivery_at,
                           da.accepted_at, da.exception_count,
                           COALESCE(j.job_number, j.job_code) AS shipment_number,
                           j.pickup_address, j.dropoff_address,
                           d.full_name AS driver_name, d.driver_code,
                           v.vehicle_code,
                           da.created_at, da.company_id
                    FROM dispatch_assignments da
                    LEFT JOIN jobs       j ON j.id = da.job_id
                    LEFT JOIN drivers    d ON d.id = da.driver_id
                    LEFT JOIN vehicles   v ON v.id = da.vehicle_id",
                RequiredPermission = "dispatch:view",
                Fields =
                [
                    new() { Key="id",                    Label="ID",                   Type="number", AllowedOperators=NumOps,  Sortable=true  },
                    new() { Key="assignment_number",     Label="Assignment #",          Type="string", AllowedOperators=StrOps,  Sortable=true  },
                    new() { Key="assignment_status",     Label="Status",                Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="planned_pickup_at",     Label="Planned Pickup",        Type="date",   AllowedOperators=DateOps, Sortable=true  },
                    new() { Key="planned_delivery_at",   Label="Planned Delivery",      Type="date",   AllowedOperators=DateOps, Sortable=true  },
                    new() { Key="actual_pickup_at",      Label="Actual Pickup",         Type="date",   AllowedOperators=DateOps, Sortable=true  },
                    new() { Key="actual_delivery_at",    Label="Actual Delivery",       Type="date",   AllowedOperators=DateOps, Sortable=true  },
                    new() { Key="accepted_at",           Label="Accepted At",           Type="date",   AllowedOperators=DateOps, Sortable=true  },
                    new() { Key="exception_count",       Label="Exceptions",            Type="number", AllowedOperators=NumOps,  Sortable=true  },
                    new() { Key="shipment_number",       Label="Shipment #",            Type="string", AllowedOperators=StrOps   },
                    new() { Key="pickup_address",        Label="Pickup Address",        Type="string", AllowedOperators=StrOps   },
                    new() { Key="dropoff_address",       Label="Delivery Address",      Type="string", AllowedOperators=StrOps   },
                    new() { Key="driver_name",           Label="Driver",                Type="string", AllowedOperators=StrOps,  Groupable=true },
                    new() { Key="driver_code",           Label="Driver Code",           Type="string", AllowedOperators=StrOps   },
                    new() { Key="vehicle_code",          Label="Vehicle",               Type="string", AllowedOperators=StrOps,  Groupable=true },
                    new() { Key="created_at",            Label="Created",               Type="date",   AllowedOperators=DateOps, Sortable=true  },
                ]
            },
            new ReportDatasetDef
            {
                Key = "dispatch_exceptions", Label = "Dispatch Exceptions",
                TenantTableAlias = "de",
                BaseQuery = @"
                    SELECT de.id, de.exception_type, de.description, de.severity,
                           de.status, de.reported_at, de.resolved_at,
                           d.full_name AS driver_name, v.vehicle_code,
                           de.company_id
                    FROM dispatch_exceptions de
                    LEFT JOIN dispatch_assignments da ON da.id = de.assignment_id
                    LEFT JOIN drivers   d ON d.id = da.driver_id
                    LEFT JOIN vehicles  v ON v.id = da.vehicle_id",
                RequiredPermission = "dispatch:view",
                Fields =
                [
                    new() { Key="id",             Label="ID",             Type="number", AllowedOperators=NumOps  },
                    new() { Key="exception_type", Label="Exception Type", Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="description",    Label="Description",    Type="string", AllowedOperators=StrOps  },
                    new() { Key="severity",       Label="Severity",       Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="status",         Label="Status",         Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="reported_at",    Label="Reported At",    Type="date",   AllowedOperators=DateOps, Sortable=true },
                    new() { Key="resolved_at",    Label="Resolved At",    Type="date",   AllowedOperators=DateOps, Sortable=true },
                    new() { Key="driver_name",    Label="Driver",         Type="string", AllowedOperators=StrOps,  Groupable=true },
                    new() { Key="vehicle_code",   Label="Vehicle",        Type="string", AllowedOperators=StrOps,  Groupable=true },
                ]
            },
            new ReportDatasetDef
            {
                Key = "trips", Label = "Trip Performance",
                TenantTableAlias = "t",
                BaseQuery = @"
                    SELECT t.id, t.trip_number, t.status, t.start_time, t.end_time,
                           t.planned_distance_km, t.actual_distance_km,
                           t.compliance_score, t.route_deviation_km,
                           t.idle_time_minutes, t.harsh_events,
                           d.full_name AS driver_name, v.vehicle_code,
                           t.company_id
                    FROM trips t
                    LEFT JOIN drivers  d ON d.id = t.driver_id
                    LEFT JOIN vehicles v ON v.id = t.vehicle_id",
                RequiredPermission = "reports:view",
                Fields =
                [
                    new() { Key="id",                   Label="ID",                   Type="number", AllowedOperators=NumOps  },
                    new() { Key="trip_number",          Label="Trip #",               Type="string", AllowedOperators=StrOps  },
                    new() { Key="status",               Label="Status",               Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="start_time",           Label="Start Time",           Type="date",   AllowedOperators=DateOps, Sortable=true },
                    new() { Key="end_time",             Label="End Time",             Type="date",   AllowedOperators=DateOps, Sortable=true },
                    new() { Key="planned_distance_km",  Label="Planned Dist (km)",    Type="number", AllowedOperators=NumOps  },
                    new() { Key="actual_distance_km",   Label="Actual Dist (km)",     Type="number", AllowedOperators=NumOps  },
                    new() { Key="compliance_score",     Label="Compliance Score",     Type="number", AllowedOperators=NumOps, Sortable=true },
                    new() { Key="route_deviation_km",   Label="Route Deviation (km)", Type="number", AllowedOperators=NumOps  },
                    new() { Key="idle_time_minutes",    Label="Idle Time (min)",      Type="number", AllowedOperators=NumOps  },
                    new() { Key="harsh_events",         Label="Harsh Events",         Type="number", AllowedOperators=NumOps  },
                    new() { Key="driver_name",          Label="Driver",               Type="string", AllowedOperators=StrOps, Groupable=true },
                    new() { Key="vehicle_code",         Label="Vehicle",              Type="string", AllowedOperators=StrOps, Groupable=true },
                ]
            },
            new ReportDatasetDef
            {
                Key = "safety_events", Label = "Safety Events",
                TenantTableAlias = "se",
                BaseQuery = @"
                    SELECT se.id, se.event_type, se.severity, se.event_time,
                           se.review_status, se.speed_kmh, se.g_force,
                           d.full_name AS driver_name, v.vehicle_code,
                           se.company_id
                    FROM safety_events se
                    LEFT JOIN drivers  d ON d.id = se.driver_id
                    LEFT JOIN vehicles v ON v.id = se.vehicle_id",
                RequiredPermission = "safety:view",
                Fields =
                [
                    new() { Key="id",            Label="ID",            Type="number", AllowedOperators=NumOps  },
                    new() { Key="event_type",    Label="Event Type",    Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="severity",      Label="Severity",      Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="event_time",    Label="Event Time",    Type="date",   AllowedOperators=DateOps, Sortable=true },
                    new() { Key="review_status", Label="Review Status", Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="speed_kmh",     Label="Speed (km/h)",  Type="number", AllowedOperators=NumOps  },
                    new() { Key="g_force",       Label="G-Force",       Type="number", AllowedOperators=NumOps  },
                    new() { Key="driver_name",   Label="Driver",        Type="string", AllowedOperators=StrOps, Groupable=true },
                    new() { Key="vehicle_code",  Label="Vehicle",       Type="string", AllowedOperators=StrOps, Groupable=true },
                ]
            },
            new ReportDatasetDef
            {
                Key = "driver_safety_scores", Label = "Driver Safety Scores",
                TenantTableAlias = "d",
                BaseQuery = @"
                    SELECT d.id, d.driver_code, d.full_name AS driver_name, d.status,
                           d.safety_score, d.license_class,
                           d.license_number, d.license_expiry,
                           d.company_id
                    FROM drivers d
                    WHERE d.deleted_at IS NULL",
                RequiredPermission = "safety:view",
                SensitivePermission = "drivers:export",
                Fields =
                [
                    new() { Key="id",             Label="ID",             Type="number", AllowedOperators=NumOps  },
                    new() { Key="driver_code",    Label="Driver Code",    Type="string", AllowedOperators=StrOps  },
                    new() { Key="driver_name",    Label="Driver Name",    Type="string", AllowedOperators=StrOps, Sortable=true },
                    new() { Key="status",         Label="Status",         Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="safety_score",   Label="Safety Score",   Type="number", AllowedOperators=NumOps, Sortable=true },
                    new() { Key="license_class",  Label="License Class",  Type="string", AllowedOperators=StrOps, Groupable=true },
                    // Sensitive fields — require drivers:export
                    new() { Key="license_number", Label="License Number", Type="string", AllowedOperators=StrOps, Sensitive=true },
                    new() { Key="license_expiry", Label="License Expiry", Type="date",   AllowedOperators=DateOps, Sensitive=true },
                ]
            },
            new ReportDatasetDef
            {
                Key = "coaching_tasks", Label = "Coaching Tasks",
                TenantTableAlias = "ct",
                BaseQuery = @"
                    SELECT ct.id, ct.title, ct.status, ct.priority, ct.due_date,
                           ct.coaching_type, ct.acknowledged_at,
                           d.full_name AS driver_name, d.driver_code,
                           ct.company_id
                    FROM coaching_tasks ct
                    LEFT JOIN drivers d ON d.id = ct.driver_id
                    WHERE ct.deleted_at IS NULL",
                RequiredPermission = "safety:view",
                Fields =
                [
                    new() { Key="id",              Label="ID",             Type="number", AllowedOperators=NumOps  },
                    new() { Key="title",           Label="Task Title",     Type="string", AllowedOperators=StrOps  },
                    new() { Key="status",          Label="Status",         Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="priority",        Label="Priority",       Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="due_date",        Label="Due Date",       Type="date",   AllowedOperators=DateOps, Sortable=true },
                    new() { Key="coaching_type",   Label="Coaching Type",  Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="acknowledged_at", Label="Acknowledged At",Type="date",   AllowedOperators=DateOps, Sortable=true },
                    new() { Key="driver_name",     Label="Driver",         Type="string", AllowedOperators=StrOps, Groupable=true },
                    new() { Key="driver_code",     Label="Driver Code",    Type="string", AllowedOperators=StrOps  },
                ]
            },
            new ReportDatasetDef
            {
                Key = "dvir_inspections", Label = "DVIR Inspections",
                TenantTableAlias = "dr",
                BaseQuery = @"
                    SELECT dr.id, dr.report_number, dr.inspection_type, dr.inspection_date,
                           dr.overall_status, dr.odometer_at_inspection,
                           dr.engine_hours_at_inspection,
                           dr.defect_count, dr.critical_defect_count, dr.out_of_service,
                           v.vehicle_code, d.full_name AS driver_name,
                           dr.company_id
                    FROM dvir_reports dr
                    LEFT JOIN vehicles v ON v.id = dr.vehicle_id
                    LEFT JOIN drivers  d ON d.id = dr.driver_id",
                RequiredPermission = "maintenance:view",
                Fields =
                [
                    new() { Key="id",                      Label="ID",                  Type="number",  AllowedOperators=NumOps  },
                    new() { Key="report_number",           Label="Report #",             Type="string",  AllowedOperators=StrOps  },
                    new() { Key="inspection_type",         Label="Inspection Type",      Type="enum",    AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="inspection_date",         Label="Inspection Date",      Type="date",    AllowedOperators=DateOps, Sortable=true },
                    new() { Key="overall_status",          Label="Overall Status",       Type="enum",    AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="odometer_at_inspection",  Label="Odometer",             Type="number",  AllowedOperators=NumOps  },
                    new() { Key="defect_count",            Label="Defect Count",         Type="number",  AllowedOperators=NumOps, Sortable=true },
                    new() { Key="critical_defect_count",   Label="Critical Defects",     Type="number",  AllowedOperators=NumOps, Sortable=true },
                    new() { Key="out_of_service",          Label="Out of Service",       Type="boolean", AllowedOperators=BoolOps, Groupable=true },
                    new() { Key="vehicle_code",            Label="Vehicle",              Type="string",  AllowedOperators=StrOps, Groupable=true },
                    new() { Key="driver_name",             Label="Driver",               Type="string",  AllowedOperators=StrOps, Groupable=true },
                ]
            },
            new ReportDatasetDef
            {
                Key = "maintenance_defects", Label = "Maintenance Defects",
                TenantTableAlias = "dd",
                BaseQuery = @"
                    SELECT dd.id, dd.defect_number, dd.category, dd.description,
                           dd.severity, dd.out_of_service, dd.status,
                           dd.reported_at, dd.resolved_at,
                           v.vehicle_code,
                           dd.company_id
                    FROM dvir_defects dd
                    LEFT JOIN vehicles v ON v.id = dd.vehicle_id",
                RequiredPermission = "maintenance:view",
                Fields =
                [
                    new() { Key="id",             Label="ID",            Type="number",  AllowedOperators=NumOps  },
                    new() { Key="defect_number",  Label="Defect #",      Type="string",  AllowedOperators=StrOps  },
                    new() { Key="category",       Label="Category",      Type="enum",    AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="description",    Label="Description",   Type="string",  AllowedOperators=StrOps  },
                    new() { Key="severity",       Label="Severity",      Type="enum",    AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="out_of_service", Label="Out of Service",Type="boolean", AllowedOperators=BoolOps, Groupable=true },
                    new() { Key="status",         Label="Status",        Type="enum",    AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="reported_at",    Label="Reported At",   Type="date",    AllowedOperators=DateOps, Sortable=true },
                    new() { Key="resolved_at",    Label="Resolved At",   Type="date",    AllowedOperators=DateOps, Sortable=true },
                    new() { Key="vehicle_code",   Label="Vehicle",       Type="string",  AllowedOperators=StrOps, Groupable=true },
                ]
            },
            new ReportDatasetDef
            {
                Key = "work_orders", Label = "Work Orders",
                TenantTableAlias = "wo",
                BaseQuery = @"
                    SELECT wo.id, wo.work_order_number, wo.work_order_type, wo.status,
                           wo.priority, wo.description, wo.scheduled_date, wo.completed_date,
                           wo.estimated_labor_hours, wo.actual_labor_hours,
                           wo.estimated_cost, wo.actual_cost,
                           v.vehicle_code,
                           wo.company_id
                    FROM work_orders wo
                    LEFT JOIN vehicles v ON v.id = wo.vehicle_id",
                RequiredPermission = "maintenance:view",
                Fields =
                [
                    new() { Key="id",                    Label="ID",               Type="number", AllowedOperators=NumOps  },
                    new() { Key="work_order_number",     Label="WO #",             Type="string", AllowedOperators=StrOps  },
                    new() { Key="work_order_type",       Label="Type",             Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="status",                Label="Status",           Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="priority",              Label="Priority",         Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="scheduled_date",        Label="Scheduled Date",   Type="date",   AllowedOperators=DateOps, Sortable=true },
                    new() { Key="completed_date",        Label="Completed Date",   Type="date",   AllowedOperators=DateOps, Sortable=true },
                    new() { Key="estimated_cost",        Label="Est. Cost",        Type="number", AllowedOperators=NumOps, Sortable=true },
                    new() { Key="actual_cost",           Label="Actual Cost",      Type="number", AllowedOperators=NumOps, Sortable=true },
                    new() { Key="estimated_labor_hours", Label="Est. Labor Hrs",   Type="number", AllowedOperators=NumOps  },
                    new() { Key="actual_labor_hours",    Label="Actual Labor Hrs", Type="number", AllowedOperators=NumOps  },
                    new() { Key="vehicle_code",          Label="Vehicle",          Type="string", AllowedOperators=StrOps, Groupable=true },
                ]
            },
            new ReportDatasetDef
            {
                Key = "fault_codes", Label = "Fault Codes",
                TenantTableAlias = "fc",
                BaseQuery = @"
                    SELECT fc.id, fc.code, fc.protocol, fc.severity, fc.component,
                           fc.description, fc.detected_at, fc.status,
                           fc.recurrence_count, fc.last_seen_at,
                           v.vehicle_code,
                           fc.company_id
                    FROM fault_codes fc
                    LEFT JOIN vehicles v ON v.id = fc.vehicle_id",
                RequiredPermission = "maintenance:view",
                Fields =
                [
                    new() { Key="id",               Label="ID",           Type="number", AllowedOperators=NumOps  },
                    new() { Key="code",             Label="Fault Code",   Type="string", AllowedOperators=StrOps  },
                    new() { Key="protocol",         Label="Protocol",     Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="severity",         Label="Severity",     Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="component",        Label="Component",    Type="string", AllowedOperators=StrOps, Groupable=true },
                    new() { Key="description",      Label="Description",  Type="string", AllowedOperators=StrOps  },
                    new() { Key="detected_at",      Label="Detected At",  Type="date",   AllowedOperators=DateOps, Sortable=true },
                    new() { Key="status",           Label="Status",       Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="recurrence_count", Label="Recurrences",  Type="number", AllowedOperators=NumOps, Sortable=true },
                    new() { Key="last_seen_at",     Label="Last Seen",    Type="date",   AllowedOperators=DateOps, Sortable=true },
                    new() { Key="vehicle_code",     Label="Vehicle",      Type="string", AllowedOperators=StrOps, Groupable=true },
                ]
            },
            new ReportDatasetDef
            {
                Key = "telemetry_alerts", Label = "Telemetry Alerts",
                TenantTableAlias = "al",
                BaseQuery = @"
                    SELECT al.id, al.alert_type, al.severity, al.message,
                           al.status, al.triggered_at, al.resolved_at, al.acknowledged_at,
                           v.vehicle_code, d.full_name AS driver_name,
                           al.company_id
                    FROM alerts al
                    LEFT JOIN vehicles v ON v.id = al.vehicle_id
                    LEFT JOIN drivers  d ON d.id = al.driver_id",
                RequiredPermission = "alerts:view",
                Fields =
                [
                    new() { Key="id",              Label="ID",           Type="number", AllowedOperators=NumOps  },
                    new() { Key="alert_type",      Label="Alert Type",   Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="severity",        Label="Severity",     Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="message",         Label="Message",      Type="string", AllowedOperators=StrOps  },
                    new() { Key="status",          Label="Status",       Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="triggered_at",    Label="Triggered At", Type="date",   AllowedOperators=DateOps, Sortable=true },
                    new() { Key="acknowledged_at", Label="Acknowledged", Type="date",   AllowedOperators=DateOps  },
                    new() { Key="vehicle_code",    Label="Vehicle",      Type="string", AllowedOperators=StrOps, Groupable=true },
                    new() { Key="driver_name",     Label="Driver",       Type="string", AllowedOperators=StrOps, Groupable=true },
                ]
            },
            new ReportDatasetDef
            {
                Key = "notifications", Label = "Notifications",
                TenantTableAlias = "n",
                BaseQuery = @"
                    SELECT n.id, n.notification_type, n.title, n.priority,
                           n.status, n.delivery_channel, n.acknowledged_at, n.created_at,
                           n.company_id
                    FROM notifications n",
                RequiredPermission = "notifications:view",
                Fields =
                [
                    new() { Key="id",                Label="ID",             Type="number", AllowedOperators=NumOps  },
                    new() { Key="notification_type", Label="Type",           Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="title",             Label="Title",          Type="string", AllowedOperators=StrOps  },
                    new() { Key="priority",          Label="Priority",       Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="status",            Label="Status",         Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="delivery_channel",  Label="Channel",        Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="acknowledged_at",   Label="Acknowledged At",Type="date",   AllowedOperators=DateOps, Sortable=true },
                    new() { Key="created_at",        Label="Created At",     Type="date",   AllowedOperators=DateOps, Sortable=true },
                ]
            },
            new ReportDatasetDef
            {
                Key = "escalations", Label = "Escalations",
                TenantTableAlias = "e",
                BaseQuery = @"
                    SELECT e.id, e.escalation_type, e.severity, e.status,
                           e.triggered_at, e.resolved_at,
                           e.escalation_level, e.current_owner_role,
                           e.company_id
                    FROM escalation_records e",
                RequiredPermission = "escalation:manage",
                Fields =
                [
                    new() { Key="id",                 Label="ID",              Type="number", AllowedOperators=NumOps  },
                    new() { Key="escalation_type",    Label="Escalation Type", Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="severity",           Label="Severity",        Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="status",             Label="Status",          Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="triggered_at",       Label="Triggered At",    Type="date",   AllowedOperators=DateOps, Sortable=true },
                    new() { Key="resolved_at",        Label="Resolved At",     Type="date",   AllowedOperators=DateOps, Sortable=true },
                    new() { Key="escalation_level",   Label="Level",           Type="number", AllowedOperators=NumOps  },
                    new() { Key="current_owner_role", Label="Owner Role",      Type="string", AllowedOperators=StrOps, Groupable=true },
                ]
            },
            new ReportDatasetDef
            {
                Key = "proofs_of_delivery", Label = "Proofs of Delivery",
                TenantTableAlias = "pod",
                BaseQuery = @"
                    SELECT pod.id, pod.proof_type, pod.notes,
                           pod.evidence_hash, pod.captured_at,
                           COALESCE(j.job_number, j.job_code) AS shipment_number,
                           pod.company_id
                    FROM proof_of_delivery pod
                    LEFT JOIN jobs j ON j.id = pod.job_id",
                RequiredPermission = "dispatch:view",
                Fields =
                [
                    new() { Key="id",              Label="ID",           Type="number", AllowedOperators=NumOps  },
                    new() { Key="proof_type",      Label="Proof Type",   Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="notes",           Label="Notes",        Type="string", AllowedOperators=StrOps  },
                    new() { Key="evidence_hash",   Label="Evidence Ref", Type="string", AllowedOperators=StrOps  },
                    new() { Key="captured_at",     Label="Captured At",  Type="date",   AllowedOperators=DateOps, Sortable=true },
                    new() { Key="shipment_number", Label="Shipment #",   Type="string", AllowedOperators=StrOps  },
                ]
            },
            new ReportDatasetDef
            {
                Key = "customer_sla", Label = "Customer SLA / ETA Risk",
                BaseQuery = @"
                    SELECT sr.id, sr.sla_number, sr.sla_type, sr.status,
                           sr.target_value, sr.actual_value, sr.unit,
                           sr.risk_score, sr.breach_reason, sr.measured_at,
                           c.name AS customer_name,
                           COALESCE(j.job_number, j.job_code) AS shipment_number,
                           sr.tenant_id AS company_id
                    FROM sla_records sr
                    LEFT JOIN customers c ON c.id = sr.customer_id
                    LEFT JOIN jobs      j ON j.id = sr.job_id",
                TenantTableAlias = "sr",
                RequiredPermission = "customer_portal:view",
                Fields =
                [
                    new() { Key="id",              Label="ID",            Type="number", AllowedOperators=NumOps  },
                    new() { Key="sla_number",      Label="SLA #",         Type="string", AllowedOperators=StrOps  },
                    new() { Key="sla_type",        Label="SLA Type",      Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="status",          Label="Status",        Type="enum",   AllowedOperators=EnumOps, Groupable=true },
                    new() { Key="target_value",    Label="Target",        Type="number", AllowedOperators=NumOps  },
                    new() { Key="actual_value",    Label="Actual",        Type="number", AllowedOperators=NumOps  },
                    new() { Key="risk_score",      Label="Risk Score",    Type="number", AllowedOperators=NumOps, Sortable=true },
                    new() { Key="breach_reason",   Label="Breach Reason", Type="string", AllowedOperators=StrOps  },
                    new() { Key="measured_at",     Label="Measured At",   Type="date",   AllowedOperators=DateOps, Sortable=true },
                    new() { Key="customer_name",   Label="Customer",      Type="string", AllowedOperators=StrOps, Groupable=true },
                    new() { Key="shipment_number", Label="Shipment #",    Type="string", AllowedOperators=StrOps  },
                ]
            },
        };

        return list.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);
    }

    // Converts snake_case column key to camelCase (mirrors Database.ToCamel).
    public static string ToCamel(string value)
    {
        var parts = value.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return value;
        return parts[0].ToLowerInvariant()
             + string.Concat(parts.Skip(1).Select(p => char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
    }
}

// ── Secure Query Builder ──────────────────────────────────────────────────────

public static class SecureQueryBuilder
{
    public const int MaxFields   = 20;
    public const int MaxFilters  = 10;
    public const int MaxPageSize = 5_000;

    private static readonly HashSet<string> GlobalAllowedOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "equals","not_equals","contains","starts_with","in",
        "date_range","number_range","greater_than","less_than",
        "boolean","is_empty","is_not_empty"
    };

    // ── Validation ──────────────────────────────────────────────────────────

    public static ValidationResult Validate(
        P8QueryBody       req,
        ReportDatasetDef  dataset,
        string[]          callerPermissions,
        bool              callerHasSensitivePermission)
    {
        if (req.Fields is null || req.Fields.Length == 0)
            return ValidationResult.Fail("At least one field must be selected.");
        if (req.Fields.Length > MaxFields)
            return ValidationResult.Fail($"Maximum {MaxFields} fields allowed per query.");

        var filters = req.Filters ?? [];
        if (filters.Length > MaxFilters)
            return ValidationResult.Fail($"Maximum {MaxFilters} filters allowed.");

        foreach (var f in req.Fields)
        {
            // Reject injection characters in field names (belt + suspenders on top of whitelist)
            if (ContainsSqlMeta(f))
                return ValidationResult.Fail($"Invalid field name: '{f}'");

            var fd = dataset.GetField(f);
            if (fd is null)
                return ValidationResult.Fail($"Unknown field '{f}' for dataset '{dataset.Key}'.");
            if (fd.Sensitive && !callerHasSensitivePermission)
                return ValidationResult.Fail($"Field '{f}' requires additional export permission.");
        }

        foreach (var fil in filters)
        {
            if (ContainsSqlMeta(fil.Field))
                return ValidationResult.Fail($"Invalid filter field: '{fil.Field}'");
            if (ContainsSqlMeta(fil.Op))
                return ValidationResult.Fail($"Invalid operator: '{fil.Op}'");

            var fd = dataset.GetField(fil.Field);
            if (fd is null)
                return ValidationResult.Fail($"Unknown filter field '{fil.Field}'.");

            if (!GlobalAllowedOperators.Contains(fil.Op))
                return ValidationResult.Fail($"Unsupported operator '{fil.Op}'.");
            if (!Array.Exists(fd.AllowedOperators, o => string.Equals(o, fil.Op, StringComparison.OrdinalIgnoreCase)))
                return ValidationResult.Fail($"Operator '{fil.Op}' is not permitted for field '{fil.Field}'.");

            if (fd.Sensitive && !callerHasSensitivePermission)
                return ValidationResult.Fail($"Field '{fil.Field}' requires additional export permission.");
        }

        if (req.Sort is not null)
        {
            if (ContainsSqlMeta(req.Sort.Field))
                return ValidationResult.Fail($"Invalid sort field: '{req.Sort.Field}'");
            var fd = dataset.GetField(req.Sort.Field);
            if (fd is null)
                return ValidationResult.Fail($"Unknown sort field '{req.Sort.Field}'.");
            if (!fd.Sortable)
                return ValidationResult.Fail($"Field '{req.Sort.Field}' is not sortable.");
            var dir = req.Sort.Dir?.ToLowerInvariant();
            if (dir is not ("asc" or "desc"))
                return ValidationResult.Fail("Sort direction must be 'asc' or 'desc'.");
        }

        if (req.GroupBy is not null)
        {
            if (ContainsSqlMeta(req.GroupBy))
                return ValidationResult.Fail($"Invalid group-by field: '{req.GroupBy}'");
            var fd = dataset.GetField(req.GroupBy);
            if (fd is null)
                return ValidationResult.Fail($"Unknown group-by field '{req.GroupBy}'.");
            if (!fd.Groupable)
                return ValidationResult.Fail($"Field '{req.GroupBy}' cannot be used for grouping.");
        }

        return ValidationResult.Ok;
    }

    // ── Build (call only after Validate returns Ok) ──────────────────────────

    public static (string Sql, string CountSql, List<(string Name, object? Value)> Params) Build(
        P8QueryBody       req,
        ReportDatasetDef  dataset,
        long              companyId)
    {
        var pageSize = Math.Min(Math.Max(1, req.PageSize), MaxPageSize);
        var offset   = (Math.Max(1, req.Page) - 1) * pageSize;

        // All field names come from the whitelist — safe to embed in SQL.
        var selectCols = string.Join(", ", req.Fields.Select(f => $"`{f}`"));

        var paramList    = new List<(string Name, object? Value)>();
        var whereClauses = new List<string>();
        var pi           = 0; // parameter index counter

        // Tenant scope — always injected server-side.
        // Uses the qualified alias (e.g. "da.company_id") in the subquery's WHERE.
        var tenantParam = $"@p{pi++}";
        whereClauses.Add($"company_id = {tenantParam}");
        paramList.Add((tenantParam, companyId));

        // Parameterized filters — no user input in SQL text.
        foreach (var fil in req.Filters ?? [])
        {
            // field key is whitelisted → safe in SQL
            var col = $"`{fil.Field}`";

            switch (fil.Op.ToLowerInvariant())
            {
                case "equals":
                case "not_equals":
                {
                    var op  = fil.Op.Equals("not_equals", StringComparison.OrdinalIgnoreCase) ? "!=" : "=";
                    var p   = $"@p{pi++}";
                    whereClauses.Add($"{col} {op} {p}");
                    paramList.Add((p, (object?)fil.Val ?? DBNull.Value));
                    break;
                }
                case "contains":
                {
                    var p = $"@p{pi++}";
                    whereClauses.Add($"{col} LIKE {p}");
                    paramList.Add((p, $"%{fil.Val}%"));
                    break;
                }
                case "starts_with":
                {
                    var p = $"@p{pi++}";
                    whereClauses.Add($"{col} LIKE {p}");
                    paramList.Add((p, $"{fil.Val}%"));
                    break;
                }
                case "in":
                {
                    var values = (fil.Val ?? "")
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (values.Length == 0) break;
                    var inParams = values.Select((_, i) => $"@p{pi + i}").ToArray();
                    whereClauses.Add($"{col} IN ({string.Join(",", inParams)})");
                    foreach (var v in values) paramList.Add(($"@p{pi++}", v));
                    break;
                }
                case "date_range":
                case "number_range":
                {
                    if (!string.IsNullOrWhiteSpace(fil.Val))
                    {
                        var p = $"@p{pi++}";
                        whereClauses.Add($"{col} >= {p}");
                        paramList.Add((p, fil.Val));
                    }
                    if (!string.IsNullOrWhiteSpace(fil.Val2))
                    {
                        var p = $"@p{pi++}";
                        whereClauses.Add($"{col} <= {p}");
                        paramList.Add((p, fil.Val2));
                    }
                    break;
                }
                case "greater_than":
                {
                    var p = $"@p{pi++}";
                    whereClauses.Add($"{col} > {p}");
                    paramList.Add((p, fil.Val ?? "0"));
                    break;
                }
                case "less_than":
                {
                    var p = $"@p{pi++}";
                    whereClauses.Add($"{col} < {p}");
                    paramList.Add((p, fil.Val ?? "0"));
                    break;
                }
                case "boolean":
                {
                    var p   = $"@p{pi++}";
                    var bv  = string.Equals(fil.Val, "true", StringComparison.OrdinalIgnoreCase) ||
                              fil.Val == "1" ? 1 : 0;
                    whereClauses.Add($"{col} = {p}");
                    paramList.Add((p, bv));
                    break;
                }
                case "is_empty":
                    whereClauses.Add($"({col} IS NULL OR {col} = '')");
                    break;
                case "is_not_empty":
                    whereClauses.Add($"({col} IS NOT NULL AND {col} != '')");
                    break;
            }
        }

        var whereClause = "WHERE " + string.Join(" AND ", whereClauses);

        var groupClause  = req.GroupBy is not null ? $"GROUP BY `{req.GroupBy}`" : "";
        var sortDir      = req.Sort?.Dir?.ToLowerInvariant() == "desc" ? "DESC" : "ASC";
        var orderClause  = req.Sort is not null ? $"ORDER BY `{req.Sort.Field}` {sortDir}" : "ORDER BY `id` DESC";

        // Wrap BaseQuery as a subquery so WHERE applies to aliased column names.
        var inner = dataset.BaseQuery.Trim();
        var sql =
            $"SELECT {selectCols}" +
            $" FROM ({inner}) AS _q" +
            $" {whereClause}" +
            $" {groupClause}" +
            $" {orderClause}" +
            $" LIMIT {pageSize} OFFSET {offset}";

        var countSql =
            $"SELECT COUNT(*)" +
            $" FROM ({inner}) AS _q" +
            $" {whereClause}";

        return (sql, countSql, paramList);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    // Returns true if the string contains SQL meta characters that could indicate injection.
    // NOTE: This is a belt-and-suspenders check. The primary protection is the whitelist.
    public static bool ContainsSqlMeta(string? s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (var c in s)
        {
            if (c is '\'' or '"' or ';' or '-' or '/' or '\\' or '\n' or '\r' or '\0'
                or '(' or ')' or '#' or '*')
                return true;
        }
        // Must look like an identifier (letters, digits, underscores only)
        return !System.Text.RegularExpressions.Regex.IsMatch(s, @"^[a-zA-Z_][a-zA-Z0-9_]*$");
    }

    // Applies the param list to a MySqlCommand.
    public static Action<MySqlCommand> BindParams(List<(string Name, object? Value)> parms) =>
        cmd =>
        {
            foreach (var (name, value) in parms)
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        };

    // Checks saved-report visibility against caller context.
    // Returns true if the caller is allowed to see this saved report.
    public static bool CanViewSavedReport(
        Dictionary<string, object?> report,
        long callerUserId,
        long callerCompanyId,
        string? callerRole,
        bool hasReportsView)
    {
        // Must be same tenant
        var reportCompanyId = report.TryGetValue("companyId", out var cid) && cid is not null
            ? Convert.ToInt64(cid) : -1;
        if (reportCompanyId != callerCompanyId) return false;
        if (report.TryGetValue("deletedAt", out var da) && da is not null) return false;

        var visibility  = report.TryGetValue("visibility", out var v) ? v?.ToString() ?? "private" : "private";
        var ownerId     = report.TryGetValue("ownerUserId", out var o) && o is not null
            ? Convert.ToInt64(o) : -1;
        var sharedRole  = report.TryGetValue("sharedRole", out var sr) ? sr?.ToString() : null;

        return visibility switch
        {
            "private"       => ownerId == callerUserId,
            "role_shared"   => ownerId == callerUserId
                               || (sharedRole is not null && string.Equals(sharedRole, callerRole, StringComparison.OrdinalIgnoreCase)),
            "tenant_shared" => hasReportsView,
            _               => ownerId == callerUserId,
        };
    }
}
