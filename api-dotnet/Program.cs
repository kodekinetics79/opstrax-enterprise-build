using MySqlConnector;
using Opstrax.Api.Infrastructure;
using Opstrax.Api.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<Database>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultCors", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:10000"];
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();
app.UseCors("DefaultCors");

var tenantCode = builder.Configuration["DefaultTenantCode"] ?? "KK-DEMO";

static void AddTenant(MySqlCommand cmd, string tenantCode) => cmd.Parameters.AddWithValue("@tenantCode", tenantCode);

async Task<long> GetTenantId(Database db, CancellationToken ct)
{
    return await db.ScalarAsync<long>(
        "SELECT id FROM tenants WHERE tenant_code = @tenantCode LIMIT 1",
        cmd => AddTenant(cmd, tenantCode),
        ct);
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "opstrax-dotnet-api", utc = DateTime.UtcNow }));

app.MapGet("/api/dashboard/summary", async (Database db, CancellationToken ct) =>
{
    var tenantId = await GetTenantId(db, ct);

    var activeVehicles = await db.ScalarAsync<int>("SELECT COUNT(*) FROM vehicles WHERE tenant_id=@tenantId AND status <> 'Inactive'", cmd => cmd.Parameters.AddWithValue("@tenantId", tenantId), ct);
    var jobsInProgress = await db.ScalarAsync<int>("SELECT COUNT(*) FROM jobs WHERE tenant_id=@tenantId AND status IN ('In Progress', 'At Risk', 'Scheduled')", cmd => cmd.Parameters.AddWithValue("@tenantId", tenantId), ct);
    var avgSafety = await db.ScalarAsync<decimal>("SELECT COALESCE(AVG(safety_score), 0) FROM drivers WHERE tenant_id=@tenantId", cmd => cmd.Parameters.AddWithValue("@tenantId", tenantId), ct);
    var maintenanceDue = await db.ScalarAsync<int>("SELECT COUNT(*) FROM maintenance_work_orders WHERE tenant_id=@tenantId AND status <> 'Closed'", cmd => cmd.Parameters.AddWithValue("@tenantId", tenantId), ct);
    var complianceRisks = await db.ScalarAsync<int>("SELECT COUNT(*) FROM compliance_documents WHERE tenant_id=@tenantId AND (status <> 'Valid' OR expiry_date <= DATE_ADD(CURDATE(), INTERVAL 30 DAY))", cmd => cmd.Parameters.AddWithValue("@tenantId", tenantId), ct);
    var fuelCost = await db.ScalarAsync<decimal>("SELECT COALESCE(SUM(total_cost), 0) FROM fuel_transactions WHERE tenant_id=@tenantId AND transaction_time >= DATE_SUB(NOW(), INTERVAL 7 DAY)", cmd => cmd.Parameters.AddWithValue("@tenantId", tenantId), ct);

    var insights = await db.QueryAsync(
        "SELECT id, insight_type, title, body, severity, status, created_at FROM ai_insights WHERE tenant_id=@tenantId ORDER BY created_at DESC LIMIT 5",
        r => new AiInsightDto(r.GetInt64("id"), r.GetString("insight_type"), r.GetString("title"), r.GetString("body"), r.GetString("severity"), r.GetString("status"), r.GetDateTime("created_at")),
        cmd => cmd.Parameters.AddWithValue("@tenantId", tenantId),
        ct);

    return Results.Ok(new DashboardSummary(activeVehicles, jobsInProgress, avgSafety, maintenanceDue, complianceRisks, fuelCost, insights));
});

app.MapGet("/api/vehicles", async (Database db, CancellationToken ct) =>
{
    var tenantId = await GetTenantId(db, ct);
    var rows = await db.QueryAsync(
        @"SELECT v.id, v.vehicle_code, v.type, v.make, v.model, v.year, v.vin, v.plate_number, v.odometer_miles, v.status, d.full_name assigned_driver
          FROM vehicles v
          LEFT JOIN drivers d ON d.id = v.assigned_driver_id
          WHERE v.tenant_id=@tenantId
          ORDER BY v.vehicle_code",
        r => new VehicleDto(
            r.GetInt64("id"),
            r.GetString("vehicle_code"),
            r.GetString("type"),
            r.IsDBNull("make") ? null : r.GetString("make"),
            r.IsDBNull("model") ? null : r.GetString("model"),
            r.IsDBNull("year") ? null : r.GetInt32("year"),
            r.IsDBNull("vin") ? null : r.GetString("vin"),
            r.IsDBNull("plate_number") ? null : r.GetString("plate_number"),
            r.GetDecimal("odometer_miles"),
            r.GetString("status"),
            r.IsDBNull("assigned_driver") ? null : r.GetString("assigned_driver")),
        cmd => cmd.Parameters.AddWithValue("@tenantId", tenantId),
        ct);
    return Results.Ok(rows);
});

