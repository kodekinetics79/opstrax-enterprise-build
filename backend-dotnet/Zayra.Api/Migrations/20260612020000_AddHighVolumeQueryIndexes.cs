using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zayra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHighVolumeQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // audit_logs: entity audit trail lookup
            // Covers: WHERE tenant_id = ? AND entity_name = ? AND entity_id = ? ORDER BY created_at_utc
            // Without this, entity-specific audit queries do a full per-tenant table scan.
            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_tenant_id_entity_name_entity_id_created_at_utc",
                table: "audit_logs",
                columns: new[] { "tenant_id", "entity_name", "entity_id", "created_at_utc" });

            // employee_notifications: paginated inbox ordered by arrival time
            // The existing (tenant_id, employee_id, is_read) index cannot satisfy ORDER BY created_at_utc.
            migrationBuilder.CreateIndex(
                name: "IX_employee_notifications_tenant_id_employee_id_created_at_utc",
                table: "employee_notifications",
                columns: new[] { "tenant_id", "employee_id", "created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_audit_logs_tenant_id_entity_name_entity_id_created_at_utc",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "IX_employee_notifications_tenant_id_employee_id_created_at_utc",
                table: "employee_notifications");
        }
    }
}
