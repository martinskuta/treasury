using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Treasury.Api.Data.Migrations;

/// <inheritdoc />
public partial class _20261120003045_Import : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Counterparty",
            table: "Transactions",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "DisplayName",
            table: "Transactions",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "OriginalAmount",
            table: "Transactions",
            type: "numeric(19,4)",
            precision: 19,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "OriginalCurrency",
            table: "Transactions",
            type: "character varying(3)",
            maxLength: 3,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Reference",
            table: "Transactions",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "SourceFileId",
            table: "Transactions",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "SourceFiles",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StoredPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Size = table.Column<long>(type: "bigint", nullable: false),
                ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ImportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ImportedByMemberId = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SourceFiles", x => x.Id);
                table.ForeignKey(
                    name: "FK_SourceFiles_Accounts_AccountId",
                    column: x => x.AccountId,
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_SourceFiles_AspNetUsers_ImportedByMemberId",
                    column: x => x.ImportedByMemberId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Transactions_SourceFileId",
            table: "Transactions",
            column: "SourceFileId");

        migrationBuilder.CreateIndex(
            name: "IX_SourceFiles_AccountId_ImportedAt",
            table: "SourceFiles",
            columns: new[] { "AccountId", "ImportedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_SourceFiles_ImportedByMemberId",
            table: "SourceFiles",
            column: "ImportedByMemberId");

        migrationBuilder.AddForeignKey(
            name: "FK_Transactions_SourceFiles_SourceFileId",
            table: "Transactions",
            column: "SourceFileId",
            principalTable: "SourceFiles",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Transactions_SourceFiles_SourceFileId",
            table: "Transactions");

        migrationBuilder.DropTable(
            name: "SourceFiles");

        migrationBuilder.DropIndex(
            name: "IX_Transactions_SourceFileId",
            table: "Transactions");

        migrationBuilder.DropColumn(
            name: "Counterparty",
            table: "Transactions");

        migrationBuilder.DropColumn(
            name: "DisplayName",
            table: "Transactions");

        migrationBuilder.DropColumn(
            name: "OriginalAmount",
            table: "Transactions");

        migrationBuilder.DropColumn(
            name: "OriginalCurrency",
            table: "Transactions");

        migrationBuilder.DropColumn(
            name: "Reference",
            table: "Transactions");

        migrationBuilder.DropColumn(
            name: "SourceFileId",
            table: "Transactions");
    }
}
