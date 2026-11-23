using DiscoverJesus.Api.Data;
using DiscoverJesus.Api.Models;
using DiscoverJesus.Api.Services;
using Fido2NetLib;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DiscoverJesus.Api.Tests;

public sealed class PasskeyAuthenticationServiceTests
{
    [Fact]
    public async Task InvitationsAreHashedOneTimeAndBoundToAnActiveGrantedAccount()
    {
        var options = new DbContextOptionsBuilder<DiscoverJesusDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new DiscoverJesusDbContext(options);
        var accountId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        db.AuthorAccounts.Add(new AuthorAccount
        {
            Id = accountId,
            CreatedAt = now,
            Version = Guid.NewGuid()
        });
        db.AuthorGrants.Add(new AuthorGrant
        {
            AccountId = accountId,
            Role = AuthorRole.DraftEditor
        });
        await db.SaveChangesAsync();

        var service = new PasskeyAuthenticationService(
            db,
            new Fido2(new Fido2Configuration
            {
                RPID = "localhost",
                RPName = "Discover Jesus Content Studio",
                Origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "http://localhost:5173"
                }
            }),
            new FixedClock(now));

        var token = await service.IssueInvitationAsync(accountId, "test-operator", CancellationToken.None);
        var invitation = await db.AuthorPasskeyInvitations.SingleAsync();
        Assert.NotEqual(token, invitation.TokenHash);
        Assert.Equal(AuthorAuthenticationPrimitives.HashToken(token), invitation.TokenHash);
        Assert.Equal(now.AddHours(24), invitation.ExpiresAt);

        var response = await service.CreateInvitationRegistrationOptionsAsync(token, CancellationToken.None);
        Assert.NotEqual(Guid.Empty, response.ChallengeId);
        Assert.NotNull(response.Options);
        Assert.Equal(
            AuthorWebAuthnChallengePurpose.InvitationRegistration,
            (await db.AuthorWebAuthnChallenges.SingleAsync()).Purpose);

        var replacement = await service.IssueInvitationAsync(accountId, "test-operator", CancellationToken.None);
        Assert.NotEqual(token, replacement);
        Assert.NotNull((await db.AuthorPasskeyInvitations.SingleAsync(item => item.TokenHash == invitation.TokenHash)).RevokedAt);
        Assert.Equal(
            new[] { AuthorAccountAction.InvitationIssued, AuthorAccountAction.InvitationRevoked, AuthorAccountAction.InvitationIssued },
            await db.AuthorAccountAudits.OrderBy(record => record.Id).Select(record => record.Action).ToArrayAsync());
    }

    [Fact]
    public async Task InvitationCannotBeIssuedForAnAccountWithoutAnActiveGrant()
    {
        var options = new DbContextOptionsBuilder<DiscoverJesusDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new DiscoverJesusDbContext(options);
        var accountId = Guid.NewGuid();
        db.AuthorAccounts.Add(new AuthorAccount
        {
            Id = accountId,
            CreatedAt = DateTimeOffset.UtcNow,
            Version = Guid.NewGuid()
        });
        await db.SaveChangesAsync();
        var service = CreateService(db, TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.IssueInvitationAsync(accountId, "test-operator", CancellationToken.None));
        Assert.Empty(await db.AuthorPasskeyInvitations.ToListAsync());
    }

    private static PasskeyAuthenticationService CreateService(DiscoverJesusDbContext db, TimeProvider clock) =>
        new(
            db,
            new Fido2(new Fido2Configuration
            {
                RPID = "localhost",
                RPName = "Discover Jesus Content Studio",
                Origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "http://localhost:5173"
                }
            }),
            clock);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
