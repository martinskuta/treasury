using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Treasury.Api.Data.Migrations;

/// <summary>An Account or Multi-currency account has several Identifiers (ADR-0002); its one identifier becomes
/// the first of them.</summary>
public partial class _20261124235253_MultipleIdentifiers : Migration
{
    private static readonly string[] Tables = ["Accounts", "MultiCurrencyAccounts"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var table in Tables)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "Identifiers",
                table: table,
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");
            migrationBuilder.Sql(
                $"""UPDATE "{table}" SET "Identifiers" = ARRAY["Identifier"] WHERE "Identifier" IS NOT NULL;""");
            migrationBuilder.Sql($"""ALTER TABLE "{table}" ALTER COLUMN "Identifiers" DROP DEFAULT;""");
            migrationBuilder.DropColumn(name: "Identifier", table: table);
        }
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Only the first Identifier fits the single column; the others are lost.
        foreach (var table in Tables)
        {
            migrationBuilder.AddColumn<string>(
                name: "Identifier",
                table: table,
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
            migrationBuilder.Sql($"""UPDATE "{table}" SET "Identifier" = "Identifiers"[1];""");
            migrationBuilder.DropColumn(name: "Identifiers", table: table);
        }
    }
}
