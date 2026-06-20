using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Domain.Entities;

namespace Zayra.Api.Infrastructure.Boot;

/// <summary>
/// Boot-time guard: any EF-mapped entity with a TenantId-shaped property MUST implement
/// <see cref="ITenantOwned"/> or <see cref="INullableTenantOwned"/>. Without this check,
/// a mis-declared entity (renamed property, copy-paste error) silently loses its query filter
/// and leaks data cross-tenant with no runtime error.
///
/// This runs BEFORE the first HTTP request so a bad entity becomes a failed boot, not a live leak.
/// </summary>
public static class TenantOwnershipBootAssertion
{
    public static void Assert(DbContext db)
    {
        var violations = new List<string>();

        foreach (var entityType in db.Model.GetEntityTypes())
        {
            if (entityType.IsOwned() || entityType.BaseType is not null) continue;

            var clr = entityType.ClrType;
            var tenantIdProp = clr.GetProperty(
                "TenantId",
                BindingFlags.Public | BindingFlags.Instance);

            var implementsOwned         = typeof(ITenantOwned).IsAssignableFrom(clr);
            var implementsNullableOwned = typeof(INullableTenantOwned).IsAssignableFrom(clr);
            var implementsEither        = implementsOwned || implementsNullableOwned;

            // Case 1: has a TenantId property but doesn't implement the interface
            if (tenantIdProp is not null && !implementsEither)
            {
                violations.Add(
                    $"  {clr.FullName}: has TenantId (type={tenantIdProp.PropertyType.Name}) " +
                    $"but does not implement ITenantOwned or INullableTenantOwned. " +
                    $"Add the appropriate interface or the global query filter will NOT apply.");
            }

            // Case 2: implements the interface but TenantId property is missing or mapped wrong
            if (implementsEither && tenantIdProp is null)
            {
                violations.Add(
                    $"  {clr.FullName}: implements ITenantOwned/INullableTenantOwned " +
                    $"but has no public TenantId property — the query filter cannot be applied.");
            }

            // Case 3: nullable/non-nullable mismatch between property type and interface
            if (tenantIdProp is not null)
            {
                if (implementsOwned && tenantIdProp.PropertyType != typeof(Guid))
                    violations.Add(
                        $"  {clr.FullName}: implements ITenantOwned (non-nullable) " +
                        $"but TenantId property type is {tenantIdProp.PropertyType.Name} — use INullableTenantOwned instead.");

                if (implementsNullableOwned && tenantIdProp.PropertyType != typeof(Guid?))
                    violations.Add(
                        $"  {clr.FullName}: implements INullableTenantOwned " +
                        $"but TenantId property type is {tenantIdProp.PropertyType.Name} — use ITenantOwned instead.");
            }
        }

        if (violations.Count > 0)
            throw new InvalidOperationException(
                $"Tenant-ownership boot assertion failed — {violations.Count} entity type(s) are mis-declared:\n" +
                string.Join("\n", violations) +
                "\n\nFix: implement ITenantOwned (Guid TenantId) or INullableTenantOwned (Guid? TenantId) on each entity.");
    }
}
