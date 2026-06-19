namespace Zayra.Api.Application.CountryPack;

public interface IStatutoryDeductionCalculator
{
    Task<StatutoryDeductionResult> CalculateAsync(StatutoryDeductionInput input, CancellationToken ct = default);
}
