namespace Platform.Identity.Enums;

public enum PasswordCheckOutcome
{
    Valid,
    Invalid,

    // Samo za interni audit. Klijent dobija isti odgovor kao za Invalid (IDN-05).
    LockedOut
}
