using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Envoy.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJobEventsAndGhostRisk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GhostRiskBand",
                table: "ApplicationLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "GhostRiskScore",
                table: "ApplicationLogs",
                type: "REAL",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "JobEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    JobUrl = table.Column<string>(type: "TEXT", nullable: false),
                    JobTitle = table.Column<string>(type: "TEXT", nullable: false),
                    Company = table.Column<string>(type: "TEXT", nullable: false),
                    PostingKey = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    RiskScore = table.Column<double>(type: "REAL", nullable: true),
                    RiskBand = table.Column<string>(type: "TEXT", nullable: true),
                    Evidence = table.Column<string>(type: "TEXT", nullable: true),
                    ApplicationLogId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobEvents_OccurredAt",
                table: "JobEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_JobEvents_PostingKey",
                table: "JobEvents",
                column: "PostingKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobEvents");

            migrationBuilder.DropColumn(
                name: "GhostRiskBand",
                table: "ApplicationLogs");

            migrationBuilder.DropColumn(
                name: "GhostRiskScore",
                table: "ApplicationLogs");
        }
    }
}
