using Platform.LabVisibility.Domain;

namespace Platform.LabVisibility.Application.StudentView;

public static class StudentViewProjector
{
	public static StudentLabView Project(LabPublicContent content)
	{
		ArgumentNullException.ThrowIfNull(content);

		var tasks = content.Tasks
			.Select(task => new StudentTaskView(
				task.TaskId,
				task.Order,
				task.Prompt,
				Array.AsReadOnly(task.Options.ToArray())))
			.ToArray();

		return new StudentLabView(
			content.LabId,
			content.Title,
			content.Goal,
			Array.AsReadOnly(tasks));
	}
}