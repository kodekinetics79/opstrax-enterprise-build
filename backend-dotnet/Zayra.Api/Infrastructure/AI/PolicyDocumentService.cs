using System.Text;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;
using Zayra.Api.Application.AI;
using Zayra.Api.Data;
using Zayra.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Zayra.Api.Infrastructure.AI;

public class PolicyDocumentService : IPolicyDocumentService
{
    private readonly ZayraDbContext _db;
    private readonly ILlmClient _llm;
    private readonly AiOptions _aiOptions;

    public PolicyDocumentService(ZayraDbContext db, ILlmClient llm, AiOptions aiOptions)
    {
        _db = db;
        _llm = llm;
        _aiOptions = aiOptions;
    }

    public async Task<PolicyDocumentDto> UploadAsync(Guid tenantId, Guid? userId, Stream content, string fileName, string mimeType, CancellationToken ct)
    {
        var doc = new PolicyDocument
        {
            TenantId = tenantId,
            FileName = fileName,
            OriginalName = fileName,
            MimeType = mimeType,
            FileSizeBytes = content.Length,
            UploadedByUserId = userId,
            Status = "Processing"
        };
        _db.PolicyDocuments.Add(doc);
        await _db.SaveChangesAsync(ct);

        try
        {
            var text = ExtractText(content, mimeType, fileName);
            var chunks = ChunkText(text, 800);
            foreach (var (chunk, i) in chunks.Select((c, i) => (c, i)))
            {
                _db.DocumentChunks.Add(new DocumentChunk
                {
                    TenantId = tenantId,
                    DocumentId = doc.Id,
                    ChunkIndex = i,
                    Content = chunk,
                    TokenCount = chunk.Length / 4
                });
            }
            doc.ChunkCount = chunks.Count;
            doc.Status = "Ready";
        }
        catch (Exception ex)
        {
            doc.Status = "Failed";
            doc.ErrorMessage = ex.Message;
        }

        doc.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToDto(doc);
    }

    public async Task<IReadOnlyList<PolicyDocumentDto>> ListAsync(Guid tenantId, CancellationToken ct)
    {
        var docs = await _db.PolicyDocuments
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
        return docs.Select(ToDto).ToList();
    }

    public async Task<bool> DeleteAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        var doc = await _db.PolicyDocuments.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == documentId && !x.IsDeleted, ct);
        if (doc is null) return false;
        doc.IsDeleted = true;
        doc.UpdatedAtUtc = DateTime.UtcNow;
        await _db.DocumentChunks.Where(x => x.DocumentId == documentId).ExecuteDeleteAsync(ct);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<PolicyAskResponse> AskAsync(Guid tenantId, string question, CancellationToken ct)
    {
        // Simple keyword retrieval — get chunks containing words from the question
        var words = question.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3).Distinct().Take(8).ToList();

        List<DocumentChunk> relevantChunks;
        if (words.Count == 0)
        {
            relevantChunks = new List<DocumentChunk>();
        }
        else
        {
            // Load all ready chunks for tenant, then filter in memory (no full-text index)
            var allChunks = await _db.DocumentChunks
                .AsNoTracking()
                .Include(x => x.Document)
                .Where(x => x.TenantId == tenantId && !x.Document.IsDeleted && x.Document.Status == "Ready")
                .ToListAsync(ct);

            relevantChunks = allChunks
                .Where(c => words.Any(w => c.Content.ToLowerInvariant().Contains(w)))
                .OrderByDescending(c => words.Count(w => c.Content.ToLowerInvariant().Contains(w)))
                .Take(5)
                .ToList();
        }

        if (relevantChunks.Count == 0)
        {
            return new PolicyAskResponse(
                "I couldn't find relevant information in the uploaded policy documents. Please ensure the relevant document has been uploaded and try rephrasing your question.",
                Array.Empty<string>(),
                false);
        }

        var context = string.Join("\n\n---\n\n", relevantChunks.Select((c, i) =>
            $"[Source: {c.Document.OriginalName}, Chunk {c.ChunkIndex + 1}]\n{c.Content}"));

        var systemPrompt = "You are a helpful HR policy assistant for KynexOne. Answer questions using ONLY the provided policy document excerpts. If the answer is not found in the excerpts, say so clearly. Always cite which document your answer comes from. Label your response as advisory — it does not constitute legal advice.";

        var userPrompt = $"""
            Policy document excerpts:
            {context}

            Question: {question}

            Answer:
            """;

        var llmRequest = new LlmRequest(
            Provider: _aiOptions.EffectiveProvider,
            Model: _aiOptions.Model,
            SystemPrompt: systemPrompt,
            UserPrompt: userPrompt,
            MaxOutputTokens: 1024);

        var llmResponse = await _llm.CompleteAsync(llmRequest, ct);
        var answer = llmResponse.Success && !string.IsNullOrWhiteSpace(llmResponse.Text)
            ? llmResponse.Text
            : "Unable to generate an answer at this time. Please try again later.";

        var sources = relevantChunks.Select(c => c.Document.OriginalName).Distinct().ToArray();
        return new PolicyAskResponse(answer, sources, true);
    }

    private static string ExtractText(Stream stream, string mimeType, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext == ".pdf" || mimeType == "application/pdf")
        {
            using var pdf = PdfDocument.Open(stream);
            var sb = new StringBuilder();
            foreach (var page in pdf.GetPages())
                sb.AppendLine(page.Text);
            return sb.ToString();
        }
        if (ext is ".docx" or ".doc" || mimeType.Contains("wordprocessingml") || mimeType.Contains("msword"))
        {
            using var doc = WordprocessingDocument.Open(stream, false);
            var sb = new StringBuilder();
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body != null)
                foreach (var text in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>())
                    sb.AppendLine(text.Text);
            return sb.ToString();
        }
        // Plain text fallback
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static List<string> ChunkText(string text, int maxChunkSize)
    {
        var chunks = new List<string>();
        var sentences = text.Split(new[] { ". ", ".\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
        var current = new StringBuilder();
        foreach (var sentence in sentences)
        {
            if (current.Length + sentence.Length > maxChunkSize && current.Length > 0)
            {
                chunks.Add(current.ToString().Trim());
                current.Clear();
            }
            current.Append(sentence).Append(". ");
        }
        if (current.Length > 0) chunks.Add(current.ToString().Trim());
        return chunks.Count > 0 ? chunks : new List<string> { text.Trim() };
    }

    private static PolicyDocumentDto ToDto(PolicyDocument d) =>
        new(d.Id, d.OriginalName, d.MimeType, d.FileSizeBytes, d.Status, d.ChunkCount, d.ErrorMessage, d.CreatedAtUtc);
}
