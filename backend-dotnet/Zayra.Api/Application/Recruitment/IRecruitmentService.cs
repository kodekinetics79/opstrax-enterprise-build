using Zayra.Api.Models;

namespace Zayra.Api.Application.Recruitment;

public interface IRecruitmentService
{
    Task<string> GenerateRequisitionNumberAsync(Guid tenantId, CancellationToken ct = default);
    Task<string> GenerateJobCodeAsync(Guid tenantId, CancellationToken ct = default);
    Task<Guid?> CreateApprovalRequestAsync(Guid tenantId, string entityName, Guid entityId, string title, Guid? requestedByUserId, CancellationToken ct = default);
    string GenerateOfferLetterHtml(OfferLetterTemplateData data);
    Task<Guid?> ConvertToEmployeeDraftAsync(Guid tenantId, Guid offerId, Guid requestedByUserId, CancellationToken ct = default);
}

public record OfferLetterTemplateData(
    string CandidateName,
    string JobTitle,
    string Department,
    DateOnly StartDate,
    decimal BasicSalary,
    decimal HousingAllowance,
    decimal TransportAllowance,
    decimal OtherAllowances,
    decimal GrossSalary,
    int ProbationMonths);
