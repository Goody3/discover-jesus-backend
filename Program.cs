using DiscoverJesus.Api;
using DiscoverJesus.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Configure the SQL Server connection string with ConnectionStrings:DefaultConnection.");
}

builder.Services.AddDbContext<DiscoverJesusDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SupportNonNullableReferenceTypes();
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Discover Jesus Content API",
        Version = "v1",
        Description = "Serves and manages dynamic pages, components, data bindings, and action definitions."
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("GetHealth")
    .WithTags("Health")
    .WithSummary("Check API availability");
app.MapContentEndpoints();

app.Run();

public partial class Program;
