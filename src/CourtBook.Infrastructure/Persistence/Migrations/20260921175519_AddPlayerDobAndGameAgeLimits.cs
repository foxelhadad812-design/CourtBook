using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerDobAndGameAgeLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DateOfBirth",
                table: "Users",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgeGroup",
                table: "Games",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "AllAges");

            migrationBuilder.AddColumn<int>(
                name: "MaxAge",
                table: "Games",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinAge",
                table: "Games",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DateOfBirth",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AgeGroup",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "MaxAge",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "MinAge",
                table: "Games");
        }
    }
}
