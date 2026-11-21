using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Treasury.Api.Data.Migrations;

/// <inheritdoc />
public partial class _20261121014953_MultiCurrencyAccounts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "MultiCurrencyAccountId",
            table: "Accounts",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "MultiCurrencyAccounts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                OwnerMemberId = table.Column<Guid>(type: "uuid", nullable: true),
                IntegrationKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                MainCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MultiCurrencyAccounts", x => x.Id);
                table.ForeignKey(
                    name: "FK_MultiCurrencyAccounts_AspNetUsers_OwnerMemberId",
                    column: x => x.OwnerMemberId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Accounts_MultiCurrencyAccountId",
            table: "Accounts",
            column: "MultiCurrencyAccountId");

        migrationBuilder.CreateIndex(
            name: "IX_MultiCurrencyAccounts_OwnerMemberId",
            table: "MultiCurrencyAccounts",
            column: "OwnerMemberId");

        migrationBuilder.CreateIndex(
            name: "IX_MultiCurrencyAccounts_Slug",
            table: "MultiCurrencyAccounts",
            column: "Slug",
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_Accounts_MultiCurrencyAccounts_MultiCurrencyAccountId",
            table: "Accounts",
            column: "MultiCurrencyAccountId",
            principalTable: "MultiCurrencyAccounts",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Accounts_MultiCurrencyAccounts_MultiCurrencyAccountId",
            table: "Accounts");

        migrationBuilder.DropTable(
            name: "MultiCurrencyAccounts");

        migrationBuilder.DropIndex(
            name: "IX_Accounts_MultiCurrencyAccountId",
            table: "Accounts");

        migrationBuilder.DropColumn(
            name: "MultiCurrencyAccountId",
            table: "Accounts");
    }
}
