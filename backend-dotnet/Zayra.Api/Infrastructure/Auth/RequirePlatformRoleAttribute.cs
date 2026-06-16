using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Zayra.Api.Models;

namespace Zayra.Api.Infrastructure.Auth;

/// <summary>
/// Enforces platform_role claim RBAC on individual PlatformController actions.
/// The controller-level [Authorize(Policy = "PlatformAdmin")] gate still runs first;
/// this filter provides fine-grained role enforcement within the platform.
///
/// Owner is always allowed regardless of the allowed-roles list.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class RequirePlatformRoleAttribute : Attribute, IActionFilter
{
    private readonly HashSet<string> _allowedRoles;

    public RequirePlatformRoleAttribute(params string[] roles)
    {
        _allowedRoles = new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var role = context.HttpContext.User.FindFirst("platform_role")?.Value;

        // Owner is always allowed.
        if (string.Equals(role, PlatformRoles.Owner, StringComparison.OrdinalIgnoreCase))
            return;

        // Missing claim or role not in allowed list → 403.
        if (role is null || !_allowedRoles.Contains(role))
        {
            context.Result = new ObjectResult(new
            {
                code    = "forbidden",
                message = $"Your platform role '{role ?? "unknown"}' does not have permission to perform this action."
            })
            { StatusCode = StatusCodes.Status403Forbidden };
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
