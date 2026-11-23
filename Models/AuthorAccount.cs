namespace DiscoverJesus.Api.Models;

public sealed class AuthorAccount
{
    public Guid Id { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? DisabledAt { get; set; }
    public Guid Version { get; set; }
}

public enum AuthorRole
{
    DraftEditor = 1,
    Publisher = 2,
}

public sealed class AuthorGrant
{
    public Guid AccountId { get; init; }
    public AuthorRole Role { get; init; }
}

public enum AuthorAccountAction
{
    Provisioned = 1,
    Granted = 2,
    Revoked = 3,
    Disabled = 4,
    InvitationIssued = 5,
    InvitationRevoked = 6,
    CredentialRegistered = 7,
    CredentialRevoked = 8,
}

public sealed class AuthorAccountAudit
{
    public long Id { get; init; }
    public Guid AccountId { get; init; }
    public required string Actor { get; init; }
    public AuthorAccountAction Action { get; init; }
    public AuthorRole? Role { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}
