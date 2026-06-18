using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zayra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHrmHierarchyAndApprovalPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── employees: new hierarchy fields ──────────────────────────────
            migrationBuilder.AddColumn<int>(
                name: "SupervisorEmployeeId",
                table: "employees",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HRBusinessPartnerEmployeeId",
                table: "employees",
                type: "int",
                nullable: true);

            // ── departments: sort order ───────────────────────────────────────
            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "departments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // ── designations: hierarchy metadata ─────────────────────────────
            migrationBuilder.AddColumn<bool>(
                name: "IsSystemDefault",
                table: "designations",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LevelRank",
                table: "designations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // ── approval_workflow_steps: hierarchy-aware routing ─────────────
            migrationBuilder.AddColumn<string>(
                name: "ApproverType",
                table: "approval_workflow_steps",
                type: "varchar(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "Role")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "SpecificEmployeeId",
                table: "approval_workflow_steps",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EscalationAfterHours",
                table: "approval_workflow_steps",
                type: "int",
                nullable: true);

            // ── reporting_lines table ─────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "reporting_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    ManagerEmployeeId = table.Column<int>(type: "int", nullable: false),
                    RelationshipType = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    IsPrimary = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reporting_lines", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_reporting_lines_TenantId_EmployeeId_RelationshipType_IsActive",
                table: "reporting_lines",
                columns: new[] { "TenantId", "EmployeeId", "RelationshipType", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_reporting_lines_TenantId_ManagerEmployeeId_IsActive",
                table: "reporting_lines",
                columns: new[] { "TenantId", "ManagerEmployeeId", "IsActive" });

            // ── approval_policies table ───────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "approval_policies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    WorkflowType = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DepartmentId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    GradeId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    IsDefault = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_policies", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_approval_policies_TenantId_WorkflowType_IsDefault_IsActive",
                table: "approval_policies",
                columns: new[] { "TenantId", "WorkflowType", "IsDefault", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_approval_policies_TenantId_WorkflowType_DepartmentId_GradeId",
                table: "approval_policies",
                columns: new[] { "TenantId", "WorkflowType", "DepartmentId", "GradeId" },
                unique: true,
                filter: "is_deleted = 0");

            // ── approval_policy_steps table ───────────────────────────────────
            migrationBuilder.CreateTable(
                name: "approval_policy_steps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PolicyId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    StepOrder = table.Column<int>(type: "int", nullable: false),
                    StepName = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ApproverType = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SpecificEmployeeId = table.Column<int>(type: "int", nullable: true),
                    ApproverRole = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EscalationAfterHours = table.Column<int>(type: "int", nullable: true),
                    IsFinalStep = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_policy_steps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_approval_policy_steps_approval_policies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "approval_policies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_approval_policy_steps_TenantId_PolicyId_StepOrder",
                table: "approval_policy_steps",
                columns: new[] { "TenantId", "PolicyId", "StepOrder" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "approval_policy_steps");
            migrationBuilder.DropTable(name: "approval_policies");
            migrationBuilder.DropTable(name: "reporting_lines");

            migrationBuilder.DropColumn(name: "SupervisorEmployeeId",      table: "employees");
            migrationBuilder.DropColumn(name: "HRBusinessPartnerEmployeeId", table: "employees");
            migrationBuilder.DropColumn(name: "SortOrder",                  table: "departments");
            migrationBuilder.DropColumn(name: "IsSystemDefault",             table: "designations");
            migrationBuilder.DropColumn(name: "LevelRank",                  table: "designations");
            migrationBuilder.DropColumn(name: "ApproverType",               table: "approval_workflow_steps");
            migrationBuilder.DropColumn(name: "SpecificEmployeeId",         table: "approval_workflow_steps");
            migrationBuilder.DropColumn(name: "EscalationAfterHours",       table: "approval_workflow_steps");
        }
    }
}
