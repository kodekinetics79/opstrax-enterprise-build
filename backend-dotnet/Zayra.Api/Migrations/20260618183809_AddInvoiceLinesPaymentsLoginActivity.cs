using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zayra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceLinesPaymentsLoginActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_gosi_contribution_rules_tenant_classification_branch_payer",
                table: "gosi_contribution_rules",
                newName: "IX_gosi_contribution_rules_tenant_id_classification_branch_paye~");

            migrationBuilder.AlterColumn<bool>(
                name: "is_active",
                table: "gosi_contribution_rules",
                type: "tinyint(1)",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)",
                oldDefaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "h_r_business_partner_employee_id",
                table: "employees",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "supervisor_employee_id",
                table: "employees",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_system_default",
                table: "designations",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "level_rank",
                table: "designations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "sort_order",
                table: "departments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "approver_type",
                table: "approval_workflow_steps",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "escalation_after_hours",
                table: "approval_workflow_steps",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "specific_employee_id",
                table: "approval_workflow_steps",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "approval_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    workflow_type = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    department_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    grade_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    is_default = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    updated_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    is_deleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_policies", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "login_activity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    user_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    email_attempted = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    event_type = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    failure_reason = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ip_address = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    user_agent = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    occurred_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_login_activity", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "reporting_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    employee_id = table.Column<int>(type: "int", nullable: false),
                    manager_employee_id = table.Column<int>(type: "int", nullable: false),
                    relationship_type = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    effective_from = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    effective_to = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    is_primary = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    updated_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reporting_lines", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tenant_hr_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    use_dept_head_approval = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    use_hr_final_approval = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    use_supervisor_before_manager = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    allow_dotted_line_approval = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    auto_create_dept_on_import = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    auto_create_designation_on_import = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    require_import_preview_before_commit = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    allow_cross_dept_manager = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    allow_cross_location_manager = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    require_cost_center_for_payroll = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    require_grade_for_approval_policy = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_hr_configs", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tenant_invoice_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    invoice_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    description = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    quantity = table.Column<int>(type: "int", nullable: false),
                    unit_price = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    discount_amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    tax_rate = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    tax_amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    line_total = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_invoice_lines", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tenant_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    invoice_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    currency_code = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    method = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reference = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    paid_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    received_by_platform_user_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    notes = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_payments", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "approval_policy_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    policy_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    step_order = table.Column<int>(type: "int", nullable: false),
                    step_name = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    approver_type = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    specific_employee_id = table.Column<int>(type: "int", nullable: true),
                    approver_role = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    escalation_after_hours = table.Column<int>(type: "int", nullable: true),
                    is_final_step = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_policy_steps", x => x.id);
                    table.ForeignKey(
                        name: "FK_approval_policy_steps_approval_policies_policy_id",
                        column: x => x.policy_id,
                        principalTable: "approval_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_approval_policies_tenant_id_workflow_type_department_id_grad~",
                table: "approval_policies",
                columns: new[] { "tenant_id", "workflow_type", "department_id", "grade_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_approval_policies_tenant_id_workflow_type_is_default_is_acti~",
                table: "approval_policies",
                columns: new[] { "tenant_id", "workflow_type", "is_default", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_approval_policy_steps_policy_id",
                table: "approval_policy_steps",
                column: "policy_id");

            migrationBuilder.CreateIndex(
                name: "IX_approval_policy_steps_tenant_id_policy_id_step_order",
                table: "approval_policy_steps",
                columns: new[] { "tenant_id", "policy_id", "step_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_login_activity_occurred_at_utc",
                table: "login_activity",
                column: "occurred_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_login_activity_tenant_id",
                table: "login_activity",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_login_activity_tenant_id_event_type_occurred_at_utc",
                table: "login_activity",
                columns: new[] { "tenant_id", "event_type", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_login_activity_user_id",
                table: "login_activity",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_reporting_lines_tenant_id_employee_id_relationship_type_is_a~",
                table: "reporting_lines",
                columns: new[] { "tenant_id", "employee_id", "relationship_type", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_reporting_lines_tenant_id_manager_employee_id_is_active",
                table: "reporting_lines",
                columns: new[] { "tenant_id", "manager_employee_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_tenant_hr_configs_tenant_id",
                table: "tenant_hr_configs",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenant_invoice_lines_invoice_id_sort_order",
                table: "tenant_invoice_lines",
                columns: new[] { "invoice_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_tenant_invoice_lines_tenant_id",
                table: "tenant_invoice_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_payments_invoice_id",
                table: "tenant_payments",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_payments_tenant_id",
                table: "tenant_payments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_payments_tenant_id_status",
                table: "tenant_payments",
                columns: new[] { "tenant_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "approval_policy_steps");

            migrationBuilder.DropTable(
                name: "login_activity");

            migrationBuilder.DropTable(
                name: "reporting_lines");

            migrationBuilder.DropTable(
                name: "tenant_hr_configs");

            migrationBuilder.DropTable(
                name: "tenant_invoice_lines");

            migrationBuilder.DropTable(
                name: "tenant_payments");

            migrationBuilder.DropTable(
                name: "approval_policies");

            migrationBuilder.DropColumn(
                name: "h_r_business_partner_employee_id",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "supervisor_employee_id",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "is_system_default",
                table: "designations");

            migrationBuilder.DropColumn(
                name: "level_rank",
                table: "designations");

            migrationBuilder.DropColumn(
                name: "sort_order",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "approver_type",
                table: "approval_workflow_steps");

            migrationBuilder.DropColumn(
                name: "escalation_after_hours",
                table: "approval_workflow_steps");

            migrationBuilder.DropColumn(
                name: "specific_employee_id",
                table: "approval_workflow_steps");

            migrationBuilder.RenameIndex(
                name: "IX_gosi_contribution_rules_tenant_id_classification_branch_paye~",
                table: "gosi_contribution_rules",
                newName: "IX_gosi_contribution_rules_tenant_classification_branch_payer");

            migrationBuilder.AlterColumn<bool>(
                name: "is_active",
                table: "gosi_contribution_rules",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "tinyint(1)");
        }
    }
}
