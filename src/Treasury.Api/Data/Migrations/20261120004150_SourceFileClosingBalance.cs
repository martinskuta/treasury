using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Treasury.Api.Data.Migrations;

/// <inheritdoc />
public partial class _20261120004150_SourceFileClosingBalance : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "ClosingBalanceAmount",
            table: "SourceFiles",
            type: "numeric(19,4)",
            precision: 19,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "ClosingBalanceDate",
            table: "SourceFiles",
            type: "date",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ClosingBalanceAmount",
            table: "SourceFiles");

        migrationBuilder.DropColumn(
            name: "ClosingBalanceDate",
            table: "SourceFiles");
    }
}
