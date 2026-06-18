using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zayra.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMfaSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── users: TOTP fields ────────────────────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "mfa_secret_encrypted",
                table: "users",
                type: "varchar(1024)",
                maxLength: 1024,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "mfa_configured_at_utc",
                table: "users",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "mfa_last_verified_at_utc",
                table: "users",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "mfa_failed_count",
                table: "users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // ── security_settings: mfa_required policy ────────────────────────
            migrationBuilder.AddColumn<bool>(
                name: "mfa_required",
                table: "security_settings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            // ── platform_users: TOTP fields ───────────────────────────────────
            migrationBuilder.AddColumn<bool>(
                name: "mfa_enabled",
                table: "platform_users",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "mfa_secret_encrypted",
                table: "platform_users",
                type: "varchar(1024)",
                maxLength: 1024,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "mfa_configured_at_utc",
                table: "platform_users",
                type: "datetime(6)",
                nullable: true);

            // ── mfa_challenge_tokens table ────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "mfa_challenge_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    user_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    platform_user_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    token_hash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    expires_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_by_ip = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    used_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mfa_challenge_tokens", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_mfa_challenge_tokens_token_hash",
                table: "mfa_challenge_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mfa_challenge_tokens_expires_at_utc",
                table: "mfa_challenge_tokens",
                column: "expires_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "mfa_challenge_tokens");

            migrationBuilder.DropColumn(name: "mfa_required",     table: "security_settings");

            migrationBuilder.DropColumn(name: "mfa_secret_encrypted",   table: "users");
            migrationBuilder.DropColumn(name: "mfa_configured_at_utc",  table: "users");
            migrationBuilder.DropColumn(name: "mfa_last_verified_at_utc", table: "users");
            migrationBuilder.DropColumn(name: "mfa_failed_count",        table: "users");

            migrationBuilder.DropColumn(name: "mfa_enabled",           table: "platform_users");
            migrationBuilder.DropColumn(name: "mfa_secret_encrypted",  table: "platform_users");
            migrationBuilder.DropColumn(name: "mfa_configured_at_utc", table: "platform_users");
        }
    }
}
