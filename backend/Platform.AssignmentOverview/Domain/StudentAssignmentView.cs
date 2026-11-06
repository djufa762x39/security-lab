namespace Platform.AssignmentOverview.Domain;

// ASN-02 TIM 16
// Podaci koje student sme da vidi o jednoj dodeli.
// Namerno nema internih pravila dodele (npr. AllowStartAfterDeadline).
public sealed record StudentAssignmentView(
    Guid AssignmentId,
    string CourseId,
    string PublishedLabRevisionId,
    DateTimeOffset? AvailableFromUtc,
    DateTimeOffset DueAtUtc,
    int MaxAttempts);