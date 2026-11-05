using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Envoy.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCoverLetter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CoverLetterText",
                table: "TailoredProfiles",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoverLetterText",
                table: "TailoredProfiles");
        }
    }
}
