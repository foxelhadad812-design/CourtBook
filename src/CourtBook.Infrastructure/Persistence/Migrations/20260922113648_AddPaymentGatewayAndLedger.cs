using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentGatewayAndLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "TransactionReference",
                table: "Payments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Payments",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<decimal>(
                name: "CommissionAmount",
                table: "Payments",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "Payments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OwnerNetAmount",
                table: "Payments",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PaymentUrl",
                table: "Payments",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderOrderId",
                table: "Payments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IdempotencyLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ProviderTransactionId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ResponseStatusCode = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TransactionLedger",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    GrossAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    CommissionAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    CommissionRateSnapshot = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: "EGP"),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ProviderReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionLedger", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransactionLedger_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ProviderOrderId",
                table: "Payments",
                column: "ProviderOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyLog_Provider_TransactionId",
                table: "IdempotencyLogs",
                columns: new[] { "Provider", "ProviderTransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyLogs_PaymentId",
                table: "IdempotencyLogs",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyLogs_ProcessedAt",
                table: "IdempotencyLogs",
                column: "ProcessedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionLedger_BookingId",
                table: "TransactionLedger",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionLedger_CreatedAt",
                table: "TransactionLedger",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionLedger_EntryType",
                table: "TransactionLedger",
                column: "EntryType");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionLedger_OwnerId",
                table: "TransactionLedger",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionLedger_PaymentId",
                table: "TransactionLedger",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionLedger_UserId",
                table: "TransactionLedger",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdempotencyLogs");

            migrationBuilder.DropTable(
                name: "TransactionLedger");

            migrationBuilder.DropIndex(
                name: "IX_Payments_ProviderOrderId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CommissionAmount",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "OwnerNetAmount",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PaymentUrl",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ProviderOrderId",
                table: "Payments");

            migrationBuilder.AlterColumn<string>(
                name: "TransactionReference",
                table: "Payments",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Payments",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(30)",
                oldMaxLength: 30);
        }
    }
}
