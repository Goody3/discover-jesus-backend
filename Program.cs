using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using DiscoverJesus.Api;
using DiscoverJesus.Api.Contracts;
using DiscoverJesus.Api.Data;
using DiscoverJesus.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
const long maximumRequestBodySize = 1024 * 1024;
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

if (allowedOrigins.Any(origin =>
        !Uri.TryCreate(origin, UriKind.Absolute, out var parsedOrigin)
        || parsedOrigin.Scheme is not ("http" or "https")
        || parsedOrigin.UserInfo.Length != 0
        || parsedOrigin.AbsolutePath != "/"
        || parsedOrigin.Query.Length != 0
        || parsedOrigin.Fragment.Length != 0
        || !string.Equals(origin, parsedOrigin.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal)))
{
    throw new InvalidOperationException(
        "Cors:AllowedOrigins entries must be exact http or https origins without paths, trailing slashes, or credentials.");
}

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Configure the SQL Server connection string with ConnectionStrings:DefaultConnection.");
}

builder.Services.AddDbContext<DiscoverJesusDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddScoped<IReusableContentService, ReusableContentService>();
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = maximumRequestBodySize);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (allowedOrigins.Length > 0)
    {
        policy.WithOrigins(allowedOrigins);
    }

    policy.AllowAnyHeader().AllowAnyMethod();
}));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("published-reader", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetClientAddress(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("authoring", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetClientAddress(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});
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
app.UseRouting();
app.UseCors();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (context.Request.ContentLength > maximumRequestBodySize)
    {
        await Results.Problem(
            title: "Request body exceeds the 1 MiB limit.",
            statusCode: StatusCodes.Status413PayloadTooLarge).ExecuteAsync(context);
        return;
    }

    await next();
});
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

static string GetClientAddress(HttpContext context) =>
    context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

public partial class Program;
