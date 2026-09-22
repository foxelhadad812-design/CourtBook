using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentHoldIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Payments_Status_ExpiresAt",
                table: "Payments",
                columns: new[] { "Status", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_Status_ExpiresAt",
                table: "Payments");
        }
    }
}
