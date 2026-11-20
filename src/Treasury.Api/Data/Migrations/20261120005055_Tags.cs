using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Treasury.Api.Data.Migrations;

/// <inheritdoc />
public partial class _20261120005055_Tags : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Tags",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                DateFrom = table.Column<DateOnly>(type: "date", nullable: true),
                DateTo = table.Column<DateOnly>(type: "date", nullable: true),
                LimitToFamily = table.Column<bool>(type: "boolean", nullable: false),
                LimitToMemberIds = table.Column<Guid[]>(type: "uuid[]", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Tags", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "TransactionTagRemovals",
            columns: table => new
            {
                TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                TagId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TransactionTagRemovals", x => new { x.TransactionId, x.TagId });
                table.ForeignKey(
                    name: "FK_TransactionTagRemovals_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_TransactionTagRemovals_Transactions_TransactionId",
                    column: x => x.TransactionId,
                    principalTable: "Transactions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "TransactionTags",
            columns: table => new
            {
                TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                TagId = table.Column<Guid>(type: "uuid", nullable: false),
                Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TransactionTags", x => new { x.TransactionId, x.TagId, x.Source });
                table.ForeignKey(
                    name: "FK_TransactionTags_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_TransactionTags_Transactions_TransactionId",
                    column: x => x.TransactionId,
                    principalTable: "Transactions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_TransactionTagRemovals_TagId",
            table: "TransactionTagRemovals",
            column: "TagId");

        migrationBuilder.CreateIndex(
            name: "IX_TransactionTags_TagId",
            table: "TransactionTags",
            column: "TagId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "TransactionTagRemovals");

        migrationBuilder.DropTable(
            name: "TransactionTags");

        migrationBuilder.DropTable(
            name: "Tags");
    }
}
