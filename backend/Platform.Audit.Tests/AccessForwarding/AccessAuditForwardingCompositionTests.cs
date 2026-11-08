using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Audit.Composition;
using Platform.Audit.Infrastructure.AccessForwarding;

namespace Platform.Audit.Tests.AccessForwarding;

/// <summary>
/// S-13 explicit DI replacement (T10 startup). Uses the real Access and Audit compositions;
/// no database connection is opened because nothing is written.
/// </summary>
[TestFixture]
public sealed class AccessAuditForwardingCompositionTests
{
    private static readonly ServiceProviderOptions ValidatingOptions = new()
    {
        ValidateScopes = true,
        ValidateOnBuild = true
    };

    [Test]
    public async Task AddAccessAuditForwarding_BuildsWithScopeValidation_AndReplacesAccessWriter()
    {
        await using var provider = CreateServices().AddAccessAuditForwarding().BuildServiceProvider(ValidatingOptions);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.GetRequiredService<IAccessAuditWriter>(), Is.InstanceOf<AccessDecisionAuditForwarder>());
            Assert.That(provider.GetRequiredService<IAccessDecision>(), Is.Not.Null, "singleton decision resolves without captive scoped writer");
        }
    }

    [Test]
    public async Task AddAccessAuditForwarding_StartupCheckPasses()
    {
        await using var provider = CreateServices().AddAccessAuditForwarding().BuildServiceProvider(ValidatingOptions);
        var check = provider.GetServices<IHostedService>().OfType<AccessAuditForwardingStartupCheck>().Single();

        Assert.DoesNotThrowAsync(() => check.StartAsync(CancellationToken.None));
    }

    [Test]
    public void AddAccessAuditForwarding_CalledTwice_Throws()
    {
        var services = CreateServices().AddAccessAuditForwarding();

        Assert.Throws<InvalidOperationException>(() => services.AddAccessAuditForwarding());
    }

    [Test]
    public void AddAccessAuditForwarding_WithoutAccessControl_Throws()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddAuditPersistence(connectionString: null);

        Assert.Throws<InvalidOperationException>(() => services.AddAccessAuditForwarding());
    }

    [Test]
    public void AddAccessAuditForwarding_WithoutAuditPersistence_Throws()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddAccessControl();

        Assert.Throws<InvalidOperationException>(() => services.AddAccessAuditForwarding());
    }

    [Test]
    public async Task StartupCheck_WhenLaterRegistrationReplacesForwarder_FailsStart()
    {
        var services = CreateServices().AddAccessAuditForwarding();
        services.AddSingleton<IAccessAuditWriter>(new RecordingAccessAuditWriter());
        await using var provider = services.BuildServiceProvider(ValidatingOptions);
        var check = provider.GetServices<IHostedService>().OfType<AccessAuditForwardingStartupCheck>().Single();

        Assert.ThrowsAsync<InvalidOperationException>(() => check.StartAsync(CancellationToken.None));
    }

    private static IServiceCollection CreateServices()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddAccessControl();
        services.AddAuditPersistence(connectionString: null);
        return services;
    }
}
