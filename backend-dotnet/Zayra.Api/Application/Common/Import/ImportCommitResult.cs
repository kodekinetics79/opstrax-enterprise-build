namespace Zayra.Api.Application.Common.Import;

public record ImportCommitResult(
    int Received,
    int Created,
    int Updated,
    int Skipped,
    IReadOnlyList<ImportRowResult> Rows,
    IReadOnlyList<string> GlobalErrors);