app.MapPost("/api/vehicles", async (CreateVehicleRequest request, Database db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.VehicleCode) || string.IsNullOrWhiteSpace(request.Type))
    {
        return Results.BadRequest(new { error = "VehicleCode and Type are required." });
    }

    var tenantId = await GetTenantId(db, ct);
    await db.ExecuteAsync(
        @"INSERT INTO vehicles (tenant_id, vehicle_code, type, make, model, year, vin, plate_number, status)
          VALUES (@tenantId, @vehicleCode, @type, @make, @model, @year, @vin, @plateNumber, @status)",
        cmd =>
        {
            cmd.Parameters.AddWithValue("@tenantId", tenantId);
            cmd.Parameters.AddWithValue("@vehicleCode", request.VehicleCode);
            cmd.Parameters.AddWithValue("@type", request.Type);
            cmd.Parameters.AddWithValue("@make", request.Make);
            cmd.Parameters.AddWithValue("@model", request.Model);
            cmd.Parameters.AddWithValue("@year", request.Year);
            cmd.Parameters.AddWithValue("@vin", request.Vin);
            cmd.Parameters.AddWithValue("@plateNumber", request.PlateNumber);
            cmd.Parameters.AddWithValue("@status", string.IsNullOrWhiteSpace(request.Status) ? "Active" : request.Status);
        }, ct);
    return Results.Created($"/api/vehicles/{request.VehicleCode}", request);
});

app.MapGet("/api/drivers", async (Database db, CancellationToken ct) =>
{
    var tenantId = await GetTenantId(db, ct);
    var rows = await db.QueryAsync(
        "SELECT id, driver_code, full_name, phone, email, license_number, license_expiry, safety_score, status FROM drivers WHERE tenant_id=@tenantId ORDER BY full_name",
        r => new DriverDto(
            r.GetInt64("id"),
            r.GetString("driver_code"),
            r.GetString("full_name"),
            r.IsDBNull("phone") ? null : r.GetString("phone"),
            r.IsDBNull("email") ? null : r.GetString("email"),
            r.IsDBNull("license_number") ? null : r.GetString("license_number"),
            r.IsDBNull("license_expiry") ? null : r.GetDateTime("license_expiry"),
            r.GetDecimal("safety_score"),
            r.GetString("status")),
        cmd => cmd.Parameters.AddWithValue("@tenantId", tenantId),
        ct);
    return Results.Ok(rows);
});

app.MapPost("/api/drivers", async (CreateDriverRequest request, Database db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.DriverCode) || string.IsNullOrWhiteSpace(request.FullName))
    {
        return Results.BadRequest(new { error = "DriverCode and FullName are required." });
    }

    var tenantId = await GetTenantId(db, ct);
    await db.ExecuteAsync(
        @"INSERT INTO drivers (tenant_id, driver_code, full_name, phone, email, license_number, license_expiry, status)
          VALUES (@tenantId, @driverCode, @fullName, @phone, @email, @licenseNumber, @licenseExpiry, @status)",
        cmd =>
        {
            cmd.Parameters.AddWithValue("@tenantId", tenantId);
            cmd.Parameters.AddWithValue("@driverCode", request.DriverCode);
            cmd.Parameters.AddWithValue("@fullName", request.FullName);
            cmd.Parameters.AddWithValue("@phone", request.Phone);
            cmd.Parameters.AddWithValue("@email", request.Email);
            cmd.Parameters.AddWithValue("@licenseNumber", request.LicenseNumber);
            cmd.Parameters.AddWithValue("@licenseExpiry", request.LicenseExpiry);
            cmd.Parameters.AddWithValue("@status", string.IsNullOrWhiteSpace(request.Status) ? "Available" : request.Status);
        }, ct);
    return Results.Created($"/api/drivers/{request.DriverCode}", request);
});

app.MapGet("/api/jobs", async (Database db, CancellationToken ct) =>
{
    var tenantId = await GetTenantId(db, ct);
    var rows = await db.QueryAsync(
        @"SELECT j.id, j.job_code, j.customer_name, j.job_type, j.pickup_address, j.dropoff_address, j.scheduled_start, j.scheduled_end, j.status, j.priority,
                 v.vehicle_code, d.full_name driver_name
          FROM jobs j
          LEFT JOIN vehicles v ON v.id = j.assigned_vehicle_id
          LEFT JOIN drivers d ON d.id = j.assigned_driver_id
          WHERE j.tenant_id=@tenantId
          ORDER BY j.scheduled_start DESC, j.id DESC",
        r => new JobDto(
            r.GetInt64("id"), r.GetString("job_code"), r.GetString("customer_name"), r.GetString("job_type"),
            r.IsDBNull("pickup_address") ? null : r.GetString("pickup_address"),
            r.IsDBNull("dropoff_address") ? null : r.GetString("dropoff_address"),
            r.IsDBNull("scheduled_start") ? null : r.GetDateTime("scheduled_start"),
            r.IsDBNull("scheduled_end") ? null : r.GetDateTime("scheduled_end"),
            r.GetString("status"), r.GetString("priority"),
            r.IsDBNull("vehicle_code") ? null : r.GetString("vehicle_code"),
            r.IsDBNull("driver_name") ? null : r.GetString("driver_name")),
        cmd => cmd.Parameters.AddWithValue("@tenantId", tenantId),
        ct);
    return Results.Ok(rows);
});

