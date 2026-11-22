using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Treasury.Api.Data.Migrations;

/// <inheritdoc />
public partial class _20261121235447_MultiCurrencyAccountIdentifier : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Identifier",
            table: "MultiCurrencyAccounts",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Identifier",
            table: "MultiCurrencyAccounts");
    }
}
