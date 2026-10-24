using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmsCafe.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewStatusToShiftClosingReport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReviewStatus",
                table: "ShiftClosingReports",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                table: "ShiftClosingReports");
        }
    }
}
