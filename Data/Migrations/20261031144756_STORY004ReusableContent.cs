using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscoverJesus.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class STORY004ReusableContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PublicId",
                table: "Pages",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWSEQUENTIALID()");

            migrationBuilder.CreateTable(
                name: "PageContentDrafts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PageId = table.Column<int>(type: "int", nullable: false),
                    Locale = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: false),
                    SectionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageContentDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PageContentDrafts_Pages_PageId",
                        column: x => x.PageId,
                        principalTable: "Pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PageContentRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PageId = table.Column<int>(type: "int", nullable: false),
                    Locale = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: false),
                    RevisionNumber = table.Column<int>(type: "int", nullable: false),
                    SectionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageContentRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PageContentRevisions_Pages_PageId",
                        column: x => x.PageId,
                        principalTable: "Pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReusableComponents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    IsWithdrawn = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReusableComponents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PageComponentReferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PageId = table.Column<int>(type: "int", nullable: false),
                    ComponentPublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PageContentDraftId = table.Column<int>(type: "int", nullable: true),
                    PageContentRevisionId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageComponentReferences", x => x.Id);
                    table.CheckConstraint("CK_PageComponentReferences_ContentVersion", "([PageContentDraftId] IS NULL AND [PageContentRevisionId] IS NOT NULL) OR ([PageContentDraftId] IS NOT NULL AND [PageContentRevisionId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_PageComponentReferences_PageContentDrafts_PageContentDraftId",
                        column: x => x.PageContentDraftId,
                        principalTable: "PageContentDrafts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PageComponentReferences_PageContentRevisions_PageContentRevisionId",
                        column: x => x.PageContentRevisionId,
                        principalTable: "PageContentRevisions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PageComponentReferences_Pages_PageId",
                        column: x => x.PageId,
                        principalTable: "Pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReusableComponentDrafts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DefinitionId = table.Column<int>(type: "int", nullable: false),
                    Locale = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReusableComponentDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReusableComponentDrafts_ReusableComponents_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "ReusableComponents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReusableComponentRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DefinitionId = table.Column<int>(type: "int", nullable: false),
                    Locale = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: false),
                    RevisionNumber = table.Column<int>(type: "int", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReusableComponentRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReusableComponentRevisions_ReusableComponents_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "ReusableComponents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pages_PublicId",
                table: "Pages",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PageComponentReferences_ComponentPublicId",
                table: "PageComponentReferences",
                column: "ComponentPublicId");

            migrationBuilder.CreateIndex(
                name: "IX_PageComponentReferences_PageContentDraftId",
                table: "PageComponentReferences",
                column: "PageContentDraftId");

            migrationBuilder.CreateIndex(
                name: "IX_PageComponentReferences_PageContentRevisionId",
                table: "PageComponentReferences",
                column: "PageContentRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_PageComponentReferences_PageId",
                table: "PageComponentReferences",
                column: "PageId");

            migrationBuilder.CreateIndex(
                name: "IX_PageContentDrafts_PageId_Locale",
                table: "PageContentDrafts",
                columns: new[] { "PageId", "Locale" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PageContentRevisions_PageId_Locale_RevisionNumber",
                table: "PageContentRevisions",
                columns: new[] { "PageId", "Locale", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReusableComponentDrafts_DefinitionId_Locale",
                table: "ReusableComponentDrafts",
                columns: new[] { "DefinitionId", "Locale" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReusableComponentRevisions_DefinitionId_Locale_RevisionNumber",
                table: "ReusableComponentRevisions",
                columns: new[] { "DefinitionId", "Locale", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReusableComponents_PublicId",
                table: "ReusableComponents",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PageComponentReferences");

            migrationBuilder.DropTable(
                name: "ReusableComponentDrafts");

            migrationBuilder.DropTable(
                name: "ReusableComponentRevisions");

            migrationBuilder.DropTable(
                name: "PageContentDrafts");

            migrationBuilder.DropTable(
                name: "PageContentRevisions");

            migrationBuilder.DropTable(
                name: "ReusableComponents");

            migrationBuilder.DropIndex(
                name: "IX_Pages_PublicId",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "Pages");
        }
    }
}
