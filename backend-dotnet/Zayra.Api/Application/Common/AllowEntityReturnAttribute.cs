namespace Zayra.Api.Application.Common;

/// <summary>
/// Opt-out from the <see cref="Zayra.Api.Infrastructure.Boot.ControllerEntityReturnBootAssertion"/>
/// startup check. Apply to a controller action when it intentionally returns a raw EF entity
/// type directly, and document WHY with the mandatory reason string.
///
/// Use only when all of these are true:
///  1. The entity contains no sensitive PII (salary, bank, passport, medical, national ID).
///  2. Internal system fields (TenantId, IsDeleted) being serialized is acceptable.
///  3. A DTO alternative would add no security or API-contract value.
///
/// If the entity has sensitive fields, create a projected DTO instead (see EmployeeDetailDto).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class AllowEntityReturnAttribute : Attribute
{
    public string Reason { get; }

    public AllowEntityReturnAttribute(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("AllowEntityReturn requires a non-empty justification.", nameof(reason));
        Reason = reason;
    }
}
