using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zayra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformComplianceAndConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "mol_id",
                table: "sif_file_records",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "routing_code",
                table: "sif_file_records",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "employee_int_id",
                table: "salary_advances",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "loan_deductions",
                table: "payroll_slips",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ytd_deductions",
                table: "payroll_slips",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ytd_gross",
                table: "payroll_slips",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ytd_net",
                table: "payroll_slips",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "bank_routing_code",
                table: "employee_payroll_profiles",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "mol_id",
                table: "employee_payroll_profiles",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "employee_int_id",
                table: "employee_loans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "baseline_value",
                table: "employee_goals",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "is_deleted",
                table: "employee_goals",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "priority",
                table: "employee_goals",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateOnly>(
                name: "start_date",
                table: "employee_goals",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "employee_int_id",
                table: "employee_bonuses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "gross_bonus_amount",
                table: "employee_bonuses",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "tax_region",
                table: "employee_bonuses",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "tax_withheld",
                table: "employee_bonuses",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "jurisdiction",
                table: "companies",
                type: "varchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "default_calculation_value",
                table: "bonus_types",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "frequency",
                table: "bonus_types",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "is_included_in_eosb",
                table: "bonus_types",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_included_in_gosi_base",
                table: "bonus_types",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_included_in_wps",
                table: "bonus_types",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "min_service_months",
                table: "bonus_types",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                table: "bonus_types",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "pro_rata_eligibility",
                table: "bonus_types",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "requires_approval",
                table: "bonus_types",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "tax_rate",
                table: "bonus_types",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "tax_region",
                table: "bonus_types",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at_utc",
                table: "bonus_types",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "updated_by",
                table: "bonus_types",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateTable(
                name: "platform_compliance_controls",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    category = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    control_id = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    title = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    owner = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    evidence_note = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    evidence_url = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reviewed_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    reviewed_by_platform_user_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_compliance_controls", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "platform_config_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    key = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    value = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_by_platform_user_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_config_entries", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "platform_security_incidents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    title = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    severity = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reporter = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    affected_systems = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    occurred_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    resolved_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    resolution = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    created_by_platform_user_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_security_incidents", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "statutory_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    country_code = table.Column<string>(type: "varchar(5)", maxLength: 5, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    jurisdiction = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    rule_key = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    rule_value = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    data_type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    effective_from = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    effective_to = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statutory_rules", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_platform_compliance_controls_category_control_id",
                table: "platform_compliance_controls",
                columns: new[] { "category", "control_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_platform_config_entries_key",
                table: "platform_config_entries",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_platform_security_incidents_status_severity",
                table: "platform_security_incidents",
                columns: new[] { "status", "severity" });

            migrationBuilder.CreateIndex(
                name: "IX_statutory_rules_tenant_id_country_code_jurisdiction_rule_key~",
                table: "statutory_rules",
                columns: new[] { "tenant_id", "country_code", "jurisdiction", "rule_key", "effective_from" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_compliance_controls");

            migrationBuilder.DropTable(
                name: "platform_config_entries");

            migrationBuilder.DropTable(
                name: "platform_security_incidents");

            migrationBuilder.DropTable(
                name: "statutory_rules");

            migrationBuilder.DropColumn(
                name: "mol_id",
                table: "sif_file_records");

            migrationBuilder.DropColumn(
                name: "routing_code",
                table: "sif_file_records");

            migrationBuilder.DropColumn(
                name: "employee_int_id",
                table: "salary_advances");

            migrationBuilder.DropColumn(
                name: "loan_deductions",
                table: "payroll_slips");

            migrationBuilder.DropColumn(
                name: "ytd_deductions",
                table: "payroll_slips");

            migrationBuilder.DropColumn(
                name: "ytd_gross",
                table: "payroll_slips");

            migrationBuilder.DropColumn(
                name: "ytd_net",
                table: "payroll_slips");

            migrationBuilder.DropColumn(
                name: "bank_routing_code",
                table: "employee_payroll_profiles");

            migrationBuilder.DropColumn(
                name: "mol_id",
                table: "employee_payroll_profiles");

            migrationBuilder.DropColumn(
                name: "employee_int_id",
                table: "employee_loans");

            migrationBuilder.DropColumn(
                name: "baseline_value",
                table: "employee_goals");

            migrationBuilder.DropColumn(
                name: "is_deleted",
                table: "employee_goals");

            migrationBuilder.DropColumn(
                name: "priority",
                table: "employee_goals");

            migrationBuilder.DropColumn(
                name: "start_date",
                table: "employee_goals");

            migrationBuilder.DropColumn(
                name: "employee_int_id",
                table: "employee_bonuses");

            migrationBuilder.DropColumn(
                name: "gross_bonus_amount",
                table: "employee_bonuses");

            migrationBuilder.DropColumn(
                name: "tax_region",
                table: "employee_bonuses");

            migrationBuilder.DropColumn(
                name: "tax_withheld",
                table: "employee_bonuses");

            migrationBuilder.DropColumn(
                name: "jurisdiction",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "default_calculation_value",
                table: "bonus_types");

            migrationBuilder.DropColumn(
                name: "frequency",
                table: "bonus_types");

            migrationBuilder.DropColumn(
                name: "is_included_in_eosb",
                table: "bonus_types");

            migrationBuilder.DropColumn(
                name: "is_included_in_gosi_base",
                table: "bonus_types");

            migrationBuilder.DropColumn(
                name: "is_included_in_wps",
                table: "bonus_types");

            migrationBuilder.DropColumn(
                name: "min_service_months",
                table: "bonus_types");

            migrationBuilder.DropColumn(
                name: "notes",
                table: "bonus_types");

            migrationBuilder.DropColumn(
                name: "pro_rata_eligibility",
                table: "bonus_types");

            migrationBuilder.DropColumn(
                name: "requires_approval",
                table: "bonus_types");

            migrationBuilder.DropColumn(
                name: "tax_rate",
                table: "bonus_types");

            migrationBuilder.DropColumn(
                name: "tax_region",
                table: "bonus_types");

            migrationBuilder.DropColumn(
                name: "updated_at_utc",
                table: "bonus_types");

            migrationBuilder.DropColumn(
                name: "updated_by",
                table: "bonus_types");
        }
    }
}
