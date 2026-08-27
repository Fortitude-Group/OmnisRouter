using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmnisRouter.Store.Migrations.Npgsql.Migrations
{
    /// <inheritdoc />
    public partial class VigilPushCursor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VigilPushCursors",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    LastPushedId = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VigilPushCursors", x => x.TenantId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VigilPushCursors");
        }
    }
}
