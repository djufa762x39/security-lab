using Microsoft.Extensions.DependencyInjection;
using Platform.Api.Modules.SessionSecurity;
using Platform.SessionSecurity.Contracts;
using Platform.SessionSecurity.Domain;
using Platform.SessionSecurity.Infrastructure;
using Platform.Tests.SessionSecurity.TestDoubles;

// IDN-11 TIM 09
namespace Platform.Tests.SessionSecurity.Api;

[TestFixture]
public sealed class SessionSecurityModuleTests
{
    private const string CorrelationId = "corr-1";

    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public void AddSessionSecurity_WhenResolvedInScope_ProvidesRotationUseCase()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var useCase = scope.ServiceProvider.GetRequiredService<IRotateSessionUseCase>();

        Assert.That(useCase, Is.Not.Null);
    }

    [Test]
    public void AddSessionSecurity_WhenResolvedInScope_ProvidesLoginHistoryUseCase()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var useCase = scope.ServiceProvider.GetRequiredService<ISearchLoginHistoryUseCase>();

        Assert.That(useCase, Is.Not.Null);
    }

    [Test]
    public async Task UnavailableSessionSecurityAuditWriter_TryWriteAsync_ReturnsFalse()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<ISessionSecurityAuditWriter>();

        var result = await writer.TryWriteAsync(new SessionSecurityAuditEntry(
            SessionSecurityAuditEvents.SessionRotated, "user-a", RotationTrigger.Login,
            SessionRotationCodes.Rotated, Now, CorrelationId));

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task AddSessionSecurity_WithUnavailableAudit_RotationIsRejected()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        const string preLoginToken = "pre-login-token-value";

        var store = (InMemorySessionRotationStore)scope.ServiceProvider
            .GetRequiredService<ISessionRotationStore>();
        var tokenGenerator = scope.ServiceProvider.GetRequiredService<ISessionTokenGenerator>();
        store.Add(new RotatableSession(
            Guid.NewGuid(),
            SubjectId: null,
            tokenGenerator.ComputeHash(preLoginToken),
            SessionPrivilegeLevel.PreAuthentication,
            Now.AddMinutes(-5),
            Now.AddHours(8)));

        var useCase = scope.ServiceProvider.GetRequiredService<IRotateSessionUseCase>();
        var result = await useCase.RotateAsync(new RotateSessionRequest(
            preLoginToken, "user-a", RotationTrigger.Login, SessionPrivilegeLevel.Authenticated, CorrelationId));

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Code, Is.EqualTo(SessionRotationCodes.AuditFailed));
        });
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddSessionSecurity();
        return services.BuildServiceProvider();
    }
}
