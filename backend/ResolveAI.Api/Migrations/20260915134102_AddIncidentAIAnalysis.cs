using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResolveAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentAIAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IncidentAIAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Model = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CategoryRecommendation = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PriorityRecommendation = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Urgency = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    PossibleCause = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ReasoningSummary = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false),
                    SuggestedActionsJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PromptVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentAIAnalyses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncidentAIAnalyses_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IncidentAIAnalyses_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IncidentAIAnalyses_IncidentId_CreatedAt",
                table: "IncidentAIAnalyses",
                columns: new[] { "IncidentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IncidentAIAnalyses_RequestedByUserId",
                table: "IncidentAIAnalyses",
                column: "RequestedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IncidentAIAnalyses");
        }
    }
}
