using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VolumeMount.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialInspections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Inspections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssetTag = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AssetName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Site = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Inspector = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    InspectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Inspections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Attachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InspectionId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    BlobName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Attachments_Inspections_InspectionId",
                        column: x => x.InspectionId,
                        principalTable: "Inspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_InspectionId",
                table: "Attachments",
                column: "InspectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Inspections_AssetTag",
                table: "Inspections",
                column: "AssetTag");

            migrationBuilder.CreateIndex(
                name: "IX_Inspections_InspectedAt",
                table: "Inspections",
                column: "InspectedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Attachments");

            migrationBuilder.DropTable(
                name: "Inspections");
        }
    }
}
