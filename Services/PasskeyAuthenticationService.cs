using System.Text;
using DiscoverJesus.Api.Contracts;
using DiscoverJesus.Api.Data;
using DiscoverJesus.Api.Models;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.EntityFrameworkCore;

namespace DiscoverJesus.Api.Services;

public sealed class PasskeyAuthenticationService(
    DiscoverJesusDbContext db,
    IFido2 fido2,
    TimeProvider clock)
{
    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);
    private static readonly TimeSpan SessionIdleLifetime = TimeSpan.FromMinutes(30);

    public async Task<PasskeyOptionsResponse<CredentialCreateOptions>> CreateInvitationRegistrationOptionsAsync(
        string invitationToken,
        CancellationToken cancellationToken)
    {
        var invitation = await GetUsableInvitationAsync(invitationToken, cancellationToken);
        await EnsureEligibleAccountAsync(invitation.AccountId, cancellationToken);
        if (await db.AuthorPasskeys.AnyAsync(
                passkey => passkey.AccountId == invitation.AccountId && passkey.RevokedAt == null,
                cancellationToken))
        {
            throw new PasskeyRequestException(
                StatusCodes.Status409Conflict,
                "This account already has an active passkey. Use an authenticated session to add another.");
        }

        var options = await CreateRegistrationOptionsAsync(invitation.AccountId, cancellationToken);
        var challenge = AddChallenge(
            options.ToJson(),
            AuthorWebAuthnChallengePurpose.InvitationRegistration,
            invitation.AccountId,
            invitation.Id,
            null);
        await db.SaveChangesAsync(cancellationToken);
        return new PasskeyOptionsResponse<CredentialCreateOptions>(challenge.Id, options);
    }

    public async Task<PasskeyOptionsResponse<CredentialCreateOptions>> CreateAuthenticatedRegistrationOptionsAsync(
        Guid accountId,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        await EnsureEligibleAccountAsync(accountId, cancellationToken);
        await EnsureActiveSessionAsync(accountId, sessionId, cancellationToken);
        var options = await CreateRegistrationOptionsAsync(accountId, cancellationToken);
        var challenge = AddChallenge(
            options.ToJson(),
            AuthorWebAuthnChallengePurpose.AuthenticatedRegistration,
            accountId,
            null,
            sessionId);
        await db.SaveChangesAsync(cancellationToken);
        return new PasskeyOptionsResponse<CredentialCreateOptions>(challenge.Id, options);
    }

    public async Task<PasskeyOptionsResponse<AssertionOptions>> CreateSignInOptionsAsync(
        CancellationToken cancellationToken)
    {
        var options = fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            UserVerification = UserVerificationRequirement.Required
        });
        var challenge = AddChallenge(
            options.ToJson(),
            AuthorWebAuthnChallengePurpose.SignIn,
            null,
            null,
            null);
        await db.SaveChangesAsync(cancellationToken);
        return new PasskeyOptionsResponse<AssertionOptions>(challenge.Id, options);
    }

    public async Task<PasskeyCompletionResult> CompleteInvitationRegistrationAsync(
        Guid challengeId,
        string invitationToken,
        string name,
        AuthenticatorAttestationRawResponse response,
        string? existingSessionToken,
        CancellationToken cancellationToken)
    {
        var challenge = await GetUsableChallengeAsync(
            challengeId,
            AuthorWebAuthnChallengePurpose.InvitationRegistration,
            cancellationToken);
        var invitation = await GetUsableInvitationAsync(invitationToken, cancellationToken);
        if (challenge.InvitationId != invitation.Id || challenge.AccountId != invitation.AccountId)
        {
            throw UnauthorizedInvitation();
        }

        await EnsureEligibleAccountAsync(invitation.AccountId, cancellationToken);
        if (await db.AuthorPasskeys.AnyAsync(
                passkey => passkey.AccountId == invitation.AccountId && passkey.RevokedAt == null,
                cancellationToken))
        {
            throw new PasskeyRequestException(StatusCodes.Status409Conflict, "The account already has an active passkey.");
        }

        var passkey = await VerifyAndCreatePasskeyAsync(
            challenge,
            invitation.AccountId,
            name,
            response,
            cancellationToken);
        var now = clock.GetUtcNow();
        invitation.ConsumedAt = now;
        invitation.Version = Guid.NewGuid();
        db.AuthorAccountAudits.Add(NewAccountAudit(
            invitation.AccountId,
            invitation.AccountId.ToString("D"),
            AuthorAccountAction.CredentialRegistered,
            now));

        var session = await CreateSessionAsync(
            invitation.AccountId,
            passkey.Id,
            existingSessionToken,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task CompleteAuthenticatedRegistrationAsync(
        Guid accountId,
        Guid sessionId,
        Guid challengeId,
        string name,
        AuthenticatorAttestationRawResponse response,
        CancellationToken cancellationToken)
    {
        var challenge = await GetUsableChallengeAsync(
            challengeId,
            AuthorWebAuthnChallengePurpose.AuthenticatedRegistration,
            cancellationToken);
        if (challenge.AccountId != accountId || challenge.SessionId != sessionId)
        {
            throw new PasskeyRequestException(StatusCodes.Status401Unauthorized, "The passkey challenge is invalid or expired.");
        }

        await EnsureEligibleAccountAsync(accountId, cancellationToken);
        await EnsureActiveSessionAsync(accountId, sessionId, cancellationToken);
        await VerifyAndCreatePasskeyAsync(
            challenge,
            accountId,
            name,
            response,
            cancellationToken);
        var now = clock.GetUtcNow();
        db.AuthorAccountAudits.Add(NewAccountAudit(
            accountId,
            accountId.ToString("D"),
            AuthorAccountAction.CredentialRegistered,
            now));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PasskeyCompletionResult> CompleteSignInAsync(
        Guid challengeId,
        AuthenticatorAssertionRawResponse response,
        string? existingSessionToken,
        CancellationToken cancellationToken)
    {
        var challenge = await GetUsableChallengeAsync(
            challengeId,
            AuthorWebAuthnChallengePurpose.SignIn,
            cancellationToken);
        if (response.RawId is null || response.RawId.Length == 0)
        {
            throw InvalidCredential();
        }

        var credentialHash = AuthorAuthenticationPrimitives.HashCredentialId(response.RawId);
        var passkey = await db.AuthorPasskeys.SingleOrDefaultAsync(
            item => item.CredentialIdHash == credentialHash && item.RevokedAt == null,
            cancellationToken);
        if (passkey is null || !passkey.CredentialId.AsSpan().SequenceEqual(response.RawId))
        {
            throw InvalidCredential();
        }

        await EnsureEligibleAccountAsync(passkey.AccountId, cancellationToken);
        var options = AssertionOptions.FromJson(challenge.OptionsJson);
        IsUserHandleOwnerOfCredentialIdAsync ownsCredential = (args, _) =>
            Task.FromResult(
                args.UserHandle is not null
                && args.CredentialId.AsSpan().SequenceEqual(passkey.CredentialId)
                && args.UserHandle.AsSpan().SequenceEqual(GetUserHandle(passkey.AccountId)));
        uint updatedSignCount;
        try
        {
            var assertion = await fido2.MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = response,
                OriginalOptions = options,
                StoredPublicKey = passkey.PublicKey,
                StoredSignatureCounter = checked((uint)passkey.SignatureCounter),
                IsUserHandleOwnerOfCredentialIdCallback = ownsCredential
            }, cancellationToken);
            if (!assertion.CredentialId.AsSpan().SequenceEqual(passkey.CredentialId))
            {
                throw InvalidCredential();
            }
            updatedSignCount = assertion.SignCount;
        }
        catch (Fido2VerificationException)
        {
            throw InvalidCredential();
        }
        catch (ArgumentException)
        {
            throw InvalidCredential();
        }

        var now = clock.GetUtcNow();
        passkey.SignatureCounter = updatedSignCount;
        passkey.LastUsedAt = now;
        challenge.ConsumedAt = now;
        challenge.Version = Guid.NewGuid();
        var session = await CreateSessionAsync(
            passkey.AccountId,
            passkey.Id,
            existingSessionToken,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<string> RefreshCsrfTokenAsync(
        Guid accountId,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        await EnsureActiveSessionAsync(accountId, sessionId, cancellationToken);
        var session = await db.AuthorWebSessions.SingleAsync(item => item.Id == sessionId, cancellationToken);
        var token = AuthorAuthenticationPrimitives.CreateToken();
        session.CsrfTokenHash = AuthorAuthenticationPrimitives.HashToken(token);
        await db.SaveChangesAsync(cancellationToken);
        return token;
    }

    public async Task RevokeSessionAsync(Guid accountId, Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await db.AuthorWebSessions.SingleOrDefaultAsync(
            item => item.Id == sessionId && item.AccountId == accountId && item.RevokedAt == null,
            cancellationToken);
        if (session is not null)
        {
            session.RevokedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<AuthorPasskeyResponse>> GetPasskeysAsync(
        Guid accountId,
        CancellationToken cancellationToken) =>
        await db.AuthorPasskeys.AsNoTracking()
            .Where(passkey => passkey.AccountId == accountId && passkey.RevokedAt == null)
            .OrderBy(passkey => passkey.CreatedAt)
            .Select(passkey => new AuthorPasskeyResponse(
                passkey.Id,
                passkey.Name,
                passkey.CreatedAt,
                passkey.LastUsedAt,
                passkey.RevokedAt))
            .ToListAsync(cancellationToken);

    public async Task<bool> RevokeOwnPasskeyAsync(
        Guid accountId,
        Guid passkeyId,
        CancellationToken cancellationToken)
    {
        var passkey = await db.AuthorPasskeys.SingleOrDefaultAsync(
            item => item.Id == passkeyId && item.AccountId == accountId && item.RevokedAt == null,
            cancellationToken);
        if (passkey is null)
        {
            return false;
        }

        var activePasskeyCount = await db.AuthorPasskeys.CountAsync(
            item => item.AccountId == accountId && item.RevokedAt == null,
            cancellationToken);
        if (activePasskeyCount <= 1)
        {
            throw new PasskeyRequestException(
                StatusCodes.Status409Conflict,
                "The last active passkey cannot be removed. Ask an operator to complete account recovery.");
        }

        var account = await db.AuthorAccounts.SingleAsync(item => item.Id == accountId, cancellationToken);
        account.Version = Guid.NewGuid();
        await RevokePasskeyAsync(passkey, accountId.ToString("D"), cancellationToken);
        return true;
    }

    public async Task<string> IssueInvitationAsync(
        Guid accountId,
        string actor,
        CancellationToken cancellationToken)
    {
        var account = await db.AuthorAccounts.SingleOrDefaultAsync(
            item => item.Id == accountId,
            cancellationToken) ?? throw new KeyNotFoundException("Author account was not found.");
        if (account.DisabledAt is not null)
        {
            throw new InvalidOperationException("Cannot invite a disabled account.");
        }
        if (!await db.AuthorGrants.AnyAsync(item => item.AccountId == accountId, cancellationToken))
        {
            throw new InvalidOperationException("Cannot invite an account without an authoring grant.");
        }
        if (await db.AuthorPasskeys.AnyAsync(
                item => item.AccountId == accountId && item.RevokedAt == null,
                cancellationToken))
        {
            throw new InvalidOperationException("Revoke all active passkeys before issuing a recovery invitation.");
        }
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 256)
        {
            throw new ArgumentException("A trusted actor identifier is required.", nameof(actor));
        }

        var now = clock.GetUtcNow();
        var invitations = await db.AuthorPasskeyInvitations
            .Where(item => item.AccountId == accountId
                && item.ConsumedAt == null
                && item.RevokedAt == null
                && item.ExpiresAt > now)
            .ToListAsync(cancellationToken);
        foreach (var existing in invitations)
        {
            existing.RevokedAt = now;
            existing.Version = Guid.NewGuid();
            db.AuthorAccountAudits.Add(NewAccountAudit(
                accountId,
                actor,
                AuthorAccountAction.InvitationRevoked,
                now));
        }

        var token = AuthorAuthenticationPrimitives.CreateToken();
        db.AuthorPasskeyInvitations.Add(new AuthorPasskeyInvitation
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            TokenHash = AuthorAuthenticationPrimitives.HashToken(token),
            CreatedAt = now,
            ExpiresAt = now.Add(InvitationLifetime),
            Version = Guid.NewGuid()
        });
        account.Version = Guid.NewGuid();
        db.AuthorAccountAudits.Add(NewAccountAudit(
            accountId,
            actor,
            AuthorAccountAction.InvitationIssued,
            now));
        await db.SaveChangesAsync(cancellationToken);
        return token;
    }

    public async Task<IReadOnlyList<AuthorPasskeyResponse>> GetPasskeysForOperatorAsync(
        Guid accountId,
        CancellationToken cancellationToken) =>
        await db.AuthorPasskeys.AsNoTracking()
            .Where(passkey => passkey.AccountId == accountId)
            .OrderBy(passkey => passkey.CreatedAt)
            .Select(passkey => new AuthorPasskeyResponse(
                passkey.Id,
                passkey.Name,
                passkey.CreatedAt,
                passkey.LastUsedAt,
                passkey.RevokedAt))
            .ToListAsync(cancellationToken);

    public async Task<bool> RevokePasskeyForOperatorAsync(
        Guid accountId,
        Guid passkeyId,
        string actor,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 256)
        {
            throw new ArgumentException("A trusted actor identifier is required.", nameof(actor));
        }

        var passkey = await db.AuthorPasskeys.SingleOrDefaultAsync(
            item => item.Id == passkeyId && item.AccountId == accountId && item.RevokedAt == null,
            cancellationToken);
        if (passkey is null)
        {
            return false;
        }

        var account = await db.AuthorAccounts.SingleAsync(item => item.Id == accountId, cancellationToken);
        account.Version = Guid.NewGuid();
        await RevokePasskeyAsync(passkey, actor, cancellationToken);
        return true;
    }

    private async Task<CredentialCreateOptions> CreateRegistrationOptionsAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var existingCredentials = await db.AuthorPasskeys.AsNoTracking()
            .Where(passkey => passkey.AccountId == accountId)
            .Select(passkey => passkey.CredentialId)
            .ToListAsync(cancellationToken);
        var user = new Fido2User
        {
            Id = GetUserHandle(accountId),
            Name = $"author-{accountId:N}",
            DisplayName = "Discover Jesus Author"
        };
        return fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = user,
            ExcludeCredentials = existingCredentials
                .Select(id => new PublicKeyCredentialDescriptor(id))
                .ToList(),
            AuthenticatorSelection = new AuthenticatorSelection
            {
                ResidentKey = ResidentKeyRequirement.Required,
                UserVerification = UserVerificationRequirement.Required
            },
            AttestationPreference = AttestationConveyancePreference.None
        });
    }

    private async Task<AuthorPasskey> VerifyAndCreatePasskeyAsync(
        AuthorWebAuthnChallenge challenge,
        Guid accountId,
        string name,
        AuthenticatorAttestationRawResponse response,
        CancellationToken cancellationToken)
    {
        var normalizedName = ValidatePasskeyName(name);
        var options = CredentialCreateOptions.FromJson(challenge.OptionsJson);
        IsCredentialIdUniqueToUserAsyncDelegate isUnique = async (args, token) =>
        {
            var hash = AuthorAuthenticationPrimitives.HashCredentialId(args.CredentialId);
            return !await db.AuthorPasskeys.AnyAsync(
                passkey => passkey.CredentialIdHash == hash,
                token);
        };

        (byte[] Id, byte[] PublicKey, uint SignCount) verifiedCredential;
        try
        {
            var credential = await fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = response,
                OriginalOptions = options,
                IsCredentialIdUniqueToUserCallback = isUnique
            }, cancellationToken);
            verifiedCredential = (credential.Id, credential.PublicKey, credential.SignCount);
        }
        catch (Fido2VerificationException)
        {
            throw InvalidCredential();
        }
        catch (ArgumentException)
        {
            throw InvalidCredential();
        }

        var credentialHash = AuthorAuthenticationPrimitives.HashCredentialId(verifiedCredential.Id);
        var passkey = new AuthorPasskey
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            CredentialIdHash = credentialHash,
            CredentialId = verifiedCredential.Id,
            PublicKey = verifiedCredential.PublicKey,
            SignatureCounter = verifiedCredential.SignCount,
            Name = normalizedName,
            CreatedAt = clock.GetUtcNow()
        };
        db.AuthorPasskeys.Add(passkey);
        challenge.ConsumedAt = clock.GetUtcNow();
        challenge.Version = Guid.NewGuid();
        return passkey;
    }

    private async Task<AuthorPasskeyInvitation> GetUsableInvitationAsync(
        string? token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw UnauthorizedInvitation();
        }

        var hash = AuthorAuthenticationPrimitives.HashToken(token);
        var now = clock.GetUtcNow();
        var invitation = await db.AuthorPasskeyInvitations.SingleOrDefaultAsync(
            item => item.TokenHash == hash
                && item.ConsumedAt == null
                && item.RevokedAt == null
                && item.ExpiresAt > now,
            cancellationToken);
        return invitation ?? throw UnauthorizedInvitation();
    }

    private async Task<AuthorWebAuthnChallenge> GetUsableChallengeAsync(
        Guid id,
        AuthorWebAuthnChallengePurpose purpose,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var challenge = await db.AuthorWebAuthnChallenges.SingleOrDefaultAsync(
            item => item.Id == id
                && item.Purpose == purpose
                && item.ConsumedAt == null
                && item.ExpiresAt > now,
            cancellationToken);
        return challenge ?? throw new PasskeyRequestException(
            StatusCodes.Status401Unauthorized,
            "The passkey challenge is invalid or expired.");
    }

    private async Task EnsureEligibleAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await db.AuthorAccounts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == accountId, cancellationToken);
        if (account is null || account.DisabledAt is not null)
        {
            throw new PasskeyRequestException(StatusCodes.Status401Unauthorized, "The author account is unavailable.");
        }
        if (!await db.AuthorGrants.AnyAsync(item => item.AccountId == accountId, cancellationToken))
        {
            throw new PasskeyRequestException(StatusCodes.Status403Forbidden, "The account has no active authoring grant.");
        }
    }

    private async Task EnsureActiveSessionAsync(
        Guid accountId,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var session = await db.AuthorWebSessions.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == sessionId
                && item.AccountId == accountId
                && item.RevokedAt == null
                && item.ExpiresAt > now
                && item.LastActivityAt > now.Subtract(SessionIdleLifetime),
            cancellationToken);
        if (session is null)
        {
            throw new PasskeyRequestException(StatusCodes.Status401Unauthorized, "The author session is unavailable.");
        }
        await EnsureEligibleAccountAsync(accountId, cancellationToken);
        if (!await db.AuthorPasskeys.AnyAsync(
                item => item.Id == session.CredentialId && item.RevokedAt == null,
                cancellationToken))
        {
            throw new PasskeyRequestException(StatusCodes.Status401Unauthorized, "The author session is unavailable.");
        }
    }

    private async Task<PasskeyCompletionResult> CreateSessionAsync(
        Guid accountId,
        Guid credentialId,
        string? existingSessionToken,
        CancellationToken cancellationToken)
    {
        var roles = await GetRolesAsync(accountId, cancellationToken);
        if (roles.Length == 0)
        {
            throw new PasskeyRequestException(StatusCodes.Status403Forbidden, "The account has no active authoring grant.");
        }

        var now = clock.GetUtcNow();
        if (!string.IsNullOrWhiteSpace(existingSessionToken))
        {
            var previousHash = AuthorAuthenticationPrimitives.HashToken(existingSessionToken);
            var previous = await db.AuthorWebSessions.SingleOrDefaultAsync(
                session => session.SessionTokenHash == previousHash && session.RevokedAt == null,
                cancellationToken);
            if (previous is not null)
            {
                previous.RevokedAt = now;
            }
        }

        var token = AuthorAuthenticationPrimitives.CreateToken();
        var csrfToken = AuthorAuthenticationPrimitives.CreateToken();
        db.AuthorWebSessions.Add(new AuthorWebSession
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            CredentialId = credentialId,
            SessionTokenHash = AuthorAuthenticationPrimitives.HashToken(token),
            CsrfTokenHash = AuthorAuthenticationPrimitives.HashToken(csrfToken),
            CreatedAt = now,
            LastActivityAt = now,
            ExpiresAt = now.Add(SessionLifetime)
        });
        return new PasskeyCompletionResult(accountId, roles, token, csrfToken);
    }

    private async Task<string[]> GetRolesAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var roles = await db.AuthorGrants.AsNoTracking()
            .Where(grant => grant.AccountId == accountId)
            .OrderBy(grant => grant.Role)
            .ToArrayAsync(cancellationToken);
        return roles.Select(grant => grant.Role.ToString()).ToArray();
    }

    private async Task RevokePasskeyAsync(
        AuthorPasskey passkey,
        string actor,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        passkey.RevokedAt = now;
        var sessions = await db.AuthorWebSessions
            .Where(session => session.CredentialId == passkey.Id && session.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.RevokedAt = now;
        }

        db.AuthorAccountAudits.Add(NewAccountAudit(
            passkey.AccountId,
            actor,
            AuthorAccountAction.CredentialRevoked,
            now));
        await db.SaveChangesAsync(cancellationToken);
    }

    private AuthorWebAuthnChallenge AddChallenge(
        string optionsJson,
        AuthorWebAuthnChallengePurpose purpose,
        Guid? accountId,
        Guid? invitationId,
        Guid? sessionId)
    {
        var now = clock.GetUtcNow();
        var challenge = new AuthorWebAuthnChallenge
        {
            Id = Guid.NewGuid(),
            Purpose = purpose,
            AccountId = accountId,
            InvitationId = invitationId,
            SessionId = sessionId,
            OptionsJson = optionsJson,
            CreatedAt = now,
            ExpiresAt = now.Add(ChallengeLifetime),
            Version = Guid.NewGuid()
        };
        db.AuthorWebAuthnChallenges.Add(challenge);
        return challenge;
    }

    private static byte[] GetUserHandle(Guid accountId) =>
        Encoding.UTF8.GetBytes(accountId.ToString("N"));

    private static string ValidatePasskeyName(string? name)
    {
        var normalized = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Length > 100
            || normalized.Any(char.IsControl))
        {
            throw new PasskeyRequestException(
                StatusCodes.Status400BadRequest,
                "Passkey name must contain 1 to 100 printable characters.");
        }

        return normalized;
    }

    private static AuthorAccountAudit NewAccountAudit(
        Guid accountId,
        string actor,
        AuthorAccountAction action,
        DateTimeOffset now) =>
        new()
        {
            AccountId = accountId,
            Actor = actor,
            Action = action,
            Role = null,
            OccurredAt = now
        };

    private static PasskeyRequestException UnauthorizedInvitation() =>
        new(StatusCodes.Status401Unauthorized, "The enrollment invitation is invalid or expired.");

    private static PasskeyRequestException InvalidCredential() =>
        new(StatusCodes.Status401Unauthorized, "The passkey response could not be verified.");
}

public sealed record PasskeyCompletionResult(
    Guid AccountId,
    string[] Roles,
    string SessionToken,
    string CsrfToken);
