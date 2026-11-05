namespace Platform.Api.Modules.Laboratories.Application;

// Ugovor iz specifikacije. KT1: fake, kasnije IDN modul (tim 18).
public interface IIdentityContext
{
    bool IsAuthenticated { get; }
    string? UserId { get; }
    PlatformRole? Role { get; }
}

public enum PlatformRole
{
    Student,
    Teacher,
    Assistant,
    Administrator
}