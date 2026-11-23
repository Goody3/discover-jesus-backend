using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscoverJesus.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorPasskeyAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AuthorAccountAudits_ActionRole",
                table: "AuthorAccountAudits");

            migrationBuilder.CreateTable(
                name: "AuthorPasskeyInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "char(64)", unicode: false, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorPasskeyInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthorPasskeyInvitations_AuthorAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "AuthorAccounts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AuthorPasskeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CredentialIdHash = table.Column<string>(type: "char(64)", unicode: false, nullable: false),
                    CredentialId = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    PublicKey = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    SignatureCounter = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorPasskeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthorPasskeys_AuthorAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "AuthorAccounts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EditorialAuditRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    ResourceType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResourceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EditorialAuditRecords", x => x.Id);
                    table.CheckConstraint("CK_EditorialAuditRecords_Action", "[Action] IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_EditorialAuditRecords_AuthorAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "AuthorAccounts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AuthorWebSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CredentialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionTokenHash = table.Column<string>(type: "char(64)", unicode: false, nullable: false),
                    CsrfTokenHash = table.Column<string>(type: "char(64)", unicode: false, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorWebSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthorWebSessions_AuthorAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "AuthorAccounts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AuthorWebSessions_AuthorPasskeys_CredentialId",
                        column: x => x.CredentialId,
                        principalTable: "AuthorPasskeys",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AuthorWebAuthnChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InvitationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OptionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorWebAuthnChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthorWebAuthnChallenges_AuthorAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "AuthorAccounts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AuthorWebAuthnChallenges_AuthorPasskeyInvitations_InvitationId",
                        column: x => x.InvitationId,
                        principalTable: "AuthorPasskeyInvitations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AuthorWebAuthnChallenges_AuthorWebSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "AuthorWebSessions",
                        principalColumn: "Id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuthorAccountAudits_ActionRole",
                table: "AuthorAccountAudits",
                sql: "([Action] IN (1, 4, 5, 6, 7, 8) AND [Role] IS NULL) OR ([Action] IN (2, 3) AND [Role] IS NOT NULL AND [Role] IN (1, 2))");

            migrationBuilder.CreateIndex(
                name: "IX_AuthorPasskeyInvitations_AccountId_ExpiresAt",
                table: "AuthorPasskeyInvitations",
                columns: new[] { "AccountId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthorPasskeyInvitations_TokenHash",
                table: "AuthorPasskeyInvitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthorPasskeys_AccountId_RevokedAt",
                table: "AuthorPasskeys",
                columns: new[] { "AccountId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthorPasskeys_CredentialIdHash",
                table: "AuthorPasskeys",
                column: "CredentialIdHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthorWebAuthnChallenges_AccountId",
                table: "AuthorWebAuthnChallenges",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthorWebAuthnChallenges_InvitationId",
                table: "AuthorWebAuthnChallenges",
                column: "InvitationId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthorWebAuthnChallenges_Purpose_ExpiresAt",
                table: "AuthorWebAuthnChallenges",
                columns: new[] { "Purpose", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthorWebAuthnChallenges_SessionId",
                table: "AuthorWebAuthnChallenges",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthorWebSessions_AccountId_RevokedAt_ExpiresAt",
                table: "AuthorWebSessions",
                columns: new[] { "AccountId", "RevokedAt", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthorWebSessions_CredentialId",
                table: "AuthorWebSessions",
                column: "CredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthorWebSessions_SessionTokenHash",
                table: "AuthorWebSessions",
                column: "SessionTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EditorialAuditRecords_AccountId_OccurredAt",
                table: "EditorialAuditRecords",
                columns: new[] { "AccountId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthorWebAuthnChallenges");

            migrationBuilder.DropTable(
                name: "EditorialAuditRecords");

            migrationBuilder.DropTable(
                name: "AuthorPasskeyInvitations");

            migrationBuilder.DropTable(
                name: "AuthorWebSessions");

            migrationBuilder.DropTable(
                name: "AuthorPasskeys");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuthorAccountAudits_ActionRole",
                table: "AuthorAccountAudits");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuthorAccountAudits_ActionRole",
                table: "AuthorAccountAudits",
                sql: "([Action] IN (1, 4) AND [Role] IS NULL) OR ([Action] IN (2, 3) AND [Role] IS NOT NULL AND [Role] IN (1, 2))");
        }
    }
}
