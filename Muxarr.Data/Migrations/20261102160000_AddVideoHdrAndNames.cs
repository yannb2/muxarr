using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muxarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoHdrAndNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing rows were scanned before detection existed; a rescan fills them in.
            migrationBuilder.AddColumn<string>(
                name: "DvLayer",
                table: "TrackSnapshot",
                type: "TEXT",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DvProfile",
                table: "TrackSnapshot",
                type: "TEXT",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HdrFormat",
                table: "TrackSnapshot",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Resolution",
                table: "TrackSnapshot",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VideoSettings",
                table: "Profile",
                type: "TEXT",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DvLayer",
                table: "TrackSnapshot");

            migrationBuilder.DropColumn(
                name: "DvProfile",
                table: "TrackSnapshot");

            migrationBuilder.DropColumn(
                name: "HdrFormat",
                table: "TrackSnapshot");

            migrationBuilder.DropColumn(
                name: "Resolution",
                table: "TrackSnapshot");

            migrationBuilder.DropColumn(
                name: "VideoSettings",
                table: "Profile");
        }
    }
}
