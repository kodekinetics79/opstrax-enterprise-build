using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Security;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class Module1ExportPostgresTests
{
    [Fact]
    public async Task VehicleAndDriverMasterExportsAreCompleteOrderedTenantAndBranchScopedAndSafe()
    {
        var db = Db();
        var pii = new PiiProtectionService(new TestKeyProvider(), NullLogger<PiiProtectionService>.Instance);
        var companyId = await Company(db, "MASTER");
        var foreignCompanyId = await Company(db, "FOREIGN");
        var branchA = await Branch(db, companyId, "AAA");
        var branchB = await Branch(db, companyId, "BBB");
        var foreignBranch = await Branch(db, foreignCompanyId, "FOREIGN");

        try
        {
            await Vehicle(db, companyId, branchB, "VEH-A", "=2+2", null, "manufacturer-serial-number", "ALT-A", "ON", "Class 8");
            await Vehicle(db, companyId, branchA, "VEH-Z", "Safe Make", "1HGCM82633A004352", null, null, "NY", "Class 6");
            await Vehicle(db, foreignCompanyId, foreignBranch, "FOREIGN-VEH", "Foreign", "1HGCM82633A004352", null, null, "CA", "Class 4");
            await Driver(db, companyId, branchB, "DRV-A", "+Formula Driver", pii.Encrypt("LIC-A-1234")!, pii.BlindIndex("LIC-A-1234"));
            await Driver(db, companyId, branchA, "DRV-Z", "Alpha Name", pii.Encrypt("LIC-Z-9876")!, pii.BlindIndex("LIC-Z-9876"));
            await Driver(db, foreignCompanyId, foreignBranch, "FOREIGN-DRV", "Foreign Driver", pii.Encrypt("LIC-F-0000")!, pii.BlindIndex("LIC-F-0000"));

            var tenantAdmin = Principal(companyId, null, "CompanyAdmin", "vehicles:export", "drivers:export");
            tenantAdmin.RequestServices = new ServiceCollection().AddSingleton(pii).BuildServiceProvider();
            var allVehicles = Csv(await Invoke(typeof(EndpointMappings), "VehiclesExport", tenantAdmin, db, CancellationToken.None));
            var allDrivers = Csv(await Invoke(typeof(EndpointMappings), "DriversExport", tenantAdmin, db, CancellationToken.None));

            Assert.StartsWith("vehicleCode,branchCode,type,make,model,year,vehicleClass,vin,vinExceptionType,alternateIdentifier,plateNumber,plateJurisdiction,status,odometerMiles,deviceStatus\n", allVehicles);
            Assert.Contains("VEH-A,BBB,Truck,'=2+2,Model,2024,Class 8,,manufacturer-serial-number,ALT-A,PLATE,ON,Available,", allVehicles);
            Assert.Contains("VEH-Z,AAA,Truck,Safe Make,Model,2024,Class 6,1HGCM82633A004352,,,PLATE,NY,Available,", allVehicles);
            Assert.True(allVehicles.IndexOf("VEH-A", StringComparison.Ordinal) < allVehicles.IndexOf("VEH-Z", StringComparison.Ordinal));
            Assert.DoesNotContain("FOREIGN-VEH", allVehicles, StringComparison.Ordinal);

            Assert.StartsWith("driverCode,branchCode,fullName,phone,email,licenseNumber,licenseExpiry,status,safetyScore,readinessScore,riskScore,complianceScore\n", allDrivers);
            Assert.Contains("DRV-A,BBB,'+Formula Driver,'+1-555-0100,driver@example.test,•••• 1234,2030-01-02,Available,", allDrivers);
            Assert.Contains("DRV-Z,AAA,Alpha Name,'+1-555-0100,driver@example.test,•••• 9876,2030-01-02,Available,", allDrivers);
            Assert.True(allDrivers.IndexOf("DRV-A", StringComparison.Ordinal) < allDrivers.IndexOf("DRV-Z", StringComparison.Ordinal));
            Assert.DoesNotContain("LIC-A-1234", allDrivers, StringComparison.Ordinal);
            Assert.DoesNotContain("enc:", allDrivers, StringComparison.Ordinal);
            Assert.DoesNotContain("licenseNumberBidx", allDrivers, StringComparison.Ordinal);
            Assert.DoesNotContain("FOREIGN-DRV", allDrivers, StringComparison.Ordinal);

            var branchManager = Principal(companyId, branchA, "Fleet Manager", "vehicles:export", "drivers:export");
            branchManager.RequestServices = tenantAdmin.RequestServices;
            var branchVehicles = Csv(await Invoke(typeof(EndpointMappings), "VehiclesExport", branchManager, db, CancellationToken.None));
            var branchDrivers = Csv(await Invoke(typeof(EndpointMappings), "DriversExport", branchManager, db, CancellationToken.None));
            Assert.Contains("VEH-Z,AAA", branchVehicles, StringComparison.Ordinal);
            Assert.DoesNotContain("VEH-A,BBB", branchVehicles, StringComparison.Ordinal);
            Assert.Contains("DRV-Z,AAA", branchDrivers, StringComparison.Ordinal);
            Assert.DoesNotContain("DRV-A,BBB", branchDrivers, StringComparison.Ordinal);
        }
        finally
        {
            foreach (var company in new[] { companyId, foreignCompanyId })
            {
                await db.ExecuteAsync("DELETE FROM drivers WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", company));
                await db.ExecuteAsync("DELETE FROM vehicles WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", company));
                await db.ExecuteAsync("DELETE FROM branches WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", company));
                await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", company));
            }
        }
    }

    [Fact]
    public async Task EmptyBranchScopedAssetAndDeviceExportsReturnCsvFiles()
    {
        var db = Db();
        var companyId = await db.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES (@code,'Module 1 export test','Transportation')",
            c => c.Parameters.AddWithValue("@code", $"M1E-{Guid.NewGuid():N}"));
        var branchId = await db.InsertAsync(
            "INSERT INTO branches(company_id,branch_code,name,status) VALUES (@cid,@code,'Export Branch','Active')",
            c =>
            {
                c.Parameters.AddWithValue("@cid", companyId);
                c.Parameters.AddWithValue("@code", $"EXP-{Guid.NewGuid():N}"[..20]);
            });

        try
        {
            var http = Principal(companyId, branchId);
            var assetResult = await Invoke(typeof(FleetTmsColdChainEndpoints), "AssetsExport", http, db, CancellationToken.None);
            var deviceResult = await Invoke(typeof(EndpointMappings), "TelemetryDeviceExport", http, db, CancellationToken.None);

            Assert.EndsWith(".csv", Assert.IsAssignableFrom<IFileHttpResult>(assetResult).FileDownloadName);
            Assert.EndsWith(".csv", Assert.IsAssignableFrom<IFileHttpResult>(deviceResult).FileDownloadName);
            Assert.Equal("text/csv", Assert.IsAssignableFrom<IContentTypeHttpResult>(assetResult).ContentType);
            Assert.Equal("text/csv", Assert.IsAssignableFrom<IContentTypeHttpResult>(deviceResult).ContentType);
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM branches WHERE id=@id AND company_id=@cid", c =>
            {
                c.Parameters.AddWithValue("@id", branchId);
                c.Parameters.AddWithValue("@cid", companyId);
            });
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@cid", c => c.Parameters.AddWithValue("@cid", companyId));
        }
    }

    private static DefaultHttpContext Principal(long companyId, long branchId)
        => Principal(companyId, (long?)branchId, "Fleet Manager", "telematics:devices:export");

    private static DefaultHttpContext Principal(long companyId, long? branchId, string role, params string[] permissions)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthUserIdItemKey] = 41L;
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
        if (branchId is not null) http.Items[EndpointMappings.AuthBranchIdItemKey] = branchId.Value;
        http.Items[EndpointMappings.AuthRoleItemKey] = role;
        http.Items[EndpointMappings.AuthPermissionsItemKey] = permissions;
        return http;
    }

    private static string Csv(IResult result)
    {
        Assert.Equal("text/csv", Assert.IsAssignableFrom<IContentTypeHttpResult>(result).ContentType);
        var bytes = Assert.IsType<byte[]>(result.GetType().GetProperty("FileContents")!.GetValue(result));
        return Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static Task<long> Company(Database db, string suffix) => db.InsertAsync(
        "INSERT INTO companies(company_code,name,industry) VALUES (@code,@name,'Transportation')",
        c => { c.Parameters.AddWithValue("@code", $"M1X-{suffix}-{Guid.NewGuid():N}"); c.Parameters.AddWithValue("@name", $"Module 1 {suffix}"); });

    private static Task<long> Branch(Database db, long companyId, string code) => db.InsertAsync(
        "INSERT INTO branches(company_id,branch_code,name,status) VALUES (@c,@code,@name,'Active')",
        c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", code); c.Parameters.AddWithValue("@name", $"Branch {code}"); });

    private static Task<long> Vehicle(Database db, long companyId, long branchId, string code, string make,
        string? vin, string? exception, string? alternate, string jurisdiction, string vehicleClass) => db.InsertAsync(
        @"INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,make,model,year,vehicle_class,vin,vin_exception_type,
                               alternate_identifier,plate_number,plate_jurisdiction,status,odometer_miles,device_status)
          VALUES (@c,@b,@code,'Truck',@make,'Model',2024,@class,@vin,@exception,@alternate,'PLATE',@jurisdiction,'Available',10,'Online')",
        c =>
        {
            c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId);
            c.Parameters.AddWithValue("@code", code); c.Parameters.AddWithValue("@make", make);
            c.Parameters.AddWithValue("@class", vehicleClass); c.Parameters.AddWithValue("@vin", (object?)vin ?? DBNull.Value);
            c.Parameters.AddWithValue("@exception", (object?)exception ?? DBNull.Value);
            c.Parameters.AddWithValue("@alternate", (object?)alternate ?? DBNull.Value);
            c.Parameters.AddWithValue("@jurisdiction", jurisdiction);
        });

    private static Task<long> Driver(Database db, long companyId, long branchId, string code, string name,
        string license, string? blindIndex) => db.InsertAsync(
        @"INSERT INTO drivers(company_id,branch_id,driver_code,full_name,phone,email,license_number,license_number_bidx,
                              license_expiry,status,safety_score,readiness_score,risk_score,compliance_score)
          VALUES (@c,@b,@code,@name,'+1-555-0100','driver@example.test',@license,@bidx,'2030-01-02','Available',91,92,8,93)",
        c =>
        {
            c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", branchId);
            c.Parameters.AddWithValue("@code", code); c.Parameters.AddWithValue("@name", name);
            c.Parameters.AddWithValue("@license", license); c.Parameters.AddWithValue("@bidx", (object?)blindIndex ?? DBNull.Value);
        });

    private static async Task<IResult> Invoke(Type owner, string name, params object[] args)
    {
        var method = owner.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Missing endpoint {owner.Name}.{name}");
        return await ((Task<IResult>)method.Invoke(null, args)!);
    }

    private static Database Db() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
            ["Rls:EnforceTenantContext"] = "false",
        }).Build(), new TenantScopeAccessor());
}
