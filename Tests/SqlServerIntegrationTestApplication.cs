using DiscoverJesus.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
                        ["ConnectionStrings:DefaultConnection"] = connectionString
                    }));
            });

        var application = new SqlServerIntegrationTestApplication(
            applicationFactory,
            applicationFactory.CreateClient());

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
            return application;
        }
        catch
        {
            await application.DisposeAsync();
            throw;
        }
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
