using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DatabaseMigrations.MigrationService.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "Appointments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Appointments",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Scheduled");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_Status_StartsAt",
                table: "Appointments",
                columns: new[] { "Status", "StartsAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_Status_StartsAt",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Appointments");
        }
    }
}
