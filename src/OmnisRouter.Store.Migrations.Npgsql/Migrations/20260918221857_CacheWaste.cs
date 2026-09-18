using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmnisRouter.Store.Migrations.Npgsql.Migrations
{
    /// <inheritdoc />
    public partial class CacheWaste : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CacheAvoidable",
                table: "DecisionLogEntries",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CacheCause",
                table: "DecisionLogEntries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CacheFixApplied",
                table: "DecisionLogEntries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CacheFxDate",
                table: "DecisionLogEntries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CachePricingVersion",
                table: "DecisionLogEntries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CacheRecomputedTokens",
                table: "DecisionLogEntries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CacheSavedGbp",
                table: "DecisionLogEntries",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CacheSavedTokens",
                table: "DecisionLogEntries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CacheShadowPrice",
                table: "DecisionLogEntries",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CacheUsdGbp",
                table: "DecisionLogEntries",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CacheWasteGbp",
                table: "DecisionLogEntries",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CacheAvoidable",
                table: "DecisionLogEntries");

            migrationBuilder.DropColumn(
                name: "CacheCause",
                table: "DecisionLogEntries");

            migrationBuilder.DropColumn(
                name: "CacheFixApplied",
                table: "DecisionLogEntries");

            migrationBuilder.DropColumn(
                name: "CacheFxDate",
                table: "DecisionLogEntries");

            migrationBuilder.DropColumn(
                name: "CachePricingVersion",
                table: "DecisionLogEntries");

            migrationBuilder.DropColumn(
                name: "CacheRecomputedTokens",
                table: "DecisionLogEntries");

            migrationBuilder.DropColumn(
                name: "CacheSavedGbp",
                table: "DecisionLogEntries");

            migrationBuilder.DropColumn(
                name: "CacheSavedTokens",
                table: "DecisionLogEntries");

            migrationBuilder.DropColumn(
                name: "CacheShadowPrice",
                table: "DecisionLogEntries");

            migrationBuilder.DropColumn(
                name: "CacheUsdGbp",
                table: "DecisionLogEntries");

            migrationBuilder.DropColumn(
                name: "CacheWasteGbp",
                table: "DecisionLogEntries");
        }
    }
}
