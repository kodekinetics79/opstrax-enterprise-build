namespace Zayra.Api.Application.Common.Import;

public interface IImportExportEngine<TEntity, TDto>
{
    string[] ExportHeaders { get; }
    string[] TemplateHeaders { get; }
    Task<ImportPreviewResult> PreviewAsync(Guid tenantId, string csv, CancellationToken ct);
    Task<ImportCommitResult> CommitAsync(Guid tenantId, string csv, CancellationToken ct);
    Task<string> ExportCsvAsync(Guid tenantId, CancellationToken ct);
    string GetTemplate();
}
