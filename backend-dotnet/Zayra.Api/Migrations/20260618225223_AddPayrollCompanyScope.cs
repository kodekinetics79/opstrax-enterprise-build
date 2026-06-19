using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zayra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollCompanyScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payroll_runs_tenant_id_year_month",
                table: "payroll_runs");

            migrationBuilder.AddColumn<int>(
                name: "max_admin_users",
                table: "tenant_subscriptions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "max_companies",
                table: "tenant_subscriptions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                table: "salary_structures",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "payroll_runs",
                type: "varchar(255)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                table: "payroll_runs",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateTable(
                name: "pricing_config",
                columns: table => new
                {
                    key = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    label = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    group = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    plan = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    value = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pricing_config", x => x.key);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "pricing_module_configs",
                columns: table => new
                {
                    module_key = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    module_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    included_in_trial = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    included_in_starter = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    included_in_growth = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    included_in_enterprise = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    is_enterprise_only = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    addon_price_monthly = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pricing_module_configs", x => x.module_key);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "pricing_quotes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    company_name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    contact_name = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    contact_email = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    phone = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    org_type = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    num_companies = table.Column<int>(type: "int", nullable: false),
                    num_branches = table.Column<int>(type: "int", nullable: false),
                    num_employees = table.Column<int>(type: "int", nullable: false),
                    num_admin_users = table.Column<int>(type: "int", nullable: false),
                    num_countries = table.Column<int>(type: "int", nullable: false),
                    needs_arabic = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    selected_modules_json = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    estimated_monthly_amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    estimated_annual_amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    notes = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    converted_to_tenant_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pricing_quotes", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_salary_structures_tenant_id_company_id",
                table: "salary_structures",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "IX_payroll_runs_tenant_id_company_id_status",
                table: "payroll_runs",
                columns: new[] { "tenant_id", "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_payroll_runs_tenant_id_company_id_year_month",
                table: "payroll_runs",
                columns: new[] { "tenant_id", "company_id", "year", "month" });

            migrationBuilder.CreateIndex(
                name: "IX_pricing_quotes_created_at_utc",
                table: "pricing_quotes",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_pricing_quotes_status",
                table: "pricing_quotes",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pricing_config");

            migrationBuilder.DropTable(
                name: "pricing_module_configs");

            migrationBuilder.DropTable(
                name: "pricing_quotes");

            migrationBuilder.DropIndex(
                name: "IX_salary_structures_tenant_id_company_id",
                table: "salary_structures");

            migrationBuilder.DropIndex(
                name: "IX_payroll_runs_tenant_id_company_id_status",
                table: "payroll_runs");

            migrationBuilder.DropIndex(
                name: "IX_payroll_runs_tenant_id_company_id_year_month",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "max_admin_users",
                table: "tenant_subscriptions");

            migrationBuilder.DropColumn(
                name: "max_companies",
                table: "tenant_subscriptions");

            migrationBuilder.DropColumn(
                name: "company_id",
                table: "salary_structures");

            migrationBuilder.DropColumn(
                name: "company_id",
                table: "payroll_runs");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "payroll_runs",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(255)")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_payroll_runs_tenant_id_year_month",
                table: "payroll_runs",
                columns: new[] { "tenant_id", "year", "month" },
                unique: true);
        }
    }
}
