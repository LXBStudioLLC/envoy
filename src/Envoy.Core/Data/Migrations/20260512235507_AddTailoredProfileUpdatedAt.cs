using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Envoy.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTailoredProfileUpdatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "TailoredProfiles",
                type: "TEXT",
                nullable: false,
                defaultValueSql: "datetime('now')");

            // Backfill UpdatedAt to CreatedAt for existing rows so the column
            // is sane for pre-migration data, not stuck at 0001-01-01 or NOW.
            migrationBuilder.Sql("UPDATE TailoredProfiles SET UpdatedAt = CreatedAt;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "TailoredProfiles");
        }
    }
}
