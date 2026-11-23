using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace DiscoverJesus.Api.Services;

public static class AuthorAuthenticationPrimitives
{
    public static string CreateToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static string HashCredentialId(ReadOnlySpan<byte> credentialId) =>
        Convert.ToHexString(SHA256.HashData(credentialId));
}

public sealed class PasskeyRequestException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
