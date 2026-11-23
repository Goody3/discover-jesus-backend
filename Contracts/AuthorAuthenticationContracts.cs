using Fido2NetLib;
using Fido2NetLib.Objects;

namespace DiscoverJesus.Api.Contracts;

public sealed record InvitationRegistrationOptionsRequest(string InvitationToken);
public sealed record CompleteInvitationRegistrationRequest(
    Guid ChallengeId,
    string InvitationToken,
    string Name,
    AuthenticatorAttestationRawResponse Credential);
public sealed record CompleteAuthenticatedRegistrationRequest(
    Guid ChallengeId,
    string Name,
    AuthenticatorAttestationRawResponse Credential);
public sealed record CompleteSignInRequest(
    Guid ChallengeId,
    AuthenticatorAssertionRawResponse Credential);
public sealed record PasskeyOptionsResponse<TOptions>(Guid ChallengeId, TOptions Options);
public sealed record AuthorSessionResponse(Guid AccountId, string[] Roles, string CsrfToken);
public sealed record AuthorPasskeyResponse(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt);
