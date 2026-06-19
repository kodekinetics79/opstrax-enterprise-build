using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zayra.Api.Migrations
{
    /// <inheritdoc />
    public partial class GoalEnhancements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Priority: High/Medium/Low
            migrationBuilder.AddColumn<string>(
                name: "Priority",
                table: "EmployeeGoals",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Medium");

            // StartDate: optional plan start date
            migrationBuilder.AddColumn<DateOnly>(
                name: "StartDate",
                table: "EmployeeGoals",
                type: "date",
                nullable: true);

            // BaselineValue: starting value before the goal period
            migrationBuilder.AddColumn<decimal>(
                name: "BaselineValue",
                table: "EmployeeGoals",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            // IsDeleted: soft-delete flag
            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "EmployeeGoals",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Priority",      table: "EmployeeGoals");
            migrationBuilder.DropColumn(name: "StartDate",     table: "EmployeeGoals");
            migrationBuilder.DropColumn(name: "BaselineValue", table: "EmployeeGoals");
            migrationBuilder.DropColumn(name: "IsDeleted",     table: "EmployeeGoals");
        }
    }
}
