using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHuntHQ.Migrations
{
    /// <inheritdoc />
    public partial class AddResumeFile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BlobName",
                table: "ResumeVersions",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "ResumeVersions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileName",
                table: "ResumeVersions",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SizeBytes",
                table: "ResumeVersions",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlobName",
                table: "ResumeVersions");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "ResumeVersions");

            migrationBuilder.DropColumn(
                name: "FileName",
                table: "ResumeVersions");

            migrationBuilder.DropColumn(
                name: "SizeBytes",
                table: "ResumeVersions");
        }
    }
}
