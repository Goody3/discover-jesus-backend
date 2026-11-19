using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscoverJesus.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuthorAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DisabledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuthorAccountAudits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorAccountAudits", x => x.Id);
                    table.CheckConstraint("CK_AuthorAccountAudits_ActionRole", "([Action] IN (1, 4) AND [Role] IS NULL) OR ([Action] IN (2, 3) AND [Role] IS NOT NULL AND [Role] IN (1, 2))");
                    table.ForeignKey(
                        name: "FK_AuthorAccountAudits_AuthorAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "AuthorAccounts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AuthorGrants",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorGrants", x => new { x.AccountId, x.Role });
                    table.CheckConstraint("CK_AuthorGrants_Role", "[Role] IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_AuthorGrants_AuthorAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "AuthorAccounts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthorAccountAudits_AccountId",
                table: "AuthorAccountAudits",
                column: "AccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthorAccountAudits");

            migrationBuilder.DropTable(
                name: "AuthorGrants");

            migrationBuilder.DropTable(
                name: "AuthorAccounts");
        }
    }
}
