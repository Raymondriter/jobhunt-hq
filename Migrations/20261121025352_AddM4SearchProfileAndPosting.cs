using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHuntHQ.Migrations
{
    /// <inheritdoc />
    public partial class AddM4SearchProfileAndPosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FitReasons",
                table: "JobApplications",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FitVerdict",
                table: "JobApplications",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PayMax",
                table: "JobApplications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PayMin",
                table: "JobApplications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostingText",
                table: "JobApplications",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostingUrl",
                table: "JobApplications",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SearchProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TargetRoles = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RemotePreference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PayFloor = table.Column<int>(type: "int", nullable: true),
                    MustHaves = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DealBreakers = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SkipCompanies = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    StandingAnswers = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchProfiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SearchProfiles_UserId",
                table: "SearchProfiles",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SearchProfiles");

            migrationBuilder.DropColumn(
                name: "FitReasons",
                table: "JobApplications");

            migrationBuilder.DropColumn(
                name: "FitVerdict",
                table: "JobApplications");

            migrationBuilder.DropColumn(
                name: "PayMax",
                table: "JobApplications");

            migrationBuilder.DropColumn(
                name: "PayMin",
                table: "JobApplications");

            migrationBuilder.DropColumn(
                name: "PostingText",
                table: "JobApplications");

            migrationBuilder.DropColumn(
                name: "PostingUrl",
                table: "JobApplications");
        }
    }
}
