using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Treasury.Api.Data.Migrations;

/// <inheritdoc />
public partial class _20261119235443_Accounts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Accounts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                OwnerMemberId = table.Column<Guid>(type: "uuid", nullable: true),
                IntegrationKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                OpeningBalance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                OpeningDate = table.Column<DateOnly>(type: "date", nullable: false),
                Identifier = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Accounts", x => x.Id);
                table.ForeignKey(
                    name: "FK_Accounts_AspNetUsers_OwnerMemberId",
                    column: x => x.OwnerMemberId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Accounts_OwnerMemberId",
            table: "Accounts",
            column: "OwnerMemberId");

        migrationBuilder.CreateIndex(
            name: "IX_Accounts_Slug",
            table: "Accounts",
            column: "Slug",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Accounts");
    }
}
