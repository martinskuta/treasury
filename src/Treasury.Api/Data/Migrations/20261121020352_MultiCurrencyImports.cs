using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Treasury.Api.Data.Migrations;

/// <inheritdoc />
public partial class _20261121020352_MultiCurrencyImports : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<Guid>(
            name: "AccountId",
            table: "SourceFiles",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.AddColumn<Guid>(
            name: "MultiCurrencyAccountId",
            table: "SourceFiles",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "SourceFileClosingBalances",
            columns: table => new
            {
                SourceFileId = table.Column<Guid>(type: "uuid", nullable: false),
                AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SourceFileClosingBalances", x => new { x.SourceFileId, x.AccountId });
                table.ForeignKey(
                    name: "FK_SourceFileClosingBalances_Accounts_AccountId",
                    column: x => x.AccountId,
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_SourceFileClosingBalances_SourceFiles_SourceFileId",
                    column: x => x.SourceFileId,
                    principalTable: "SourceFiles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_SourceFiles_MultiCurrencyAccountId_ImportedAt",
            table: "SourceFiles",
            columns: new[] { "MultiCurrencyAccountId", "ImportedAt" });

        migrationBuilder.AddCheckConstraint(
            name: "CK_SourceFiles_AccountOrMultiCurrencyAccount",
            table: "SourceFiles",
            sql: "(\"AccountId\" IS NULL) <> (\"MultiCurrencyAccountId\" IS NULL)");

        migrationBuilder.CreateIndex(
            name: "IX_SourceFileClosingBalances_AccountId",
            table: "SourceFileClosingBalances",
            column: "AccountId");

        migrationBuilder.AddForeignKey(
            name: "FK_SourceFiles_MultiCurrencyAccounts_MultiCurrencyAccountId",
            table: "SourceFiles",
            column: "MultiCurrencyAccountId",
            principalTable: "MultiCurrencyAccounts",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_SourceFiles_MultiCurrencyAccounts_MultiCurrencyAccountId",
            table: "SourceFiles");

        migrationBuilder.DropTable(
            name: "SourceFileClosingBalances");

        migrationBuilder.DropIndex(
            name: "IX_SourceFiles_MultiCurrencyAccountId_ImportedAt",
            table: "SourceFiles");

        migrationBuilder.DropCheckConstraint(
            name: "CK_SourceFiles_AccountOrMultiCurrencyAccount",
            table: "SourceFiles");

        migrationBuilder.DropColumn(
            name: "MultiCurrencyAccountId",
            table: "SourceFiles");

        migrationBuilder.AlterColumn<Guid>(
            name: "AccountId",
            table: "SourceFiles",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);
    }
}
