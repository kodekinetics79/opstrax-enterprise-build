using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zayra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddQiwaSyncLogTenantStatusIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Narrow the status column from longtext to varchar(20) so MySQL can index it.
            // Longest value in QiwaSyncLogStatuses is "DeadLetter" (10 chars); 20 is safe.
            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "QiwaSyncLogs",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_QiwaSyncLogs_tenant_id_status",
                table: "QiwaSyncLogs",
                columns: new[] { "tenant_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QiwaSyncLogs_tenant_id_status",
                table: "QiwaSyncLogs");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "QiwaSyncLogs",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldMaxLength: 20)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}
