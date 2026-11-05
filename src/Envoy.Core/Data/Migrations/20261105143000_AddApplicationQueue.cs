using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Envoy.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QueuedApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MasterProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PostingKey = table.Column<string>(type: "TEXT", nullable: false),
                    JobUrl = table.Column<string>(type: "TEXT", nullable: false),
                    JobTitle = table.Column<string>(type: "TEXT", nullable: false),
                    Company = table.Column<string>(type: "TEXT", nullable: false),
                    DescriptionText = table.Column<string>(type: "TEXT", nullable: true),
                    GhostRiskScore = table.Column<double>(type: "REAL", nullable: true),
                    GhostRiskBand = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    TailoredProfileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueuedApplications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QueuedApplications_PostingKey",
                table: "QueuedApplications",
                column: "PostingKey");

            migrationBuilder.CreateIndex(
                name: "IX_QueuedApplications_Status",
                table: "QueuedApplications",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QueuedApplications");
        }
    }
}
