using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zayra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCountryPackFramework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Add Jurisdiction column to companies ────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "Jurisdiction",
                table: "companies",
                type: "varchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            // Default all existing companies to KSA-mainland.
            // Also normalises any empty CountryCode to "SAU" so no row is
            // ambiguous after this migration.
            migrationBuilder.Sql(
                "UPDATE companies SET Jurisdiction = 'KSA-mainland' WHERE Jurisdiction = '' OR Jurisdiction IS NULL;");
            migrationBuilder.Sql(
                "UPDATE companies SET CountryCode = 'SAU' WHERE CountryCode = '' OR CountryCode IS NULL;");

            // ── 2. Create statutory_rules table ───────────────────────────────
            migrationBuilder.CreateTable(
                name: "statutory_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CountryCode = table.Column<string>(type: "varchar(5)", maxLength: 5, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Jurisdiction = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RuleKey = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RuleValue = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DataType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statutory_rules", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_statutory_rules_TenantId_CountryCode_Jurisdiction_RuleKey_EffectiveFrom",
                table: "statutory_rules",
                columns: new[] { "TenantId", "CountryCode", "Jurisdiction", "RuleKey", "EffectiveFrom" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "statutory_rules");

            migrationBuilder.DropColumn(name: "Jurisdiction", table: "companies");
        }
    }
}
