using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muxarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackNameBackup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BackupTrackNames",
                table: "Profile",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Filled by the next scan of a file whose profile backs names up.
            migrationBuilder.AddColumn<string>(
                name: "OriginalTrackNames",
                table: "MediaFile",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackupTrackNames",
                table: "Profile");

            migrationBuilder.DropColumn(
                name: "OriginalTrackNames",
                table: "MediaFile");
        }
    }
}
