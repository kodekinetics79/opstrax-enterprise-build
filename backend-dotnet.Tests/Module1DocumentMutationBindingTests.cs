using System.Reflection;
using System.Text.Json;
using Npgsql;
using Opstrax.Api.Controllers;

namespace Opstrax.Tests;

// Focused binding regressions for the independently observed local HTTP failures.
// These do not replace actual route/permission/RLS/persistence or customer-browser retests.
public sealed class Module1DocumentMutationBindingTests
{
    [Theory]
    [InlineData("issuedAt", "null")]
    [InlineData("issuedAt", "\"\"")]
    [InlineData("issuedAt", "\"   \"")]
    [InlineData("issuedAt", "\"\\t\\r\\n\"")]
    [InlineData("expiresAt", "null")]
    [InlineData("expiresAt", "\"\"")]
    [InlineData("expiresAt", "\"   \"")]
    [InlineData("expiresAt", "\"\\t\\r\\n\"")]
    public void JsonBlankDate_NormalizesAndBindsDatabaseNull_NotText(string field, string jsonValue)
    {
        var body = Parse("{\"" + field + "\":" + jsonValue + "}");
        Assert.Empty(EndpointMappings.ValidateDocumentDateFields(body));

        Normalize(body);
        Assert.Same(DBNull.Value, body[field]);
        using var command = Bind(body, generateDocumentNumber: false);
        Assert.Same(DBNull.Value, command.Parameters[Parameter(field)].Value);
    }

    [Theory]
    [InlineData("issuedAt")]
    [InlineData("expiresAt")]
    public void AlreadyDatabaseNullDate_RemainsDatabaseNull(string field)
    {
        var body = new Dictionary<string, object?> { [field] = DBNull.Value };
        Normalize(body);
        using var command = Bind(body, generateDocumentNumber: false);
        Assert.Same(DBNull.Value, command.Parameters[Parameter(field)].Value);
    }

    [Theory]
    [InlineData("issuedAt", "expiresAt")]
    [InlineData("expiresAt", "issuedAt")]
    public void OneDateProvided_OmittedCounterpartIsNotAdded_AndBindsNull(string supplied, string omitted)
    {
        var body = Parse("{\"" + supplied + "\":\"2026-09-01\"}");
        Normalize(body);
        Assert.False(body.ContainsKey(omitted));
        using var command = Bind(body, generateDocumentNumber: false);
        Assert.Equal(new DateTime(2026, 9, 1), Assert.IsType<DateTime>(command.Parameters[Parameter(supplied)].Value));
        Assert.Same(DBNull.Value, command.Parameters[Parameter(omitted)].Value);
    }

    [Fact]
    public void ValidJsonCalendarDates_BindTypedNormalizedDates()
    {
        var body = Parse("{\"issuedAt\":\"2026-08-01\",\"expiresAt\":\"2026-09-01\"}");
        Assert.Empty(EndpointMappings.ValidateDocumentDateFields(body));
        Normalize(body);
        using var command = Bind(body, generateDocumentNumber: false);
        Assert.Equal(new DateTime(2026, 8, 1), Assert.IsType<DateTime>(command.Parameters["@issued"].Value));
        Assert.Equal(new DateTime(2026, 9, 1), Assert.IsType<DateTime>(command.Parameters["@expires"].Value));
    }

    [Theory]
    [InlineData("issuedAt", "not-a-date")]
    [InlineData("expiresAt", "tomorrow-ish")]
    public void NonblankMalformedDate_RemainsRejected_NotSilentlyConvertedToNull(string field, string value)
    {
        var body = Parse(JsonSerializer.Serialize(new Dictionary<string, string> { [field] = value }));
        Assert.NotEmpty(EndpointMappings.ValidateDocumentDateFields(body));
        Normalize(body);
        Assert.Equal(value, body[field]?.ToString());
        Assert.NotEmpty(EndpointMappings.ValidateDocumentDateFields(body));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"documentNumber\":null}")]
    [InlineData("{\"documentNumber\":\"\"}")]
    [InlineData("{\"documentNumber\":\"   \"}")]
    public void UpdateMissingOrBlankNumber_BindsNullToPreserveExistingNumber(string json)
    {
        using var command = Bind(Parse(json), generateDocumentNumber: false);
        // UpdateDocument's existing COALESCE keeps the persisted identity only when this is database null.
        Assert.Same(DBNull.Value, command.Parameters["@number"].Value);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"documentNumber\":null}")]
    [InlineData("{\"documentNumber\":\"\"}")]
    [InlineData("{\"documentNumber\":\"   \"}")]
    public void CreateMissingOrBlankNumber_StillGeneratesCurrentDocumentNumber(string json)
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var command = Bind(Parse(json), generateDocumentNumber: true);
        var after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var number = Assert.IsType<string>(command.Parameters["@number"].Value);
        Assert.StartsWith("DOC-", number, StringComparison.Ordinal);
        Assert.True(long.TryParse(number[4..], out var seconds));
        Assert.InRange(seconds, before, after);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitDocumentNumber_PreservedInCreateAndUpdate(bool generateDocumentNumber)
    {
        const string expected = "W1DOC-DATE-EXPLICIT-IDENTITY";
        using var command = Bind(Parse("{\"documentNumber\":\"" + expected + "\"}"), generateDocumentNumber);
        Assert.Equal(expected, command.Parameters["@number"].Value);
    }

    [Fact]
    public void BinderDefault_RemainsCreateNumberGeneration()
    {
        var parameter = Binder().GetParameters()[2];
        Assert.Equal(typeof(bool), parameter.ParameterType);
        Assert.True(parameter.HasDefaultValue);
        Assert.Equal(true, parameter.DefaultValue);
    }

    private static Dictionary<string, object?> Parse(string json) => JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;
    private static string Parameter(string field) => field == "issuedAt" ? "@issued" : "@expires";
    private static void Normalize(Dictionary<string, object?> body)
        => typeof(EndpointMappings).GetMethod("NormalizeDocumentDates", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [body]);
    private static MethodInfo Binder() => typeof(EndpointMappings).GetMethod("BindDocument", BindingFlags.NonPublic | BindingFlags.Static,
        binder: null, types: [typeof(NpgsqlCommand), typeof(Dictionary<string, object?>), typeof(bool)], modifiers: null)!;
    private static NpgsqlCommand Bind(Dictionary<string, object?> body, bool generateDocumentNumber)
    {
        var command = new NpgsqlCommand();
        try { Binder().Invoke(null, [command, body, generateDocumentNumber]); return command; }
        catch { command.Dispose(); throw; }
    }
}
