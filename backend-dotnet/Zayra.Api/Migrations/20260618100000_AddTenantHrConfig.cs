using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zayra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantHrConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_hr_configs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UseDeptHeadApproval = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    UseHrFinalApproval = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    UseSupervisorBeforeManager = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    AllowDottedLineApproval = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    AutoCreateDeptOnImport = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    AutoCreateDesignationOnImport = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    RequireImportPreviewBeforeCommit = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    AllowCrossDeptManager = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    AllowCrossLocationManager = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    RequireCostCenterForPayroll = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    RequireGradeForApprovalPolicy = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_hr_configs", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_hr_configs_TenantId",
                table: "tenant_hr_configs",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "tenant_hr_configs");
        }
    }
}
