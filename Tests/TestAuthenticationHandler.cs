using System.Security.Claims;
using System.Text.Encodings.Web;
using DiscoverJesus.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiscoverJesus.Api.Tests;

internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public static readonly Guid AccountId = Guid.Parse("90a8d7ac-a756-4c0a-a62e-a7db9e1d6de3");
    public const string CsrfToken =
        "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY";
    public const string SchemeName = "TestOnly";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-Auth", out var enabled)
            || enabled != "enabled")
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var roles = Request.Headers.TryGetValue("X-Test-Roles", out var requestedRoles)
            ? requestedRoles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : ["DraftEditor", "Publisher"];
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, AccountId.ToString("D")),
            new(AuthorSessionDefaults.SessionIdClaim, Guid.NewGuid().ToString("D")),
            new(AuthorSessionDefaults.CsrfHashClaim, AuthorAuthenticationPrimitives.HashToken(CsrfToken))
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        var identity = new ClaimsIdentity(claims, Scheme.Name, ClaimTypes.Name, ClaimTypes.Role);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
