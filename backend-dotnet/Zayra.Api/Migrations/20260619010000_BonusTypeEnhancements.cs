using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zayra.Api.Migrations
{
    /// <inheritdoc />
    public partial class BonusTypeEnhancements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── BonusTypes: eligibility, compliance flags, region-aware tax ────────
            migrationBuilder.AddColumn<decimal>(
                name: "DefaultCalculationValue",
                table: "BonusTypes",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Frequency",
                table: "BonusTypes",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "OneTime");

            migrationBuilder.AddColumn<int>(
                name: "MinServiceMonths",
                table: "BonusTypes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ProRataEligibility",
                table: "BonusTypes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresApproval",
                table: "BonusTypes",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsIncludedInEosb",
                table: "BonusTypes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsIncludedInGosiBase",
                table: "BonusTypes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsIncludedInWps",
                table: "BonusTypes",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxRegion",
                table: "BonusTypes",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "GCC");

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRate",
                table: "BonusTypes",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "BonusTypes",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "BonusTypes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedBy",
                table: "BonusTypes",
                type: "uniqueidentifier",
                nullable: true);

            // ── EmployeeBonuses: gross/tax split + region tag ─────────────────────
            migrationBuilder.AddColumn<decimal>(
                name: "GrossBonusAmount",
                table: "EmployeeBonuses",
                type: "decimal(18,2)",
                nullable: false,
                defaultValueSql: "[BonusAmount]"); // backfill from existing net amount

            migrationBuilder.AddColumn<decimal>(
                name: "TaxWithheld",
                table: "EmployeeBonuses",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "TaxRegion",
                table: "EmployeeBonuses",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "GCC");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DefaultCalculationValue", table: "BonusTypes");
            migrationBuilder.DropColumn(name: "Frequency",           table: "BonusTypes");
            migrationBuilder.DropColumn(name: "MinServiceMonths",    table: "BonusTypes");
            migrationBuilder.DropColumn(name: "ProRataEligibility",  table: "BonusTypes");
            migrationBuilder.DropColumn(name: "RequiresApproval",    table: "BonusTypes");
            migrationBuilder.DropColumn(name: "IsIncludedInEosb",    table: "BonusTypes");
            migrationBuilder.DropColumn(name: "IsIncludedInGosiBase",table: "BonusTypes");
            migrationBuilder.DropColumn(name: "IsIncludedInWps",     table: "BonusTypes");
            migrationBuilder.DropColumn(name: "TaxRegion",           table: "BonusTypes");
            migrationBuilder.DropColumn(name: "TaxRate",             table: "BonusTypes");
            migrationBuilder.DropColumn(name: "Notes",               table: "BonusTypes");
            migrationBuilder.DropColumn(name: "UpdatedAtUtc",        table: "BonusTypes");
            migrationBuilder.DropColumn(name: "UpdatedBy",           table: "BonusTypes");
            migrationBuilder.DropColumn(name: "GrossBonusAmount",    table: "EmployeeBonuses");
            migrationBuilder.DropColumn(name: "TaxWithheld",         table: "EmployeeBonuses");
            migrationBuilder.DropColumn(name: "TaxRegion",           table: "EmployeeBonuses");
        }
    }
}
