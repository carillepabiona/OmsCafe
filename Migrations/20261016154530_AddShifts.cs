using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmsCafe.Migrations
{
    /// <inheritdoc />
    public partial class AddShifts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.AddColumn<int>(
                name: "ShiftId",
                table: "Payments",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "DiscountAmount",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.CreateTable(
                name: "Shifts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CashierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CashierName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OpeningFloat = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shifts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShiftClosingReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShiftId = table.Column<int>(type: "int", nullable: false),
                    ExpectedCash = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    ActualCash = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Variance = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Result = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Explanation = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedByName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ManagerNotes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftClosingReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftClosingReports_Shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "Shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Shifts_ShiftId",
                table: "Payments",
                column: "ShiftId",
                principalTable: "Shifts",
                principalColumn: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ShiftId",
                table: "Payments",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftClosingReports_ShiftId",
                table: "ShiftClosingReports",
                column: "ShiftId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_CashierId",
                table: "Shifts",
                column: "CashierId",
                unique: true,
                filter: "[Status] = 'Open'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Shifts_ShiftId",
                table: "Payments");

            migrationBuilder.DropTable(
                name: "ShiftClosingReports");

            migrationBuilder.DropTable(
                name: "Shifts");

            migrationBuilder.DropIndex(
                name: "IX_Payments_ShiftId",
                table: "Payments");


            migrationBuilder.DropColumn(
                name: "ShiftId",
                table: "Payments");

            migrationBuilder.AlterColumn<decimal>(
                name: "DiscountAmount",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);
        }
    }
}
