using System.Text.Json;
using Platform.LabMetadata;

namespace Platform.Api.Modules.RevisionReviews;

public sealed record PublicationDefinition(
    string? Type, string? Title, string? Goal, LogEventDefinition?[]? Events, string? Question,
    EventSetConfiguration? Evaluator)
{
    public const string SupportedType = nameof(LaboratoryType.LogAnalysis);
    public const string EvaluatorKind = "event-set";

    public IReadOnlyList<PublicationError> Validate()
    {
        var errors = new List<PublicationError>();
        if (Type != SupportedType)
            errors.Add(new("type", "Only LogAnalysis is supported for publication."));
        RequireText(Title, 200, "title", errors);
        RequireText(Goal, 2000, "goal", errors);
        RequireText(Question, 2000, "question", errors);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (Events is not { Length: >= 1 and <= 100 })
            errors.Add(new("events", "Provide between 1 and 100 log events."));
        else
        {
            foreach (var entry in Events)
            {
                if (entry?.Id is not { Length: >= 1 and <= 64 } id ||
                    id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')) ||
                    !ids.Add(id) || entry.Timestamp is null || entry.Timestamp == default(DateTimeOffset) ||
                    !ValidText(entry.Message, 1000))
                    errors.Add(new("events", "Each event requires a unique ID, a timestamp and a message."));
            }
        }

        if (Evaluator is not { Kind: EvaluatorKind, Version: 1 })
            errors.Add(new("evaluator", "LogAnalysis requires event-set evaluator version 1."));
        if (Evaluator?.ExpectedEventIds is not { Length: >= 1 and <= 100 } expected ||
            expected.Any(id => id is null || !ids.Contains(id)) ||
            expected.Distinct(StringComparer.Ordinal).Count() != expected.Length)
            errors.Add(new("evaluator.expectedEventIds", "Provide distinct IDs of events in this definition."));
        return errors;
    }

    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(this, JsonSerializerOptions.Web);

    internal static PublicationDefinition? Read(byte[] snapshot) =>
        JsonSerializer.Deserialize<PublicationDefinition>(snapshot, JsonSerializerOptions.Web);

    private static void RequireText(string? value, int maxLength, string field, List<PublicationError> errors)
    {
        if (!ValidText(value, maxLength))
            errors.Add(new(field, $"Enter non-empty text of at most {maxLength} characters."));
    }

    private static bool ValidText(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength && !value.Contains('\0');
}

public sealed record LogEventDefinition(string? Id, DateTimeOffset? Timestamp, string? Message);
public sealed record EventSetConfiguration(string? Kind, int Version, string?[]? ExpectedEventIds);
