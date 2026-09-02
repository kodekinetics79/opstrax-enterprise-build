using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

internal sealed record ResolvedTelemetryIdentity(
    long InstallationId,
    long VehicleId,
    long? AssignmentId,
    long? TripId,
    long? DriverId,
    long? VehicleBranchId,
    bool IsCurrentInstallation);

// One governed identity resolver for every backend telemetry ingress. The caller must
// invoke this inside the same system transaction that persists the event so the device
// lifecycle and effective-dated installation cannot change between attribution and write.
internal static class TelemetryIdentityResolver
{
    internal static async Task<ResolvedTelemetryIdentity?> ResolveAsync(
        Database db, long companyId, long deviceId, DateTimeOffset attributionTime, CancellationToken ct)
    {
        var deviceRows = await db.QueryAsync(
            @"SELECT id,status,device_state FROM eld_devices
              WHERE id=@did AND company_id=@cid AND deleted_at IS NULL FOR UPDATE",
            c => { c.Parameters.AddWithValue("@did", deviceId); c.Parameters.AddWithValue("@cid", companyId); }, ct);
        if (deviceRows.Count != 1) return null;
        var status = deviceRows[0].GetValueOrDefault("status")?.ToString()?.Trim().ToLowerInvariant();
        var state = deviceRows[0].GetValueOrDefault("deviceState")?.ToString()?.Trim().ToLowerInvariant();
        if (status is not ("active" or "provisioning" or "pending") ||
            state is "suspended" or "quarantined" or "lost" or "decommissioning" or "decommissioned" or "retired")
            return null;

        var installations = await db.QueryAsync(
            @"SELECT i.id,i.vehicle_id,v.branch_id,
                     (i.effective_to IS NULL AND i.status IN ('Installed','Verified')) is_current
              FROM device_installations i
              JOIN vehicles v ON v.id=i.vehicle_id AND v.company_id=i.company_id AND v.deleted_at IS NULL
              WHERE i.company_id=@cid AND i.device_id=@did
                AND (i.status IN ('Installed','Verified') OR (i.status='Removed' AND i.effective_to IS NOT NULL))
                AND i.effective_from<=@at AND (i.effective_to IS NULL OR i.effective_to>@at)
              ORDER BY i.effective_from DESC,i.id DESC LIMIT 2",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@did", deviceId);
                c.Parameters.AddWithValue("@at", attributionTime);
            }, ct);
        if (installations.Count != 1) return null;

        var installation = installations[0];
        var vehicleId = Convert.ToInt64(installation["vehicleId"]);
        var assignments = await db.QueryAsync(
            @"SELECT id,trip_id,driver_id
              FROM dispatch_assignments
              WHERE company_id=@cid AND vehicle_id=@vid AND assigned_at<=@at
                AND COALESCE(LEAST(actual_delivery_at,completed_at,cancelled_at),'infinity'::timestamptz)>@at
                AND (assignment_status NOT IN ('delivered','cancelled')
                     OR actual_delivery_at IS NOT NULL OR completed_at IS NOT NULL OR cancelled_at IS NOT NULL)
              ORDER BY assigned_at DESC,id DESC LIMIT 2",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@vid", vehicleId);
                c.Parameters.AddWithValue("@at", attributionTime);
            }, ct);
        if (assignments.Count > 1) return null;
        var assignment = assignments.FirstOrDefault();

        static long? OptionalLong(Dictionary<string, object?>? row, string key) =>
            row?.GetValueOrDefault(key) is { } value and not DBNull ? Convert.ToInt64(value) : null;

        return new ResolvedTelemetryIdentity(
            Convert.ToInt64(installation["id"]),
            vehicleId,
            OptionalLong(assignment, "id"),
            OptionalLong(assignment, "tripId"),
            OptionalLong(assignment, "driverId"),
            OptionalLong(installation, "branchId"),
            Convert.ToBoolean(installation["isCurrent"]));
    }
}
