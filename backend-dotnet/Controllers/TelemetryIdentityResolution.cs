using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

public static partial class EndpointMappings
{
    private sealed record TelemetryIdentityContext(
        long InstallationId,
        long VehicleId,
        long? AssignmentId,
        long? TripId,
        long? DriverId,
        long? VehicleBranchId,
        bool IsCurrentInstallation);

    // Called only inside the same system transaction that reserves replay and writes
    // telemetry. Device lifecycle, event-time installation, and dispatch attribution
    // therefore cannot change between authorization and persistence.
    private static async Task<TelemetryIdentityContext?> ResolveTelemetryIdentityAsync(
        Database db, long companyId, long deviceId, DateTimeOffset attributionTime, CancellationToken ct)
    {
        var resolved = await TelemetryIdentityResolver.ResolveAsync(db, companyId, deviceId, attributionTime, ct);
        return resolved is null ? null : new TelemetryIdentityContext(
            resolved.InstallationId,
            resolved.VehicleId,
            resolved.AssignmentId,
            resolved.TripId,
            resolved.DriverId,
            resolved.VehicleBranchId,
            resolved.IsCurrentInstallation);
    }
}
