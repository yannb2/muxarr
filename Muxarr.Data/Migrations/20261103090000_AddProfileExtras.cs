using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muxarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileExtras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing profiles keep treating extras like any other file.
            migrationBuilder.AddColumn<string>(
                name: "Extras",
                table: "Profile",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Process");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Extras",
                table: "Profile");
        }
    }
}
