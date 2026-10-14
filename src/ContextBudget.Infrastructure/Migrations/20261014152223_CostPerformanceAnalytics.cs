using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContextBudget.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CostPerformanceAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PricingSnapshotJson",
                table: "ExperimentRuns",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'null'::jsonb");

            migrationBuilder.CreateTable(
                name: "PricingProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ModelId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    InputPricePerMillionTokens = table.Column<decimal>(type: "numeric(24,12)", precision: 24, scale: 12, nullable: false),
                    OutputPricePerMillionTokens = table.Column<decimal>(type: "numeric(24,12)", precision: 24, scale: 12, nullable: false),
                    CachedInputPricePerMillionTokens = table.Column<decimal>(type: "numeric(24,12)", precision: 24, scale: 12, nullable: true),
                    CacheWritePricePerMillionTokens = table.Column<decimal>(type: "numeric(24,12)", precision: 24, scale: 12, nullable: true),
                    EffectiveFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PricingMode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SourceDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricingProfiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PricingProfiles_Provider_ModelId_PricingMode_EffectiveFromU~",
                table: "PricingProfiles",
                columns: new[] { "Provider", "ModelId", "PricingMode", "EffectiveFromUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PricingProfiles");

            migrationBuilder.DropColumn(
                name: "PricingSnapshotJson",
                table: "ExperimentRuns");
        }
    }
}
