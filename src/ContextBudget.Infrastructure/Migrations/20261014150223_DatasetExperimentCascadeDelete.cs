using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContextBudget.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DatasetExperimentCascadeDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Experiments_DatasetVersions_DatasetVersionId",
                table: "Experiments");

            migrationBuilder.AddForeignKey(
                name: "FK_Experiments_DatasetVersions_DatasetVersionId",
                table: "Experiments",
                column: "DatasetVersionId",
                principalTable: "DatasetVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Experiments_DatasetVersions_DatasetVersionId",
                table: "Experiments");

            migrationBuilder.AddForeignKey(
                name: "FK_Experiments_DatasetVersions_DatasetVersionId",
                table: "Experiments",
                column: "DatasetVersionId",
                principalTable: "DatasetVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
