using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Application.Common;
using Zayra.Api.Domain.Entities;

namespace Zayra.Api.Infrastructure.Boot;

/// <summary>
/// Boot-time guard: no controller action may return a raw EF entity type (directly or as a
/// generic argument) unless it explicitly opts out with <see cref="AllowEntityReturnAttribute"/>.
///
/// WHY: a raw entity serializes TenantId, IsDeleted, and any new field added to the entity
/// model — including sensitive ones — without any masking gate. The fix is always a projected DTO
/// (see EmployeeDetailDto.Project()). Opt-out is only acceptable for entities with NO sensitive
/// PII (device config, sync logs, acknowledgement records).
///
/// ResolveActionType fully unwraps nested generics in any order:
///   Task&lt;T&gt;, ValueTask&lt;T&gt;, ActionResult&lt;T&gt; → recurse on T
///   IEnumerable&lt;T&gt;, IReadOnlyCollection&lt;T&gt;, IReadOnlyList&lt;T&gt;,
///   ICollection&lt;T&gt;, IList&lt;T&gt;, List&lt;T&gt;, IAsyncEnumerable&lt;T&gt; → recurse on T
///   T[] (arrays) → recurse on element type
///   IActionResult, ActionResult (non-generic), void, Task → null (skip)
///   Any remaining concrete type → returned for entity check
///   One level of DTO members is also checked to catch wrapper DTOs containing entity fields.
/// </summary>
public static class ControllerEntityReturnBootAssertion
{
    public static void Assert(DbContext db, Assembly controllerAssembly)
    {
        // Build the set of EF-mapped CLR types once
        var entityTypes = db.Model.GetEntityTypes()
            .Select(e => e.ClrType)
            .ToHashSet();

        var httpMethodAttributes = new[]
        {
            typeof(HttpGetAttribute), typeof(HttpPostAttribute),
            typeof(HttpPutAttribute), typeof(HttpPatchAttribute), typeof(HttpDeleteAttribute)
        };

        var violations = new List<string>();

        var controllerTypes = controllerAssembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

        foreach (var controller in controllerTypes)
        {
            foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                // Only check action methods (those decorated with HTTP verb attributes)
                bool isAction = method.GetCustomAttributes()
                    .Any(a => httpMethodAttributes.Any(h => h.IsInstanceOfType(a)));
                if (!isAction) continue;

                // Explicit opt-out
                if (method.GetCustomAttribute<AllowEntityReturnAttribute>() is not null) continue;
                if (controller.GetCustomAttribute<AllowEntityReturnAttribute>() is not null) continue;

                var resolved = ResolveActionType(method.ReturnType);
                if (resolved is null) continue;

                if (IsEntityOrContainsEntity(resolved, entityTypes, out var entityFound))
                {
                    violations.Add(
                        $"  {controller.Name}.{method.Name}: returns or wraps " +
                        $"{entityFound!.Name} which is an EF-mapped entity. " +
                        $"Project to a DTO, or add [AllowEntityReturn(\"reason\")] " +
                        $"if this entity has no sensitive fields.");
                }
            }
        }

        if (violations.Count > 0)
            throw new InvalidOperationException(
                $"Controller entity-return boot assertion failed — {violations.Count} action(s) return raw EF entities:\n" +
                string.Join("\n", violations) +
                "\n\nFix: project to a DTO (see EmployeeDetailDto.Project()), or add [AllowEntityReturn(\"reason\")] for entities without sensitive PII.");
    }

    // Transparent generic wrappers that should be peeled during type resolution.
    // All of these carry a single T argument that is the actual payload type.
    private static readonly HashSet<Type> TransparentWrappers = new()
    {
        typeof(Task<>), typeof(ValueTask<>), typeof(ActionResult<>),
        typeof(IEnumerable<>), typeof(IReadOnlyCollection<>), typeof(IReadOnlyList<>),
        typeof(ICollection<>), typeof(IList<>), typeof(List<>), typeof(IAsyncEnumerable<>)
    };

    /// <summary>
    /// Recursively unwraps transparent generic wrappers (Task, ValueTask, ActionResult,
    /// collection interfaces/classes, arrays) until a concrete leaf type is reached.
    /// Returns null for void, Task, IActionResult, ActionResult, and unknown interfaces.
    /// </summary>
    private static Type? ResolveActionType(Type type)
    {
        // Hard stops — no entity to check
        if (type == typeof(void) || type == typeof(Task) ||
            type == typeof(IActionResult) || type == typeof(ActionResult))
            return null;

        // Arrays — unwrap element type and recurse
        if (type.IsArray)
            return ResolveActionType(type.GetElementType()!);

        if (!type.IsGenericType)
            // Non-generic concrete type — return for entity check; unknown interfaces → skip
            return type.IsInterface ? null : type;

        var def = type.GetGenericTypeDefinition();
        var arg = type.GetGenericArguments()[0];

        // All transparent single-argument wrappers — peel one layer and recurse
        if (TransparentWrappers.Contains(def))
            return ResolveActionType(arg);

        // Other generic types (PagedResult<T>, custom DTOs) — return as-is;
        // IsEntityOrContainsEntity will inspect their generic args and public properties
        return type;
    }

    private static bool IsEntityOrContainsEntity(Type type, HashSet<Type> entityTypes, out Type? found)
    {
        // Direct entity type
        if (entityTypes.Contains(type)) { found = type; return true; }

        // IReadOnlyCollection<T>, List<T>, IEnumerable<T>
        if (type.IsGenericType)
        {
            foreach (var arg in type.GetGenericArguments())
            {
                if (IsEntityOrContainsEntity(arg, entityTypes, out found)) return true;
            }
        }

        // Skip primitives, system types, and simple value types
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(object)
            || type.Namespace?.StartsWith("System") == true
            || type.Namespace?.StartsWith("Microsoft") == true)
        {
            found = null;
            return false;
        }

        // One-level DTO member inspection: if a DTO property is an entity type, flag it
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var propType = prop.PropertyType;
            // Unwrap nullable
            propType = Nullable.GetUnderlyingType(propType) ?? propType;
            if (entityTypes.Contains(propType)) { found = propType; return true; }
        }

        found = null;
        return false;
    }
}