app.MapPost("/api/jobs", async (CreateJobRequest request, Database db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.JobCode) || string.IsNullOrWhiteSpace(request.CustomerName) || string.IsNullOrWhiteSpace(request.JobType))
    {
        return Results.BadRequest(new { error = "JobCode, CustomerName, and JobType are required." });
    }

    var tenantId = await GetTenantId(db, ct);
    await db.ExecuteAsync(
        @"INSERT INTO jobs
            (tenant_id, job_code, customer_name, job_type, pickup_address, dropoff_address, scheduled_start, scheduled_end, priority, assigned_vehicle_id, assigned_driver_id)
          VALUES
            (@tenantId, @jobCode, @customerName, @jobType, @pickupAddress, @dropoffAddress, @scheduledStart, @scheduledEnd, @priority, @assignedVehicleId, @assignedDriverId)",
        cmd =>
        {
            cmd.Parameters.AddWithValue("@tenantId", tenantId);
            cmd.Parameters.AddWithValue("@jobCode", request.JobCode);
            cmd.Parameters.AddWithValue("@customerName", request.CustomerName);
            cmd.Parameters.AddWithValue("@jobType", request.JobType);
            cmd.Parameters.AddWithValue("@pickupAddress", request.PickupAddress);
            cmd.Parameters.AddWithValue("@dropoffAddress", request.DropoffAddress);
            cmd.Parameters.AddWithValue("@scheduledStart", request.ScheduledStart);
            cmd.Parameters.AddWithValue("@scheduledEnd", request.ScheduledEnd);
            cmd.Parameters.AddWithValue("@priority", string.IsNullOrWhiteSpace(request.Priority) ? "Normal" : request.Priority);
            cmd.Parameters.AddWithValue("@assignedVehicleId", request.AssignedVehicleId);
            cmd.Parameters.AddWithValue("@assignedDriverId", request.AssignedDriverId);
        }, ct);
    return Results.Created($"/api/jobs/{request.JobCode}", request);
});

app.MapGet("/api/assets", async (Database db, CancellationToken ct) =>
{
    var tenantId = await GetTenantId(db, ct);
    var rows = await db.QueryAsync(
        @"SELECT a.id, a.asset_code, a.asset_type, a.name, a.status, a.current_location, v.vehicle_code assigned_vehicle
          FROM assets a
          LEFT JOIN vehicles v ON v.id = a.assigned_vehicle_id
          WHERE a.tenant_id=@tenantId
          ORDER BY a.asset_code",
        r => new AssetDto(
            r.GetInt64("id"),
            r.GetString("asset_code"),
            r.GetString("asset_type"),
            r.GetString("name"),
            r.GetString("status"),
            r.IsDBNull("current_location") ? null : r.GetString("current_location"),
            r.IsDBNull("assigned_vehicle") ? null : r.GetString("assigned_vehicle")),
        cmd => cmd.Parameters.AddWithValue("@tenantId", tenantId),
        ct);
    return Results.Ok(rows);
});

app.MapGet("/api/maintenance/work-orders", async (Database db, CancellationToken ct) =>
{
    var tenantId = await GetTenantId(db, ct);
    var rows = await db.QueryAsync(
        @"SELECT wo.id, wo.work_order_code, v.vehicle_code, wo.title, wo.priority, wo.status, wo.due_date, wo.estimated_cost
          FROM maintenance_work_orders wo
          JOIN vehicles v ON v.id = wo.vehicle_id
          WHERE wo.tenant_id=@tenantId
          ORDER BY FIELD(wo.priority, 'Critical', 'High', 'Normal', 'Low'), wo.due_date",
        r => new WorkOrderDto(
            r.GetInt64("id"), r.GetString("work_order_code"), r.GetString("vehicle_code"), r.GetString("title"),
            r.GetString("priority"), r.GetString("status"),
            r.IsDBNull("due_date") ? null : r.GetDateTime("due_date"),
            r.IsDBNull("estimated_cost") ? null : r.GetDecimal("estimated_cost")),
        cmd => cmd.Parameters.AddWithValue("@tenantId", tenantId),
        ct);
    return Results.Ok(rows);
});

