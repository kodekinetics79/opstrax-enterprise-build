using Zayra.Api.Models;

namespace Zayra.Api.Application.Performance;

public interface IPerformanceService
{
    /// <summary>Compute and persist weighted final score for a review from its component scores.</summary>
    Task<decimal> CalculateAndSaveFinalScoreAsync(Guid tenantId, Guid reviewId, CancellationToken ct = default);

    /// <summary>Derive rating label (e.g. "Outstanding") from a numeric score using template rating labels JSON.</summary>
    string GetRatingLabel(decimal score, string ratingLabelsJson);

    /// <summary>Build score breakdown rows for a review and upsert them.</summary>
    Task SaveScoreBreakdownAsync(Guid tenantId, Guid reviewId, CancellationToken ct = default);

    /// <summary>Append an immutable audit entry for any score or status change.</summary>
    Task LogAuditAsync(Guid tenantId, string entityType, string entityId,
        string action, string oldValue, string newValue, string reason,
        Guid? userId, string performedByName, CancellationToken ct = default);

    /// <summary>Enroll all active employees matching a cycle's eligibility criteria.</summary>
    Task<int> EnrollEmployeesAsync(Guid tenantId, Guid cycleId, CancellationToken ct = default);

    /// <summary>Auto-compute attendance score for a review from attendance_records.</summary>
    Task<decimal> ComputeAttendanceScoreAsync(Guid tenantId, int employeeId,
        DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default);
}
