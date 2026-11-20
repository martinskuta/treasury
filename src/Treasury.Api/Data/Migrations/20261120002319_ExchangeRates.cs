using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Treasury.Api.Data.Migrations;

/// <inheritdoc />
public partial class _20261120002319_ExchangeRates : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ExchangeRates",
            columns: table => new
            {
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                PerEur = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ExchangeRates", x => new { x.Date, x.Currency });
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ExchangeRates");
    }
}
