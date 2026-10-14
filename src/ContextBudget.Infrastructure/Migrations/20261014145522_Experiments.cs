using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContextBudget.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Experiments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EvaluationDatasets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationDatasets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvaluationDatasets_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DatasetVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DatasetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CaseCount = table.Column<int>(type: "integer", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EvaluatorVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatasetVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DatasetVersions_EvaluationDatasets_DatasetId",
                        column: x => x.DatasetId,
                        principalTable: "EvaluationDatasets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Experiments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    DatasetVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Repeats = table.Column<int>(type: "integer", nullable: false),
                    BaselineContextHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OptimizedContextHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Temperature = table.Column<double>(type: "double precision", nullable: true),
                    MaxOutputTokens = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Experiments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Experiments_DatasetVersions_DatasetVersionId",
                        column: x => x.DatasetVersionId,
                        principalTable: "DatasetVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Experiments_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExperimentRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExperimentId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    QualityGate = table.Column<string>(type: "text", nullable: false),
                    SummaryJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExperimentRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExperimentRuns_Experiments_ExperimentId",
                        column: x => x.ExperimentId,
                        principalTable: "Experiments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExperimentCaseResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CaseName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Repeat = table.Column<int>(type: "integer", nullable: false),
                    Variant = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ProviderInputTokens = table.Column<long>(type: "bigint", nullable: true),
                    ProviderOutputTokens = table.Column<long>(type: "bigint", nullable: true),
                    CachedTokens = table.Column<long>(type: "bigint", nullable: true),
                    EstimatedInputTokens = table.Column<long>(type: "bigint", nullable: true),
                    EstimatedOutputTokens = table.Column<long>(type: "bigint", nullable: true),
                    LatencyMs = table.Column<long>(type: "bigint", nullable: false),
                    ChecksJson = table.Column<string>(type: "jsonb", nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExperimentCaseResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExperimentCaseResults_ExperimentRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "ExperimentRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DatasetVersions_DatasetId_Version",
                table: "DatasetVersions",
                columns: new[] { "DatasetId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationDatasets_ProjectId",
                table: "EvaluationDatasets",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ExperimentCaseResults_RunId_CaseId_Repeat_Variant",
                table: "ExperimentCaseResults",
                columns: new[] { "RunId", "CaseId", "Repeat", "Variant" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExperimentRuns_ExperimentId_IdempotencyKey",
                table: "ExperimentRuns",
                columns: new[] { "ExperimentId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExperimentRuns_ExperimentId_Status",
                table: "ExperimentRuns",
                columns: new[] { "ExperimentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Experiments_DatasetVersionId",
                table: "Experiments",
                column: "DatasetVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Experiments_ProjectId_CreatedAtUtc",
                table: "Experiments",
                columns: new[] { "ProjectId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExperimentCaseResults");

            migrationBuilder.DropTable(
                name: "ExperimentRuns");

            migrationBuilder.DropTable(
                name: "Experiments");

            migrationBuilder.DropTable(
                name: "DatasetVersions");

            migrationBuilder.DropTable(
                name: "EvaluationDatasets");
        }
    }
}
