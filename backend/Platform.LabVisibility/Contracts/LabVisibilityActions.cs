namespace Platform.LabVisibility.Contracts;

public static class LabVisibilityActions
{
    public const string StudentView = "lab.student.view";
    public const string Preview = "lab.preview";
    public const string KeyRead = "lab.key.read";

    public static readonly IReadOnlyList<string> All = [StudentView, Preview, KeyRead];
}
