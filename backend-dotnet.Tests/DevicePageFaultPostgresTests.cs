using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class DevicePageFaultPostgresTests
{
    [Fact]
    public async Task DiagnosticsAndFaultCountRecognizeCanonicalLowercaseActiveFault()
    {
        var db = Db();
        var companyId = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES (@code,'Device fault page test','Transportation')",
            c => c.Parameters.AddWithValue("@code", $"DFP-{Guid.NewGuid():N}"));
        try
        {
            var serial = $"LOWER-ACTIVE-{Guid.NewGuid():N}";
            await db.ExecuteAsync(
                "INSERT INTO eld_devices(company_id,device_serial,status,device_state) VALUES (@cid,@serial,'Provisioning','Registered')",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@serial", serial); });
            await db.ExecuteAsync(
                @"INSERT INTO fault_codes
                    (company_id,device_id,protocol,code,canonical_identity,last_observed_at,last_source_event_id,status)
                  VALUES (@cid,@serial,'OBD','P1000','OBD:UNKNOWN:P1000',NOW(),@event,'active')",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@serial", serial);
                    c.Parameters.AddWithValue("@event", $"lower-active-{Guid.NewGuid():N}");
                });

            var http = Principal(companyId);
            http.Request.QueryString = new QueryString("?view=diagnostics&pageSize=100");
            var result = await Invoke("TelemetryDevicePage", http, db, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
            using var payload = Payload(result);
            var data = payload.RootElement.GetProperty("data");
            Assert.Equal(1, data.GetProperty("total").GetInt64());
            var item = Assert.Single(data.GetProperty("items").EnumerateArray());
            Assert.Equal(serial, item.GetProperty("deviceSerial").GetString());
            Assert.Equal(1, item.GetProperty("activeFaultCount").GetInt64());
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM fault_codes WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM eld_devices WHERE company_id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
        }
    }

    private static DefaultHttpContext Principal(long companyId)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthUserIdItemKey] = 41L;
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Fleet Manager";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "telematics:devices:view" };
        return http;
    }

    private static async Task<IResult> Invoke(string name, params object[] args)
    {
        var method = typeof(EndpointMappings).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Missing endpoint {name}");
        return await ((Task<IResult>)method.Invoke(null, args)!);
    }

    private static JsonDocument Payload(IResult result)
    {
        var value = Assert.IsAssignableFrom<IValueHttpResult>(result).Value;
        return JsonDocument.Parse(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static Database Db() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
            ["Rls:EnforceTenantContext"] = "false",
        }).Build(), new TenantScopeAccessor());
}
