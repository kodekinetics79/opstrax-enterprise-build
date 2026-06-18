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
                name: "MfaSecretEncrypted",
                table: "users",
                type: "varchar(1024)",
                maxLength: 1024,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "MfaConfiguredAtUtc",
                table: "users",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MfaLastVerifiedAtUtc",
                table: "users",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MfaFailedCount",
                table: "users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // ── security_settings: MfaRequired policy ────────────────────────
            migrationBuilder.AddColumn<bool>(
                name: "MfaRequired",
                table: "security_settings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            // ── platform_users: TOTP fields ───────────────────────────────────
            migrationBuilder.AddColumn<bool>(
                name: "MfaEnabled",
                table: "platform_users",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MfaSecretEncrypted",
                table: "platform_users",
                type: "varchar(1024)",
                maxLength: 1024,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "MfaConfiguredAtUtc",
                table: "platform_users",
                type: "datetime(6)",
                nullable: true);

            // ── mfa_challenge_tokens table ────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "mfa_challenge_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    PlatformUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    TokenHash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedByIp = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UsedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mfa_challenge_tokens", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_mfa_challenge_tokens_TokenHash",
                table: "mfa_challenge_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mfa_challenge_tokens_ExpiresAtUtc",
                table: "mfa_challenge_tokens",
                column: "ExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "mfa_challenge_tokens");

            migrationBuilder.DropColumn(name: "MfaRequired",     table: "security_settings");

            migrationBuilder.DropColumn(name: "MfaSecretEncrypted",  table: "users");
            migrationBuilder.DropColumn(name: "MfaConfiguredAtUtc",  table: "users");
            migrationBuilder.DropColumn(name: "MfaLastVerifiedAtUtc", table: "users");
            migrationBuilder.DropColumn(name: "MfaFailedCount",       table: "users");

            migrationBuilder.DropColumn(name: "MfaEnabled",          table: "platform_users");
            migrationBuilder.DropColumn(name: "MfaSecretEncrypted",  table: "platform_users");
            migrationBuilder.DropColumn(name: "MfaConfiguredAtUtc",  table: "platform_users");
        }
    }
}
