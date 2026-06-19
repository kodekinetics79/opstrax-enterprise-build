namespace Zayra.Api.Application.CountryPack;

public interface INationalizationTracker
{
    Task<NationalizationResult> GetStatusAsync(NationalizationInput input, CancellationToken ct = default);
}
