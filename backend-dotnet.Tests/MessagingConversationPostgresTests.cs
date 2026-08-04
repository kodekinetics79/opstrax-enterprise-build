using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// Two-way dispatch<->driver messaging on the existing messaging spine. These prove the pieces the
// new increment adds: (1) the driver unread-count / per-row unread SQL the handlers run counts only
// the driver's OWN unread INBOUND threads, company-scoped; (2) a message send fans a notification to
// the other party resolving the driver's login, deduped, with the body sanitized. Handler HTTP
// plumbing (403 on a foreign thread, sender_user_id from session) is unchanged existing behavior;
// here we lock the new SQL + the NotificationService wiring against a real DB.
[Trait("Category", "Integration")]
public class MessagingConversationPostgresTests
{
    // The exact driver-branch query from MessageUnreadCount + the per-row column from
    // MessageConversationList — mirrored here so a divergence in the handler SQL breaks a test.
    private const string DriverUnreadCountSql =
        @"SELECT COUNT(DISTINCT c.id) FROM messaging_conversations c
          JOIN messaging_messages m ON m.conversation_id=c.id
          WHERE c.company_id=@cid AND c.driver_id=@did AND m.read_at IS NULL AND m.sender_user_id!=@uid";
    private const string PerRowUnreadSql =
        @"SELECT (SELECT COUNT(*) FROM messaging_messages m WHERE m.conversation_id=c.id AND m.read_at IS NULL AND m.sender_user_id!=@uid) unread_count
          FROM messaging_conversations c WHERE c.id=@conv";

    [Fact]
    public async Task Driver_Unread_Count_Counts_Only_Own_Unread_Inbound_Threads()
    {
        var db = CreateDatabase();
        var cid = await SeedCompanyAsync(db);
        var otherCid = await SeedCompanyAsync(db);
        try
        {
            var dispatcherUser = await SeedUserAsync(db, cid, "Dispatcher", "Dispatcher");
            var driverUserA = await SeedUserAsync(db, cid, "Driver A", "Driver");
            var driverA = await SeedDriverAsync(db, cid, driverUserA);
            var driverUserB = await SeedUserAsync(db, cid, "Driver B", "Driver");
            var driverB = await SeedDriverAsync(db, cid, driverUserB);

            // conv1: two unread inbound (from dispatch) + one already-read inbound -> the thread counts once.
            var conv1 = await SeedConversationAsync(db, cid, driverA, dispatcherUser, "Load 1");
            await SeedMessageAsync(db, conv1, cid, dispatcherUser, "Dispatcher", "where are you", read: false);
            await SeedMessageAsync(db, conv1, cid, dispatcherUser, "Dispatcher", "call me", read: false);
            await SeedMessageAsync(db, conv1, cid, dispatcherUser, "Dispatcher", "old note", read: true);
            // conv2: only the driver's own message -> not inbound, must not count.
            var conv2 = await SeedConversationAsync(db, cid, driverA, dispatcherUser, "Load 2");
            await SeedMessageAsync(db, conv2, cid, driverUserA, "Driver", "on my way", read: false);
            // conv3: belongs to driver B -> never counts for A.
            var conv3 = await SeedConversationAsync(db, cid, driverB, dispatcherUser, "Load 3");
            await SeedMessageAsync(db, conv3, cid, dispatcherUser, "Dispatcher", "hi B", read: false);
            // A conversation in ANOTHER tenant with the same driver_id value -> company scope must exclude it.
            var convX = await SeedConversationAsync(db, otherCid, driverA, dispatcherUser, "Foreign");
            await SeedMessageAsync(db, convX, otherCid, dispatcherUser, "Dispatcher", "leak?", read: false);

            var count = await db.ScalarLongAsync(DriverUnreadCountSql, c =>
            {
                c.Parameters.AddWithValue("@cid", cid); c.Parameters.AddWithValue("@did", driverA); c.Parameters.AddWithValue("@uid", driverUserA);
            });
            Assert.Equal(1, count);   // only conv1 (conv2 is own, conv3 is B's, convX is another tenant)

            // Per-row: conv1 shows 2 unread for driver A, conv2 shows 0.
            Assert.Equal(2, await PerRowUnreadAsync(db, conv1, driverUserA));
            Assert.Equal(0, await PerRowUnreadAsync(db, conv2, driverUserA));

            // After the driver reads conv1 (existing /read marks the other party's messages), unread -> 0.
            await db.ExecuteAsync(
                "UPDATE messaging_messages SET read_at=COALESCE(read_at, NOW()) WHERE conversation_id=@id AND company_id=@cid AND sender_user_id!=@uid",
                c => { c.Parameters.AddWithValue("@id", conv1); c.Parameters.AddWithValue("@cid", cid); c.Parameters.AddWithValue("@uid", driverUserA); });
            var after = await db.ScalarLongAsync(DriverUnreadCountSql, c =>
            {
                c.Parameters.AddWithValue("@cid", cid); c.Parameters.AddWithValue("@did", driverA); c.Parameters.AddWithValue("@uid", driverUserA);
            });
            Assert.Equal(0, after);
        }
        finally { await CleanupAsync(db, cid); await CleanupAsync(db, otherCid); }
    }

