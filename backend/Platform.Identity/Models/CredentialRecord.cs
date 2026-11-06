using Platform.Identity.Enums;

namespace Platform.Identity.Models;

public sealed record CredentialRecord(Guid AccountId, AccountStatus Status, string? PasswordHash);
