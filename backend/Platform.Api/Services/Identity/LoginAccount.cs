using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Identity.Entities;
using Platform.Identity.Enums;

namespace Platform.Api.Services.Identity;

public enum LoginOutcome
{
    Success,
    InvalidCredentials,
    AccountBlocked,
    AccountArchived,
    Unavailable
}

public sealed record LoginResult(LoginOutcome Outcome, string? AccessToken = null);

public sealed class LoginAccount(
    ICredentialAuthenticator credentials,
    ISessionIssuer sessions,
    IAuditWriter audit,
    TimeProvider clock)
{
    private const string AuditAction = "auth.login";

    public async Task<LoginResult> ExecuteAsync(
        string? email,
        string? password,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
            return Fail(LoginOutcome.InvalidCredentials, null, null, correlationId, "invalid_request");

        var verification = await credentials.AuthenticateAsync(email.Trim(), password, cancellationToken);
        if (verification.Outcome == CredentialAuthenticationOutcome.Unavailable)
            return Fail(LoginOutcome.Unavailable, null, null, correlationId, "login_unavailable");

        // IDN-05: zaključan nalog dobija isti odgovor kao pogrešna lozinka ili nepostojeći nalog.
        // Razlika postoji samo u audit zapisu.
        if (verification.Outcome == CredentialAuthenticationOutcome.LockedOut)
            return Fail(LoginOutcome.InvalidCredentials, null, verification.AccountId, correlationId, "account_locked");

        if (verification.Outcome != CredentialAuthenticationOutcome.Valid || verification.AccountId is not Guid accountId)
            return Fail(LoginOutcome.InvalidCredentials, null, null, correlationId, "invalid_credentials");

        // Status is checked only after valid credentials, so the endpoint does not
        // disclose account state to callers who do not know the password.
        switch (verification.Status)
        {
            case AccountStatus.Blocked:
                return Fail(LoginOutcome.AccountBlocked, accountId, accountId, correlationId, "account_blocked");
            case AccountStatus.Archived:
                return Fail(LoginOutcome.AccountArchived, accountId, accountId, correlationId, "account_archived");
            case AccountStatus.Active:
                break;
            default:
                return Fail(LoginOutcome.Unavailable, accountId, accountId, correlationId, "account_status_unknown");
        }

        var session = sessions.Issue(accountId);
        if (!session.Succeeded || string.IsNullOrWhiteSpace(session.AccessToken))
            return Fail(LoginOutcome.Unavailable, accountId, accountId, correlationId, "session_unavailable");

        Write(accountId, accountId, correlationId, "success", null);
        return new LoginResult(LoginOutcome.Success, session.AccessToken);
    }

    private LoginResult Fail(
        LoginOutcome outcome, Guid? actorId, Guid? targetId, string correlationId, string reasonCode)
    {
        Write(actorId, targetId, correlationId, "failure", reasonCode);
        return new LoginResult(outcome);
    }

    // Email, lozinka i token nikad ne idu u audit (AUD-03).
    private void Write(Guid? actorId, Guid? targetId, string correlationId, string outcome, string? reasonCode) =>
        audit.Write(new AuditEvent(
            clock.GetUtcNow(), actorId, AuditAction, targetId?.ToString() ?? "-", outcome, correlationId, reasonCode));
}
