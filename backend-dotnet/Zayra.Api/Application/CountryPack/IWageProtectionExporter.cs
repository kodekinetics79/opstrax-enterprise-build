namespace Zayra.Api.Application.CountryPack;

public interface IWageProtectionExporter
{
    Task<WageProtectionExportResult> ExportAsync(WageProtectionExportInput input, CancellationToken ct = default);
}
