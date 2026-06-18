namespace Zayra.Api.Application.Common.Import;

public record ImportPreviewResult(
    int Received,
    int WouldCreate,
    int WouldUpdate,
    int WouldSkip,
    IReadOnlyList<ImportRowResult> Rows);
