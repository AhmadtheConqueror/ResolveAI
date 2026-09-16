using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ResolveAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class SeedDepartments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Departments",
                columns: new[] { "Id", "CreatedAt", "Name" },
                values: new object[,]
                {
                    { new Guid("cccccccc-cccc-cccc-cccc-cccccccccc01"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "IT" },
                    { new Guid("cccccccc-cccc-cccc-cccc-cccccccccc02"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Finance" },
                    { new Guid("cccccccc-cccc-cccc-cccc-cccccccccc03"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "HR" },
                    { new Guid("cccccccc-cccc-cccc-cccc-cccccccccc04"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Operations" },
                    { new Guid("cccccccc-cccc-cccc-cccc-cccccccccc05"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Administration" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: new Guid("cccccccc-cccc-cccc-cccc-cccccccccc01"));

            migrationBuilder.DeleteData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: new Guid("cccccccc-cccc-cccc-cccc-cccccccccc02"));

            migrationBuilder.DeleteData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: new Guid("cccccccc-cccc-cccc-cccc-cccccccccc03"));

            migrationBuilder.DeleteData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: new Guid("cccccccc-cccc-cccc-cccc-cccccccccc04"));

            migrationBuilder.DeleteData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: new Guid("cccccccc-cccc-cccc-cccc-cccccccccc05"));
        }
    }
}
