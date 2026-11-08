using Platform.Api.Enums.Identity;

namespace Platform.Api.Models.Identity;

public sealed record ValidateSession(
    Guid Handle,
    Guid UserId,
    AuthenticationLevel Level
);
