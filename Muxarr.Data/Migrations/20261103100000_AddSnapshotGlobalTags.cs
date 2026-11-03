using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muxarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSnapshotGlobalTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing snapshots keep an empty list until the file is rescanned.
            migrationBuilder.AddColumn<string>(
                name: "GlobalTags",
                table: "MediaSnapshot",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GlobalTags",
                table: "MediaSnapshot");
        }
    }
}
