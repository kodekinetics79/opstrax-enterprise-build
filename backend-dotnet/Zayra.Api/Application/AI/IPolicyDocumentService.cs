namespace Zayra.Api.Application.AI;
public interface IPolicyDocumentService
{
    Task<PolicyDocumentDto> UploadAsync(Guid tenantId, Guid? userId, Stream content, string fileName, string mimeType, CancellationToken ct);
    Task<IReadOnlyList<PolicyDocumentDto>> ListAsync(Guid tenantId, CancellationToken ct);
    Task<bool> DeleteAsync(Guid tenantId, Guid documentId, CancellationToken ct);
    Task<PolicyAskResponse> AskAsync(Guid tenantId, string question, CancellationToken ct);
}
