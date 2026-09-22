using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialPayoutAndSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "PaymentId",
                table: "TransactionLedger",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "BookingId",
                table: "TransactionLedger",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "PayoutRequestId",
                table: "TransactionLedger",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OwnerBalances",
                columns: table => new
                {
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PendingBalance = table.Column<decimal>(type: "decimal(10,2)", nullable: false, defaultValue: 0m),
                    AvailableBalance = table.Column<decimal>(type: "decimal(10,2)", nullable: false, defaultValue: 0m),
                    InFlightBalance = table.Column<decimal>(type: "decimal(10,2)", nullable: false, defaultValue: 0m),
                    TotalPaidOut = table.Column<decimal>(type: "decimal(10,2)", nullable: false, defaultValue: 0m),
                    TotalRefunded = table.Column<decimal>(type: "decimal(10,2)", nullable: false, defaultValue: 0m),
                    OutstandingDeficit = table.Column<decimal>(type: "decimal(10,2)", nullable: false, defaultValue: 0m),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: "EGP"),
                    ConcurrencyStamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerBalances", x => x.OwnerId);
                    table.ForeignKey(
                        name: "FK_OwnerBalances_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OwnerPayoutMethods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AccountHolderName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BankName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Iban = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AccountNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    InstaPayAddress = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MobileWalletNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsVerified = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerPayoutMethods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OwnerPayoutMethods_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecoveryObligations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObligationReference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TotalDeficitAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    RemainingDeficitAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: "EGP"),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedBySettlementBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AdminNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryObligations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecoveryObligations_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecoveryObligations_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecoveryObligations_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SettlementBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchReference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalGross = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    TotalCommission = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    TotalNet = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettlementBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayoutRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayoutReference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayoutMethodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Fee = table.Column<decimal>(type: "decimal(10,2)", nullable: false, defaultValue: 0m),
                    NetAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: "EGP"),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ExternalTransactionReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DisbursementNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ApprovedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DisbursedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayoutRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayoutRequests_OwnerPayoutMethods_PayoutMethodId",
                        column: x => x.PayoutMethodId,
                        principalTable: "OwnerPayoutMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayoutRequests_Users_ApprovedByAdminId",
                        column: x => x.ApprovedByAdminId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayoutRequests_Users_DisbursedByAdminId",
                        column: x => x.DisbursedByAdminId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayoutRequests_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SettlementItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SettlementBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrossAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    CommissionAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    SettledAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettlementItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SettlementItems_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SettlementItems_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SettlementItems_SettlementBatches_SettlementBatchId",
                        column: x => x.SettlementBatchId,
                        principalTable: "SettlementBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SettlementItems_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionLedger_PayoutRequestId",
                table: "TransactionLedger",
                column: "PayoutRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerPayoutMethods_OwnerId",
                table: "OwnerPayoutMethods",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerPayoutMethods_OwnerId_IsActive",
                table: "OwnerPayoutMethods",
                columns: new[] { "OwnerId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_OwnerPayoutMethods_OwnerId_IsDefault",
                table: "OwnerPayoutMethods",
                columns: new[] { "OwnerId", "IsDefault" });

            migrationBuilder.CreateIndex(
                name: "IX_PayoutRequests_ApprovedByAdminId",
                table: "PayoutRequests",
                column: "ApprovedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_PayoutRequests_DisbursedByAdminId",
                table: "PayoutRequests",
                column: "DisbursedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_PayoutRequests_ExternalTransactionReference",
                table: "PayoutRequests",
                column: "ExternalTransactionReference",
                unique: true,
                filter: "[ExternalTransactionReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PayoutRequests_OwnerId",
                table: "PayoutRequests",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_PayoutRequests_PayoutMethodId",
                table: "PayoutRequests",
                column: "PayoutMethodId");

            migrationBuilder.CreateIndex(
                name: "IX_PayoutRequests_PayoutReference",
                table: "PayoutRequests",
                column: "PayoutReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayoutRequests_Status",
                table: "PayoutRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PayoutRequests_SubmittedAt",
                table: "PayoutRequests",
                column: "SubmittedAt");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryObligations_BookingId",
                table: "RecoveryObligations",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryObligations_CreatedAt",
                table: "RecoveryObligations",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryObligations_ObligationReference",
                table: "RecoveryObligations",
                column: "ObligationReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryObligations_OwnerId",
                table: "RecoveryObligations",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryObligations_OwnerId_Status",
                table: "RecoveryObligations",
                columns: new[] { "OwnerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryObligations_PaymentId",
                table: "RecoveryObligations",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementBatches_BatchReference",
                table: "SettlementBatches",
                column: "BatchReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SettlementBatches_CreatedAt",
                table: "SettlementBatches",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementItems_BookingId",
                table: "SettlementItems",
                column: "BookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SettlementItems_OwnerId",
                table: "SettlementItems",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementItems_PaymentId",
                table: "SettlementItems",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementItems_SettledAt",
                table: "SettlementItems",
                column: "SettledAt");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementItems_SettlementBatchId",
                table: "SettlementItems",
                column: "SettlementBatchId");

            migrationBuilder.AddForeignKey(
                name: "FK_TransactionLedger_PayoutRequests_PayoutRequestId",
                table: "TransactionLedger",
                column: "PayoutRequestId",
                principalTable: "PayoutRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TransactionLedger_PayoutRequests_PayoutRequestId",
                table: "TransactionLedger");

            migrationBuilder.DropTable(
                name: "OwnerBalances");

            migrationBuilder.DropTable(
                name: "PayoutRequests");

            migrationBuilder.DropTable(
                name: "RecoveryObligations");

            migrationBuilder.DropTable(
                name: "SettlementItems");

            migrationBuilder.DropTable(
                name: "OwnerPayoutMethods");

            migrationBuilder.DropTable(
                name: "SettlementBatches");

            migrationBuilder.DropIndex(
                name: "IX_TransactionLedger_PayoutRequestId",
                table: "TransactionLedger");

            migrationBuilder.DropColumn(
                name: "PayoutRequestId",
                table: "TransactionLedger");

            migrationBuilder.AlterColumn<Guid>(
                name: "PaymentId",
                table: "TransactionLedger",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "BookingId",
                table: "TransactionLedger",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
