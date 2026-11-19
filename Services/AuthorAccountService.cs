using DiscoverJesus.Api.Data;
using DiscoverJesus.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DiscoverJesus.Api.Services;

public sealed class AuthorAccountService(DiscoverJesusDbContext db, TimeProvider clock)
{
    public async Task ProvisionAsync(Guid accountId, string actor, CancellationToken cancellationToken)
    {
        ValidateActor(actor);
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID must not be empty.", nameof(accountId));
        var now = clock.GetUtcNow();
        var account = new AuthorAccount { Id = accountId, CreatedAt = now, Version = Guid.NewGuid() };
        db.AuthorAccounts.Add(account);
        AddAudit(account.Id, actor, AuthorAccountAction.Provisioned, null, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DisableAsync(Guid accountId, string actor, CancellationToken cancellationToken)
    {
        ValidateActor(actor);
        var account = await GetAccountAsync(accountId, cancellationToken);
        if (account.DisabledAt is not null)
            return false;

        var now = clock.GetUtcNow();
        account.DisabledAt = now;
        await SaveChangeAsync(account, actor, AuthorAccountAction.Disabled, null, now, cancellationToken);
        return true;
    }

    public async Task<bool> SetGrantAsync(
        Guid accountId, AuthorRole role, bool granted, string actor, CancellationToken cancellationToken)
    {
        ValidateActor(actor);
        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role));

        var account = await GetAccountAsync(accountId, cancellationToken);
        if (granted && account.DisabledAt is not null)
            throw new InvalidOperationException("Cannot grant a role to a disabled account.");

        var existing = await db.AuthorGrants.SingleOrDefaultAsync(
            grant => grant.AccountId == accountId && grant.Role == role, cancellationToken);
        if (granted == (existing is not null))
            return false;

        if (existing is null)
            db.AuthorGrants.Add(new AuthorGrant { AccountId = accountId, Role = role });
        else
            db.AuthorGrants.Remove(existing);

        await SaveChangeAsync(account, actor, granted ? AuthorAccountAction.Granted : AuthorAccountAction.Revoked,
            role, clock.GetUtcNow(), cancellationToken);
        return true;
    }

    private async Task<AuthorAccount> GetAccountAsync(Guid id, CancellationToken cancellationToken) =>
        await db.AuthorAccounts.SingleOrDefaultAsync(account => account.Id == id, cancellationToken)
        ?? throw new KeyNotFoundException("Author account was not found.");

    private async Task SaveChangeAsync(
        AuthorAccount account, string actor, AuthorAccountAction action, AuthorRole? role,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Updating the account version serializes changes to its separate grants with disablement.
        account.Version = Guid.NewGuid();
        AddAudit(account.Id, actor, action, role, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    private void AddAudit(Guid id, string actor, AuthorAccountAction action, AuthorRole? role, DateTimeOffset now) =>
        db.AuthorAccountAudits.Add(new AuthorAccountAudit
        {
            AccountId = id, Actor = actor, Action = action, Role = role, OccurredAt = now,
        });

    private static void ValidateActor(string actor)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 256)
            throw new ArgumentException("A trusted actor identifier of at most 256 characters is required.", nameof(actor));
    }
}
