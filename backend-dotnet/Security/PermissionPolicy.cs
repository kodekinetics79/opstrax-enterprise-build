namespace Opstrax.Api.Security;

/// <summary>
/// Canonical, directed tenant-permission policy. Held grants may imply narrower
/// capabilities; individual actions never become sibling actions or manage grants.
/// </summary>
public static class PermissionPolicy
{
    private static readonly Dictionary<string, HashSet<string>> Implications = BuildImplications();

    public static bool Allows(IReadOnlyCollection<string> heldPermissions, string requiredPermission)
    {
        if (heldPermissions.Count == 0) return false;
        if (heldPermissions.Any(static permission => permission.Trim() == "*")) return true;

        var required = Canonicalize(requiredPermission);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>(heldPermissions.Select(Canonicalize));
        while (pending.TryDequeue(out var held))
        {
            if (!visited.Add(held)) continue;
            if (string.Equals(held, required, StringComparison.OrdinalIgnoreCase)) return true;
            if (!Implications.TryGetValue(held, out var implied)) continue;
            foreach (var permission in implied) pending.Enqueue(permission);
        }
        return false;
    }

    public static string Canonicalize(string permission)
    {
        var canonical = permission.Trim().ToLowerInvariant().Replace('.', ':');
        return canonical switch
        {
            "customer-portal:view" => "customer_portal:view",
            "customer-portal:manage" => "customer_portal:manage",
            "telemetry:live-state:read" => "telemetry:live_state:read",
            "finance:job:ready-to-bill" => "finance:job:ready_to_bill",
            "rate-card:read" => "rate_card:read",
            "rate-card:create" => "rate_card:create",
            "rate-card:update" => "rate_card:update",
            "rate-card:manage" => "rate_card:manage",
            _ => canonical,
        };
    }

