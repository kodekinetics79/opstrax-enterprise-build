using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zayra.Api.Migrations
{
    /// <inheritdoc />
    public partial class FixApprovalRequestIndexLengths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // These columns were corrected to their final sizes in the original CreateTable
            // (EmployeeManagementLivePersistence) so these ALTER statements are no-ops on a
            // fresh database. They remain here for idempotent safety on databases that were
            // created before that correction was applied.
            migrationBuilder.AlterColumn<string>(
                name: "title",
                table: "approval_requests",
                type: "varchar(240)",
                maxLength: 240,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "approval_requests",
                type: "varchar(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(40)")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "entity_name",
                table: "approval_requests",
                type: "varchar(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(120)")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "entity_id",
                table: "approval_requests",
                type: "varchar(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(80)")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down() mirrors Up(): columns stay at their correct sizes to keep the composite
            // index (created in EmployeeManagementLivePersistence) valid at all times.
            migrationBuilder.AlterColumn<string>(
                name: "title",
                table: "approval_requests",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(240)",
                oldMaxLength: 240)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "approval_requests",
                type: "varchar(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(40)",
                oldMaxLength: 40)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "entity_name",
                table: "approval_requests",
                type: "varchar(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(120)",
                oldMaxLength: 120)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "entity_id",
                table: "approval_requests",
                type: "varchar(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(80)",
                oldMaxLength: 80)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}
