using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// Financial config envelope (ADR-008 P1 meta-layer). Verified against real Postgres: draft lifecycle,
// document upsert (draft-only, JSON-validated), maker-checker publish with append-only supersede, the
// effective-config resolver, archive guardrails, and the tamper-evident change-log hash chain.
[Trait("Category", "Integration")]
public class FinancialConfigPostgresTests
{
    [Fact]
    public async Task Draft_Upsert_Publish_Resolves()
    {
        var (db, svc, cid) = Setup();
        try
        {
            var draft = await svc.CreateDraftAsync(cid, "spot_broker", null, null, "Broker config", authorUserId: 1);
            Assert.True(draft.Ok);
            var setId = draft.ConfigSetId!.Value;

            var doc = await svc.UpsertDocumentAsync(cid, setId, "rating", "default", "{\"basis\":\"per_mile\",\"rate\":2.0}", 1);
            Assert.True(doc.Ok);

            // Maker-checker: author (1) cannot publish; a different user (2) can.
            var self = await svc.PublishAsync(cid, setId, new DateOnly(2026, 1, 1), publisherUserId: 1, ConfigPublishMode.Commit);
            Assert.False(self.Published);
            Assert.Equal("author_cannot_publish", self.Reason);

            var pub = await svc.PublishAsync(cid, setId, new DateOnly(2026, 1, 1), publisherUserId: 2, ConfigPublishMode.Commit);
            Assert.True(pub.Published);

            var resolved = await svc.GetResolvedAsync(cid, new DateOnly(2026, 6, 1));
            Assert.NotEmpty(resolved);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Cannot_Upsert_Into_Published_Set()
    {
        var (db, svc, cid) = Setup();
        try
        {
            var draft = await svc.CreateDraftAsync(cid, "custom", null, null, "C", authorUserId: 1);
            var setId = draft.ConfigSetId!.Value;
            await svc.PublishAsync(cid, setId, new DateOnly(2026, 1, 1), publisherUserId: 2, ConfigPublishMode.Commit);

            var doc = await svc.UpsertDocumentAsync(cid, setId, "rating", "default", "{}", 1);
            Assert.False(doc.Ok);
            Assert.Equal("config_locked", doc.Reason);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task New_Version_Supersedes_Prior_Published_Head()
    {
        var (db, svc, cid) = Setup();
        try
        {
            var v1 = await svc.CreateDraftAsync(cid, "custom", null, null, "v1", authorUserId: 1);
            await svc.PublishAsync(cid, v1.ConfigSetId!.Value, new DateOnly(2026, 1, 1), 2, ConfigPublishMode.Commit);

            // Base a v2 on v1 (same logical config_set_no), publish it -> v1 becomes superseded.
            var v2 = await svc.CreateDraftAsync(cid, "custom", null, v1.ConfigSetId, "v2", authorUserId: 1);
            Assert.Equal(2, v2.VersionNo);
            await svc.PublishAsync(cid, v2.ConfigSetId!.Value, new DateOnly(2026, 3, 1), 2, ConfigPublishMode.Commit);

            var v1Status = (await svc.GetConfigSetAsync(cid, v1.ConfigSetId!.Value))!["status"]?.ToString();
            var v2Status = (await svc.GetConfigSetAsync(cid, v2.ConfigSetId!.Value))!["status"]?.ToString();
            Assert.Equal("superseded", v1Status);
            Assert.Equal("published", v2Status);

            // Resolver returns exactly one head for the logical set.
            var resolved = await svc.GetResolvedAsync(cid, new DateOnly(2026, 6, 1));
            Assert.Single(resolved);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Cannot_Archive_Active_Published_Head()
    {
        var (db, svc, cid) = Setup();
        try
        {
            var s = await svc.CreateDraftAsync(cid, "custom", null, null, "C", authorUserId: 1);
            await svc.PublishAsync(cid, s.ConfigSetId!.Value, new DateOnly(2026, 1, 1), 2, ConfigPublishMode.Commit);
            var arch = await svc.ArchiveAsync(cid, s.ConfigSetId!.Value, 2);
            Assert.False(arch.Ok);
            Assert.Equal("cannot_archive_active_head", arch.Reason);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Change_Log_Is_Hash_Chained()
    {
        var (db, svc, cid) = Setup();
        try
        {
            var s = await svc.CreateDraftAsync(cid, "custom", null, null, "C", authorUserId: 1);
            var setId = s.ConfigSetId!.Value;
            await svc.UpsertDocumentAsync(cid, setId, "rating", "default", "{}", 1);
            await svc.PublishAsync(cid, setId, new DateOnly(2026, 1, 1), 2, ConfigPublishMode.Commit);

            var log = await svc.GetChangeLogAsync(cid, setId);
            Assert.True(log.Count >= 3);   // draft_created, document_upserted, published
            // Each row's prev_hash equals the previous row's row_hash (chain intact).
            for (var i = 1; i < log.Count; i++)
                Assert.Equal(log[i - 1]["rowHash"]?.ToString(), log[i]["prevHash"]?.ToString());
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Invalid_Json_Document_Is_Rejected()
    {
        var (db, svc, cid) = Setup();
        try
        {
            var s = await svc.CreateDraftAsync(cid, "custom", null, null, "C", authorUserId: 1);
            var doc = await svc.UpsertDocumentAsync(cid, s.ConfigSetId!.Value, "rating", "default", "{not valid json", 1);
            Assert.False(doc.Ok);
            Assert.Equal("invalid_json", doc.Reason);
        }
        finally { await CleanupAsync(db, cid); }
    }

    // ── helpers ──

    private static (Database, FinancialConfigService, long) Setup()
    {
        var db = new Database(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());
        var cid = db.InsertAsync("INSERT INTO companies (company_code, name, industry) VALUES (@code,'Cfg Co','logistics') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"CF-{Guid.NewGuid():N}".Substring(0, 16))).GetAwaiter().GetResult();
        return (db, new FinancialConfigService(db), cid);
    }

    private static async Task CleanupAsync(Database db, long cid)
    {
        foreach (var t in new[] { "fin_config_change_log", "fin_config_documents", "fin_config_sets" })
            await db.ExecuteAsync($"DELETE FROM {t} WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", cid));
    }
}
