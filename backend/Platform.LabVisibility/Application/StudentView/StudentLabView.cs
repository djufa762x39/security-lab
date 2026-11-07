namespace Platform.LabVisibility.Application.StudentView;

// Oblik koji ide klijentu (studentski prikaz i pregled pre objave). Samo javna polja.
public sealed record StudentLabView(
    Guid LabId,
    string Title,
    string Goal,
    IReadOnlyList<StudentTaskView> Tasks);

public sealed record StudentTaskView(
    string TaskId,
    int Order,
    string Prompt,
    IReadOnlyList<string> Options);
