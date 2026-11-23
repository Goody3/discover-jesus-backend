using DiscoverJesus.Api.Data;
using DiscoverJesus.Api.Models;
using DiscoverJesus.Api.Services;
using Fido2NetLib;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

const string usage = """
    Usage: AuthorAccounts <expected-server> <expected-database>
             provision | disable <account-id> | grant <account-id> <role> | revoke <account-id> <role>
             invite <account-id> | list-passkeys <account-id> | revoke-passkey <account-id> <passkey-id>
    Roles: DraftEditor, Publisher. Set AuthorAccounts__ConnectionString separately.
    """;

if (args is ["--help"])
{
    Console.WriteLine(usage);
    return 0;
}

var accountId = Guid.Empty;
var role = default(AuthorRole);
if (args.Length < 3 || (args[2] switch
    {
        "provision" => args.Length != 3,
        "disable" => args.Length != 4 || !Guid.TryParse(args[3], out accountId) || accountId == Guid.Empty,
        "grant" or "revoke" => args.Length != 5 || !Guid.TryParse(args[3], out accountId)
            || accountId == Guid.Empty || !Enum.TryParse(args[4], out role)
            || !Enum.IsDefined(role) || role.ToString() != args[4],
        "invite" or "list-passkeys" => args.Length != 4
            || !Guid.TryParse(args[3], out accountId) || accountId == Guid.Empty,
        "revoke-passkey" => args.Length != 5
            || !Guid.TryParse(args[3], out accountId) || accountId == Guid.Empty
            || !Guid.TryParse(args[4], out var parsedPasskeyId) || parsedPasskeyId == Guid.Empty,
        _ => true,
    }))
{
    Console.Error.WriteLine(usage);
    return 2;
}

var connectionString = Environment.GetEnvironmentVariable("AuthorAccounts__ConnectionString");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Set AuthorAccounts__ConnectionString to a named operator connection.");
    return 2;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

try
{
    var target = new SqlConnectionStringBuilder(connectionString);
    if (string.IsNullOrWhiteSpace(target.DataSource) || string.IsNullOrWhiteSpace(target.InitialCatalog)
        || !string.Equals(target.DataSource, args[0], StringComparison.OrdinalIgnoreCase)
        || !string.Equals(target.InitialCatalog, args[1], StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("Expected server and database must match the configured connection target.");
        return 2;
    }

    await using var db = new DiscoverJesusDbContext(
        new DbContextOptionsBuilder<DiscoverJesusDbContext>().UseSqlServer(target.ConnectionString).Options);
    await db.Database.OpenConnectionAsync(cancellation.Token);
    var actor = await db.Database.SqlQueryRaw<string>("SELECT ORIGINAL_LOGIN() AS [Value]")
        .SingleAsync(cancellation.Token);
    var service = new AuthorAccountService(db, TimeProvider.System);
    var passkeyService = new PasskeyAuthenticationService(
        db,
        new Fido2(new Fido2Configuration
        {
            RPID = "localhost",
            RPName = "Discover Jesus Content Studio",
            Origins = new HashSet<string> { "http://localhost:5173" }
        }),
        TimeProvider.System);

    if (args[2] == "provision")
    {
        // Make the identifier available for reconciliation if the commit acknowledgement is lost.
        var newAccountId = Guid.NewGuid();
        Console.WriteLine($"Provisioning account {newAccountId:D}.");
        await service.ProvisionAsync(newAccountId, actor, cancellation.Token);
        Console.WriteLine($"Provisioned {newAccountId:D} without authoring grants.");
    }
    else if (args[2] == "invite")
    {
        var token = await passkeyService.IssueInvitationAsync(accountId, actor, cancellation.Token);
        Console.WriteLine($"One-time enrollment invitation (expires in 24 hours): {token}");
        Console.WriteLine("Deliver this token out of band. It cannot be retrieved again.");
    }
    else if (args[2] == "list-passkeys")
    {
        var passkeys = await passkeyService.GetPasskeysForOperatorAsync(accountId, cancellation.Token);
        foreach (var passkey in passkeys)
        {
            Console.WriteLine(
                $"{passkey.Id:D}\t{passkey.Name}\t{passkey.CreatedAt:O}\t" +
                $"{(passkey.LastUsedAt is DateTimeOffset lastUsed ? lastUsed.ToString("O") : "never used")}\t" +
                $"{(passkey.RevokedAt is null ? "active" : $"revoked at {passkey.RevokedAt:O}")}");
        }
    }
    else if (args[2] == "revoke-passkey")
    {
        var passkeyId = Guid.Parse(args[4]);
        var changed = await passkeyService.RevokePasskeyForOperatorAsync(
            accountId,
            passkeyId,
            actor,
            cancellation.Token);
        Console.WriteLine(changed ? "Passkey revoked." : "No active passkey matched the account and ID.");
    }
    else
    {
        var changed = args[2] == "disable"
            ? await service.DisableAsync(accountId, actor, cancellation.Token)
            : await service.SetGrantAsync(accountId, role, args[2] == "grant", actor, cancellation.Token);
        Console.WriteLine(changed ? "Change recorded." : "No change; the requested state already holds.");
    }

    return 0;
}
catch (Exception error) when (error is KeyNotFoundException or ArgumentException or InvalidOperationException)
{
    Console.Error.WriteLine(error is KeyNotFoundException
        ? "Author account not found."
        : "Invalid operator configuration or account operation. Check the target, actor, and account state.");
    return 2;
}
catch (DbUpdateConcurrencyException)
{
    Console.Error.WriteLine("Account changed concurrently. Review its current state before retrying.");
    return 1;
}
catch (Exception error) when (error is DbUpdateException or SqlException or OperationCanceledException)
{
    var sqlNumber = (error as SqlException ?? error.InnerException as SqlException)?.Number;
    Console.Error.WriteLine($"Account operation did not confirm success{(sqlNumber is int n ? $" (SQL error {n})" : "")}. Check the account and audit records before retrying.");
    return 1;
}
