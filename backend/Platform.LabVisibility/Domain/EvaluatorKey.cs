namespace Platform.LabVisibility.Domain;

// Tačni odgovori. Poseban tip i posebna tabela; nikad ne napušta server (AUT-04).
public sealed record EvaluatorKey(
    Guid LabId,
    IReadOnlyList<TaskAnswerKey> Answers);

public sealed record TaskAnswerKey(
    string TaskId,
    string ExpectedAnswer);
