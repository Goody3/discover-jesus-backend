using DiscoverJesus.Api.Data;
using DiscoverJesus.Api.Models;
using DiscoverJesus.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DiscoverJesus.Api.Tests;

public sealed class AuthorAccountServiceTests
{
    [Fact]
    public async Task LifecycleChangesPersistAndAuditOnlyActualChanges()
    {
        var options = new DbContextOptionsBuilder<DiscoverJesusDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new DiscoverJesusDbContext(options);
        await ExerciseLifecycleAsync(db, "test-operator");
    }

    [Fact]
    public async Task SqlServerMigrationPersistsLifecycleAndAudit()
    {
        await using var application = await SqlServerIntegrationTestApplication.StartAsync();
        await using var scope = application.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DiscoverJesusDbContext>();
        var actor = await db.Database.SqlQueryRaw<string>("SELECT ORIGINAL_LOGIN() AS [Value]").SingleAsync();
        await ExerciseLifecycleAsync(db, actor);
    }

    private static async Task ExerciseLifecycleAsync(DiscoverJesusDbContext db, string actor)
    {
        var id = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
        var service = new AuthorAccountService(db, new FixedClock(now));
        var ct = CancellationToken.None;

        await service.ProvisionAsync(id, actor, ct);
        Assert.False(await db.AuthorGrants.AnyAsync(grant => grant.AccountId == id));
        Assert.True(await service.SetGrantAsync(id, AuthorRole.DraftEditor, true, actor, ct));
        Assert.False(await service.SetGrantAsync(id, AuthorRole.DraftEditor, true, actor, ct));
        Assert.Equal(
            AuthorRole.DraftEditor,
            (await db.AuthorGrants.SingleAsync(grant => grant.AccountId == id)).Role);
        Assert.True(await service.SetGrantAsync(id, AuthorRole.Publisher, true, actor, ct));
        var passkeyId = Guid.NewGuid();
        db.AuthorPasskeys.Add(new AuthorPasskey
        {
            Id = passkeyId,
            AccountId = id,
            CredentialIdHash = new string('A', 64),
            CredentialId = [1, 2, 3],
            PublicKey = [4, 5, 6],
            Name = "test",
            CreatedAt = now
        });
        db.AuthorWebSessions.Add(new AuthorWebSession
        {
            Id = Guid.NewGuid(),
            AccountId = id,
            CredentialId = passkeyId,
            SessionTokenHash = new string('B', 64),
            CsrfTokenHash = new string('C', 64),
            CreatedAt = now,
            LastActivityAt = now,
            ExpiresAt = now.AddHours(8)
        });
        await db.SaveChangesAsync(ct);
        Assert.True(await service.SetGrantAsync(id, AuthorRole.DraftEditor, false, actor, ct));
        Assert.False(await service.SetGrantAsync(id, AuthorRole.DraftEditor, false, actor, ct));
        Assert.True(await service.DisableAsync(id, actor, ct));
        Assert.False(await service.DisableAsync(id, actor, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetGrantAsync(id, AuthorRole.DraftEditor, true, actor, ct));
        Assert.True(await service.SetGrantAsync(id, AuthorRole.Publisher, false, actor, ct));

        db.ChangeTracker.Clear();
        var account = await db.AuthorAccounts.SingleAsync(item => item.Id == id);
        Assert.Equal(id, account.Id);
        Assert.Equal(now, account.CreatedAt);
        Assert.Equal(now, account.DisabledAt);
        Assert.False(await db.AuthorGrants.AnyAsync(grant => grant.AccountId == id));
        Assert.Equal(
            now,
            (await db.AuthorWebSessions.SingleAsync(session => session.AccountId == id)).RevokedAt);
        var audit = await db.AuthorAccountAudits
            .Where(entry => entry.AccountId == id)
            .OrderBy(entry => entry.Id)
            .ToListAsync();
        Assert.Equal(
            new (AuthorAccountAction, AuthorRole?)[]
            {
                (AuthorAccountAction.Provisioned, null),
                (AuthorAccountAction.Granted, AuthorRole.DraftEditor),
                (AuthorAccountAction.Granted, AuthorRole.Publisher),
                (AuthorAccountAction.Revoked, AuthorRole.DraftEditor),
                (AuthorAccountAction.Disabled, null),
                (AuthorAccountAction.Revoked, AuthorRole.Publisher),
            },
            audit.Select(entry => (entry.Action, entry.Role)));
        Assert.All(audit, entry =>
        {
            Assert.Equal(id, entry.AccountId);
            Assert.Equal(actor, entry.Actor);
            Assert.Equal(now, entry.OccurredAt);
        });
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
