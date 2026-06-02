using MySqlConnector;
using System.Security.Cryptography;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

public static class EndpointMappings
{
    public const string AuthUserIdItemKey = "opstrax.auth.user_id";
    public const string AuthCompanyIdItemKey = "opstrax.auth.company_id";
    public const string AuthRoleItemKey = "opstrax.auth.role";
    public const string AuthPermissionsItemKey = "opstrax.auth.permissions";

    public static void MapOpsTraxEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/login", Login);
        app.MapGet("/api/command-center/summary", CommandCenterSummary);
        app.MapGet("/api/control-tower/summary", ControlTowerSummary);
        app.MapGet("/api/control-tower/entities", ControlTowerEntities);
        app.MapGet("/api/control-tower/entities/{entityType}/{id:long}", ControlTowerEntity);
        app.MapGet("/api/control-tower/events", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM operational_events ORDER BY event_time DESC LIMIT 50", ct: ct));
        app.MapPost("/api/control-tower/actions/send-eta-update", SimpleAction("eta.sent", "ETA update queued"));
        app.MapPost("/api/control-tower/actions/create-dispatch-review", SimpleAction("dispatch.review.created", "Dispatch review created"));
        app.MapPost("/api/control-tower/actions/create-maintenance-review", SimpleAction("maintenance.review.created", "Maintenance review created"));

        app.MapGet("/api/vehicles/summary", VehicleSummary);
        app.MapGet("/api/vehicles/planning-insights", VehiclePlanningInsights);
        app.MapGet("/api/vehicles", Vehicles);
        app.MapGet("/api/vehicles/{id:long}", VehicleDetail);
        app.MapPost("/api/vehicles", CreateVehicle);
        app.MapPut("/api/vehicles/{id:long}", UpdateVehicle);
        app.MapDelete("/api/vehicles/{id:long}", SoftDeleteWithPermission("vehicles", "vehicle.deleted", "fleet:manage"));
        app.MapGet("/api/vehicles/{id:long}/timeline", Timeline("Vehicle"));
        app.MapGet("/api/vehicles/{id:long}/recommendations", Recommendations("vehicles"));
        app.MapPost("/api/vehicles/{id:long}/assign-driver", ChangeEntityStatus("vehicles", "assigned_driver_id", "vehicle.driver.assigned"));
        app.MapPost("/api/vehicles/{id:long}/change-status", ChangeStatus("vehicles", "vehicle.status.changed"));

        app.MapGet("/api/drivers/summary", DriverSummary);
        app.MapGet("/api/drivers", Drivers);
        app.MapGet("/api/drivers/{id:long}", DriverDetail);
        app.MapPost("/api/drivers", CreateDriver);
        app.MapPut("/api/drivers/{id:long}", UpdateDriver);
        app.MapDelete("/api/drivers/{id:long}", SoftDeleteWithPermission("drivers", "driver.deleted", "fleet:manage"));
        app.MapGet("/api/drivers/{id:long}/timeline", Timeline("Driver"));
        app.MapGet("/api/drivers/{id:long}/recommendations", Recommendations("drivers"));
        app.MapPost("/api/drivers/{id:long}/assign-vehicle", ChangeEntityStatus("drivers", "assigned_vehicle_id", "driver.vehicle.assigned"));
        app.MapPost("/api/drivers/{id:long}/change-status", ChangeStatus("drivers", "driver.status.changed"));

        app.MapGet("/api/customers/summary", CustomerSummary);
        app.MapGet("/api/customers", Customers);
        app.MapGet("/api/customers/{id:long}", CustomerDetail);
        app.MapPost("/api/customers", CreateCustomer);
        app.MapPut("/api/customers/{id:long}", UpdateCustomer);
        app.MapDelete("/api/customers/{id:long}", SoftDelete("customers", "customer.deleted"));
        app.MapGet("/api/customers/{id:long}/timeline", Timeline("Customer"));
        app.MapGet("/api/customers/{id:long}/recommendations", Recommendations("customers"));

        app.MapGet("/api/assets/summary", AssetSummary);
        app.MapGet("/api/assets", Assets);
        app.MapGet("/api/assets/{id:long}", AssetDetail);
        app.MapPost("/api/assets", (HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "fleet:manage");
            return denied is not null ? Task.FromResult(denied) : CreateAsset(http, body, db, audit, ct);
        });
        app.MapPut("/api/assets/{id:long}", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "fleet:manage");
            return denied is not null ? Task.FromResult(denied) : UpdateAsset(http, id, body, db, audit, ct);
        });
        app.MapDelete("/api/assets/{id:long}", SoftDeleteWithPermission("assets", "asset.deleted", "fleet:manage"));
        app.MapGet("/api/assets/{id:long}/timeline", Timeline("Asset"));
        app.MapGet("/api/assets/{id:long}/recommendations", Recommendations("assets"));
        app.MapPost("/api/assets/{id:long}/assign", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "fleet:manage");
            return denied is not null ? Task.FromResult(denied) : AssignAsset(http, id, body, db, audit, ct);
        });

        app.MapGet("/api/jobs/summary", JobsSummary);
        app.MapGet("/api/jobs", Jobs);
        app.MapGet("/api/jobs/{id:long}", JobDetail);
        app.MapPost("/api/jobs", CreateJob);
        app.MapPut("/api/jobs/{id:long}", UpdateJob);
        app.MapDelete("/api/jobs/{id:long}", SoftDeleteWithPermission("jobs", "job.deleted", "dispatch:manage"));
        app.MapGet("/api/jobs/{id:long}/timeline", Timeline("Job"));
        app.MapGet("/api/jobs/{id:long}/recommendations", Recommendations("jobs"));
        app.MapPost("/api/jobs/import-preview", JobsImportPreview);
        app.MapPost("/api/jobs/{id:long}/assign", AssignJob);
        app.MapPost("/api/jobs/{id:long}/status", ChangeJobStatus);
        app.MapPost("/api/jobs/{id:long}/send-eta", SendEta);
        app.MapPost("/api/jobs/{id:long}/proof-placeholder", CreateProofPlaceholder);

        app.MapGet("/api/dispatch/summary", DispatchSummary);
        app.MapGet("/api/dispatch/board", DispatchBoard);
        app.MapGet("/api/dispatch/recommendations", DispatchRecommendations);
        app.MapGet("/api/dispatch/available-drivers", AvailableDrivers);
        app.MapGet("/api/dispatch/available-vehicles", AvailableVehicles);
        app.MapPost("/api/dispatch/assign", DispatchAssign);
        app.MapPost("/api/dispatch/status", DispatchStatus);
        app.MapPost("/api/dispatch/auto-suggest", DispatchAutoSuggest);
        app.MapPost("/api/dispatch/send-eta-updates", DispatchSendEtaUpdates);

        app.MapGet("/api/routes/summary", RoutesSummary);
        app.MapGet("/api/routes", Routes);
        app.MapGet("/api/routes/{id:long}", RouteDetail);
        app.MapPost("/api/routes", (HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "dispatch:manage");
            return denied is not null ? Task.FromResult(denied) : CreateRoute(http, body, db, audit, ct);
        });
        app.MapPut("/api/routes/{id:long}", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "dispatch:manage");
            return denied is not null ? Task.FromResult(denied) : UpdateRoute(http, id, body, db, audit, ct);
        });
        app.MapDelete("/api/routes/{id:long}", SoftDeleteWithPermission("routes", "route.deleted", "dispatch:manage"));
        app.MapGet("/api/routes/{id:long}/stops", RouteStops);
        app.MapPost("/api/routes/{id:long}/stops", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "dispatch:manage");
            return denied is not null ? Task.FromResult(denied) : CreateRouteStop(http, id, body, db, audit, ct);
        });
        app.MapPut("/api/routes/{id:long}/stops/{stopId:long}", (HttpContext http, long id, long stopId, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "dispatch:manage");
            return denied is not null ? Task.FromResult(denied) : UpdateRouteStop(http, id, stopId, body, db, audit, ct);
        });
        app.MapDelete("/api/routes/{id:long}/stops/{stopId:long}", (HttpContext http, long id, long stopId, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "dispatch:manage");
            return denied is not null ? Task.FromResult(denied) : DeleteRouteStop(http, id, stopId, db, audit, ct);
        });
        app.MapPost("/api/routes/{id:long}/optimize-preview", RouteOptimizePreview);
        app.MapPost("/api/routes/{id:long}/assign", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "dispatch:manage");
            return denied is not null ? Task.FromResult(denied) : AssignRoute(http, id, body, db, audit, ct);
        });
        app.MapGet("/api/routes/{id:long}/timeline", Timeline("Route"));
        app.MapGet("/api/routes/{id:long}/recommendations", RouteRecommendations);

        app.MapGet("/api/customer-eta/summary", CustomerEtaSummary);
        app.MapGet("/api/customer-eta/track/{trackingCode}", CustomerEtaTrack);
        app.MapGet("/api/customer-eta/job/{jobId:long}", CustomerEtaJob);
        app.MapPost("/api/customer-eta/job/{jobId:long}/send-update", (HttpContext http, long jobId, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "dispatch:manage");
            return denied is not null ? Task.FromResult(denied) : CustomerEtaSendUpdate(http, jobId, body, db, audit, ct);
        });
        app.MapPost("/api/customer-eta/job/{jobId:long}/feedback", (HttpContext http, long jobId, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "dispatch:manage");
            return denied is not null ? Task.FromResult(denied) : CustomerEtaFeedback(http, jobId, body, db, audit, ct);
        });
        app.MapGet("/api/customer-eta/communications", CustomerEtaCommunications);
        app.MapGet("/api/customer-eta/recommendations", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM ai_recommendations WHERE module_key IN ('customer-eta','customer-portal') ORDER BY score DESC LIMIT 8", ct: ct));

        app.MapGet("/api/maintenance/summary", MaintenanceSummary);
        app.MapGet("/api/maintenance/due", (Database db, CancellationToken ct) => OkRows(db, MaintenanceBaseSql + " WHERE mi.deleted_at IS NULL AND mi.due_date BETWEEN CURDATE() AND DATE_ADD(CURDATE(), INTERVAL 14 DAY) ORDER BY mi.due_date", ct: ct));
        app.MapGet("/api/maintenance/overdue", (Database db, CancellationToken ct) => OkRows(db, MaintenanceBaseSql + " WHERE mi.deleted_at IS NULL AND (mi.status='Overdue' OR mi.due_date < CURDATE()) ORDER BY mi.due_date", ct: ct));
        app.MapGet("/api/maintenance/recommendations", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM ai_recommendations WHERE module_key='maintenance' ORDER BY score DESC LIMIT 8", ct: ct));
        app.MapGet("/api/maintenance", MaintenanceItems);
        app.MapGet("/api/maintenance/{id:long}", MaintenanceDetail);
        app.MapPost("/api/maintenance", (HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "maintenance:manage");
            return denied is not null ? Task.FromResult(denied) : CreateMaintenance(http, body, db, audit, ct);
        });
        app.MapPut("/api/maintenance/{id:long}", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "maintenance:manage");
            return denied is not null ? Task.FromResult(denied) : UpdateMaintenance(http, id, body, db, audit, ct);
        });
        app.MapDelete("/api/maintenance/{id:long}", SoftDeleteWithPermission("maintenance_items", "maintenance.deleted", "maintenance:manage"));
        app.MapPost("/api/maintenance/{id:long}/schedule", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "maintenance:manage");
            return denied is not null ? Task.FromResult(denied) : MaintenanceSchedule(http, id, body, db, audit, ct);
        });
        app.MapPost("/api/maintenance/{id:long}/defer", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "maintenance:manage");
            return denied is not null ? Task.FromResult(denied) : MaintenanceDefer(http, id, body, db, audit, ct);
        });
        app.MapPost("/api/maintenance/{id:long}/create-workorder", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "maintenance:manage");
            return denied is not null ? Task.FromResult(denied) : MaintenanceCreateWorkOrder(http, id, body, db, audit, ct);
        });

        app.MapGet("/api/workorders/summary", WorkOrdersSummary);
        app.MapGet("/api/workorders", WorkOrders);
        app.MapGet("/api/workorders/{id:long}", WorkOrderDetail);
        app.MapPost("/api/workorders", (HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "maintenance:manage");
            return denied is not null ? Task.FromResult(denied) : CreateWorkOrder(http, body, db, audit, ct);
        });
        app.MapPut("/api/workorders/{id:long}", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "maintenance:manage");
            return denied is not null ? Task.FromResult(denied) : UpdateWorkOrder(http, id, body, db, audit, ct);
        });
        app.MapDelete("/api/workorders/{id:long}", SoftDeleteWithPermission("work_orders", "workorder.deleted", "maintenance:manage"));
        app.MapGet("/api/workorders/{id:long}/timeline", WorkOrderTimeline);
        app.MapGet("/api/workorders/{id:long}/recommendations", Recommendations("work-orders"));
        app.MapPost("/api/workorders/{id:long}/assign", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "maintenance:manage");
            return denied is not null ? Task.FromResult(denied) : WorkOrderAssign(http, id, body, db, audit, ct);
        });
        app.MapPost("/api/workorders/{id:long}/status", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "maintenance:manage");
            return denied is not null ? Task.FromResult(denied) : WorkOrderStatus(http, id, body, db, audit, ct);
        });
        app.MapPost("/api/workorders/{id:long}/add-labor", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "maintenance:manage");
            return denied is not null ? Task.FromResult(denied) : WorkOrderAddLabor(http, id, body, db, audit, ct);
        });
        app.MapPost("/api/workorders/{id:long}/add-part", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "maintenance:manage");
            return denied is not null ? Task.FromResult(denied) : WorkOrderAddPart(http, id, body, db, audit, ct);
        });
        app.MapPost("/api/workorders/{id:long}/complete", (HttpContext http, long id, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "maintenance:manage");
            return denied is not null ? Task.FromResult(denied) : WorkOrderComplete(http, id, db, audit, ct);
        });
        app.MapPost("/api/workorders/{id:long}/approve-cost", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "maintenance:manage");
            return denied is not null ? Task.FromResult(denied) : WorkOrderApproveCost(http, id, body, db, audit, ct);
        });

        app.MapGet("/api/dvir/summary", DvirSummary);
        app.MapGet("/api/dvir/templates", DvirTemplates);
        app.MapPost("/api/dvir/templates", (HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "maintenance:manage");
            return denied is not null ? Task.FromResult(denied) : CreateDvirTemplate(body, db, audit, ct);
        });
        app.MapPut("/api/dvir/templates/{id:long}", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "maintenance:manage");
            return denied is not null ? Task.FromResult(denied) : UpdateDvirTemplate(id, body, db, audit, ct);
        });
        app.MapGet("/api/dvir/recommendations", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM ai_recommendations WHERE module_key IN ('dvir','dvir-inspections') ORDER BY score DESC LIMIT 8", ct: ct));
        app.MapGet("/api/dvir/reports", DvirReports);
        app.MapGet("/api/dvir/reports/{id:long}", DvirDetail);
        app.MapPost("/api/dvir/reports", (HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "maintenance:manage");
            return denied is not null ? Task.FromResult(denied) : CreateDvirReport(body, db, audit, ct);
        });
        app.MapPut("/api/dvir/reports/{id:long}", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "maintenance:manage");
            return denied is not null ? Task.FromResult(denied) : UpdateDvirReport(id, body, db, audit, ct);
        });
        app.MapDelete("/api/dvir/reports/{id:long}", (HttpContext http, long id, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "maintenance:manage");
            return denied is not null ? Task.FromResult(denied) : DeleteDvirReport(id, db, audit, ct);
        });
        app.MapPost("/api/dvir/reports/{id:long}/mechanic-review", (HttpContext http, long id, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "maintenance:manage");
            return denied is not null ? Task.FromResult(denied) : DvirMechanicReview(id, db, audit, ct);
        });
        app.MapPost("/api/dvir/reports/{id:long}/certify-repair", (HttpContext http, long id, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "maintenance:manage");
            return denied is not null ? Task.FromResult(denied) : DvirCertifyRepair(id, db, audit, ct);
        });
        app.MapPost("/api/dvir/reports/{id:long}/driver-sign", (HttpContext http, long id, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "maintenance:manage");
            return denied is not null ? Task.FromResult(denied) : DvirDriverSign(id, db, audit, ct);
        });
        app.MapGet("/api/dvir/reports/{id:long}/timeline", DvirTimeline);

        app.MapGet("/api/documents/summary", DocumentsSummary);
        app.MapGet("/api/documents/expiring", (Database db, CancellationToken ct) => OkRows(db, DocumentsBaseSql + " WHERE d.deleted_at IS NULL AND (d.status IN ('Expiring','Expired') OR d.expires_at <= DATE_ADD(CURDATE(), INTERVAL 30 DAY)) ORDER BY d.expires_at", ct: ct));
        app.MapGet("/api/documents/recommendations", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM ai_recommendations WHERE module_key='documents' ORDER BY score DESC LIMIT 8", ct: ct));
        app.MapGet("/api/documents", Documents);
        app.MapGet("/api/documents/{id:long}", DocumentDetail);
        app.MapPost("/api/documents", (HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "compliance:manage");
            return denied is not null ? Task.FromResult(denied) : CreateDocument(body, db, audit, ct);
        });
        app.MapPut("/api/documents/{id:long}", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "compliance:manage");
            return denied is not null ? Task.FromResult(denied) : UpdateDocument(id, body, db, audit, ct);
        });
        app.MapDelete("/api/documents/{id:long}", SoftDeleteWithPermission("documents", "document.deleted", "compliance:manage"));
        app.MapPost("/api/documents/upload-placeholder", (HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "compliance:manage");
            return denied is not null ? Task.FromResult(denied) : DocumentUploadPlaceholder(body, db, audit, ct);
        });
        app.MapPost("/api/documents/{id:long}/renew-placeholder", (HttpContext http, long id, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "compliance:manage");
            return denied is not null ? Task.FromResult(denied) : DocumentRenewPlaceholder(id, db, audit, ct);
        });
        app.MapGet("/api/documents/{id:long}/timeline", DocumentTimeline);

        app.MapGet("/api/safety/summary", SafetySummary);
        app.MapGet("/api/safety/events", SafetyEvents);
        app.MapGet("/api/safety/events/{id:long}", SafetyEventDetail);
        app.MapPost("/api/safety/events", (HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "safety:manage");
            return denied is not null ? Task.FromResult(denied) : CreateSafetyEvent(http, body, db, audit, ct);
        });
        app.MapPut("/api/safety/events/{id:long}", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "safety:manage");
            return denied is not null ? Task.FromResult(denied) : UpdateSafetyEvent(http, id, body, db, audit, ct);
        });
        app.MapDelete("/api/safety/events/{id:long}", SoftDeleteWithPermission("safety_events", "safety.event.deleted", "safety:manage"));
        app.MapGet("/api/safety/drivers/scorecards", (Database db, CancellationToken ct) => OkRows(db, "SELECT sc.*, d.full_name driver_name, d.driver_code FROM driver_safety_scorecards sc JOIN drivers d ON d.id=sc.driver_id ORDER BY sc.risk_score DESC", ct: ct));
        app.MapGet("/api/safety/vehicles/scorecards", (Database db, CancellationToken ct) => OkRows(db, "SELECT sc.*, v.vehicle_code, v.type FROM vehicle_safety_scorecards sc JOIN vehicles v ON v.id=sc.vehicle_id ORDER BY sc.risk_score DESC", ct: ct));
        app.MapGet("/api/safety/recommendations", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM ai_recommendations WHERE module_key='safety' ORDER BY score DESC LIMIT 8", ct: ct));
        app.MapGet("/api/safety/trends", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM safety_trends ORDER BY trend_date", ct: ct));
        app.MapPost("/api/safety/events/{id:long}/review", (HttpContext http, long id, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "safety:manage");
            return denied is not null ? Task.FromResult(denied) : SafetyReview(http, id, db, audit, ct);
        });
        app.MapPost("/api/safety/events/{id:long}/create-coaching-task", (HttpContext http, long id, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "safety:manage");
            return denied is not null ? Task.FromResult(denied) : SafetyCreateCoaching(http, id, db, audit, ct);
        });
        app.MapPost("/api/safety/events/{id:long}/create-incident", (HttpContext http, long id, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "safety:manage");
            return denied is not null ? Task.FromResult(denied) : SafetyCreateIncident(http, id, db, audit, ct);
        });

        app.MapGet("/api/dashcam/summary", DashcamSummary);
        app.MapGet("/api/dashcam/events", DashcamEvents);
        app.MapGet("/api/dashcam/events/{id:long}", DashcamEventDetail);
        app.MapPost("/api/dashcam/events", (HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "dashcam:manage");
            return denied is not null ? Task.FromResult(denied) : CreateDashcamEvent(http, body, db, audit, ct);
        });
        app.MapPut("/api/dashcam/events/{id:long}", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "dashcam:manage");
            return denied is not null ? Task.FromResult(denied) : UpdateDashcamEvent(http, id, body, db, audit, ct);
        });
        app.MapDelete("/api/dashcam/events/{id:long}", SoftDeleteWithPermission("dashcam_events", "dashcam.event.deleted", "dashcam:manage"));
        app.MapGet("/api/dashcam/recommendations", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM ai_recommendations WHERE module_key='dashcam' ORDER BY score DESC LIMIT 8", ct: ct));
        app.MapPost("/api/dashcam/events/{id:long}/review", (HttpContext http, long id, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "dashcam:manage");
            return denied is not null ? Task.FromResult(denied) : DashcamReview(http, id, db, audit, ct);
        });
        app.MapPost("/api/dashcam/events/{id:long}/mark-false-positive", (HttpContext http, long id, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "dashcam:manage");
            return denied is not null ? Task.FromResult(denied) : DashcamFalsePositive(http, id, db, audit, ct);
        });
        app.MapPost("/api/dashcam/events/{id:long}/create-coaching-task", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "dashcam:manage");
            return denied is not null ? Task.FromResult(denied) : DashcamCreateCoaching(http, id, body, db, audit, ct);
        });
        app.MapPost("/api/dashcam/events/{id:long}/create-evidence-package", (HttpContext http, long id, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "dashcam:manage");
            return denied is not null ? Task.FromResult(denied) : DashcamCreateEvidencePackage(http, id, db, audit, ct);
        });
        app.MapPost("/api/dashcam/events/{id:long}/create-incident-report", (HttpContext http, long id, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "dashcam:manage");
            return denied is not null ? Task.FromResult(denied) : DashcamCreateIncidentReport(http, id, db, audit, ct);
        });

        app.MapGet("/api/coaching/summary", CoachingSummary);
        app.MapGet("/api/coaching/tasks", CoachingTasks);
        app.MapGet("/api/coaching/tasks/{id:long}", CoachingTaskDetail);
        app.MapPost("/api/coaching/tasks", (HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "safety:manage");
            return denied is not null ? Task.FromResult(denied) : CreateCoachingTask(http, body, db, audit, ct);
        });
        app.MapPut("/api/coaching/tasks/{id:long}", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "safety:manage");
            return denied is not null ? Task.FromResult(denied) : UpdateCoachingTask(http, id, body, db, audit, ct);
        });
        app.MapDelete("/api/coaching/tasks/{id:long}", SoftDeleteWithPermission("coaching_tasks", "coaching.deleted", "safety:manage"));
        app.MapGet("/api/coaching/recommendations", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM ai_recommendations WHERE module_key='coaching' ORDER BY score DESC LIMIT 8", ct: ct));
        app.MapPost("/api/coaching/tasks/{id:long}/assign", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "safety:manage");
            return denied is not null ? Task.FromResult(denied) : CoachingAssign(http, id, body, db, audit, ct);
        });
        app.MapPost("/api/coaching/tasks/{id:long}/acknowledge", (HttpContext http, long id, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "safety:manage");
            return denied is not null ? Task.FromResult(denied) : CoachingAcknowledge(http, id, db, audit, ct);
        });
        app.MapPost("/api/coaching/tasks/{id:long}/complete", (HttpContext http, long id, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "safety:manage");
            return denied is not null ? Task.FromResult(denied) : CoachingComplete(http, id, db, audit, ct);
        });
        app.MapPost("/api/coaching/tasks/{id:long}/add-note", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "safety:manage");
            return denied is not null ? Task.FromResult(denied) : CoachingAddNote(http, id, body, db, audit, ct);
        });

        app.MapGet("/api/incidents/summary", IncidentsSummary);
        app.MapGet("/api/incidents", Incidents);
        app.MapGet("/api/incidents/{id:long}", IncidentDetail);
        app.MapPost("/api/incidents", (HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "safety:manage");
            return denied is not null ? Task.FromResult(denied) : CreateIncident(http, body, db, audit, ct);
        });
        app.MapPut("/api/incidents/{id:long}", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "safety:manage");
            return denied is not null ? Task.FromResult(denied) : UpdateIncident(http, id, body, db, audit, ct);
        });
        app.MapDelete("/api/incidents/{id:long}", SoftDeleteWithPermission("incidents", "incident.deleted", "safety:manage"));
        app.MapGet("/api/incidents/{id:long}/timeline", IncidentTimeline);
        app.MapGet("/api/incidents/{id:long}/recommendations", (long id, Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM ai_recommendations WHERE module_key='incidents' ORDER BY score DESC LIMIT 8", ct: ct));
        app.MapPost("/api/incidents/{id:long}/status", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "safety:manage");
            return denied is not null ? Task.FromResult(denied) : IncidentStatus(http, id, body, db, audit, ct);
        });
        app.MapPost("/api/incidents/{id:long}/attach-evidence", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "safety:manage");
            return denied is not null ? Task.FromResult(denied) : IncidentAttachEvidence(http, id, body, db, audit, ct);
        });
        app.MapPost("/api/incidents/{id:long}/create-insurance-report", (HttpContext http, long id, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "safety:manage");
            return denied is not null ? Task.FromResult(denied) : IncidentCreateInsuranceReport(http, id, db, audit, ct);
        });

        app.MapGet("/api/evidence-packages/summary", EvidenceSummary);
        app.MapGet("/api/evidence-packages", EvidencePackages);
        app.MapGet("/api/evidence-packages/{id:long}", EvidencePackageDetail);
        app.MapPost("/api/evidence-packages", (HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "safety:manage");
            return denied is not null ? Task.FromResult(denied) : CreateEvidencePackage(http, body, db, audit, ct);
        });
        app.MapPut("/api/evidence-packages/{id:long}", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "safety:manage");
            return denied is not null ? Task.FromResult(denied) : UpdateEvidencePackage(http, id, body, db, audit, ct);
        });
        app.MapDelete("/api/evidence-packages/{id:long}", SoftDeleteWithPermission("evidence_packages", "evidence.package.deleted", "safety:manage"));
        app.MapPost("/api/evidence-packages/{id:long}/generate-export-placeholder", (HttpContext http, long id, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "safety:manage");
            return denied is not null ? Task.FromResult(denied) : EvidenceExport(http, id, db, audit, ct);
        });
        app.MapPost("/api/evidence-packages/{id:long}/lock-package", (HttpContext http, long id, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "safety:manage");
            return denied is not null ? Task.FromResult(denied) : EvidenceLock(http, id, db, audit, ct);
        });

        app.MapGet("/api/ai/insights", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM ai_insights ORDER BY created_at DESC LIMIT 30", ct: ct));
        app.MapPost("/api/ai/ask", AiAsk);

        // ===== BATCH 5: FUEL & IDLING ===========================================
        app.MapGet("/api/fuel/summary", FuelSummary);
        app.MapGet("/api/fuel/transactions", FuelTransactions);
        app.MapGet("/api/fuel/transactions/{id:long}", FuelTransactionDetail);
        app.MapPost("/api/fuel/transactions", (HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "fuel:manage");
            return denied is not null ? Task.FromResult(denied) : CreateFuelTransaction(http, body, db, audit, ct);
        });
        app.MapPut("/api/fuel/transactions/{id:long}", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "fuel:manage");
            return denied is not null ? Task.FromResult(denied) : UpdateFuelTransaction(http, id, body, db, audit, ct);
        });
        app.MapDelete("/api/fuel/transactions/{id:long}", SoftDeleteWithPermission("fuel_transactions", "fuel.transaction.deleted", "fuel:manage"));
        app.MapGet("/api/fuel/idling-events", IdlingEvents);
        app.MapGet("/api/fuel/idling-events/{id:long}", IdlingEventDetail);
        app.MapPost("/api/fuel/idling-events", (HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "fuel:manage");
            return denied is not null ? Task.FromResult(denied) : CreateIdlingEvent(http, body, db, audit, ct);
        });
        app.MapPut("/api/fuel/idling-events/{id:long}", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "fuel:manage");
            return denied is not null ? Task.FromResult(denied) : UpdateIdlingEvent(http, id, body, db, audit, ct);
        });
        app.MapGet("/api/fuel/vehicle/{vehicleId:long}/summary", (long vehicleId, Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM fuel_transactions WHERE vehicle_id=@id AND deleted_at IS NULL ORDER BY fuel_date DESC LIMIT 24", c => c.Parameters.AddWithValue("@id", vehicleId), ct: ct));
        app.MapGet("/api/fuel/driver/{driverId:long}/summary", (long driverId, Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM fuel_transactions WHERE driver_id=@id AND deleted_at IS NULL ORDER BY fuel_date DESC LIMIT 24", c => c.Parameters.AddWithValue("@id", driverId), ct: ct));
        app.MapGet("/api/fuel/vehicle-summary", (Database db, CancellationToken ct) => OkRows(db,
            @"SELECT ft.vehicle_id, v.vehicle_code, v.type vehicle_type,
                     COUNT(*) transactions, ROUND(SUM(ft.quantity),2) total_quantity,
                     ROUND(SUM(ft.total_cost),2) total_cost, ROUND(AVG(ft.unit_price),4) avg_unit_price,
                     SUM(IF(ft.anomaly_status='Anomaly Detected',1,0)) anomaly_count
              FROM fuel_transactions ft LEFT JOIN vehicles v ON v.id=ft.vehicle_id
              WHERE ft.deleted_at IS NULL GROUP BY ft.vehicle_id, v.vehicle_code, v.type
              ORDER BY total_cost DESC LIMIT 20", ct: ct));
        app.MapGet("/api/fuel/driver-summary", (Database db, CancellationToken ct) => OkRows(db,
            @"SELECT ft.driver_id, d.full_name driver_name,
                     COUNT(*) transactions, ROUND(SUM(ft.quantity),2) total_quantity,
                     ROUND(SUM(ft.total_cost),2) total_cost, ROUND(AVG(ft.unit_price),4) avg_unit_price,
                     SUM(IF(ft.anomaly_status='Anomaly Detected',1,0)) anomaly_count
              FROM fuel_transactions ft LEFT JOIN drivers d ON d.id=ft.driver_id
              WHERE ft.deleted_at IS NULL GROUP BY ft.driver_id, d.full_name
              ORDER BY total_cost DESC LIMIT 20", ct: ct));
        app.MapGet("/api/fuel/anomalies", FuelAnomalies);
        app.MapGet("/api/fuel/recommendations", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM ai_recommendations WHERE module_key='fuel-idling' ORDER BY score DESC LIMIT 8", ct: ct));
        app.MapPost("/api/fuel/import-preview", FuelImportPreview);
        app.MapPost("/api/fuel/anomalies/{id:long}/review", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "fuel:manage");
            return denied is not null ? Task.FromResult(denied) : FuelAnomalyReview(http, id, body, db, audit, ct);
        });

        // ===== BATCH 5: EXPENSES ===============================================
        app.MapGet("/api/expenses/summary", ExpensesSummary);
        app.MapGet("/api/expenses", Expenses);
        app.MapGet("/api/expenses/{id:long}", ExpenseDetail);
        app.MapPost("/api/expenses", CreateExpense);
        app.MapPut("/api/expenses/{id:long}", UpdateExpense);
        app.MapDelete("/api/expenses/{id:long}", SoftDeleteWithPermission("expenses", "expense.deleted", "finance:manage"));
        app.MapPost("/api/expenses/{id:long}/approve", ExpenseApprove);
        app.MapPost("/api/expenses/{id:long}/reject", ExpenseReject);
        app.MapGet("/api/expenses/categories", (HttpContext http, Database db, CancellationToken ct) =>
            OkRows(db, "SELECT * FROM expense_categories WHERE company_id=@companyId AND status='Active' ORDER BY category_name",
                c => c.Parameters.AddWithValue("@companyId", GetCompanyId(http)), ct: ct));
        app.MapGet("/api/expenses/recommendations", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM ai_recommendations WHERE module_key='expenses' ORDER BY score DESC LIMIT 8", ct: ct));
        app.MapPost("/api/expenses/import-preview", ExpenseImportPreview);

        // ===== BATCH 5: CONTRACTS / RATES ======================================
        app.MapGet("/api/contracts/summary", ContractsSummary);
        app.MapGet("/api/contracts", Contracts);
        app.MapGet("/api/contracts/{id:long}", ContractDetail);
        app.MapPost("/api/contracts", CreateContract);
        app.MapPut("/api/contracts/{id:long}", UpdateContract);
        app.MapDelete("/api/contracts/{id:long}", SoftDeleteWithPermission("contracts", "contract.deleted", "finance:manage"));
        app.MapGet("/api/contracts/{id:long}/rates", (long id, Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM contract_rates WHERE contract_id=@id AND status='Active' ORDER BY effective_date DESC", c => c.Parameters.AddWithValue("@id", id), ct: ct));
        app.MapPost("/api/contracts/{id:long}/rates", CreateContractRate);
        app.MapPut("/api/contracts/{id:long}/rates/{rateId:long}", UpdateContractRate);
        app.MapDelete("/api/contracts/{id:long}/rates/{rateId:long}", (HttpContext http, long id, long rateId, Database db, AuditService audit, CancellationToken ct) => DeleteContractRate(http, id, rateId, db, audit, ct));
        app.MapGet("/api/contracts/recommendations", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM ai_recommendations WHERE module_key='contracts-rates' ORDER BY score DESC LIMIT 8", ct: ct));
        app.MapPost("/api/contracts/{id:long}/activate", ContractActivate);
        app.MapPost("/api/contracts/{id:long}/expire", ContractExpire);

        // ===== BATCH 5: CARRIER MANAGEMENT =====================================
        app.MapGet("/api/carriers/summary", CarriersSummary);
        app.MapGet("/api/carriers", Carriers);
        app.MapGet("/api/carriers/{id:long}", CarrierDetail);
        app.MapPost("/api/carriers", CreateCarrier);
        app.MapPut("/api/carriers/{id:long}", UpdateCarrier);
        app.MapDelete("/api/carriers/{id:long}", SoftDeleteWithPermission("carriers", "carrier.deleted", "finance:manage"));
        app.MapGet("/api/carriers/{id:long}/performance", (long id, Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM carrier_performance WHERE carrier_id=@id ORDER BY period_start DESC LIMIT 12", c => c.Parameters.AddWithValue("@id", id), ct: ct));
        app.MapGet("/api/carriers/{id:long}/documents", (long id, Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM carrier_documents WHERE carrier_id=@id ORDER BY expiry_date", c => c.Parameters.AddWithValue("@id", id), ct: ct));
        app.MapPost("/api/carriers/{id:long}/status", CarrierStatus);
        app.MapGet("/api/carriers/recommendations", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM ai_recommendations WHERE module_key='carrier-management' ORDER BY score DESC LIMIT 8", ct: ct));

        // ===== BATCH 5: PREDICTIVE COST & MARGIN ================================
        app.MapGet("/api/cost-margin/summary", CostMarginSummary);
        app.MapGet("/api/cost-margin/jobs", (Database db, CancellationToken ct) => OkRows(db, "SELECT cm.*, COALESCE(j.job_code,CONCAT('Job-',cm.entity_id)) job_code, c.name customer_name FROM cost_margin_records cm LEFT JOIN jobs j ON j.id=cm.job_id LEFT JOIN customers c ON c.id=cm.customer_id WHERE cm.entity_type='job' ORDER BY cm.margin_percent ASC LIMIT 50", ct: ct));
        app.MapGet("/api/cost-margin/routes", (Database db, CancellationToken ct) => OkRows(db, "SELECT cm.*, r.route_code, COALESCE(r.route_name,r.name) route_name FROM cost_margin_records cm LEFT JOIN routes r ON r.id=cm.route_id WHERE cm.entity_type='route' ORDER BY cm.margin_percent ASC LIMIT 50", ct: ct));
        app.MapGet("/api/cost-margin/vehicles", (Database db, CancellationToken ct) => OkRows(db, "SELECT cm.*, v.vehicle_code, v.type vehicle_type FROM cost_margin_records cm LEFT JOIN vehicles v ON v.id=cm.vehicle_id WHERE cm.entity_type='vehicle' ORDER BY cm.total_cost DESC LIMIT 50", ct: ct));
        app.MapGet("/api/cost-margin/customers", (Database db, CancellationToken ct) => OkRows(db, "SELECT cm.*, c.name customer_name, c.sla_tier FROM cost_margin_records cm LEFT JOIN customers c ON c.id=cm.customer_id WHERE cm.entity_type='customer' ORDER BY cm.margin_percent ASC LIMIT 50", ct: ct));
        app.MapGet("/api/cost-margin/predictions", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM cost_margin_predictions ORDER BY risk_level DESC, created_at DESC LIMIT 30", ct: ct));
        app.MapGet("/api/cost-margin/recommendations", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM ai_recommendations WHERE module_key='predictive-margin' ORDER BY score DESC LIMIT 8", ct: ct));
        app.MapPost("/api/cost-margin/recalculate", CostMarginRecalculate);
        app.MapPost("/api/cost-margin/jobs/{jobId:long}/recalculate", (long jobId, Database db, AuditService audit, CancellationToken ct) => CostMarginRecalculateJob(jobId, db, audit, ct));

        // ===== BATCH 5: COST LEAKAGE INTELLIGENCE ================================
        app.MapGet("/api/cost-leakage/summary", CostLeakageSummary);
        app.MapGet("/api/cost-leakage/items", CostLeakageItems);
        app.MapGet("/api/cost-leakage/recommendations", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM ai_recommendations WHERE module_key='cost-leakage' ORDER BY score DESC LIMIT 8", ct: ct));
        app.MapPost("/api/cost-leakage/items/{id:long}/acknowledge", CostLeakageAcknowledge);
        app.MapPost("/api/cost-leakage/items/{id:long}/create-action", CostLeakageCreateAction);

        // ===== BATCH 6: COMPLIANCE CENTER ========================================
        app.MapGet("/api/compliance/summary", ComplianceSummary);
        app.MapGet("/api/compliance/profiles", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM compliance_profiles WHERE is_active=1 ORDER BY country_code, profile_name", ct: ct));
        app.MapGet("/api/compliance/rules", (Database db, CancellationToken ct) => OkRows(db, "SELECT cr.*, cp.profile_name, cp.country_code FROM compliance_rules cr JOIN compliance_profiles cp ON cp.id=cr.profile_id WHERE cr.is_active=1 ORDER BY cr.severity DESC, cr.profile_id", ct: ct));
        app.MapGet("/api/compliance/violations", (Database db, CancellationToken ct) => OkRows(db, @"SELECT cv.*, d.full_name driver_name, d.driver_code, v.vehicle_code, cp.profile_name FROM compliance_violations cv LEFT JOIN drivers d ON d.id=cv.driver_id LEFT JOIN vehicles v ON v.id=cv.vehicle_id LEFT JOIN compliance_profiles cp ON cp.id=cv.profile_id ORDER BY FIELD(cv.severity,'Critical','High','Medium','Low'), cv.detected_at DESC LIMIT 50", ct: ct));
        app.MapGet("/api/compliance/violations/{id:long}", (long id, Database db, CancellationToken ct) => OkRows(db, "SELECT cv.*, d.full_name driver_name, v.vehicle_code FROM compliance_violations cv LEFT JOIN drivers d ON d.id=cv.driver_id LEFT JOIN vehicles v ON v.id=cv.vehicle_id WHERE cv.id=@id", c => c.Parameters.AddWithValue("@id", id), ct: ct));
        app.MapPost("/api/compliance/violations/{id:long}/acknowledge", (long id, Database db, AuditService audit, CancellationToken ct) => SimpleUpdateStatus("compliance_violations", id, "Acknowledged", "compliance.violation_acknowledged", db, audit, ct));
        app.MapPost("/api/compliance/violations/{id:long}/resolve", (long id, Database db, AuditService audit, CancellationToken ct) => SimpleUpdateStatus("compliance_violations", id, "Resolved", "compliance.violation_resolved", db, audit, ct));
        app.MapGet("/api/compliance/documents", (Database db, CancellationToken ct) => OkRows(db, @"SELECT d.*, e.name entity_name FROM documents d LEFT JOIN (SELECT id, full_name name FROM drivers UNION ALL SELECT id, vehicle_code name FROM vehicles) e ON e.id=d.entity_id WHERE d.deleted_at IS NULL ORDER BY d.expires_at LIMIT 50", ct: ct));
        app.MapGet("/api/compliance/audit-packages", (Database db, CancellationToken ct) => OkRows(db, "SELECT cap.*, cp.profile_name FROM compliance_audit_packages cap LEFT JOIN compliance_profiles cp ON cp.id=cap.profile_id ORDER BY cap.created_at DESC", ct: ct));
        app.MapGet("/api/compliance/audit-packages/{id:long}", (long id, Database db, CancellationToken ct) => OkRows(db, "SELECT cap.*, cp.profile_name FROM compliance_audit_packages cap LEFT JOIN compliance_profiles cp ON cp.id=cap.profile_id WHERE cap.id=@id", c => c.Parameters.AddWithValue("@id", id), ct: ct));
        app.MapPost("/api/compliance/audit-packages", CreateAuditPackage);
        app.MapPost("/api/compliance/audit-packages/{id:long}/finalize", (long id, Database db, AuditService audit, CancellationToken ct) => SimpleUpdateStatus("compliance_audit_packages", id, "Ready", "compliance.audit_package_finalized", db, audit, ct));
        app.MapGet("/api/compliance/cross-border-watch", (Database db, CancellationToken ct) => OkRows(db, @"SELECT cv.*, cp.profile_name, cp.country_code, cp.authority FROM compliance_violations cv JOIN compliance_profiles cp ON cp.id=cv.profile_id WHERE cv.country_code != 'US' OR cv.status IN ('Open','Escalated') ORDER BY FIELD(cv.severity,'Critical','High','Medium','Low'), cv.detected_at DESC LIMIT 30", ct: ct));
        app.MapGet("/api/compliance/driver-status", (Database db, CancellationToken ct) => OkRows(db, "SELECT dcs.*, d.full_name driver_name, d.driver_code, cp.profile_name FROM driver_compliance_status dcs JOIN drivers d ON d.id=dcs.driver_id LEFT JOIN compliance_profiles cp ON cp.id=dcs.profile_id ORDER BY FIELD(dcs.overall_status,'Violation','Warning','Compliant'), d.full_name", ct: ct));
        app.MapGet("/api/compliance/vehicle-status", (Database db, CancellationToken ct) => OkRows(db, "SELECT vcs.*, v.vehicle_code, v.type vehicle_type, cp.profile_name FROM vehicle_compliance_status vcs JOIN vehicles v ON v.id=vcs.vehicle_id LEFT JOIN compliance_profiles cp ON cp.id=vcs.profile_id ORDER BY FIELD(vcs.overall_status,'Violation','Warning','Compliant'), v.vehicle_code", ct: ct));
        app.MapGet("/api/compliance/ai/recommendations", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM ai_recommendations WHERE module_key='compliance' ORDER BY score DESC LIMIT 10", ct: ct));

        // ===== BATCH 6: HOS / ELD ================================================
        app.MapGet("/api/hos/summary", HosSummary);
        app.MapGet("/api/hos/drivers", (Database db, CancellationToken ct) => OkRows(db, @"SELECT hc.*, d.full_name driver_name, d.driver_code, d.status driver_status, cp.profile_name FROM hos_clocks hc JOIN drivers d ON d.id=hc.driver_id LEFT JOIN compliance_profiles cp ON cp.id=hc.profile_id ORDER BY FIELD(hc.status,'Violation','Warning','OK'), hc.drive_time_remaining_minutes ASC", ct: ct));
        app.MapGet("/api/hos/clocks", (Database db, CancellationToken ct) => OkRows(db, "SELECT hc.*, d.full_name driver_name, d.driver_code FROM hos_clocks hc JOIN drivers d ON d.id=hc.driver_id ORDER BY hc.drive_time_remaining_minutes ASC", ct: ct));
        app.MapGet("/api/hos/logs", (Database db, CancellationToken ct) => OkRows(db, @"SELECT hl.*, d.full_name driver_name, d.driver_code, v.vehicle_code FROM hos_logs hl JOIN drivers d ON d.id=hl.driver_id LEFT JOIN vehicles v ON v.id=hl.vehicle_id WHERE hl.deleted_at IS NULL ORDER BY hl.log_date DESC, hl.start_time DESC LIMIT 50", ct: ct));
        app.MapGet("/api/hos/logs/{driverId:long}", (long driverId, Database db, CancellationToken ct) => OkRows(db, "SELECT hl.*, v.vehicle_code FROM hos_logs hl LEFT JOIN vehicles v ON v.id=hl.vehicle_id WHERE hl.driver_id=@id AND hl.deleted_at IS NULL ORDER BY hl.log_date DESC, hl.start_time DESC LIMIT 30", c => c.Parameters.AddWithValue("@id", driverId), ct: ct));
        app.MapPost("/api/hos/logs/{id:long}/certify", HosCertify);
        app.MapGet("/api/hos/ai/recommendations", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM ai_recommendations WHERE module_key='hos-eld' ORDER BY score DESC LIMIT 10", ct: ct));

        app.MapGet("/api/eld/devices", (Database db, CancellationToken ct) => OkRows(db, @"SELECT e.*, v.vehicle_code, d.full_name driver_name, d.driver_code FROM eld_devices e LEFT JOIN vehicles v ON v.id=e.vehicle_id LEFT JOIN drivers d ON d.id=e.driver_id WHERE e.deleted_at IS NULL ORDER BY FIELD(e.status,'Malfunction','Diagnostic','Active'), e.device_serial", ct: ct));
        app.MapGet("/api/eld/devices/{id:long}", (long id, Database db, CancellationToken ct) => OkRows(db, "SELECT e.*, v.vehicle_code, d.full_name driver_name FROM eld_devices e LEFT JOIN vehicles v ON v.id=e.vehicle_id LEFT JOIN drivers d ON d.id=e.driver_id WHERE e.id=@id", c => c.Parameters.AddWithValue("@id", id), ct: ct));
        app.MapPost("/api/eld/devices/{id:long}/mark-malfunction", EldMarkMalfunction);
        app.MapPost("/api/eld/devices/{id:long}/resolve-malfunction", (long id, Database db, AuditService audit, CancellationToken ct) => SimpleUpdateStatus("eld_devices", id, "Active", "eld.malfunction.resolved", db, audit, ct));

        // ===== BATCH 6: LOCALIZATION =============================================
        app.MapGet("/api/localization/countries", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM countries ORDER BY name", ct: ct));
        app.MapGet("/api/localization/languages", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM languages ORDER BY name", ct: ct));
        app.MapGet("/api/localization/settings", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM tenant_locale_settings WHERE id=1 LIMIT 1", ct: ct));
        app.MapPut("/api/localization/settings", UpdateLocaleSettings);
        app.MapGet("/api/localization/user-preferences", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM user_locale_preferences LIMIT 1", ct: ct));
        app.MapPut("/api/localization/user-preferences", UpdateUserLocalePreferences);

        // ===== BATCH 7: REPORTS & ANALYTICS ======================================
        app.MapGet("/api/reports/catalog", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM report_catalog WHERE status='Active' ORDER BY report_category, report_name", ct: ct));
        app.MapGet("/api/reports/summary", ReportsSummary);
        app.MapGet("/api/reports/runs", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM report_runs ORDER BY started_at DESC LIMIT 50", ct: ct));
        app.MapPost("/api/reports/{key}/run", ReportRun);
        app.MapGet("/api/reports/scheduled", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM scheduled_reports ORDER BY next_run_at LIMIT 30", ct: ct));
        app.MapPost("/api/reports/scheduled", CreateScheduledReport);
        app.MapPost("/api/reports/scheduled/{id:long}/pause", (HttpContext http, long id, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "reports:manage");
            return denied is not null ? Task.FromResult(denied) : SimpleUpdateStatus("scheduled_reports", id, "Paused", "scheduled_report.paused", db, audit, ct);
        });
        app.MapPost("/api/reports/scheduled/{id:long}/resume", (HttpContext http, long id, Database db, AuditService audit, CancellationToken ct) =>
        {
            var denied = RequirePermission(http, "reports:manage");
            return denied is not null ? Task.FromResult(denied) : SimpleUpdateStatus("scheduled_reports", id, "Active", "scheduled_report.resumed", db, audit, ct);
        });
        app.MapGet("/api/reports/exports", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM report_exports ORDER BY requested_at DESC LIMIT 30", ct: ct));
        app.MapPost("/api/reports/exports", CreateReportExport);
        app.MapGet("/api/reports/ai/recommendations", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM ai_recommendations WHERE module_key='reports-analytics' ORDER BY score DESC LIMIT 10", ct: ct));

        // ===== BATCH 7: KPI / SLA ================================================
        app.MapGet("/api/kpi/metrics", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM kpi_metrics WHERE deleted_at IS NULL ORDER BY category, kpi_name", ct: ct));
        app.MapGet("/api/kpi/summary", KpiSummary);
        app.MapGet("/api/kpi/targets", (Database db, CancellationToken ct) => OkRows(db, "SELECT kt.*, km.kpi_name, km.kpi_key FROM kpi_targets kt LEFT JOIN kpi_metrics km ON km.id=kt.kpi_metric_id ORDER BY kt.period_start DESC LIMIT 30", ct: ct));
        app.MapGet("/api/kpi/ai/recommendations", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM ai_recommendations WHERE module_key='sla-kpi' ORDER BY score DESC LIMIT 10", ct: ct));
        app.MapGet("/api/sla/records", (Database db, CancellationToken ct) => OkRows(db, @"SELECT sr.*, c.company_name customer_name, j.job_number FROM sla_records sr LEFT JOIN customers c ON c.id=sr.customer_id LEFT JOIN jobs j ON j.id=sr.job_id ORDER BY FIELD(sr.status,'Breached','At Risk','Met'), sr.created_at DESC LIMIT 50", ct: ct));
        app.MapGet("/api/sla/summary", SlaSummary);
        app.MapGet("/api/sla/breaches", (Database db, CancellationToken ct) => OkRows(db, @"SELECT sb.*, sr.sla_name, sr.sla_type, c.company_name customer_name, j.job_number FROM sla_breaches sb JOIN sla_records sr ON sr.id=sb.sla_record_id LEFT JOIN customers c ON c.id=sr.customer_id LEFT JOIN jobs j ON j.id=sr.job_id ORDER BY sb.breach_detected_at DESC LIMIT 30", ct: ct));
        app.MapPost("/api/sla/breaches/{id:long}/acknowledge", (long id, Database db, AuditService audit, CancellationToken ct) => SimpleUpdateStatus("sla_breaches", id, "Acknowledged", "sla.breach_acknowledged", db, audit, ct));
        app.MapPost("/api/sla/breaches/{id:long}/resolve", (long id, Database db, AuditService audit, CancellationToken ct) => SimpleUpdateStatus("sla_breaches", id, "Resolved", "sla.breach_resolved", db, audit, ct));

        // ===== BATCH 7: AUDIT LOGS ===============================================
        app.MapGet("/api/audit/logs", AuditLogs);
        app.MapGet("/api/audit/logs/{id:long}", (long id, Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM audit_logs WHERE id=@id", c => c.Parameters.AddWithValue("@id", id), ct: ct));
        app.MapGet("/api/audit/export-requests", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM audit_export_requests ORDER BY requested_at DESC LIMIT 20", ct: ct));
        app.MapPost("/api/audit/export-requests", CreateAuditExportRequest);
        app.MapGet("/api/audit/ai/recommendations", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM ai_recommendations WHERE module_key='audit-logs' ORDER BY score DESC LIMIT 10", ct: ct));

        // ===== BATCH 7: EXECUTIVE DASHBOARD ======================================
        app.MapGet("/api/executive/snapshots", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM executive_snapshots WHERE deleted_at IS NULL ORDER BY snapshot_date DESC LIMIT 14", ct: ct));
        app.MapGet("/api/executive/summary", ExecutiveSummary);
        app.MapGet("/api/executive/ai/recommendations", (Database db, CancellationToken ct) => OkRows(db, "SELECT * FROM ai_recommendations WHERE module_key='executive' ORDER BY score DESC LIMIT 10", ct: ct));

        // ===== ABOUT / PLATFORM =====================================================
        app.MapGet("/api/about/platform", AboutPlatform);
        app.MapGet("/api/about/health-summary", AboutHealthSummary);

        MapDedicatedModule(app, "route-planning");
        MapDedicatedModule(app, "fuel-idling");
        MapDedicatedModule(app, "compliance");
        MapDedicatedModule(app, "hos-eld");
        MapDedicatedModule(app, "customer-portal");
        MapDedicatedModule(app, "contracts-rates");
        MapDedicatedModule(app, "carrier-management");
        MapDedicatedModule(app, "expenses");
        MapDedicatedModule(app, "reports-analytics");
        MapDedicatedModule(app, "sla-kpi");
        MapDedicatedModule(app, "predictive-margin");
        MapDedicatedModule(app, "audit-logs");
        MapDedicatedModule(app, "integrations");
        MapDedicatedModule(app, "user-management");
        MapDedicatedModule(app, "settings");
        MapDedicatedModule(app, "billing");
        MapDedicatedModule(app, "companies");
        MapDedicatedModule(app, "white-label");

        app.MapGet("/api/modules/{moduleKey}", GenericModule);
        app.MapGet("/api/modules/{moduleKey}/{id:long}", GenericModuleDetail);
        app.MapPost("/api/modules/{moduleKey}", CreateGenericModuleRecord);
        app.MapPut("/api/modules/{moduleKey}/{id:long}", UpdateGenericModuleRecord);
    }

    private static void MapDedicatedModule(WebApplication app, string moduleKey)
    {
        app.MapGet($"/api/{moduleKey}", (Database db, CancellationToken ct) => LoadModule(moduleKey, db, ct));
        app.MapGet($"/api/{moduleKey}/{{id:long}}", (long id, Database db, CancellationToken ct) => LoadModuleDetail(moduleKey, id, db, ct));
        app.MapPost($"/api/{moduleKey}", (HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) => CreateModuleRecord(http, moduleKey, body, db, audit, ct));
        app.MapPut($"/api/{moduleKey}/{{id:long}}", (HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) => UpdateModuleRecord(http, moduleKey, id, body, db, audit, ct));
    }

    private static readonly Dictionary<string, string[]> RolePermissionDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Super Admin"]              = ["*"],
        ["Company Admin"]            = ["*"],
        ["Fleet Manager"]            = ["dashboard:view","fleet:view","fleet:manage","maintenance:view","maintenance:manage","telematics:view","dispatch:view","intelligence:view","map:view"],
        ["Dispatcher"]               = ["dashboard:view","dispatch:view","dispatch:manage","fleet:view","jobs:view","jobs:manage","map:view","customers:view"],
        ["Driver"]                   = ["driver:portal","jobs:view","dvir:manage"],
        ["Mechanic"]                 = ["maintenance:view","maintenance:manage","dvir:review","fleet:view"],
        ["Safety Manager"]           = ["dashboard:view","safety:view","safety:manage","compliance:view","fleet:view","telematics:view","intelligence:view"],
        ["Compliance Manager"]       = ["dashboard:view","compliance:view","compliance:manage","audit:view","fleet:view","intelligence:view"],
        ["Customer Service"]         = ["customers:view","customer-portal:view","dispatch:view","crm:view"],
        ["Customer Portal User"]     = ["customer-portal:view"],
        ["Reseller / Partner Admin"] = ["*"],
        ["Read-only Auditor"]        = ["audit:view","fleet:view","dashboard:view"],
    };

    public static async Task<string[]> ResolvePermissionsAsync(Dictionary<string, object?> user, Database db, CancellationToken ct)
    {
        var role = user["roleName"]?.ToString() ?? "Company Admin";
        var roleId = user.TryGetValue("roleId", out var roleIdValue) && roleIdValue is not null and not DBNull
            ? Convert.ToInt64(roleIdValue)
            : (long?)null;
        var permsRaw = user["permissionsJson"]?.ToString();
        if (!string.IsNullOrWhiteSpace(permsRaw))
        {
            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<string[]>(permsRaw);
                if (parsed is { Length: > 0 }) return parsed;
            }
            catch { /* fallback below */ }
        }

        if (roleId is not null)
        {
            var rolePerms = await db.QueryAsync(
                "SELECT permission_key FROM role_permissions WHERE role_id=@id",
                c => c.Parameters.AddWithValue("@id", roleId.Value), ct);
            if (rolePerms.Count > 0)
            {
                return rolePerms
                    .Select(static x => x["permissionKey"]?.ToString())
                    .Where(static x => !string.IsNullOrWhiteSpace(x))
                    .Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }

        return RolePermissionDefaults.GetValueOrDefault(role, ["dashboard:view"]);
    }

    public static bool HasPermission(IReadOnlyCollection<string> permissions, string requiredPermission)
    {
        if (permissions.Count == 0) return false;
        if (permissions.Any(static p => string.Equals(p, "*", StringComparison.OrdinalIgnoreCase))) return true;
        var set = permissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return PermissionAliases(requiredPermission).Any(set.Contains);
    }

    public static IResult? RequirePermission(HttpContext http, string permission)
    {
        if (!http.Items.TryGetValue(AuthPermissionsItemKey, out var raw) || raw is not string[] permissions)
        {
            return Results.Json(ApiResponse<object>.Fail("Unauthorized"), statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!HasPermission(permissions, permission))
        {
            return Results.Json(ApiResponse<object>.Fail("Forbidden", $"Missing permission: {permission}"), statusCode: StatusCodes.Status403Forbidden);
        }

        return null;
    }

    public static long GetCompanyId(HttpContext http)
    {
        if (!http.Items.TryGetValue(AuthCompanyIdItemKey, out var value) || value is null) return 1;
        return Convert.ToInt64(value);
    }

    private static IEnumerable<string> PermissionAliases(string permission)
    {
        var normalized = permission.ToLowerInvariant();
        yield return normalized;
        yield return normalized.Replace('.', ':');
        yield return normalized.Replace(':', '.');
        yield return normalized.Replace('-', ':');
        yield return normalized.Replace('_', '-');
        yield return normalized.Replace('-', '_');
    }

    private static async Task<IResult> Login(LoginRequest request, Database db, AuditService audit, CancellationToken ct)
    {
        var user = await db.QuerySingleAsync(
            @"SELECT u.id, u.full_name, u.email, u.role_name, u.role_id, u.permissions_json, u.password_hash, u.demo_password,
                     c.id company_id, c.name company_name, c.company_code
              FROM users u JOIN companies c ON c.id = u.company_id
              WHERE u.email=@email LIMIT 1",
            cmd =>
            {
                cmd.Parameters.AddWithValue("@email", request.Email);
            }, ct);
        if (user is null) return Results.Unauthorized();

        var passwordHash = user["passwordHash"]?.ToString();
        var passwordOk = VerifyPasswordHash(request.Password, passwordHash);
        var usedLegacyDemoPassword = false;

        // Temporary local/demo fallback only; keeps seeded demo users working while migrating away from plaintext storage.
        if (!passwordOk)
        {
            var legacy = user["demoPassword"]?.ToString();
            if (!string.IsNullOrWhiteSpace(legacy) && string.Equals(legacy, request.Password, StringComparison.Ordinal))
            {
                passwordOk = true;
                usedLegacyDemoPassword = true;
            }
        }

        if (!passwordOk) return Results.Unauthorized();

        await audit.LogAsync("user.login", "User", Convert.ToInt64(user["id"]), request.Email, ct);

        var role = user["roleName"]?.ToString() ?? "Company Admin";
        var permissions = await ResolvePermissionsAsync(user, db, ct);

        var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray());

        // Persist session so the token can be validated later if needed
        try
        {
            var userId = Convert.ToInt64(user["id"]);
            var companyId = Convert.ToInt64(user["companyId"]);
            await db.ExecuteAsync(
                @"INSERT INTO user_sessions (user_id, company_id, session_token, expires_at)
                  VALUES (@uid, @cid, @tok, DATE_ADD(NOW(), INTERVAL 8 HOUR))
                  ON DUPLICATE KEY UPDATE expires_at = DATE_ADD(NOW(), INTERVAL 8 HOUR)",
                c =>
                {
                    c.Parameters.AddWithValue("@uid", userId);
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@tok", token);
                }, ct);
        }
        catch { /* non-fatal — session table may not exist on first boot before migrations run */ }

        if (usedLegacyDemoPassword)
        {
            try
            {
                var newHash = HashPassword(request.Password);
                await db.ExecuteAsync(
                    "UPDATE users SET password_hash=@hash WHERE id=@id",
                    c =>
                    {
                        c.Parameters.AddWithValue("@hash", newHash);
                        c.Parameters.AddWithValue("@id", user["id"]);
                    }, ct);
            }
            catch { /* do not block login on demo hash upgrade failure */ }
        }

        return Results.Ok(ApiResponse<object>.Ok(new
        {
            token,
            user = new
            {
                id    = user["id"],
                email = user["email"],
                name  = user["fullName"],
            },
            role,
            company = new { name = user["companyName"], code = user["companyCode"] },
            permissions,
        }, "Login successful"));
    }

    private static async Task<IResult> CommandCenterSummary(Database db, CancellationToken ct)
    {
        var kpis = await db.QueryAsync("SELECT * FROM kpi_records ORDER BY id LIMIT 8", ct: ct);
        var actions = await db.QueryAsync("SELECT * FROM command_center_actions ORDER BY FIELD(priority,'Critical','High','Medium','Low'), id LIMIT 12", ct: ct);
        var timeline = await db.QueryAsync("SELECT * FROM operational_events ORDER BY event_time DESC LIMIT 12", ct: ct);
        var alerts = await db.QueryAsync("SELECT * FROM ai_insights ORDER BY FIELD(severity,'Critical','High','Warning','Info'), created_at DESC LIMIT 8", ct: ct);
        var fleet = await db.QuerySingleAsync("SELECT COUNT(*) total, SUM(status IN ('Available','Active','On Route')) ready, ROUND(AVG(readiness_score),1) readinessScore FROM vehicles", ct: ct);
        var dispatch = await db.QuerySingleAsync("SELECT COUNT(*) totalJobs, SUM(status IN ('Delayed','At Risk')) exceptions, SUM(status='Completed') completed FROM jobs", ct: ct);
        var map = await db.QueryAsync("SELECT id, vehicle_code, lat, lng, event_type, speed_mph FROM location_events ORDER BY event_time DESC LIMIT 12", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            operationalStatus = "Active command posture",
            generatedAt = DateTime.UtcNow,
            kpis,
            aiBrief = alerts.FirstOrDefault()?["body"] ?? "OpsTrax AI is monitoring fleet risk, cost leakage, ETA variance, safety signals, and compliance exposure.",
            priorityActions = actions,
            timeline,
            charts = new
            {
                weeklyJobs = new[] { 28, 35, 31, 44, 39, 52, 47 },
                riskMix = new[] { 6, 3, 4, 2 },
                costLeakage = new[] { 1200, 930, 1450, 710, 560, 880 }
            },
            fleetSnapshot = fleet,
            dispatchSnapshot = dispatch,
            mapPreview = map,
            alerts
        }));
    }

    private static async Task<IResult> ControlTowerSummary(Database db, CancellationToken ct)
    {
        var entities = await db.QueryAsync(
            @"SELECT v.id, v.id vehicleId, v.assigned_driver_id driverId, v.vehicle_code label, 'vehicle' entity_type, COALESCE(v.status, le.event_type) status,
                     le.lat, le.lng, le.speed_mph speedMph, le.heading, le.event_type eventType, le.event_time eventTime, v.type vehicleType, v.device_status deviceStatus, v.camera_status cameraStatus,
                     v.readiness_score readinessScore, v.data_quality_score dataQualityScore, v.risk_score riskScore, d.full_name driverName,
                     CASE WHEN le.speed_mph > 65 THEN 'Speeding watch'
                          WHEN v.device_status <> 'Online' THEN 'Device offline'
                          WHEN v.camera_status <> 'Online' THEN 'Camera offline'
                          WHEN v.risk_score >= 70 THEN 'Fleet risk'
                          ELSE 'Normal' END live_alert,
                     CASE WHEN v.risk_score >= 70 OR le.speed_mph > 65 THEN 'High' WHEN v.device_status <> 'Online' OR v.camera_status <> 'Online' THEN 'Medium' ELSE 'Low' END risk_level
              FROM vehicles v
              INNER JOIN (SELECT le1.* FROM location_events le1 INNER JOIN (SELECT vehicle_id, MAX(id) max_id FROM location_events GROUP BY vehicle_id) le2 ON le1.id=le2.max_id) le ON v.id=le.vehicle_id
              LEFT JOIN drivers d ON d.id=v.assigned_driver_id
              WHERE v.deleted_at IS NULL
              ORDER BY le.event_time DESC LIMIT 24", ct: ct);
        var geofences = await db.QueryAsync("SELECT * FROM geofences ORDER BY name", ct: ct);
        var events = await db.QueryAsync("SELECT * FROM operational_events ORDER BY event_time DESC LIMIT 20", ct: ct);
        var recommendations = await db.QueryAsync("SELECT * FROM ai_recommendations WHERE module_key='control-tower' ORDER BY score DESC LIMIT 6", ct: ct);
        var kpis = await db.QuerySingleAsync(
            @"SELECT (SELECT COUNT(*) FROM vehicles WHERE deleted_at IS NULL) tracked_entities,
                     SUM(v.device_status='Online') online_devices,
                     SUM(v.camera_status='Online') online_cameras,
                     SUM(v.status IN ('Available','Active','On Route','Idle')) active_units,
                     SUM(v.risk_score >= 70) high_risk_units,
                     (SELECT COUNT(*) FROM location_events le2 INNER JOIN (SELECT vehicle_id, MAX(id) max_id FROM location_events GROUP BY vehicle_id) latest ON le2.id=latest.max_id WHERE le2.speed_mph > 65) speed_alerts,
                     ROUND(AVG(v.data_quality_score),1) telemetry_quality,
                     ROUND(AVG(v.readiness_score),1) fleet_readiness
              FROM vehicles v WHERE v.deleted_at IS NULL", ct: ct);
        var jobs = await db.QueryAsync(
            @"SELECT j.id, COALESCE(j.job_number,j.job_code) job_number, j.status, j.priority, j.sla_status, j.eta,
                     c.name customer_name, v.vehicle_code, d.full_name driver_name,
                     CASE WHEN j.sla_status='At Risk' OR j.status IN ('Delayed','At Risk') THEN 'Send ETA and dispatch review'
                          WHEN j.assigned_vehicle_id IS NULL THEN 'Assign vehicle'
                          ELSE 'Monitor SLA' END recommended_action
              FROM jobs j
              LEFT JOIN customers c ON c.id=j.customer_id
              LEFT JOIN vehicles v ON v.id=j.assigned_vehicle_id
              LEFT JOIN drivers d ON d.id=j.assigned_driver_id
              WHERE j.deleted_at IS NULL
              ORDER BY j.risk_score DESC, j.scheduled_start LIMIT 10", ct: ct);
        var diagnostics = await db.QueryAsync(
            @"SELECT v.id, v.vehicle_code, v.device_status, v.camera_status, v.readiness_score, v.data_quality_score, v.risk_score,
                     CASE WHEN v.device_status <> 'Online' THEN 'Recover gateway connection'
                          WHEN v.camera_status <> 'Online' THEN 'Verify camera health'
                          WHEN v.risk_score >= 70 THEN 'Create maintenance/safety review'
                          ELSE 'Healthy telemetry' END recommended_action
              FROM vehicles v WHERE v.deleted_at IS NULL ORDER BY v.risk_score DESC, v.data_quality_score LIMIT 10", ct: ct);
        var safetyVideo = await db.QueryAsync(
            @"SELECT de.id, de.event_number, de.event_type, de.severity, de.review_status, de.evidence_status, de.thumbnail_url,
                     d.full_name driver_name, v.vehicle_code, de.ai_summary
              FROM dashcam_events de
              LEFT JOIN drivers d ON d.id=de.driver_id
              LEFT JOIN vehicles v ON v.id=de.vehicle_id
              WHERE de.deleted_at IS NULL
              ORDER BY de.occurred_at DESC LIMIT 6", ct: ct);
        var actionQueue = await db.QueryAsync(
            @"SELECT id, title, severity priority, event_type module_key, event_time created_at FROM operational_events
              UNION ALL
              SELECT id, CONCAT('SLA watch: ', COALESCE(job_number,job_code)), priority, 'dispatch', created_at FROM jobs WHERE deleted_at IS NULL AND (sla_status='At Risk' OR status IN ('Delayed','At Risk'))
              ORDER BY created_at DESC LIMIT 12", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            status = "Live Simulation",
            generatedAt = DateTime.UtcNow,
            kpis,
            entities,
            geofences,
            events,
            recommendations,
            jobs,
            diagnostics,
            safetyVideo,
            actionQueue,
            replay = new
            {
                window = "Last 4 hours",
                available = true,
                description = "Trip replay placeholder with GPS trail, speed, route, geofence and video event overlays."
            },
            competitorGapAnalysis = new[]
            {
                new { capability = "Live map and asset context", status = "Matched and extended", opstraxAdvantage = "Vehicle, driver, job, SLA, telemetry quality and camera health appear in the same command surface." },
                new { capability = "Geofences and alerts", status = "Matched", opstraxAdvantage = "Risk zones, event feed and action queue are joined to dispatch/ETA workflows." },
                new { capability = "Video safety context", status = "Extended", opstraxAdvantage = "Dashcam evidence, exoneration and evidence package workflows are surfaced from Control Tower." },
                new { capability = "Rules/diagnostics", status = "Extended placeholder", opstraxAdvantage = "Device/camera/data-quality health and maintenance/safety actions are visible without leaving operations." },
                new { capability = "Customer SLA operations", status = "Differentiated", opstraxAdvantage = "Live map is tied directly to ETA updates, SLA risk and customer communication queues." }
            },
            filters = new[] { "Vehicles", "Jobs", "Geofences", "Incidents", "Maintenance Risk", "Delayed" }
        }));
    }

    private static async Task<IResult> ControlTowerEntities(Database db, CancellationToken ct)
    {
        var rows = await db.QueryAsync(
            @"SELECT v.id, v.id vehicleId, v.vehicle_code label, 'vehicle' entity_type, le.lat, le.lng, le.speed_mph speedMph, le.event_type eventType, v.status, d.full_name driverName
              FROM vehicles v
              INNER JOIN (
                SELECT le1.* FROM location_events le1
                INNER JOIN (SELECT vehicle_id, MAX(id) max_id FROM location_events GROUP BY vehicle_id) le2 ON le1.id=le2.max_id
              ) le ON v.id=le.vehicle_id
              LEFT JOIN drivers d ON d.id=v.assigned_driver_id
              WHERE v.deleted_at IS NULL
              ORDER BY le.event_time DESC", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(rows));
    }

    private static Task<IResult> ControlTowerEntity(string entityType, long id, Database db, CancellationToken ct)
        => entityType.Equals("driver", StringComparison.OrdinalIgnoreCase) ? EntityById(db, "drivers", id, ct) : ControlTowerVehicleDetail(id, db, ct);

    private static async Task<IResult> ControlTowerVehicleDetail(long id, Database db, CancellationToken ct)
    {
        var record = await db.QuerySingleAsync(
            @"SELECT v.*, d.full_name driver_name, le.lat, le.lng, le.speed_mph, le.heading, le.event_time last_seen_at
              FROM vehicles v
              LEFT JOIN drivers d ON d.id=v.assigned_driver_id
              LEFT JOIN location_events le ON le.vehicle_id=v.id
              WHERE v.id=@id
              ORDER BY le.event_time DESC LIMIT 1", c => c.Parameters.AddWithValue("@id", id), ct);
        if (record is null) return Results.NotFound(ApiResponse<object>.Fail("Vehicle not found"));
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            record,
            activeJobs = await db.QueryAsync("SELECT id, COALESCE(job_number,job_code) job_number, status, sla_status, eta, priority FROM jobs WHERE assigned_vehicle_id=@id AND deleted_at IS NULL ORDER BY scheduled_start DESC LIMIT 6", c => c.Parameters.AddWithValue("@id", id), ct),
            safetyEvents = await db.QueryAsync("SELECT event_number, event_type, severity, review_status, occurred_at FROM safety_events WHERE vehicle_id=@id AND deleted_at IS NULL ORDER BY occurred_at DESC LIMIT 6", c => c.Parameters.AddWithValue("@id", id), ct),
            videoEvents = await db.QueryAsync("SELECT event_number, event_type, severity, review_status, evidence_status, thumbnail_url FROM dashcam_events WHERE vehicle_id=@id AND deleted_at IS NULL ORDER BY occurred_at DESC LIMIT 4", c => c.Parameters.AddWithValue("@id", id), ct),
            maintenance = await db.QueryAsync("SELECT service_type, status, priority, due_date, risk_score FROM maintenance_items WHERE vehicle_id=@id AND deleted_at IS NULL ORDER BY due_date LIMIT 5", c => c.Parameters.AddWithValue("@id", id), ct),
            replayTrail = await db.QueryAsync("SELECT lat, lng, speed_mph, heading, event_type, event_time FROM location_events WHERE vehicle_id=@id ORDER BY event_time DESC LIMIT 20", c => c.Parameters.AddWithValue("@id", id), ct)
        }));
    }

    private static Task<IResult> Vehicles(Database db, CancellationToken ct)
        => OkRows(db,
            @"SELECT v.*, d.full_name assigned_driver,
                     ROUND((v.readiness_score + v.data_quality_score + (100 - v.risk_score)) / 3, 1) fleet_readiness_score,
                     CASE WHEN v.risk_score >= 70 OR v.status IN ('Delayed','Maintenance') THEN 'High'
                          WHEN v.risk_score >= 40 OR v.camera_status <> 'Online' OR v.device_status <> 'Online' THEN 'Medium'
                          ELSE 'Low' END risk_heat_score,
                     CASE WHEN v.status='Maintenance' THEN 'Create maintenance review'
                          WHEN v.assigned_driver_id IS NULL THEN 'Assign best available driver'
                          WHEN v.camera_status <> 'Online' THEN 'Review camera health'
                          ELSE 'Keep in active rotation' END recommended_action
              FROM vehicles v
              LEFT JOIN drivers d ON d.id=v.assigned_driver_id
              WHERE v.deleted_at IS NULL
              ORDER BY v.vehicle_code", ct: ct);

    private static Task<IResult> Drivers(Database db, CancellationToken ct)
        => OkRows(db,
            @"SELECT d.*, v.vehicle_code assigned_vehicle,
                     ROUND((d.readiness_score + d.safety_score + d.compliance_score + (100 - d.risk_score)) / 4, 1) driver_readiness_score,
                     CASE WHEN d.risk_score >= 70 OR d.status='Delayed' THEN 'High'
                          WHEN d.risk_score >= 40 OR d.compliance_score < 85 THEN 'Medium'
                          ELSE 'Low' END risk_heat_score,
                     CASE WHEN d.assigned_vehicle_id IS NULL THEN 'Assign best-fit available vehicle'
                          WHEN d.compliance_score < 85 THEN 'Review certifications'
                          WHEN d.safety_score < 88 THEN 'Queue coaching review'
                          ELSE 'Ready for dispatch' END recommended_action
              FROM drivers d
              LEFT JOIN vehicles v ON v.id=d.assigned_vehicle_id
              WHERE d.deleted_at IS NULL
              ORDER BY d.full_name", ct: ct);

    private static Task<IResult> Customers(Database db, CancellationToken ct)
        => OkRows(db,
            @"SELECT c.*,
                     COUNT(j.id) active_jobs,
                     ROUND((c.sla_health_score + c.delivery_experience_score + (100 - c.risk_score)) / 3, 1) customer_delivery_experience_score,
                     CASE WHEN c.risk_score >= 65 OR c.status='At Risk' THEN 'High'
                          WHEN c.risk_score >= 35 OR c.sla_health_score < 88 THEN 'Medium'
                          ELSE 'Low' END risk_heat_score,
                     CASE WHEN c.status='At Risk' OR c.sla_health_score < 88 THEN 'Send proactive customer update'
                          WHEN COUNT(j.id) > 4 THEN 'Review active workload'
                          ELSE 'Maintain SLA cadence' END recommended_action
              FROM customers c
              LEFT JOIN jobs j ON j.customer_id=c.id AND j.status NOT IN ('Completed','Delivered')
              WHERE c.deleted_at IS NULL
              GROUP BY c.id
              ORDER BY c.name", ct: ct);

    private static Task<IResult> Assets(Database db, CancellationToken ct)
        => OkRows(db,
            @"SELECT a.*, v.vehicle_code assigned_vehicle, d.full_name assigned_driver, c.name customer_name,
                     CASE WHEN a.risk_score >= 70 OR a.geofence_status LIKE 'Outside%' THEN 'High'
                          WHEN a.status='Maintenance' OR a.risk_score >= 40 THEN 'Medium'
                          ELSE 'Low' END geofence_risk_badge,
                     CASE WHEN a.geofence_status LIKE 'Outside%' THEN 'Lost asset / unauthorized movement watch'
                          WHEN a.assigned_vehicle_id IS NULL THEN 'Assign to an active route or yard zone'
                          WHEN a.status='Maintenance' THEN 'Create maintenance review'
                          ELSE 'Utilization in normal band' END recommended_action
              FROM assets a
              LEFT JOIN vehicles v ON v.id=a.assigned_vehicle_id
              LEFT JOIN drivers d ON d.id=a.assigned_driver_id
              LEFT JOIN customers c ON c.id=a.customer_id
              WHERE a.deleted_at IS NULL
              ORDER BY a.asset_code", ct: ct);

    private static async Task<IResult> VehicleSummary(Database db, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT COUNT(*) total,
                     SUM(status IN ('Active','Available','On Route','At Stop','Idle')) active,
                     SUM(status IN ('Delayed','Maintenance') OR risk_score >= 55) at_risk,
                     ROUND(AVG(readiness_score),1) fleet_readiness_score,
                     ROUND(AVG(data_quality_score),1) data_completeness_score,
                     ROUND(AVG(risk_score),1) average_risk_score,
                     SUM(device_status <> 'Online' OR camera_status <> 'Online') device_exceptions
              FROM vehicles WHERE deleted_at IS NULL", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(row ?? new Dictionary<string, object?>()));
    }

    private static async Task<IResult> VehiclePlanningInsights(Database db, CancellationToken ct)
    {
        var replacementForecast = await db.QueryAsync(
            @"SELECT v.id, v.vehicle_code, v.type, v.make, v.model, v.year, v.odometer_miles, v.status,
                     v.readiness_score, v.data_quality_score, v.risk_score, v.device_status, v.camera_status,
                     GREATEST(0, YEAR(CURDATE()) - COALESCE(v.year, YEAR(CURDATE()))) age_years,
                     CASE
                       WHEN COALESCE(v.year, YEAR(CURDATE())) <= YEAR(CURDATE()) - 8 OR v.odometer_miles >= 250000 THEN 'Retire / replace now'
                       WHEN COALESCE(v.year, YEAR(CURDATE())) <= YEAR(CURDATE()) - 6 OR v.odometer_miles >= 180000 THEN 'Budget replacement'
                       WHEN COALESCE(v.year, YEAR(CURDATE())) <= YEAR(CURDATE()) - 4 OR v.odometer_miles >= 120000 THEN 'Monitor lifecycle'
                       ELSE 'Healthy lifecycle'
                     END lifecycle_status,
                     CASE
                       WHEN COALESCE(v.year, YEAR(CURDATE())) <= YEAR(CURDATE()) - 8 OR v.odometer_miles >= 250000 THEN '0-6 months'
                       WHEN COALESCE(v.year, YEAR(CURDATE())) <= YEAR(CURDATE()) - 6 OR v.odometer_miles >= 180000 THEN '6-18 months'
                       WHEN COALESCE(v.year, YEAR(CURDATE())) <= YEAR(CURDATE()) - 4 OR v.odometer_miles >= 120000 THEN '18-36 months'
                       ELSE '36+ months'
                     END replacement_window,
                     ROUND((GREATEST(0, YEAR(CURDATE()) - COALESCE(v.year, YEAR(CURDATE()))) * 8) + (v.odometer_miles / 3500) + v.risk_score + IF(v.status='Maintenance',18,0), 1) capex_priority_score,
                     CASE
                       WHEN COALESCE(v.year, YEAR(CURDATE())) <= YEAR(CURDATE()) - 8 OR v.odometer_miles >= 250000 THEN 'Start replacement sourcing and avoid long-haul assignment'
                       WHEN COALESCE(v.year, YEAR(CURDATE())) <= YEAR(CURDATE()) - 6 OR v.odometer_miles >= 180000 THEN 'Add to capital plan and reserve newer unit for SLA-sensitive lanes'
                       WHEN v.status='Maintenance' THEN 'Resolve maintenance before assigning to priority routes'
                       ELSE 'Keep in active rotation'
                     END recommended_action
              FROM vehicles v
              WHERE v.deleted_at IS NULL
              ORDER BY capex_priority_score DESC, v.odometer_miles DESC
              LIMIT 12", ct: ct);

        var customerBusiness = await db.QueryAsync(
            @"SELECT c.id, c.customer_code, c.name customer_name, c.sla_tier, c.sla_health_score,
                     COUNT(j.id) job_count,
                     CONCAT('$', FORMAT(COALESCE(SUM(j.revenue_estimate),0),0)) revenue_estimate,
                     CONCAT('$', FORMAT(COALESCE(SUM(j.margin_estimate),0),0)) margin_estimate,
                     ROUND(COALESCE(AVG(j.risk_score),0),1) avg_job_risk,
                     MAX(j.scheduled_start) last_scheduled_work,
                     CASE
                       WHEN COUNT(j.id) >= 8 AND COALESCE(SUM(j.margin_estimate),0) > 2500 THEN 'Protect and expand'
                       WHEN COUNT(j.id) >= 5 THEN 'Capacity planning account'
                       WHEN COALESCE(AVG(j.risk_score),0) >= 55 THEN 'Service recovery watch'
                       ELSE 'Standard cadence'
                     END planning_signal
              FROM customers c
              LEFT JOIN jobs j ON j.customer_id=c.id AND j.deleted_at IS NULL
              WHERE c.deleted_at IS NULL
              GROUP BY c.id
              ORDER BY COALESCE(SUM(j.revenue_estimate),0) DESC, COUNT(j.id) DESC
              LIMIT 10", ct: ct);

        var routeBusiness = await db.QueryAsync(
            @"SELECT r.id, COALESCE(r.route_code, CONCAT('ROUTE-', r.id)) route_code, COALESCE(r.route_name, r.name) route_name, r.region,
                     r.status, r.efficiency_score, r.sla_risk,
                     COUNT(j.id) job_count,
                     CONCAT('$', FORMAT(COALESCE(SUM(j.revenue_estimate),0),0)) revenue_estimate,
                     CONCAT('$', FORMAT(COALESCE(SUM(j.margin_estimate),0),0)) margin_estimate,
                     ROUND(COALESCE(AVG(j.risk_score),0),1) avg_job_risk,
                     CASE
                       WHEN COUNT(j.id) >= 6 AND r.efficiency_score >= 86 THEN 'Scale route capacity'
                       WHEN COUNT(j.id) >= 4 AND r.sla_risk='High' THEN 'Add buffer or split route'
                       WHEN r.efficiency_score < 82 THEN 'Optimize before expanding'
                       ELSE 'Maintain plan'
                     END planning_signal
              FROM routes r
              LEFT JOIN jobs j ON j.route_id=r.id AND j.deleted_at IS NULL
              WHERE r.deleted_at IS NULL
              GROUP BY r.id
              ORDER BY COALESCE(SUM(j.revenue_estimate),0) DESC, COUNT(j.id) DESC, r.efficiency_score DESC
              LIMIT 10", ct: ct);

        var operationalGaps = await db.QueryAsync(
            @"SELECT 'Downtime / unavailable dispatch' gap_name, COUNT(*) affected_records,
                     'Vehicles delayed, in maintenance, or carrying high risk before dispatch.' visibility
              FROM vehicles WHERE deleted_at IS NULL AND (status IN ('Delayed','Maintenance') OR risk_score >= 55)
              UNION ALL
              SELECT 'Device or camera blind spot', COUNT(*), 'Units where telematics or camera status is not online.'
              FROM vehicles WHERE deleted_at IS NULL AND (device_status <> 'Online' OR camera_status <> 'Online')
              UNION ALL
              SELECT 'Expiring vehicle documents', COUNT(*), 'Registration, inspection, insurance or other vehicle documents needing renewal.'
              FROM vehicle_documents WHERE status IN ('Expiring Soon','Expired','Review') OR expiry_date <= DATE_ADD(CURDATE(), INTERVAL 30 DAY)
              UNION ALL
              SELECT 'Cost leakage by vehicle', COUNT(*), 'Vehicles with fuel, maintenance, or margin pressure signals.'
              FROM vehicles v
              WHERE v.deleted_at IS NULL AND (
                v.risk_score >= 55 OR
                EXISTS (SELECT 1 FROM fuel_transactions ft WHERE ft.vehicle_id=v.id AND COALESCE(ft.total_cost,0) > 300)
              )", ct: ct);

        return Results.Ok(ApiResponse<object>.Ok(new
        {
            replacementForecast,
            customerBusiness,
            routeBusiness,
            operationalGaps
        }));
    }

    private static async Task<IResult> DriverSummary(Database db, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT COUNT(*) total,
                     SUM(status IN ('Available','On Route','At Stop','Idle')) active,
                     SUM(status IN ('Delayed','Suspended') OR risk_score >= 55) at_risk,
                     ROUND(AVG(readiness_score),1) driver_readiness_score,
                     ROUND(AVG(compliance_score),1) data_completeness_score,
                     ROUND(AVG(safety_score),1) safety_score,
                     SUM(compliance_score < 85) compliance_exceptions
              FROM drivers WHERE deleted_at IS NULL", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(row ?? new Dictionary<string, object?>()));
    }

    private static async Task<IResult> CustomerSummary(Database db, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT COUNT(*) total,
                     SUM(status='Active') active,
                     SUM(status='At Risk' OR risk_score >= 50 OR sla_health_score < 88) at_risk,
                     ROUND(AVG(sla_health_score),1) sla_health_score,
                     ROUND(AVG(delivery_experience_score),1) delivery_experience_score,
                     SUM(sla_tier='Platinum') platinum_accounts
              FROM customers WHERE deleted_at IS NULL", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(row ?? new Dictionary<string, object?>()));
    }

    private static async Task<IResult> AssetSummary(Database db, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT COUNT(*) total,
                     SUM(status IN ('Available','Assigned')) active,
                     SUM(status='Maintenance' OR risk_score >= 55 OR geofence_status LIKE 'Outside%') at_risk,
                     ROUND(AVG(utilization_score),1) utilization_score,
                     SUM(geofence_status LIKE 'Outside%') geofence_exceptions,
                     SUM(assigned_vehicle_id IS NULL) unassigned
              FROM assets WHERE deleted_at IS NULL", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(row ?? new Dictionary<string, object?>()));
    }

    private static async Task<IResult> VehicleDetail(long id, Database db, CancellationToken ct)
    {
        var record = await db.QuerySingleAsync(
            @"SELECT v.*, d.full_name assigned_driver,
                     ROUND((v.readiness_score + v.data_quality_score + (100 - v.risk_score)) / 3, 1) fleet_readiness_score
              FROM vehicles v LEFT JOIN drivers d ON d.id=v.assigned_driver_id WHERE v.id=@id AND v.deleted_at IS NULL",
            c => c.Parameters.AddWithValue("@id", id), ct);
        if (record is null) return Results.NotFound(ApiResponse<object>.Fail("Vehicle not found"));
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            record,
            timeline = await EntityTimeline(db, "Vehicle", id, ct),
            recommendations = await ModuleRecommendations(db, "vehicles", ct),
            documents = await db.QueryAsync("SELECT * FROM vehicle_documents WHERE vehicle_id=@id ORDER BY expiry_date", c => c.Parameters.AddWithValue("@id", id), ct),
            maintenance = await db.QueryAsync("SELECT * FROM maintenance_items WHERE vehicle_id=@id ORDER BY due_date LIMIT 8", c => c.Parameters.AddWithValue("@id", id), ct),
            compliance = await db.QueryAsync("SELECT * FROM compliance_documents WHERE related_entity_type='Vehicle' AND related_entity_id=@id ORDER BY expiry_date LIMIT 8", c => c.Parameters.AddWithValue("@id", id), ct),
            safetyEvents = await db.QueryAsync("SELECT * FROM safety_events WHERE vehicle_id=@id ORDER BY event_time DESC LIMIT 8", c => c.Parameters.AddWithValue("@id", id), ct),
            trips = await db.QueryAsync("SELECT * FROM trips WHERE vehicle_id=@id ORDER BY started_at DESC LIMIT 8", c => c.Parameters.AddWithValue("@id", id), ct),
            costSummary = await db.QuerySingleAsync("SELECT COALESCE(SUM(total_cost),0) fuel_cost, COALESCE(SUM(idle_minutes),0) idle_minutes FROM fuel_transactions WHERE vehicle_id=@id", c => c.Parameters.AddWithValue("@id", id), ct),
            auditTrail = await AuditTrail(db, "Vehicle", id, ct)
        }));
    }

    private static async Task<IResult> DriverDetail(long id, Database db, CancellationToken ct)
    {
        var record = await db.QuerySingleAsync(
            @"SELECT d.*, v.vehicle_code assigned_vehicle,
                     ROUND((d.readiness_score + d.safety_score + d.compliance_score + (100 - d.risk_score)) / 4, 1) driver_readiness_score
              FROM drivers d LEFT JOIN vehicles v ON v.id=d.assigned_vehicle_id WHERE d.id=@id AND d.deleted_at IS NULL",
            c => c.Parameters.AddWithValue("@id", id), ct);
        if (record is null) return Results.NotFound(ApiResponse<object>.Fail("Driver not found"));
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            record,
            timeline = await EntityTimeline(db, "Driver", id, ct),
            recommendations = await ModuleRecommendations(db, "drivers", ct),
            documents = await db.QueryAsync("SELECT * FROM driver_documents WHERE driver_id=@id ORDER BY expiry_date", c => c.Parameters.AddWithValue("@id", id), ct),
            certifications = await db.QueryAsync("SELECT * FROM driver_certifications WHERE driver_id=@id ORDER BY expiry_date", c => c.Parameters.AddWithValue("@id", id), ct),
            hos = await db.QueryAsync("SELECT * FROM hos_logs WHERE driver_id=@id ORDER BY log_date DESC LIMIT 8", c => c.Parameters.AddWithValue("@id", id), ct),
            inspections = await db.QueryAsync("SELECT * FROM inspections WHERE driver_id=@id ORDER BY created_at DESC LIMIT 8", c => c.Parameters.AddWithValue("@id", id), ct),
            safetyEvents = await db.QueryAsync("SELECT * FROM safety_events WHERE driver_id=@id ORDER BY event_time DESC LIMIT 8", c => c.Parameters.AddWithValue("@id", id), ct),
            auditTrail = await AuditTrail(db, "Driver", id, ct)
        }));
    }

    private static async Task<IResult> CustomerDetail(long id, Database db, CancellationToken ct)
    {
        var record = await db.QuerySingleAsync("SELECT * FROM customers WHERE id=@id AND deleted_at IS NULL", c => c.Parameters.AddWithValue("@id", id), ct);
        if (record is null) return Results.NotFound(ApiResponse<object>.Fail("Customer not found"));
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            record,
            timeline = await EntityTimeline(db, "Customer", id, ct),
            recommendations = await ModuleRecommendations(db, "customers", ct),
            contacts = await db.QueryAsync("SELECT * FROM customer_contacts WHERE customer_id=@id ORDER BY is_primary DESC, full_name", c => c.Parameters.AddWithValue("@id", id), ct),
            addresses = await db.QueryAsync("SELECT * FROM customer_addresses WHERE customer_id=@id ORDER BY address_type", c => c.Parameters.AddWithValue("@id", id), ct),
            activeJobs = await db.QueryAsync("SELECT * FROM jobs WHERE customer_id=@id AND status NOT IN ('Completed','Delivered') ORDER BY scheduled_start LIMIT 12", c => c.Parameters.AddWithValue("@id", id), ct),
            communications = await db.QueryAsync("SELECT * FROM customer_communications WHERE customer_id=@id ORDER BY sent_at DESC LIMIT 10", c => c.Parameters.AddWithValue("@id", id), ct),
            contracts = await db.QueryAsync("SELECT * FROM contracts WHERE customer_id=@id ORDER BY expiration_date", c => c.Parameters.AddWithValue("@id", id), ct),
            etaHistory = await db.QueryAsync("SELECT eu.* FROM eta_updates eu JOIN jobs j ON j.id=eu.job_id WHERE j.customer_id=@id ORDER BY eu.sent_at DESC LIMIT 10", c => c.Parameters.AddWithValue("@id", id), ct),
            auditTrail = await AuditTrail(db, "Customer", id, ct)
        }));
    }

    private static async Task<IResult> AssetDetail(long id, Database db, CancellationToken ct)
    {
        var record = await db.QuerySingleAsync(
            @"SELECT a.*, v.vehicle_code assigned_vehicle, d.full_name assigned_driver, c.name customer_name
              FROM assets a
              LEFT JOIN vehicles v ON v.id=a.assigned_vehicle_id
              LEFT JOIN drivers d ON d.id=a.assigned_driver_id
              LEFT JOIN customers c ON c.id=a.customer_id
              WHERE a.id=@id AND a.deleted_at IS NULL",
            c => c.Parameters.AddWithValue("@id", id), ct);
        if (record is null) return Results.NotFound(ApiResponse<object>.Fail("Asset not found"));
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            record,
            timeline = await EntityTimeline(db, "Asset", id, ct),
            recommendations = await ModuleRecommendations(db, "assets", ct),
            documents = await db.QueryAsync("SELECT * FROM asset_documents WHERE asset_id=@id ORDER BY expiry_date", c => c.Parameters.AddWithValue("@id", id), ct),
            movementHistory = await db.QueryAsync("SELECT * FROM entity_timeline_events WHERE entity_type='Asset' AND entity_id=@id ORDER BY created_at DESC LIMIT 8", c => c.Parameters.AddWithValue("@id", id), ct),
            auditTrail = await AuditTrail(db, "Asset", id, ct)
        }));
    }

    private static Task<IResult> Jobs(Database db, CancellationToken ct)
        => OkRows(db, @"SELECT j.*, v.vehicle_code, d.full_name driver_name, c.name customer_name
                             , COALESCE(j.job_number, j.job_code) job_number
                             , CONCAT(DATE_FORMAT(j.scheduled_start, '%b %d %H:%i'), ' - ', DATE_FORMAT(j.scheduled_end, '%H:%i')) time_window
                             , CASE WHEN j.risk_score >= 70 OR j.sla_status='At Risk' THEN 'High' WHEN j.risk_score >= 40 THEN 'Medium' ELSE 'Low' END risk_heat_score
                             , CASE WHEN j.assigned_driver_id IS NULL OR j.assigned_vehicle_id IS NULL THEN 'Assign driver and vehicle'
                                    WHEN j.sla_status='At Risk' THEN 'Send ETA and dispatch review'
                                    WHEN j.proof_status='Pending' AND j.status IN ('Completed','Delivered') THEN 'Mark proof pending'
                                    ELSE 'Monitor ETA confidence' END recommended_action
                        FROM jobs j
                        LEFT JOIN vehicles v ON v.id=j.assigned_vehicle_id
                        LEFT JOIN drivers d ON d.id=j.assigned_driver_id
                        LEFT JOIN customers c ON c.id=j.customer_id
                        WHERE j.deleted_at IS NULL
                        ORDER BY j.scheduled_start DESC", ct: ct);

    private static async Task<IResult> JobsSummary(Database db, CancellationToken ct)
    {
        var summary = await db.QuerySingleAsync(
            @"SELECT COUNT(*) total_jobs_today,
                     SUM(status='Unassigned') unassigned_jobs,
                     SUM(status='Assigned') assigned_jobs,
                     SUM(status='En Route' OR status='In Progress') en_route,
                     SUM(status='At Stop') at_stop,
                     SUM(status IN ('Completed','Delivered')) completed,
                     SUM(status IN ('Delayed','At Risk')) delayed,
                     SUM(sla_status='At Risk') sla_at_risk,
                     SUM(proof_status='Pending') proof_pending,
                     SUM(customer_update_status='Sent') customer_updates_sent,
                     '92%' average_eta_accuracy,
                     CONCAT('$', FORMAT(COALESCE(SUM(margin_estimate),0),0)) revenue_margin_placeholder,
                     COUNT(*) total,
                     SUM(status IN ('Assigned','En Route','In Progress','At Stop')) active,
                     SUM(risk_score >= 60 OR sla_status='At Risk') at_risk
              FROM jobs WHERE deleted_at IS NULL", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(summary ?? new Dictionary<string, object?>()));
    }

    private static async Task<IResult> JobDetail(long id, Database db, CancellationToken ct)
    {
        var record = await db.QuerySingleAsync(
            @"SELECT j.*, COALESCE(j.job_number,j.job_code) job_number, c.name customer_name, c.sla_tier, v.vehicle_code, d.full_name driver_name, r.route_code,
                     CASE WHEN j.risk_score >= 70 OR j.sla_status='At Risk' THEN 'High' WHEN j.risk_score >= 40 THEN 'Medium' ELSE 'Low' END risk_heat_score
              FROM jobs j
              LEFT JOIN customers c ON c.id=j.customer_id
              LEFT JOIN vehicles v ON v.id=j.assigned_vehicle_id
              LEFT JOIN drivers d ON d.id=j.assigned_driver_id
              LEFT JOIN routes r ON r.id=j.route_id
              WHERE j.id=@id AND j.deleted_at IS NULL", c => c.Parameters.AddWithValue("@id", id), ct);
        if (record is null) return Results.NotFound(ApiResponse<object>.Fail("Job not found"));
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            record,
            timeline = await EntityTimeline(db, "Job", id, ct),
            recommendations = await ModuleRecommendations(db, "jobs", ct),
            assignment = await db.QuerySingleAsync(@"SELECT da.*, d.full_name driver_name, v.vehicle_code FROM dispatch_assignments da LEFT JOIN drivers d ON d.id=da.driver_id LEFT JOIN vehicles v ON v.id=da.vehicle_id WHERE da.job_id=@id ORDER BY da.assigned_at DESC LIMIT 1", c => c.Parameters.AddWithValue("@id", id), ct),
            stops = await db.QueryAsync("SELECT * FROM route_stops WHERE job_id=@id ORDER BY stop_sequence", c => c.Parameters.AddWithValue("@id", id), ct),
            communications = await db.QueryAsync("SELECT * FROM customer_communications WHERE job_id=@id ORDER BY sent_at DESC LIMIT 10", c => c.Parameters.AddWithValue("@id", id), ct),
            etaUpdates = await db.QueryAsync("SELECT * FROM eta_updates WHERE job_id=@id ORDER BY sent_at DESC LIMIT 10", c => c.Parameters.AddWithValue("@id", id), ct),
            proof = await db.QueryAsync("SELECT * FROM proof_of_delivery WHERE job_id=@id ORDER BY captured_at DESC LIMIT 5", c => c.Parameters.AddWithValue("@id", id), ct),
            costs = await db.QuerySingleAsync("SELECT revenue_estimate, cost_estimate, margin_estimate, CASE WHEN margin_estimate < 150 THEN 'High' ELSE 'Low' END margin_risk FROM jobs WHERE id=@id", c => c.Parameters.AddWithValue("@id", id), ct),
            auditTrail = await AuditTrail(db, "Job", id, ct)
        }));
    }

    private static async Task<IResult> DispatchBoard(Database db, CancellationToken ct)
    {
        var jobs = await db.QueryAsync(@"SELECT j.*, COALESCE(j.job_number,j.job_code) job_number, c.name customer_name, v.vehicle_code, d.full_name driver_name,
                                                CASE WHEN j.risk_score >= 70 OR j.sla_status='At Risk' THEN 'High' WHEN j.risk_score >= 40 THEN 'Medium' ELSE 'Low' END risk_heat_score,
                                                CASE WHEN j.customer_update_status='Sent' THEN 'Updated' ELSE 'Needs update' END customer_update_label
                                         FROM jobs j
                                         LEFT JOIN customers c ON c.id=j.customer_id
                                         LEFT JOIN vehicles v ON v.id=j.assigned_vehicle_id
                                         LEFT JOIN drivers d ON d.id=j.assigned_driver_id
                                         WHERE j.deleted_at IS NULL
                                         ORDER BY j.scheduled_start", ct: ct);
        var groups = new[] { "Unassigned", "Assigned", "En Route", "At Stop", "Completed", "Delayed / Exception" }
            .ToDictionary(x => x, x => jobs.Where(j => StageFor(j["status"]?.ToString()) == x).ToList());
        return Results.Ok(ApiResponse<object>.Ok(groups));
    }

    private static async Task<IResult> DispatchSummary(Database db, CancellationToken ct)
    {
        var summary = await db.QuerySingleAsync(
            @"SELECT COUNT(*) total, SUM(status='Unassigned') unassigned, SUM(status='Assigned') assigned,
                     SUM(status IN ('En Route','In Progress')) en_route, SUM(status='At Stop') at_stop,
                     SUM(status IN ('Delayed','At Risk')) exceptions, SUM(status IN ('Completed','Delivered')) completed,
                     ROUND(AVG(100 - LEAST(risk_score, 95)),1) dispatch_readiness_score,
                     SUM(sla_status='At Risk') sla_watch, SUM(customer_update_status <> 'Sent') eta_action_queue
              FROM jobs WHERE deleted_at IS NULL", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(summary ?? new Dictionary<string, object?>()));
    }

    private static async Task<IResult> GenericModule(string moduleKey, Database db, CancellationToken ct)
    {
        return await LoadModule(moduleKey, db, ct);
    }

    private static async Task<IResult> GenericModuleDetail(string moduleKey, long id, Database db, CancellationToken ct)
    {
        return await LoadModuleDetail(moduleKey, id, db, ct);
    }

    private static async Task<IResult> LoadModule(string moduleKey, Database db, CancellationToken ct)
    {
        var definition = ModuleDefinitions.GetValueOrDefault(moduleKey) ?? ModuleDefinitions["fallback"];
        var summary = await db.QuerySingleAsync(definition.SummarySql, BindModule(moduleKey, definition), ct);
        var rows = await db.QueryAsync(definition.ListSql, BindModule(moduleKey, definition), ct);
        var insights = await db.QueryAsync("SELECT * FROM ai_recommendations WHERE module_key=@key ORDER BY score DESC LIMIT 4", c => c.Parameters.AddWithValue("@key", moduleKey), ct);
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            moduleKey,
            sourceTable = definition.TableName,
            summary,
            records = rows,
            insights
        }));
    }

    private static async Task<IResult> LoadModuleDetail(string moduleKey, long id, Database db, CancellationToken ct)
    {
        var definition = ModuleDefinitions.GetValueOrDefault(moduleKey) ?? ModuleDefinitions["fallback"];
        var row = await db.QuerySingleAsync(definition.DetailSql, c =>
        {
            c.Parameters.AddWithValue("@id", id);
            c.Parameters.AddWithValue("@key", moduleKey);
        }, ct);
        return row is null ? Results.NotFound(ApiResponse<object>.Fail("Record not found")) : Results.Ok(ApiResponse<object>.Ok(row));
    }

    private static async Task<IResult> CreateModuleRecord(HttpContext http, string moduleKey, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        if (ModuleWritePermissionByKey.TryGetValue(moduleKey, out var permission))
        {
            var denied = RequirePermission(http, permission);
            if (denied is not null) return denied;
        }

        var definition = ModuleDefinitions.GetValueOrDefault(moduleKey);
        if (definition?.CreateSql is null)
        {
            return await CreateGenericModuleRecord(moduleKey, body, db, audit, ct);
        }

        var companyId = GetCompanyId(http);
        var id = await db.InsertAsync(definition.CreateSql, c => BindModuleRecord(c, moduleKey, body, companyId), ct);
        await audit.LogAsync($"{moduleKey}.created", moduleKey, id, ct: ct);
        return Results.Created($"/api/{moduleKey}/{id}", ApiResponse<object>.Ok(new { id }, "Record created"));
    }

    private static async Task<IResult> UpdateModuleRecord(HttpContext http, string moduleKey, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        if (ModuleWritePermissionByKey.TryGetValue(moduleKey, out var permission))
        {
            var denied = RequirePermission(http, permission);
            if (denied is not null) return denied;
        }

        var definition = ModuleDefinitions.GetValueOrDefault(moduleKey);
        if (definition?.UpdateSql is null)
        {
            return await UpdateGenericModuleRecord(moduleKey, id, body, db, audit, ct);
        }

        await db.ExecuteAsync(definition.UpdateSql, c =>
        {
            c.Parameters.AddWithValue("@id", id);
            BindModuleRecord(c, moduleKey, body, GetCompanyId(http));
        }, ct);
        await audit.LogAsync($"{moduleKey}.updated", moduleKey, id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Record updated"));
    }

    private static async Task<IResult> CreateGenericModuleRecord(string moduleKey, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var id = await db.InsertAsync(
            @"INSERT INTO module_records (module_key, title, status, owner_name, location_name, due_at, risk_level, amount, metadata_json)
              VALUES (@key, @title, @status, @owner, @location, @dueAt, @risk, @amount, @metadata)",
            c =>
            {
                c.Parameters.AddWithValue("@key", moduleKey);
                c.Parameters.AddWithValue("@title", Get(body, "title") ?? "New record");
                c.Parameters.AddWithValue("@status", Get(body, "status") ?? "Open");
                c.Parameters.AddWithValue("@owner", Get(body, "ownerName"));
                c.Parameters.AddWithValue("@location", Get(body, "locationName"));
                c.Parameters.AddWithValue("@dueAt", DBNull.Value);
                c.Parameters.AddWithValue("@risk", Get(body, "riskLevel") ?? "Medium");
                c.Parameters.AddWithValue("@amount", Get(body, "amount") ?? 0);
                c.Parameters.AddWithValue("@metadata", "{}");
            }, ct);
        await audit.LogAsync("module.record.created", moduleKey, id, ct: ct);
        return Results.Created($"/api/modules/{moduleKey}/{id}", ApiResponse<object>.Ok(new { id }));
    }

    private static async Task<IResult> UpdateGenericModuleRecord(string moduleKey, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync("UPDATE module_records SET title=COALESCE(@title,title), status=COALESCE(@status,status), risk_level=COALESCE(@risk,risk_level) WHERE module_key=@key AND id=@id", c =>
        {
            c.Parameters.AddWithValue("@key", moduleKey);
            c.Parameters.AddWithValue("@id", id);
            c.Parameters.AddWithValue("@title", Get(body, "title"));
            c.Parameters.AddWithValue("@status", Get(body, "status"));
            c.Parameters.AddWithValue("@risk", Get(body, "riskLevel"));
        }, ct);
        await audit.LogAsync("module.record.updated", moduleKey, id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }));
    }

    private static async Task<IResult> CreateVehicle(HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "fleet:manage");
        if (denied is not null) return denied;
        var companyId = GetCompanyId(http);
        var id = await db.InsertAsync(@"INSERT INTO vehicles (company_id, vehicle_code, type, make, model, year, vin, plate_number, status, readiness_score, data_quality_score)
            VALUES (@companyId, @code, @type, @make, @model, @year, @vin, @plate, @status, 92, 96)", c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                BindVehicle(c, body);
            }, ct);
        await audit.LogAsync("vehicle.created", "Vehicle", id, ct: ct);
        return Results.Created($"/api/vehicles/{id}", ApiResponse<object>.Ok(new { id }));
    }

    private static async Task<IResult> UpdateVehicle(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "fleet:manage");
        if (denied is not null) return denied;
        await db.ExecuteAsync(@"UPDATE vehicles SET vehicle_code=COALESCE(@code,vehicle_code), type=COALESCE(@type,type), make=COALESCE(@make,make),
            model=COALESCE(@model,model), year=COALESCE(@year,year), vin=COALESCE(@vin,vin), plate_number=COALESCE(@plate,plate_number), status=COALESCE(@status,status) WHERE id=@id AND company_id=@companyId", c =>
        {
            c.Parameters.AddWithValue("@id", id);
            c.Parameters.AddWithValue("@companyId", GetCompanyId(http));
            BindVehicle(c, body);
        }, ct);
        await audit.LogAsync("vehicle.updated", "Vehicle", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }));
    }

    private static async Task<IResult> CreateDriver(HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "fleet:manage");
        if (denied is not null) return denied;
        var companyId = GetCompanyId(http);
        var id = await db.InsertAsync(@"INSERT INTO drivers (company_id, driver_code, full_name, phone, email, license_number, status, safety_score, readiness_score)
            VALUES (@companyId, @code, @name, @phone, @email, @license, @status, 92, 93)", c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                BindDriver(c, body);
            }, ct);
        await audit.LogAsync("driver.created", "Driver", id, ct: ct);
        return Results.Created($"/api/drivers/{id}", ApiResponse<object>.Ok(new { id }));
    }

    private static async Task<IResult> UpdateDriver(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "fleet:manage");
        if (denied is not null) return denied;
        await db.ExecuteAsync(@"UPDATE drivers SET driver_code=COALESCE(@code,driver_code), full_name=COALESCE(@name,full_name), phone=COALESCE(@phone,phone),
            email=COALESCE(@email,email), license_number=COALESCE(@license,license_number), status=COALESCE(@status,status) WHERE id=@id AND company_id=@companyId", c =>
        {
            c.Parameters.AddWithValue("@id", id);
            c.Parameters.AddWithValue("@companyId", GetCompanyId(http));
            BindDriver(c, body);
        }, ct);
        await audit.LogAsync("driver.updated", "Driver", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }));
    }

    private static async Task<IResult> CreateCustomer(Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var id = await db.InsertAsync(
            @"INSERT INTO customers (company_id, customer_code, name, contact_name, email, phone, billing_address, shipping_address, status, sla_tier, sla_health_score, delivery_experience_score, risk_score)
              VALUES (1, @code, @name, @contact, @email, @phone, @billing, @shipping, @status, @slaTier, 94, 92, 18)",
            c => BindCustomer(c, body), ct);
        await audit.LogAsync("customer.created", "Customer", id, ct: ct);
        await AddTimeline(db, "Customer", id, "customer.created", "Customer profile created", ct);
        return Results.Created($"/api/customers/{id}", ApiResponse<object>.Ok(new { id }, "Customer created"));
    }

    private static async Task<IResult> UpdateCustomer(long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync(
            @"UPDATE customers SET customer_code=COALESCE(@code,customer_code), name=COALESCE(@name,name), contact_name=COALESCE(@contact,contact_name),
                     email=COALESCE(@email,email), phone=COALESCE(@phone,phone), billing_address=COALESCE(@billing,billing_address),
                     shipping_address=COALESCE(@shipping,shipping_address), status=COALESCE(@status,status), sla_tier=COALESCE(@slaTier,sla_tier)
              WHERE id=@id",
            c =>
            {
                c.Parameters.AddWithValue("@id", id);
                BindCustomer(c, body);
            }, ct);
        await audit.LogAsync("customer.updated", "Customer", id, ct: ct);
        await AddTimeline(db, "Customer", id, "customer.updated", "Customer profile updated", ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Customer updated"));
    }

    private static async Task<IResult> CreateAsset(HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var companyId = GetCompanyId(http);
        var id = await db.InsertAsync(
            @"INSERT INTO assets (company_id, asset_code, asset_type, name, status, current_location, assigned_vehicle_id, assigned_driver_id, customer_id, current_zone, geofence_status, utilization_score, risk_score)
              VALUES (@companyId, @code, @type, @name, @status, @location, @vehicleId, @driverId, @customerId, @zone, @geofence, @utilization, @risk)",
            c => { c.Parameters.AddWithValue("@companyId", companyId); BindAsset(c, body); }, ct);
        await audit.LogAsync("asset.created", "Asset", id, ct: ct);
        await AddTimeline(db, "Asset", id, "asset.created", "Asset profile created", ct);
        return Results.Created($"/api/assets/{id}", ApiResponse<object>.Ok(new { id }, "Asset created"));
    }

    private static async Task<IResult> UpdateAsset(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync(
            @"UPDATE assets SET asset_code=COALESCE(@code,asset_code), asset_type=COALESCE(@type,asset_type), name=COALESCE(@name,name),
                     status=COALESCE(@status,status), current_location=COALESCE(@location,current_location), assigned_vehicle_id=COALESCE(@vehicleId,assigned_vehicle_id),
                     assigned_driver_id=COALESCE(@driverId,assigned_driver_id), customer_id=COALESCE(@customerId,customer_id), current_zone=COALESCE(@zone,current_zone),
                     geofence_status=COALESCE(@geofence,geofence_status), utilization_score=COALESCE(@utilization,utilization_score), risk_score=COALESCE(@risk,risk_score)
              WHERE id=@id AND company_id=@companyId",
            c =>
            {
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@companyId", GetCompanyId(http));
                BindAsset(c, body);
            }, ct);
        await audit.LogAsync("asset.updated", "Asset", id, ct: ct);
        await AddTimeline(db, "Asset", id, "asset.updated", "Asset profile updated", ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Asset updated"));
    }

    private static async Task<IResult> AssignAsset(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync(
            @"UPDATE assets SET assigned_vehicle_id=COALESCE(@vehicleId,assigned_vehicle_id), assigned_driver_id=COALESCE(@driverId,assigned_driver_id),
                     customer_id=COALESCE(@customerId,customer_id), status='Assigned'
              WHERE id=@id AND company_id=@companyId",
            c =>
            {
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@companyId", GetCompanyId(http));
                c.Parameters.AddWithValue("@vehicleId", Get(body, "vehicleId"));
                c.Parameters.AddWithValue("@driverId", Get(body, "driverId"));
                c.Parameters.AddWithValue("@customerId", Get(body, "customerId"));
            }, ct);
        await audit.LogAsync("asset.assigned", "Asset", id, ct: ct);
        await AddTimeline(db, "Asset", id, "asset.assigned", "Asset assignment updated", ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Asset assigned"));
    }

    private static async Task<IResult> CreateJob(HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "dispatch:manage");
        if (denied is not null) return denied;
        var validation = await ValidateJob(body, db, ct);
        if (validation.Count > 0) return Results.BadRequest(ApiResponse<object>.Fail("Job validation failed", validation.ToArray()));
        var companyId = GetCompanyId(http);
        var id = await db.InsertAsync(
            @"INSERT INTO jobs (company_id, customer_id, job_code, job_number, job_type, priority, pickup_address, pickup_latitude, pickup_longitude,
                                dropoff_address, dropoff_latitude, dropoff_longitude, scheduled_start, scheduled_end, sla_window_start, sla_window_end,
                                required_vehicle_type, required_driver_certification, assigned_driver_id, assigned_vehicle_id, route_id, status, eta,
                                sla_status, proof_status, customer_update_status, tracking_code, risk_score, revenue_estimate, cost_estimate, margin_estimate, notes)
              VALUES (@companyId, @customerId, @code, @code, @type, @priority, @pickup, @pickupLat, @pickupLng,
                      @dropoff, @dropLat, @dropLng, COALESCE(@start, NOW()), COALESCE(@end, DATE_ADD(NOW(), INTERVAL 4 HOUR)),
                      @slaStart, @slaEnd, @requiredVehicleType, @requiredDriverCertification, @driverId, @vehicleId, @routeId,
                      COALESCE(@status,'Unassigned'), COALESCE(@eta, @end), COALESCE(@slaStatus,'On Track'), COALESCE(@proofStatus,'Pending'),
                      COALESCE(@customerUpdateStatus,'Not Sent'), COALESCE(@trackingCode, CONCAT('ETA-', @code)), COALESCE(@riskScore, 24),
                      COALESCE(@revenue, 600), COALESCE(@cost, 320), COALESCE(@margin, 280), @notes)",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                BindJob(c, body);
            }, ct);
        await audit.LogAsync("job.created", "Job", id, ct: ct);
        await AddTimeline(db, "Job", id, "job.created", "Job created", ct);
        return Results.Created($"/api/jobs/{id}", ApiResponse<object>.Ok(new { id }, "Job created"));
    }

    private static async Task<IResult> UpdateJob(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "dispatch:manage");
        if (denied is not null) return denied;
        var validation = await ValidateJob(body, db, ct, partial: true);
        if (validation.Count > 0) return Results.BadRequest(ApiResponse<object>.Fail("Job validation failed", validation.ToArray()));
        await db.ExecuteAsync(
            @"UPDATE jobs SET job_code=COALESCE(@code,job_code), job_number=COALESCE(@code,job_number), customer_id=COALESCE(@customerId,customer_id),
                 job_type=COALESCE(@type,job_type), priority=COALESCE(@priority,priority), pickup_address=COALESCE(@pickup,pickup_address),
                 pickup_latitude=COALESCE(@pickupLat,pickup_latitude), pickup_longitude=COALESCE(@pickupLng,pickup_longitude),
                 dropoff_address=COALESCE(@dropoff,dropoff_address), dropoff_latitude=COALESCE(@dropLat,dropoff_latitude), dropoff_longitude=COALESCE(@dropLng,dropoff_longitude),
                 scheduled_start=COALESCE(@start,scheduled_start), scheduled_end=COALESCE(@end,scheduled_end), sla_window_start=COALESCE(@slaStart,sla_window_start),
                 sla_window_end=COALESCE(@slaEnd,sla_window_end), required_vehicle_type=COALESCE(@requiredVehicleType,required_vehicle_type),
                 required_driver_certification=COALESCE(@requiredDriverCertification,required_driver_certification), assigned_driver_id=COALESCE(@driverId,assigned_driver_id),
                 assigned_vehicle_id=COALESCE(@vehicleId,assigned_vehicle_id), route_id=COALESCE(@routeId,route_id), status=COALESCE(@status,status),
                 eta=COALESCE(@eta,eta), sla_status=COALESCE(@slaStatus,sla_status), proof_status=COALESCE(@proofStatus,proof_status),
                 customer_update_status=COALESCE(@customerUpdateStatus,customer_update_status), tracking_code=COALESCE(@trackingCode,tracking_code),
                 risk_score=COALESCE(@riskScore,risk_score), revenue_estimate=COALESCE(@revenue,revenue_estimate), cost_estimate=COALESCE(@cost,cost_estimate),
                 margin_estimate=COALESCE(@margin,margin_estimate), notes=COALESCE(@notes,notes)
              WHERE id=@id AND company_id=@companyId",
            c =>
            {
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@companyId", GetCompanyId(http));
                BindJob(c, body);
            }, ct);
        await audit.LogAsync("job.updated", "Job", id, ct: ct);
        await AddTimeline(db, "Job", id, "job.updated", "Job updated", ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Job updated"));
    }

    private static async Task<IResult> AssignJob(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "dispatch:manage");
        if (denied is not null) return denied;
        var validation = await ValidateAssignment(body, db, ct);
        if (validation.Count > 0) return Results.BadRequest(ApiResponse<object>.Fail("Assignment validation failed", validation.ToArray()));
        var match = await CalculateDispatchMatch(body, db, ct);
        var companyId = GetCompanyId(http);
        var affected = await db.ExecuteAsync("UPDATE jobs SET assigned_vehicle_id=@vehicleId, assigned_driver_id=@driverId, status='Assigned' WHERE id=@id AND company_id=@companyId", c =>
        {
            c.Parameters.AddWithValue("@id", id);
            c.Parameters.AddWithValue("@companyId", companyId);
            c.Parameters.AddWithValue("@vehicleId", Get(body, "vehicleId"));
            c.Parameters.AddWithValue("@driverId", Get(body, "driverId"));
        }, ct);
        if (affected == 0) return Results.NotFound(ApiResponse<object>.Fail("Job not found"));
        await db.ExecuteAsync(@"INSERT INTO dispatch_assignments (company_id, job_id, vehicle_id, driver_id, match_score, status, assignment_status, match_reasons_json)
                                VALUES (@companyId, @jobId, @vehicleId, @driverId, @score, 'Assigned', 'Assigned', @reasons)", c =>
        {
            c.Parameters.AddWithValue("@companyId", companyId);
            c.Parameters.AddWithValue("@jobId", id);
            c.Parameters.AddWithValue("@vehicleId", Get(body, "vehicleId"));
            c.Parameters.AddWithValue("@driverId", Get(body, "driverId"));
            c.Parameters.AddWithValue("@score", match.Score);
            c.Parameters.AddWithValue("@reasons", match.ReasonsJson);
        }, ct);
        await audit.LogAsync("job.assigned", "Job", id, ct: ct);
        await AddTimeline(db, "Job", id, "job.assigned", "Job assigned to driver and vehicle", ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, matchScore = match.Score, matchReasons = match.Reasons }, "Job assigned"));
    }

    private static async Task<IResult> ChangeJobStatus(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "dispatch:manage");
        if (denied is not null) return denied;
        var status = Get(body, "status")?.ToString() ?? "Assigned";
        var valid = new[] { "Unassigned", "Assigned", "En Route", "In Progress", "At Stop", "Completed", "Delivered", "Delayed", "At Risk", "Exception" };
        if (!valid.Contains(status)) return Results.BadRequest(ApiResponse<object>.Fail("Invalid status transition", [$"Unsupported status: {status}"]));
        var companyId = GetCompanyId(http);
        var current = await db.QuerySingleAsync("SELECT status FROM jobs WHERE id=@id AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        if (current is null) return Results.NotFound(ApiResponse<object>.Fail("Job not found"));
        await db.ExecuteAsync("UPDATE jobs SET status=@status WHERE id=@id AND company_id=@companyId", c =>
        {
            c.Parameters.AddWithValue("@id", id);
            c.Parameters.AddWithValue("@companyId", companyId);
            c.Parameters.AddWithValue("@status", status);
        }, ct);
        await db.ExecuteAsync("INSERT INTO job_status_events (company_id, job_id, from_status, to_status, notes) VALUES (@companyId, @id, @from, @to, @notes)", c =>
        {
            c.Parameters.AddWithValue("@companyId", companyId);
            c.Parameters.AddWithValue("@id", id);
            c.Parameters.AddWithValue("@from", current?["status"]);
            c.Parameters.AddWithValue("@to", status);
            c.Parameters.AddWithValue("@notes", Get(body, "notes"));
        }, ct);
        await audit.LogAsync("job.status.changed", "Job", id, ct: ct);
        await AddTimeline(db, "Job", id, "job.status_changed", $"Job marked {status}", ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, status }, "Job status updated"));
    }

    private static async Task<IResult> SendEta(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var companyId = GetCompanyId(http);
        var job = await db.QuerySingleAsync("SELECT customer_id, tracking_code, eta FROM jobs WHERE id=@id AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        if (job is null) return Results.NotFound(ApiResponse<object>.Fail("Job not found"));
        await db.ExecuteAsync(
            @"INSERT INTO eta_updates (company_id, job_id, customer_id, tracking_code, eta, confidence_level, message, channel, status)
              VALUES (@companyId, @id, @customerId, @tracking, COALESCE(@eta, DATE_ADD(NOW(), INTERVAL 2 HOUR)), @confidence, @message, @channel, 'Sent')",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@customerId", job?["customerId"]);
                c.Parameters.AddWithValue("@tracking", job?["trackingCode"]);
                c.Parameters.AddWithValue("@eta", Get(body, "eta"));
                c.Parameters.AddWithValue("@confidence", Get(body, "confidenceLevel") is DBNull ? "High" : Get(body, "confidenceLevel"));
                c.Parameters.AddWithValue("@message", Get(body, "message") is DBNull ? "OpsTrax ETA update sent from dispatch." : Get(body, "message"));
                c.Parameters.AddWithValue("@channel", Get(body, "channel") is DBNull ? "Email/SMS" : Get(body, "channel"));
            }, ct);
        await db.ExecuteAsync("UPDATE jobs SET customer_update_status='Sent' WHERE id=@id AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        await audit.LogAsync("eta.sent", "Job", id, ct: ct);
        await AddTimeline(db, "Job", id, "eta.sent", "ETA update sent", ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "ETA update sent"));
    }

    private static async Task<IResult> CreateProofPlaceholder(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var companyId = GetCompanyId(http);
        var proofId = await db.InsertAsync(
            @"INSERT INTO proof_of_delivery (company_id, job_id, receiver_name, received_by, proof_type, status, notes)
              VALUES (@companyId, @id, @receiver, @receiver, 'Placeholder', COALESCE(@status,'Pending'), @notes)",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@receiver", Get(body, "receivedBy") is DBNull ? "Pending receiver" : Get(body, "receivedBy"));
                c.Parameters.AddWithValue("@status", Get(body, "status"));
                c.Parameters.AddWithValue("@notes", Get(body, "notes") is DBNull ? "Proof placeholder created from OpsTrax." : Get(body, "notes"));
            }, ct);
        await db.ExecuteAsync("UPDATE jobs SET proof_status='Pending' WHERE id=@id AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        await audit.LogAsync("proof.placeholder.created", "Job", id, ct: ct);
        await AddTimeline(db, "Job", id, "proof.placeholder.created", "Proof placeholder created", ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, proofId }, "Proof placeholder created"));
    }

    private static Task<IResult> JobsImportPreview(Dictionary<string, object?> body, CancellationToken ct)
    {
        var count = Get(body, "count") is DBNull ? 8 : Convert.ToInt32(Get(body, "count"));
        return Task.FromResult(Results.Ok(ApiResponse<object>.Ok(new
        {
            detectedRows = count,
            validRows = Math.Max(count - 1, 0),
            warnings = new[] { "One row missing optional latitude/longitude", "Customer names matched by seeded customer records" },
            columns = new[] { "jobNumber", "customer", "pickup", "dropoff", "slaWindow", "priority" }
        }, "Import preview generated")));
    }

    private static Task<IResult> DispatchRecommendations(Database db, CancellationToken ct)
        => OkRows(db,
            @"SELECT dr.*, j.job_code, COALESCE(j.job_number,j.job_code) job_number, c.name customer_name, d.full_name driver_name, v.vehicle_code
              FROM dispatch_recommendations dr
              LEFT JOIN jobs j ON j.id=dr.job_id
              LEFT JOIN customers c ON c.id=j.customer_id
              LEFT JOIN drivers d ON d.id=dr.driver_id
              LEFT JOIN vehicles v ON v.id=dr.vehicle_id
              ORDER BY dr.score DESC LIMIT 12", ct: ct);

    private static Task<IResult> AvailableDrivers(Database db, CancellationToken ct)
        => OkRows(db, @"SELECT d.*, ROUND((d.readiness_score+d.safety_score+d.compliance_score)/3,1) match_readiness
                        FROM drivers d WHERE d.deleted_at IS NULL AND d.status IN ('Available','Idle') ORDER BY match_readiness DESC", ct: ct);

    private static Task<IResult> AvailableVehicles(Database db, CancellationToken ct)
        => OkRows(db, @"SELECT v.*, ROUND((v.readiness_score+v.data_quality_score+(100-v.risk_score))/3,1) match_readiness
                        FROM vehicles v WHERE v.deleted_at IS NULL AND v.status IN ('Available','Idle','Active') ORDER BY match_readiness DESC", ct: ct);

    private static async Task<IResult> DispatchAssign(HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "dispatch:manage");
        if (denied is not null) return denied;
        var jobId = Convert.ToInt64(Get(body, "jobId"));
        var result = await AssignJob(http, jobId, body, db, audit, ct);
        await audit.LogAsync("dispatch.recommendation.accepted", "Dispatch", jobId, ct: ct);
        return result;
    }

    private static async Task<IResult> DispatchStatus(HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "dispatch:manage");
        if (denied is not null) return denied;
        var jobId = Convert.ToInt64(Get(body, "jobId"));
        return await ChangeJobStatus(http, jobId, body, db, audit, ct);
    }

    private static async Task<IResult> DispatchAutoSuggest(Database db, AuditService audit, CancellationToken ct)
    {
        await audit.LogAsync("dispatch.auto_suggest.run", "Dispatch", null, ct: ct);
        var rows = await db.QueryAsync(
            @"SELECT dr.*, j.job_code, c.name customer_name,
                     JSON_ARRAY('Same region','Available driver','Available vehicle','Required vehicle type match','Safety score in range','HOS risk placeholder','Proximity placeholder') match_reasons
              FROM dispatch_recommendations dr
              LEFT JOIN jobs j ON j.id=dr.job_id
              LEFT JOIN customers c ON c.id=j.customer_id
              ORDER BY dr.score DESC LIMIT 8", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(rows, "AI dispatch suggestions generated"));
    }

    private static async Task<IResult> DispatchSendEtaUpdates(HttpContext http, Database db, AuditService audit, CancellationToken ct)
    {
        var jobs = await db.QueryAsync("SELECT id FROM jobs WHERE deleted_at IS NULL AND (status IN ('Delayed','At Risk') OR customer_update_status <> 'Sent') LIMIT 10", ct: ct);
        foreach (var job in jobs)
        {
            await SendEta(http, Convert.ToInt64(job["id"]), new Dictionary<string, object?>(), db, audit, ct);
        }
        await audit.LogAsync("dispatch.eta.bulk.sent", "Dispatch", null, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { sent = jobs.Count }, "Bulk ETA updates sent"));
    }

    private static Task<IResult> Routes(Database db, CancellationToken ct)
        => OkRows(db,
            @"SELECT r.*, COALESCE(r.route_name,r.name) route_name, v.vehicle_code, d.full_name driver_name,
                     COALESCE(r.total_stops, COUNT(rs.id)) stops,
                     CASE WHEN r.sla_risk='High' OR r.status IN ('Delayed','At Risk') THEN 'High' WHEN r.efficiency_score < 82 THEN 'Medium' ELSE 'Low' END delay_hotspot_badge,
                     CASE WHEN r.sla_risk='High' THEN 'Optimize sequence and send ETA updates'
                          WHEN r.efficiency_score < 82 THEN 'Run route optimization preview'
                          ELSE 'Release route plan' END recommended_action
              FROM routes r
              LEFT JOIN vehicles v ON v.id=r.assigned_vehicle_id
              LEFT JOIN drivers d ON d.id=r.assigned_driver_id
              LEFT JOIN route_stops rs ON rs.route_id=r.id
              WHERE r.deleted_at IS NULL
              GROUP BY r.id, v.vehicle_code, d.full_name
              ORDER BY r.planned_start DESC, r.id DESC", ct: ct);

    private static async Task<IResult> RoutesSummary(Database db, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT COUNT(*) total_routes_today,
                     SUM(status='Active') active_routes,
                     SUM(status='Planned') planned_routes,
                     SUM(status='Completed') completed_routes,
                     SUM(status IN ('Delayed','At Risk')) delayed_routes,
                     ROUND(AVG(total_stops),1) average_stops_per_route,
                     CONCAT(ROUND(AVG(estimated_duration_minutes)), ' min') average_route_eta,
                     ROUND(AVG(efficiency_score),1) route_efficiency_score,
                     SUM(sla_risk='High' OR status IN ('Delayed','At Risk')) high_risk_routes,
                     CONCAT('$', FORMAT(COALESCE(SUM(cost_estimate),0),0)) route_cost_estimate,
                     COUNT(*) total,
                     SUM(status IN ('Active','Planned')) active,
                     SUM(sla_risk='High' OR status IN ('Delayed','At Risk')) at_risk
              FROM routes WHERE deleted_at IS NULL", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(row ?? new Dictionary<string, object?>()));
    }

    private static async Task<IResult> RouteDetail(long id, Database db, CancellationToken ct)
    {
        var record = await db.QuerySingleAsync(
            @"SELECT r.*, COALESCE(r.route_name,r.name) route_name, v.vehicle_code, d.full_name driver_name
              FROM routes r LEFT JOIN vehicles v ON v.id=r.assigned_vehicle_id LEFT JOIN drivers d ON d.id=r.assigned_driver_id
              WHERE r.id=@id AND r.deleted_at IS NULL", c => c.Parameters.AddWithValue("@id", id), ct);
        if (record is null) return Results.NotFound(ApiResponse<object>.Fail("Route not found"));
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            record,
            stops = await RouteStopsRows(id, db, ct),
            timeline = await EntityTimeline(db, "Route", id, ct),
            recommendations = await db.QueryAsync("SELECT * FROM route_recommendations WHERE route_id=@id OR route_id IS NULL ORDER BY score DESC LIMIT 8", c => c.Parameters.AddWithValue("@id", id), ct),
            path = await db.QuerySingleAsync("SELECT * FROM route_paths WHERE route_id=@id ORDER BY created_at DESC LIMIT 1", c => c.Parameters.AddWithValue("@id", id), ct),
            auditTrail = await AuditTrail(db, "Route", id, ct)
        }));
    }

    private static async Task<IResult> CreateRoute(HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var companyId = GetCompanyId(http);
        var id = await db.InsertAsync(
            @"INSERT INTO routes (company_id, route_code, name, route_name, status, assigned_driver_id, assigned_vehicle_id, region, route_type, planned_start, planned_end, efficiency_score, sla_risk, cost_estimate, optimization_mode, notes)
              VALUES (@companyId, @code, @name, @name, COALESCE(@status,'Planned'), @driverId, @vehicleId, @region, COALESCE(@routeType,'Delivery'), @start, @end, 88, 'Low', COALESCE(@cost,250), COALESCE(@mode,'Balanced'), @notes)",
            c => { c.Parameters.AddWithValue("@companyId", companyId); BindRoute(c, body); }, ct);
        await audit.LogAsync("route.created", "Route", id, ct: ct);
        await AddTimeline(db, "Route", id, "route.created", "Route created", ct);
        return Results.Created($"/api/routes/{id}", ApiResponse<object>.Ok(new { id }, "Route created"));
    }

    private static async Task<IResult> UpdateRoute(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync(
            @"UPDATE routes SET route_code=COALESCE(@code,route_code), name=COALESCE(@name,name), route_name=COALESCE(@name,route_name),
                  status=COALESCE(@status,status), assigned_driver_id=COALESCE(@driverId,assigned_driver_id), assigned_vehicle_id=COALESCE(@vehicleId,assigned_vehicle_id),
                  region=COALESCE(@region,region), route_type=COALESCE(@routeType,route_type), planned_start=COALESCE(@start,planned_start), planned_end=COALESCE(@end,planned_end),
                  cost_estimate=COALESCE(@cost,cost_estimate), optimization_mode=COALESCE(@mode,optimization_mode), notes=COALESCE(@notes,notes)
              WHERE id=@id AND company_id=@companyId", c =>
            {
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@companyId", GetCompanyId(http));
                BindRoute(c, body);
            }, ct);
        await audit.LogAsync("route.updated", "Route", id, ct: ct);
        await AddTimeline(db, "Route", id, "route.updated", "Route updated", ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Route updated"));
    }

    private static async Task<IResult> RouteStops(long id, Database db, CancellationToken ct)
        => Results.Ok(ApiResponse<object>.Ok(await RouteStopsRows(id, db, ct)));

    private static Task<List<Dictionary<string, object?>>> RouteStopsRows(long id, Database db, CancellationToken ct)
        => db.QueryAsync(@"SELECT rs.*, c.name customer_name, j.job_code FROM route_stops rs LEFT JOIN customers c ON c.id=rs.customer_id LEFT JOIN jobs j ON j.id=rs.job_id WHERE rs.route_id=@id ORDER BY rs.stop_sequence", c => c.Parameters.AddWithValue("@id", id), ct);

    private static async Task<IResult> CreateRouteStop(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var sequence = Get(body, "stopSequence") is DBNull ? 1 : Convert.ToInt32(Get(body, "stopSequence"));
        if (sequence < 1) return Results.BadRequest(ApiResponse<object>.Fail("Route stop sequence must be valid"));
        var stopId = await db.InsertAsync(
            @"INSERT INTO route_stops (company_id, route_id, job_id, customer_id, stop_sequence, stop_type, address, lat, lng, latitude, longitude, time_window_start, time_window_end, eta, status, proof_status, notes)
              VALUES (@companyId, @routeId, @jobId, @customerId, @sequence, @type, @address, @lat, @lng, @lat, @lng, @start, @end, @eta, COALESCE(@status,'Pending'), COALESCE(@proof,'Pending'), @notes)",
            c => { c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); BindRouteStop(c, id, body); }, ct);
        await audit.LogAsync("route.stop.added", "Route", id, ct: ct);
        await AddTimeline(db, "Route", id, "route.stop.added", "Route stop added", ct);
        return Results.Created($"/api/routes/{id}/stops/{stopId}", ApiResponse<object>.Ok(new { id = stopId }, "Route stop added"));
    }

    private static async Task<IResult> UpdateRouteStop(HttpContext http, long id, long stopId, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync(
            @"UPDATE route_stops SET job_id=COALESCE(@jobId,job_id), customer_id=COALESCE(@customerId,customer_id), stop_sequence=COALESCE(@sequence,stop_sequence),
                  stop_type=COALESCE(@type,stop_type), address=COALESCE(@address,address), lat=COALESCE(@lat,lat), lng=COALESCE(@lng,lng),
                  latitude=COALESCE(@lat,latitude), longitude=COALESCE(@lng,longitude), time_window_start=COALESCE(@start,time_window_start),
                  time_window_end=COALESCE(@end,time_window_end), eta=COALESCE(@eta,eta), status=COALESCE(@status,status), proof_status=COALESCE(@proof,proof_status), notes=COALESCE(@notes,notes)
              WHERE id=@stopId AND route_id=@routeId AND company_id=@companyId", c =>
            {
                c.Parameters.AddWithValue("@stopId", stopId);
                c.Parameters.AddWithValue("@companyId", GetCompanyId(http));
                BindRouteStop(c, id, body);
            }, ct);
        await audit.LogAsync("route.stop.updated", "Route", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id = stopId }, "Route stop updated"));
    }

    private static async Task<IResult> DeleteRouteStop(HttpContext http, long id, long stopId, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync("DELETE FROM route_stops WHERE id=@stopId AND route_id=@routeId AND company_id=@companyId", c =>
        {
            c.Parameters.AddWithValue("@stopId", stopId);
            c.Parameters.AddWithValue("@routeId", id);
            c.Parameters.AddWithValue("@companyId", GetCompanyId(http));
        }, ct);
        await audit.LogAsync("route.stop.deleted", "Route", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id = stopId }, "Route stop deleted"));
    }

    private static async Task<IResult> RouteOptimizePreview(long id, Database db, AuditService audit, CancellationToken ct)
    {
        var stops = await RouteStopsRows(id, db, ct);
        var score = Math.Min(98, 80 + stops.Count);
        await audit.LogAsync("route.optimization.preview.run", "Route", id, ct: ct);
        await AddTimeline(db, "Route", id, "route.optimized", "Route optimization preview generated", ct);
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            routeId = id,
            efficiencyScore = score,
            estimatedSavingsMinutes = Math.Max(8, stops.Count * 4),
            costLeakageReduction = "$" + (stops.Count * 23),
            recommendedSequence = stops.Select((stop, i) => new { stopId = stop["id"], sequence = i + 1, reason = "Balanced SLA and distance" })
        }, "Optimization preview generated"));
    }

    private static async Task<IResult> AssignRoute(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync("UPDATE routes SET assigned_driver_id=COALESCE(@driverId,assigned_driver_id), assigned_vehicle_id=COALESCE(@vehicleId,assigned_vehicle_id), status='Active' WHERE id=@id AND company_id=@companyId", c =>
        {
            c.Parameters.AddWithValue("@id", id);
            c.Parameters.AddWithValue("@companyId", GetCompanyId(http));
            c.Parameters.AddWithValue("@driverId", Get(body, "driverId"));
            c.Parameters.AddWithValue("@vehicleId", Get(body, "vehicleId"));
        }, ct);
        await audit.LogAsync("route.assigned", "Route", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Route assigned"));
    }

    private static async Task<IResult> RouteRecommendations(long id, Database db, CancellationToken ct)
        => Results.Ok(ApiResponse<object>.Ok(await db.QueryAsync("SELECT * FROM route_recommendations WHERE route_id=@id OR route_id IS NULL ORDER BY score DESC LIMIT 8", c => c.Parameters.AddWithValue("@id", id), ct)));

    private static async Task<IResult> CustomerEtaSummary(Database db, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT COUNT(*) total_tracked,
                     SUM(j.status IN ('Delayed','At Risk') OR j.sla_status='At Risk') eta_risk,
                     SUM(j.customer_update_status <> 'Sent') updates_needed,
                     SUM(cc.status='Sent') communications_sent,
                     SUM(cc.status <> 'Sent') pending_communications,
                     ROUND(AVG(CASE WHEN j.status IN ('Completed','Delivered') THEN 96 WHEN j.status IN ('Delayed','At Risk') THEN 72 ELSE 88 END),1) customer_experience_score
              FROM jobs j
              LEFT JOIN customer_communications cc ON cc.job_id=j.id
              WHERE j.deleted_at IS NULL", ct: ct);
        var jobs = await db.QueryAsync(
            @"SELECT j.id, COALESCE(j.job_number,j.job_code) job_number, j.tracking_code, j.status, j.eta, j.sla_status, j.customer_update_status,
                     c.name customer_name, d.full_name driver_name, v.vehicle_code,
                     CASE WHEN j.risk_score >= 70 OR j.sla_status='At Risk' THEN 'At Risk' WHEN j.risk_score >= 45 THEN 'Medium' ELSE 'High' END eta_confidence_level,
                     CASE WHEN j.customer_update_status <> 'Sent' OR j.sla_status='At Risk' THEN 'Send customer update' ELSE 'Monitor' END recommended_action
              FROM jobs j
              LEFT JOIN customers c ON c.id=j.customer_id
              LEFT JOIN drivers d ON d.id=j.assigned_driver_id
              LEFT JOIN vehicles v ON v.id=j.assigned_vehicle_id
              WHERE j.deleted_at IS NULL AND (j.customer_update_status <> 'Sent' OR j.status IN ('Delayed','At Risk') OR j.sla_status='At Risk')
              ORDER BY j.risk_score DESC, j.scheduled_start LIMIT 16", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { summary = row, jobs }));
    }

    private static async Task<IResult> CustomerEtaTrack(string trackingCode, Database db, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT j.id, COALESCE(j.job_number,j.job_code) job_number, j.status, j.eta, j.sla_status, j.proof_status, j.tracking_code,
                     c.name customer_name, j.pickup_address, j.dropoff_address,
                     CASE WHEN j.risk_score >= 70 OR j.sla_status='At Risk' THEN 'At Risk' WHEN j.risk_score >= 45 THEN 'Medium' ELSE 'High' END eta_confidence_level
              FROM jobs j LEFT JOIN customers c ON c.id=j.customer_id
              WHERE j.tracking_code=@code AND j.deleted_at IS NULL LIMIT 1", c => c.Parameters.AddWithValue("@code", trackingCode), ct);
        if (row is null) return Results.NotFound(ApiResponse<object>.Fail("Tracking code not found"));
        await db.ExecuteAsync("UPDATE customer_eta_links SET last_viewed_at=NOW() WHERE tracking_code=@code", c => c.Parameters.AddWithValue("@code", trackingCode), ct);
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            tracking = row,
            timeline = new[]
            {
                new { label = "Scheduled", complete = true },
                new { label = "Assigned", complete = !string.Equals(row["status"]?.ToString(), "Unassigned", StringComparison.OrdinalIgnoreCase) },
                new { label = "En Route", complete = new[] { "En Route", "In Progress", "At Stop", "Completed", "Delivered" }.Contains(row["status"]?.ToString()) },
                new { label = "Arrived", complete = new[] { "At Stop", "Completed", "Delivered" }.Contains(row["status"]?.ToString()) },
                new { label = "Completed", complete = new[] { "Completed", "Delivered" }.Contains(row["status"]?.ToString()) }
            },
            proofPreview = await db.QuerySingleAsync("SELECT status, received_by, captured_at FROM proof_of_delivery WHERE job_id=@id ORDER BY captured_at DESC LIMIT 1", c => c.Parameters.AddWithValue("@id", row["id"]), ct),
            customerMessage = "Connected transport. Intelligent control. Enterprise execution."
        }));
    }

    private static Task<IResult> CustomerEtaJob(long jobId, Database db, CancellationToken ct)
        => JobDetail(jobId, db, ct);

    private static async Task<IResult> CustomerEtaSendUpdate(HttpContext http, long jobId, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
        => await SendEta(http, jobId, body, db, audit, ct);

    private static async Task<IResult> CustomerEtaFeedback(HttpContext http, long jobId, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var companyId = GetCompanyId(http);
        var id = await db.InsertAsync(
            @"INSERT INTO customer_feedback (company_id, job_id, tracking_code, rating, sentiment, comments)
              VALUES (@companyId, @jobId, @tracking, @rating, @sentiment, @comments)", c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@jobId", jobId);
                c.Parameters.AddWithValue("@tracking", Get(body, "trackingCode"));
                c.Parameters.AddWithValue("@rating", Get(body, "rating"));
                c.Parameters.AddWithValue("@sentiment", Get(body, "sentiment") is DBNull ? "Pending Review" : Get(body, "sentiment"));
                c.Parameters.AddWithValue("@comments", Get(body, "comments"));
            }, ct);
        await audit.LogAsync("customer.eta.feedback.received", "Job", jobId, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Feedback received"));
    }

    private static Task<IResult> CustomerEtaCommunications(Database db, CancellationToken ct)
        => OkRows(db,
            @"SELECT cc.*, c.name customer_name, COALESCE(j.job_number,j.job_code) job_number, j.tracking_code
              FROM customer_communications cc
              LEFT JOIN customers c ON c.id=cc.customer_id
              LEFT JOIN jobs j ON j.id=cc.job_id
              ORDER BY cc.sent_at DESC LIMIT 50", ct: ct);

    private const string MaintenanceBaseSql =
        @"SELECT mi.*, COALESCE(mi.service_type,mi.category,mi.title) service_type, mi.risk_score maintenance_risk_heat_score,
                 COALESCE(mi.recommended_action, 'Review maintenance timing') recommended_action,
                 v.vehicle_code, a.asset_code, a.name asset_name,
                 CASE WHEN mi.due_date < CURDATE() OR mi.status='Overdue' THEN 'Overdue'
                      WHEN mi.due_date <= DATE_ADD(CURDATE(), INTERVAL 7 DAY) THEN 'Due Soon'
                      ELSE 'Scheduled' END downtime_risk_badge
          FROM maintenance_items mi
          LEFT JOIN vehicles v ON v.id=mi.vehicle_id
          LEFT JOIN assets a ON a.id=mi.asset_id";

    private const string WorkOrdersBaseSql =
        @"SELECT wo.*, COALESCE(wo.work_order_number,wo.work_order_code) work_order_number, wo.risk_score repair_priority_score,
                 COALESCE(wo.recommended_action, 'Review repair queue') recommended_action,
                 v.vehicle_code, a.asset_code, a.name asset_name, u.full_name assigned_to_name
          FROM work_orders wo
          LEFT JOIN vehicles v ON v.id=wo.vehicle_id
          LEFT JOIN assets a ON a.id=wo.asset_id
          LEFT JOIN users u ON u.id=wo.assigned_to_user_id";

    private const string DvirBaseSql =
        @"SELECT dr.*, dr.risk_score defect_severity_score,
                 COALESCE(dr.recommended_action, 'Review inspection evidence') recommended_action,
                 v.vehicle_code, d.full_name driver_name, wo.work_order_number linked_work_order_number
          FROM dvir_reports dr
          LEFT JOIN vehicles v ON v.id=dr.vehicle_id
          LEFT JOIN drivers d ON d.id=dr.driver_id
          LEFT JOIN work_orders wo ON wo.dvir_report_id=dr.id";

    private const string DocumentsBaseSql =
        @"SELECT d.*, d.risk_score document_expiry_risk_score,
                 COALESCE(d.recommended_action, 'Keep document in active vault') recommended_action,
                 CASE
                   WHEN d.entity_type='vehicle' THEN (SELECT vehicle_code FROM vehicles WHERE id=d.entity_id)
                   WHEN d.entity_type='driver' THEN (SELECT full_name FROM drivers WHERE id=d.entity_id)
                   WHEN d.entity_type='asset' THEN (SELECT name FROM assets WHERE id=d.entity_id)
                   WHEN d.entity_type='customer' THEN (SELECT name FROM customers WHERE id=d.entity_id)
                   ELSE d.owner_name
                 END entity_name
          FROM documents d";

    private static Task<IResult> MaintenanceItems(Database db, CancellationToken ct)
        => OkRows(db, MaintenanceBaseSql + " WHERE mi.deleted_at IS NULL ORDER BY FIELD(mi.priority,'Critical','High','Medium','Low'), mi.due_date, mi.id DESC", ct: ct);

    private static async Task<IResult> MaintenanceSummary(Database db, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT SUM(mi.status IN ('Open','Scheduled','In Progress') OR mi.due_date <= DATE_ADD(CURDATE(), INTERVAL 14 DAY)) maintenance_due,
                     SUM(mi.status='Overdue' OR mi.due_date < CURDATE()) overdue_services,
                     SUM(mi.priority='Critical' OR mi.risk_score >= 80) critical_maintenance,
                     (SELECT COUNT(*) FROM vehicles WHERE status='Maintenance') vehicles_out_of_service,
                     CONCAT(ROUND(AVG(CASE WHEN wo.status='Completed' THEN wo.downtime_hours ELSE NULL END),1),'h') average_downtime,
                     CONCAT(ROUND(100 * SUM(mi.status NOT IN ('Overdue','Deleted')) / NULLIF(COUNT(*),0),1),'%') pm_compliance,
                     (SELECT COUNT(*) FROM work_orders WHERE deleted_at IS NULL AND status NOT IN ('Completed','Cancelled','Deleted')) open_work_orders,
                     CONCAT('$', FORMAT(COALESCE(SUM(mi.estimated_cost),0),0)) estimated_maintenance_cost,
                     SUM(mi.service_type IN ('Brake Inspection','Tire Rotation')) repeat_issues,
                     SUM(mi.due_date BETWEEN CURDATE() AND DATE_ADD(CURDATE(), INTERVAL 7 DAY)) service_due_this_week,
                     SUM(mi.asset_id IS NOT NULL) asset_maintenance_due,
                     SUM(mi.risk_score >= 70) warranty_risk_placeholder,
                     ROUND(100 - AVG(LEAST(mi.risk_score,95)),1) maintenance_readiness_score
              FROM maintenance_items mi
              LEFT JOIN work_orders wo ON wo.maintenance_item_id=mi.id
              WHERE mi.deleted_at IS NULL", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(row ?? new Dictionary<string, object?>()));
    }

    private static async Task<IResult> MaintenanceDetail(long id, Database db, CancellationToken ct)
    {
        var record = (await db.QueryAsync(MaintenanceBaseSql + " WHERE mi.id=@id", c => c.Parameters.AddWithValue("@id", id), ct)).FirstOrDefault();
        if (record is null) return Results.NotFound(ApiResponse<object>.Fail("Maintenance item not found"));
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            record,
            schedules = await db.QueryAsync("SELECT * FROM maintenance_schedules WHERE (vehicle_id=@vehicleId OR asset_id=@assetId) AND deleted_at IS NULL ORDER BY next_due_date LIMIT 8", c =>
            {
                c.Parameters.AddWithValue("@vehicleId", record["vehicleId"]);
                c.Parameters.AddWithValue("@assetId", record["assetId"]);
            }, ct),
            workOrders = await db.QueryAsync("SELECT * FROM work_orders WHERE maintenance_item_id=@id AND deleted_at IS NULL ORDER BY created_date DESC", c => c.Parameters.AddWithValue("@id", id), ct),
            timeline = await EntityTimeline(db, "Maintenance", id, ct),
            recommendations = await ModuleRecommendations(db, "maintenance", ct),
            auditTrail = await AuditRows(db, "Maintenance", id, ct)
        }));
    }

    private static async Task<IResult> CreateMaintenance(HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var errors = ValidateMaintenance(body);
        if (errors.Count > 0) return Results.BadRequest(ApiResponse<object>.Fail("Maintenance validation failed", errors.ToArray()));
        var companyId = GetCompanyId(http);
        var id = await db.InsertAsync(
            @"INSERT INTO maintenance_items (company_id, vehicle_id, asset_id, service_type, title, category, description, priority, status, due_date, due_odometer, due_engine_hours, estimated_cost, risk_level, risk_score, recommended_action)
              VALUES (@companyId, @vehicleId, @assetId, @serviceType, @serviceType, @serviceType, @description, COALESCE(@priority,'Medium'), COALESCE(@status,'Open'), @dueDate, @dueOdometer, @dueHours, COALESCE(@cost,300), COALESCE(@priority,'Medium'), COALESCE(@risk,42), COALESCE(@action,'Schedule preventive service'))",
            c => { c.Parameters.AddWithValue("@companyId", companyId); BindMaintenance(c, body); }, ct);
        await audit.LogAsync("maintenance.created", "Maintenance", id, ct: ct);
        await AddTimeline(db, "Maintenance", id, "maintenance.created", "Maintenance item created", ct);
        return Results.Created($"/api/maintenance/{id}", ApiResponse<object>.Ok(new { id }, "Maintenance item created"));
    }

    private static async Task<IResult> UpdateMaintenance(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync(
            @"UPDATE maintenance_items SET vehicle_id=COALESCE(@vehicleId,vehicle_id), asset_id=COALESCE(@assetId,asset_id), service_type=COALESCE(@serviceType,service_type),
                title=COALESCE(@serviceType,title), category=COALESCE(@serviceType,category), description=COALESCE(@description,description), priority=COALESCE(@priority,priority),
                status=COALESCE(@status,status), due_date=COALESCE(@dueDate,due_date), due_odometer=COALESCE(@dueOdometer,due_odometer),
                due_engine_hours=COALESCE(@dueHours,due_engine_hours), estimated_cost=COALESCE(@cost,estimated_cost), risk_score=COALESCE(@risk,risk_score),
                recommended_action=COALESCE(@action,recommended_action) WHERE id=@id AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); BindMaintenance(c, body); }, ct);
        await audit.LogAsync("maintenance.updated", "Maintenance", id, ct: ct);
        await AddTimeline(db, "Maintenance", id, "maintenance.updated", "Maintenance item updated", ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Maintenance item updated"));
    }

    private static async Task<IResult> MaintenanceSchedule(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync("UPDATE maintenance_items SET status='Scheduled', due_date=COALESCE(@date,due_date) WHERE id=@id AND company_id=@companyId", c =>
        {
            c.Parameters.AddWithValue("@id", id);
            c.Parameters.AddWithValue("@companyId", GetCompanyId(http));
            c.Parameters.AddWithValue("@date", Get(body, "scheduledDate"));
        }, ct);
        await audit.LogAsync("maintenance.scheduled", "Maintenance", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Maintenance scheduled"));
    }

    private static async Task<IResult> MaintenanceDefer(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync("UPDATE maintenance_items SET status='Deferred', due_date=DATE_ADD(COALESCE(@date,due_date,CURDATE()), INTERVAL 7 DAY) WHERE id=@id AND company_id=@companyId", c =>
        {
            c.Parameters.AddWithValue("@id", id);
            c.Parameters.AddWithValue("@companyId", GetCompanyId(http));
            c.Parameters.AddWithValue("@date", Get(body, "dueDate"));
        }, ct);
        await audit.LogAsync("maintenance.deferred", "Maintenance", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Maintenance deferred"));
    }

    private static async Task<IResult> MaintenanceCreateWorkOrder(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var companyId = GetCompanyId(http);
        var item = await db.QuerySingleAsync("SELECT * FROM maintenance_items WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        if (item is null) return Results.NotFound(ApiResponse<object>.Fail("Maintenance item not found"));
        var woId = await db.InsertAsync(
            @"INSERT INTO work_orders (company_id, vehicle_id, asset_id, maintenance_item_id, work_order_code, work_order_number, issue_type, title, description, priority, status, vendor_name, created_date, due_date, estimated_cost, cost_approval_status, risk_score, recommended_action)
              VALUES (@companyId, @vehicleId, @assetId, @maintenanceId, CONCAT('WO-MNT-', @maintenanceId), CONCAT('WO-MNT-', @maintenanceId), @issue, @title, @description, @priority, 'Open', 'NOVA Fleet Care', NOW(), COALESCE(@dueDate, DATE_ADD(NOW(), INTERVAL 5 DAY)), @cost, 'Pending', @risk, 'Assign technician and reserve parts')",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@vehicleId", item["vehicleId"]);
                c.Parameters.AddWithValue("@assetId", item["assetId"]);
                c.Parameters.AddWithValue("@maintenanceId", id);
                c.Parameters.AddWithValue("@issue", item["serviceType"]);
                c.Parameters.AddWithValue("@title", $"Work order for {item["serviceType"]}");
                c.Parameters.AddWithValue("@description", item["description"]);
                c.Parameters.AddWithValue("@priority", item["priority"]);
                c.Parameters.AddWithValue("@dueDate", item["dueDate"]);
                c.Parameters.AddWithValue("@cost", item["estimatedCost"]);
                c.Parameters.AddWithValue("@risk", item["riskScore"]);
            }, ct);
        await db.ExecuteAsync("UPDATE maintenance_items SET status='Scheduled' WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        await audit.LogAsync("maintenance.workorder.created", "Maintenance", id, ct: ct);
        await audit.LogAsync("workorder.created", "WorkOrder", woId, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id = woId }, "Work order created from maintenance item"));
    }

    private static Task<IResult> WorkOrders(Database db, CancellationToken ct)
        => OkRows(db, WorkOrdersBaseSql + " WHERE wo.deleted_at IS NULL ORDER BY FIELD(wo.priority,'Critical','High','Medium','Low'), FIELD(wo.status,'Open','Assigned','In Progress','Waiting Parts','Waiting Approval','Draft','Completed','Cancelled'), wo.due_date", ct: ct);

    private static async Task<IResult> WorkOrdersSummary(Database db, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT SUM(status NOT IN ('Completed','Cancelled','Deleted')) open_work_orders,
                     SUM(priority='Critical' AND status NOT IN ('Completed','Cancelled','Deleted')) critical_work_orders,
                     SUM(status='In Progress') in_progress,
                     SUM(status='Waiting Parts') waiting_parts,
                     SUM(status='Waiting Approval') waiting_approval,
                     SUM(status='Completed' AND completed_at >= DATE_SUB(NOW(), INTERVAL 7 DAY)) completed_this_week,
                     CONCAT(ROUND(AVG(CASE WHEN status='Completed' THEN downtime_hours ELSE NULL END),1),'h') average_resolution_time,
                     CONCAT('$', FORMAT(COALESCE(SUM(estimated_cost),0),0)) total_estimated_cost,
                     CONCAT('$', FORMAT(COALESCE(SUM(approved_cost),0),0)) total_approved_cost,
                     SUM(issue_type IN ('Brakes','Tires','DVIR Defect')) repeat_repairs,
                     SUM(status IN ('Open','Assigned','In Progress','Waiting Parts') AND priority IN ('High','Critical')) vehicles_down,
                     SUM(vendor_name IS NOT NULL AND status IN ('Waiting Parts','Waiting Approval')) vendor_sla_risk
              FROM work_orders WHERE deleted_at IS NULL", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(row ?? new Dictionary<string, object?>()));
    }

    private static async Task<IResult> WorkOrderDetail(long id, Database db, CancellationToken ct)
    {
        var record = (await db.QueryAsync(WorkOrdersBaseSql + " WHERE wo.id=@id", c => c.Parameters.AddWithValue("@id", id), ct)).FirstOrDefault();
        if (record is null) return Results.NotFound(ApiResponse<object>.Fail("Work order not found"));
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            record,
            labor = await db.QueryAsync("SELECT * FROM work_order_labor WHERE work_order_id=@id ORDER BY created_at DESC", c => c.Parameters.AddWithValue("@id", id), ct),
            parts = await db.QueryAsync("SELECT * FROM work_order_parts WHERE work_order_id=@id ORDER BY created_at DESC", c => c.Parameters.AddWithValue("@id", id), ct),
            timeline = await WorkOrderTimelineRows(id, db, ct),
            documents = await db.QueryAsync("SELECT * FROM documents WHERE entity_type IN ('work order','work_order') AND entity_id=@id AND deleted_at IS NULL", c => c.Parameters.AddWithValue("@id", id), ct),
            recommendations = await ModuleRecommendations(db, "work-orders", ct),
            auditTrail = await AuditRows(db, "WorkOrder", id, ct)
        }));
    }

    private static async Task<IResult> CreateWorkOrder(HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var errors = ValidateWorkOrder(body);
        if (errors.Count > 0) return Results.BadRequest(ApiResponse<object>.Fail("Work order validation failed", errors.ToArray()));
        var companyId = GetCompanyId(http);
        var id = await db.InsertAsync(
            @"INSERT INTO work_orders (company_id, vehicle_id, asset_id, maintenance_item_id, dvir_report_id, work_order_code, work_order_number, issue_type, title, description, priority, status, assigned_to_user_id, vendor_name, created_date, due_date, estimated_cost, approved_cost, downtime_hours, cost_approval_status, risk_score, recommended_action, notes)
              VALUES (@companyId, @vehicleId, @assetId, @maintenanceItemId, @dvirReportId, @number, @number, @issueType, @title, @description, COALESCE(@priority,'Medium'), COALESCE(@status,'Open'), @assignedTo, @vendor, NOW(), @dueDate, COALESCE(@estimatedCost,0), @approvedCost, COALESCE(@downtime,0), COALESCE(@approval,'Pending'), COALESCE(@risk,45), COALESCE(@action,'Assign repair owner'), @notes)",
            c => { c.Parameters.AddWithValue("@companyId", companyId); BindWorkOrder(c, body); }, ct);
        await audit.LogAsync("workorder.created", "WorkOrder", id, ct: ct);
        await AddWorkOrderEvent(db, id, null, Get(body, "status")?.ToString() ?? "Open", "Work order created", ct);
        return Results.Created($"/api/workorders/{id}", ApiResponse<object>.Ok(new { id }, "Work order created"));
    }

    private static async Task<IResult> UpdateWorkOrder(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync(
            @"UPDATE work_orders SET vehicle_id=COALESCE(@vehicleId,vehicle_id), asset_id=COALESCE(@assetId,asset_id), maintenance_item_id=COALESCE(@maintenanceItemId,maintenance_item_id),
                dvir_report_id=COALESCE(@dvirReportId,dvir_report_id), work_order_code=COALESCE(@number,work_order_code), work_order_number=COALESCE(@number,work_order_number),
                issue_type=COALESCE(@issueType,issue_type), title=COALESCE(@title,title), description=COALESCE(@description,description), priority=COALESCE(@priority,priority),
                status=COALESCE(@status,status), assigned_to_user_id=COALESCE(@assignedTo,assigned_to_user_id), vendor_name=COALESCE(@vendor,vendor_name), due_date=COALESCE(@dueDate,due_date),
                estimated_cost=COALESCE(@estimatedCost,estimated_cost), approved_cost=COALESCE(@approvedCost,approved_cost), downtime_hours=COALESCE(@downtime,downtime_hours),
                cost_approval_status=COALESCE(@approval,cost_approval_status), risk_score=COALESCE(@risk,risk_score), recommended_action=COALESCE(@action,recommended_action), notes=COALESCE(@notes,notes)
              WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); BindWorkOrder(c, body); }, ct);
        await audit.LogAsync("workorder.updated", "WorkOrder", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Work order updated"));
    }

    private static async Task<IResult> WorkOrderTimeline(long id, Database db, CancellationToken ct)
        => Results.Ok(ApiResponse<object>.Ok(await WorkOrderTimelineRows(id, db, ct)));

    private static Task<List<Dictionary<string, object?>>> WorkOrderTimelineRows(long id, Database db, CancellationToken ct)
        => db.QueryAsync("SELECT * FROM work_order_status_events WHERE work_order_id=@id ORDER BY occurred_at DESC LIMIT 20", c => c.Parameters.AddWithValue("@id", id), ct);

    private static async Task<IResult> WorkOrderAssign(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync("UPDATE work_orders SET assigned_to_user_id=@userId, status='Assigned' WHERE id=@id AND company_id=@companyId", c =>
        {
            c.Parameters.AddWithValue("@id", id);
            c.Parameters.AddWithValue("@companyId", GetCompanyId(http));
            c.Parameters.AddWithValue("@userId", Get(body, "assignedToUserId"));
        }, ct);
        await AddWorkOrderEvent(db, id, null, "Assigned", "Work order assigned", ct);
        await audit.LogAsync("workorder.assigned", "WorkOrder", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Work order assigned"));
    }

    private static async Task<IResult> WorkOrderStatus(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var status = Get(body, "status")?.ToString() ?? "Open";
        var valid = new[] { "Draft", "Open", "Assigned", "In Progress", "Waiting Parts", "Waiting Approval", "Completed", "Cancelled" };
        if (!valid.Contains(status)) return Results.BadRequest(ApiResponse<object>.Fail("Invalid work order status", [$"Unsupported status: {status}"]));
        var current = await db.QuerySingleAsync("SELECT status FROM work_orders WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); }, ct);
        await db.ExecuteAsync("UPDATE work_orders SET status=@status WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); c.Parameters.AddWithValue("@status", status); }, ct);
        await AddWorkOrderEvent(db, id, current?["status"]?.ToString(), status, $"Status changed to {status}", ct);
        await audit.LogAsync("workorder.status.changed", "WorkOrder", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, status }, "Work order status updated"));
    }

    private static async Task<IResult> WorkOrderAddLabor(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var companyId = GetCompanyId(http);
        var laborId = await db.InsertAsync(
            @"INSERT INTO work_order_labor (company_id, work_order_id, technician_name, labor_hours, labor_rate, total_cost, notes)
              VALUES (@companyId, @id, @tech, COALESCE(@hours,1), COALESCE(@rate,95), COALESCE(@hours,1)*COALESCE(@rate,95), @notes)",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@tech", Get(body, "technicianName") is DBNull ? "OpsTrax Technician" : Get(body, "technicianName")); c.Parameters.AddWithValue("@hours", Get(body, "laborHours")); c.Parameters.AddWithValue("@rate", Get(body, "laborRate")); c.Parameters.AddWithValue("@notes", Get(body, "notes")); }, ct);
        await audit.LogAsync("workorder.labor.added", "WorkOrder", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id = laborId }, "Labor line added"));
    }

    private static async Task<IResult> WorkOrderAddPart(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var companyId = GetCompanyId(http);
        var partId = await db.InsertAsync(
            @"INSERT INTO work_order_parts (company_id, work_order_id, part_name, part_number, quantity, unit_cost, total_cost, status, notes)
              VALUES (@companyId, @id, @name, @number, COALESCE(@qty,1), COALESCE(@cost,0), COALESCE(@qty,1)*COALESCE(@cost,0), COALESCE(@status,'Reserved'), @notes)",
            c => { c.Parameters.AddWithValue("@companyId", companyId); c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@name", Get(body, "partName") is DBNull ? "Placeholder part" : Get(body, "partName")); c.Parameters.AddWithValue("@number", Get(body, "partNumber")); c.Parameters.AddWithValue("@qty", Get(body, "quantity")); c.Parameters.AddWithValue("@cost", Get(body, "unitCost")); c.Parameters.AddWithValue("@status", Get(body, "status")); c.Parameters.AddWithValue("@notes", Get(body, "notes")); }, ct);
        await audit.LogAsync("workorder.part.added", "WorkOrder", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id = partId }, "Part line added"));
    }

    private static async Task<IResult> WorkOrderComplete(HttpContext http, long id, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync("UPDATE work_orders SET status='Completed', completed_at=NOW() WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); }, ct);
        await AddWorkOrderEvent(db, id, null, "Completed", "Work order completed", ct);
        await audit.LogAsync("workorder.completed", "WorkOrder", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Work order completed"));
    }

    private static async Task<IResult> WorkOrderApproveCost(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync("UPDATE work_orders SET approved_cost=COALESCE(@cost,estimated_cost), cost_approval_status='Approved' WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); c.Parameters.AddWithValue("@cost", Get(body, "approvedCost")); }, ct);
        await audit.LogAsync("workorder.cost.approved", "WorkOrder", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Work order cost approved"));
    }

    private static Task<IResult> DvirReports(Database db, CancellationToken ct)
        => OkRows(db, DvirBaseSql + " WHERE dr.deleted_at IS NULL ORDER BY dr.submitted_at DESC", ct: ct);

    private static async Task<IResult> DvirSummary(Database db, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT COUNT(*) inspections_today,
                     SUM(inspection_type='Pre-Trip') pre_trip_completed,
                     SUM(inspection_type='Post-Trip') post_trip_completed,
                     SUM(defects_found > 0) defects_found,
                     SUM(safe_to_operate=FALSE) unsafe_vehicles,
                     SUM(mechanic_review_status='Pending') pending_mechanic_review,
                     SUM(repair_certification_status='Pending' AND defects_found > 0) pending_repair_certification,
                     SUM(driver_signature_status <> 'Signed') missing_driver_signatures,
                     CONCAT(ROUND(100 * SUM(driver_signature_status='Signed' AND safe_to_operate=TRUE) / NULLIF(COUNT(*),0),1),'%') dvir_compliance,
                     SUM(defects_found > 1) repeat_defects,
                     SUM(risk_score >= 80) critical_defects,
                     (SELECT COUNT(*) FROM work_orders WHERE dvir_report_id IS NOT NULL) work_orders_created
              FROM dvir_reports WHERE deleted_at IS NULL", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(row ?? new Dictionary<string, object?>()));
    }

    private static async Task<IResult> DvirDetail(long id, Database db, CancellationToken ct)
    {
        var record = (await db.QueryAsync(DvirBaseSql + " WHERE dr.id=@id", c => c.Parameters.AddWithValue("@id", id), ct)).FirstOrDefault();
        if (record is null) return Results.NotFound(ApiResponse<object>.Fail("DVIR report not found"));
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            record,
            defects = await db.QueryAsync("SELECT * FROM dvir_defects WHERE dvir_report_id=@id ORDER BY FIELD(severity,'Critical','Major','Minor'), id", c => c.Parameters.AddWithValue("@id", id), ct),
            checklist = await db.QueryAsync("SELECT ici.* FROM inspection_checklist_items ici JOIN dvir_templates t ON t.id=ici.template_id WHERE t.inspection_type=@type ORDER BY ici.sort_order LIMIT 30", c => c.Parameters.AddWithValue("@type", record["inspectionType"]), ct),
            workOrders = await db.QueryAsync("SELECT * FROM work_orders WHERE dvir_report_id=@id AND deleted_at IS NULL", c => c.Parameters.AddWithValue("@id", id), ct),
            timeline = await DvirTimelineRows(id, db, ct),
            recommendations = await ModuleRecommendations(db, "dvir-inspections", ct),
            auditTrail = await AuditRows(db, "DVIR", id, ct)
        }));
    }

    private static async Task<IResult> DvirTemplates(Database db, CancellationToken ct)
        => Results.Ok(ApiResponse<object>.Ok(new
        {
            templates = await db.QueryAsync("SELECT * FROM dvir_templates ORDER BY template_name", ct: ct),
            checklistItems = await db.QueryAsync("SELECT * FROM inspection_checklist_items ORDER BY template_id, sort_order", ct: ct)
        }));

    private static async Task<IResult> CreateDvirTemplate(Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var id = await db.InsertAsync("INSERT INTO dvir_templates (company_id, template_name, country_code, vehicle_type, inspection_type, status) VALUES (1, @name, @country, @vehicleType, @type, COALESCE(@status,'Active'))", c => BindDvirTemplate(c, body), ct);
        await audit.LogAsync("dvir.template.created", "DVIRTemplate", id, ct: ct);
        return Results.Created($"/api/dvir/templates/{id}", ApiResponse<object>.Ok(new { id }, "DVIR template created"));
    }

    private static async Task<IResult> UpdateDvirTemplate(long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync("UPDATE dvir_templates SET template_name=COALESCE(@name,template_name), country_code=COALESCE(@country,country_code), vehicle_type=COALESCE(@vehicleType,vehicle_type), inspection_type=COALESCE(@type,inspection_type), status=COALESCE(@status,status) WHERE id=@id", c => { c.Parameters.AddWithValue("@id", id); BindDvirTemplate(c, body); }, ct);
        await audit.LogAsync("dvir.template.updated", "DVIRTemplate", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "DVIR template updated"));
    }

    private static async Task<IResult> CreateDvirReport(Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var errors = await ValidateDvir(body, db, ct);
        if (errors.Count > 0) return Results.BadRequest(ApiResponse<object>.Fail("DVIR validation failed", errors.ToArray()));
        var id = await db.InsertAsync(
            @"INSERT INTO dvir_reports (company_id, report_number, driver_id, vehicle_id, country_code, inspection_type, inspection_status, defects_found, safe_to_operate, driver_signature_status, mechanic_review_status, repair_certification_status, submitted_at, risk_score, recommended_action, notes)
              VALUES (1, @number, @driverId, @vehicleId, COALESCE(@country,'US'), @type, COALESCE(@status,'Submitted'), COALESCE(@defects,0), COALESCE(@safe,TRUE), COALESCE(@signature,'Pending'), COALESCE(@mechanic,'Pending'), COALESCE(@repair,'Pending'), NOW(), COALESCE(@risk,35), COALESCE(@action,'Review inspection report'), @notes)",
            c => BindDvir(c, body), ct);
        await audit.LogAsync("dvir.created", "DVIR", id, ct: ct);
        return Results.Created($"/api/dvir/reports/{id}", ApiResponse<object>.Ok(new { id }, "DVIR report created"));
    }

    private static async Task<IResult> UpdateDvirReport(long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync(
            @"UPDATE dvir_reports SET report_number=COALESCE(@number,report_number), driver_id=COALESCE(@driverId,driver_id), vehicle_id=COALESCE(@vehicleId,vehicle_id),
                country_code=COALESCE(@country,country_code), inspection_type=COALESCE(@type,inspection_type), inspection_status=COALESCE(@status,inspection_status),
                defects_found=COALESCE(@defects,defects_found), safe_to_operate=COALESCE(@safe,safe_to_operate), driver_signature_status=COALESCE(@signature,driver_signature_status),
                mechanic_review_status=COALESCE(@mechanic,mechanic_review_status), repair_certification_status=COALESCE(@repair,repair_certification_status),
                risk_score=COALESCE(@risk,risk_score), recommended_action=COALESCE(@action,recommended_action), notes=COALESCE(@notes,notes) WHERE id=@id",
            c => { c.Parameters.AddWithValue("@id", id); BindDvir(c, body); }, ct);
        await audit.LogAsync("dvir.updated", "DVIR", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "DVIR report updated"));
    }

    private static async Task<IResult> DeleteDvirReport(long id, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync("UPDATE dvir_reports SET deleted_at=CURRENT_TIMESTAMP, inspection_status='Deleted' WHERE id=@id", c => c.Parameters.AddWithValue("@id", id), ct);
        await audit.LogAsync("dvir.deleted", "DVIR", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "DVIR report deleted"));
    }

    private static async Task<IResult> DvirMechanicReview(long id, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync("UPDATE dvir_reports SET mechanic_review_status='Reviewed', mechanic_reviewed_at=NOW(), inspection_status=IF(defects_found>0,'Repair Required','Reviewed') WHERE id=@id", c => c.Parameters.AddWithValue("@id", id), ct);
        await audit.LogAsync("dvir.mechanic.reviewed", "DVIR", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Mechanic review completed"));
    }

    private static async Task<IResult> DvirCertifyRepair(long id, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync("UPDATE dvir_reports SET repair_certification_status='Certified', repair_certified_at=NOW(), safe_to_operate=TRUE, inspection_status='Certified' WHERE id=@id", c => c.Parameters.AddWithValue("@id", id), ct);
        await audit.LogAsync("dvir.repair.certified", "DVIR", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Repair certified"));
    }

    private static async Task<IResult> DvirDriverSign(long id, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync("UPDATE dvir_reports SET driver_signature_status='Signed' WHERE id=@id", c => c.Parameters.AddWithValue("@id", id), ct);
        await audit.LogAsync("dvir.driver.signed", "DVIR", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Driver signed DVIR"));
    }

    private static async Task<IResult> DvirTimeline(long id, Database db, CancellationToken ct)
        => Results.Ok(ApiResponse<object>.Ok(await DvirTimelineRows(id, db, ct)));

    private static Task<List<Dictionary<string, object?>>> DvirTimelineRows(long id, Database db, CancellationToken ct)
        => db.QueryAsync(
            @"SELECT id, 'Defect' event_title, defect_description event_description, created_at occurred_at, severity FROM dvir_defects WHERE dvir_report_id=@id
              UNION ALL SELECT id, action_name, actor_name, created_at, 'Audit' FROM audit_logs WHERE entity_name='DVIR' AND entity_id=@id
              ORDER BY occurred_at DESC LIMIT 20", c => c.Parameters.AddWithValue("@id", id), ct);

    private static Task<IResult> Documents(Database db, CancellationToken ct)
        => OkRows(db, DocumentsBaseSql + " WHERE d.deleted_at IS NULL ORDER BY d.expires_at, d.id DESC", ct: ct);

    private static async Task<IResult> DocumentsSummary(Database db, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT COUNT(*) total_documents,
                     SUM(status='Expiring' OR expires_at BETWEEN CURDATE() AND DATE_ADD(CURDATE(), INTERVAL 30 DAY)) expiring_soon,
                     SUM(status='Expired' OR expires_at < CURDATE()) expired,
                     SUM(risk_score >= 80) missing_critical_documents,
                     SUM(entity_type='vehicle') vehicle_documents,
                     SUM(entity_type='driver') driver_documents,
                     SUM(category LIKE '%Compliance%' OR document_type LIKE '%Inspection%') compliance_documents,
                     SUM(renewal_status='Renewal Required') pending_renewal,
                     SUM(created_at >= DATE_SUB(NOW(), INTERVAL 30 DAY)) uploaded_this_month,
                     SUM(category LIKE '%Audit%') audit_package_documents,
                     SUM(country_code <> 'US') cross_border_missing_docs,
                     CONCAT(ROUND(100 - AVG(LEAST(risk_score,95)),1),'%') data_completeness_score
              FROM documents WHERE deleted_at IS NULL", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(row ?? new Dictionary<string, object?>()));
    }

    private static async Task<IResult> DocumentDetail(long id, Database db, CancellationToken ct)
    {
        var record = (await db.QueryAsync(DocumentsBaseSql + " WHERE d.id=@id", c => c.Parameters.AddWithValue("@id", id), ct)).FirstOrDefault();
        if (record is null) return Results.NotFound(ApiResponse<object>.Fail("Document not found"));
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            record,
            timeline = await db.QueryAsync("SELECT * FROM document_timeline_events WHERE document_id=@id ORDER BY occurred_at DESC LIMIT 20", c => c.Parameters.AddWithValue("@id", id), ct),
            recommendations = await ModuleRecommendations(db, "documents", ct),
            auditTrail = await AuditRows(db, "Document", id, ct)
        }));
    }

    private static async Task<IResult> CreateDocument(Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var errors = ValidateDocument(body);
        if (errors.Count > 0) return Results.BadRequest(ApiResponse<object>.Fail("Document validation failed", errors.ToArray()));
        var id = await db.InsertAsync(
            @"INSERT INTO documents (company_id, title, document_number, entity_type, entity_id, document_type, category, country_code, issuing_authority, issued_at, expires_at, status, renewal_status, file_url, risk_score, recommended_action, notes)
              VALUES (1, @title, @number, @entityType, @entityId, @type, @category, @country, @authority, @issued, @expires, COALESCE(@status,'Active'), COALESCE(@renewal,'Current'), @file, COALESCE(@risk,25), COALESCE(@action,'Keep active in vault'), @notes)",
            c => BindDocument(c, body), ct);
        await audit.LogAsync("document.created", "Document", id, ct: ct);
        await AddDocumentEvent(db, id, "Document created", "Document metadata entered into vault", ct);
        return Results.Created($"/api/documents/{id}", ApiResponse<object>.Ok(new { id }, "Document created"));
    }

    private static async Task<IResult> UpdateDocument(long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync(
            @"UPDATE documents SET title=COALESCE(@title,title), document_number=COALESCE(@number,document_number), entity_type=COALESCE(@entityType,entity_type),
                entity_id=COALESCE(@entityId,entity_id), document_type=COALESCE(@type,document_type), category=COALESCE(@category,category), country_code=COALESCE(@country,country_code),
                issuing_authority=COALESCE(@authority,issuing_authority), issued_at=COALESCE(@issued,issued_at), expires_at=COALESCE(@expires,expires_at), status=COALESCE(@status,status),
                renewal_status=COALESCE(@renewal,renewal_status), file_url=COALESCE(@file,file_url), risk_score=COALESCE(@risk,risk_score), recommended_action=COALESCE(@action,recommended_action), notes=COALESCE(@notes,notes)
              WHERE id=@id", c => { c.Parameters.AddWithValue("@id", id); BindDocument(c, body); }, ct);
        await audit.LogAsync("document.updated", "Document", id, ct: ct);
        await AddDocumentEvent(db, id, "Document updated", "Document metadata updated", ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Document updated"));
    }

    private static async Task<IResult> DocumentUploadPlaceholder(Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>(body, StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = body.TryGetValue("title", out var title) ? title : "Uploaded placeholder document",
            ["documentNumber"] = body.TryGetValue("documentNumber", out var number) ? number : $"DOC-UP-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            ["fileUrl"] = "/placeholder/uploaded-document.pdf"
        };
        var result = await CreateDocument(payload, db, audit, ct);
        await audit.LogAsync("document.upload.placeholder", "Document", null, ct: ct);
        return result;
    }

    private static async Task<IResult> DocumentRenewPlaceholder(long id, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync("UPDATE documents SET renewal_status='Renewal Queued', status='Expiring', recommended_action='Renewal queued by OpsTrax advisor' WHERE id=@id", c => c.Parameters.AddWithValue("@id", id), ct);
        await AddDocumentEvent(db, id, "Renewal queued", "Document renewal placeholder triggered", ct);
        await audit.LogAsync("document.renewal.placeholder", "Document", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Document renewal queued"));
    }

    private static async Task<IResult> DocumentTimeline(long id, Database db, CancellationToken ct)
        => Results.Ok(ApiResponse<object>.Ok(await db.QueryAsync("SELECT * FROM document_timeline_events WHERE document_id=@id ORDER BY occurred_at DESC", c => c.Parameters.AddWithValue("@id", id), ct)));

    private const string SafetySql = @"SELECT se.*, d.full_name driver_name, v.vehicle_code, COALESCE(j.job_number,j.job_code) job_number, r.route_code
        FROM safety_events se LEFT JOIN drivers d ON d.id=se.driver_id LEFT JOIN vehicles v ON v.id=se.vehicle_id
        LEFT JOIN jobs j ON j.id=se.job_id LEFT JOIN routes r ON r.id=se.route_id";
    private const string DashcamSql = @"SELECT de.*, d.full_name driver_name, v.vehicle_code, COALESCE(j.job_number,j.job_code) job_number, r.route_code
        FROM dashcam_events de LEFT JOIN drivers d ON d.id=de.driver_id LEFT JOIN vehicles v ON v.id=de.vehicle_id
        LEFT JOIN jobs j ON j.id=de.job_id LEFT JOIN routes r ON r.id=de.route_id";
    private const string CoachingSql = @"SELECT ct.*, d.full_name driver_name, u.full_name assigned_to_name, se.event_number safety_event_number, de.event_number dashcam_event_number
        FROM coaching_tasks ct LEFT JOIN drivers d ON d.id=ct.driver_id LEFT JOIN users u ON u.id=ct.assigned_to_user_id
        LEFT JOIN safety_events se ON se.id=ct.safety_event_id LEFT JOIN dashcam_events de ON de.id=ct.dashcam_event_id";
    private const string IncidentSql = @"SELECT i.*, d.full_name driver_name, v.vehicle_code, se.event_number safety_event_number, de.event_number dashcam_event_number, COALESCE(j.job_number,j.job_code) job_number, r.route_code
        FROM incidents i LEFT JOIN drivers d ON d.id=i.driver_id LEFT JOIN vehicles v ON v.id=i.vehicle_id
        LEFT JOIN safety_events se ON se.id=i.safety_event_id LEFT JOIN dashcam_events de ON de.id=i.dashcam_event_id
        LEFT JOIN jobs j ON j.id=i.job_id LEFT JOIN routes r ON r.id=i.route_id";
    private const string EvidenceSql = @"SELECT ep.*, i.incident_number, d.full_name driver_name, v.vehicle_code, se.event_number safety_event_number, de.event_number dashcam_event_number
        FROM evidence_packages ep LEFT JOIN incidents i ON i.id=ep.incident_id LEFT JOIN drivers d ON d.id=ep.driver_id LEFT JOIN vehicles v ON v.id=ep.vehicle_id
        LEFT JOIN safety_events se ON se.id=ep.safety_event_id LEFT JOIN dashcam_events de ON de.id=ep.dashcam_event_id";

    private static async Task<IResult> SafetySummary(Database db, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(@"SELECT ROUND(100-AVG(LEAST(risk_score,95)),1) fleet_safety_score, COUNT(*) safety_events_today,
            SUM(severity='Critical') critical_events, SUM(event_type='Harsh Braking') harsh_braking, SUM(event_type='Harsh Acceleration') harsh_acceleration,
            SUM(event_type='Speeding') speeding_events, SUM(event_type='Route Deviation') route_deviation, SUM(event_type LIKE '%Distracted%') distracted_driving_placeholder,
            SUM(coaching_status IN ('Needed','Created')) coaching_needed, SUM(incident_status='Open') open_incidents, SUM(review_status='Reviewed') reviewed_events,
            ROUND(AVG(risk_score),1) preventable_risk_score FROM safety_events WHERE deleted_at IS NULL", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(row ?? new Dictionary<string, object?>()));
    }
    private static Task<IResult> SafetyEvents(Database db, CancellationToken ct) => OkRows(db, SafetySql + " WHERE se.deleted_at IS NULL ORDER BY se.occurred_at DESC", ct: ct);
    private static async Task<IResult> SafetyEventDetail(long id, Database db, CancellationToken ct)
    {
        var record = (await db.QueryAsync(SafetySql + " WHERE se.id=@id", c => c.Parameters.AddWithValue("@id", id), ct)).FirstOrDefault();
        if (record is null) return Results.NotFound(ApiResponse<object>.Fail("Safety event not found"));
        return Results.Ok(ApiResponse<object>.Ok(new { record,
            dashcamEvents = await db.QueryAsync("SELECT * FROM dashcam_events WHERE safety_event_id=@id AND deleted_at IS NULL", c => c.Parameters.AddWithValue("@id", id), ct),
            coachingTasks = await db.QueryAsync("SELECT * FROM coaching_tasks WHERE safety_event_id=@id AND deleted_at IS NULL", c => c.Parameters.AddWithValue("@id", id), ct),
            incidents = await db.QueryAsync("SELECT * FROM incidents WHERE safety_event_id=@id AND deleted_at IS NULL", c => c.Parameters.AddWithValue("@id", id), ct),
            recommendations = await ModuleRecommendations(db, "safety", ct), auditTrail = await AuditRows(db, "SafetyEvent", id, ct) }));
    }
    private static async Task<IResult> CreateSafetyEvent(HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        if (IsBlank(Get(body, "eventType")) || IsBlank(Get(body, "severity"))) return Results.BadRequest(ApiResponse<object>.Fail("Safety event validation failed", ["Event type and severity are required."]));
        var companyId = GetCompanyId(http);
        var id = await db.InsertAsync(@"INSERT INTO safety_events (company_id,event_number,event_type,severity,driver_id,vehicle_id,job_id,route_id,location_description,speed,posted_speed_limit,occurred_at,review_status,coaching_status,incident_status,risk_score,ai_summary,recommended_action)
            VALUES (@companyId,@number,@type,@severity,@driver,@vehicle,@job,@route,@location,@speed,@limit,COALESCE(@occurred,NOW()),COALESCE(@review,'New'),COALESCE(@coaching,'Not Created'),COALESCE(@incident,'None'),COALESCE(@risk,40),@summary,@action)", c => { c.Parameters.AddWithValue("@companyId", companyId); BindSafety(c, body); }, ct);
        await audit.LogAsync("safety.event.created", "SafetyEvent", id, ct: ct);
        return Results.Created($"/api/safety/events/{id}", ApiResponse<object>.Ok(new { id }, "Safety event created"));
    }
    private static async Task<IResult> UpdateSafetyEvent(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync(@"UPDATE safety_events SET event_type=COALESCE(@type,event_type), severity=COALESCE(@severity,severity), driver_id=COALESCE(@driver,driver_id), vehicle_id=COALESCE(@vehicle,vehicle_id), job_id=COALESCE(@job,job_id), route_id=COALESCE(@route,route_id), location_description=COALESCE(@location,location_description), speed=COALESCE(@speed,speed), posted_speed_limit=COALESCE(@limit,posted_speed_limit), review_status=COALESCE(@review,review_status), coaching_status=COALESCE(@coaching,coaching_status), incident_status=COALESCE(@incident,incident_status), risk_score=COALESCE(@risk,risk_score), ai_summary=COALESCE(@summary,ai_summary), recommended_action=COALESCE(@action,recommended_action) WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); BindSafety(c, body); }, ct);
        await audit.LogAsync("safety.event.updated", "SafetyEvent", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Safety event updated"));
    }
    private static async Task<IResult> SafetyReview(HttpContext http, long id, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync("UPDATE safety_events SET review_status='Reviewed' WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); }, ct);
        await audit.LogAsync("safety.event.reviewed", "SafetyEvent", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Safety event reviewed"));
    }
    private static async Task<IResult> SafetyCreateCoaching(HttpContext http, long id, Database db, AuditService audit, CancellationToken ct)
    {
        var companyId = GetCompanyId(http);
        var ev = await db.QuerySingleAsync("SELECT * FROM safety_events WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        if (ev is null) return Results.NotFound(ApiResponse<object>.Fail("Safety event not found"));
        var taskId = await InsertCoaching(http, db, ev["driverId"], id, null, "Safety Event Coaching", ev["severity"], ct);
        await db.ExecuteAsync("UPDATE safety_events SET coaching_status='Created' WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        await audit.LogAsync("safety.event.coaching.created", "SafetyEvent", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id = taskId }, "Coaching task created"));
    }
    private static async Task<IResult> SafetyCreateIncident(HttpContext http, long id, Database db, AuditService audit, CancellationToken ct)
    {
        var companyId = GetCompanyId(http);
        var ev = await db.QuerySingleAsync("SELECT * FROM safety_events WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        if (ev is null) return Results.NotFound(ApiResponse<object>.Fail("Safety event not found"));
        var incidentId = await InsertIncident(http, db, ev, null, ct);
        await db.ExecuteAsync("UPDATE safety_events SET incident_status='Open' WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        await audit.LogAsync("safety.event.incident.created", "SafetyEvent", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id = incidentId }, "Incident created"));
    }

    private static async Task<IResult> DashcamSummary(Database db, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(@"SELECT COUNT(*) dashcam_events_today, SUM(severity='Critical') critical_video_events, SUM(review_status LIKE '%Pending%') pending_review,
            SUM(review_status='Reviewed') reviewed_events, SUM(false_positive=TRUE) false_positives, SUM(coaching_status='Created') coaching_created,
            SUM(evidence_status='Packaged') evidence_packages, SUM(event_type LIKE '%Collision%' OR event_type LIKE '%Near Miss%') collision_near_miss,
            SUM(event_type LIKE '%Distracted%') distracted_driving_placeholder, SUM(event_type LIKE '%Tailgating%') tailgating_placeholder,
            SUM(event_type LIKE '%Speeding%') speeding_video_events, SUM(recommended_action LIKE '%exoneration%') driver_exoneration_placeholder FROM dashcam_events WHERE deleted_at IS NULL", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(row ?? new Dictionary<string, object?>()));
    }
    private static Task<IResult> DashcamEvents(Database db, CancellationToken ct) => OkRows(db, DashcamSql + " WHERE de.deleted_at IS NULL ORDER BY de.occurred_at DESC", ct: ct);
    private static async Task<IResult> DashcamEventDetail(long id, Database db, CancellationToken ct)
    {
        var record = (await db.QueryAsync(DashcamSql + " WHERE de.id=@id", c => c.Parameters.AddWithValue("@id", id), ct)).FirstOrDefault();
        if (record is null) return Results.NotFound(ApiResponse<object>.Fail("Dashcam event not found"));
        return Results.Ok(ApiResponse<object>.Ok(new { record,
            coachingTasks = await db.QueryAsync("SELECT * FROM coaching_tasks WHERE dashcam_event_id=@id AND deleted_at IS NULL", c => c.Parameters.AddWithValue("@id", id), ct),
            evidencePackages = await db.QueryAsync("SELECT * FROM evidence_packages WHERE dashcam_event_id=@id AND deleted_at IS NULL", c => c.Parameters.AddWithValue("@id", id), ct),
            recommendations = await ModuleRecommendations(db, "dashcam", ct), auditTrail = await AuditRows(db, "DashcamEvent", id, ct) }));
    }
    private static async Task<IResult> CreateDashcamEvent(HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        if (IsBlank(Get(body, "eventType")) || IsBlank(Get(body, "severity"))) return Results.BadRequest(ApiResponse<object>.Fail("Dashcam event validation failed", ["Event type and severity are required."]));
        var companyId = GetCompanyId(http);
        var id = await db.InsertAsync(@"INSERT INTO dashcam_events (company_id,event_number,safety_event_id,event_type,title,severity,driver_id,vehicle_id,job_id,route_id,location_description,thumbnail_url,road_facing_clip_url,driver_facing_clip_url,ai_summary,ai_confidence,review_status,evidence_status,recommended_action,occurred_at)
            VALUES (@companyId,@number,@safety,@type,@title,@severity,@driver,@vehicle,@job,@route,@location,'/placeholder/dashcam-thumb.jpg','/placeholder/road-facing.mp4','/placeholder/driver-facing.mp4',@summary,COALESCE(@confidence,85),COALESCE(@review,'Pending Review'),COALESCE(@evidence,'Not Packaged'),@action,COALESCE(@occurred,NOW()))", c => { c.Parameters.AddWithValue("@companyId", companyId); BindDashcam(c, body); }, ct);
        await audit.LogAsync("dashcam.event.created", "DashcamEvent", id, ct: ct);
        return Results.Created($"/api/dashcam/events/{id}", ApiResponse<object>.Ok(new { id }, "Dashcam event created"));
    }
    private static async Task<IResult> UpdateDashcamEvent(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync(@"UPDATE dashcam_events SET event_type=COALESCE(@type,event_type), title=COALESCE(@title,title), severity=COALESCE(@severity,severity), driver_id=COALESCE(@driver,driver_id), vehicle_id=COALESCE(@vehicle,vehicle_id), review_status=COALESCE(@review,review_status), evidence_status=COALESCE(@evidence,evidence_status), ai_summary=COALESCE(@summary,ai_summary), ai_confidence=COALESCE(@confidence,ai_confidence), recommended_action=COALESCE(@action,recommended_action) WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); BindDashcam(c, body); }, ct);
        await audit.LogAsync("dashcam.event.updated", "DashcamEvent", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Dashcam event updated"));
    }
    private static async Task<IResult> DashcamReview(HttpContext http, long id, Database db, AuditService audit, CancellationToken ct) { await db.ExecuteAsync("UPDATE dashcam_events SET review_status='Reviewed' WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); }, ct); await audit.LogAsync("dashcam.event.reviewed", "DashcamEvent", id, ct: ct); return Results.Ok(ApiResponse<object>.Ok(new { id }, "Dashcam event reviewed")); }
    private static async Task<IResult> DashcamFalsePositive(HttpContext http, long id, Database db, AuditService audit, CancellationToken ct) { await db.ExecuteAsync("UPDATE dashcam_events SET false_positive=TRUE, review_status='False Positive' WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); }, ct); await audit.LogAsync("dashcam.false_positive", "DashcamEvent", id, ct: ct); return Results.Ok(ApiResponse<object>.Ok(new { id }, "Marked false positive")); }
    private static async Task<IResult> DashcamCreateCoaching(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var companyId = GetCompanyId(http);
        var ev = await db.QuerySingleAsync("SELECT * FROM dashcam_events WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        if (ev is null) return Results.NotFound(ApiResponse<object>.Fail("Dashcam event not found"));
        if (Convert.ToBoolean(ev["falsePositive"] ?? false) && !string.Equals(Get(body, "override")?.ToString(), "true", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest(ApiResponse<object>.Fail("False positive dashcam events cannot create coaching without override."));
        var taskId = await InsertCoaching(http, db, ev["driverId"], null, id, "Video Coaching", ev["severity"], ct);
        await db.ExecuteAsync("UPDATE dashcam_events SET coaching_status='Created' WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        await audit.LogAsync("dashcam.coaching.created", "DashcamEvent", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id = taskId }, "Coaching task created"));
    }
    private static async Task<IResult> DashcamCreateEvidencePackage(HttpContext http, long id, Database db, AuditService audit, CancellationToken ct)
    {
        var companyId = GetCompanyId(http);
        var ev = await db.QuerySingleAsync("SELECT * FROM dashcam_events WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        if (ev is null) return Results.NotFound(ApiResponse<object>.Fail("Dashcam event not found"));
        var packageId = await InsertEvidencePackage(http, db, null, ev["safetyEventId"], id, ev["driverId"], ev["vehicleId"], ev["jobId"], ct);
        await db.ExecuteAsync("UPDATE dashcam_events SET evidence_status='Packaged' WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        await audit.LogAsync("dashcam.evidence_package.created", "DashcamEvent", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id = packageId }, "Evidence package created"));
    }
    private static async Task<IResult> DashcamCreateIncidentReport(HttpContext http, long id, Database db, AuditService audit, CancellationToken ct)
    {
        var companyId = GetCompanyId(http);
        var ev = await db.QuerySingleAsync("SELECT * FROM dashcam_events WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", companyId); }, ct);
        if (ev is null) return Results.NotFound(ApiResponse<object>.Fail("Dashcam event not found"));
        var incidentId = await InsertIncident(http, db, null, ev, ct);
        var reportId = await InsertInsuranceReport(http, db, incidentId, null, ct);
        await audit.LogAsync("dashcam.insurance_report.created", "DashcamEvent", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { incidentId, reportId }, "Incident report created"));
    }

    private static async Task<IResult> CoachingSummary(Database db, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(@"SELECT SUM(status NOT IN ('Completed','Cancelled')) open_coaching_tasks, SUM(priority='Critical') critical_coaching,
            SUM(status='Assigned') assigned_tasks, SUM(driver_acknowledged=TRUE) driver_acknowledged, SUM(status='Completed' AND completed_at >= DATE_SUB(NOW(), INTERVAL 30 DAY)) completed_this_month,
            SUM(due_at < NOW() AND status NOT IN ('Completed','Cancelled')) overdue_coaching, COUNT(DISTINCT CASE WHEN risk.driver_count>1 THEN ct.driver_id END) repeat_coaching_drivers,
            SUM(after_safety_score > before_safety_score) safety_score_improved, SUM(status='Escalated') escalated_coaching, CONCAT(ROUND(AVG(TIMESTAMPDIFF(HOUR, created_at, COALESCE(completed_at,NOW()))),1),'h') average_completion_time
            FROM coaching_tasks ct LEFT JOIN (SELECT driver_id, COUNT(*) driver_count FROM coaching_tasks GROUP BY driver_id) risk ON risk.driver_id=ct.driver_id WHERE ct.deleted_at IS NULL", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(row ?? new Dictionary<string, object?>()));
    }
    private static Task<IResult> CoachingTasks(Database db, CancellationToken ct) => OkRows(db, CoachingSql + " WHERE ct.deleted_at IS NULL ORDER BY FIELD(ct.priority,'Critical','High','Medium','Low'), ct.due_at", ct: ct);
    private static async Task<IResult> CoachingTaskDetail(long id, Database db, CancellationToken ct)
    {
        var record = (await db.QueryAsync(CoachingSql + " WHERE ct.id=@id", c => c.Parameters.AddWithValue("@id", id), ct)).FirstOrDefault();
        if (record is null) return Results.NotFound(ApiResponse<object>.Fail("Coaching task not found"));
        return Results.Ok(ApiResponse<object>.Ok(new { record,
            notes = await db.QueryAsync("SELECT * FROM coaching_notes WHERE coaching_task_id=@id ORDER BY created_at DESC", c => c.Parameters.AddWithValue("@id", id), ct),
            relatedSafetyEvents = await db.QueryAsync("SELECT * FROM safety_events WHERE id=@id", c => c.Parameters.AddWithValue("@id", record["safetyEventId"]), ct),
            relatedDashcamEvents = await db.QueryAsync("SELECT * FROM dashcam_events WHERE id=@id", c => c.Parameters.AddWithValue("@id", record["dashcamEventId"]), ct),
            recommendations = await ModuleRecommendations(db, "coaching", ct), auditTrail = await AuditRows(db, "CoachingTask", id, ct) }));
    }
    private static async Task<IResult> CreateCoachingTask(HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        if (IsBlank(Get(body, "driverId"))) return Results.BadRequest(ApiResponse<object>.Fail("Coaching task requires driver."));
        var id = await InsertCoaching(http, db, Get(body, "driverId"), Get(body, "safetyEventId"), Get(body, "dashcamEventId"), Get(body, "coachingType")?.ToString() ?? "Safety Coaching", Get(body, "priority"), ct);
        await audit.LogAsync("coaching.created", "CoachingTask", id, ct: ct);
        return Results.Created($"/api/coaching/tasks/{id}", ApiResponse<object>.Ok(new { id }, "Coaching task created"));
    }
    private static async Task<IResult> UpdateCoachingTask(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync(@"UPDATE coaching_tasks SET assigned_to_user_id=COALESCE(@assigned,assigned_to_user_id), coaching_type=COALESCE(@type,coaching_type), priority=COALESCE(@priority,priority), status=COALESCE(@status,status), title=COALESCE(@title,title), description=COALESCE(@description,description), ai_script=COALESCE(@script,ai_script), due_at=COALESCE(@due,due_at) WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); BindCoaching(c, body); }, ct);
        await audit.LogAsync("coaching.updated", "CoachingTask", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Coaching task updated"));
    }
    private static async Task<IResult> CoachingAssign(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) { await db.ExecuteAsync("UPDATE coaching_tasks SET assigned_to_user_id=@assigned, status='Assigned' WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); c.Parameters.AddWithValue("@assigned", Get(body, "assignedToUserId") is DBNull ? 1 : Get(body, "assignedToUserId")); }, ct); await audit.LogAsync("coaching.assigned", "CoachingTask", id, ct: ct); return Results.Ok(ApiResponse<object>.Ok(new { id }, "Coaching assigned")); }
    private static async Task<IResult> CoachingAcknowledge(HttpContext http, long id, Database db, AuditService audit, CancellationToken ct) { await db.ExecuteAsync("UPDATE coaching_tasks SET driver_acknowledged=TRUE, acknowledged_at=NOW(), status='Driver Acknowledged' WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); }, ct); await audit.LogAsync("coaching.acknowledged", "CoachingTask", id, ct: ct); return Results.Ok(ApiResponse<object>.Ok(new { id }, "Coaching acknowledged")); }
    private static async Task<IResult> CoachingComplete(HttpContext http, long id, Database db, AuditService audit, CancellationToken ct) { await db.ExecuteAsync("UPDATE coaching_tasks SET status='Completed', completed_at=NOW(), after_safety_score=COALESCE(after_safety_score,before_safety_score+6), effectiveness_score=COALESCE(effectiveness_score,88) WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); }, ct); await audit.LogAsync("coaching.completed", "CoachingTask", id, ct: ct); return Results.Ok(ApiResponse<object>.Ok(new { id }, "Coaching completed")); }
    private static async Task<IResult> CoachingAddNote(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var noteId = await db.InsertAsync("INSERT INTO coaching_notes (company_id, coaching_task_id, note_type, note_text, created_by_user_id) VALUES (@companyId,@id,COALESCE(@type,'Manager Note'),@text,@userId)", c => { c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); c.Parameters.AddWithValue("@userId", http.Items[AuthUserIdItemKey] ?? 1); c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@type", Get(body, "noteType")); c.Parameters.AddWithValue("@text", Get(body, "noteText") is DBNull ? "Coaching note placeholder." : Get(body, "noteText")); }, ct);
        await audit.LogAsync("coaching.note.added", "CoachingTask", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id = noteId }, "Coaching note added"));
    }

    private static async Task<IResult> IncidentsSummary(Database db, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(@"SELECT COUNT(*) total_incidents, SUM(status NOT IN ('Closed','Deleted')) open_incidents, SUM(status='Closed') closed_incidents,
            SUM(severity='Critical') critical_incidents, SUM(status='Insurance Report Ready') insurance_ready, SUM(status='Awaiting Driver Statement') awaiting_driver_statement,
            SUM(insurance_report_status <> 'Not Created') insurance_reports, SUM(status='Evidence Collected') evidence_collected FROM incidents WHERE deleted_at IS NULL", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(row ?? new Dictionary<string, object?>()));
    }
    private static Task<IResult> Incidents(Database db, CancellationToken ct) => OkRows(db, IncidentSql + " WHERE i.deleted_at IS NULL ORDER BY i.occurred_at DESC", ct: ct);
    private static async Task<IResult> IncidentDetail(long id, Database db, CancellationToken ct)
    {
        var record = (await db.QueryAsync(IncidentSql + " WHERE i.id=@id", c => c.Parameters.AddWithValue("@id", id), ct)).FirstOrDefault();
        if (record is null) return Results.NotFound(ApiResponse<object>.Fail("Incident not found"));
        return Results.Ok(ApiResponse<object>.Ok(new { record,
            evidence = await db.QueryAsync("SELECT * FROM incident_evidence WHERE incident_id=@id ORDER BY created_at DESC", c => c.Parameters.AddWithValue("@id", id), ct),
            packages = await db.QueryAsync("SELECT * FROM evidence_packages WHERE incident_id=@id AND deleted_at IS NULL", c => c.Parameters.AddWithValue("@id", id), ct),
            insuranceReports = await db.QueryAsync("SELECT * FROM insurance_reports WHERE incident_id=@id", c => c.Parameters.AddWithValue("@id", id), ct),
            timeline = await IncidentTimelineRows(id, db, ct), recommendations = await ModuleRecommendations(db, "incidents", ct), auditTrail = await AuditRows(db, "Incident", id, ct) }));
    }
    private static async Task<IResult> CreateIncident(HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        if (IsBlank(Get(body, "incidentType")) || IsBlank(Get(body, "severity")) || IsBlank(Get(body, "status"))) return Results.BadRequest(ApiResponse<object>.Fail("Incident requires type, severity and status."));
        var id = await InsertIncident(http, db, body, null, ct);
        await audit.LogAsync("incident.created", "Incident", id, ct: ct);
        return Results.Created($"/api/incidents/{id}", ApiResponse<object>.Ok(new { id }, "Incident created"));
    }
    private static async Task<IResult> UpdateIncident(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync(@"UPDATE incidents SET incident_type=COALESCE(@type,incident_type), severity=COALESCE(@severity,severity), status=COALESCE(@status,status), driver_statement=COALESCE(@driverStatement,driver_statement), witness_statement=COALESCE(@witness,witness_statement), customer_statement=COALESCE(@customer,customer_statement), recommended_action=COALESCE(@action,recommended_action) WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); BindIncident(c, body); }, ct);
        await audit.LogAsync("incident.updated", "Incident", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Incident updated"));
    }
    private static async Task<IResult> IncidentStatus(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct) { var status = Get(body, "status")?.ToString() ?? "Under Review"; await db.ExecuteAsync("UPDATE incidents SET status=@status WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); c.Parameters.AddWithValue("@status", status); }, ct); await audit.LogAsync("incident.status.changed", "Incident", id, ct: ct); return Results.Ok(ApiResponse<object>.Ok(new { id, status }, "Incident status updated")); }
    private static async Task<IResult> IncidentAttachEvidence(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var evidenceId = await db.InsertAsync("INSERT INTO incident_evidence (company_id, incident_id, evidence_type, evidence_title, evidence_url, evidence_json, source_entity_type, source_entity_id) VALUES (@companyId,@id,COALESCE(@type,'Document'),COALESCE(@title,'Evidence placeholder'),@url,JSON_OBJECT('placeholder',true),@sourceType,@sourceId)", c => { c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@type", Get(body, "evidenceType")); c.Parameters.AddWithValue("@title", Get(body, "evidenceTitle")); c.Parameters.AddWithValue("@url", Get(body, "evidenceUrl")); c.Parameters.AddWithValue("@sourceType", Get(body, "sourceEntityType")); c.Parameters.AddWithValue("@sourceId", Get(body, "sourceEntityId")); }, ct);
        await audit.LogAsync("incident.evidence.attached", "Incident", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id = evidenceId }, "Evidence attached"));
    }
    private static async Task<IResult> IncidentCreateInsuranceReport(HttpContext http, long id, Database db, AuditService audit, CancellationToken ct) { var reportId = await InsertInsuranceReport(http, db, id, null, ct); await db.ExecuteAsync("UPDATE incidents SET insurance_report_status='Ready', status='Insurance Report Ready' WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); }, ct); await audit.LogAsync("insurance.report.created", "Incident", id, ct: ct); return Results.Ok(ApiResponse<object>.Ok(new { id = reportId }, "Insurance report created")); }
    private static async Task<IResult> IncidentTimeline(long id, Database db, CancellationToken ct) => Results.Ok(ApiResponse<object>.Ok(await IncidentTimelineRows(id, db, ct)));
    private static Task<List<Dictionary<string, object?>>> IncidentTimelineRows(long id, Database db, CancellationToken ct) => db.QueryAsync("SELECT id, evidence_title title, evidence_type event_type, created_at event_time FROM incident_evidence WHERE incident_id=@id UNION ALL SELECT id, action_name, 'Audit', created_at FROM audit_logs WHERE entity_name='Incident' AND entity_id=@id ORDER BY event_time DESC", c => c.Parameters.AddWithValue("@id", id), ct);

    private static async Task<IResult> EvidenceSummary(Database db, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(@"SELECT COUNT(*) total_packages, SUM(status='Draft') draft_packages, SUM(status='Export Ready') export_ready,
            SUM(locked=TRUE) locked_packages, SUM(package_type LIKE '%Insurance%') insurance_packages, SUM(export_url IS NOT NULL) exports_generated FROM evidence_packages WHERE deleted_at IS NULL", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(row ?? new Dictionary<string, object?>()));
    }
    private static Task<IResult> EvidencePackages(Database db, CancellationToken ct) => OkRows(db, EvidenceSql + " WHERE ep.deleted_at IS NULL ORDER BY ep.created_at DESC", ct: ct);
    private static async Task<IResult> EvidencePackageDetail(long id, Database db, CancellationToken ct)
    {
        var record = (await db.QueryAsync(EvidenceSql + " WHERE ep.id=@id", c => c.Parameters.AddWithValue("@id", id), ct)).FirstOrDefault();
        if (record is null) return Results.NotFound(ApiResponse<object>.Fail("Evidence package not found"));
        return Results.Ok(ApiResponse<object>.Ok(new { record,
            items = await db.QueryAsync("SELECT * FROM evidence_package_items WHERE evidence_package_id=@id ORDER BY created_at", c => c.Parameters.AddWithValue("@id", id), ct),
            recommendations = await ModuleRecommendations(db, "evidence-packages", ct), auditTrail = await AuditRows(db, "EvidencePackage", id, ct) }));
    }
    private static async Task<IResult> CreateEvidencePackage(HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        if (IsBlank(Get(body, "incidentId")) && IsBlank(Get(body, "safetyEventId")) && IsBlank(Get(body, "dashcamEventId"))) return Results.BadRequest(ApiResponse<object>.Fail("Evidence package must reference incident, safety event or dashcam event."));
        var id = await InsertEvidencePackage(http, db, Get(body, "incidentId"), Get(body, "safetyEventId"), Get(body, "dashcamEventId"), Get(body, "driverId"), Get(body, "vehicleId"), Get(body, "jobId"), ct);
        await audit.LogAsync("evidence.package.created", "EvidencePackage", id, ct: ct);
        return Results.Created($"/api/evidence-packages/{id}", ApiResponse<object>.Ok(new { id }, "Evidence package created"));
    }
    private static async Task<IResult> UpdateEvidencePackage(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var locked = await db.ScalarLongAsync("SELECT COUNT(*) FROM evidence_packages WHERE id=@id AND locked=TRUE", c => c.Parameters.AddWithValue("@id", id), ct);
        if (locked > 0 && !string.Equals(Get(body, "override")?.ToString(), "true", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest(ApiResponse<object>.Fail("Locked evidence package cannot be modified without override."));
        await db.ExecuteAsync("UPDATE evidence_packages SET status=COALESCE(@status,status), summary=COALESCE(@summary,summary) WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); c.Parameters.AddWithValue("@status", Get(body, "status")); c.Parameters.AddWithValue("@summary", Get(body, "summary")); }, ct);
        await audit.LogAsync("evidence.package.updated", "EvidencePackage", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Evidence package updated"));
    }
    private static async Task<IResult> EvidenceExport(HttpContext http, long id, Database db, AuditService audit, CancellationToken ct) { await db.ExecuteAsync("UPDATE evidence_packages SET status='Export Ready', export_url='/placeholder/evidence-export.pdf' WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); }, ct); await audit.LogAsync("evidence.package.export.generated", "EvidencePackage", id, ct: ct); return Results.Ok(ApiResponse<object>.Ok(new { id, exportUrl = "/placeholder/evidence-export.pdf" }, "Export placeholder generated")); }
    private static async Task<IResult> EvidenceLock(HttpContext http, long id, Database db, AuditService audit, CancellationToken ct) { await db.ExecuteAsync("UPDATE evidence_packages SET locked=TRUE, status='Locked' WHERE id=@id AND company_id=@companyId", c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); }, ct); await audit.LogAsync("evidence.package.locked", "EvidencePackage", id, ct: ct); return Results.Ok(ApiResponse<object>.Ok(new { id }, "Evidence package locked")); }

    private static async Task<IResult> AiAsk(Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var prompt = Get(body, "prompt")?.ToString() ?? "Executive Summary";
        var evidence = await db.QueryAsync("SELECT title, body, severity FROM ai_insights ORDER BY created_at DESC LIMIT 4", ct: ct);
        var actions = await db.QueryAsync("SELECT title, priority, status FROM command_center_actions ORDER BY FIELD(priority,'Critical','High','Medium','Low') LIMIT 4", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            summary = $"OpsTrax AI reviewed {prompt}. Current operational risk is concentrated around dispatch exceptions, idling cost, maintenance timing, and SLA-sensitive customer ETAs.",
            evidence,
            recommendedActions = actions,
            suggestedNextSteps = new[] { "Send proactive ETA updates", "Pull delayed jobs into dispatch review", "Schedule high-priority maintenance", "Coach drivers with repeated safety signals" },
            actionButtons = new[] { "Create Dispatch Review", "Send ETA Updates", "Generate Executive Brief", "Open Evidence" }
        }));
    }

    private static Func<Database, AuditService, CancellationToken, Task<IResult>> SimpleAction(string actionName, string message)
        => async (db, audit, ct) =>
        {
            await audit.LogAsync(actionName, "ControlTower", null, ct: ct);
            return Results.Ok(ApiResponse<object>.Ok(new { action = actionName, completedAt = DateTime.UtcNow }, message));
        };

    private static Func<long, Database, CancellationToken, Task<IResult>> Timeline(string entityType)
        => async (id, db, ct) => Results.Ok(ApiResponse<object>.Ok(await EntityTimeline(db, entityType, id, ct)));

    private static Func<long, Database, CancellationToken, Task<IResult>> Recommendations(string module)
        => async (id, db, ct) => Results.Ok(ApiResponse<object>.Ok(await ModuleRecommendations(db, module, ct)));

    private static Func<long, Dictionary<string, object?>, Database, AuditService, CancellationToken, Task<IResult>> ChangeStatus(string table, string action)
        => async (id, body, db, audit, ct) =>
        {
            await db.ExecuteAsync($"UPDATE {table} SET status=@status WHERE id=@id", c =>
            {
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@status", Get(body, "status") ?? "Active");
            }, ct);
            await audit.LogAsync(action, table, id, ct: ct);
            return Results.Ok(ApiResponse<object>.Ok(new { id, status = Get(body, "status") }));
        };

    private static Func<long, Dictionary<string, object?>, Database, AuditService, CancellationToken, Task<IResult>> ChangeEntityStatus(string table, string column, string action)
        => async (id, body, db, audit, ct) =>
        {
            var target = Get(body, "targetId") ?? Get(body, "driverId") ?? Get(body, "vehicleId");
            await db.ExecuteAsync($"UPDATE {table} SET {column}=@targetId WHERE id=@id", c =>
            {
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@targetId", target);
            }, ct);
            if (table == "vehicles" && column == "assigned_driver_id" && target is not DBNull)
            {
                await db.ExecuteAsync("UPDATE drivers SET assigned_vehicle_id=@vehicleId WHERE id=@driverId", c =>
                {
                    c.Parameters.AddWithValue("@vehicleId", id);
                    c.Parameters.AddWithValue("@driverId", target);
                }, ct);
                await db.ExecuteAsync("INSERT INTO vehicle_assignments (company_id, vehicle_id, driver_id, assignment_type, status) VALUES (1, @vehicleId, @driverId, 'Smart Driver Assignment', 'Active')", c =>
                {
                    c.Parameters.AddWithValue("@vehicleId", id);
                    c.Parameters.AddWithValue("@driverId", target);
                }, ct);
                await AddTimeline(db, "Vehicle", id, action, "Smart driver assignment updated", ct);
            }
            if (table == "drivers" && column == "assigned_vehicle_id" && target is not DBNull)
            {
                await db.ExecuteAsync("UPDATE vehicles SET assigned_driver_id=@driverId WHERE id=@vehicleId", c =>
                {
                    c.Parameters.AddWithValue("@driverId", id);
                    c.Parameters.AddWithValue("@vehicleId", target);
                }, ct);
                await AddTimeline(db, "Driver", id, action, "Smart vehicle assignment updated", ct);
            }
            await audit.LogAsync(action, table, id, ct: ct);
            return Results.Ok(ApiResponse<object>.Ok(new { id }));
        };

    private static Func<long, Database, AuditService, CancellationToken, Task<IResult>> DeleteEntity(string table, string action)
        => async (id, db, audit, ct) =>
        {
            await db.ExecuteAsync($"DELETE FROM {table} WHERE id=@id", c => c.Parameters.AddWithValue("@id", id), ct);
            await audit.LogAsync(action, table, id, ct: ct);
            return Results.Ok(ApiResponse<object>.Ok(new { id }, "Deleted"));
        };

    private static Func<long, Database, AuditService, CancellationToken, Task<IResult>> SoftDelete(string table, string action)
        => async (id, db, audit, ct) =>
        {
            await db.ExecuteAsync($"UPDATE {table} SET deleted_at=CURRENT_TIMESTAMP, status='Deleted' WHERE id=@id", c => c.Parameters.AddWithValue("@id", id), ct);
            var entityName = table switch
            {
                "vehicles" => "Vehicle",
                "drivers" => "Driver",
                "customers" => "Customer",
                "assets" => "Asset",
                _ => table
            };
            await audit.LogAsync(action, entityName, id, ct: ct);
            await AddTimeline(db, entityName, id, action, $"{entityName} deleted", ct);
            return Results.Ok(ApiResponse<object>.Ok(new { id }, "Deleted"));
        };

    private static Func<HttpContext, long, Database, AuditService, CancellationToken, Task<IResult>> SoftDeleteWithPermission(string table, string action, string permission)
        => async (http, id, db, audit, ct) =>
        {
            var denied = RequirePermission(http, permission);
            if (denied is not null) return denied;

            var companyId = GetCompanyId(http);
            var affected = await db.ExecuteAsync(
                $"UPDATE {table} SET deleted_at=CURRENT_TIMESTAMP, status='Deleted' WHERE id=@id AND company_id=@companyId",
                c =>
                {
                    c.Parameters.AddWithValue("@id", id);
                    c.Parameters.AddWithValue("@companyId", companyId);
                }, ct);

            if (affected == 0)
            {
                return Results.NotFound(ApiResponse<object>.Fail("Record not found"));
            }

            var entityName = table switch
            {
                "vehicles" => "Vehicle",
                "drivers" => "Driver",
                "customers" => "Customer",
                "assets" => "Asset",
                _ => table
            };
            await audit.LogAsync(action, entityName, id, ct: ct);
            await AddTimeline(db, entityName, id, action, $"{entityName} deleted", ct);
            return Results.Ok(ApiResponse<object>.Ok(new { id }, "Deleted"));
        };

    private static Task<List<Dictionary<string, object?>>> EntityTimeline(Database db, string entityType, long id, CancellationToken ct)
        => db.QueryAsync(
            @"SELECT id, entity_type, entity_id, event_type, title, body, severity, created_at event_time
              FROM entity_timeline_events
              WHERE entity_type=@type AND entity_id=@id
              UNION ALL
              SELECT id, entity_type, entity_id, event_type, title, NULL body, severity, event_time
              FROM operational_events
              WHERE entity_type=@type AND entity_id=@id
              ORDER BY event_time DESC LIMIT 20",
            c =>
            {
                c.Parameters.AddWithValue("@type", entityType);
                c.Parameters.AddWithValue("@id", id);
            }, ct);

    private static Task<List<Dictionary<string, object?>>> ModuleRecommendations(Database db, string module, CancellationToken ct)
        => db.QueryAsync("SELECT * FROM ai_recommendations WHERE module_key=@module ORDER BY score DESC LIMIT 8",
            c => c.Parameters.AddWithValue("@module", module.ToLowerInvariant()), ct);

    private static Task<List<Dictionary<string, object?>>> AuditTrail(Database db, string entityName, long id, CancellationToken ct)
        => db.QueryAsync("SELECT * FROM audit_logs WHERE entity_name=@entity AND entity_id=@id ORDER BY created_at DESC LIMIT 20",
            c =>
            {
                c.Parameters.AddWithValue("@entity", entityName);
                c.Parameters.AddWithValue("@id", id);
            }, ct);

    private static Task AddTimeline(Database db, string entityType, long id, string eventType, string title, CancellationToken ct)
        => db.ExecuteAsync(
            @"INSERT INTO entity_timeline_events (company_id, entity_type, entity_id, event_type, title, body, severity)
              VALUES (1, @type, @id, @eventType, @title, 'Created by OpsTrax Batch 1 workflow.', 'Info')",
            c =>
            {
                c.Parameters.AddWithValue("@type", entityType);
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@eventType", eventType);
                c.Parameters.AddWithValue("@title", title);
            }, ct);

    private static async Task<IResult> EntityById(Database db, string table, long id, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync($"SELECT * FROM {table} WHERE id=@id", c => c.Parameters.AddWithValue("@id", id), ct);
        return row is null ? Results.NotFound(ApiResponse<object>.Fail("Record not found")) : Results.Ok(ApiResponse<object>.Ok(row));
    }

    private static async Task<IResult> Summary(Database db, string table, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync($"SELECT COUNT(*) total, SUM(status IN ('Active','Available','Open','In Progress','Assigned','On Route')) active, SUM(status IN ('Delayed','At Risk','Critical','Expiring Soon')) atRisk FROM {table}", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(row ?? new Dictionary<string, object?>()));
    }

    private static async Task<IResult> OkRows(Database db, string sql, Action<MySqlCommand>? bind = null, string message = "", CancellationToken ct = default)
        => Results.Ok(ApiResponse<object>.Ok(await db.QueryAsync(sql, bind, ct), message));

    private static Action<MySqlCommand>? BindModule(string moduleKey, ModuleDefinition definition)
        => definition.RequiresModuleKey ? c => c.Parameters.AddWithValue("@key", moduleKey) : null;

    private static void BindModuleRecord(MySqlCommand c, string moduleKey, Dictionary<string, object?> body, long companyId)
    {
        c.Parameters.AddWithValue("@key", moduleKey);
        c.Parameters.AddWithValue("@companyId", companyId);
        c.Parameters.AddWithValue("@title", Get(body, "title"));
        c.Parameters.AddWithValue("@status", Get(body, "status"));
        c.Parameters.AddWithValue("@owner", Get(body, "ownerName"));
        c.Parameters.AddWithValue("@location", Get(body, "locationName"));
        c.Parameters.AddWithValue("@risk", Get(body, "riskLevel"));
        c.Parameters.AddWithValue("@amount", Get(body, "amount"));
    }

    private sealed record ModuleDefinition(
        string TableName,
        string ListSql,
        string DetailSql,
        string SummarySql,
        bool RequiresModuleKey = false,
        string? CreateSql = null,
        string? UpdateSql = null
    );

    private static readonly Dictionary<string, ModuleDefinition> ModuleDefinitions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["route-planning"] = new(
            "routes",
            @"SELECT r.id, r.route_code route_code, r.name title, r.status, v.vehicle_code vehicle_code, d.full_name driver_name, COUNT(rs.id) stop_count, MIN(rs.eta) due_at, 'Route Plan' record_type
              FROM routes r
              LEFT JOIN vehicles v ON v.id=r.assigned_vehicle_id
              LEFT JOIN drivers d ON d.id=r.assigned_driver_id
              LEFT JOIN route_stops rs ON rs.route_id=r.id
              GROUP BY r.id, r.route_code, r.name, r.status, v.vehicle_code, d.full_name
              ORDER BY r.id DESC",
            @"SELECT r.*, v.vehicle_code, d.full_name driver_name FROM routes r LEFT JOIN vehicles v ON v.id=r.assigned_vehicle_id LEFT JOIN drivers d ON d.id=r.assigned_driver_id WHERE r.id=@id",
            "SELECT COUNT(*) total, SUM(status IN ('Active','Planned')) active, SUM(status IN ('At Risk','Delayed')) risk_items FROM routes",
            CreateSql: @"INSERT INTO routes (company_id, route_code, name, status) VALUES (@companyId, CONCAT('RTE-', UUID_SHORT()), @title, COALESCE(NULLIF(@status,''), 'Planned'))",
            UpdateSql: "UPDATE routes SET name=COALESCE(NULLIF(@title,''), name), status=COALESCE(NULLIF(@status,''), status) WHERE id=@id"),

        ["leads"] = new(
            "module_records",
            "SELECT * FROM module_records WHERE module_key='leads' ORDER BY id DESC",
            "SELECT * FROM module_records WHERE module_key='leads' AND id=@id",
            "SELECT COUNT(*) total, SUM(status IN ('Qualified','Proposal Needed','New')) active, SUM(risk_level IN ('High','Critical')) risk_items FROM module_records WHERE module_key='leads'",
            RequiresModuleKey: true),

        ["sales-pipeline"] = new(
            "module_records",
            "SELECT * FROM module_records WHERE module_key='sales-pipeline' ORDER BY amount DESC",
            "SELECT * FROM module_records WHERE module_key='sales-pipeline' AND id=@id",
            "SELECT COUNT(*) total, SUM(status IN ('Negotiation','Contracting','Discovery')) active, SUM(risk_level IN ('High','Critical')) risk_items FROM module_records WHERE module_key='sales-pipeline'",
            RequiresModuleKey: true),

        ["opportunities"] = new(
            "module_records",
            "SELECT * FROM module_records WHERE module_key='opportunities' ORDER BY id DESC",
            "SELECT * FROM module_records WHERE module_key='opportunities' AND id=@id",
            "SELECT COUNT(*) total, SUM(status NOT IN ('Closed Won','Closed Lost')) active, SUM(risk_level IN ('High','Critical')) risk_items FROM module_records WHERE module_key='opportunities'",
            RequiresModuleKey: true),

        ["campaigns"] = new(
            "module_records",
            "SELECT * FROM module_records WHERE module_key='campaigns' ORDER BY id DESC",
            "SELECT * FROM module_records WHERE module_key='campaigns' AND id=@id",
            "SELECT COUNT(*) total, SUM(status='Active') active, 0 risk_items FROM module_records WHERE module_key='campaigns'",
            RequiresModuleKey: true),

        ["assets"] = new(
            "assets",
            @"SELECT a.id, a.asset_code asset_code, a.name title, a.asset_type asset_type, a.status, a.current_location location_name, v.vehicle_code assigned_vehicle
              FROM assets a LEFT JOIN vehicles v ON v.id=a.assigned_vehicle_id ORDER BY a.asset_code",
            "SELECT a.*, v.vehicle_code assigned_vehicle FROM assets a LEFT JOIN vehicles v ON v.id=a.assigned_vehicle_id WHERE a.id=@id",
            "SELECT COUNT(*) total, SUM(status IN ('Available','Assigned')) active, SUM(status IN ('Maintenance','At Risk')) risk_items FROM assets",
            CreateSql: @"INSERT INTO assets (company_id, asset_code, asset_type, name, status, current_location) VALUES (@companyId, CONCAT('AST-', UUID_SHORT()), 'Equipment', @title, COALESCE(NULLIF(@status,''),'Available'), @location)",
            UpdateSql: "UPDATE assets SET name=COALESCE(NULLIF(@title,''), name), status=COALESCE(NULLIF(@status,''), status), current_location=COALESCE(NULLIF(@location,''), current_location) WHERE id=@id"),

        ["maintenance"] = new(
            "maintenance_items",
            @"SELECT mi.id, mi.title, mi.category, mi.status, mi.risk_level risk_level, mi.due_date due_at, v.vehicle_code vehicle_code
              FROM maintenance_items mi LEFT JOIN vehicles v ON v.id=mi.vehicle_id ORDER BY mi.due_date, mi.id DESC",
            "SELECT mi.*, v.vehicle_code FROM maintenance_items mi LEFT JOIN vehicles v ON v.id=mi.vehicle_id WHERE mi.id=@id",
            "SELECT COUNT(*) total, SUM(status IN ('Open','Scheduled','In Progress')) active, SUM(risk_level IN ('High','Critical')) risk_items FROM maintenance_items",
            CreateSql: @"INSERT INTO maintenance_items (company_id, title, category, status, risk_level, due_date) VALUES (@companyId, @title, 'General', COALESCE(NULLIF(@status,''),'Open'), COALESCE(NULLIF(@risk,''),'Medium'), DATE_ADD(CURDATE(), INTERVAL 7 DAY))",
            UpdateSql: "UPDATE maintenance_items SET title=COALESCE(NULLIF(@title,''), title), status=COALESCE(NULLIF(@status,''), status), risk_level=COALESCE(NULLIF(@risk,''), risk_level) WHERE id=@id"),

        ["work-orders"] = new(
            "work_orders",
            @"SELECT wo.id, wo.work_order_code work_order_code, wo.title, wo.priority risk_level, wo.status, wo.due_date due_at, wo.estimated_cost amount, v.vehicle_code vehicle_code
              FROM work_orders wo LEFT JOIN vehicles v ON v.id=wo.vehicle_id ORDER BY FIELD(wo.priority,'Critical','High','Normal','Low'), wo.due_date",
            "SELECT wo.*, v.vehicle_code FROM work_orders wo LEFT JOIN vehicles v ON v.id=wo.vehicle_id WHERE wo.id=@id",
            "SELECT COUNT(*) total, SUM(status IN ('Open','Scheduled','In Progress','Waiting Parts')) active, SUM(priority IN ('High','Critical')) risk_items FROM work_orders",
            CreateSql: @"INSERT INTO work_orders (company_id, work_order_code, title, priority, status, due_date, estimated_cost) VALUES (@companyId, CONCAT('WO-', UUID_SHORT()), @title, COALESCE(NULLIF(@risk,''),'Normal'), COALESCE(NULLIF(@status,''),'Open'), DATE_ADD(CURDATE(), INTERVAL 5 DAY), NULLIF(@amount,''))",
            UpdateSql: "UPDATE work_orders SET title=COALESCE(NULLIF(@title,''), title), status=COALESCE(NULLIF(@status,''), status), priority=COALESCE(NULLIF(@risk,''), priority), estimated_cost=COALESCE(NULLIF(@amount,''), estimated_cost) WHERE id=@id"),

        ["fuel-idling"] = new(
            "fuel_transactions",
            @"SELECT ft.id, CONCAT('Fuel transaction ', ft.id) title, 'Approved' status, v.vehicle_code vehicle_code, ft.fuel_station location_name, ft.gallons, ft.idle_minutes idle_minutes, ft.total_cost amount, ft.transaction_time due_at
              FROM fuel_transactions ft LEFT JOIN vehicles v ON v.id=ft.vehicle_id ORDER BY ft.transaction_time DESC",
            "SELECT ft.*, v.vehicle_code FROM fuel_transactions ft LEFT JOIN vehicles v ON v.id=ft.vehicle_id WHERE ft.id=@id",
            "SELECT COUNT(*) total, COUNT(*) active, SUM(idle_minutes > 60) risk_items, COALESCE(SUM(total_cost),0) total_cost FROM fuel_transactions"),

        ["safety"] = new(
            "safety_events",
            @"SELECT se.id, se.event_type title, se.severity risk_level, se.review_status status, se.description, se.event_time due_at, v.vehicle_code vehicle_code, d.full_name driver_name
              FROM safety_events se LEFT JOIN vehicles v ON v.id=se.vehicle_id LEFT JOIN drivers d ON d.id=se.driver_id ORDER BY se.event_time DESC",
            "SELECT se.*, v.vehicle_code, d.full_name driver_name FROM safety_events se LEFT JOIN vehicles v ON v.id=se.vehicle_id LEFT JOIN drivers d ON d.id=se.driver_id WHERE se.id=@id",
            "SELECT COUNT(*) total, SUM(review_status <> 'Resolved') active, SUM(severity IN ('High','Critical')) risk_items FROM safety_events"),

        ["dashcam"] = new(
            "dashcam_events",
            "SELECT id, title, severity risk_level, coaching_status status, event_time due_at FROM dashcam_events ORDER BY event_time DESC",
            "SELECT * FROM dashcam_events WHERE id=@id",
            "SELECT COUNT(*) total, SUM(coaching_status <> 'Resolved') active, SUM(severity IN ('High','Critical')) risk_items FROM dashcam_events"),

        ["compliance"] = new(
            "compliance_documents",
            @"SELECT id, document_name title, document_type, related_entity_type owner_name, status, expiry_date due_at,
                     CASE WHEN status <> 'Valid' OR expiry_date <= DATE_ADD(CURDATE(), INTERVAL 30 DAY) THEN 'High' ELSE 'Low' END risk_level
              FROM compliance_documents ORDER BY expiry_date",
            "SELECT * FROM compliance_documents WHERE id=@id",
            "SELECT COUNT(*) total, SUM(status='Valid') active, SUM(status <> 'Valid' OR expiry_date <= DATE_ADD(CURDATE(), INTERVAL 30 DAY)) risk_items FROM compliance_documents"),

        ["hos-eld"] = new(
            "hos_logs",
            @"SELECT hl.id, CONCAT('HOS log - ', d.full_name) title, hl.status, d.full_name owner_name, hl.log_date due_at, hl.driving_hours, hl.on_duty_hours, hl.cycle_hours_left,
                     CASE WHEN hl.status='Near Limit' THEN 'High' ELSE 'Low' END risk_level
              FROM hos_logs hl JOIN drivers d ON d.id=hl.driver_id ORDER BY hl.log_date DESC",
            "SELECT hl.*, d.full_name driver_name FROM hos_logs hl JOIN drivers d ON d.id=hl.driver_id WHERE hl.id=@id",
            "SELECT COUNT(*) total, SUM(status='Compliant') active, SUM(status <> 'Compliant') risk_items FROM hos_logs"),

        ["dvir-inspections"] = new(
            "inspections",
            @"SELECT i.id, i.inspection_type title, i.result status, i.created_at due_at, i.notes, v.vehicle_code vehicle_code, d.full_name driver_name,
                     CASE WHEN i.result IN ('Defect Found','Needs Review') THEN 'High' ELSE 'Low' END risk_level
              FROM inspections i LEFT JOIN vehicles v ON v.id=i.vehicle_id LEFT JOIN drivers d ON d.id=i.driver_id ORDER BY i.created_at DESC",
            "SELECT i.*, v.vehicle_code, d.full_name driver_name FROM inspections i LEFT JOIN vehicles v ON v.id=i.vehicle_id LEFT JOIN drivers d ON d.id=i.driver_id WHERE i.id=@id",
            "SELECT COUNT(*) total, SUM(result='Passed') active, SUM(result <> 'Passed') risk_items FROM inspections"),

        ["customer-portal"] = new(
            "customer_communications",
            @"SELECT cc.id, CONCAT('Customer update - ', c.name) title, cc.status, c.name owner_name, cc.channel, cc.sent_at due_at, j.job_code job_code
              FROM customer_communications cc LEFT JOIN customers c ON c.id=cc.customer_id LEFT JOIN jobs j ON j.id=cc.job_id ORDER BY cc.sent_at DESC",
            "SELECT cc.*, c.name customer_name, j.job_code FROM customer_communications cc LEFT JOIN customers c ON c.id=cc.customer_id LEFT JOIN jobs j ON j.id=cc.job_id WHERE cc.id=@id",
            "SELECT COUNT(*) total, SUM(status='Sent') active, SUM(status <> 'Sent') risk_items FROM customer_communications"),

        ["customers"] = new(
            "customers",
            "SELECT id, customer_code customer_code, name title, contact_name owner_name, email, status, sla_tier risk_level FROM customers ORDER BY name",
            "SELECT * FROM customers WHERE id=@id",
            "SELECT COUNT(*) total, SUM(status='Active') active, SUM(sla_tier='Platinum') risk_items FROM customers",
            CreateSql: @"INSERT INTO customers (company_id, customer_code, name, contact_name, status, sla_tier) VALUES (@companyId, CONCAT('CUS-', UUID_SHORT()), @title, @owner, COALESCE(NULLIF(@status,''),'Active'), COALESCE(NULLIF(@risk,''),'Standard'))",
            UpdateSql: "UPDATE customers SET name=COALESCE(NULLIF(@title,''), name), contact_name=COALESCE(NULLIF(@owner,''), contact_name), status=COALESCE(NULLIF(@status,''), status), sla_tier=COALESCE(NULLIF(@risk,''), sla_tier) WHERE id=@id"),

        ["contracts-rates"] = new(
            "contracts",
            @"SELECT c.id, c.contract_code contract_code, c.title, c.rate_type, c.status, c.expiration_date due_at, cu.name owner_name
              FROM contracts c JOIN customers cu ON cu.id=c.customer_id ORDER BY c.expiration_date",
            "SELECT c.*, cu.name customer_name FROM contracts c JOIN customers cu ON cu.id=c.customer_id WHERE c.id=@id",
            "SELECT COUNT(*) total, SUM(status='Active') active, SUM(expiration_date <= DATE_ADD(CURDATE(), INTERVAL 60 DAY)) risk_items FROM contracts"),

        ["carrier-management"] = new(
            "carriers",
            "SELECT id, name title, mc_number, safety_rating risk_level, status FROM carriers ORDER BY name",
            "SELECT * FROM carriers WHERE id=@id",
            "SELECT COUNT(*) total, SUM(status='Active') active, SUM(safety_rating='Watchlist') risk_items FROM carriers"),

        ["expenses"] = new(
            "expenses",
            "SELECT id, title, category, status, amount, expense_date due_at, CASE WHEN status='Review' THEN 'High' ELSE 'Low' END risk_level FROM expenses ORDER BY expense_date DESC",
            "SELECT * FROM expenses WHERE id=@id",
            "SELECT COUNT(*) total, SUM(status='Approved') active, SUM(status <> 'Approved') risk_items, COALESCE(SUM(amount),0) total_amount FROM expenses"),

        ["documents"] = new(
            "documents",
            "SELECT id, title, document_type, owner_name, status, uploaded_at due_at, CASE WHEN status='Expiring' THEN 'High' ELSE 'Low' END risk_level FROM documents ORDER BY uploaded_at DESC",
            "SELECT * FROM documents WHERE id=@id",
            "SELECT COUNT(*) total, SUM(status='Active') active, SUM(status='Expiring') risk_items FROM documents"),

        ["reports-analytics"] = new(
            "kpi_records",
            "SELECT id, label title, value_text value_text, trend, trend_value, status FROM kpi_records ORDER BY id",
            "SELECT * FROM kpi_records WHERE id=@id",
            "SELECT COUNT(*) total, SUM(status='Healthy') active, SUM(status <> 'Healthy') risk_items FROM kpi_records"),

        ["sla-kpi"] = new(
            "sla_records",
            @"SELECT sr.id, sr.metric_name title, sr.status, sr.target_value target_value, sr.actual_value actual_value, c.name owner_name,
                     CASE WHEN sr.status='At Risk' THEN 'High' ELSE 'Low' END risk_level
              FROM sla_records sr LEFT JOIN customers c ON c.id=sr.customer_id ORDER BY sr.id DESC",
            "SELECT sr.*, c.name customer_name FROM sla_records sr LEFT JOIN customers c ON c.id=sr.customer_id WHERE sr.id=@id",
            "SELECT COUNT(*) total, SUM(status='On Track') active, SUM(status <> 'On Track') risk_items FROM sla_records"),

        ["predictive-margin"] = new(
            "module_records",
            "SELECT * FROM module_records WHERE module_key=@key ORDER BY amount DESC, id DESC",
            "SELECT * FROM module_records WHERE module_key=@key AND id=@id",
            "SELECT COUNT(*) total, SUM(status IN ('Open','Active','In Progress')) active, SUM(risk_level IN ('High','Critical')) risk_items FROM module_records WHERE module_key=@key",
            RequiresModuleKey: true),

        ["audit-logs"] = new(
            "audit_logs",
            "SELECT id, action_name title, actor_name owner_name, entity_name, entity_id, created_at due_at, 'Audit' status FROM audit_logs ORDER BY created_at DESC",
            "SELECT * FROM audit_logs WHERE id=@id",
            "SELECT COUNT(*) total, COUNT(*) active, 0 risk_items FROM audit_logs"),

        ["integrations"] = new(
            "integrations",
            "SELECT id, provider_name title, category, status FROM integrations ORDER BY provider_name",
            "SELECT * FROM integrations WHERE id=@id",
            "SELECT COUNT(*) total, SUM(status='Connected') active, SUM(status <> 'Connected') risk_items FROM integrations"),

        ["user-management"] = new(
            "users",
            "SELECT u.id, u.full_name title, u.email, u.role_name risk_level, u.status, c.name owner_name FROM users u JOIN companies c ON c.id=u.company_id ORDER BY u.full_name",
            "SELECT u.*, c.name company_name FROM users u JOIN companies c ON c.id=u.company_id WHERE u.id=@id",
            "SELECT COUNT(*) total, SUM(status='Active') active, 0 risk_items FROM users"),

        ["settings"] = new(
            "companies",
            "SELECT id, name title, company_code company_code, industry, timezone location_name, status FROM companies ORDER BY id",
            "SELECT * FROM companies WHERE id=@id",
            "SELECT COUNT(*) total, SUM(status='Active') active, 0 risk_items FROM companies"),

        ["billing"] = new(
            "subscription_plans",
            "SELECT id, plan_name title, billing_status status, seats, monthly_amount amount FROM subscription_plans ORDER BY id DESC",
            "SELECT * FROM subscription_plans WHERE id=@id",
            "SELECT COUNT(*) total, SUM(billing_status='Active') active, SUM(billing_status <> 'Active') risk_items FROM subscription_plans"),

        ["fallback"] = new(
            "module_records",
            "SELECT * FROM module_records WHERE module_key=@key ORDER BY id DESC",
            "SELECT * FROM module_records WHERE module_key=@key AND id=@id",
            "SELECT COUNT(*) total, SUM(status IN ('Open','Active','In Progress')) active, SUM(status IN ('Open','At Risk','Critical','Delayed')) risk_items FROM module_records WHERE module_key=@key",
            RequiresModuleKey: true,
            CreateSql: @"INSERT INTO module_records (module_key, title, status, owner_name, location_name, risk_level, amount, metadata_json) VALUES (@key, @title, COALESCE(NULLIF(@status,''),'Open'), @owner, @location, COALESCE(NULLIF(@risk,''),'Medium'), NULLIF(@amount,''), JSON_OBJECT('source','api'))",
            UpdateSql: "UPDATE module_records SET title=COALESCE(NULLIF(@title,''), title), status=COALESCE(NULLIF(@status,''), status), owner_name=COALESCE(NULLIF(@owner,''), owner_name), location_name=COALESCE(NULLIF(@location,''), location_name), risk_level=COALESCE(NULLIF(@risk,''), risk_level), amount=COALESCE(NULLIF(@amount,''), amount) WHERE id=@id AND module_key=@key")
    };

    private static readonly Dictionary<string, string> ModuleWritePermissionByKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["user-management"] = "users:manage",
        ["settings"] = "settings:manage",
        ["integrations"] = "settings:manage",
        ["feature-flags"] = "users:manage",
        ["companies"] = "users:manage",
        ["audit-logs"] = "reports:manage",
        ["billing"] = "finance:manage",
        ["contracts-rates"] = "finance:manage",
        ["carrier-management"] = "finance:manage",
        ["expenses"] = "finance:manage",
    };

    private const int PasswordHashIterations = 100_000;
    private const int PasswordSaltLength = 16;
    private const int PasswordSubkeyLength = 32;

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(PasswordSaltLength);
        var subkey = Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordHashIterations, HashAlgorithmName.SHA256, PasswordSubkeyLength);
        return $"PBKDF2${PasswordHashIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(subkey)}";
    }

    private static bool VerifyPasswordHash(string password, string? storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash)) return false;
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || !string.Equals(parts[0], "PBKDF2", StringComparison.OrdinalIgnoreCase)) return false;
        if (!int.TryParse(parts[1], out var iterations)) return false;

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    private static string StageFor(string? status) => status switch
    {
        "Unassigned" or null => "Unassigned",
        "Assigned" or "Scheduled" => "Assigned",
        "In Progress" or "En Route" => "En Route",
        "At Stop" => "At Stop",
        "Completed" or "Delivered" => "Completed",
        _ => "Delayed / Exception"
    };

    private static object? Get(Dictionary<string, object?> body, string key)
    {
        if (!body.TryGetValue(key, out var value) || value is null) return DBNull.Value;
        if (value is System.Text.Json.JsonElement json)
        {
            return json.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => json.GetString() ?? (object)DBNull.Value,
                System.Text.Json.JsonValueKind.Number when json.TryGetInt64(out var l) => l,
                System.Text.Json.JsonValueKind.Number when json.TryGetDecimal(out var d) => d,
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                _ => json.ToString()
            };
        }
        return value;
    }

    private static async Task<List<string>> ValidateJob(Dictionary<string, object?> body, Database db, CancellationToken ct, bool partial = false)
    {
        var errors = new List<string>();
        if (!partial && IsBlank(Get(body, "jobNumber")) && IsBlank(Get(body, "jobCode"))) errors.Add("Job number is required.");
        if (!partial && IsBlank(Get(body, "customerId"))) errors.Add("Customer is required.");
        if (!partial && IsBlank(Get(body, "pickupAddress"))) errors.Add("Pickup address is required.");
        if (!partial && IsBlank(Get(body, "dropoffAddress"))) errors.Add("Drop-off address is required.");
        if (!IsBlank(Get(body, "slaWindowStart")) && !IsBlank(Get(body, "slaWindowEnd")) && DateTime.TryParse(Get(body, "slaWindowStart")?.ToString(), out var start) && DateTime.TryParse(Get(body, "slaWindowEnd")?.ToString(), out var end) && end < start) errors.Add("SLA end cannot be before SLA start.");
        if (!IsBlank(Get(body, "assignedDriverId")) && await db.ScalarLongAsync("SELECT COUNT(*) FROM drivers WHERE id=@id AND deleted_at IS NULL", c => c.Parameters.AddWithValue("@id", Get(body, "assignedDriverId")), ct) == 0) errors.Add("Assigned driver must exist.");
        if (!IsBlank(Get(body, "assignedVehicleId")) && await db.ScalarLongAsync("SELECT COUNT(*) FROM vehicles WHERE id=@id AND deleted_at IS NULL", c => c.Parameters.AddWithValue("@id", Get(body, "assignedVehicleId")), ct) == 0) errors.Add("Assigned vehicle must exist.");
        if (!IsBlank(Get(body, "routeId")) && await db.ScalarLongAsync("SELECT COUNT(*) FROM routes WHERE id=@id AND deleted_at IS NULL", c => c.Parameters.AddWithValue("@id", Get(body, "routeId")), ct) == 0) errors.Add("Route must exist when assigned.");
        if (!IsBlank(Get(body, "trackingCode")) && await db.ScalarLongAsync("SELECT COUNT(*) FROM jobs WHERE tracking_code=@code", c => c.Parameters.AddWithValue("@code", Get(body, "trackingCode")), ct) > 0 && !partial) errors.Add("Tracking code must be unique.");
        return errors;
    }

    private static async Task<List<string>> ValidateAssignment(Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var errors = new List<string>();
        var driverId = Get(body, "driverId");
        var vehicleId = Get(body, "vehicleId");
        var overrideFlag = string.Equals(Get(body, "override")?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
        var driver = await db.QuerySingleAsync("SELECT status FROM drivers WHERE id=@id AND deleted_at IS NULL", c => c.Parameters.AddWithValue("@id", driverId), ct);
        var vehicle = await db.QuerySingleAsync("SELECT status FROM vehicles WHERE id=@id AND deleted_at IS NULL", c => c.Parameters.AddWithValue("@id", vehicleId), ct);
        if (driver is null) errors.Add("Assigned driver must exist.");
        if (vehicle is null) errors.Add("Assigned vehicle must exist.");
        if (!overrideFlag && driver is not null && !new[] { "Available", "Idle" }.Contains(driver["status"]?.ToString())) errors.Add("Cannot assign unavailable driver without override.");
        if (!overrideFlag && vehicle is not null && !new[] { "Available", "Idle", "Active" }.Contains(vehicle["status"]?.ToString())) errors.Add("Cannot assign unavailable/maintenance vehicle without override.");
        return errors;
    }

    private static async Task<(decimal Score, string ReasonsJson, string[] Reasons)> CalculateDispatchMatch(Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var driver = await db.QuerySingleAsync("SELECT safety_score, readiness_score FROM drivers WHERE id=@id", c => c.Parameters.AddWithValue("@id", Get(body, "driverId")), ct);
        var vehicle = await db.QuerySingleAsync("SELECT readiness_score, risk_score, type FROM vehicles WHERE id=@id", c => c.Parameters.AddWithValue("@id", Get(body, "vehicleId")), ct);
        var score = 70m;
        if (driver is not null) score += Math.Min(12, Convert.ToDecimal(driver["safetyScore"]) / 10);
        if (vehicle is not null) score += Math.Min(10, Convert.ToDecimal(vehicle["readinessScore"]) / 12);
        if (vehicle is not null) score -= Math.Min(8, Convert.ToDecimal(vehicle["riskScore"]) / 12);
        score = Math.Clamp(score, 45, 99);
        var reasons = new[] { "Same region placeholder", "Available driver", "Available vehicle", "Required vehicle type match", "Driver safety score", "Vehicle maintenance status", "HOS risk placeholder", "Proximity placeholder" };
        return (Math.Round(score, 1), System.Text.Json.JsonSerializer.Serialize(reasons), reasons);
    }

    private static bool IsBlank(object? value) => value is null or DBNull || string.IsNullOrWhiteSpace(value.ToString());

    private static void BindVehicle(MySqlCommand c, Dictionary<string, object?> body)
    {
        c.Parameters.AddWithValue("@code", Get(body, "vehicleCode"));
        c.Parameters.AddWithValue("@type", Get(body, "type"));
        c.Parameters.AddWithValue("@make", Get(body, "make"));
        c.Parameters.AddWithValue("@model", Get(body, "model"));
        c.Parameters.AddWithValue("@year", Get(body, "year"));
        c.Parameters.AddWithValue("@vin", Get(body, "vin"));
        c.Parameters.AddWithValue("@plate", Get(body, "plateNumber"));
        c.Parameters.AddWithValue("@status", Get(body, "status"));
    }

    private static void BindDriver(MySqlCommand c, Dictionary<string, object?> body)
    {
        c.Parameters.AddWithValue("@code", Get(body, "driverCode"));
        c.Parameters.AddWithValue("@name", Get(body, "fullName"));
        c.Parameters.AddWithValue("@phone", Get(body, "phone"));
        c.Parameters.AddWithValue("@email", Get(body, "email"));
        c.Parameters.AddWithValue("@license", Get(body, "licenseNumber"));
        c.Parameters.AddWithValue("@status", Get(body, "status"));
    }

    private static void BindCustomer(MySqlCommand c, Dictionary<string, object?> body)
    {
        c.Parameters.AddWithValue("@code", Get(body, "customerCode"));
        c.Parameters.AddWithValue("@name", Get(body, "name"));
        c.Parameters.AddWithValue("@contact", Get(body, "contactName"));
        c.Parameters.AddWithValue("@email", Get(body, "email"));
        c.Parameters.AddWithValue("@phone", Get(body, "phone"));
        c.Parameters.AddWithValue("@billing", Get(body, "billingAddress"));
        c.Parameters.AddWithValue("@shipping", Get(body, "shippingAddress"));
        c.Parameters.AddWithValue("@status", Get(body, "status"));
        c.Parameters.AddWithValue("@slaTier", Get(body, "slaTier"));
    }

    private static void BindAsset(MySqlCommand c, Dictionary<string, object?> body)
    {
        c.Parameters.AddWithValue("@code", Get(body, "assetCode"));
        c.Parameters.AddWithValue("@type", Get(body, "assetType"));
        c.Parameters.AddWithValue("@name", Get(body, "name"));
        c.Parameters.AddWithValue("@status", Get(body, "status"));
        c.Parameters.AddWithValue("@location", Get(body, "currentLocation"));
        c.Parameters.AddWithValue("@vehicleId", Get(body, "assignedVehicleId"));
        c.Parameters.AddWithValue("@driverId", Get(body, "assignedDriverId"));
        c.Parameters.AddWithValue("@customerId", Get(body, "customerId"));
        c.Parameters.AddWithValue("@zone", Get(body, "currentZone"));
        c.Parameters.AddWithValue("@geofence", Get(body, "geofenceStatus"));
        c.Parameters.AddWithValue("@utilization", Get(body, "utilizationScore") is DBNull ? 80 : Get(body, "utilizationScore"));
        c.Parameters.AddWithValue("@risk", Get(body, "riskScore") is DBNull ? 12 : Get(body, "riskScore"));
    }

    private static void BindJob(MySqlCommand c, Dictionary<string, object?> body)
    {
        c.Parameters.AddWithValue("@code", !IsBlank(Get(body, "jobNumber")) ? Get(body, "jobNumber") : Get(body, "jobCode"));
        c.Parameters.AddWithValue("@customerId", Get(body, "customerId"));
        c.Parameters.AddWithValue("@type", Get(body, "jobType"));
        c.Parameters.AddWithValue("@priority", Get(body, "priority"));
        c.Parameters.AddWithValue("@pickup", Get(body, "pickupAddress"));
        c.Parameters.AddWithValue("@pickupLat", Get(body, "pickupLatitude"));
        c.Parameters.AddWithValue("@pickupLng", Get(body, "pickupLongitude"));
        c.Parameters.AddWithValue("@dropoff", Get(body, "dropoffAddress"));
        c.Parameters.AddWithValue("@dropLat", Get(body, "dropoffLatitude"));
        c.Parameters.AddWithValue("@dropLng", Get(body, "dropoffLongitude"));
        c.Parameters.AddWithValue("@start", Get(body, "scheduledStart"));
        c.Parameters.AddWithValue("@end", Get(body, "scheduledEnd"));
        c.Parameters.AddWithValue("@slaStart", Get(body, "slaWindowStart"));
        c.Parameters.AddWithValue("@slaEnd", Get(body, "slaWindowEnd"));
        c.Parameters.AddWithValue("@requiredVehicleType", Get(body, "requiredVehicleType"));
        c.Parameters.AddWithValue("@requiredDriverCertification", Get(body, "requiredDriverCertification"));
        c.Parameters.AddWithValue("@driverId", Get(body, "assignedDriverId"));
        c.Parameters.AddWithValue("@vehicleId", Get(body, "assignedVehicleId"));
        c.Parameters.AddWithValue("@routeId", Get(body, "routeId"));
        c.Parameters.AddWithValue("@status", Get(body, "status"));
        c.Parameters.AddWithValue("@eta", Get(body, "eta"));
        c.Parameters.AddWithValue("@slaStatus", Get(body, "slaStatus"));
        c.Parameters.AddWithValue("@proofStatus", Get(body, "proofStatus"));
        c.Parameters.AddWithValue("@customerUpdateStatus", Get(body, "customerUpdateStatus"));
        c.Parameters.AddWithValue("@trackingCode", Get(body, "trackingCode"));
        c.Parameters.AddWithValue("@riskScore", Get(body, "riskScore"));
        c.Parameters.AddWithValue("@revenue", Get(body, "revenueEstimate"));
        c.Parameters.AddWithValue("@cost", Get(body, "costEstimate"));
        c.Parameters.AddWithValue("@margin", Get(body, "marginEstimate"));
        c.Parameters.AddWithValue("@notes", Get(body, "notes"));
    }

    private static void BindRoute(MySqlCommand c, Dictionary<string, object?> body)
    {
        c.Parameters.AddWithValue("@code", Get(body, "routeCode"));
        c.Parameters.AddWithValue("@name", !IsBlank(Get(body, "routeName")) ? Get(body, "routeName") : Get(body, "name"));
        c.Parameters.AddWithValue("@status", Get(body, "status"));
        c.Parameters.AddWithValue("@driverId", Get(body, "assignedDriverId"));
        c.Parameters.AddWithValue("@vehicleId", Get(body, "assignedVehicleId"));
        c.Parameters.AddWithValue("@region", Get(body, "region"));
        c.Parameters.AddWithValue("@routeType", Get(body, "routeType"));
        c.Parameters.AddWithValue("@start", Get(body, "plannedStart"));
        c.Parameters.AddWithValue("@end", Get(body, "plannedEnd"));
        c.Parameters.AddWithValue("@cost", Get(body, "costEstimate"));
        c.Parameters.AddWithValue("@mode", Get(body, "optimizationMode"));
        c.Parameters.AddWithValue("@notes", Get(body, "notes"));
    }

    private static void BindRouteStop(MySqlCommand c, long routeId, Dictionary<string, object?> body)
    {
        c.Parameters.AddWithValue("@routeId", routeId);
        c.Parameters.AddWithValue("@jobId", Get(body, "jobId"));
        c.Parameters.AddWithValue("@customerId", Get(body, "customerId"));
        c.Parameters.AddWithValue("@sequence", Get(body, "stopSequence"));
        c.Parameters.AddWithValue("@type", Get(body, "stopType"));
        c.Parameters.AddWithValue("@address", Get(body, "address"));
        c.Parameters.AddWithValue("@lat", Get(body, "latitude"));
        c.Parameters.AddWithValue("@lng", Get(body, "longitude"));
        c.Parameters.AddWithValue("@start", Get(body, "timeWindowStart"));
        c.Parameters.AddWithValue("@end", Get(body, "timeWindowEnd"));
        c.Parameters.AddWithValue("@eta", Get(body, "eta"));
        c.Parameters.AddWithValue("@status", Get(body, "status"));
        c.Parameters.AddWithValue("@proof", Get(body, "proofStatus"));
        c.Parameters.AddWithValue("@notes", Get(body, "notes"));
    }

    private static List<string> ValidateMaintenance(Dictionary<string, object?> body)
    {
        var errors = new List<string>();
        if (IsBlank(Get(body, "vehicleId")) && IsBlank(Get(body, "assetId"))) errors.Add("Vehicle or asset is required.");
        if (IsBlank(Get(body, "serviceType"))) errors.Add("Service type is required.");
        if (IsBlank(Get(body, "dueDate")) && IsBlank(Get(body, "dueOdometer")) && IsBlank(Get(body, "dueEngineHours"))) errors.Add("Due date, odometer, or engine-hour trigger is required.");
        return errors;
    }

    private static void BindMaintenance(MySqlCommand c, Dictionary<string, object?> body)
    {
        c.Parameters.AddWithValue("@vehicleId", Get(body, "vehicleId"));
        c.Parameters.AddWithValue("@assetId", Get(body, "assetId"));
        c.Parameters.AddWithValue("@serviceType", Get(body, "serviceType"));
        c.Parameters.AddWithValue("@description", Get(body, "description"));
        c.Parameters.AddWithValue("@priority", Get(body, "priority"));
        c.Parameters.AddWithValue("@status", Get(body, "status"));
        c.Parameters.AddWithValue("@dueDate", Get(body, "dueDate"));
        c.Parameters.AddWithValue("@dueOdometer", Get(body, "dueOdometer"));
        c.Parameters.AddWithValue("@dueHours", Get(body, "dueEngineHours"));
        c.Parameters.AddWithValue("@cost", Get(body, "estimatedCost"));
        c.Parameters.AddWithValue("@risk", Get(body, "riskScore"));
        c.Parameters.AddWithValue("@action", Get(body, "recommendedAction"));
    }

    private static List<string> ValidateWorkOrder(Dictionary<string, object?> body)
    {
        var errors = new List<string>();
        if (IsBlank(Get(body, "workOrderNumber")) && IsBlank(Get(body, "workOrderCode"))) errors.Add("Work order number is required.");
        if (IsBlank(Get(body, "priority"))) errors.Add("Work order priority is required.");
        if (IsBlank(Get(body, "status"))) errors.Add("Work order status is required.");
        return errors;
    }

    private static void BindWorkOrder(MySqlCommand c, Dictionary<string, object?> body)
    {
        var number = !IsBlank(Get(body, "workOrderNumber")) ? Get(body, "workOrderNumber") : Get(body, "workOrderCode");
        c.Parameters.AddWithValue("@number", number);
        c.Parameters.AddWithValue("@vehicleId", Get(body, "vehicleId"));
        c.Parameters.AddWithValue("@assetId", Get(body, "assetId"));
        c.Parameters.AddWithValue("@maintenanceItemId", Get(body, "maintenanceItemId"));
        c.Parameters.AddWithValue("@dvirReportId", Get(body, "dvirReportId"));
        c.Parameters.AddWithValue("@issueType", Get(body, "issueType"));
        c.Parameters.AddWithValue("@title", Get(body, "title"));
        c.Parameters.AddWithValue("@description", Get(body, "description"));
        c.Parameters.AddWithValue("@priority", Get(body, "priority"));
        c.Parameters.AddWithValue("@status", Get(body, "status"));
        c.Parameters.AddWithValue("@assignedTo", Get(body, "assignedToUserId"));
        c.Parameters.AddWithValue("@vendor", Get(body, "vendorName"));
        c.Parameters.AddWithValue("@dueDate", Get(body, "dueDate"));
        c.Parameters.AddWithValue("@estimatedCost", Get(body, "estimatedCost"));
        c.Parameters.AddWithValue("@approvedCost", Get(body, "approvedCost"));
        c.Parameters.AddWithValue("@downtime", Get(body, "downtimeHours"));
        c.Parameters.AddWithValue("@approval", Get(body, "costApprovalStatus"));
        c.Parameters.AddWithValue("@risk", Get(body, "riskScore"));
        c.Parameters.AddWithValue("@action", Get(body, "recommendedAction"));
        c.Parameters.AddWithValue("@notes", Get(body, "notes"));
    }

    private static async Task AddWorkOrderEvent(Database db, long id, string? previousStatus, string newStatus, string title, CancellationToken ct)
    {
        await db.ExecuteAsync(
            @"INSERT INTO work_order_status_events (company_id, work_order_id, previous_status, new_status, event_title, event_description)
              VALUES (1, @id, @previous, @new, @title, @description)",
            c =>
            {
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@previous", previousStatus);
                c.Parameters.AddWithValue("@new", newStatus);
                c.Parameters.AddWithValue("@title", title);
                c.Parameters.AddWithValue("@description", "OpsTrax maintenance execution event");
            }, ct);
    }

    private static async Task<List<string>> ValidateDvir(Dictionary<string, object?> body, Database db, CancellationToken ct)
    {
        var errors = new List<string>();
        if (IsBlank(Get(body, "driverId"))) errors.Add("DVIR report requires a driver.");
        if (IsBlank(Get(body, "vehicleId"))) errors.Add("DVIR report requires a vehicle.");
        if (!IsBlank(Get(body, "driverId")) && await db.ScalarLongAsync("SELECT COUNT(*) FROM drivers WHERE id=@id AND deleted_at IS NULL", c => c.Parameters.AddWithValue("@id", Get(body, "driverId")), ct) == 0) errors.Add("Driver must exist.");
        if (!IsBlank(Get(body, "vehicleId")) && await db.ScalarLongAsync("SELECT COUNT(*) FROM vehicles WHERE id=@id AND deleted_at IS NULL", c => c.Parameters.AddWithValue("@id", Get(body, "vehicleId")), ct) == 0) errors.Add("Vehicle must exist.");
        var safe = Get(body, "safeToOperate")?.ToString();
        var defects = IsBlank(Get(body, "defectsFound")) ? 0 : Convert.ToInt32(Get(body, "defectsFound"));
        var overrideFlag = string.Equals(Get(body, "override")?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
        if (!overrideFlag && string.Equals(safe, "true", StringComparison.OrdinalIgnoreCase) && defects > 0 && string.Equals(Get(body, "defectSeverity")?.ToString(), "Critical", StringComparison.OrdinalIgnoreCase)) errors.Add("DVIR safe-to-operate cannot be true with unresolved critical defects without override.");
        return errors;
    }

    private static void BindDvir(MySqlCommand c, Dictionary<string, object?> body)
    {
        c.Parameters.AddWithValue("@number", !IsBlank(Get(body, "reportNumber")) ? Get(body, "reportNumber") : $"DVIR-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
        c.Parameters.AddWithValue("@driverId", Get(body, "driverId"));
        c.Parameters.AddWithValue("@vehicleId", Get(body, "vehicleId"));
        c.Parameters.AddWithValue("@country", Get(body, "countryCode"));
        c.Parameters.AddWithValue("@type", Get(body, "inspectionType"));
        c.Parameters.AddWithValue("@status", Get(body, "inspectionStatus"));
        c.Parameters.AddWithValue("@defects", Get(body, "defectsFound"));
        c.Parameters.AddWithValue("@safe", Get(body, "safeToOperate"));
        c.Parameters.AddWithValue("@signature", Get(body, "driverSignatureStatus"));
        c.Parameters.AddWithValue("@mechanic", Get(body, "mechanicReviewStatus"));
        c.Parameters.AddWithValue("@repair", Get(body, "repairCertificationStatus"));
        c.Parameters.AddWithValue("@risk", Get(body, "riskScore"));
        c.Parameters.AddWithValue("@action", Get(body, "recommendedAction"));
        c.Parameters.AddWithValue("@notes", Get(body, "notes"));
    }

    private static void BindDvirTemplate(MySqlCommand c, Dictionary<string, object?> body)
    {
        c.Parameters.AddWithValue("@name", Get(body, "templateName"));
        c.Parameters.AddWithValue("@country", Get(body, "countryCode"));
        c.Parameters.AddWithValue("@vehicleType", Get(body, "vehicleType"));
        c.Parameters.AddWithValue("@type", Get(body, "inspectionType"));
        c.Parameters.AddWithValue("@status", Get(body, "status"));
    }

    private static List<string> ValidateDocument(Dictionary<string, object?> body)
    {
        var errors = new List<string>();
        if (IsBlank(Get(body, "entityType"))) errors.Add("Document entity type is required.");
        if (IsBlank(Get(body, "entityId"))) errors.Add("Document entity id is required.");
        if (!IsBlank(Get(body, "issuedAt")) && !IsBlank(Get(body, "expiresAt")) && DateTime.TryParse(Get(body, "issuedAt")?.ToString(), out var issued) && DateTime.TryParse(Get(body, "expiresAt")?.ToString(), out var expires) && expires < issued) errors.Add("Document expiry date cannot be before issued date.");
        return errors;
    }

    private static void BindDocument(MySqlCommand c, Dictionary<string, object?> body)
    {
        c.Parameters.AddWithValue("@title", Get(body, "title"));
        c.Parameters.AddWithValue("@number", !IsBlank(Get(body, "documentNumber")) ? Get(body, "documentNumber") : $"DOC-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
        c.Parameters.AddWithValue("@entityType", Get(body, "entityType"));
        c.Parameters.AddWithValue("@entityId", Get(body, "entityId"));
        c.Parameters.AddWithValue("@type", Get(body, "documentType"));
        c.Parameters.AddWithValue("@category", Get(body, "category"));
        c.Parameters.AddWithValue("@country", Get(body, "countryCode"));
        c.Parameters.AddWithValue("@authority", Get(body, "issuingAuthority"));
        c.Parameters.AddWithValue("@issued", Get(body, "issuedAt"));
        c.Parameters.AddWithValue("@expires", Get(body, "expiresAt"));
        c.Parameters.AddWithValue("@status", Get(body, "status"));
        c.Parameters.AddWithValue("@renewal", Get(body, "renewalStatus"));
        c.Parameters.AddWithValue("@file", Get(body, "fileUrl"));
        c.Parameters.AddWithValue("@risk", Get(body, "riskScore"));
        c.Parameters.AddWithValue("@action", Get(body, "recommendedAction"));
        c.Parameters.AddWithValue("@notes", Get(body, "notes"));
    }

    private static async Task AddDocumentEvent(Database db, long id, string title, string description, CancellationToken ct)
    {
        await db.ExecuteAsync(
            "INSERT INTO document_timeline_events (company_id, document_id, event_title, event_description) VALUES (1, @id, @title, @description)",
            c =>
            {
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@title", title);
                c.Parameters.AddWithValue("@description", description);
            }, ct);
    }

    private static Task<List<Dictionary<string, object?>>> AuditRows(Database db, string entity, long id, CancellationToken ct)
        => db.QueryAsync("SELECT * FROM audit_logs WHERE entity_name=@entity AND entity_id=@id ORDER BY created_at DESC LIMIT 20", c =>
        {
            c.Parameters.AddWithValue("@entity", entity);
            c.Parameters.AddWithValue("@id", id);
        }, ct);

    private static void BindSafety(MySqlCommand c, Dictionary<string, object?> body)
    {
        c.Parameters.AddWithValue("@number", !IsBlank(Get(body, "eventNumber")) ? Get(body, "eventNumber") : $"SAFE-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
        c.Parameters.AddWithValue("@type", Get(body, "eventType"));
        c.Parameters.AddWithValue("@severity", Get(body, "severity"));
        c.Parameters.AddWithValue("@driver", Get(body, "driverId"));
        c.Parameters.AddWithValue("@vehicle", Get(body, "vehicleId"));
        c.Parameters.AddWithValue("@job", Get(body, "jobId"));
        c.Parameters.AddWithValue("@route", Get(body, "routeId"));
        c.Parameters.AddWithValue("@location", Get(body, "locationDescription"));
        c.Parameters.AddWithValue("@speed", Get(body, "speed"));
        c.Parameters.AddWithValue("@limit", Get(body, "postedSpeedLimit"));
        c.Parameters.AddWithValue("@occurred", Get(body, "occurredAt"));
        c.Parameters.AddWithValue("@review", Get(body, "reviewStatus"));
        c.Parameters.AddWithValue("@coaching", Get(body, "coachingStatus"));
        c.Parameters.AddWithValue("@incident", Get(body, "incidentStatus"));
        c.Parameters.AddWithValue("@risk", Get(body, "riskScore"));
        c.Parameters.AddWithValue("@summary", Get(body, "aiSummary"));
        c.Parameters.AddWithValue("@action", Get(body, "recommendedAction"));
    }

    private static void BindDashcam(MySqlCommand c, Dictionary<string, object?> body)
    {
        c.Parameters.AddWithValue("@number", !IsBlank(Get(body, "eventNumber")) ? Get(body, "eventNumber") : $"VID-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
        c.Parameters.AddWithValue("@safety", Get(body, "safetyEventId"));
        c.Parameters.AddWithValue("@type", Get(body, "eventType"));
        c.Parameters.AddWithValue("@title", !IsBlank(Get(body, "title")) ? Get(body, "title") : Get(body, "eventType"));
        c.Parameters.AddWithValue("@severity", Get(body, "severity"));
        c.Parameters.AddWithValue("@driver", Get(body, "driverId"));
        c.Parameters.AddWithValue("@vehicle", Get(body, "vehicleId"));
        c.Parameters.AddWithValue("@job", Get(body, "jobId"));
        c.Parameters.AddWithValue("@route", Get(body, "routeId"));
        c.Parameters.AddWithValue("@location", Get(body, "locationDescription"));
        c.Parameters.AddWithValue("@summary", Get(body, "aiSummary"));
        c.Parameters.AddWithValue("@confidence", Get(body, "aiConfidence"));
        c.Parameters.AddWithValue("@review", Get(body, "reviewStatus"));
        c.Parameters.AddWithValue("@evidence", Get(body, "evidenceStatus"));
        c.Parameters.AddWithValue("@action", Get(body, "recommendedAction"));
        c.Parameters.AddWithValue("@occurred", Get(body, "occurredAt"));
    }

    private static void BindCoaching(MySqlCommand c, Dictionary<string, object?> body)
    {
        c.Parameters.AddWithValue("@assigned", Get(body, "assignedToUserId"));
        c.Parameters.AddWithValue("@type", Get(body, "coachingType"));
        c.Parameters.AddWithValue("@priority", Get(body, "priority"));
        c.Parameters.AddWithValue("@status", Get(body, "status"));
        c.Parameters.AddWithValue("@title", Get(body, "title"));
        c.Parameters.AddWithValue("@description", Get(body, "description"));
        c.Parameters.AddWithValue("@script", Get(body, "aiScript"));
        c.Parameters.AddWithValue("@due", Get(body, "dueAt"));
    }

    private static async Task<long> InsertCoaching(HttpContext http, Database db, object? driverId, object? safetyEventId, object? dashcamEventId, string type, object? priority, CancellationToken ct)
        => await db.InsertAsync(@"INSERT INTO coaching_tasks (company_id, task_number, driver_id, safety_event_id, dashcam_event_id, assigned_to_user_id, coaching_type, priority, status, title, description, ai_script, before_safety_score, due_at)
            VALUES (@companyId, CONCAT('COACH-', UUID_SHORT()), @driver, @safety, @dashcam, @userId, @type, COALESCE(@priority,'High'), 'Assigned', CONCAT(@type, ' action'), 'Generated from OpsTrax safety intelligence.', 'Review following distance and braking patterns from the event. Focus on maintaining safe distance in high-traffic zones.', 82, DATE_ADD(NOW(), INTERVAL 7 DAY))",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", GetCompanyId(http));
                c.Parameters.AddWithValue("@userId", http.Items[AuthUserIdItemKey] ?? 1);
                c.Parameters.AddWithValue("@driver", driverId ?? DBNull.Value);
                c.Parameters.AddWithValue("@safety", safetyEventId ?? DBNull.Value);
                c.Parameters.AddWithValue("@dashcam", dashcamEventId ?? DBNull.Value);
                c.Parameters.AddWithValue("@type", type);
                c.Parameters.AddWithValue("@priority", priority ?? "High");
            }, ct);

    private static void BindIncident(MySqlCommand c, Dictionary<string, object?> body)
    {
        c.Parameters.AddWithValue("@number", !IsBlank(Get(body, "incidentNumber")) ? Get(body, "incidentNumber") : $"INC-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
        c.Parameters.AddWithValue("@safety", Get(body, "safetyEventId"));
        c.Parameters.AddWithValue("@dashcam", Get(body, "dashcamEventId"));
        c.Parameters.AddWithValue("@driver", Get(body, "driverId"));
        c.Parameters.AddWithValue("@vehicle", Get(body, "vehicleId"));
        c.Parameters.AddWithValue("@job", Get(body, "jobId"));
        c.Parameters.AddWithValue("@route", Get(body, "routeId"));
        c.Parameters.AddWithValue("@type", Get(body, "incidentType"));
        c.Parameters.AddWithValue("@severity", Get(body, "severity"));
        c.Parameters.AddWithValue("@status", Get(body, "status"));
        c.Parameters.AddWithValue("@location", Get(body, "locationDescription"));
        c.Parameters.AddWithValue("@occurred", Get(body, "occurredAt"));
        c.Parameters.AddWithValue("@driverStatement", Get(body, "driverStatement"));
        c.Parameters.AddWithValue("@witness", Get(body, "witnessStatement"));
        c.Parameters.AddWithValue("@customer", Get(body, "customerStatement"));
        c.Parameters.AddWithValue("@summary", Get(body, "aiSummary"));
        c.Parameters.AddWithValue("@action", Get(body, "recommendedAction"));
    }

    private static async Task<long> InsertIncident(HttpContext http, Database db, Dictionary<string, object?>? source, Dictionary<string, object?>? dashcam, CancellationToken ct)
    {
        var body = source ?? dashcam ?? new Dictionary<string, object?>();
        var type = !IsBlank(Get(body, "incidentType")) ? Get(body, "incidentType") : (dashcam is null ? Get(body, "eventType") : dashcam["eventType"]);
        return await db.InsertAsync(@"INSERT INTO incidents (company_id, incident_number, safety_event_id, dashcam_event_id, driver_id, vehicle_id, job_id, route_id, incident_type, severity, status, location_description, occurred_at, driver_statement, witness_statement, customer_statement, ai_summary, recommended_action, insurance_report_status)
            VALUES (@companyId, CONCAT('INC-', UUID_SHORT()), @safety, @dashcam, @driver, @vehicle, @job, @route, @type, COALESCE(@severity,'Medium'), COALESCE(@status,'New'), @location, COALESCE(@occurred,NOW()), COALESCE(@driverStatement,'Driver statement placeholder pending.'), COALESCE(@witness,'Witness statement placeholder pending.'), COALESCE(@customer,'Customer statement placeholder pending.'), COALESCE(@summary,'AI incident report builder summarized what happened, involved units, evidence available and next step.'), COALESCE(@action,'Build evidence package and review insurance/legal placeholder.'), 'Not Created')",
            c => { c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); BindIncident(c, body); c.Parameters["@type"].Value = type ?? "Safety Incident"; if (dashcam is not null) c.Parameters["@dashcam"].Value = dashcam["id"] ?? DBNull.Value; }, ct);
    }

    private static async Task<long> InsertEvidencePackage(HttpContext http, Database db, object? incidentId, object? safetyEventId, object? dashcamEventId, object? driverId, object? vehicleId, object? jobId, CancellationToken ct)
    {
        var id = await db.InsertAsync(@"INSERT INTO evidence_packages (company_id, package_number, incident_id, safety_event_id, dashcam_event_id, driver_id, vehicle_id, job_id, package_type, status, summary)
            VALUES (@companyId, CONCAT('EVD-', UUID_SHORT()), @incident, @safety, @dashcam, @driver, @vehicle, @job, 'Insurance Evidence', 'Draft', 'GPS, speed, video, job, DVIR, maintenance and statement placeholders bundled.')",
            c => { c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); c.Parameters.AddWithValue("@incident", incidentId ?? DBNull.Value); c.Parameters.AddWithValue("@safety", safetyEventId ?? DBNull.Value); c.Parameters.AddWithValue("@dashcam", dashcamEventId ?? DBNull.Value); c.Parameters.AddWithValue("@driver", driverId ?? DBNull.Value); c.Parameters.AddWithValue("@vehicle", vehicleId ?? DBNull.Value); c.Parameters.AddWithValue("@job", jobId ?? DBNull.Value); }, ct);
        await db.ExecuteAsync(@"INSERT INTO evidence_package_items (company_id,evidence_package_id,item_type,item_title,item_url,item_json,source_entity_type,source_entity_id)
            VALUES (@companyId,@id,'Bundle','GPS + Speed + Video Evidence Bundle','/placeholder/evidence-bundle.dat',JSON_OBJECT('chainOfCustody','created'), 'package', @id)", c => { c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); c.Parameters.AddWithValue("@id", id); }, ct);
        return id;
    }

    private static async Task<long> InsertInsuranceReport(HttpContext http, Database db, long incidentId, long? packageId, CancellationToken ct)
        => await db.InsertAsync(@"INSERT INTO insurance_reports (company_id, report_number, incident_id, evidence_package_id, status, report_summary, export_url)
            VALUES (@companyId, CONCAT('INS-', UUID_SHORT()), @incident, @package, 'Draft', 'Insurance/legal incident report placeholder generated by OpsTrax.', '/placeholder/insurance-report.pdf')",
            c => { c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); c.Parameters.AddWithValue("@incident", incidentId); c.Parameters.AddWithValue("@package", packageId.HasValue ? packageId.Value : DBNull.Value); }, ct);

    // =====================================================================
    // BATCH 5 HANDLERS — FUEL & IDLING
    // =====================================================================

    private static async Task<IResult> FuelSummary(Database db, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT
                CONCAT('$', FORMAT(COALESCE(SUM(CASE WHEN DATE(fuel_date)=CURDATE() THEN total_cost ELSE 0 END),0),2)) fuel_spend_today,
                CONCAT('$', FORMAT(COALESCE(SUM(CASE WHEN fuel_date >= DATE_FORMAT(CURDATE(),'%Y-%m-01') THEN total_cost ELSE 0 END),0),2)) fuel_spend_this_month,
                CONCAT('$', FORMAT(COALESCE((SELECT SUM(estimated_cost) FROM idling_events WHERE DATE(started_at)=CURDATE()),0),2)) idle_cost_today,
                COALESCE((SELECT ROUND(SUM(duration_minutes),0) FROM idling_events WHERE DATE(started_at)=CURDATE()),0) idle_hours_today,
                ROUND(AVG(quantity / NULLIF(total_cost,0) * unit_price),2) average_mpg_placeholder,
                COUNT(*) fuel_transactions,
                (SELECT COUNT(*) FROM fuel_anomalies WHERE status='Open') fuel_anomalies,
                (SELECT COUNT(DISTINCT vehicle_id) FROM idling_events WHERE threshold_status='Excessive') high_idle_vehicles,
                (SELECT COUNT(DISTINCT driver_id) FROM fuel_transactions WHERE anomaly_status='Anomaly Detected') high_fuel_drivers,
                CONCAT('$', FORMAT(COALESCE(SUM(total_cost) / NULLIF(SUM(quantity),0),0),4)) cost_per_gallon,
                'Integration Ready' fuel_card_import_status,
                CONCAT('$', FORMAT(COALESCE((SELECT SUM(estimated_loss) FROM fuel_anomalies WHERE status='Open'),0),2)) estimated_savings_opportunity
              FROM fuel_transactions WHERE deleted_at IS NULL", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(row ?? new Dictionary<string, object?>()));
    }

    private static Task<IResult> FuelTransactions(Database db, CancellationToken ct)
        => OkRows(db,
            @"SELECT ft.*, v.vehicle_code, d.full_name driver_name, j.job_code,
                     CASE WHEN ft.anomaly_status='Anomaly Detected' THEN 'High' WHEN ft.anomaly_status='Under Review' THEN 'Medium' ELSE 'Low' END risk_heat_score,
                     COALESCE(ft.recommended_action, IF(ft.anomaly_status='Anomaly Detected','Investigate fuel quantity vs odometer','Normal transaction')) recommended_action
              FROM fuel_transactions ft
              LEFT JOIN vehicles v ON v.id=ft.vehicle_id
              LEFT JOIN drivers d ON d.id=ft.driver_id
              LEFT JOIN jobs j ON j.id=ft.job_id
              WHERE ft.deleted_at IS NULL
              ORDER BY ft.fuel_date DESC, ft.id DESC", ct: ct);

    private static async Task<IResult> FuelTransactionDetail(long id, Database db, CancellationToken ct)
    {
        var record = await db.QuerySingleAsync(
            @"SELECT ft.*, v.vehicle_code, d.full_name driver_name, j.job_code
              FROM fuel_transactions ft
              LEFT JOIN vehicles v ON v.id=ft.vehicle_id
              LEFT JOIN drivers d ON d.id=ft.driver_id
              LEFT JOIN jobs j ON j.id=ft.job_id
              WHERE ft.id=@id AND ft.deleted_at IS NULL",
            c => c.Parameters.AddWithValue("@id", id), ct);
        if (record is null) return Results.NotFound(ApiResponse<object>.Fail("Fuel transaction not found"));
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            record,
            anomalies = await db.QueryAsync("SELECT * FROM fuel_anomalies WHERE fuel_transaction_id=@id ORDER BY created_at DESC", c => c.Parameters.AddWithValue("@id", id), ct),
            recommendations = await ModuleRecommendations(db, "fuel-idling", ct),
            auditTrail = await AuditRows(db, "FuelTransaction", id, ct)
        }));
    }

    private static async Task<IResult> CreateFuelTransaction(HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var qty = Convert.ToDouble(Get(body, "quantity") ?? 0);
        var price = Convert.ToDouble(Get(body, "unitPrice") ?? 0);
        var total = qty > 0 && price > 0 ? qty * price : Convert.ToDouble(Get(body, "totalCost") ?? qty * price);
        var companyId = GetCompanyId(http);
        var id = await db.InsertAsync(
            @"INSERT INTO fuel_transactions (company_id, transaction_number, vehicle_id, driver_id, job_id, route_id,
                fuel_date, fuel_type, gallons, quantity, unit, unit_price, total_cost, currency,
                odometer, fuel_station, payment_method, fuel_card_number, region, anomaly_status, notes)
              VALUES (@companyId, @number, @vehicle, @driver, @job, @route,
                COALESCE(@date, CURDATE()), COALESCE(@fuelType,'Diesel'), @qty, @qty, COALESCE(@unit,'Gallons'), @price, @total,
                COALESCE(@currency,'USD'), @odometer, @station, COALESCE(@payment,'Fleet Card'), @card, @region,
                COALESCE(@anomaly,'Normal'), @notes)",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@number", !IsBlank(Get(body, "transactionNumber")) ? Get(body, "transactionNumber") : $"FT-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
                c.Parameters.AddWithValue("@vehicle", Get(body, "vehicleId"));
                c.Parameters.AddWithValue("@driver", Get(body, "driverId"));
                c.Parameters.AddWithValue("@job", Get(body, "jobId"));
                c.Parameters.AddWithValue("@route", Get(body, "routeId"));
                c.Parameters.AddWithValue("@date", Get(body, "fuelDate"));
                c.Parameters.AddWithValue("@fuelType", Get(body, "fuelType"));
                c.Parameters.AddWithValue("@qty", qty);
                c.Parameters.AddWithValue("@unit", Get(body, "unit"));
                c.Parameters.AddWithValue("@price", price);
                c.Parameters.AddWithValue("@total", total);
                c.Parameters.AddWithValue("@currency", Get(body, "currency"));
                c.Parameters.AddWithValue("@odometer", Get(body, "odometer"));
                c.Parameters.AddWithValue("@station", Get(body, "fuelStation"));
                c.Parameters.AddWithValue("@payment", Get(body, "paymentMethod"));
                c.Parameters.AddWithValue("@card", Get(body, "fuelCardNumber"));
                c.Parameters.AddWithValue("@region", Get(body, "region"));
                c.Parameters.AddWithValue("@anomaly", Get(body, "anomalyStatus"));
                c.Parameters.AddWithValue("@notes", Get(body, "notes"));
            }, ct);
        await audit.LogAsync("fuel.transaction.created", "FuelTransaction", id, ct: ct);
        return Results.Created($"/api/fuel/transactions/{id}", ApiResponse<object>.Ok(new { id }, "Fuel transaction created"));
    }

    private static async Task<IResult> UpdateFuelTransaction(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync(
            @"UPDATE fuel_transactions SET vehicle_id=COALESCE(@vehicle,vehicle_id), driver_id=COALESCE(@driver,driver_id),
                fuel_date=COALESCE(@date,fuel_date), fuel_type=COALESCE(@fuelType,fuel_type),
                quantity=COALESCE(@qty,quantity), unit=COALESCE(@unit,unit), unit_price=COALESCE(@price,unit_price),
                total_cost=COALESCE(@total,total_cost), odometer=COALESCE(@odometer,odometer),
                fuel_station=COALESCE(@station,fuel_station), payment_method=COALESCE(@payment,payment_method),
                region=COALESCE(@region,region), anomaly_status=COALESCE(@anomaly,anomaly_status), notes=COALESCE(@notes,notes)
              WHERE id=@id AND company_id=@companyId",
            c =>
            {
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@companyId", GetCompanyId(http));
                c.Parameters.AddWithValue("@vehicle", Get(body, "vehicleId"));
                c.Parameters.AddWithValue("@driver", Get(body, "driverId"));
                c.Parameters.AddWithValue("@date", Get(body, "fuelDate"));
                c.Parameters.AddWithValue("@fuelType", Get(body, "fuelType"));
                c.Parameters.AddWithValue("@qty", Get(body, "quantity"));
                c.Parameters.AddWithValue("@unit", Get(body, "unit"));
                c.Parameters.AddWithValue("@price", Get(body, "unitPrice"));
                c.Parameters.AddWithValue("@total", Get(body, "totalCost"));
                c.Parameters.AddWithValue("@odometer", Get(body, "odometer"));
                c.Parameters.AddWithValue("@station", Get(body, "fuelStation"));
                c.Parameters.AddWithValue("@payment", Get(body, "paymentMethod"));
                c.Parameters.AddWithValue("@region", Get(body, "region"));
                c.Parameters.AddWithValue("@anomaly", Get(body, "anomalyStatus"));
                c.Parameters.AddWithValue("@notes", Get(body, "notes"));
            }, ct);
        await audit.LogAsync("fuel.transaction.updated", "FuelTransaction", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Fuel transaction updated"));
    }

    private static Task<IResult> IdlingEvents(Database db, CancellationToken ct)
        => OkRows(db,
            @"SELECT ie.*, v.vehicle_code, d.full_name driver_name, j.job_code
              FROM idling_events ie
              LEFT JOIN vehicles v ON v.id=ie.vehicle_id
              LEFT JOIN drivers d ON d.id=ie.driver_id
              LEFT JOIN jobs j ON j.id=ie.job_id
              ORDER BY ie.started_at DESC", ct: ct);

    private static async Task<IResult> IdlingEventDetail(long id, Database db, CancellationToken ct)
    {
        var record = await db.QuerySingleAsync(
            @"SELECT ie.*, v.vehicle_code, d.full_name driver_name
              FROM idling_events ie
              LEFT JOIN vehicles v ON v.id=ie.vehicle_id
              LEFT JOIN drivers d ON d.id=ie.driver_id
              WHERE ie.id=@id",
            c => c.Parameters.AddWithValue("@id", id), ct);
        if (record is null) return Results.NotFound(ApiResponse<object>.Fail("Idling event not found"));
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            record,
            recommendations = await ModuleRecommendations(db, "fuel-idling", ct),
        }));
    }

    private static async Task<IResult> CreateIdlingEvent(HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var companyId = GetCompanyId(http);
        var id = await db.InsertAsync(
            @"INSERT INTO idling_events (company_id, event_number, vehicle_id, driver_id, job_id, route_id,
                location_description, started_at, ended_at, duration_minutes, estimated_fuel_burn, estimated_cost,
                currency, threshold_status, risk_score, recommended_action)
              VALUES (@companyId, @number, @vehicle, @driver, @job, @route, @location,
                COALESCE(@start, NOW()), @end, COALESCE(@duration,0), COALESCE(@fuel,0), COALESCE(@cost,0),
                COALESCE(@currency,'USD'), COALESCE(@threshold,'Normal'), COALESCE(@risk,20), @action)",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@number", $"IDLE-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
                c.Parameters.AddWithValue("@vehicle", Get(body, "vehicleId"));
                c.Parameters.AddWithValue("@driver", Get(body, "driverId"));
                c.Parameters.AddWithValue("@job", Get(body, "jobId"));
                c.Parameters.AddWithValue("@route", Get(body, "routeId"));
                c.Parameters.AddWithValue("@location", Get(body, "locationDescription"));
                c.Parameters.AddWithValue("@start", Get(body, "startedAt"));
                c.Parameters.AddWithValue("@end", Get(body, "endedAt"));
                c.Parameters.AddWithValue("@duration", Get(body, "durationMinutes"));
                c.Parameters.AddWithValue("@fuel", Get(body, "estimatedFuelBurn"));
                c.Parameters.AddWithValue("@cost", Get(body, "estimatedCost"));
                c.Parameters.AddWithValue("@currency", Get(body, "currency"));
                c.Parameters.AddWithValue("@threshold", Get(body, "thresholdStatus"));
                c.Parameters.AddWithValue("@risk", Get(body, "riskScore"));
                c.Parameters.AddWithValue("@action", Get(body, "recommendedAction"));
            }, ct);
        await audit.LogAsync("idling.event.created", "IdlingEvent", id, ct: ct);
        return Results.Created($"/api/fuel/idling-events/{id}", ApiResponse<object>.Ok(new { id }, "Idling event created"));
    }

    private static async Task<IResult> UpdateIdlingEvent(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync(
            @"UPDATE idling_events SET threshold_status=COALESCE(@threshold,threshold_status),
                risk_score=COALESCE(@risk,risk_score), recommended_action=COALESCE(@action,recommended_action)
              WHERE id=@id AND company_id=@companyId",
            c =>
            {
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@companyId", GetCompanyId(http));
                c.Parameters.AddWithValue("@threshold", Get(body, "thresholdStatus"));
                c.Parameters.AddWithValue("@risk", Get(body, "riskScore"));
                c.Parameters.AddWithValue("@action", Get(body, "recommendedAction"));
            }, ct);
        await audit.LogAsync("idling.event.updated", "IdlingEvent", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Idling event updated"));
    }

    private static Task<IResult> FuelAnomalies(Database db, CancellationToken ct)
        => OkRows(db,
            @"SELECT fa.*, v.vehicle_code, d.full_name driver_name, ft.transaction_number
              FROM fuel_anomalies fa
              LEFT JOIN vehicles v ON v.id=fa.vehicle_id
              LEFT JOIN drivers d ON d.id=fa.driver_id
              LEFT JOIN fuel_transactions ft ON ft.id=fa.fuel_transaction_id
              ORDER BY FIELD(fa.severity,'Critical','High','Medium','Low'), fa.created_at DESC", ct: ct);

    private static async Task<IResult> FuelAnomalyReview(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync("UPDATE fuel_anomalies SET status=COALESCE(@status,'Reviewed'), reviewed_at=NOW() WHERE id=@id AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); c.Parameters.AddWithValue("@status", Get(body, "status")); }, ct);
        await audit.LogAsync("fuel.anomaly.reviewed", "FuelAnomaly", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Fuel anomaly reviewed"));
    }

    private static Task<IResult> FuelImportPreview(Dictionary<string, object?> body, CancellationToken ct)
        => Task.FromResult(Results.Ok(ApiResponse<object>.Ok(new
        {
            source = "Fuel Card Import Placeholder",
            detectedRows = 28,
            validRows = 26,
            warnings = new[] { "2 rows missing odometer readings", "Station names matched to known vendor list" },
            columns = new[] { "transactionNumber", "vehicleCode", "fuelDate", "quantity", "unitPrice", "totalCost", "fuelStation", "fuelCardNumber" }
        }, "Fuel card import preview generated")));

    // =====================================================================
    // BATCH 5 HANDLERS — EXPENSES
    // =====================================================================

    private static async Task<IResult> ExpensesSummary(Database db, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT
                CONCAT('$', FORMAT(COALESCE(SUM(CASE WHEN expense_date >= DATE_FORMAT(CURDATE(),'%Y-%m-01') THEN amount ELSE 0 END),0),2)) total_expenses_this_month,
                SUM(approval_status='Pending') pending_approval,
                SUM(approval_status='Approved') approved_expenses,
                SUM(approval_status='Rejected') rejected_expenses,
                CONCAT('$', FORMAT(COALESCE(SUM(CASE WHEN category_name='Fuel' THEN amount ELSE 0 END),0),2)) fuel_expenses,
                CONCAT('$', FORMAT(COALESCE(SUM(CASE WHEN category_name='Maintenance' THEN amount ELSE 0 END),0),2)) maintenance_expenses,
                CONCAT('$', FORMAT(COALESCE(SUM(CASE WHEN category_name='Driver Reimbursement' THEN amount ELSE 0 END),0),2)) driver_expenses,
                CONCAT('$', FORMAT(COALESCE(SUM(CASE WHEN category_name='Carrier Charge' THEN amount ELSE 0 END),0),2)) carrier_expenses,
                SUM(risk_score >= 60) unusual_expenses,
                CONCAT('$', FORMAT(COALESCE(AVG(amount),0),2)) average_expense_amount,
                SUM(receipt_status='Missing') missing_receipts,
                COUNT(*) total
              FROM expenses WHERE deleted_at IS NULL", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(row ?? new Dictionary<string, object?>()));
    }

    private static Task<IResult> Expenses(Database db, CancellationToken ct)
        => OkRows(db,
            @"SELECT e.*, v.vehicle_code, d.full_name driver_name, j.job_code, c.name customer_name,
                     CASE WHEN e.risk_score >= 65 THEN 'High' WHEN e.risk_score >= 40 THEN 'Medium' ELSE 'Low' END risk_heat_score,
                     COALESCE(e.recommended_action, IF(e.receipt_status='Missing','Upload receipt before approval','Review and approve expense')) recommended_action
              FROM expenses e
              LEFT JOIN vehicles v ON v.id=e.vehicle_id
              LEFT JOIN drivers d ON d.id=e.driver_id
              LEFT JOIN jobs j ON j.id=e.job_id
              LEFT JOIN customers c ON c.id=e.customer_id
              WHERE e.deleted_at IS NULL
              ORDER BY FIELD(e.approval_status,'Pending','Rejected','Approved'), e.expense_date DESC", ct: ct);

    private static async Task<IResult> ExpenseDetail(long id, Database db, CancellationToken ct)
    {
        var record = await db.QuerySingleAsync(
            @"SELECT e.*, v.vehicle_code, d.full_name driver_name, j.job_code, c.name customer_name
              FROM expenses e
              LEFT JOIN vehicles v ON v.id=e.vehicle_id
              LEFT JOIN drivers d ON d.id=e.driver_id
              LEFT JOIN jobs j ON j.id=e.job_id
              LEFT JOIN customers c ON c.id=e.customer_id
              WHERE e.id=@id AND e.deleted_at IS NULL",
            c => c.Parameters.AddWithValue("@id", id), ct);
        if (record is null) return Results.NotFound(ApiResponse<object>.Fail("Expense not found"));
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            record,
            recommendations = await ModuleRecommendations(db, "expenses", ct),
            auditTrail = await AuditRows(db, "Expense", id, ct)
        }));
    }

    private static async Task<IResult> CreateExpense(HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "finance:manage");
        if (denied is not null) return denied;
        var amount = Convert.ToDouble(Get(body, "amount") ?? 0);
        if (amount < 0) return Results.BadRequest(ApiResponse<object>.Fail("Expense amount must be non-negative"));
        var companyId = GetCompanyId(http);
        var id = await db.InsertAsync(
            @"INSERT INTO expenses (company_id, expense_number, category_name, amount, currency, expense_date,
                vehicle_id, driver_id, job_id, route_id, customer_id, carrier_id, vendor_name,
                status, approval_status, receipt_status, risk_score, recommended_action, notes)
              VALUES (@companyId, @number, COALESCE(@category,'Miscellaneous'), @amount, COALESCE(@currency,'USD'),
                COALESCE(@date, CURDATE()), @vehicle, @driver, @job, @route, @customer, @carrier, @vendor,
                COALESCE(@status,'Pending'), COALESCE(@approval,'Pending'), COALESCE(@receipt,'Missing'),
                COALESCE(@risk,20), @action, @notes)",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@number", $"EXP-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
                c.Parameters.AddWithValue("@category", Get(body, "categoryName"));
                c.Parameters.AddWithValue("@amount", amount);
                c.Parameters.AddWithValue("@currency", Get(body, "currency"));
                c.Parameters.AddWithValue("@date", Get(body, "expenseDate"));
                c.Parameters.AddWithValue("@vehicle", Get(body, "vehicleId"));
                c.Parameters.AddWithValue("@driver", Get(body, "driverId"));
                c.Parameters.AddWithValue("@job", Get(body, "jobId"));
                c.Parameters.AddWithValue("@route", Get(body, "routeId"));
                c.Parameters.AddWithValue("@customer", Get(body, "customerId"));
                c.Parameters.AddWithValue("@carrier", Get(body, "carrierId"));
                c.Parameters.AddWithValue("@vendor", Get(body, "vendorName"));
                c.Parameters.AddWithValue("@status", Get(body, "status"));
                c.Parameters.AddWithValue("@approval", Get(body, "approvalStatus"));
                c.Parameters.AddWithValue("@receipt", Get(body, "receiptStatus"));
                c.Parameters.AddWithValue("@risk", Get(body, "riskScore"));
                c.Parameters.AddWithValue("@action", Get(body, "recommendedAction"));
                c.Parameters.AddWithValue("@notes", Get(body, "notes"));
            }, ct);
        await audit.LogAsync("expense.created", "Expense", id, ct: ct);
        return Results.Created($"/api/expenses/{id}", ApiResponse<object>.Ok(new { id }, "Expense created"));
    }

    private static async Task<IResult> UpdateExpense(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "finance:manage");
        if (denied is not null) return denied;
        await db.ExecuteAsync(
            @"UPDATE expenses SET category_name=COALESCE(@category,category_name), amount=COALESCE(@amount,amount),
                expense_date=COALESCE(@date,expense_date), vendor_name=COALESCE(@vendor,vendor_name),
                receipt_status=COALESCE(@receipt,receipt_status), notes=COALESCE(@notes,notes)
              WHERE id=@id AND company_id=@companyId",
            c =>
            {
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@companyId", GetCompanyId(http));
                c.Parameters.AddWithValue("@category", Get(body, "categoryName"));
                c.Parameters.AddWithValue("@amount", Get(body, "amount"));
                c.Parameters.AddWithValue("@date", Get(body, "expenseDate"));
                c.Parameters.AddWithValue("@vendor", Get(body, "vendorName"));
                c.Parameters.AddWithValue("@receipt", Get(body, "receiptStatus"));
                c.Parameters.AddWithValue("@notes", Get(body, "notes"));
            }, ct);
        await audit.LogAsync("expense.updated", "Expense", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Expense updated"));
    }

    private static async Task<IResult> ExpenseApprove(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "finance:manage");
        if (denied is not null) return denied;
        await db.ExecuteAsync("UPDATE expenses SET approval_status='Approved', status='Approved' WHERE id=@id AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); }, ct);
        await audit.LogAsync("expense.approved", "Expense", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Expense approved"));
    }

    private static async Task<IResult> ExpenseReject(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "finance:manage");
        if (denied is not null) return denied;
        await db.ExecuteAsync("UPDATE expenses SET approval_status='Rejected', status='Rejected' WHERE id=@id AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); }, ct);
        await audit.LogAsync("expense.rejected", "Expense", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Expense rejected"));
    }

    private static Task<IResult> ExpenseImportPreview(Dictionary<string, object?> body, CancellationToken ct)
        => Task.FromResult(Results.Ok(ApiResponse<object>.Ok(new
        {
            source = "Expense Import Placeholder",
            detectedRows = 18,
            validRows = 17,
            warnings = new[] { "1 row missing category — defaulted to Miscellaneous" },
            columns = new[] { "expenseNumber", "category", "amount", "expenseDate", "vehicleCode", "vendorName" }
        }, "Expense import preview generated")));

    // =====================================================================
    // BATCH 5 HANDLERS — CONTRACTS / RATES
    // =====================================================================

    private static async Task<IResult> ContractsSummary(Database db, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT
                SUM(status='Active') active_contracts,
                SUM(status='Expiring Soon' OR (expiry_date BETWEEN CURDATE() AND DATE_ADD(CURDATE(), INTERVAL 30 DAY))) expiring_soon,
                SUM(status='Expired' OR expiry_date < CURDATE()) expired_contracts,
                COUNT(DISTINCT customer_id) customers_covered,
                COUNT(DISTINCT carrier_id) carrier_agreements,
                CONCAT('$', FORMAT(COALESCE(AVG(base_rate),0),4)) average_base_rate,
                SUM(fuel_surcharge_enabled=TRUE) fuel_surcharge_active,
                SUM(margin_risk='High') margin_risk_contracts,
                SUM(base_rate < 2.20 AND status='Active') underpriced_contracts,
                CONCAT('$', FORMAT(COALESCE(SUM(base_rate * 1200),0),0)) contract_revenue_estimate,
                SUM(status='Active' AND (expiry_date BETWEEN CURDATE() AND DATE_ADD(CURDATE(), INTERVAL 60 DAY))) renewal_queue,
                COUNT(*) total
              FROM contracts WHERE deleted_at IS NULL", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(row ?? new Dictionary<string, object?>()));
    }

    private static Task<IResult> Contracts(Database db, CancellationToken ct)
        => OkRows(db,
            @"SELECT con.*, c.name customer_name, car.name carrier_name,
                     CASE WHEN con.margin_risk='High' THEN 'High' WHEN con.margin_risk='Medium' THEN 'Medium' ELSE 'Low' END risk_heat_score,
                     CASE WHEN con.status='Expired' OR con.expiry_date < CURDATE() THEN 'Renew contract immediately'
                          WHEN con.expiry_date BETWEEN CURDATE() AND DATE_ADD(CURDATE(), INTERVAL 30 DAY) THEN 'Initiate renewal workflow'
                          WHEN con.margin_risk='High' THEN 'Renegotiate underpriced rate'
                          ELSE 'Monitor contract health' END recommended_action,
                     CASE WHEN con.expiry_date < CURDATE() THEN 'Expired'
                          WHEN con.expiry_date BETWEEN CURDATE() AND DATE_ADD(CURDATE(), INTERVAL 30 DAY) THEN 'Expiring Soon'
                          ELSE con.status END display_status
              FROM contracts con
              LEFT JOIN customers c ON c.id=con.customer_id
              LEFT JOIN carriers car ON car.id=con.carrier_id
              WHERE con.deleted_at IS NULL
              ORDER BY FIELD(con.margin_risk,'High','Medium','Low'), con.expiry_date", ct: ct);

    private static async Task<IResult> ContractDetail(long id, Database db, CancellationToken ct)
    {
        var record = await db.QuerySingleAsync(
            @"SELECT con.*, c.name customer_name, car.name carrier_name
              FROM contracts con
              LEFT JOIN customers c ON c.id=con.customer_id
              LEFT JOIN carriers car ON car.id=con.carrier_id
              WHERE con.id=@id AND con.deleted_at IS NULL",
            c => c.Parameters.AddWithValue("@id", id), ct);
        if (record is null) return Results.NotFound(ApiResponse<object>.Fail("Contract not found"));
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            record,
            rates = await db.QueryAsync("SELECT * FROM contract_rates WHERE contract_id=@id ORDER BY effective_date DESC", c => c.Parameters.AddWithValue("@id", id), ct),
            recommendations = await ModuleRecommendations(db, "contracts-rates", ct),
            auditTrail = await AuditRows(db, "Contract", id, ct)
        }));
    }

    private static async Task<IResult> CreateContract(HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "finance:manage");
        if (denied is not null) return denied;
        var effective = Get(body, "effectiveDate")?.ToString();
        var expiry = Get(body, "expiryDate")?.ToString();
        if (!IsBlank(effective) && !IsBlank(expiry) && DateTime.TryParse(effective, out var eff) && DateTime.TryParse(expiry, out var exp) && exp <= eff)
            return Results.BadRequest(ApiResponse<object>.Fail("Contract expiry date must be after effective date"));
        var companyId = GetCompanyId(http);
        var id = await db.InsertAsync(
            @"INSERT INTO contracts (company_id, contract_number, customer_id, carrier_id, contract_type,
                effective_date, expiry_date, status, currency, base_rate, rate_type,
                fuel_surcharge_enabled, fuel_surcharge_percent, sla_terms, margin_risk, notes)
              VALUES (@companyId, @number, @customer, @carrier, COALESCE(@type,'Customer'),
                @effective, @expiry, COALESCE(@status,'Active'), COALESCE(@currency,'USD'),
                COALESCE(@rate,0), COALESCE(@rateType,'Per Mile'),
                COALESCE(@fuelEnabled, FALSE), @fuelPct, @sla,
                COALESCE(@marginRisk,'Low'), @notes)",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@number", !IsBlank(Get(body, "contractNumber")) ? Get(body, "contractNumber") : $"CON-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
                c.Parameters.AddWithValue("@customer", Get(body, "customerId"));
                c.Parameters.AddWithValue("@carrier", Get(body, "carrierId"));
                c.Parameters.AddWithValue("@type", Get(body, "contractType"));
                c.Parameters.AddWithValue("@effective", effective);
                c.Parameters.AddWithValue("@expiry", expiry);
                c.Parameters.AddWithValue("@status", Get(body, "status"));
                c.Parameters.AddWithValue("@currency", Get(body, "currency"));
                c.Parameters.AddWithValue("@rate", Get(body, "baseRate"));
                c.Parameters.AddWithValue("@rateType", Get(body, "rateType"));
                c.Parameters.AddWithValue("@fuelEnabled", Get(body, "fuelSurchargeEnabled"));
                c.Parameters.AddWithValue("@fuelPct", Get(body, "fuelSurchargePercent"));
                c.Parameters.AddWithValue("@sla", Get(body, "slaTerms"));
                c.Parameters.AddWithValue("@marginRisk", Get(body, "marginRisk"));
                c.Parameters.AddWithValue("@notes", Get(body, "notes"));
            }, ct);
        await audit.LogAsync("contract.created", "Contract", id, ct: ct);
        return Results.Created($"/api/contracts/{id}", ApiResponse<object>.Ok(new { id }, "Contract created"));
    }

    private static async Task<IResult> UpdateContract(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "finance:manage");
        if (denied is not null) return denied;
        await db.ExecuteAsync(
            @"UPDATE contracts SET customer_id=COALESCE(@customer,customer_id), carrier_id=COALESCE(@carrier,carrier_id),
                contract_type=COALESCE(@type,contract_type), effective_date=COALESCE(@effective,effective_date),
                expiry_date=COALESCE(@expiry,expiry_date), status=COALESCE(@status,status),
                base_rate=COALESCE(@rate,base_rate), rate_type=COALESCE(@rateType,rate_type),
                fuel_surcharge_enabled=COALESCE(@fuelEnabled,fuel_surcharge_enabled),
                fuel_surcharge_percent=COALESCE(@fuelPct,fuel_surcharge_percent),
                sla_terms=COALESCE(@sla,sla_terms), margin_risk=COALESCE(@marginRisk,margin_risk), notes=COALESCE(@notes,notes)
              WHERE id=@id AND company_id=@companyId",
            c =>
            {
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@companyId", GetCompanyId(http));
                c.Parameters.AddWithValue("@customer", Get(body, "customerId"));
                c.Parameters.AddWithValue("@carrier", Get(body, "carrierId"));
                c.Parameters.AddWithValue("@type", Get(body, "contractType"));
                c.Parameters.AddWithValue("@effective", Get(body, "effectiveDate"));
                c.Parameters.AddWithValue("@expiry", Get(body, "expiryDate"));
                c.Parameters.AddWithValue("@status", Get(body, "status"));
                c.Parameters.AddWithValue("@rate", Get(body, "baseRate"));
                c.Parameters.AddWithValue("@rateType", Get(body, "rateType"));
                c.Parameters.AddWithValue("@fuelEnabled", Get(body, "fuelSurchargeEnabled"));
                c.Parameters.AddWithValue("@fuelPct", Get(body, "fuelSurchargePercent"));
                c.Parameters.AddWithValue("@sla", Get(body, "slaTerms"));
                c.Parameters.AddWithValue("@marginRisk", Get(body, "marginRisk"));
                c.Parameters.AddWithValue("@notes", Get(body, "notes"));
            }, ct);
        await audit.LogAsync("contract.updated", "Contract", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Contract updated"));
    }

    private static async Task<IResult> CreateContractRate(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "finance:manage");
        if (denied is not null) return denied;
        var rate = Convert.ToDouble(Get(body, "baseRate") ?? 0);
        if (rate < 0) return Results.BadRequest(ApiResponse<object>.Fail("Rate base rate must be non-negative"));
        var companyId = GetCompanyId(http);
        var rateId = await db.InsertAsync(
            @"INSERT INTO contract_rates (company_id, contract_id, rate_code, rate_type, origin_zone, destination_zone,
                vehicle_type, base_rate, minimum_charge, fuel_surcharge_percent, accessorial_type,
                effective_date, expiry_date, status)
              VALUES (@companyId, @contractId, @code, COALESCE(@type,'Per Mile'), @origin, @dest, @vehicleType,
                @rate, @min, @fuelPct, @accessorial,
                COALESCE(@effective, CURDATE()), @expiry, COALESCE(@status,'Active'))",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@contractId", id);
                c.Parameters.AddWithValue("@code", !IsBlank(Get(body, "rateCode")) ? Get(body, "rateCode") : $"RATE-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
                c.Parameters.AddWithValue("@type", Get(body, "rateType"));
                c.Parameters.AddWithValue("@origin", Get(body, "originZone"));
                c.Parameters.AddWithValue("@dest", Get(body, "destinationZone"));
                c.Parameters.AddWithValue("@vehicleType", Get(body, "vehicleType"));
                c.Parameters.AddWithValue("@rate", rate);
                c.Parameters.AddWithValue("@min", Get(body, "minimumCharge"));
                c.Parameters.AddWithValue("@fuelPct", Get(body, "fuelSurchargePercent"));
                c.Parameters.AddWithValue("@accessorial", Get(body, "accessorialType"));
                c.Parameters.AddWithValue("@effective", Get(body, "effectiveDate"));
                c.Parameters.AddWithValue("@expiry", Get(body, "expiryDate"));
                c.Parameters.AddWithValue("@status", Get(body, "status"));
            }, ct);
        await audit.LogAsync("contract.rate.created", "Contract", id, ct: ct);
        return Results.Created($"/api/contracts/{id}/rates/{rateId}", ApiResponse<object>.Ok(new { id = rateId }, "Contract rate created"));
    }

    private static async Task<IResult> UpdateContractRate(HttpContext http, long id, long rateId, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "finance:manage");
        if (denied is not null) return denied;
        await db.ExecuteAsync(
            @"UPDATE contract_rates SET rate_type=COALESCE(@type,rate_type), base_rate=COALESCE(@rate,base_rate),
                minimum_charge=COALESCE(@min,minimum_charge), status=COALESCE(@status,status)
              WHERE id=@rateId AND contract_id=@contractId AND company_id=@companyId",
            c =>
            {
                c.Parameters.AddWithValue("@rateId", rateId);
                c.Parameters.AddWithValue("@contractId", id);
                c.Parameters.AddWithValue("@companyId", GetCompanyId(http));
                c.Parameters.AddWithValue("@type", Get(body, "rateType"));
                c.Parameters.AddWithValue("@rate", Get(body, "baseRate"));
                c.Parameters.AddWithValue("@min", Get(body, "minimumCharge"));
                c.Parameters.AddWithValue("@status", Get(body, "status"));
            }, ct);
        await audit.LogAsync("contract.rate.updated", "Contract", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id = rateId }, "Contract rate updated"));
    }

    private static async Task<IResult> DeleteContractRate(HttpContext http, long id, long rateId, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "finance:manage");
        if (denied is not null) return denied;
        await db.ExecuteAsync("UPDATE contract_rates SET status='Inactive' WHERE id=@rateId AND contract_id=@contractId AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@rateId", rateId); c.Parameters.AddWithValue("@contractId", id); c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); }, ct);
        await audit.LogAsync("contract.rate.deleted", "Contract", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id = rateId }, "Contract rate deactivated"));
    }

    private static async Task<IResult> ContractActivate(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "finance:manage");
        if (denied is not null) return denied;
        await db.ExecuteAsync("UPDATE contracts SET status='Active' WHERE id=@id AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); }, ct);
        await audit.LogAsync("contract.activated", "Contract", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Contract activated"));
    }

    private static async Task<IResult> ContractExpire(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "finance:manage");
        if (denied is not null) return denied;
        await db.ExecuteAsync("UPDATE contracts SET status='Expired', expiry_date=CURDATE() WHERE id=@id AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); }, ct);
        await audit.LogAsync("contract.expired", "Contract", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Contract marked expired"));
    }

    // =====================================================================
    // BATCH 5 HANDLERS — CARRIERS
    // =====================================================================

    private static async Task<IResult> CarriersSummary(Database db, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT
                SUM(status='Active') active_carriers,
                SUM(status='Pending') pending_carriers,
                SUM(status='Suspended') suspended_carriers,
                SUM(compliance_status IN ('Non-Compliant','At Risk')) compliance_risk_carriers,
                SUM(insurance_expiry BETWEEN CURDATE() AND DATE_ADD(CURDATE(), INTERVAL 60 DAY)) insurance_expiring,
                ROUND(AVG(performance_score),1) average_carrier_score,
                ROUND(AVG(on_time_percent),1) on_time_performance,
                CONCAT('$', FORMAT(COALESCE((SELECT SUM(expense_total) FROM carrier_performance WHERE period_start >= DATE_FORMAT(CURDATE(),'%Y-%m-01')),0),2)) carrier_cost_this_month,
                (SELECT COUNT(*) FROM carrier_documents WHERE status IN ('Expired','Expiring')) documents_missing,
                SUM(contract_status='Active') contracts_active,
                SUM(performance_score >= 90) preferred_carriers,
                COUNT(*) total
              FROM carriers WHERE deleted_at IS NULL", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(row ?? new Dictionary<string, object?>()));
    }

    private static Task<IResult> Carriers(Database db, CancellationToken ct)
        => OkRows(db,
            @"SELECT c.*,
                     CASE WHEN c.compliance_status='Non-Compliant' OR c.risk_score >= 70 THEN 'High'
                          WHEN c.compliance_status='At Risk' OR c.risk_score >= 40 THEN 'Medium'
                          ELSE 'Low' END risk_heat_score,
                     COALESCE(c.recommended_action, IF(c.compliance_status='Non-Compliant','Suspend carrier — compliance risk',IF(c.insurance_expiry < DATE_ADD(CURDATE(), INTERVAL 60 DAY),'Renew insurance immediately','Monitor performance'))) recommended_action
              FROM carriers c
              WHERE c.deleted_at IS NULL
              ORDER BY FIELD(c.compliance_status,'Non-Compliant','At Risk','Compliant'), c.performance_score DESC", ct: ct);

    private static async Task<IResult> CarrierDetail(long id, Database db, CancellationToken ct)
    {
        var record = await db.QuerySingleAsync("SELECT * FROM carriers WHERE id=@id AND deleted_at IS NULL", c => c.Parameters.AddWithValue("@id", id), ct);
        if (record is null) return Results.NotFound(ApiResponse<object>.Fail("Carrier not found"));
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            record,
            performance = await db.QueryAsync("SELECT * FROM carrier_performance WHERE carrier_id=@id ORDER BY period_start DESC LIMIT 12", c => c.Parameters.AddWithValue("@id", id), ct),
            documents = await db.QueryAsync("SELECT * FROM carrier_documents WHERE carrier_id=@id ORDER BY expiry_date", c => c.Parameters.AddWithValue("@id", id), ct),
            contracts = await db.QueryAsync("SELECT * FROM contracts WHERE carrier_id=@id AND deleted_at IS NULL ORDER BY expiry_date DESC LIMIT 8", c => c.Parameters.AddWithValue("@id", id), ct),
            expenses = await db.QueryAsync("SELECT * FROM expenses WHERE carrier_id=@id AND deleted_at IS NULL ORDER BY expense_date DESC LIMIT 8", c => c.Parameters.AddWithValue("@id", id), ct),
            recommendations = await ModuleRecommendations(db, "carrier-management", ct),
            auditTrail = await AuditRows(db, "Carrier", id, ct)
        }));
    }

    private static async Task<IResult> CreateCarrier(HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "finance:manage");
        if (denied is not null) return denied;
        var email = Get(body, "email")?.ToString();
        if (!IsBlank(email) && !email!.Contains('@'))
            return Results.BadRequest(ApiResponse<object>.Fail("Carrier email is not valid"));
        var companyId = GetCompanyId(http);
        var id = await db.InsertAsync(
            @"INSERT INTO carriers (company_id, carrier_number, name, mc_number, contact_name, phone, email,
                region, status, compliance_status, insurance_expiry, contract_status,
                on_time_percent, safety_score, cost_score, performance_score, risk_score, recommended_action, notes)
              VALUES (@companyId, @number, @name, @mc, @contact, @phone, @email,
                @region, COALESCE(@status,'Active'), COALESCE(@compliance,'Compliant'), @insurance, COALESCE(@contract,'Active'),
                COALESCE(@onTime,90), COALESCE(@safety,88), COALESCE(@cost,82), COALESCE(@perf,86), COALESCE(@risk,20), @action, @notes)",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@number", $"CAR-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
                c.Parameters.AddWithValue("@name", Get(body, "carrierName") ?? Get(body, "name"));
                c.Parameters.AddWithValue("@mc", Get(body, "mcNumber"));
                c.Parameters.AddWithValue("@contact", Get(body, "contactName"));
                c.Parameters.AddWithValue("@phone", Get(body, "phone"));
                c.Parameters.AddWithValue("@email", email);
                c.Parameters.AddWithValue("@region", Get(body, "region"));
                c.Parameters.AddWithValue("@status", Get(body, "status"));
                c.Parameters.AddWithValue("@compliance", Get(body, "complianceStatus"));
                c.Parameters.AddWithValue("@insurance", Get(body, "insuranceExpiry"));
                c.Parameters.AddWithValue("@contract", Get(body, "contractStatus"));
                c.Parameters.AddWithValue("@onTime", Get(body, "onTimePercent"));
                c.Parameters.AddWithValue("@safety", Get(body, "safetyScore"));
                c.Parameters.AddWithValue("@cost", Get(body, "costScore"));
                c.Parameters.AddWithValue("@perf", Get(body, "performanceScore"));
                c.Parameters.AddWithValue("@risk", Get(body, "riskScore"));
                c.Parameters.AddWithValue("@action", Get(body, "recommendedAction"));
                c.Parameters.AddWithValue("@notes", Get(body, "notes"));
            }, ct);
        await audit.LogAsync("carrier.created", "Carrier", id, ct: ct);
        return Results.Created($"/api/carriers/{id}", ApiResponse<object>.Ok(new { id }, "Carrier created"));
    }

    private static async Task<IResult> UpdateCarrier(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "finance:manage");
        if (denied is not null) return denied;
        await db.ExecuteAsync(
            @"UPDATE carriers SET name=COALESCE(@name,name), contact_name=COALESCE(@contact,contact_name),
                phone=COALESCE(@phone,phone), email=COALESCE(@email,email), region=COALESCE(@region,region),
                compliance_status=COALESCE(@compliance,compliance_status),
                insurance_expiry=COALESCE(@insurance,insurance_expiry),
                contract_status=COALESCE(@contract,contract_status),
                on_time_percent=COALESCE(@onTime,on_time_percent), safety_score=COALESCE(@safety,safety_score),
                performance_score=COALESCE(@perf,performance_score), notes=COALESCE(@notes,notes)
              WHERE id=@id AND company_id=@companyId",
            c =>
            {
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@companyId", GetCompanyId(http));
                c.Parameters.AddWithValue("@name", Get(body, "carrierName") ?? Get(body, "name"));
                c.Parameters.AddWithValue("@contact", Get(body, "contactName"));
                c.Parameters.AddWithValue("@phone", Get(body, "phone"));
                c.Parameters.AddWithValue("@email", Get(body, "email"));
                c.Parameters.AddWithValue("@region", Get(body, "region"));
                c.Parameters.AddWithValue("@compliance", Get(body, "complianceStatus"));
                c.Parameters.AddWithValue("@insurance", Get(body, "insuranceExpiry"));
                c.Parameters.AddWithValue("@contract", Get(body, "contractStatus"));
                c.Parameters.AddWithValue("@onTime", Get(body, "onTimePercent"));
                c.Parameters.AddWithValue("@safety", Get(body, "safetyScore"));
                c.Parameters.AddWithValue("@perf", Get(body, "performanceScore"));
                c.Parameters.AddWithValue("@notes", Get(body, "notes"));
            }, ct);
        await audit.LogAsync("carrier.updated", "Carrier", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Carrier updated"));
    }

    private static async Task<IResult> CarrierStatus(HttpContext http, long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "finance:manage");
        if (denied is not null) return denied;
        var status = Get(body, "status")?.ToString() ?? "Active";
        await db.ExecuteAsync("UPDATE carriers SET status=@status WHERE id=@id AND company_id=@companyId",
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@companyId", GetCompanyId(http)); c.Parameters.AddWithValue("@status", status); }, ct);
        await audit.LogAsync("carrier.status.changed", "Carrier", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, status }, "Carrier status updated"));
    }

    // =====================================================================
    // BATCH 5 HANDLERS — PREDICTIVE COST & MARGIN
    // =====================================================================

    private static async Task<IResult> CostMarginSummary(Database db, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT
                CONCAT('$', FORMAT(COALESCE(SUM(revenue_estimate),0),0)) revenue_estimate,
                CONCAT('$', FORMAT(COALESCE(SUM(total_cost),0),0)) cost_estimate,
                CONCAT('$', FORMAT(COALESCE(SUM(margin_estimate),0),0)) gross_margin_estimate,
                CONCAT(ROUND(COALESCE(AVG(margin_percent),0),1),'%') margin_pct,
                SUM(margin_percent < 15) jobs_below_margin_target,
                SUM(margin_risk='High') routes_below_margin_target,
                (SELECT COUNT(DISTINCT vehicle_id) FROM cost_margin_records WHERE entity_type='vehicle' AND total_cost > 400) high_cost_vehicles,
                (SELECT COUNT(DISTINCT driver_id) FROM cost_margin_records WHERE driver_id IS NOT NULL AND total_cost > 300) high_cost_drivers,
                CONCAT('$', FORMAT(COALESCE(SUM(fuel_cost),0),0)) fuel_cost_impact,
                CONCAT('$', FORMAT(COALESCE(SUM(maintenance_cost),0),0)) maintenance_cost_impact,
                CONCAT('$', FORMAT(COALESCE(SUM(delay_cost),0),0)) delay_cost_impact,
                CONCAT('$', FORMAT(COALESCE(SUM(idle_cost),0),0)) savings_opportunity
              FROM cost_margin_records", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(row ?? new Dictionary<string, object?>()));
    }

    private static async Task<IResult> CostMarginRecalculate(Database db, AuditService audit, CancellationToken ct)
    {
        await audit.LogAsync("cost.margin.recalculate.run", "CostMargin", null, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            recalculated = true,
            jobsUpdated = 12,
            routesUpdated = 6,
            vehiclesUpdated = 8,
            message = "Cost margin recalculation simulation complete."
        }, "Cost margin recalculated"));
    }

    private static async Task<IResult> CostMarginRecalculateJob(long jobId, Database db, AuditService audit, CancellationToken ct)
    {
        var job = await db.QuerySingleAsync("SELECT revenue_estimate, cost_estimate, margin_estimate FROM jobs WHERE id=@id", c => c.Parameters.AddWithValue("@id", jobId), ct);
        await audit.LogAsync("cost.margin.job.recalculated", "Job", jobId, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            jobId,
            revenueEstimate = job?["revenueEstimate"] ?? 600,
            costEstimate = job?["costEstimate"] ?? 320,
            marginEstimate = job?["marginEstimate"] ?? 280,
            recalculated = true,
            message = "Job margin recalculated from cost records."
        }, "Job margin recalculated"));
    }

    // =====================================================================
    // BATCH 5 HANDLERS — COST LEAKAGE INTELLIGENCE
    // =====================================================================

    private static async Task<IResult> CostLeakageSummary(Database db, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT
                CONCAT('$', FORMAT(COALESCE(SUM(estimated_loss),0),2)) total_estimated_leakage,
                CONCAT('$', FORMAT(COALESCE(SUM(projected_monthly_loss),0),2)) monthly_leakage_projection,
                SUM(status='Open') open_items,
                SUM(severity IN ('High','Critical')) critical_leakage_items,
                SUM(status='Acknowledged') acknowledged_items,
                SUM(status='In Progress') in_progress_items,
                (SELECT COUNT(*) FROM cost_leakage_actions WHERE status='Open') open_actions,
                CONCAT('$', FORMAT(COALESCE((SELECT SUM(estimated_savings) FROM cost_leakage_actions WHERE status='Open'),0),2)) recoverable_savings,
                SUM(category='Idle Time') idle_leakage_count,
                SUM(category='Fuel Anomaly') fuel_anomaly_count,
                SUM(category='Carrier Overcharge') carrier_overcharge_count,
                SUM(category='Underpriced Contract') underpriced_count,
                COUNT(*) total
              FROM cost_leakage_items", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(row ?? new Dictionary<string, object?>()));
    }

    private static Task<IResult> CostLeakageItems(Database db, CancellationToken ct)
        => OkRows(db,
            @"SELECT cli.*,
                     (SELECT COUNT(*) FROM cost_leakage_actions a WHERE a.cost_leakage_item_id=cli.id AND a.status <> 'Cancelled') actions_count,
                     (SELECT ROUND(SUM(a.estimated_savings),2) FROM cost_leakage_actions a WHERE a.cost_leakage_item_id=cli.id) potential_savings
              FROM cost_leakage_items cli
              ORDER BY FIELD(cli.severity,'Critical','High','Medium','Low'), cli.estimated_loss DESC", ct: ct);

    private static async Task<IResult> CostLeakageAcknowledge(long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync("UPDATE cost_leakage_items SET status='Acknowledged' WHERE id=@id", c => c.Parameters.AddWithValue("@id", id), ct);
        await audit.LogAsync("cost.leakage.acknowledged", "CostLeakage", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "Cost leakage item acknowledged"));
    }

    private static async Task<IResult> CostLeakageCreateAction(long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var actionId = await db.InsertAsync(
            @"INSERT INTO cost_leakage_actions (company_id, cost_leakage_item_id, action_title, action_description, estimated_savings, status, assigned_to_user_id, due_at)
              VALUES (1, @itemId, @title, @description, COALESCE(@savings,0), 'Open', @user, @due)",
            c =>
            {
                c.Parameters.AddWithValue("@itemId", id);
                c.Parameters.AddWithValue("@title", Get(body, "actionTitle") ?? "Cost recovery action");
                c.Parameters.AddWithValue("@description", Get(body, "actionDescription"));
                c.Parameters.AddWithValue("@savings", Get(body, "estimatedSavings"));
                c.Parameters.AddWithValue("@user", Get(body, "assignedToUserId") ?? 1);
                c.Parameters.AddWithValue("@due", Get(body, "dueAt"));
            }, ct);
        await db.ExecuteAsync("UPDATE cost_leakage_items SET status='In Progress' WHERE id=@id", c => c.Parameters.AddWithValue("@id", id), ct);
        await audit.LogAsync("cost.leakage.action.created", "CostLeakage", id, ct: ct);
        return Results.Created($"/api/cost-leakage/items/{id}/actions/{actionId}", ApiResponse<object>.Ok(new { id = actionId }, "Cost leakage action created"));
    }

    // ===== BATCH 6 HANDLERS =================================================

    private static async Task<IResult> ComplianceSummary(Database db, CancellationToken ct)
    {
        var violations = await db.QueryAsync("SELECT severity, status, COUNT(*) cnt FROM compliance_violations GROUP BY severity, status", ct: ct);
        var drivers = await db.QueryAsync("SELECT overall_status, COUNT(*) cnt FROM driver_compliance_status GROUP BY overall_status", ct: ct);
        var vehicles = await db.QueryAsync("SELECT overall_status, COUNT(*) cnt FROM vehicle_compliance_status GROUP BY overall_status", ct: ct);
        var elDevices = await db.QueryAsync("SELECT status, COUNT(*) cnt FROM eld_devices WHERE deleted_at IS NULL GROUP BY status", ct: ct);
        var audits = await db.QueryAsync("SELECT status, COUNT(*) cnt FROM compliance_audit_packages GROUP BY status", ct: ct);
        var countries = await db.QueryAsync("SELECT code, name, rtl FROM countries ORDER BY name", ct: ct);
        var profiles = await db.QueryAsync("SELECT * FROM compliance_profiles WHERE is_active=1 ORDER BY country_code, profile_name", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { violations, drivers, vehicles, elDevices, audits, countries, profiles }, "Compliance summary"));
    }

    private static async Task<IResult> HosSummary(Database db, CancellationToken ct)
    {
        var clocks = await db.QueryAsync("SELECT status, COUNT(*) cnt FROM hos_clocks GROUP BY status", ct: ct);
        var logs = await db.QueryAsync("SELECT log_date, COUNT(*) entries, SUM(duration_minutes) total_minutes FROM hos_logs WHERE deleted_at IS NULL GROUP BY log_date ORDER BY log_date DESC LIMIT 7", ct: ct);
        var violations = await db.QueryAsync("SELECT * FROM compliance_violations WHERE category='HOS' AND status IN ('Open','Escalated') ORDER BY FIELD(severity,'Critical','High','Medium','Low') LIMIT 10", ct: ct);
        var eldDevices = await db.QueryAsync("SELECT e.*, v.vehicle_code FROM eld_devices e LEFT JOIN vehicles v ON v.id=e.vehicle_id WHERE e.deleted_at IS NULL ORDER BY FIELD(e.status,'Malfunction','Diagnostic','Active') LIMIT 20", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { clocks, logs, violations, eldDevices }, "HOS summary"));
    }

    private static async Task<IResult> HosCertify(long id, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync("UPDATE hos_logs SET is_certified=1, certified_at=NOW() WHERE id=@id", c => c.Parameters.AddWithValue("@id", id), ct);
        await audit.LogAsync("hos.log_certified", "HosLog", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "HOS log certified"));
    }

    private static async Task<IResult> EldMarkMalfunction(long id, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync(
            "UPDATE eld_devices SET status='Malfunction', malfunction_code=@code, malfunction_description=@desc WHERE id=@id",
            c =>
            {
                c.Parameters.AddWithValue("@id", id);
                c.Parameters.AddWithValue("@code", Get(body, "malfunctionCode") ?? "UNKNOWN");
                c.Parameters.AddWithValue("@desc", Get(body, "malfunctionDescription"));
            }, ct);
        await audit.LogAsync("eld.malfunction", "EldDevice", id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id }, "ELD device marked as malfunction"));
    }

    private static async Task<IResult> CreateAuditPackage(Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var code = $"AUD-{DateTime.UtcNow:yyyy-MM}-{new Random().Next(100, 999)}";
        var pkgId = await db.InsertAsync(
            @"INSERT INTO compliance_audit_packages (package_code,country_code,profile_id,created_by,status,date_range_start,date_range_end,notes)
              VALUES (@code,@country,@profile,@by,'Draft',@start,@end,@notes)",
            c =>
            {
                c.Parameters.AddWithValue("@code", code);
                c.Parameters.AddWithValue("@country", Get(body, "countryCode") ?? "US");
                c.Parameters.AddWithValue("@profile", Get(body, "profileId"));
                c.Parameters.AddWithValue("@by", Get(body, "createdBy") ?? "admin");
                c.Parameters.AddWithValue("@start", Get(body, "dateRangeStart"));
                c.Parameters.AddWithValue("@end", Get(body, "dateRangeEnd"));
                c.Parameters.AddWithValue("@notes", Get(body, "notes"));
            }, ct);
        await audit.LogAsync("compliance.audit_package_created", "AuditPackage", pkgId, ct: ct);
        return Results.Created($"/api/compliance/audit-packages/{pkgId}", ApiResponse<object>.Ok(new { id = pkgId, packageCode = code }, "Audit package created"));
    }

    private static async Task<IResult> UpdateLocaleSettings(HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "settings:manage");
        if (denied is not null) return denied;
        var companyId = GetCompanyId(http);
        await db.ExecuteAsync(
            @"INSERT INTO tenant_locale_settings (id,tenant_id,default_language,default_country,timezone,date_format,currency,distance_unit,volume_unit)
              VALUES (@tenantId,@tenantId,@lang,@country,@tz,@fmt,@curr,@dist,@vol)
              ON DUPLICATE KEY UPDATE default_language=@lang,default_country=@country,timezone=@tz,date_format=@fmt,currency=@curr,distance_unit=@dist,volume_unit=@vol,updated_at=NOW()",
            c =>
            {
                c.Parameters.AddWithValue("@tenantId", companyId);
                c.Parameters.AddWithValue("@lang", Get(body, "defaultLanguage") ?? "en-US");
                c.Parameters.AddWithValue("@country", Get(body, "defaultCountry") ?? "US");
                c.Parameters.AddWithValue("@tz", Get(body, "timezone") ?? "America/New_York");
                c.Parameters.AddWithValue("@fmt", Get(body, "dateFormat") ?? "MM/DD/YYYY");
                c.Parameters.AddWithValue("@curr", Get(body, "currency") ?? "USD");
                c.Parameters.AddWithValue("@dist", Get(body, "distanceUnit") ?? "Miles");
                c.Parameters.AddWithValue("@vol", Get(body, "volumeUnit") ?? "Gallons");
            }, ct);
        await audit.LogAsync("localization.settings_updated", "LocaleSettings", companyId, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { updated = true }, "Locale settings updated"));
    }

    private static async Task<IResult> UpdateUserLocalePreferences(HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "settings:manage");
        if (denied is not null) return denied;
        var companyId = GetCompanyId(http);
        var userId = Convert.ToInt64(http.Items[AuthUserIdItemKey] ?? 0L);
        await db.ExecuteAsync(
            @"INSERT INTO user_locale_preferences (id,user_id,language,country_code,timezone,date_format)
              VALUES (@userId,@userId,@lang,@country,@tz,@fmt)
              ON DUPLICATE KEY UPDATE language=@lang,country_code=@country,timezone=@tz,date_format=@fmt,updated_at=NOW()",
            c =>
            {
                c.Parameters.AddWithValue("@userId", userId > 0 ? userId : companyId);
                c.Parameters.AddWithValue("@lang", Get(body, "language") ?? "en-US");
                c.Parameters.AddWithValue("@country", Get(body, "countryCode"));
                c.Parameters.AddWithValue("@tz", Get(body, "timezone"));
                c.Parameters.AddWithValue("@fmt", Get(body, "dateFormat"));
            }, ct);
        await audit.LogAsync("localization.preference_changed", "UserLocale", userId > 0 ? userId : companyId, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { updated = true }, "User locale preferences updated"));
    }

    private static async Task<IResult> SimpleUpdateStatus(string table, long id, string status, string eventName, Database db, AuditService audit, CancellationToken ct)
    {
        await db.ExecuteAsync($"UPDATE {table} SET status=@s WHERE id=@id", c => { c.Parameters.AddWithValue("@s", status); c.Parameters.AddWithValue("@id", id); }, ct);
        await audit.LogAsync(eventName, table, id, ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { id, status }, $"Status updated to {status}"));
    }

    private sealed record LoginRequest(string Email, string Password);

    // ── Batch 7 handlers ─────────────────────────────────────────────────────

    private static async Task<IResult> ReportsSummary(Database db, CancellationToken ct)
    {
        var catalogCount  = await db.ScalarLongAsync("SELECT COUNT(*) FROM report_catalog WHERE status='Active'", ct: ct);
        var runsToday     = await db.ScalarLongAsync("SELECT COUNT(*) FROM report_runs WHERE DATE(started_at)=CURDATE()", ct: ct);
        var scheduled     = await db.ScalarLongAsync("SELECT COUNT(*) FROM scheduled_reports WHERE status='Active'", ct: ct);
        var exports       = await db.ScalarLongAsync("SELECT COUNT(*) FROM report_exports WHERE status='Pending'", ct: ct);
        var categories    = await db.QueryAsync("SELECT report_category, COUNT(*) cnt FROM report_catalog WHERE status='Active' GROUP BY report_category ORDER BY cnt DESC", ct: ct);
        var recentRuns    = await db.QueryAsync("SELECT * FROM report_runs ORDER BY started_at DESC LIMIT 5", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { catalogCount, runsToday, scheduled, pendingExports = exports, categories, recentRuns }, "Reports summary"));
    }

    private static async Task<IResult> ReportRun(HttpContext http, string key, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "reports:manage");
        if (denied is not null) return denied;
        var catalog = await db.QueryAsync("SELECT * FROM report_catalog WHERE report_key=@key LIMIT 1", c => c.Parameters.AddWithValue("@key", key), ct);
        var name    = catalog.Count > 0 ? String(catalog[0], "report_name") : key;
        var rows    = new Random().Next(12, 800);
        var runId   = await db.InsertAsync(
            @"INSERT INTO report_runs (report_key,report_name,run_by_name,status,row_count,completed_at,filters_json)
              VALUES (@key,@name,'Admin','Completed',@rows,NOW(),@filt)",
            c =>
            {
                c.Parameters.AddWithValue("@key",  key);
                c.Parameters.AddWithValue("@name", name);
                c.Parameters.AddWithValue("@rows", rows);
                c.Parameters.AddWithValue("@filt", System.Text.Json.JsonSerializer.Serialize(body));
            }, ct);
        await audit.LogAsync("report.run_completed", "ReportRun", runId, ct: ct);
        return Results.Created($"/api/reports/runs/{runId}", ApiResponse<object>.Ok(new { id = runId, reportKey = key, rowCount = rows, status = "Completed" }, "Report run completed"));
    }

    private static async Task<IResult> CreateScheduledReport(HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "reports:manage");
        if (denied is not null) return denied;
        var id = await db.InsertAsync(
            @"INSERT INTO scheduled_reports (report_key,report_name,schedule_name,frequency,recipients_json,status,next_run_at)
              VALUES (@key,@name,@sched,@freq,@rec,'Active',DATE_ADD(NOW(), INTERVAL 7 DAY))",
            c =>
            {
                c.Parameters.AddWithValue("@key",   Get(body, "reportKey") ?? "custom");
                c.Parameters.AddWithValue("@name",  Get(body, "reportName") ?? "Custom Report");
                c.Parameters.AddWithValue("@sched", Get(body, "scheduleName") ?? "New Schedule");
                c.Parameters.AddWithValue("@freq",  Get(body, "frequency") ?? "Weekly");
                c.Parameters.AddWithValue("@rec",   System.Text.Json.JsonSerializer.Serialize(Get(body, "recipients") ?? new[] { "admin@opstrax.com" }));
            }, ct);
        await audit.LogAsync("scheduled_report.created", "ScheduledReport", id, ct: ct);
        return Results.Created($"/api/reports/scheduled/{id}", ApiResponse<object>.Ok(new { id }, "Scheduled report created"));
    }

    private static async Task<IResult> CreateReportExport(HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "reports:manage");
        if (denied is not null) return denied;
        var id = await db.InsertAsync(
            @"INSERT INTO report_exports (report_key,report_name,export_format,run_by_name,status,requested_at)
              VALUES (@key,@name,@fmt,'Admin','Pending',NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@key",  Get(body, "reportKey") ?? "custom");
                c.Parameters.AddWithValue("@name", Get(body, "reportName") ?? "Export");
                c.Parameters.AddWithValue("@fmt",  Get(body, "exportFormat") ?? "CSV");
            }, ct);
        await audit.LogAsync("report.export_requested", "ReportExport", id, ct: ct);
        return Results.Created($"/api/reports/exports/{id}", ApiResponse<object>.Ok(new { id }, "Export request created"));
    }

    private static async Task<IResult> KpiSummary(Database db, CancellationToken ct)
    {
        var total    = await db.ScalarLongAsync("SELECT COUNT(*) FROM kpi_metrics WHERE deleted_at IS NULL", ct: ct);
        var onTarget = await db.ScalarLongAsync("SELECT COUNT(*) FROM kpi_metrics WHERE status='On Target' AND deleted_at IS NULL", ct: ct);
        var atRisk   = await db.ScalarLongAsync("SELECT COUNT(*) FROM kpi_metrics WHERE status='At Risk' AND deleted_at IS NULL", ct: ct);
        var critical = await db.ScalarLongAsync("SELECT COUNT(*) FROM kpi_metrics WHERE status='Critical' AND deleted_at IS NULL", ct: ct);
        var drifting = await db.QueryAsync("SELECT * FROM kpi_metrics WHERE status IN ('At Risk','Critical') AND deleted_at IS NULL ORDER BY FIELD(status,'Critical','At Risk') LIMIT 10", ct: ct);
        var byCategory = await db.QueryAsync("SELECT category, COUNT(*) total, SUM(status='On Target') on_target, SUM(status='At Risk') at_risk, SUM(status='Critical') critical FROM kpi_metrics WHERE deleted_at IS NULL GROUP BY category", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { total, onTarget, atRisk, critical, drifting, byCategory }, "KPI summary"));
    }

    private static async Task<IResult> SlaSummary(Database db, CancellationToken ct)
    {
        var total    = await db.ScalarLongAsync("SELECT COUNT(*) FROM sla_records WHERE deleted_at IS NULL", ct: ct);
        var met      = await db.ScalarLongAsync("SELECT COUNT(*) FROM sla_records WHERE status='Met' AND deleted_at IS NULL", ct: ct);
        var atRisk   = await db.ScalarLongAsync("SELECT COUNT(*) FROM sla_records WHERE status='At Risk' AND deleted_at IS NULL", ct: ct);
        var breached = await db.ScalarLongAsync("SELECT COUNT(*) FROM sla_records WHERE status='Breached' AND deleted_at IS NULL", ct: ct);
        var breaches = await db.QueryAsync(@"SELECT sb.*, sr.sla_name, sr.sla_type, c.company_name customer_name
            FROM sla_breaches sb JOIN sla_records sr ON sr.id=sb.sla_record_id LEFT JOIN customers c ON c.id=sr.customer_id
            WHERE sb.status='Open' ORDER BY sb.breach_detected_at DESC LIMIT 10", ct: ct);
        var byType   = await db.QueryAsync("SELECT sla_type, COUNT(*) total, SUM(status='Met') met, SUM(status='Breached') breached FROM sla_records WHERE deleted_at IS NULL GROUP BY sla_type", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { total, met, atRisk, breached, openBreaches = breaches, byType }, "SLA summary"));
    }

    private static async Task<IResult> AuditLogs(HttpRequest req, Database db, CancellationToken ct)
    {
        var module    = req.Query["module"].FirstOrDefault();
        var action    = req.Query["action"].FirstOrDefault();
        var severity  = req.Query["severity"].FirstOrDefault();
        var search    = req.Query["search"].FirstOrDefault();

        var logs = await db.QueryAsync(
            @"SELECT * FROM audit_logs 
              WHERE (@module IS NULL OR module_key = @module)
              AND (@action IS NULL OR action_name LIKE CONCAT('%', @action, '%'))
              AND (@severity IS NULL OR severity = @severity)
              AND (@search IS NULL OR actor_name LIKE CONCAT('%', @search, '%') OR entity_name LIKE CONCAT('%', @search, '%') OR action_name LIKE CONCAT('%', @search, '%'))
              ORDER BY created_at DESC LIMIT 100",
            c => {
                c.Parameters.AddWithValue("@module", string.IsNullOrWhiteSpace(module) ? DBNull.Value : module);
                c.Parameters.AddWithValue("@action", string.IsNullOrWhiteSpace(action) ? DBNull.Value : action);
                c.Parameters.AddWithValue("@severity", string.IsNullOrWhiteSpace(severity) ? DBNull.Value : severity);
                c.Parameters.AddWithValue("@search", string.IsNullOrWhiteSpace(search) ? DBNull.Value : search);
            }, ct);
        return Results.Ok(ApiResponse<object>.Ok(logs, "Audit logs"));
    }

    private static async Task<IResult> CreateAuditExportRequest(HttpContext http, Dictionary<string, object?> body, Database db, AuditService audit, CancellationToken ct)
    {
        var denied = RequirePermission(http, "reports:manage");
        if (denied is not null) return denied;
        var id = await db.InsertAsync(
            @"INSERT INTO audit_export_requests (requested_by_name,date_range_start,date_range_end,filters_json,export_format,status,requested_at)
              VALUES (@by,@start,@end,@filt,@fmt,'Pending',NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@by",    Get(body, "requestedByName") ?? "Admin");
                c.Parameters.AddWithValue("@start", Get(body, "dateRangeStart"));
                c.Parameters.AddWithValue("@end",   Get(body, "dateRangeEnd"));
                c.Parameters.AddWithValue("@filt",  System.Text.Json.JsonSerializer.Serialize(body));
                c.Parameters.AddWithValue("@fmt",   Get(body, "exportFormat") ?? "CSV");
            }, ct);
        await audit.LogAsync("audit.export_requested", "AuditExport", id, ct: ct);
        return Results.Created($"/api/audit/export-requests/{id}", ApiResponse<object>.Ok(new { id }, "Audit export request created"));
    }

    private static async Task<IResult> ExecutiveSummary(Database db, CancellationToken ct)
    {
        var latest     = await db.QueryAsync("SELECT * FROM executive_snapshots WHERE deleted_at IS NULL ORDER BY snapshot_date DESC LIMIT 1", ct: ct);
        var trend      = await db.QueryAsync("SELECT snapshot_date, fleet_health_score, safety_score, compliance_score, financial_score, overall_score FROM executive_snapshots WHERE deleted_at IS NULL ORDER BY snapshot_date DESC LIMIT 14", ct: ct);
        var kpiCrit    = await db.ScalarLongAsync("SELECT COUNT(*) FROM kpi_metrics WHERE status='Critical' AND deleted_at IS NULL", ct: ct);
        var slaBreach  = await db.ScalarLongAsync("SELECT COUNT(*) FROM sla_breaches WHERE status='Open'", ct: ct);
        var auditToday = await db.ScalarLongAsync("SELECT COUNT(*) FROM audit_logs WHERE DATE(created_at)=CURDATE()", ct: ct);
        var aiRecs     = await db.QueryAsync("SELECT * FROM ai_recommendations WHERE module_key='executive' ORDER BY score DESC LIMIT 5", ct: ct);
        return Results.Ok(ApiResponse<object>.Ok(new { latest, trend, kpiCritical = kpiCrit, openSlaBreaches = slaBreach, auditActionsToday = auditToday, aiRecommendations = aiRecs }, "Executive summary"));
    }

    private static string String(System.Collections.Generic.IDictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";

    private static IResult AboutPlatform()
    {
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            fullProductName  = "OpsTrax Transport Management Solution",
            shortName        = "OpsTrax",
            developer        = "Kode Kinetics",
            version          = "Enterprise Demo Build",
            environment      = "Local / Demo",
            companyDescription = "Kode Kinetics is a technology company specializing in custom software development, AI automation, SaaS platforms, enterprise integrations, cloud solutions, web/mobile applications, and digital transformation systems for modern organizations.",
            disclaimer       = "OpsTrax provides compliance management, monitoring, and audit-readiness tools. Final regulatory compliance remains the carrier's responsibility. ELD certification depends on the connected ELD provider/device and applicable country requirements.",
            support = new
            {
                website = "www.kodekinetics.com",
                email   = "info@kodekinetics.com",
                phone   = "+1 571 430 5333"
            },
            modules = new[]
            {
                "Fleet Command Center", "Live Control Tower", "Dispatch Board", "Jobs & Orders",
                "Route Planning", "Driver & Vehicle Management", "Maintenance & Work Orders",
                "DVIR / Inspections", "Safety & AI Dashcam", "Compliance & HOS/ELD Framework",
                "Fuel, Expenses & Cost Intelligence", "Reports & Analytics",
                "Integrations & API Readiness", "AI Copilot"
            }
        }, "Platform info"));
    }

    private static async Task<IResult> AboutHealthSummary(Database db, CancellationToken ct)
    {
        long moduleCount;
        string dbStatus;
        try
        {
            moduleCount = await db.ScalarLongAsync("SELECT COUNT(DISTINCT table_name) FROM information_schema.tables WHERE table_schema=DATABASE()", ct: ct);
            dbStatus    = "Connected";
        }
        catch
        {
            moduleCount = 35;
            dbStatus    = "Degraded";
        }

        return Results.Ok(ApiResponse<object>.Ok(new
        {
            apiStatus        = "Connected",
            databaseStatus   = dbStatus,
            nodeEventsStatus = "Connected",
            moduleCount      = moduleCount > 0 ? $"{moduleCount} tables" : "35+",
            version          = "Enterprise Demo Build",
            environment      = "Local / Demo"
        }, "Health summary"));
    }
}
