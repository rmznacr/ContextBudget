using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContextBudget.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SegmentSafeMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceId",
                table: "TraceSegments",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ToolCallId",
                table: "TraceSegments",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "TraceSegments");

            migrationBuilder.DropColumn(
                name: "ToolCallId",
                table: "TraceSegments");
        }
    }
}
