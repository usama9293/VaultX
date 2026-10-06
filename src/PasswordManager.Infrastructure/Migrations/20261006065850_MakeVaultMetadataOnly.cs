using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PasswordManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakeVaultMetadataOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                LOCK TABLE "Vaults" IN ACCESS EXCLUSIVE MODE;
                """);

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "Vaults") THEN
                        RAISE EXCEPTION 'Cannot remove legacy vault key material while vault records exist. Back up and migrate vault data before applying this migration.';
                    END IF;
                END
                $$;
                """);

            migrationBuilder.DropColumn(
                name: "EncryptedKey",
                table: "Vaults");

            migrationBuilder.DropColumn(
                name: "KeyAuthenticationTag",
                table: "Vaults");

            migrationBuilder.DropColumn(
                name: "KeyNonce",
                table: "Vaults");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new InvalidOperationException(
                "Vault key material cannot be restored without valid cryptographic data.");
        }
    }
}