    private static Dictionary<string, HashSet<string>> BuildImplications()
    {
        var graph = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        void Add(string held, params string[] implied)
        {
            var source = Canonicalize(held);
            if (!graph.TryGetValue(source, out var targets)) graph[source] = targets = new(StringComparer.OrdinalIgnoreCase);
            foreach (var target in implied.Select(Canonicalize))
                if (!string.Equals(source, target, StringComparison.OrdinalIgnoreCase)) targets.Add(target);
        }
        void Manage(string held, string view, params string[] actions) => Add(held, [view, .. actions]);

        // Coarse and legacy read grants flow toward concrete reads only.
        Add("fleet:view", "vehicles:view", "drivers:view", "fleet:read");
        Add("orders:view", "shipments:view");
        Add("crm:view", "customers:view");
        Add("customers:view", "customer.account.read", "customer.contact.read", "customer.address.read");
        Add("crm:view", "customer.account.read", "customer.contact.read", "customer.address.read");
        Add("map:view", "telemetry.live_state.read", "telematics:gps:view");
        Add("telematics:gps:view", "telemetry.live_state.read");
        Add("telematics:devices:view", "telemetry.devices.read");
        Add("alerts:view", "telemetry.alerts.read");
        Add("safety:view", "telemetry.alerts.read");
        Add("maintenance:view", "telemetry.alerts.read");
        Add("reports:view", "telemetry.recommendations.read");
        Add("billing:view", "finance:view");
        Add("finance:view", "contract:read", "rate_card:read", "charge:read", "tax:read", "settlement:read", "revrec:read", "finance.invoice:read", "finance.invoice_draft:read");
        Add("dispatch:view", "job:read", "trip:read", "operations.execution_summary.read");
        Add("shipments:view", "job:read", "operations.execution_summary.read", "fleet.shipments.view");
        Add("fleet:view", "operations.execution_summary.read");
        Add("driver:self", "operations.execution_summary.read");
        Add("carriers:view", "fleet.carriers.view");
        Add("carriers:manage", "fleet.carriers.view", "fleet.carriers.manage");
        Add("fuel:view", "fleet.fuel.view");
        Add("fuel:manage", "fleet.fuel.view", "fleet.fuel.manage");

        // Approved manage grants imply their own view/action tiers, one way.
        Manage("fleet:manage", "fleet:view", "vehicles:create", "vehicles:update", "vehicles:delete", "vehicles:assign", "vehicles:export", "fleet.shipments.manage", "fleet.carriers.manage", "fleet.fuel.manage", "telemetry.devices.manage", "telemetry.rules.manage", "operations.proof:validate");
        Manage("drivers:manage", "drivers:view", "drivers:create", "drivers:update", "drivers:delete", "drivers:assign", "drivers:export");
        Manage("shipments:manage", "shipments:view", "shipments:create", "shipments:update", "shipments:delete", "shipments:export");
        Manage("orders:manage", "orders:view", "shipments:create", "shipments:update", "shipments:delete");
        Manage("dispatch:manage", "dispatch:view", "dispatch:create", "dispatch:update", "dispatch:assign", "dispatch:cancel", "job:create", "job:update", "trip:create", "trip:update");
        Manage("customers:manage", "customers:view", "customers:create", "customers:update", "customers:delete", "customer.account:create", "customer.account:update", "customer.account:delete");
        Manage("crm:manage", "crm:view", "customers:create", "customers:update", "customers:delete");
        Manage("safety:manage", "safety:view", "safety:create", "safety:update", "safety:review");
        Manage("maintenance:manage", "maintenance:view", "maintenance:create", "maintenance:update", "maintenance:close", "maintenance:review");
        Manage("compliance:manage", "compliance:view", "compliance:update", "compliance:export");
        Manage("alerts:manage", "alerts:view", "alerts:acknowledge", "alerts:close", "telemetry.alerts.manage");
        Manage("reports:manage", "reports:view", "reports:export");
        Manage("users:manage", "users:view", "users:create", "users:update", "users:delete");
        Manage("roles:manage", "roles:view", "roles:create", "roles:update");
        Manage("settings:manage", "settings:view", "settings:update");
        Manage("telemetry.devices.manage", "telemetry.devices.read", "telematics:devices:create", "telematics:devices:update", "telematics:devices:delete", "telematics:devices:assign", "telematics:providers:manage");
        Add("telematics:providers:manage", "telemetry.devices.manage");
        Manage("telemetry.alerts.manage", "telemetry.alerts.read", "alerts:acknowledge", "alerts:close");
        Manage("telemetry.rules.manage", "telemetry.rules.read");
        Add("devices:manage", "telemetry.rules.manage");
        Manage("finance:manage", "finance:view", "contract:create", "contract:update", "rate_card:create", "rate_card:update", "charge:create", "charge:update", "finance.job.ready_to_bill", "finance.invoice_draft:create", "finance.invoice_draft:update", "finance.invoice:issue", "finance.invoice:approve", "finance.invoice.payment:record", "settlement:create", "settlement:update", "settlement:approve", "settlement:pay", "tax:create", "tax:update", "tax:publish", "billing:create", "billing:update", "revrec:create", "revrec:update", "revrec.period:close", "finance.config:create", "finance.config:update", "finance.config:publish");
        Manage("billing:manage", "billing:view", "billing:create", "billing:update", "finance.job.ready_to_bill", "fleet.billing.manage");

        // Narrow workflow actions may imply read, never another mutation.
        Add("dispatch.smart_assign:recommend", "dispatch.smart_assign:read");
        Add("dispatch.smart_assign:accept", "dispatch.smart_assign:read");
        Add("dispatch.smart_assign:reject", "dispatch.smart_assign:read");
        Add("operations.site_access:create", "operations.site_access:read");
        Add("operations.site_access:update", "operations.site_access:read");
        Add("operations.access_document:create", "operations.access_document:read");
        Add("operations.access_document:update", "operations.access_document:read");
        Add("operations.access_document:verify", "operations.access_document:read");
        Add("operations.pickup_authorization:create", "operations.pickup_authorization:read");
        Add("operations.pickup_authorization:update", "operations.pickup_authorization:read");
        Add("operations.pickup_authorization:verify", "operations.pickup_authorization:read");
        Add("operations.warehouse_handover:create", "operations.warehouse_handover:read");
        Add("operations.warehouse_handover:update", "operations.warehouse_handover:read");
        Add("operations.proof:create", "operations.proof:read");
        Add("operations.proof:update", "operations.proof:read");
        Add("operations.proof:submit", "operations.proof:read");
        Add("operations.proof:validate", "operations.proof:read");
        Add("operations.proof_artifact:create", "operations.proof_artifact:read");
        return graph;
    }
}
