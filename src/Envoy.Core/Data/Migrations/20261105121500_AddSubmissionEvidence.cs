using Envoy.Core.Data.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Envoy.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubmissionEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RequiredFieldCount",
                table: "ApplicationLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FilledFieldCount",
                table: "ApplicationLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "VerifiedSubmission",
                table: "ApplicationLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationNote",
                table: "ApplicationLogs",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiredFieldCount",
                table: "ApplicationLogs");

            migrationBuilder.DropColumn(
                name: "FilledFieldCount",
                table: "ApplicationLogs");

            migrationBuilder.DropColumn(
                name: "VerifiedSubmission",
                table: "ApplicationLogs");

            migrationBuilder.DropColumn(
                name: "VerificationNote",
                table: "ApplicationLogs");
        }
    }
}
