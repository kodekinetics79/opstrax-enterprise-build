using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zayra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantAiUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_ai_usage",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    year_month = table.Column<int>(type: "int", nullable: false),
                    tokens_used = table.Column<long>(type: "bigint", nullable: false),
                    request_count = table.Column<int>(type: "int", nullable: false),
                    blocked_count = table.Column<int>(type: "int", nullable: false),
                    last_updated_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_ai_usage", x => new { x.tenant_id, x.year_month });
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_ai_usage");
        }
    }
}
