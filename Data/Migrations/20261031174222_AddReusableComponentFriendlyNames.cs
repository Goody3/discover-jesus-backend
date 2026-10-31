using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscoverJesus.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReusableComponentFriendlyNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "ReusableComponents",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "ReusableComponents",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.Sql(
                """
                ;WITH ComponentCandidates AS
                (
                    SELECT component.Id,
                        component.PublicId,
                        LEFT(
                            COALESCE(
                                NULLIF(LTRIM(RTRIM(JSON_VALUE(draft.ContentJson, '$.exampleName'))), N''),
                                NULLIF(LTRIM(RTRIM(JSON_VALUE(revision.ContentJson, '$.exampleName'))), N''),
                                CONCAT(N'Reusable component ', CONVERT(nvarchar(36), component.PublicId))),
                            200) AS CandidateName
                    FROM ReusableComponents AS component
                    OUTER APPLY
                    (
                        SELECT TOP (1) ContentJson
                        FROM ReusableComponentDrafts
                        WHERE DefinitionId = component.Id
                        ORDER BY UpdatedAt DESC
                    ) AS draft
                    OUTER APPLY
                    (
                        SELECT TOP (1) ContentJson
                        FROM ReusableComponentRevisions
                        WHERE DefinitionId = component.Id
                        ORDER BY PublishedAt DESC, RevisionNumber DESC
                    ) AS revision
                ),
                RankedCandidates AS
                (
                    SELECT Id,
                        PublicId,
                        CandidateName,
                        COUNT(*) OVER (PARTITION BY UPPER(CandidateName)) AS CandidateCount
                    FROM ComponentCandidates
                ),
                FinalNames AS
                (
                    SELECT Id,
                        LEFT(
                            CASE WHEN CandidateCount = 1
                                THEN CandidateName
                                ELSE CONCAT(N'Reusable component ', CONVERT(nvarchar(36), PublicId))
                            END,
                            200) AS ComponentName
                    FROM RankedCandidates
                )
                UPDATE component
                SET Name = final.ComponentName,
                    NormalizedName = UPPER(final.ComponentName)
                FROM ReusableComponents AS component
                INNER JOIN FinalNames AS final ON final.Id = component.Id;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ReusableComponents",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedName",
                table: "ReusableComponents",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReusableComponents_NormalizedName",
                table: "ReusableComponents",
                column: "NormalizedName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReusableComponents_NormalizedName",
                table: "ReusableComponents");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "ReusableComponents");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "ReusableComponents");
        }
    }
}
