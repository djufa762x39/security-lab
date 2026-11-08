using Microsoft.AspNetCore.Builder;

namespace Platform.Access.AspNet;

public static class LegacyAuthorizationIntegrationExtensions
{
    public static TBuilder DelegatesToLegacyAuthorization<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WithMetadata(new DelegatedAuthorizationAttribute());
        return builder;
    }
}
