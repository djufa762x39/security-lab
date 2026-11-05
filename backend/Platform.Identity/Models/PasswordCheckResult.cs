using Platform.Identity.Enums;

namespace Platform.Identity.Models;

public sealed record PasswordCheckResult(
    PasswordCheckOutcome Outcome,
    Guid? AccountId,
    AccountStatus? Status)
{
    public static PasswordCheckResult Valid(Guid id, AccountStatus status) =>
        new(PasswordCheckOutcome.Valid, id, status);

    public static PasswordCheckResult Invalid() =>
        new(PasswordCheckOutcome.Invalid, null, null);

    public static PasswordCheckResult LockedOut(Guid id) =>
        new(PasswordCheckOutcome.LockedOut, id, null);
}
