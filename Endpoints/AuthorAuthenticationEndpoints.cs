using System.Security.Claims;
using DiscoverJesus.Api.Contracts;
using DiscoverJesus.Api.Services;
using Fido2NetLib;
using Microsoft.AspNetCore.RateLimiting;

namespace DiscoverJesus.Api;

public static class AuthorAuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapAuthorAuthenticationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/api/auth")
            .WithTags("Author authentication");
        var ceremonies = auth.MapGroup("")
            .RequireRateLimiting("authentication");

        ceremonies.MapPost("/invitations/registration-options", CreateInvitationRegistrationOptions)
            .AllowAnonymous()
            .WithName("CreateInvitationPasskeyOptions")
            .WithSummary("Start passkey enrollment with a one-time operator invitation")
            .Produces<PasskeyOptionsResponse<CredentialCreateOptions>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);
        ceremonies.MapPost("/invitations/registration", CompleteInvitationRegistration)
            .AllowAnonymous()
            .WithName("CompleteInvitationPasskeyRegistration")
            .WithSummary("Complete first passkey enrollment for an invited author")
            .Produces<AuthorSessionResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);
        ceremonies.MapPost("/sign-in/options", CreateSignInOptions)
            .AllowAnonymous()
            .WithName("CreatePasskeySignInOptions")
            .WithSummary("Start username-less passkey sign-in")
            .Produces<PasskeyOptionsResponse<AssertionOptions>>();
        ceremonies.MapPost("/sign-in", CompleteSignIn)
            .AllowAnonymous()
            .WithName("CompletePasskeySignIn")
            .WithSummary("Complete username-less passkey sign-in")
            .Produces<AuthorSessionResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        var authenticated = auth.MapGroup("")
            .RequireRateLimiting("authoring")
            .RequireAuthorization("Author")
            .RequireCsrfProtection();
        authenticated.MapGet("/session", GetSession)
            .WithName("GetAuthorSession")
            .WithSummary("Get the current author session and a fresh CSRF token")
            .Produces<AuthorSessionResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        authenticated.MapPost("/sign-out", SignOut)
            .WithName("SignOutAuthor")
            .WithSummary("Revoke the current author session")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        authenticated.MapGet("/passkeys", GetPasskeys)
            .WithName("GetAuthorPasskeys")
            .WithSummary("List active passkeys for the current author")
            .Produces<IReadOnlyList<AuthorPasskeyResponse>>();
        authenticated.MapPost("/passkeys/registration-options", CreateAuthenticatedRegistrationOptions)
            .WithName("CreateAdditionalPasskeyOptions")
            .WithSummary("Start registration of an additional passkey")
            .Produces<PasskeyOptionsResponse<CredentialCreateOptions>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        authenticated.MapPost("/passkeys", CompleteAuthenticatedRegistration)
            .WithName("CompleteAdditionalPasskeyRegistration")
            .WithSummary("Complete registration of an additional passkey")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);
        authenticated.MapDelete("/passkeys/{passkeyId:guid}", RevokePasskey)
            .WithName("RevokeAuthorPasskey")
            .WithSummary("Revoke one active passkey without changing the author account")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> CreateInvitationRegistrationOptions(
        InvitationRegistrationOptionsRequest request,
        PasskeyAuthenticationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await service.CreateInvitationRegistrationOptionsAsync(
                request.InvitationToken,
                cancellationToken));
        }
        catch (PasskeyRequestException error)
        {
            return ToProblem(error);
        }
    }

    private static async Task<IResult> CompleteInvitationRegistration(
        CompleteInvitationRegistrationRequest request,
        HttpContext context,
        PasskeyAuthenticationService service,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.CompleteInvitationRegistrationAsync(
                request.ChallengeId,
                request.InvitationToken,
                request.Name,
                request.Credential,
                GetExistingSessionToken(context),
                cancellationToken);
            SetSessionCookie(context, result.SessionToken, clock.GetUtcNow().AddHours(8));
            return Results.Ok(new AuthorSessionResponse(result.AccountId, result.Roles, result.CsrfToken));
        }
        catch (PasskeyRequestException error)
        {
            return ToProblem(error);
        }
    }

    private static async Task<IResult> CreateSignInOptions(
        PasskeyAuthenticationService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.CreateSignInOptionsAsync(cancellationToken));

    private static async Task<IResult> CompleteSignIn(
        CompleteSignInRequest request,
        HttpContext context,
        PasskeyAuthenticationService service,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.CompleteSignInAsync(
                request.ChallengeId,
                request.Credential,
                GetExistingSessionToken(context),
                cancellationToken);
            SetSessionCookie(context, result.SessionToken, clock.GetUtcNow().AddHours(8));
            return Results.Ok(new AuthorSessionResponse(result.AccountId, result.Roles, result.CsrfToken));
        }
        catch (PasskeyRequestException error)
        {
            return ToProblem(error);
        }
    }

    private static async Task<IResult> GetSession(
        HttpContext context,
        PasskeyAuthenticationService service,
        CancellationToken cancellationToken)
    {
        var accountId = GetAccountId(context.User);
        var sessionId = GetSessionId(context.User);
        try
        {
            var csrfToken = await service.RefreshCsrfTokenAsync(accountId, sessionId, cancellationToken);
            var roles = context.User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray();
            return Results.Ok(new AuthorSessionResponse(accountId, roles, csrfToken));
        }
        catch (PasskeyRequestException error)
        {
            return ToProblem(error);
        }
    }

    private static async Task<IResult> SignOut(
        HttpContext context,
        PasskeyAuthenticationService service,
        CancellationToken cancellationToken)
    {
        await service.RevokeSessionAsync(
            GetAccountId(context.User),
            GetSessionId(context.User),
            cancellationToken);
        DeleteSessionCookie(context);
        return Results.NoContent();
    }

    private static async Task<IResult> GetPasskeys(
        ClaimsPrincipal user,
        PasskeyAuthenticationService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.GetPasskeysAsync(GetAccountId(user), cancellationToken));

    private static async Task<IResult> CreateAuthenticatedRegistrationOptions(
        ClaimsPrincipal user,
        PasskeyAuthenticationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await service.CreateAuthenticatedRegistrationOptionsAsync(
                GetAccountId(user),
                GetSessionId(user),
                cancellationToken));
        }
        catch (PasskeyRequestException error)
        {
            return ToProblem(error);
        }
    }

    private static async Task<IResult> CompleteAuthenticatedRegistration(
        CompleteAuthenticatedRegistrationRequest request,
        ClaimsPrincipal user,
        PasskeyAuthenticationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            await service.CompleteAuthenticatedRegistrationAsync(
                GetAccountId(user),
                GetSessionId(user),
                request.ChallengeId,
                request.Name,
                request.Credential,
                cancellationToken);
            return Results.NoContent();
        }
        catch (PasskeyRequestException error)
        {
            return ToProblem(error);
        }
    }

    private static async Task<IResult> RevokePasskey(
        Guid passkeyId,
        ClaimsPrincipal user,
        PasskeyAuthenticationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var revoked = await service.RevokeOwnPasskeyAsync(
                GetAccountId(user),
                passkeyId,
                cancellationToken);
            return revoked ? Results.NoContent() : Results.NotFound();
        }
        catch (PasskeyRequestException error)
        {
            return ToProblem(error);
        }
    }

    private static IResult ToProblem(PasskeyRequestException error) =>
        Results.Problem(title: error.Message, statusCode: error.StatusCode);

    private static Guid GetAccountId(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static Guid GetSessionId(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(AuthorSessionDefaults.SessionIdClaim)!);

    private static string? GetExistingSessionToken(HttpContext context) =>
        context.Request.Cookies.TryGetValue(AuthorSessionDefaults.CookieName, out var token)
            ? token
            : null;

    private static void SetSessionCookie(HttpContext context, string token, DateTimeOffset expiresAt) =>
        context.Response.Cookies.Append(
            AuthorSessionDefaults.CookieName,
            token,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                IsEssential = true,
                Expires = expiresAt
            });

    private static void DeleteSessionCookie(HttpContext context) =>
        context.Response.Cookies.Delete(
            AuthorSessionDefaults.CookieName,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Path = "/"
            });
}
