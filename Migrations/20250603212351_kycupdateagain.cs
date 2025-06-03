using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace blogapplication.Migrations
{
    /// <inheritdoc />
    public partial class kycupdateagain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessedAt",
                table: "KycDetails",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProcessedAt",
                table: "KycDetails");
        }
    }
}
