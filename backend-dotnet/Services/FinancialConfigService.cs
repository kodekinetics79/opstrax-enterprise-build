using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public enum ConfigPublishMode { Preview, Commit }

public sealed record ConfigSetOutcome(bool Ok, long? ConfigSetId, int VersionNo, string Status, string? Reason = null);
public sealed record ConfigPublishOutcome(bool Published, long? ConfigSetId, string Status, string? Reason = null);

// Financial config envelope service (ADR-008 P1 meta-layer). Manages the lifecycle of a per-tenant,
// versioned, effective-dated config set: draft -> pending/published -> superseded/archived. Published
// sets + documents are append-only; a change is a NEW version, never an in-place edit. The change log
// is a per-(company,set) hash chain for tamper-evidence. This is inert scaffolding in P1 — no money
// path reads it yet — so it changes zero numbers; it exists so later modules resolve config from here.
public sealed class FinancialConfigService(Database db)
{
    // Create a draft envelope. A new logical set gets a fresh config_set_no @ version 1; basing on an
    // existing set opens the NEXT version of that set's config_set_no (append-only versioning). Optionally
    // clones the source set's documents so the new draft starts from the prior content.
    public async Task<ConfigSetOutcome> CreateDraftAsync(
        long companyId, string archetype, string? templateKey, long? basedOnConfigSetId, string title, long authorUserId, CancellationToken ct = default)
    {
        if (authorUserId <= 0) return new ConfigSetOutcome(false, null, 0, "invalid", "author_required");

        string configSetNo; int versionNo;
        if (basedOnConfigSetId is { } baseId)
        {
            var baseRow = await db.QuerySingleAsync("SELECT config_set_no FROM fin_config_sets WHERE company_id=@c AND id=@id",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@id", baseId); }, ct);
            if (baseRow is null) return new ConfigSetOutcome(false, null, 0, "missing", "base_not_found");
            configSetNo = baseRow.GetValueOrDefault("configSetNo")!.ToString()!;
            versionNo = (int)(await db.ScalarLongAsync("SELECT COALESCE(MAX(version_no),0)+1 FROM fin_config_sets WHERE company_id=@c AND config_set_no=@n",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@n", configSetNo); }, ct));
        }
        else
        {
            configSetNo = $"CFG-{companyId}-{Guid.NewGuid():N}".Substring(0, 20);
            versionNo = 1;
        }

        var id = await db.WithTransactionAsync(async (conn, tx) =>
        {
            long newId;
            await using (var ins = new NpgsqlCommand(
                @"INSERT INTO fin_config_sets (company_id, config_set_no, version_no, archetype, title, status, source, template_key, based_on_config_set_id, author_user_id)
                  VALUES (@c, @no, @ver, @arch, @title, 'draft', 'user', @tk, @base, @author) RETURNING id", conn, tx))
            {
                ins.Parameters.AddWithValue("@c", companyId); ins.Parameters.AddWithValue("@no", configSetNo);
                ins.Parameters.AddWithValue("@ver", versionNo); ins.Parameters.AddWithValue("@arch", archetype);
                ins.Parameters.AddWithValue("@title", title); ins.Parameters.AddWithValue("@tk", (object?)templateKey ?? DBNull.Value);
                ins.Parameters.AddWithValue("@base", (object?)basedOnConfigSetId ?? DBNull.Value); ins.Parameters.AddWithValue("@author", authorUserId);
                newId = Convert.ToInt64((await ins.ExecuteScalarAsync(ct))!, CultureInfo.InvariantCulture);
            }
            if (basedOnConfigSetId is { } src)
                await Exec(conn, tx,
                    @"INSERT INTO fin_config_documents (company_id, config_set_id, doc_type, doc_key, content_json, content_hash)
                      SELECT company_id, @new, doc_type, doc_key, content_json, content_hash FROM fin_config_documents WHERE company_id=@c AND config_set_id=@src",
                    p => { p.AddWithValue("@new", newId); p.AddWithValue("@c", companyId); p.AddWithValue("@src", src); }, ct);
            await AppendChangeLogAsync(conn, tx, companyId, newId, basedOnConfigSetId is null ? "draft_created" : "cloned", null, "draft", authorUserId, null, ct);
            return newId;
        }, ct);

        return new ConfigSetOutcome(true, id, versionNo, "draft");
    }

    // Upsert a typed document into a DRAFT envelope only. A non-draft envelope is immutable => fail-closed.
    public async Task<ConfigSetOutcome> UpsertDocumentAsync(
        long companyId, long configSetId, string docType, string docKey, string contentJson, long userId, CancellationToken ct = default)
    {
        var status = await StatusOf(companyId, configSetId, ct);
        if (status is null) return new ConfigSetOutcome(false, configSetId, 0, "missing", "not_found");
        if (status != "draft") return new ConfigSetOutcome(false, configSetId, 0, status, "config_locked");

        try { using var _ = JsonDocument.Parse(contentJson); }
        catch { return new ConfigSetOutcome(false, configSetId, 0, "draft", "invalid_json"); }

        var hash = Sha256(contentJson);
        await db.WithTransactionAsync(async (conn, tx) =>
        {
            await Exec(conn, tx,
                @"INSERT INTO fin_config_documents (company_id, config_set_id, doc_type, doc_key, content_json, content_hash)
                  VALUES (@c, @s, @dt, @dk, @json::jsonb, @h)
                  ON CONFLICT (company_id, config_set_id, doc_type, doc_key) DO UPDATE SET content_json=EXCLUDED.content_json, content_hash=EXCLUDED.content_hash",
                p => { p.AddWithValue("@c", companyId); p.AddWithValue("@s", configSetId); p.AddWithValue("@dt", docType);
                       p.AddWithValue("@dk", docKey); p.AddWithValue("@json", contentJson); p.AddWithValue("@h", hash); }, ct);
            await AppendChangeLogAsync(conn, tx, companyId, configSetId, "document_upserted", "draft", "draft", userId, hash, ct);
            return true;
        }, ct);
        return new ConfigSetOutcome(true, configSetId, 0, "draft");
    }

    // Publish a draft. Maker-checker: a non-null author who is NOT the publisher. Append-only: supersedes
    // any currently-published version of the same config_set_no, then flips this one to published.
    public async Task<ConfigPublishOutcome> PublishAsync(
        long companyId, long configSetId, DateOnly effectiveFrom, long publisherUserId, ConfigPublishMode mode, CancellationToken ct = default)
    {
        var row = await db.QuerySingleAsync("SELECT config_set_no, status, author_user_id FROM fin_config_sets WHERE company_id=@c AND id=@id",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@id", configSetId); }, ct);
        if (row is null) return new ConfigPublishOutcome(false, null, "missing", "not_found");
        var status = row.GetValueOrDefault("status")?.ToString() ?? "draft";
        if (status == "published") return new ConfigPublishOutcome(true, configSetId, "published"); // idempotent
        if (status != "draft") return new ConfigPublishOutcome(false, configSetId, status, "not_publishable");
        var author = row.GetValueOrDefault("authorUserId") is { } a and not DBNull ? Convert.ToInt64(a, CultureInfo.InvariantCulture) : (long?)null;
        if (author is null) return new ConfigPublishOutcome(false, configSetId, status, "no_author_maker_checker");
        if (author == publisherUserId) return new ConfigPublishOutcome(false, configSetId, status, "author_cannot_publish");

        if (mode == ConfigPublishMode.Preview)
            return new ConfigPublishOutcome(false, configSetId, "preview", "preview");

        var configSetNo = row.GetValueOrDefault("configSetNo")!.ToString()!;
        await db.WithTransactionAsync(async (conn, tx) =>
        {
            // Supersede the prior published head of this logical set (append-only: it stays as a row).
            await Exec(conn, tx,
                "UPDATE fin_config_sets SET status='superseded', updated_at=NOW() WHERE company_id=@c AND config_set_no=@no AND status='published'",
                p => { p.AddWithValue("@c", companyId); p.AddWithValue("@no", configSetNo); }, ct);
            await Exec(conn, tx,
                "UPDATE fin_config_sets SET status='published', published_by_user_id=@u, published_at=NOW(), effective_from=@eff, updated_at=NOW() WHERE company_id=@c AND id=@id",
                p => { p.AddWithValue("@u", publisherUserId); p.AddWithValue("@eff", effectiveFrom); p.AddWithValue("@c", companyId); p.AddWithValue("@id", configSetId); }, ct);
            await AppendChangeLogAsync(conn, tx, companyId, configSetId, "published", "draft", "published", publisherUserId, null, ct);
            return true;
        }, ct);
        return new ConfigPublishOutcome(true, configSetId, "published");
    }

    // Archive a draft or superseded set. The currently-effective published head cannot be archived (that
    // would strand the resolver on null and fail-close every consumer).
    public async Task<ConfigSetOutcome> ArchiveAsync(long companyId, long configSetId, long userId, CancellationToken ct = default)
    {
        var status = await StatusOf(companyId, configSetId, ct);
        if (status is null) return new ConfigSetOutcome(false, configSetId, 0, "missing", "not_found");
        if (status == "published") return new ConfigSetOutcome(false, configSetId, 0, status, "cannot_archive_active_head");
        if (status is not ("draft" or "superseded")) return new ConfigSetOutcome(false, configSetId, 0, status, "not_archivable");
        await db.WithTransactionAsync(async (conn, tx) =>
        {
            await Exec(conn, tx, "UPDATE fin_config_sets SET status='archived', updated_at=NOW() WHERE company_id=@c AND id=@id",
                p => { p.AddWithValue("@c", companyId); p.AddWithValue("@id", configSetId); }, ct);
            await AppendChangeLogAsync(conn, tx, companyId, configSetId, "archived", status, "archived", userId, null, ct);
            return true;
        }, ct);
        return new ConfigSetOutcome(true, configSetId, 0, "archived");
    }

    // Resolver: the published set of each logical config_set_no that is effective as-of the date (latest
    // effective_from <= asOf, not superseded/archived). Zero-tenant-branch — pure data. Returns each
    // set's header + documents. Null-safe: a tenant with no published config resolves to an empty map.
    public async Task<Dictionary<string, object?>> GetResolvedAsync(long companyId, DateOnly asOf, CancellationToken ct = default)
    {
        var sets = await db.QueryAsync(
            @"SELECT DISTINCT ON (config_set_no) id, config_set_no, version_no, title, effective_from
              FROM fin_config_sets
              WHERE company_id=@c AND status='published' AND (effective_from IS NULL OR effective_from <= @d)
              ORDER BY config_set_no, effective_from DESC NULLS LAST, version_no DESC",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@d", asOf); }, ct);
        var result = new Dictionary<string, object?>();
        foreach (var s in sets)
        {
            var id = Convert.ToInt64(s["id"], CultureInfo.InvariantCulture);
            var docs = await GetDocumentsAsync(companyId, id, ct);
            result[s["configSetNo"]!.ToString()!] = new { setId = id, versionNo = s.GetValueOrDefault("versionNo"), title = s.GetValueOrDefault("title"), documents = docs };
        }
        return result;
    }

    // ── reads ──

    public async Task<Dictionary<string, object?>?> GetConfigSetAsync(long companyId, long configSetId, CancellationToken ct = default)
        => await db.QuerySingleAsync("SELECT * FROM fin_config_sets WHERE company_id=@c AND id=@id",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@id", configSetId); }, ct);

    public async Task<List<Dictionary<string, object?>>> ListConfigSetsAsync(long companyId, string? status, CancellationToken ct = default)
        => await db.QueryAsync("SELECT * FROM fin_config_sets WHERE company_id=@c AND (@s IS NULL OR status=@s) ORDER BY created_at DESC, id DESC LIMIT 500",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@s", (object?)status ?? DBNull.Value); }, ct);

    public async Task<List<Dictionary<string, object?>>> GetDocumentsAsync(long companyId, long configSetId, CancellationToken ct = default)
        => await db.QueryAsync("SELECT doc_type, doc_key, content_json, content_hash FROM fin_config_documents WHERE company_id=@c AND config_set_id=@id ORDER BY doc_type, doc_key",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@id", configSetId); }, ct);

    public async Task<List<Dictionary<string, object?>>> GetChangeLogAsync(long companyId, long configSetId, CancellationToken ct = default)
        => await db.QueryAsync("SELECT * FROM fin_config_change_log WHERE company_id=@c AND config_set_id=@id ORDER BY id",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@id", configSetId); }, ct);

    // ── helpers ──

    private async Task<string?> StatusOf(long companyId, long configSetId, CancellationToken ct)
        => (await db.QuerySingleAsync("SELECT status FROM fin_config_sets WHERE company_id=@c AND id=@id",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@id", configSetId); }, ct))?.GetValueOrDefault("status")?.ToString();

    // Append a hash-chained change-log row: row_hash = sha256(prev_hash + business content). Tamper-evident
    // per (company, config_set) — rewriting any row breaks the chain for every later row.
    private static async Task AppendChangeLogAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long companyId, long configSetId,
        string action, string? fromStatus, string? toStatus, long? actorUserId, string? contentHash, CancellationToken ct)
    {
        string? prevHash;
        await using (var prev = new NpgsqlCommand("SELECT row_hash FROM fin_config_change_log WHERE company_id=@c AND config_set_id=@s ORDER BY id DESC LIMIT 1", conn, tx))
        {
            prev.Parameters.AddWithValue("@c", companyId); prev.Parameters.AddWithValue("@s", configSetId);
            prevHash = (await prev.ExecuteScalarAsync(ct))?.ToString();
        }
        var rowHash = Sha256($"{prevHash}|{configSetId}|{action}|{fromStatus}|{toStatus}|{actorUserId}|{contentHash}");
        await using var ins = new NpgsqlCommand(
            @"INSERT INTO fin_config_change_log (company_id, config_set_id, action, from_status, to_status, actor_user_id, content_hash, prev_hash, row_hash)
              VALUES (@c, @s, @a, @from, @to, @actor, @ch, @prev, @row)", conn, tx);
        ins.Parameters.AddWithValue("@c", companyId); ins.Parameters.AddWithValue("@s", configSetId);
        ins.Parameters.AddWithValue("@a", action); ins.Parameters.AddWithValue("@from", (object?)fromStatus ?? DBNull.Value);
        ins.Parameters.AddWithValue("@to", (object?)toStatus ?? DBNull.Value); ins.Parameters.AddWithValue("@actor", (object?)actorUserId ?? DBNull.Value);
        ins.Parameters.AddWithValue("@ch", (object?)contentHash ?? DBNull.Value); ins.Parameters.AddWithValue("@prev", (object?)prevHash ?? DBNull.Value);
        ins.Parameters.AddWithValue("@row", rowHash);
        await ins.ExecuteNonQueryAsync(ct);
    }

    private static async Task Exec(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, Action<NpgsqlParameterCollection> bind, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        bind(cmd.Parameters);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string Sha256(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
}
