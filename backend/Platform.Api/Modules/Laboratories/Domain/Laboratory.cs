namespace Platform.Api.Modules.Laboratories.Domain;

public sealed class Laboratory
{
    public const int MaxTitleLength = 200;

    public Guid Id { get; }
    public string Title { get; private set; }

    public Laboratory(string title) : this(Guid.NewGuid(), title)
    {
    }

    // Allows server-side reconstruction of a laboratory with its existing identity.
    public Laboratory(Guid id, string title)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Laboratory ID is required.", nameof(id));
        }

        Id = id;
        Title = ValidateTitle(title);
    }

    public void Rename(string title)
    {
        Title = ValidateTitle(title);
    }

    private static string ValidateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Laboratory title is required.", nameof(title));
        }

        string normalized = title.Trim();
        if (normalized.Length > MaxTitleLength)
        {
            throw new ArgumentException($"Laboratory title must not exceed {MaxTitleLength} characters.", nameof(title));
        }

        return normalized;
    }
}
