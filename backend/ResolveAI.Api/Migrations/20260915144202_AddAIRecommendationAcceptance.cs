using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResolveAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAIRecommendationAcceptance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AppliedAt",
                table: "IncidentAIAnalyses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AppliedByUserId",
                table: "IncidentAIAnalyses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CategoryApplied",
                table: "IncidentAIAnalyses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PriorityApplied",
                table: "IncidentAIAnalyses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_IncidentAIAnalyses_AppliedByUserId",
                table: "IncidentAIAnalyses",
                column: "AppliedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_IncidentAIAnalyses_Users_AppliedByUserId",
                table: "IncidentAIAnalyses",
                column: "AppliedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IncidentAIAnalyses_Users_AppliedByUserId",
                table: "IncidentAIAnalyses");

            migrationBuilder.DropIndex(
                name: "IX_IncidentAIAnalyses_AppliedByUserId",
                table: "IncidentAIAnalyses");

            migrationBuilder.DropColumn(
                name: "AppliedAt",
                table: "IncidentAIAnalyses");

            migrationBuilder.DropColumn(
                name: "AppliedByUserId",
                table: "IncidentAIAnalyses");

            migrationBuilder.DropColumn(
                name: "CategoryApplied",
                table: "IncidentAIAnalyses");

            migrationBuilder.DropColumn(
                name: "PriorityApplied",
                table: "IncidentAIAnalyses");
        }
    }
}
