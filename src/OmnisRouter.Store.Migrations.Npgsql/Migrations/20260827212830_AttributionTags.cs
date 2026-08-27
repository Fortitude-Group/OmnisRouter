using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmnisRouter.Store.Migrations.Npgsql.Migrations
{
    /// <inheritdoc />
    public partial class AttributionTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TagBranch",
                table: "DecisionLogEntries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TagClientName",
                table: "DecisionLogEntries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TagCommit",
                table: "DecisionLogEntries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TagProject",
                table: "DecisionLogEntries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TagTeam",
                table: "DecisionLogEntries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TagBranch",
                table: "DecisionLogEntries");

            migrationBuilder.DropColumn(
                name: "TagClientName",
                table: "DecisionLogEntries");

            migrationBuilder.DropColumn(
                name: "TagCommit",
                table: "DecisionLogEntries");

            migrationBuilder.DropColumn(
                name: "TagProject",
                table: "DecisionLogEntries");

            migrationBuilder.DropColumn(
                name: "TagTeam",
                table: "DecisionLogEntries");
        }
    }
}
