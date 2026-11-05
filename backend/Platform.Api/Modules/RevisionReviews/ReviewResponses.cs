namespace Platform.Api.Modules.RevisionReviews;

public sealed record ReviewPerson(Guid Id, string DisplayName);
public sealed record ReviewPolicy(bool RequiresSeparateApprover);
public sealed record ReviewPreview(string? Type, string? Goal, LogEventDefinition?[]? Events, string? Question);
public sealed record ReviewDecision(Guid Id, string Type, ReviewPerson Actor, DateTimeOffset At,
    string? Comment, string ContentHash, int? RevisionNumber);

public record ReviewSummary(Guid Id, Guid? LabId, string LabTitle, Guid CourseId, string CourseName,
    string Status, int? RevisionNumber, ReviewPerson Author, DateTimeOffset? SubmittedAt, DateTimeOffset? UpdatedAt);

public sealed record ReviewDetail : ReviewSummary
{
    public ReviewDetail(ReviewSummary summary, string contentHash, string rowVersion, ReviewPolicy policy,
        IReadOnlyList<string> allowedActions, IReadOnlyList<ReviewDecision> decisions, ReviewPreview? preview)
        : base(summary)
    {
        ContentHash = contentHash;
        RowVersion = rowVersion;
        Policy = policy;
        AllowedActions = allowedActions;
        Decisions = decisions;
        Preview = preview;
    }

    public string ContentHash { get; }
    public string RowVersion { get; }
    public ReviewPolicy Policy { get; }
    public IReadOnlyList<string> AllowedActions { get; }
    public IReadOnlyList<ReviewDecision> Decisions { get; }
    public ReviewPreview? Preview { get; }
}
