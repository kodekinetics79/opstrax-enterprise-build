using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zayra.Api.Migrations
{
    /// <inheritdoc />
    public partial class FixSifWpsColumnName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "w_p_s_file_batch_id",
                table: "sif_file_records",
                newName: "wps_file_batch_id");

            migrationBuilder.RenameIndex(
                name: "IX_sif_file_records_tenant_id_w_p_s_file_batch_id",
                table: "sif_file_records",
                newName: "IX_sif_file_records_tenant_id_wps_file_batch_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "wps_file_batch_id",
                table: "sif_file_records",
                newName: "w_p_s_file_batch_id");

            migrationBuilder.RenameIndex(
                name: "IX_sif_file_records_tenant_id_wps_file_batch_id",
                table: "sif_file_records",
                newName: "IX_sif_file_records_tenant_id_w_p_s_file_batch_id");
        }
    }
}
