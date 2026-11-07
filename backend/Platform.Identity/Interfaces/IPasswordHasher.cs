namespace Platform.Identity.Interfaces;

/// <summary>Password hashing for the Identity module. Single place that knows the algorithm (BCrypt).</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>
    /// Checks a password against a stored hash. When <paramref name="passwordHash"/> is null or malformed
    /// the same amount of work is still done and the result is false, so unknown e-mails and wrong
    /// passwords cost the same time.
    /// </summary>
    bool Verify(string password, string? passwordHash);
}
