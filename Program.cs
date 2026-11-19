using DiscoverJesus.Api;
using DiscoverJesus.Api.Contracts;
using DiscoverJesus.Api.Data;
using DiscoverJesus.Api.Services;
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
builder.Services.AddScoped<IReusableContentService, ReusableContentService>();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SupportNonNullableReferenceTypes();
    options.SchemaFilter<ResponseSchemaFilter>();
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Discover Jesus Content API",
        Version = "v1",
        Description = "Serves and manages pages, reusable components, data bindings, and action definitions."
    });
});

var app = builder.Build();

app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => Results.Ok(new HealthResponse("healthy")))
    .WithName("GetHealth")
    .WithTags("Health")
    .WithSummary("Check API availability")
    .Produces<HealthResponse>();
app.MapContentEndpoints();
app.MapReusableContentEndpoints();

app.Run();

public partial class Program;
