using Opstrax.Api.Observability;
using System.IO.Compression;
using System.Text;

namespace Opstrax.Api.Storage;

// ─────────────────────────────────────────────────────────────────────────────
// FileStorageService — the application-facing upload/download facade.
//
// Responsibilities:
//   • Validate uploads (size cap, allowed content types) — reject before storing.
//   • Compute TENANT-SCOPED keys so files can never cross tenants and a whole
//     tenant's files can be erased/offboarded by prefix.
//   • Store via IObjectStore (R2/S3/local) and return a stable reference string
//     "objkey:<key>" persisted in the existing *_url columns (additive, no schema
//     migration; legacy absolute URLs still resolve).
//   • Resolve a reference to a time-boxed signed URL, or signal that it must be
//     streamed through the authenticated proxy (local dev / signing unsupported).
//
// COMPLIANCE: this is the durable, access-controlled home for personal documents
// (PODs, signatures, licenses). Objects are private; access is short-lived and
// authenticated; erasure + retention can delete by key/prefix.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class FileStorageService(IObjectStore store, ILogger<FileStorageService> logger)
{
    public const string RefPrefix = "objkey:";
    private const long MaxBytes = 25 * 1024 * 1024; // 25 MB per file

    private sealed record FileFormat(string ContentType, string[] Extensions, Func<Stream, bool> Validate);

    private static readonly FileFormat[] AllowedFormats =
    {
        new("image/jpeg", [".jpg", ".jpeg"], s => StartsWith(s, [0xff, 0xd8, 0xff])),
        new("image/png", [".png"], s => StartsWith(s, [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])),
        new("image/gif", [".gif"], s => StartsWithAscii(s, "GIF87a") || StartsWithAscii(s, "GIF89a")),
        new("image/webp", [".webp"], s => StartsWithAscii(s, "RIFF") && AtAscii(s, 8, "WEBP")),
        new("image/heic", [".heic", ".heif"], IsHeif),
        new("application/pdf", [".pdf"], IsPdf),
        new("application/vnd.openxmlformats-officedocument.wordprocessingml.document", [".docx"], s => IsOfficeZip(s, "word/")),
        new("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", [".xlsx"], s => IsOfficeZip(s, "xl/")),
        new("text/plain", [".txt"], IsSafeText),
        new("text/csv", [".csv"], IsSafeText),
    };

    public bool StoreConfigured => store.IsConfigured;
    public string Provider => store.Provider;

    // ── Upload ────────────────────────────────────────────────────────────────

    public sealed record UploadResult(string Reference, string Key, long Size, string ContentType);

    /// <summary>Validates + stores a file for a tenant/category, returns the
    /// persistable reference. Throws ArgumentException on validation failure.</summary>
    public async Task<UploadResult> UploadAsync(
        long companyId, string category, string fileName, string contentType,
        Stream content, CancellationToken ct = default)
    {
        if (companyId <= 0) throw new ArgumentException("A tenant is required for uploads.");
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
            throw new ArgumentException("A safe file name is required.");

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var format = AllowedFormats.SingleOrDefault(f =>
            f.ContentType.Equals(contentType?.Split(';', 2)[0].Trim(), StringComparison.OrdinalIgnoreCase) &&
            f.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
        if (format is null) throw new ArgumentException("File extension and content type are not allowed.");

        var tempPath = Path.Combine(Path.GetTempPath(), $"opstrax-upload-{Guid.NewGuid():N}.tmp");
        try
        {
            long length = 0;
            await using (var staged = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                             64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var chunk = new byte[64 * 1024];
                while (true)
                {
                    var read = await content.ReadAsync(chunk.AsMemory(0, chunk.Length), ct);
                    if (read == 0) break;
                    length += read;
                    if (length > MaxBytes)
                        throw new ArgumentException($"File exceeds {MaxBytes / (1024 * 1024)} MB limit.");
                    await staged.WriteAsync(chunk.AsMemory(0, read), ct);
                }

                if (length == 0) throw new ArgumentException("Empty file.");
                await staged.FlushAsync(ct);
                staged.Position = 0;
                if (!format.Validate(staged) || ContainsActiveMarkup(staged))
                    throw new ArgumentException("File content does not match its declared format or contains active content.");

                staged.Position = 0;
                var key = BuildKey(companyId, category, extension);
                await store.PutAsync(key, staged, format.ContentType, ct);

                logger.LogInformation(new EventId(0, "file_uploaded"),
                    "Stored file for tenant {Tenant} category {Category} ({Bytes} bytes) via {Provider}",
                    companyId, category, length, store.Provider);

                return new UploadResult(RefPrefix + key, key, length, format.ContentType);
            }
        }
        finally
        {
            try { File.Delete(tempPath); } catch (IOException) { /* best-effort cleanup */ }
        }
    }

    // ── Resolve for download ────────────────────────────────────────────────────

    public sealed record ResolvedRef(bool IsManaged, string? Key, string? SignedUrl, string? LegacyUrl);

    /// <summary>Turns a stored reference into either a signed URL (redirect) or a
    /// key to stream via the proxy. Legacy absolute URLs pass through unchanged.</summary>
    public async Task<ResolvedRef> ResolveAsync(string? reference, TimeSpan ttl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return new ResolvedRef(false, null, null, null);

        if (!reference.StartsWith(RefPrefix, StringComparison.Ordinal))
            return new ResolvedRef(false, null, null, reference); // legacy URL

        var key = reference[RefPrefix.Length..];
        var signed = await store.SignedUrlAsync(key, ttl, ct);
        return new ResolvedRef(true, key, signed, null);
    }

    /// <summary>Enforces that a resolved key belongs to the caller's tenant — a
    /// hard guard against IDOR on the download proxy.</summary>
    public static bool KeyBelongsToTenant(string key, long companyId) =>
        key.StartsWith($"tenant/{companyId}/", StringComparison.Ordinal);

    public Task<Stream> OpenAsync(string key, CancellationToken ct = default) => store.GetAsync(key, ct);

    // ── Erasure / retention ──────────────────────────────────────────────────────

    public Task DeleteAsync(string reference, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reference) || !reference.StartsWith(RefPrefix, StringComparison.Ordinal))
            return Task.CompletedTask; // nothing managed to delete
        return store.DeleteAsync(reference[RefPrefix.Length..], ct);
    }

    /// <summary>Deletes ALL of a tenant's stored files (offboarding / full erasure).</summary>
    public Task<int> DeleteTenantAsync(long companyId, CancellationToken ct = default)
        => store.DeletePrefixAsync($"tenant/{companyId}/", ct);

    // ── Key layout ────────────────────────────────────────────────────────────────

    private static string BuildKey(long companyId, string category, string extension)
    {
        var safeCat = SanitizeSegment(string.IsNullOrWhiteSpace(category) ? "misc" : category);
        if (string.IsNullOrWhiteSpace(safeCat)) safeCat = "misc";
        var unique = Guid.NewGuid().ToString("n");
        var yyyymm = DateTime.UtcNow.ToString("yyyy/MM");
        // tenant/{id}/{category}/{yyyy}/{mm}/{uuid}{ext}
        return $"tenant/{companyId}/{safeCat}/{yyyymm}/{unique}{extension}";
    }

    private static string SanitizeSegment(string s) =>
        new(s.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());

    private static bool StartsWith(Stream stream, byte[] expected)
    {
        Span<byte> actual = stackalloc byte[expected.Length];
        stream.Position = 0;
        return stream.Read(actual) == actual.Length && actual.SequenceEqual(expected);
    }

    private static bool StartsWithAscii(Stream stream, string value) => AtAscii(stream, 0, value);

    private static bool AtAscii(Stream stream, int offset, string value)
    {
        var expected = Encoding.ASCII.GetBytes(value);
        var actual = new byte[expected.Length];
        stream.Position = offset;
        return stream.Read(actual, 0, actual.Length) == actual.Length && actual.SequenceEqual(expected);
    }

    private static bool IsHeif(Stream stream)
    {
        if (stream.Length < 12 || !AtAscii(stream, 4, "ftyp")) return false;
        stream.Position = 8;
        Span<byte> brand = stackalloc byte[4];
        if (stream.Read(brand) != 4) return false;
        var value = Encoding.ASCII.GetString(brand);
        return value is "heic" or "heix" or "hevc" or "hevx" or "mif1" or "msf1";
    }

    private static bool IsPdf(Stream stream)
    {
        if (!StartsWithAscii(stream, "%PDF-")) return false;
        var tailSize = (int)Math.Min(stream.Length, 4096);
        var tail = new byte[tailSize];
        stream.Position = stream.Length - tailSize;
        stream.ReadExactly(tail);
        var text = Encoding.ASCII.GetString(tail);
        if (!text.Contains("%%EOF", StringComparison.Ordinal)) return false;
        return !ContainsActiveMarkup(stream);
    }

    private static bool IsOfficeZip(Stream stream, string requiredPrefix)
    {
        if (!StartsWith(stream, [0x50, 0x4b, 0x03, 0x04])) return false;
        try
        {
            stream.Position = 0;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count is 0 or > 10_000) return false;
            if (!archive.Entries.Any(e => e.FullName.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase)) ||
                !archive.Entries.Any(e => e.FullName.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase)))
                return false;

            long expanded = 0;
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.StartsWith("/", StringComparison.Ordinal) ||
                    entry.FullName.Split('/').Any(p => p == "..") ||
                    entry.FullName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                    entry.FullName.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                    entry.FullName.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                    entry.FullName.Contains("vbaProject.bin", StringComparison.OrdinalIgnoreCase) ||
                    entry.FullName.Contains("/embeddings/", StringComparison.OrdinalIgnoreCase) ||
                    entry.FullName.Contains("/activeX/", StringComparison.OrdinalIgnoreCase) ||
                    entry.FullName.Contains("/externalLinks/", StringComparison.OrdinalIgnoreCase) ||
                    entry.FullName.StartsWith("customUI/", StringComparison.OrdinalIgnoreCase))
                    return false;
                expanded += entry.Length;
                if (expanded > 100L * 1024 * 1024) return false;

                if (entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                {
                    if (entry.Length > 1024 * 1024) return false;
                    using var relation = entry.Open();
                    using var reader = new StreamReader(relation, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    var xml = reader.ReadToEnd();
                    if (xml.Contains("TargetMode=\"External\"", StringComparison.OrdinalIgnoreCase) ||
                        xml.Contains("TargetMode='External'", StringComparison.OrdinalIgnoreCase))
                        return false;
                }
            }
            return true;
        }
        catch (InvalidDataException) { return false; }
    }

    private static bool IsSafeText(Stream stream)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096, leaveOpen: true);
        try
        {
            var sample = new char[Math.Min(stream.Length, 8192)];
            var read = reader.Read(sample, 0, sample.Length);
            var text = new string(sample, 0, read);
            return !text.Contains('\0') &&
                   !text.Contains("<script", StringComparison.OrdinalIgnoreCase) &&
                   !text.Contains("<html", StringComparison.OrdinalIgnoreCase) &&
                   !text.Contains("<svg", StringComparison.OrdinalIgnoreCase);
        }
        catch (DecoderFallbackException) { return false; }
    }

    private static bool ContainsActiveMarkup(Stream stream)
    {
        stream.Position = 0;
        var chunk = new byte[64 * 1024];
        var carry = "";
        while (true)
        {
            var read = stream.Read(chunk, 0, chunk.Length);
            if (read == 0) return false;
            var text = carry + Encoding.ASCII.GetString(chunk, 0, read);
            if (text.Contains("<script", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("<svg", StringComparison.OrdinalIgnoreCase))
                return true;
            carry = text.Length > 16 ? text[^16..] : text;
        }
    }
}
