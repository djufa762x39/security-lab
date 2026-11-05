using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;
using Platform.Identity.Enums;

namespace Platform.Tests.Identity;

// IDN-05: prijava ne otkriva da li nalog postoji ili je zaključan; razlika je samo u auditu.
[TestFixture]
public class LoginAccountTests
{
    private const string Email = "student@example.com";
    private const string Password = "Str0ng-Password!2026";
    private const string CorrelationId = "corr-login";
    private static readonly Guid AccountId = Guid.NewGuid();

    private sealed class FakeAuthenticator(CredentialAuthenticationResult result) : ICredentialAuthenticator
    {
        public Task<CredentialAuthenticationResult> AuthenticateAsync(
            string email, string password, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class FakeSessions(bool succeeds = true) : ISessionIssuer
    {
        public int Issued { get; private set; }

        public SessionIssueResult Issue(Guid accountId)
        {
            Issued++;
            return succeeds ? new SessionIssueResult(true, "token-value") : new SessionIssueResult(false);
        }
    }

    private sealed class RecordingAudit : IAuditWriter
    {
        public List<AuditEvent> Events { get; } = new();
        public void Write(AuditEvent e) => Events.Add(e);
    }

    private static (LoginAccount Login, RecordingAudit Audit, FakeSessions Sessions) Create(
        CredentialAuthenticationResult result, bool sessionSucceeds = true)
    {
        var audit = new RecordingAudit();
        var sessions = new FakeSessions(sessionSucceeds);
        return (new LoginAccount(new FakeAuthenticator(result), sessions, audit, TimeProvider.System), audit, sessions);
    }

    [Test]
    public async Task Locked_account_gets_the_same_outcome_as_wrong_password()
    {
        var (locked, lockedAudit, lockedSessions) = Create(new(CredentialAuthenticationOutcome.LockedOut, AccountId));
        var (invalid, invalidAudit, _) = Create(new(CredentialAuthenticationOutcome.Invalid));

        var lockedResult = await locked.ExecuteAsync(Email, Password, CorrelationId);
        var invalidResult = await invalid.ExecuteAsync(Email, Password, CorrelationId);

        Assert.That(lockedResult, Is.EqualTo(invalidResult));
        Assert.That(lockedResult.Outcome, Is.EqualTo(LoginOutcome.InvalidCredentials));
        Assert.That(lockedResult.AccessToken, Is.Null);
        Assert.That(lockedSessions.Issued, Is.EqualTo(0));

        Assert.That(lockedAudit.Events.Single().Detail, Is.EqualTo("account_locked"));
        Assert.That(lockedAudit.Events.Single().Target, Is.EqualTo(AccountId.ToString()));
        Assert.That(lockedAudit.Events.Single().ActorId, Is.Null);
        Assert.That(invalidAudit.Events.Single().Detail, Is.EqualTo("invalid_credentials"));
    }

    [TestCase(null, Password)]
    [TestCase("  ", Password)]
    [TestCase(Email, null)]
    [TestCase(Email, "")]
    public async Task Missing_input_is_invalid_credentials(string? email, string? password)
    {
        var (login, _, _) = Create(new(CredentialAuthenticationOutcome.Valid, AccountId, AccountStatus.Active));

        var result = await login.ExecuteAsync(email, password, CorrelationId);

        Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.InvalidCredentials));
    }

    [Test]
    public async Task Valid_active_account_gets_a_session()
    {
        var (login, audit, sessions) = Create(new(CredentialAuthenticationOutcome.Valid, AccountId, AccountStatus.Active));

        var result = await login.ExecuteAsync(Email, Password, CorrelationId);

        Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.Success));
        Assert.That(result.AccessToken, Is.EqualTo("token-value"));
        Assert.That(sessions.Issued, Is.EqualTo(1));
        Assert.That(audit.Events.Single().Outcome, Is.EqualTo("success"));
        Assert.That(audit.Events.Single().ActorId, Is.EqualTo(AccountId));
    }

    [TestCase(AccountStatus.Blocked, LoginOutcome.AccountBlocked)]
    [TestCase(AccountStatus.Archived, LoginOutcome.AccountArchived)]
    public async Task Inactive_account_gets_no_session(AccountStatus status, LoginOutcome expected)
    {
        var (login, _, sessions) = Create(new(CredentialAuthenticationOutcome.Valid, AccountId, status));

        var result = await login.ExecuteAsync(Email, Password, CorrelationId);

        Assert.That(result.Outcome, Is.EqualTo(expected));
        Assert.That(sessions.Issued, Is.EqualTo(0));
    }

    [Test]
    public async Task Valid_credentials_without_status_fail_closed()
    {
        var (login, _, sessions) = Create(new(CredentialAuthenticationOutcome.Valid, AccountId));

        var result = await login.ExecuteAsync(Email, Password, CorrelationId);

        Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.Unavailable));
        Assert.That(sessions.Issued, Is.EqualTo(0));
    }

    [Test]
    public async Task Unavailable_authenticator_is_unavailable()
    {
        var (login, _, _) = Create(new(CredentialAuthenticationOutcome.Unavailable));

        var result = await login.ExecuteAsync(Email, Password, CorrelationId);

        Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.Unavailable));
    }

    [Test]
    public async Task Audit_never_contains_email_or_password()
    {
        var outcomes = new CredentialAuthenticationResult[]
        {
            new(CredentialAuthenticationOutcome.Invalid),
            new(CredentialAuthenticationOutcome.LockedOut, AccountId),
            new(CredentialAuthenticationOutcome.Valid, AccountId, AccountStatus.Active)
        };

        foreach (var outcome in outcomes)
        {
            var (login, audit, _) = Create(outcome);
            await login.ExecuteAsync(Email, Password, CorrelationId);

            var text = string.Join("|", audit.Events.Select(e => e.ToString()));
            Assert.That(text, Does.Not.Contain(Email));
            Assert.That(text, Does.Not.Contain(Password));
            Assert.That(text, Does.Not.Contain("token-value"));
        }
    }
}
