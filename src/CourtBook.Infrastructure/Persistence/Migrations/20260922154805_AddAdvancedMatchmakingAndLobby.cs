using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdvancedMatchmakingAndLobby : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxDistanceKm",
                table: "PlayerPreferences",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreferredGameType",
                table: "PlayerPreferences",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PreferredSkillLevel",
                table: "PlayerPreferences",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreferredSports",
                table: "PlayerPreferences",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AccessCode",
                table: "Games",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyStamp",
                table: "Games",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<bool>(
                name: "HasTeams",
                table: "Games",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPrivate",
                table: "Games",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyStamp",
                table: "GameParticipants",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<bool>(
                name: "IsReady",
                table: "GameParticipants",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Team",
                table: "GameParticipants",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlayerSportSkills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SportType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SkillLevel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SkillScore = table.Column<int>(type: "int", nullable: false),
                    MatchesPlayed = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerSportSkills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerSportSkills_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Games_IsPrivate_Status",
                table: "Games",
                columns: new[] { "IsPrivate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSportSkills_UserId_SportType",
                table: "PlayerSportSkills",
                columns: new[] { "UserId", "SportType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerSportSkills");

            migrationBuilder.DropIndex(
                name: "IX_Games_IsPrivate_Status",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "MaxDistanceKm",
                table: "PlayerPreferences");

            migrationBuilder.DropColumn(
                name: "PreferredGameType",
                table: "PlayerPreferences");

            migrationBuilder.DropColumn(
                name: "PreferredSkillLevel",
                table: "PlayerPreferences");

            migrationBuilder.DropColumn(
                name: "PreferredSports",
                table: "PlayerPreferences");

            migrationBuilder.DropColumn(
                name: "AccessCode",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "HasTeams",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "IsPrivate",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "GameParticipants");

            migrationBuilder.DropColumn(
                name: "IsReady",
                table: "GameParticipants");

            migrationBuilder.DropColumn(
                name: "Team",
                table: "GameParticipants");
        }
    }
}
