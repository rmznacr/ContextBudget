using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContextBudget.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AnalysisForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Analyses_TraceRecordId",
                table: "Analyses",
                column: "TraceRecordId");

            migrationBuilder.AddForeignKey(
                name: "FK_Analyses_Projects_ProjectId",
                table: "Analyses",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Analyses_Traces_TraceRecordId",
                table: "Analyses",
                column: "TraceRecordId",
                principalTable: "Traces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Analyses_Projects_ProjectId",
                table: "Analyses");

            migrationBuilder.DropForeignKey(
                name: "FK_Analyses_Traces_TraceRecordId",
                table: "Analyses");

            migrationBuilder.DropIndex(
                name: "IX_Analyses_TraceRecordId",
                table: "Analyses");
        }
    }
}
