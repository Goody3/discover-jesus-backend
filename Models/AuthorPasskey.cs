namespace DiscoverJesus.Api.Models;

public sealed class AuthorPasskeyInvitation
{
    public Guid Id { get; init; }
    public Guid AccountId { get; init; }
    public required string TokenHash { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid Version { get; set; }
}

public sealed class AuthorPasskey
{
    public Guid Id { get; init; }
    public Guid AccountId { get; init; }
    public required string CredentialIdHash { get; init; }
    public required byte[] CredentialId { get; init; }
    public required byte[] PublicKey { get; init; }
    public long SignatureCounter { get; set; }
    public required string Name { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public enum AuthorWebAuthnChallengePurpose
{
    InvitationRegistration = 1,
    AuthenticatedRegistration = 2,
    SignIn = 3,
}

public sealed class AuthorWebAuthnChallenge
{
    public Guid Id { get; init; }
    public AuthorWebAuthnChallengePurpose Purpose { get; init; }
    public Guid? AccountId { get; init; }
    public Guid? InvitationId { get; init; }
    public Guid? SessionId { get; init; }
    public required string OptionsJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public Guid Version { get; set; }
}

public sealed class AuthorWebSession
{
    public Guid Id { get; init; }
    public Guid AccountId { get; init; }
    public Guid CredentialId { get; init; }
    public required string SessionTokenHash { get; init; }
    public required string CsrfTokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset LastActivityAt { get; set; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; set; }
}