    // The dispatch-side unread count is "unread messages FROM drivers" — a colleague's unread
    // outbound-to-driver message must NOT inflate it (read_at is a shared column).
    private const string DispatcherUnreadCountSql =
        @"SELECT COUNT(DISTINCT c.id) FROM messaging_conversations c
          JOIN messaging_messages m ON m.conversation_id=c.id
          WHERE c.company_id=@cid AND m.read_at IS NULL AND m.sender_role='Driver'";

    [Fact]
    public async Task Dispatcher_Unread_Counts_Only_Inbound_Driver_Messages_Not_Colleague_Outbound()
    {
        var db = CreateDatabase();
        var cid = await SeedCompanyAsync(db);
        try
        {
            var dispatcherA = await SeedUserAsync(db, cid, "Dispatcher A", "Dispatcher");
            var dispatcherB = await SeedUserAsync(db, cid, "Dispatcher B", "Dispatcher");
            var driverUser = await SeedUserAsync(db, cid, "Driver", "Driver");
            var driver = await SeedDriverAsync(db, cid, driverUser);

            // conv1: an unread message FROM the driver -> genuinely inbound to dispatch, counts.
            var conv1 = await SeedConversationAsync(db, cid, driver, dispatcherA, "Load 1");
            await SeedMessageAsync(db, conv1, cid, driverUser, "Driver", "where do I park", read: false);
            // conv2: only dispatcher B's unread outbound-to-driver message -> NOT inbound to dispatch, must not count.
            var conv2 = await SeedConversationAsync(db, cid, driver, dispatcherB, "Load 2");
            await SeedMessageAsync(db, conv2, cid, dispatcherB, "Dispatcher", "call me", read: false);

            var count = await db.ScalarLongAsync(DispatcherUnreadCountSql, c => c.Parameters.AddWithValue("@cid", cid));
            Assert.Equal(1, count);   // only conv1 (driver-sent), NOT conv2 (colleague outbound)
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Message_To_Driver_Fans_A_Deduped_Notification_To_The_Drivers_Login()
    {
        var db = CreateDatabase();
        var svc = new NotificationService(db);
        var cid = await SeedCompanyAsync(db);
        try
        {
            var driverUser = await SeedUserAsync(db, cid, "Driver Z", "Driver");
            var driver = await SeedDriverAsync(db, cid, driverUser);
            long convId = 4242;

            var n1 = await svc.CreateAsync(cid, "message.received", "MessagingConversation", convId, "info",
                "New message from dispatch", "your lumper is approved", "driver", default,
                targetDriverId: driver, dedupeKey: $"msg:conv:{convId}:driver", suppressionWindow: TimeSpan.FromMinutes(5));
            Assert.True(n1 > 0);

            // The notification resolved the driver's LOGIN (user_id) + driver_id into a recipient row.
            var recip = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM notification_recipients WHERE notification_id=@n AND company_id=@c AND user_id=@u AND driver_id=@d",
                c => { c.Parameters.AddWithValue("@n", n1); c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@u", driverUser); c.Parameters.AddWithValue("@d", driver); });
            Assert.Equal(1, recip);

            // A rapid second send on the same conversation is deduped (one live bell, not two).
            var n2 = await svc.CreateAsync(cid, "message.received", "MessagingConversation", convId, "info",
                "New message from dispatch", "and your detour too", "driver", default,
                targetDriverId: driver, dedupeKey: $"msg:conv:{convId}:driver", suppressionWindow: TimeSpan.FromMinutes(5));
            Assert.Equal(-1, n2);   // suppressed
            var notifCount = await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM notifications WHERE company_id=@c AND dedupe_key=@k",
                c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@k", $"msg:conv:{convId}:driver"); });
            Assert.Equal(1, notifCount);
        }
        finally { await CleanupAsync(db, cid); }
    }

    [Fact]
    public async Task Driver_Notification_Body_Is_Sanitized_Of_Internal_Data()
    {
        var db = CreateDatabase();
        var svc = new NotificationService(db);
        var cid = await SeedCompanyAsync(db);
        try
        {
            var driverUser = await SeedUserAsync(db, cid, "Driver S", "Driver");
            var driver = await SeedDriverAsync(db, cid, driverUser);
            // A dispatcher message that (accidentally) carries internal safety data must not reach the driver.
            var nId = await svc.CreateAsync(cid, "message.received", "MessagingConversation", 9001, "info",
                "New message from dispatch", "note: driver safety_score dropped, coach them", "driver", default,
                targetDriverId: driver);
            var stored = await db.QuerySingleAsync(
                "SELECT message FROM notifications WHERE id=@n", c => c.Parameters.AddWithValue("@n", nId));
            var msg = stored!["message"]?.ToString() ?? "";
            Assert.DoesNotContain("safety_score", msg);
            Assert.Contains("contact your dispatcher", msg);   // the canned safe replacement
        }
        finally { await CleanupAsync(db, cid); }
    }

    // ── helpers ───────────────────────────────────────────────────────────────
    private static async Task<long> SeedCompanyAsync(Database db) =>
        await db.InsertAsync("INSERT INTO companies (company_code, name, industry) VALUES (@code, 'Msg Co', 'logistics') RETURNING id",
            c => c.Parameters.AddWithValue("@code", $"MSG-{Guid.NewGuid():N}".Substring(0, 15)));

    private static Task<long> SeedUserAsync(Database db, long cid, string name, string role) =>
        db.InsertAsync("INSERT INTO users (company_id, full_name, email, role_name, status) VALUES (@c, @n, @e, @r, 'Active') RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@n", name);
                   c.Parameters.AddWithValue("@e", $"{Guid.NewGuid():N}@ex.com".Substring(0, 24)); c.Parameters.AddWithValue("@r", role); });

    private static Task<long> SeedDriverAsync(Database db, long cid, long userId) =>
        db.InsertAsync("INSERT INTO drivers (company_id, driver_code, full_name, status, user_id) VALUES (@c, @dc, 'Test Driver', 'Active', @u) RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@dc", $"D-{Guid.NewGuid():N}".Substring(0, 12)); c.Parameters.AddWithValue("@u", userId); });

    private static Task<long> SeedConversationAsync(Database db, long cid, long driverId, long createdBy, string subject) =>
        db.InsertAsync("INSERT INTO messaging_conversations (company_id, driver_id, subject, status, created_by, updated_at) VALUES (@c, @d, @s, 'open', @by, NOW()) RETURNING id",
            c => { c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@d", driverId); c.Parameters.AddWithValue("@s", subject); c.Parameters.AddWithValue("@by", createdBy); });

    private static Task SeedMessageAsync(Database db, long convId, long cid, long senderUserId, string role, string body, bool read) =>
        db.ExecuteAsync("INSERT INTO messaging_messages (conversation_id, company_id, sender_user_id, sender_role, body, sent_at, read_at) VALUES (@conv, @c, @u, @r, @b, NOW(), @read)",
            c => { c.Parameters.AddWithValue("@conv", convId); c.Parameters.AddWithValue("@c", cid); c.Parameters.AddWithValue("@u", senderUserId);
                   c.Parameters.AddWithValue("@r", role); c.Parameters.AddWithValue("@b", body); c.Parameters.AddWithValue("@read", read ? DateTime.UtcNow : (object)DBNull.Value); });

    private static Task<long> PerRowUnreadAsync(Database db, long convId, long uid) =>
        db.ScalarLongAsync(PerRowUnreadSql, c => { c.Parameters.AddWithValue("@conv", convId); c.Parameters.AddWithValue("@uid", uid); });

    private static async Task CleanupAsync(Database db, long cid)
    {
        foreach (var t in new[] { "messaging_messages", "messaging_conversations", "notification_recipients", "notifications", "drivers", "users" })
            await db.ExecuteAsync($"DELETE FROM {t} WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", cid));
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", cid));
    }

    private static Database CreateDatabase() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());
}
