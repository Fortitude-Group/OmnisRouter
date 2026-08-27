using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmnisRouter.Store.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class ActualCostAndUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ActualCacheCreationTokens",
                table: "DecisionLogEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ActualCacheReadTokens",
                table: "DecisionLogEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ActualCostDeltaVsBigUsd",
                table: "DecisionLogEntries",
                type: "decimal(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ActualCostUsd",
                table: "DecisionLogEntries",
                type: "decimal(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ActualInputTokens",
                table: "DecisionLogEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ActualOutputTokens",
                table: "DecisionLogEntries",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActualCacheCreationTokens",
                table: "DecisionLogEntries");

            migrationBuilder.DropColumn(
                name: "ActualCacheReadTokens",
                table: "DecisionLogEntries");

            migrationBuilder.DropColumn(
                name: "ActualCostDeltaVsBigUsd",
                table: "DecisionLogEntries");

            migrationBuilder.DropColumn(
                name: "ActualCostUsd",
                table: "DecisionLogEntries");

            migrationBuilder.DropColumn(
                name: "ActualInputTokens",
                table: "DecisionLogEntries");

            migrationBuilder.DropColumn(
                name: "ActualOutputTokens",
                table: "DecisionLogEntries");
        }
    }
}
