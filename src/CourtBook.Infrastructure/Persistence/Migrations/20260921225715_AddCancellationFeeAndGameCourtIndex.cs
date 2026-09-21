using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCancellationFeeAndGameCourtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Games_CourtId",
                table: "Games");

            migrationBuilder.AddColumn<decimal>(
                name: "CancellationFee",
                table: "Bookings",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_Games_CourtId_Date_Status",
                table: "Games",
                columns: new[] { "CourtId", "Date", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Games_CourtId_Date_Status",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "CancellationFee",
                table: "Bookings");

            migrationBuilder.CreateIndex(
                name: "IX_Games_CourtId",
                table: "Games",
                column: "CourtId");
        }
    }
}
