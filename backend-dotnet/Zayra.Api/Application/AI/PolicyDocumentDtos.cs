namespace Zayra.Api.Application.AI;

public record PolicyDocumentDto(Guid Id, string OriginalName, string MimeType, long FileSizeBytes, string Status, int ChunkCount, string? ErrorMessage, DateTime CreatedAtUtc);
public record PolicyAskRequest(string Question);
public record PolicyAskResponse(string Answer, string[] Sources, bool IsGrounded);
