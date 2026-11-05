using Microsoft.Extensions.Configuration;
using Platform.Access.Domain.Models;

namespace Platform.Identity.Services;

/// Administratori su id-jevi iz konfiguracije Identity:AdminAccountIds.
internal static class AdminAccounts
{
    public const string ConfigKey = "Identity:AdminAccountIds";

    public static bool IsAdministrator(IConfiguration configuration, SubjectRef? subject)
    {
        var admins = configuration.GetSection(ConfigKey).Get<string[]>() ?? [];
        return subject is not null && admins.Contains(subject.Id, StringComparer.Ordinal);
    }
}
