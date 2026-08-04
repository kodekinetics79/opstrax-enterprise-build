using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Security;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class CoreFleetIdentityConflictPostgresTests
{
    [Fact]
    public async Task VehicleCreate_ArchivedCode_ReturnsStableConflict()
    {
        var db = Db();
        var companyId = await SeedCompany(db);
        try
        {
            await db.ExecuteAsync(
                "INSERT INTO vehicles(company_id,vehicle_code,type,deleted_at) VALUES (@c,'ARCHIVED-VEHICLE','Truck',NOW())",
                c => c.Parameters.AddWithValue("@c", companyId));

            var result = await CreateVehicle(companyId, "ARCHIVED-VEHICLE");

            AssertConflict(result, "Vehicle code 'ARCHIVED-VEHICLE' already exists in this fleet.");
            Assert.Equal(1, await Count(db, "vehicles", companyId));
        }
        finally { await Cleanup(db, companyId); }
    }

    [Fact]
    public async Task DriverCreate_ArchivedCode_ReturnsStableConflict()
    {
        var db = Db();
        var companyId = await SeedCompany(db);
        try
        {
            await db.ExecuteAsync(
                "INSERT INTO drivers(company_id,driver_code,full_name,deleted_at) VALUES (@c,'ARCHIVED-DRIVER','Archived Driver',NOW())",
                c => c.Parameters.AddWithValue("@c", companyId));

            var result = await CreateDriver(companyId, "ARCHIVED-DRIVER");

            AssertConflict(result, "Driver code 'ARCHIVED-DRIVER' already exists in this fleet.");
            Assert.Equal(1, await Count(db, "drivers", companyId));
        }
        finally { await Cleanup(db, companyId); }
    }

    [Fact]
    public async Task VehicleCreate_ConcurrentSameCode_CreatesOneAndConflictsTheRest()
    {
        var db = Db();
        var companyId = await SeedCompany(db);
        try
        {
            var results = await Task.WhenAll(
                Enumerable.Range(0, 24).Select(_ => CreateVehicle(companyId, "RACE-VEHICLE")));

            Assert.Equal(1, results.Count(IsCreated));
            Assert.Equal(23, results.Count(IsConflict));
            Assert.All(results.Where(IsConflict), r =>
                AssertConflict(r, "Vehicle code 'RACE-VEHICLE' already exists in this fleet."));
            Assert.Equal(1, await Count(db, "vehicles", companyId));
        }
        finally { await Cleanup(db, companyId); }
    }

    [Fact]
    public async Task DriverCreate_ConcurrentSameCode_CreatesOneAndConflictsTheRest()
    {
        var db = Db();
        var companyId = await SeedCompany(db);
        try
        {
            var results = await Task.WhenAll(
                Enumerable.Range(0, 24).Select(_ => CreateDriver(companyId, "RACE-DRIVER")));

            Assert.Equal(1, results.Count(IsCreated));
            Assert.Equal(23, results.Count(IsConflict));
            Assert.All(results.Where(IsConflict), r =>
                AssertConflict(r, "Driver code 'RACE-DRIVER' already exists in this fleet."));
            Assert.Equal(1, await Count(db, "drivers", companyId));
        }
        finally { await Cleanup(db, companyId); }
    }

    [Fact]
    public async Task VehicleCreate_ConcurrentDifferentCodesSameVin_CreatesOneAndConflictsTheRest()
    {
        var db = Db();
        var companyId = await SeedCompany(db);
        try
        {
            var results = await Task.WhenAll(Enumerable.Range(0, 24).Select(i =>
                CreateVehicle(companyId, $"VIN-RACE-{i:00}", "SHARED-VIN")));

            Assert.Equal(1, results.Count(IsCreated));
            Assert.Equal(23, results.Count(IsConflict));
            Assert.All(results.Where(IsConflict), r => AssertConflict(r,
                "VIN 'SHARED-VIN' is already registered to another vehicle."));
            Assert.Equal(1, await Count(db, "vehicles", companyId));
        }
        finally { await Cleanup(db, companyId); }
    }

    [Fact]
    public async Task DriverCreate_ConcurrentDifferentCodesSameEncryptedLicense_CreatesOneAndConflictsTheRest()
    {
        var db = Db();
        var companyId = await SeedCompany(db);
        try
        {
            var results = await Task.WhenAll(Enumerable.Range(0, 24).Select(i =>
                CreateDriver(companyId, $"LICENSE-RACE-{i:00}", "SHARED-LICENSE", piiEnabled: true)));

            Assert.Equal(1, results.Count(IsCreated));
            Assert.Equal(23, results.Count(IsConflict));
            Assert.All(results.Where(IsConflict), r => AssertConflict(r,
                "License number 'SHARED-LICENSE' is already registered to another driver."));
            Assert.Equal(1, await Count(db, "drivers", companyId));
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(DISTINCT license_number_bidx) FROM drivers WHERE company_id=@c AND license_number LIKE 'enc:%'",
                c => c.Parameters.AddWithValue("@c", companyId)));
        }
        finally { await Cleanup(db, companyId); }
    }

    [Fact]
    public async Task VehicleUpdate_ConcurrentCodeAndVinRaces_ReturnStableConflicts()
    {
        var db = Db();
        var companyId = await SeedCompany(db);
        try
        {
            var codeA = await InsertVehicle(db, companyId, "UPDATE-CODE-A");
            var codeB = await InsertVehicle(db, companyId, "UPDATE-CODE-B");
            var codeResults = await Task.WhenAll(
                UpdateVehicle(companyId, codeA, new() { ["vehicleCode"] = "UPDATE-CODE-SHARED" }),
                UpdateVehicle(companyId, codeB, new() { ["vehicleCode"] = "UPDATE-CODE-SHARED" }));
            Assert.Equal(1, codeResults.Count(IsOk));
            Assert.Equal(1, codeResults.Count(IsConflict));
            AssertConflict(codeResults.Single(IsConflict),
                "Vehicle code 'UPDATE-CODE-SHARED' already exists in this fleet.");

            var vinA = await InsertVehicle(db, companyId, "UPDATE-VIN-A");
            var vinB = await InsertVehicle(db, companyId, "UPDATE-VIN-B");
            var vinResults = await Task.WhenAll(
                UpdateVehicle(companyId, vinA, new() { ["vin"] = "UPDATE-VIN-SHARED" }),
                UpdateVehicle(companyId, vinB, new() { ["vin"] = "UPDATE-VIN-SHARED" }));
            Assert.Equal(1, vinResults.Count(IsOk));
            Assert.Equal(1, vinResults.Count(IsConflict));
            AssertConflict(vinResults.Single(IsConflict),
                "VIN 'UPDATE-VIN-SHARED' is already registered to another vehicle.");
        }
        finally { await Cleanup(db, companyId); }
    }

    [Fact]
    public async Task DriverUpdate_ConcurrentCodeAndEncryptedLicenseRaces_ReturnStableConflicts()
    {
        var db = Db();
        var companyId = await SeedCompany(db);
        try
        {
            var codeA = await InsertDriver(db, companyId, "UPDATE-DRIVER-A");
            var codeB = await InsertDriver(db, companyId, "UPDATE-DRIVER-B");
            var codeResults = await Task.WhenAll(
                UpdateDriver(companyId, codeA, new() { ["driverCode"] = "UPDATE-DRIVER-SHARED" }, true),
                UpdateDriver(companyId, codeB, new() { ["driverCode"] = "UPDATE-DRIVER-SHARED" }, true));
            Assert.Equal(1, codeResults.Count(IsOk));
            Assert.Equal(1, codeResults.Count(IsConflict));
            AssertConflict(codeResults.Single(IsConflict),
                "Driver code 'UPDATE-DRIVER-SHARED' already exists in this fleet.");

            var licenseA = await InsertDriver(db, companyId, "UPDATE-LICENSE-A");
            var licenseB = await InsertDriver(db, companyId, "UPDATE-LICENSE-B");
            var licenseResults = await Task.WhenAll(
                UpdateDriver(companyId, licenseA, new() { ["licenseNumber"] = "UPDATE-LICENSE-SHARED" }, true),
                UpdateDriver(companyId, licenseB, new() { ["licenseNumber"] = "UPDATE-LICENSE-SHARED" }, true));
            Assert.Equal(1, licenseResults.Count(IsOk));
            Assert.Equal(1, licenseResults.Count(IsConflict));
            AssertConflict(licenseResults.Single(IsConflict),
                "License number 'UPDATE-LICENSE-SHARED' is already registered to another driver.");
        }
        finally { await Cleanup(db, companyId); }
    }

    [Fact]
    public async Task PiiTransition_EncryptedCreateUpdateAndImportsRejectLegacyPlaintextLicense()
    {
        var db = Db();
        var companyId = await SeedCompany(db);
        try
        {
            await db.ExecuteAsync(@"INSERT INTO drivers(company_id,driver_code,full_name,license_number,license_number_bidx)
                VALUES (@c,'LEGACY-PLAIN','Legacy Plaintext','TRANSITION-LICENSE',NULL)",
                c => c.Parameters.AddWithValue("@c", companyId));
            var updateTarget = await InsertDriver(db, companyId, "TRANSITION-UPDATE");

            var create = await CreateDriver(companyId, "TRANSITION-CREATE", " transition-license ", piiEnabled: true);
            AssertConflict(create, "License number 'transition-license' is already registered to another driver.");

            var update = await UpdateDriver(companyId, updateTarget,
                new() { ["licenseNumber"] = " TRANSITION-LICENSE " }, piiEnabled: true);
            AssertConflict(update, "License number 'TRANSITION-LICENSE' is already registered to another driver.");

            var importBody = JsonSerializer.Deserialize<Dictionary<string, object?>>(@"{
                ""rows"": [{
                    ""driverCode"": ""TRANSITION-IMPORT"",
                    ""fullName"": ""Transition Import"",
                    ""licenseNumber"": ""transition-license""
                }]
            }")!;
            var preview = await Invoke("DriversImportPreview", Principal(companyId, piiEnabled: true),
                importBody, Db(), CancellationToken.None);
            using (var payload = Payload(preview))
            {
                Assert.Equal(1, payload.RootElement.GetProperty("data").GetProperty("invalid").GetInt32());
                Assert.Contains(payload.RootElement.GetProperty("data").GetProperty("rows")[0]
                        .GetProperty("errors").EnumerateArray(),
                    error => error.GetString() == "License number 'transition-license' is already registered to another driver.");
            }

            var commit = await Invoke("DriversImportCommit", Principal(companyId, piiEnabled: true),
                importBody, Db(), new AuditService(Db()), CancellationToken.None);
            using (var payload = Payload(commit))
            {
                Assert.Equal(0, payload.RootElement.GetProperty("data").GetProperty("created").GetInt32());
                Assert.Single(payload.RootElement.GetProperty("data").GetProperty("skipped").EnumerateArray());
            }

            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM drivers WHERE company_id=@c AND driver_code IN ('TRANSITION-CREATE','TRANSITION-IMPORT')",
                c => c.Parameters.AddWithValue("@c", companyId)));
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM drivers WHERE id=@id AND license_number IS NOT NULL",
                c => c.Parameters.AddWithValue("@id", updateTarget)));
        }
        finally { await Cleanup(db, companyId); }
    }

    [Fact]
    public async Task AmbientTenantTransactions_ConcurrentCreateConflictsRemainCommittable()
    {
        var db = Db();
        var companyId = await SeedCompany(db);
        try
        {
            var vehicleResults = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
                CreateVehicleInAmbientTransaction(companyId, "AMBIENT-RACE-VEHICLE")));
            Assert.Equal(1, vehicleResults.Count(IsCreated));
            Assert.Equal(19, vehicleResults.Count(IsConflict));

            var driverResults = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
                CreateDriverInAmbientTransaction(companyId, "AMBIENT-RACE-DRIVER")));
            Assert.Equal(1, driverResults.Count(IsCreated));
            Assert.Equal(19, driverResults.Count(IsConflict));
        }
        finally { await Cleanup(db, companyId); }
    }

    [Fact]
    public async Task ConcurrentImportIdentityRacesReturnDeterministicPerRowSkips()
    {
        var db = Db();
        var companyId = await SeedCompany(db);
        try
        {
            var vehicleResults = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
                CommitVehicleImport(companyId, $"IMPORT-VIN-{i:00}", "IMPORT-SHARED-VIN")));
            AssertImportContention(vehicleResults, "VIN 'IMPORT-SHARED-VIN' is already registered to another vehicle.");

            var driverResults = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
                CommitDriverImport(companyId, $"IMPORT-LIC-{i:00}", "IMPORT-SHARED-LICENSE")));
            AssertImportContention(driverResults,
                "License number 'IMPORT-SHARED-LICENSE' is already registered to another driver.");

            // Same-code contention may legitimately resolve late arrivals as updates,
            // but every request must remain a stable 200 import result rather than a
            // leaked unique violation or transaction-aborted 500.
            var codeResults = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
                CommitVehicleImport(companyId, "IMPORT-SHARED-CODE", null)));
            Assert.All(codeResults, result => Assert.True(IsOk(result)));
            Assert.Equal(20, codeResults.Sum(ImportProcessedRows));
        }
        finally { await Cleanup(db, companyId); }
    }

    [Fact]
    public async Task BranchImportPreviewAndCommit_UseTenantWideVinAndLicenseIdentityDomains()
    {
        var db = Db();
        var companyId = await SeedCompany(db);
        var otherCompanyId = await SeedCompany(db);
        const long ownerBranch = 101;
        const long callerBranch = 202;
        try
        {
            await db.ExecuteAsync(@"INSERT INTO vehicles(company_id,branch_id,vehicle_code,type,vin)
                VALUES (@c,@b,'BRANCH-OWNER-VEHICLE','Truck','TENANT-WIDE-VIN')",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", ownerBranch); });
            var ownerDriver = await Invoke("CreateDriver", Principal(companyId, piiEnabled: true, branchId: ownerBranch),
                new Dictionary<string, object?>
                {
                    ["driverCode"] = "BRANCH-OWNER-DRIVER",
                    ["fullName"] = "Branch Owner Driver",
                    ["licenseNumber"] = "TENANT-WIDE-LICENSE",
                }, db, new AuditService(db), CancellationToken.None);
            Assert.True(IsCreated(ownerDriver));
            await db.ExecuteAsync(@"INSERT INTO drivers
                    (company_id,branch_id,driver_code,full_name,license_number,license_number_bidx)
                VALUES (@c,@b,'LEGACY-BRANCH-OWNER','Legacy Branch Owner',
                        'LEGACY-CROSS-BRANCH-LICENSE',NULL)",
                c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", ownerBranch); });

            var vehicleBody = ImportBody(new()
            {
                ["vehicleCode"] = "CALLER-BRANCH-VEHICLE",
                ["type"] = "Truck",
                ["vin"] = "TENANT-WIDE-VIN",
            });
            var driverBody = ImportBody(new()
            {
                ["driverCode"] = "CALLER-BRANCH-DRIVER",
                ["fullName"] = "Caller Branch Driver",
                ["licenseNumber"] = "TENANT-WIDE-LICENSE",
            });
            var legacyDriverBody = ImportBody(new()
            {
                ["driverCode"] = "CALLER-BRANCH-LEGACY-DRIVER",
                ["fullName"] = "Caller Branch Legacy Collision",
                ["licenseNumber"] = " legacy-cross-branch-license ",
            });

            await AssertImportPreviewConflict("VehiclesImportPreview",
                Principal(companyId, branchId: callerBranch), vehicleBody,
                "VIN 'TENANT-WIDE-VIN' is already registered to another vehicle.");
            await AssertImportCommitConflict("VehiclesImportCommit",
                Principal(companyId, branchId: callerBranch), vehicleBody,
                "VIN 'TENANT-WIDE-VIN' is already registered to another vehicle.");
            await AssertImportPreviewConflict("DriversImportPreview",
                Principal(companyId, piiEnabled: true, branchId: callerBranch), driverBody,
                "License number 'TENANT-WIDE-LICENSE' is already registered to another driver.");
            await AssertImportCommitConflict("DriversImportCommit",
                Principal(companyId, piiEnabled: true, branchId: callerBranch), driverBody,
                "License number 'TENANT-WIDE-LICENSE' is already registered to another driver.");
            await AssertImportPreviewConflict("DriversImportPreview",
                Principal(companyId, piiEnabled: true, branchId: callerBranch), legacyDriverBody,
                "License number 'legacy-cross-branch-license' is already registered to another driver.");
            await AssertImportCommitConflict("DriversImportCommit",
                Principal(companyId, piiEnabled: true, branchId: callerBranch), legacyDriverBody,
                "License number 'legacy-cross-branch-license' is already registered to another driver.");

            // The same submitted identities in a different tenant remain valid.  This
            // locks the broader identity lookup to company_id and prevents a global
            // cross-tenant existence oracle.
            await AssertImportPreviewCreate("VehiclesImportPreview", Principal(otherCompanyId, branchId: callerBranch), vehicleBody);
            await AssertImportPreviewCreate("DriversImportPreview", Principal(otherCompanyId, piiEnabled: true, branchId: callerBranch), driverBody);

            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM vehicles WHERE company_id=@c AND vehicle_code='CALLER-BRANCH-VEHICLE'",
                c => c.Parameters.AddWithValue("@c", companyId)));
            Assert.Equal(0, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM drivers WHERE company_id=@c AND driver_code='CALLER-BRANCH-DRIVER'",
                c => c.Parameters.AddWithValue("@c", companyId)));
        }
        finally
        {
            await Cleanup(db, companyId);
            await Cleanup(db, otherCompanyId);
        }
    }

    private static Task<IResult> CreateVehicle(long companyId, string code, string? vin = null)
        => Invoke("CreateVehicle", Principal(companyId),
            new Dictionary<string, object?>
            {
                ["vehicleCode"] = code,
                ["type"] = "Truck",
                ["vin"] = vin,
            }, Db(), new AuditService(Db()), CancellationToken.None);

    private static Task<IResult> CreateDriver(long companyId, string code, string? license = null, bool piiEnabled = false)
        => Invoke("CreateDriver", Principal(companyId, piiEnabled),
            new Dictionary<string, object?>
            {
                ["driverCode"] = code,
                ["fullName"] = "Fleet Identity Test Driver",
                ["licenseNumber"] = license,
            }, Db(), new AuditService(Db()), CancellationToken.None);

    private static Task<IResult> UpdateVehicle(long companyId, long id, Dictionary<string, object?> body)
        => Invoke("UpdateVehicle", Principal(companyId), id, body, Db(), new AuditService(Db()), CancellationToken.None);

    private static Task<IResult> UpdateDriver(long companyId, long id, Dictionary<string, object?> body, bool piiEnabled)
        => Invoke("UpdateDriver", Principal(companyId, piiEnabled), id, body, Db(), new AuditService(Db()), CancellationToken.None);

    private static async Task<IResult> CreateVehicleInAmbientTransaction(long companyId, string code)
    {
        var accessor = new TenantScopeAccessor();
        var db = Db(accessor);
        await using var scope = await db.BeginTenantScopeAsync(companyId);
        accessor.Current = scope;
        try
        {
            var result = await Invoke("CreateVehicle", Principal(companyId),
                new Dictionary<string, object?> { ["vehicleCode"] = code, ["type"] = "Truck" },
                db, new AuditService(db), CancellationToken.None);
            await scope.CompleteAsync();
            return result;
        }
        finally { accessor.Current = null; }
    }

    private static async Task<IResult> CreateDriverInAmbientTransaction(long companyId, string code)
    {
        var accessor = new TenantScopeAccessor();
        var db = Db(accessor);
        await using var scope = await db.BeginTenantScopeAsync(companyId);
        accessor.Current = scope;
        try
        {
            var result = await Invoke("CreateDriver", Principal(companyId),
                new Dictionary<string, object?> { ["driverCode"] = code, ["fullName"] = "Ambient Race Driver" },
                db, new AuditService(db), CancellationToken.None);
            await scope.CompleteAsync();
            return result;
        }
        finally { accessor.Current = null; }
    }

    private static async Task<IResult> CommitVehicleImport(long companyId, string code, string? vin)
    {
        var accessor = new TenantScopeAccessor();
        var db = Db(accessor);
        await using var scope = await db.BeginTenantScopeAsync(companyId);
        accessor.Current = scope;
        try
        {
            var result = await Invoke("VehiclesImportCommit", Principal(companyId), ImportBody(new()
            {
                ["vehicleCode"] = code,
                ["type"] = "Truck",
                ["vin"] = vin,
            }), db, new AuditService(db), CancellationToken.None);
            await scope.CompleteAsync();
            return result;
        }
        finally { accessor.Current = null; }
    }

    private static async Task<IResult> CommitDriverImport(long companyId, string code, string license)
    {
        var accessor = new TenantScopeAccessor();
        var db = Db(accessor);
        await using var scope = await db.BeginTenantScopeAsync(companyId);
        accessor.Current = scope;
        try
        {
            var result = await Invoke("DriversImportCommit", Principal(companyId, piiEnabled: true), ImportBody(new()
            {
                ["driverCode"] = code,
                ["fullName"] = "Import Race Driver",
                ["licenseNumber"] = license,
            }), db, new AuditService(db), CancellationToken.None);
            await scope.CompleteAsync();
            return result;
        }
        finally { accessor.Current = null; }
    }

    private static Dictionary<string, object?> ImportBody(Dictionary<string, object?> row)
        => JsonSerializer.Deserialize<Dictionary<string, object?>>(
            JsonSerializer.Serialize(new { rows = new[] { row } }))!;

    private static void AssertImportContention(IReadOnlyCollection<IResult> results, string expectedError)
    {
        Assert.All(results, result => Assert.True(IsOk(result)));
        Assert.Equal(1, results.Sum(ImportCreatedRows));
        Assert.Equal(results.Count - 1, results.Sum(ImportSkippedRows));
        foreach (var result in results.Where(result => ImportSkippedRows(result) == 1))
        {
            using var payload = Payload(result);
            Assert.Contains(payload.RootElement.GetProperty("data").GetProperty("skipped")[0]
                    .GetProperty("errors").EnumerateArray(),
                error => error.GetString() == expectedError);
        }
    }

    private static int ImportCreatedRows(IResult result)
    {
        using var payload = Payload(result);
        return payload.RootElement.GetProperty("data").GetProperty("created").GetInt32();
    }

    private static int ImportSkippedRows(IResult result)
    {
        using var payload = Payload(result);
        return payload.RootElement.GetProperty("data").GetProperty("skipped").GetArrayLength();
    }

    private static int ImportProcessedRows(IResult result)
    {
        using var payload = Payload(result);
        var data = payload.RootElement.GetProperty("data");
        return data.GetProperty("created").GetInt32() + data.GetProperty("updated").GetInt32() +
               data.GetProperty("skipped").GetArrayLength();
    }

    private static async Task AssertImportPreviewConflict(string method, DefaultHttpContext principal,
        Dictionary<string, object?> body, string expectedError)
    {
        var result = await Invoke(method, principal, body, Db(), CancellationToken.None);
        Assert.True(IsOk(result));
        using var payload = Payload(result);
        var data = payload.RootElement.GetProperty("data");
        Assert.Equal(0, data.GetProperty("creates").GetInt32());
        Assert.Equal(1, data.GetProperty("invalid").GetInt32());
        Assert.Contains(data.GetProperty("rows")[0].GetProperty("errors").EnumerateArray(),
            error => error.GetString() == expectedError);
    }

    private static async Task AssertImportCommitConflict(string method, DefaultHttpContext principal,
        Dictionary<string, object?> body, string expectedError)
    {
        var db = Db();
        var result = await Invoke(method, principal, body, db, new AuditService(db), CancellationToken.None);
        Assert.True(IsOk(result));
        using var payload = Payload(result);
        var data = payload.RootElement.GetProperty("data");
        Assert.Equal(0, data.GetProperty("created").GetInt32());
        Assert.Equal(1, data.GetProperty("skipped").GetArrayLength());
        Assert.Contains(data.GetProperty("skipped")[0].GetProperty("errors").EnumerateArray(),
            error => error.GetString() == expectedError);
    }

    private static async Task AssertImportPreviewCreate(string method, DefaultHttpContext principal,
        Dictionary<string, object?> body)
    {
        var result = await Invoke(method, principal, body, Db(), CancellationToken.None);
        Assert.True(IsOk(result));
        using var payload = Payload(result);
        var data = payload.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("creates").GetInt32());
        Assert.Equal(0, data.GetProperty("invalid").GetInt32());
    }

    private static async Task<IResult> Invoke(string methodName, params object[] arguments)
    {
        var method = typeof(EndpointMappings).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
        try { return await (Task<IResult>)method.Invoke(null, arguments)!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static DefaultHttpContext Principal(long companyId, bool piiEnabled = false, long? branchId = null)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IDataKeyProvider>(piiEnabled
                ? new ConfiguredDataKeyProvider()
                : new DisabledDataKeyProvider())
            .AddSingleton<PiiProtectionService>()
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
        http.Items[EndpointMappings.AuthUserIdItemKey] = 0L;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Tenant Admin";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "fleet:manage" };
        if (branchId is not null)
            http.Items[EndpointMappings.AuthBranchIdItemKey] = branchId.Value;
        return http;
    }

    private static bool IsCreated(IResult result)
        => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode == StatusCodes.Status201Created;

    private static bool IsConflict(IResult result)
        => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode == StatusCodes.Status409Conflict;

    private static bool IsOk(IResult result)
        => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode == StatusCodes.Status200OK;

    private static void AssertConflict(IResult result, string expectedError)
    {
        Assert.True(IsConflict(result));
        var value = Assert.IsAssignableFrom<IValueHttpResult>(result).Value;
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var payload = JsonDocument.Parse(json);
        Assert.Contains(payload.RootElement.GetProperty("errors").EnumerateArray(),
            error => string.Equals(error.GetString(), expectedError, StringComparison.Ordinal));
    }

    private static JsonDocument Payload(IResult result)
    {
        var value = Assert.IsAssignableFrom<IValueHttpResult>(result).Value;
        return JsonDocument.Parse(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static Database Db(TenantScopeAccessor? accessor = null) => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
            ["Rls:EnforceTenantContext"] = "false",
        }).Build(), accessor);

    private static Task<long> SeedCompany(Database db) => db.InsertAsync(
        "INSERT INTO companies(company_code,name,industry) VALUES (@code,'Fleet Identity Conflict Test','Transportation')",
        c => c.Parameters.AddWithValue("@code", $"FIC-{Guid.NewGuid():N}"));

    private static Task<long> Count(Database db, string table, long companyId)
        => db.ScalarLongAsync($"SELECT COUNT(*) FROM {table} WHERE company_id=@c",
            c => c.Parameters.AddWithValue("@c", companyId));

    private static Task<long> InsertVehicle(Database db, long companyId, string code)
        => db.InsertAsync(
            "INSERT INTO vehicles(company_id,vehicle_code,type) VALUES (@c,@code,'Truck')",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", code); });

    private static Task<long> InsertDriver(Database db, long companyId, string code)
        => db.InsertAsync(
            "INSERT INTO drivers(company_id,driver_code,full_name) VALUES (@c,@code,'Update Test Driver')",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", code); });

    private static async Task Cleanup(Database db, long companyId)
    {
        foreach (var table in new[] { "audit_logs", "vehicles", "drivers" })
            await db.ExecuteAsync($"DELETE FROM {table} WHERE company_id=@c",
                c => c.Parameters.AddWithValue("@c", companyId));
        await db.ExecuteAsync("DELETE FROM companies WHERE id=@c",
            c => c.Parameters.AddWithValue("@c", companyId));
    }

    private sealed class DisabledDataKeyProvider : IDataKeyProvider
    {
        public (byte KeyId, byte[] Key) ActiveKey => (0, new byte[32]);
        public byte[]? ResolveKey(byte keyId) => null;
        public byte[] IndexKey => new byte[32];
        public bool IsConfigured => false;
    }

    private sealed class ConfiguredDataKeyProvider : IDataKeyProvider
    {
        private static readonly byte[] DataKey = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        private static readonly byte[] BlindIndexKey = Enumerable.Range(33, 32).Select(i => (byte)i).ToArray();
        public (byte KeyId, byte[] Key) ActiveKey => (1, DataKey);
        public byte[]? ResolveKey(byte keyId) => keyId == 1 ? DataKey : null;
        public byte[] IndexKey => BlindIndexKey;
        public bool IsConfigured => true;
    }
}
