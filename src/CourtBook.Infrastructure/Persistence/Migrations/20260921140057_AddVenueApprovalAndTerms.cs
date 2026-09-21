using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVenueApprovalAndTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovalStatus",
                table: "Venues",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "Venues",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedById",
                table: "Venues",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "Venues",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TermsDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TermsDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TermsAcceptances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TermsDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TermsAcceptances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TermsAcceptances_TermsDocuments_TermsDocumentId",
                        column: x => x.TermsDocumentId,
                        principalTable: "TermsDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TermsAcceptances_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Venues_ApprovalStatus",
                table: "Venues",
                column: "ApprovalStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Venues_ApprovedById",
                table: "Venues",
                column: "ApprovedById");

            migrationBuilder.CreateIndex(
                name: "IX_TermsAcceptances_TermsDocumentId",
                table: "TermsAcceptances",
                column: "TermsDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_TermsAcceptances_UserId",
                table: "TermsAcceptances",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TermsDocuments_Type_IsActive",
                table: "TermsDocuments",
                columns: new[] { "Type", "IsActive" });

            migrationBuilder.AddForeignKey(
                name: "FK_Venues_Users_ApprovedById",
                table: "Venues",
                column: "ApprovedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Venues_Users_ApprovedById",
                table: "Venues");

            migrationBuilder.DropTable(
                name: "TermsAcceptances");

            migrationBuilder.DropTable(
                name: "TermsDocuments");

            migrationBuilder.DropIndex(
                name: "IX_Venues_ApprovalStatus",
                table: "Venues");

            migrationBuilder.DropIndex(
                name: "IX_Venues_ApprovedById",
                table: "Venues");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "Venues");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "Venues");

            migrationBuilder.DropColumn(
                name: "ApprovedById",
                table: "Venues");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "Venues");
        }
    }
}
