using Platform.Api.Enums.Identity;

namespace Platform.Api.Models.Identity;

public sealed record LoginResult(LoginOutcome Outcome, Guid? UserId = null, IssuedSession? Session = null);
