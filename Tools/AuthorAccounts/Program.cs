using DiscoverJesus.Api.Data;
using DiscoverJesus.Api.Models;
using DiscoverJesus.Api.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

const string usage = """
    Usage: AuthorAccounts <expected-server> <expected-database>
             provision | disable <account-id> | grant <account-id> <role> | revoke <account-id> <role>
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

    if (args[2] == "provision")
    {
        // Make the identifier available for reconciliation if the commit acknowledgement is lost.
        var newAccountId = Guid.NewGuid();
        Console.WriteLine($"Provisioning account {newAccountId:D}.");
        await service.ProvisionAsync(newAccountId, actor, cancellation.Token);
        Console.WriteLine($"Provisioned {newAccountId:D} without authoring grants.");
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
