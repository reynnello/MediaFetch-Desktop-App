using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaFetch.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DownloadJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    MaxHeight = table.Column<int>(type: "INTEGER", nullable: true),
                    AudioFormat = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ProgressPercent = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Author = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    DurationSeconds = table.Column<double>(type: "REAL", nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ThumbnailUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    OutputPath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadJobs_CreatedAt",
                table: "DownloadJobs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadJobs_Status",
                table: "DownloadJobs",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DownloadJobs");
        }
    }
}
