namespace Opstrax.Api.Controllers;

// Transitional DB V2 read routing.
// Existing POST/PUT handlers still write module_records during this slice;
// the database compatibility trigger mirrors only these four governed CRM keys
// into canonical relational tables. Reads come from security-invoker views so
// the application contract is preserved without making module_records authoritative.
public static partial class EndpointMappings
{
    static EndpointMappings()
    {
        ModuleDefinitions["leads"] = new("dbv2_leads_module_records");
        ModuleDefinitions["opportunities"] = new("dbv2_opportunities_module_records");
        ModuleDefinitions["campaigns"] = new("dbv2_campaigns_module_records");
        ModuleDefinitions["quotations"] = new("dbv2_quotations_module_records");
    }
}
