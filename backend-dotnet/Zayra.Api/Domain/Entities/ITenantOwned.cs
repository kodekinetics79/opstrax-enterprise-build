namespace Zayra.Api.Domain.Entities;

/// <summary>
/// Marks a multi-tenant entity that owns a non-nullable tenant scope.
/// Every entity implementing this interface receives an automatic EF Core global query filter
/// (see <see cref="Zayra.Api.Data.ZayraDbContext"/>) that prevents cross-tenant data leaks.
///
/// Naming guarantee: the TenantId property MUST be named exactly "TenantId".
/// A misnamed property (TenantID, TenantIdentifier, etc.) will fail the
/// <see cref="Zayra.Api.Infrastructure.Boot.TenantOwnershipBootAssertion"/> check at startup.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}

/// <summary>
/// Like <see cref="ITenantOwned"/> but for entities where the tenant scope is nullable
/// (e.g. cross-tenant shared records where TenantId == null means "all tenants").
/// A null TenantId bypasses the query filter entirely, so use this sparingly.
/// </summary>
public interface INullableTenantOwned
{
    Guid? TenantId { get; set; }
}
