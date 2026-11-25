using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHuntHQ.Migrations
{
    /// <inheritdoc />
    public partial class AddM5InterviewAndApiTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BlockingQuestion",
                table: "JobApplications",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompanyResearch",
                table: "JobApplications",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmationReference",
                table: "JobApplications",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ElevatorPitch",
                table: "JobApplications",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalAttemptId",
                table: "JobApplications",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceAgent",
                table: "JobApplications",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ApiTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TokenPrefix = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Scopes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUsedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InterviewRounds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    JobApplicationId = table.Column<int>(type: "int", nullable: false),
                    RoundNumber = table.Column<int>(type: "int", nullable: false),
                    RoundType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ScheduledLocal = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TimeZoneLabel = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    MeetingUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Interviewers = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PrepNotes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    QuestionsToAsk = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DebriefNotes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Completed = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterviewRounds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InterviewRounds_JobApplications_JobApplicationId",
                        column: x => x.JobApplicationId,
                        principalTable: "JobApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoryBankItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Tags = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Situation = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Task = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Action = table.Column<string>(type: "nvarchar(3000)", maxLength: 3000, nullable: true),
                    Result = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoryBankItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobApplications_UserId_ExternalAttemptId",
                table: "JobApplications",
                columns: new[] { "UserId", "ExternalAttemptId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApiTokens_TokenHash",
                table: "ApiTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiTokens_UserId",
                table: "ApiTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_InterviewRounds_JobApplicationId_RoundNumber",
                table: "InterviewRounds",
                columns: new[] { "JobApplicationId", "RoundNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_InterviewRounds_UserId",
                table: "InterviewRounds",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_StoryBankItems_UserId",
                table: "StoryBankItems",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiTokens");

            migrationBuilder.DropTable(
                name: "InterviewRounds");

            migrationBuilder.DropTable(
                name: "StoryBankItems");

            migrationBuilder.DropIndex(
                name: "IX_JobApplications_UserId_ExternalAttemptId",
                table: "JobApplications");

            migrationBuilder.DropColumn(
                name: "BlockingQuestion",
                table: "JobApplications");

            migrationBuilder.DropColumn(
                name: "CompanyResearch",
                table: "JobApplications");

            migrationBuilder.DropColumn(
                name: "ConfirmationReference",
                table: "JobApplications");

            migrationBuilder.DropColumn(
                name: "ElevatorPitch",
                table: "JobApplications");

            migrationBuilder.DropColumn(
                name: "ExternalAttemptId",
                table: "JobApplications");

            migrationBuilder.DropColumn(
                name: "SourceAgent",
                table: "JobApplications");
        }
    }
}
