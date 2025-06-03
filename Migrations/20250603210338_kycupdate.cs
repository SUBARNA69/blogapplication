using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace blogapplication.Migrations
{
    /// <inheritdoc />
    public partial class kycupdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "IdCardImagePath",
                table: "KycDetails",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FaceImagePath",
                table: "KycDetails",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IdMatch",
                table: "KycDetails",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NameMatch",
                table: "KycDetails",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "OcrProcessed",
                table: "KycDetails",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "UserEnteredId",
                table: "KycDetails",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UserEnteredName",
                table: "KycDetails",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IdMatch",
                table: "KycDetails");

            migrationBuilder.DropColumn(
                name: "NameMatch",
                table: "KycDetails");

            migrationBuilder.DropColumn(
                name: "OcrProcessed",
                table: "KycDetails");

            migrationBuilder.DropColumn(
                name: "UserEnteredId",
                table: "KycDetails");

            migrationBuilder.DropColumn(
                name: "UserEnteredName",
                table: "KycDetails");

            migrationBuilder.AlterColumn<string>(
                name: "IdCardImagePath",
                table: "KycDetails",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "FaceImagePath",
                table: "KycDetails",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");
        }
    }
}
