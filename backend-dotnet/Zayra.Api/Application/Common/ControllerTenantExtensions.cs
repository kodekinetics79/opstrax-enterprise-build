using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace Zayra.Api.Application.Common;

public static class ControllerTenantExtensions
{
    public static Guid? GetTenantId(this ControllerBase controller)
    {
        var value = controller.User.FindFirstValue("tenant_id");
        return Guid.TryParse(value, out var id) ? id : null;
    }

    public static Guid? GetUserId(this ControllerBase controller)
    {
        var value = controller.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? controller.User.FindFirstValue("sub");
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
