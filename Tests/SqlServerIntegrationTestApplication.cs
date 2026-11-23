using DiscoverJesus.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DiscoverJesus.Api.Models;

namespace DiscoverJesus.Api.Tests;

internal sealed class SqlServerIntegrationTestApplication : IAsyncDisposable
{
    private const string BaseDatabaseName = "DiscoverJesusCi";
    private const string TestDatabasePrefix = "DiscoverJesusTest_";
    private readonly WebApplicationFactory<Program> factory;
    private bool databaseCreated;

    private SqlServerIntegrationTestApplication(
        WebApplicationFactory<Program> factory,
        HttpClient client)
    {
        this.factory = factory;
        Client = client;
    }

    public HttpClient Client { get; }

    public AsyncServiceScope CreateScope() => factory.Services.CreateAsyncScope();

    public static async Task<SqlServerIntegrationTestApplication> StartAsync()
    {
        var configuredConnectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            throw new InvalidOperationException(
                "Set ConnectionStrings__DefaultConnection to the disposable SQL Server database described in README.md.");
        }

        var sqlConnectionString = new SqlConnectionStringBuilder(configuredConnectionString);
        if (!string.Equals(
                sqlConnectionString.InitialCatalog,
                BaseDatabaseName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Integration tests require the dedicated '{BaseDatabaseName}' database in the connection string.");
        }

        sqlConnectionString.InitialCatalog = $"{TestDatabasePrefix}{Guid.NewGuid():N}";
        var databaseName = sqlConnectionString.InitialCatalog;
        var connectionString = sqlConnectionString.ConnectionString;
        var applicationFactory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = connectionString,
                        ["WebAuthn:RPID"] = "localhost",
                        ["WebAuthn:Origins:0"] = "http://localhost:5173",
                        ["Cors:AllowedOrigins:0"] = "http://localhost:5173"
                    }));
                builder.ConfigureTestServices(services =>
                {
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                        options.DefaultForbidScheme = TestAuthenticationHandler.SchemeName;
                    }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName,
                        _ => { });
                });
            });

        var client = applicationFactory.CreateClient();
        ConfigureTestClient(client);
        var application = new SqlServerIntegrationTestApplication(applicationFactory, client);

        try
        {
            var adminConnectionString = new SqlConnectionStringBuilder(configuredConnectionString)
            {
                InitialCatalog = "master"
            };
            await using (var connection = new SqlConnection(adminConnectionString.ConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE DATABASE [{databaseName}]";
                await command.ExecuteNonQueryAsync();
            }

            application.databaseCreated = true;
            await using var scope = applicationFactory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DiscoverJesusDbContext>();
            await db.Database.MigrateAsync();
            db.AuthorAccounts.Add(new AuthorAccount
            {
                Id = TestAuthenticationHandler.AccountId,
                CreatedAt = DateTimeOffset.UtcNow,
                Version = Guid.NewGuid()
            });
            db.AuthorGrants.Add(new AuthorGrant
            {
                AccountId = TestAuthenticationHandler.AccountId,
                Role = AuthorRole.Publisher
            });
            await db.SaveChangesAsync();
            return application;
        }
        catch
        {
            await application.DisposeAsync();
            throw;
        }
    }

    private static void ConfigureTestClient(HttpClient client)
    {
        client.DefaultRequestHeaders.Add("X-Test-Auth", "enabled");
        client.DefaultRequestHeaders.Add("X-CSRF-Token", TestAuthenticationHandler.CsrfToken);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (databaseCreated)
            {
                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<DiscoverJesusDbContext>();
                await db.Database.EnsureDeletedAsync();
            }

        }
        finally
        {
            Client.Dispose();
            await factory.DisposeAsync();
        }
    }
}
