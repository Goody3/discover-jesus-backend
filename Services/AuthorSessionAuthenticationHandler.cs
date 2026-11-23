using System.Security.Claims;
using System.Text.Encodings.Web;
using DiscoverJesus.Api.Data;
using DiscoverJesus.Api.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.WebUtilities;

namespace DiscoverJesus.Api.Services;

public static class AuthorSessionDefaults
{
    public const string Scheme = "AuthorSession";
    public const string CookieName = "__Host-DiscoverJesusAuthor";
    public const string SessionIdClaim = "author_session_id";
    public const string CsrfHashClaim = "author_csrf_hash";
}

public sealed class AuthorSessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    DiscoverJesusDbContext db,
    TimeProvider clock)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private static readonly TimeSpan IdleLifetime = TimeSpan.FromMinutes(30);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(AuthorSessionDefaults.CookieName, out var token)
            || !IsSessionToken(token))
        {
            return AuthenticateResult.NoResult();
        }

        var tokenHash = AuthorAuthenticationPrimitives.HashToken(token);
        var session = await db.AuthorWebSessions.SingleOrDefaultAsync(
            item => item.SessionTokenHash == tokenHash,
            Context.RequestAborted);
        if (session is null || session.RevokedAt is not null)
        {
            return AuthenticateResult.NoResult();
        }

        var now = clock.GetUtcNow();
        if (session.ExpiresAt <= now || session.LastActivityAt <= now.Subtract(IdleLifetime))
        {
            session.RevokedAt = now;
            await db.SaveChangesAsync(Context.RequestAborted);
            return AuthenticateResult.NoResult();
        }

        var account = await db.AuthorAccounts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == session.AccountId, Context.RequestAborted);
        if (account is null || account.DisabledAt is not null)
        {
            return AuthenticateResult.NoResult();
        }

        if (!await db.AuthorPasskeys.AnyAsync(
                item => item.Id == session.CredentialId && item.RevokedAt == null,
                Context.RequestAborted))
        {
            session.RevokedAt = now;
            await db.SaveChangesAsync(Context.RequestAborted);
            return AuthenticateResult.NoResult();
        }

        var roles = await db.AuthorGrants.AsNoTracking()
            .Where(item => item.AccountId == session.AccountId)
            .Select(item => item.Role)
            .ToArrayAsync(Context.RequestAborted);
        if (roles.Length == 0)
        {
            session.RevokedAt = now;
            await db.SaveChangesAsync(Context.RequestAborted);
            return AuthenticateResult.NoResult();
        }

        session.LastActivityAt = now;
        await db.SaveChangesAsync(Context.RequestAborted);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, session.AccountId.ToString("D")),
            new(AuthorSessionDefaults.SessionIdClaim, session.Id.ToString("D")),
            new(AuthorSessionDefaults.CsrfHashClaim, session.CsrfTokenHash)
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role.ToString())));
        var identity = new ClaimsIdentity(claims, Scheme.Name, ClaimTypes.Name, ClaimTypes.Role);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    private static bool IsSessionToken(string token)
    {
        if (token.Length != 43)
        {
            return false;
        }

        try
        {
            return WebEncoders.Base64UrlDecode(token).Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
