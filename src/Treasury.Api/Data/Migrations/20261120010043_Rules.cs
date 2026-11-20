using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Treasury.Api.Data.Migrations;

/// <inheritdoc />
public partial class _20261120010043_Rules : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Rules",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Priority = table.Column<int>(type: "integer", nullable: false),
                Field = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                Match = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                Text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                SetsOwner = table.Column<bool>(type: "boolean", nullable: false),
                OwnerMemberId = table.Column<Guid>(type: "uuid", nullable: true),
                AuthorMemberId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Rules", x => x.Id);
                table.ForeignKey(
                    name: "FK_Rules_AspNetUsers_AuthorMemberId",
                    column: x => x.AuthorMemberId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Rules_AspNetUsers_OwnerMemberId",
                    column: x => x.OwnerMemberId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Rules_Categories_CategoryId",
                    column: x => x.CategoryId,
                    principalTable: "Categories",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "RuleTags",
            columns: table => new
            {
                RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                TagId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RuleTags", x => new { x.RuleId, x.TagId });
                table.ForeignKey(
                    name: "FK_RuleTags_Rules_RuleId",
                    column: x => x.RuleId,
                    principalTable: "Rules",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_RuleTags_Tags_TagId",
                    column: x => x.TagId,
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Rules_AuthorMemberId",
            table: "Rules",
            column: "AuthorMemberId");

        migrationBuilder.CreateIndex(
            name: "IX_Rules_CategoryId",
            table: "Rules",
            column: "CategoryId");

        migrationBuilder.CreateIndex(
            name: "IX_Rules_OwnerMemberId",
            table: "Rules",
            column: "OwnerMemberId");

        migrationBuilder.CreateIndex(
            name: "IX_Rules_Priority",
            table: "Rules",
            column: "Priority");

        migrationBuilder.CreateIndex(
            name: "IX_RuleTags_TagId",
            table: "RuleTags",
            column: "TagId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "RuleTags");

        migrationBuilder.DropTable(
            name: "Rules");
    }
}
