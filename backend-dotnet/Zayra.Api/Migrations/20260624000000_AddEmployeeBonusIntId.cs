using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zayra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeBonusIntId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Bridge column so payroll Process() can join EmployeeBonus → Employee by int PK,
            // matching the same pattern used by EmployeeLoan.EmployeeIntId and SalaryAdvance.EmployeeIntId.
            migrationBuilder.AddColumn<int>(
                name: "EmployeeIntId",
                table: "employee_bonuses",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "EmployeeIntId", table: "employee_bonuses");
        }
    }
}
