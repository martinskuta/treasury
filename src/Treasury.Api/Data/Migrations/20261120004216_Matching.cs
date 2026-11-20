using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Treasury.Api.Data.Migrations;

/// <inheritdoc />
public partial class _20261120004216_Matching : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "LinkKind",
            table: "Transactions",
            type: "character varying(16)",
            maxLength: 16,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "LinkedTransactionId",
            table: "Transactions",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Transactions_LinkedTransactionId",
            table: "Transactions",
            column: "LinkedTransactionId",
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_Transactions_Transactions_LinkedTransactionId",
            table: "Transactions",
            column: "LinkedTransactionId",
            principalTable: "Transactions",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Transactions_Transactions_LinkedTransactionId",
            table: "Transactions");

        migrationBuilder.DropIndex(
            name: "IX_Transactions_LinkedTransactionId",
            table: "Transactions");

        migrationBuilder.DropColumn(
            name: "LinkKind",
            table: "Transactions");

        migrationBuilder.DropColumn(
            name: "LinkedTransactionId",
            table: "Transactions");
    }
}
