using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResolveAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentAuditTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IncidentNumber",
                table: "Notifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IncidentTitle",
                table: "Notifications",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IncidentAuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorDisplayName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ActorType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Summary = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    OldValue = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    NewValue = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    Metadata = table.Column<string>(type: "text", nullable: true),
                    DeduplicationKey = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentAuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncidentAuditEvents_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IncidentAuditEvents_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IncidentAuditEvents_ActorUserId",
                table: "IncidentAuditEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentAuditEvents_DeduplicationKey",
                table: "IncidentAuditEvents",
                column: "DeduplicationKey",
                unique: true,
                filter: "\"DeduplicationKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentAuditEvents_IncidentId_CreatedAt",
                table: "IncidentAuditEvents",
                columns: new[] { "IncidentId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IncidentAuditEvents");

            migrationBuilder.DropColumn(
                name: "IncidentNumber",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "IncidentTitle",
                table: "Notifications");
        }
    }
}
