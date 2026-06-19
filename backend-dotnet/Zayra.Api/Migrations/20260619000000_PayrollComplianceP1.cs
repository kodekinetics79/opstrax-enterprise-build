using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zayra.Api.Migrations
{
    /// <inheritdoc />
    public partial class PayrollComplianceP1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── EmployeePayrollProfiles: MOL ID + Bank Routing Code ──────────────────
            migrationBuilder.AddColumn<string>(
                name: "MolId",
                table: "EmployeePayrollProfiles",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BankRoutingCode",
                table: "EmployeePayrollProfiles",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            // ── EmployeeLoans: int bridge FK to Employee.Id ──────────────────────────
            migrationBuilder.AddColumn<int>(
                name: "EmployeeIntId",
                table: "EmployeeLoans",
                type: "int",
                nullable: true);

            // ── SalaryAdvances: int bridge FK to Employee.Id ─────────────────────────
            migrationBuilder.AddColumn<int>(
                name: "EmployeeIntId",
                table: "SalaryAdvances",
                type: "int",
                nullable: true);

            // ── PayrollSlips: YTD + LoanDeductions ──────────────────────────────────
            migrationBuilder.AddColumn<decimal>(
                name: "YtdGross",
                table: "PayrollSlips",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "YtdDeductions",
                table: "PayrollSlips",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "YtdNet",
                table: "PayrollSlips",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LoanDeductions",
                table: "PayrollSlips",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            // ── SIFFileRecords: MolId + RoutingCode ─────────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "MolId",
                table: "SIFFileRecords",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RoutingCode",
                table: "SIFFileRecords",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "MolId",           table: "EmployeePayrollProfiles");
            migrationBuilder.DropColumn(name: "BankRoutingCode", table: "EmployeePayrollProfiles");
            migrationBuilder.DropColumn(name: "EmployeeIntId",   table: "EmployeeLoans");
            migrationBuilder.DropColumn(name: "EmployeeIntId",   table: "SalaryAdvances");
            migrationBuilder.DropColumn(name: "YtdGross",        table: "PayrollSlips");
            migrationBuilder.DropColumn(name: "YtdDeductions",   table: "PayrollSlips");
            migrationBuilder.DropColumn(name: "YtdNet",          table: "PayrollSlips");
            migrationBuilder.DropColumn(name: "LoanDeductions",  table: "PayrollSlips");
            migrationBuilder.DropColumn(name: "MolId",           table: "SIFFileRecords");
            migrationBuilder.DropColumn(name: "RoutingCode",     table: "SIFFileRecords");
        }
    }
}
