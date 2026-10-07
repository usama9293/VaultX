using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PasswordManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase4MetadataOnlyEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "WebsiteUrl",
                table: "VaultEntries",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048);

            migrationBuilder.AlterColumn<byte[]>(
                name: "PasswordNonce",
                table: "VaultEntries",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "bytea");

            migrationBuilder.AlterColumn<byte[]>(
                name: "PasswordAuthenticationTag",
                table: "VaultEntries",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "bytea");

            migrationBuilder.AlterColumn<byte[]>(
                name: "EncryptedPassword",
                table: "VaultEntries",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "bytea");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "VaultEntries"
                        WHERE "WebsiteUrl" IS NULL
                           OR "EncryptedPassword" IS NULL
                           OR "PasswordNonce" IS NULL
                           OR "PasswordAuthenticationTag" IS NULL
                    ) THEN
                        RAISE EXCEPTION 'Cannot roll back metadata-only entries while nullable entry fields are in use.';
                    END IF;
                END
                $$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "WebsiteUrl",
                table: "VaultEntries",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048,
                oldNullable: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "PasswordNonce",
                table: "VaultEntries",
                type: "bytea",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "PasswordAuthenticationTag",
                table: "VaultEntries",
                type: "bytea",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "EncryptedPassword",
                table: "VaultEntries",
                type: "bytea",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);
        }
    }
}
