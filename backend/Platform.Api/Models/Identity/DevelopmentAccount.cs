using Platform.Identity.Enums;

namespace Platform.Api.Models.Identity;

public sealed record DevelopmentAccount(Guid Id, string Email, AccountRole Role, AccountStatus Status);
