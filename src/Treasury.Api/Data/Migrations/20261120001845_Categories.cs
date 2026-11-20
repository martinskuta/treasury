using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Treasury.Api.Data.Migrations;

/// <inheritdoc />
public partial class _20261120001845_Categories : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "CategoryId",
            table: "Transactions",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "Categories",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                BuiltInKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Categories", x => x.Id);
                table.ForeignKey(
                    name: "FK_Categories_Categories_ParentId",
                    column: x => x.ParentId,
                    principalTable: "Categories",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Transactions_CategoryId",
            table: "Transactions",
            column: "CategoryId");

        migrationBuilder.CreateIndex(
            name: "IX_Categories_BuiltInKey",
            table: "Categories",
            column: "BuiltInKey",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Categories_ParentId",
            table: "Categories",
            column: "ParentId");

        migrationBuilder.AddForeignKey(
            name: "FK_Transactions_Categories_CategoryId",
            table: "Transactions",
            column: "CategoryId",
            principalTable: "Categories",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Transactions_Categories_CategoryId",
            table: "Transactions");

        migrationBuilder.DropTable(
            name: "Categories");

        migrationBuilder.DropIndex(
            name: "IX_Transactions_CategoryId",
            table: "Transactions");

        migrationBuilder.DropColumn(
            name: "CategoryId",
            table: "Transactions");
    }
}
