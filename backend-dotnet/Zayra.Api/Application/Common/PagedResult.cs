namespace Zayra.Api.Application.Common;

public record PagedResult<T>(IReadOnlyCollection<T> Items, int Total, int Page, int PageSize);
