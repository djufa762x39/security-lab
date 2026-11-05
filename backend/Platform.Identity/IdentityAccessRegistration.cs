using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Infrastructure.Catalog;
using Platform.Access.Infrastructure.Policies;
using Platform.Identity.Services;

namespace Platform.Identity;

public static class IdentityAccessRegistration
{
    public static IServiceProvider RegisterIdentityAccess(this IServiceProvider services)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var catalog = services.GetRequiredService<InMemoryActionCatalog>();
        var policies = services.GetRequiredService<InMemoryAccessPolicyRegistry>();

        catalog.Register(CreateAccountPolicy.ActionId);
        policies.Register(CreateAccountPolicy.ActionId, new CreateAccountPolicy(configuration));

        // IDN-05: administratorsko otključavanje naloga
        catalog.Register(UnlockAccountPolicy.ActionId);
        policies.Register(UnlockAccountPolicy.ActionId, new UnlockAccountPolicy(configuration));

        return services;
    }
}
