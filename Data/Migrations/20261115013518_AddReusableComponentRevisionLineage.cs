using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscoverJesus.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReusableComponentRevisionLineage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReusableComponentRevisionLineages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceRevisionId = table.Column<int>(type: "int", nullable: false),
                    MigratedRevisionId = table.Column<int>(type: "int", nullable: false),
                    MigrationVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MigrationBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MigratedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReusableComponentRevisionLineages", x => x.Id);
                    table.CheckConstraint("CK_ReusableComponentRevisionLineages_DistinctRevisions", "[SourceRevisionId] <> [MigratedRevisionId]");
                    table.ForeignKey(
                        name: "FK_ReusableComponentRevisionLineages_ReusableComponentRevisions_MigratedRevisionId",
                        column: x => x.MigratedRevisionId,
                        principalTable: "ReusableComponentRevisions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReusableComponentRevisionLineages_ReusableComponentRevisions_SourceRevisionId",
                        column: x => x.SourceRevisionId,
                        principalTable: "ReusableComponentRevisions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReusableComponentRevisionLineages_MigratedRevisionId",
                table: "ReusableComponentRevisionLineages",
                column: "MigratedRevisionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReusableComponentRevisionLineages_MigrationBatchId",
                table: "ReusableComponentRevisionLineages",
                column: "MigrationBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ReusableComponentRevisionLineages_SourceRevisionId_MigrationVersion",
                table: "ReusableComponentRevisionLineages",
                columns: new[] { "SourceRevisionId", "MigrationVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReusableComponentRevisionLineages");
        }
    }
}
