using System.Reflection;
using System.Text.Json;
using Opstrax.Api.Controllers;

namespace Opstrax.Tests;

public sealed class VehicleNumericValidationTests
{
    [Theory]
    [InlineData("year", "1949")]
    [InlineData("year", "2101")]
    [InlineData("year", "2020.5")]
    [InlineData("year", "invalid")]
    [InlineData("odometerMiles", "-1")]
    [InlineData("odometerMiles", "NaN")]
    [InlineData("odometerMiles", "Infinity")]
    public void InvalidValuesAreRejected(string key, string value)
    {
        Assert.Single(Validate(new() { [key] = value }));
    }

    [Theory]
    [InlineData(1950)]
    [InlineData(2020)]
    [InlineData(2100)]
    public void ValidBoundariesNormalizeJsonAndStringInputs(int year)
    {
        using var json = JsonDocument.Parse($"{{\"year\":{year},\"odometerMiles\":0.5}}");
        var body = new Dictionary<string, object?> { ["year"] = json.RootElement.GetProperty("year"), ["odometerMiles"] = json.RootElement.GetProperty("odometerMiles") };
        Assert.Empty(Validate(body));
        Assert.Equal((long)year, body["year"]);
        Assert.Equal(0.5m, body["odometerMiles"]);
        body["year"] = year.ToString();
        body["odometerMiles"] = "0";
        Assert.Empty(Validate(body));
        Assert.Equal((long)year, body["year"]);
        Assert.Equal(0m, body["odometerMiles"]);
    }

    [Fact]
    public void OptionalFieldsAndPartialUpdatesRemainOptional()
    {
        var body = new Dictionary<string, object?>();
        Assert.Empty(Validate(body));
        Assert.Empty(body);
        body["year"] = " ";
        body["odometerMiles"] = null;
        Assert.Empty(Validate(body));
        Assert.Null(body["year"]);
        Assert.Null(body["odometerMiles"]);
    }

    [Fact]
    public void TypedDecimalsUseInvariantCulture()
    {
        var prior = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new("fr-FR");
            var body = new Dictionary<string, object?> { ["year"] = 2020L, ["odometerMiles"] = 1.5m };
            Assert.Empty(Validate(body));
            Assert.Equal(1.5m, body["odometerMiles"]);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = prior; }
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    public void ImportValidationAndCleanupPreserveFractionalMileage(string culture)
    {
        var prior = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new(culture);
            var row = new Dictionary<string, object?> { ["year"] = "2020", ["odometerMiles"] = "1.5" };
            Assert.Empty(Validate(row));
            var clean = (Dictionary<string, object?>)typeof(EndpointMappings).GetMethod("CleanVehicleImportRow", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { row })!;
            Assert.Equal(2020L, clean["year"]);
            Assert.Equal(1.5m, clean["odometerMiles"]);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = prior; }
    }

    [Fact]
    public void CreateUpdateAndImportUseSharedPolicyBeforePersistence()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        var source = File.ReadAllText(Path.Combine(root, "backend-dotnet", "Controllers", "EndpointMappings.cs"));
        foreach (var (start, end, persistence) in new[] {
            ("private static async Task<IResult> CreateVehicle", "private static async Task<IResult> UpdateVehicle", "db.InsertWithSavepointAsync"),
            ("private static async Task<IResult> UpdateVehicle", "private static async Task<IResult> CreateDriver", "db.ExecuteWithSavepointAsync") })
        {
            var offset = source.IndexOf(start, StringComparison.Ordinal);
            var limit = source.IndexOf(end, offset + start.Length, StringComparison.Ordinal);
            var block = source[offset..limit];
            var validation = block.IndexOf("ValidateAndNormalizeVehicleNumbers(body)", StringComparison.Ordinal);
            Assert.True(validation >= 0 && validation < block.IndexOf(persistence, StringComparison.Ordinal));
        }
        var importOffset = source.IndexOf("private static List<string> ValidateVehicleImportRow", StringComparison.Ordinal);
        Assert.Contains("ValidateAndNormalizeVehicleNumbers(row)", source[importOffset..source.IndexOf("private static List<string> ValidateAndNormalizeVehicleNumbers", importOffset, StringComparison.Ordinal)]);
    }

    private static List<string> Validate(Dictionary<string, object?> body) =>
        (List<string>)typeof(EndpointMappings).GetMethod("ValidateAndNormalizeVehicleNumbers", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { body })!;
}
