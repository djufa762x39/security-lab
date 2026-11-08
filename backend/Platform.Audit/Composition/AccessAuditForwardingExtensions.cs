using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Access.Contracts;
using Platform.Audit.Contracts;
using Platform.Audit.Infrastructure.AccessForwarding;

namespace Platform.Audit.Composition;

/// <summary>
/// Explicit composition for the S-13 Access forwarder. The host calls
/// <see cref="AddAccessAuditForwarding"/> once, after <c>AddAccessControl()</c> and
/// <c>AddAuditPersistence(...)</c>. A wrong order, a missing module or a second call is a
/// startup error, never a silent DI override.
/// <para>
/// Not called from Platform.Api yet: wiring waits for decision D2 with team 3
/// (see docs/components/audit/S13-ACCESS-FORWARDER.md).
/// </para>
/// </summary>
public static class AccessAuditForwardingExtensions
{
    public static IServiceCollection AddAccessAuditForwarding(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(AccessAuditForwardingRegistered)))
        {
            throw new InvalidOperationException("AddAccessAuditForwarding() has already been called.");
        }

        var accessWriters = services
            .Where(descriptor => descriptor.ServiceType == typeof(IAccessAuditWriter) && !descriptor.IsKeyedService)
            .ToArray();
        if (accessWriters.Length != 1)
        {
            throw new InvalidOperationException(
                "AddAccessAuditForwarding() needs exactly one IAccessAuditWriter registration. Call AddAccessControl() first.");
        }

        if (!services.Any(descriptor => descriptor.ServiceType == typeof(ITrustedAuditWriter)))
        {
            throw new InvalidOperationException(
                "AddAccessAuditForwarding() needs ITrustedAuditWriter. Call AddAuditPersistence(...) first.");
        }

        var original = accessWriters[0];
        if (original.Lifetime != ServiceLifetime.Singleton)
        {
            throw new InvalidOperationException(
                "AddAccessAuditForwarding() expects the Access audit writer to be a singleton.");
        }

        services.Remove(original);
        services.AddSingleton<AccessAuditForwardingRegistered>();
        services.AddMetrics();
        services.TryAddSingleton<AccessForwardingMetrics>();
        services.AddSingleton<IAccessAuditWriter>(provider => new AccessDecisionAuditForwarder(
            CreateOriginal(provider, original),
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetService<ILogger<AccessDecisionAuditForwarder>>()
                ?? NullLogger<AccessDecisionAuditForwarder>.Instance,
            provider.GetRequiredService<AccessForwardingMetrics>()));
        services.AddSingleton<IHostedService, AccessAuditForwardingStartupCheck>();

        return services;
    }

    private static IAccessAuditWriter CreateOriginal(IServiceProvider provider, ServiceDescriptor original)
    {
        if (original.ImplementationInstance is IAccessAuditWriter instance)
            return instance;

        if (original.ImplementationFactory is not null)
            return (IAccessAuditWriter)original.ImplementationFactory(provider);

        return (IAccessAuditWriter)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!);
    }

    private sealed class AccessAuditForwardingRegistered
    {
    }
}

/// <summary>
/// Fails host start when a later registration replaced the forwarder, so the explicit
/// replacement is verified instead of assumed.
/// </summary>
internal sealed class AccessAuditForwardingStartupCheck(IAccessAuditWriter accessAuditWriter) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (accessAuditWriter is not AccessDecisionAuditForwarder)
        {
            throw new InvalidOperationException(
                "The Access audit writer is not the S-13 forwarder. Check the order of DI registrations.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
