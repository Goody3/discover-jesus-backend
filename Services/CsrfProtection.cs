using System.Security.Cryptography;
using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.WebUtilities;

namespace DiscoverJesus.Api.Services;

public sealed class CsrfProtectedEndpointMetadata
{
}

public static class CsrfProtectionConventions
{
    public static RouteGroupBuilder RequireCsrfProtection(this RouteGroupBuilder builder)
    {
        builder.WithMetadata(new CsrfProtectedEndpointMetadata());
        return builder;
    }

    public static RouteHandlerBuilder RequireCsrfProtection(this RouteHandlerBuilder builder)
    {
        builder.WithMetadata(new CsrfProtectedEndpointMetadata());
        return builder;
    }
}

public sealed class CsrfProtectionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<CsrfProtectedEndpointMetadata>() is null
            || HttpMethods.IsGet(context.Request.Method)
            || HttpMethods.IsHead(context.Request.Method)
            || HttpMethods.IsOptions(context.Request.Method)
            || HttpMethods.IsTrace(context.Request.Method)
            || context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var expectedHash = context.User.FindFirstValue(AuthorSessionDefaults.CsrfHashClaim);
        var token = context.Request.Headers["X-CSRF-Token"].ToString();
        if (!IsValidToken(token, expectedHash))
        {
            await Results.Problem(
                title: "A valid session CSRF token is required.",
                statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(context);
            return;
        }

        await next(context);
    }

    private static bool IsValidToken(string token, string? expectedHash)
    {
        if (expectedHash is null || token.Length != 43)
        {
            return false;
        }

        try
        {
            if (WebEncoders.Base64UrlDecode(token).Length != 32)
            {
                return false;
            }

            var actual = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            var expected = Convert.FromHexString(expectedHash);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
