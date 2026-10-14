using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContextBudget.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TraceCostAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PricingSnapshotJson",
                table: "Traces",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'null'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PricingSnapshotJson",
                table: "Traces");
        }
    }
}