app.MapPost("/api/maintenance/work-orders", async (CreateWorkOrderRequest request, Database db, CancellationToken ct) =>
{
    if (request.VehicleId <= 0 || string.IsNullOrWhiteSpace(request.WorkOrderCode) || string.IsNullOrWhiteSpace(request.Title))
    {
        return Results.BadRequest(new { error = "VehicleId, WorkOrderCode, and Title are required." });
    }

    var tenantId = await GetTenantId(db, ct);
    await db.ExecuteAsync(
        @"INSERT INTO maintenance_work_orders
            (tenant_id, vehicle_id, work_order_code, title, priority, due_date, estimated_cost)
          VALUES
            (@tenantId, @vehicleId, @workOrderCode, @title, @priority, @dueDate, @estimatedCost)",
        cmd =>
        {
            cmd.Parameters.AddWithValue("@tenantId", tenantId);
            cmd.Parameters.AddWithValue("@vehicleId", request.VehicleId);
            cmd.Parameters.AddWithValue("@workOrderCode", request.WorkOrderCode);
            cmd.Parameters.AddWithValue("@title", request.Title);
            cmd.Parameters.AddWithValue("@priority", string.IsNullOrWhiteSpace(request.Priority) ? "Normal" : request.Priority);
            cmd.Parameters.AddWithValue("@dueDate", request.DueDate);
            cmd.Parameters.AddWithValue("@estimatedCost", request.EstimatedCost);
        }, ct);
    return Results.Created($"/api/maintenance/work-orders/{request.WorkOrderCode}", request);
});

app.MapGet("/api/financials/fuel-transactions", async (Database db, CancellationToken ct) =>
{
    var tenantId = await GetTenantId(db, ct);
    var rows = await db.QueryAsync(
        @"SELECT ft.id, v.vehicle_code, ft.gallons, ft.total_cost, ft.fuel_station, ft.transaction_time
          FROM fuel_transactions ft
          JOIN vehicles v ON v.id = ft.vehicle_id
          WHERE ft.tenant_id=@tenantId
          ORDER BY ft.transaction_time DESC",
        r => new FuelTransactionDto(
            r.GetInt64("id"),
            r.GetString("vehicle_code"),
            r.GetDecimal("gallons"),
            r.GetDecimal("total_cost"),
            r.IsDBNull("fuel_station") ? null : r.GetString("fuel_station"),
            r.GetDateTime("transaction_time")),
        cmd => cmd.Parameters.AddWithValue("@tenantId", tenantId),
        ct);
    return Results.Ok(rows);
});

app.MapGet("/api/location-events/latest", async (Database db, CancellationToken ct) =>
{
    var tenantId = await GetTenantId(db, ct);
    var rows = await db.QueryAsync(
        @"SELECT le.id, le.vehicle_code, le.driver_code, le.lat, le.lng, le.speed_mph, le.heading, le.event_type, le.event_time
          FROM location_events le
          INNER JOIN (
            SELECT vehicle_code, MAX(event_time) max_event_time
            FROM location_events
            WHERE tenant_id=@tenantId
            GROUP BY vehicle_code
          ) latest ON latest.vehicle_code = le.vehicle_code AND latest.max_event_time = le.event_time
          WHERE le.tenant_id=@tenantId
          ORDER BY le.event_time DESC",
        r => new LocationEventDto(
            r.GetInt64("id"),
            r.IsDBNull("vehicle_code") ? null : r.GetString("vehicle_code"),
            r.IsDBNull("driver_code") ? null : r.GetString("driver_code"),
            r.GetDecimal("lat"), r.GetDecimal("lng"), r.GetDecimal("speed_mph"),
            r.IsDBNull("heading") ? null : r.GetDecimal("heading"),
            r.GetString("event_type"), r.GetDateTime("event_time")),
        cmd => cmd.Parameters.AddWithValue("@tenantId", tenantId),
        ct);
    return Results.Ok(rows);
});

app.MapGet("/api/ai/insights", async (Database db, CancellationToken ct) =>
{
    var tenantId = await GetTenantId(db, ct);
    var rows = await db.QueryAsync(
        "SELECT id, insight_type, title, body, severity, status, created_at FROM ai_insights WHERE tenant_id=@tenantId ORDER BY created_at DESC",
        r => new AiInsightDto(r.GetInt64("id"), r.GetString("insight_type"), r.GetString("title"), r.GetString("body"), r.GetString("severity"), r.GetString("status"), r.GetDateTime("created_at")),
        cmd => cmd.Parameters.AddWithValue("@tenantId", tenantId),
        ct);
    return Results.Ok(rows);
});

app.Run();
