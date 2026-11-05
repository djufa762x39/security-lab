namespace Platform.LabVisibility.Domain;

// Sve što student sme da vidi. Ovde nikad ne dodajemo tačan odgovor (AUT-04).
public sealed record LabPublicContent(
    Guid LabId,
    Guid CourseId,
    Guid AuthorId,
    LabVisibilityState State,
    string Title,
    string Goal,
    IReadOnlyList<PublicTask> Tasks);

public sealed record PublicTask(
    string TaskId,
    int Order,
    string Prompt,
    IReadOnlyList<string> Options);
