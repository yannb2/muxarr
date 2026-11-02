using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muxarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackAudioFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing rows were scanned before detection existed; a rescan fills them in.
            migrationBuilder.AddColumn<string>(
                name: "AudioFeature",
                table: "TrackSnapshot",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AudioFeature",
                table: "TrackSnapshot");
        }
    }
}
