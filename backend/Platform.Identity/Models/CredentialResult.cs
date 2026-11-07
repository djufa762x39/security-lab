using Platform.Identity.Enums;

namespace Platform.Identity.Models;

/// <param name="IsValid">True only when the account exists and the password matches.</param>
public sealed record CredentialResult(bool IsValid, Guid AccountId = default, AccountStatus Status = default)
{
    public static CredentialResult Invalid { get; } = new(false);
}
