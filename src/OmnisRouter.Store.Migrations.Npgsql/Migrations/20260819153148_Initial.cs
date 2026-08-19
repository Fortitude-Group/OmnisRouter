using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmnisRouter.Store.Migrations.Npgsql.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DecisionLogEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SessionId = table.Column<string>(type: "text", nullable: true),
                    RequestHash = table.Column<string>(type: "text", nullable: false),
                    ClientFormat = table.Column<int>(type: "integer", nullable: false),
                    ClusterId = table.Column<int>(type: "integer", nullable: false),
                    ChosenProvider = table.Column<int>(type: "integer", nullable: false),
                    ChosenModelId = table.Column<string>(type: "text", nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    Top1Sim = table.Column<double>(type: "double precision", nullable: false),
                    Top2Sim = table.Column<double>(type: "double precision", nullable: false),
                    Margin = table.Column<double>(type: "double precision", nullable: false),
                    Decision = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<int>(type: "integer", nullable: false),
                    PolicyVersion = table.Column<string>(type: "text", nullable: false),
                    EstCostUsd = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    EstCostDeltaVsBigUsd = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    SessionPinApplied = table.Column<bool>(type: "boolean", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    LatencyMs = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DecisionLogEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Installs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Settings = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Installs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProviderKeys",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: false),
                    ApiKeyEncrypted = table.Column<byte[]>(type: "bytea", nullable: false),
                    KeyVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RouterTokens",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    HashedToken = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouterTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SessionPins",
                columns: table => new
                {
                    SessionKey = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    PinnedProvider = table.Column<int>(type: "integer", nullable: false),
                    PinnedModelId = table.Column<string>(type: "text", nullable: false),
                    ClusterId = table.Column<int>(type: "integer", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionPins", x => x.SessionKey);
                });

            migrationBuilder.CreateTable(
                name: "Usages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    ModelId = table.Column<string>(type: "text", nullable: false),
                    Requests = table.Column<long>(type: "bigint", nullable: false),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    CostUsd = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    CostVsBigUsd = table.Column<decimal>(type: "numeric(18,8)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DecisionLogEntries_TenantId",
                table: "DecisionLogEntries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DecisionLogEntries_Timestamp",
                table: "DecisionLogEntries",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_Installs_TenantId",
                table: "Installs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderKeys_TenantId_Provider",
                table: "ProviderKeys",
                columns: new[] { "TenantId", "Provider" });

            migrationBuilder.CreateIndex(
                name: "IX_RouterTokens_HashedToken",
                table: "RouterTokens",
                column: "HashedToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RouterTokens_TenantId",
                table: "RouterTokens",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionPins_ExpiresAt",
                table: "SessionPins",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_SessionPins_TenantId",
                table: "SessionPins",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Usages_TenantId_Date_Provider_ModelId",
                table: "Usages",
                columns: new[] { "TenantId", "Date", "Provider", "ModelId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DecisionLogEntries");

            migrationBuilder.DropTable(
                name: "Installs");

            migrationBuilder.DropTable(
                name: "ProviderKeys");

            migrationBuilder.DropTable(
                name: "RouterTokens");

            migrationBuilder.DropTable(
                name: "SessionPins");

            migrationBuilder.DropTable(
                name: "Usages");
        }
    }
}
