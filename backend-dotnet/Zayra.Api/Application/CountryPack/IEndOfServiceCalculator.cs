namespace Zayra.Api.Application.CountryPack;

public interface IEndOfServiceCalculator
{
    Task<EndOfServiceResult> CalculateAsync(EndOfServiceInput input, CancellationToken ct = default);
}
