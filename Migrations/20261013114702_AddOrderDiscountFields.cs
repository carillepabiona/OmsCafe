using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmsCafe.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderDiscountFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Baseline migration: every table and the discount columns already exist
            // in the hosted database, so nothing needs to run here.
            // This migration only exists so EF records the current model
            // (see the .Designer.cs and AppDbContextModelSnapshot.cs files).
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}