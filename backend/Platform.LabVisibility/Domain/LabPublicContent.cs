namespace Platform.LabVisibility.Domain;

// Sadrži samo podatke koje student sme da vidi.
public sealed record LabPublicContent
{
    public Guid LabId { get; }
    public Guid CourseId { get; }
    public Guid AuthorId { get; }
    public LabVisibilityState State { get; }
    public string Title { get; }
    public string Goal { get; }
    public IReadOnlyList<PublicTask> Tasks { get; }

    private LabPublicContent(
        Guid labId,
        Guid courseId,
        Guid authorId,
        LabVisibilityState state,
        string title,
        string goal,
        IReadOnlyList<PublicTask> tasks)
    {
        LabId = labId;
        CourseId = courseId;
        AuthorId = authorId;
        State = state;
        Title = title;
        Goal = goal;
        Tasks = tasks;
    }

    public static LabPublicContent Create(
        Guid labId,
        Guid courseId,
        Guid authorId,
        LabVisibilityState state,
        string title,
        string goal,
        IReadOnlyList<PublicTask> tasks)
    {
        if (labId == Guid.Empty)
            throw new ArgumentException(
                "ID laboratorije je obavezan.", nameof(labId));

        if (courseId == Guid.Empty)
            throw new ArgumentException(
                "ID kursa je obavezan.", nameof(courseId));

        if (authorId == Guid.Empty)
            throw new ArgumentException(
                "ID autora je obavezan.", nameof(authorId));

        if (!Enum.IsDefined(state))
            throw new ArgumentException(
                "Nepoznato stanje laboratorije.", nameof(state));

        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException(
                "Naslov je obavezan.", nameof(title));

        if (string.IsNullOrWhiteSpace(goal))
            throw new ArgumentException(
                "Cilj je obavezan.", nameof(goal));

        ArgumentNullException.ThrowIfNull(tasks);

        var taskIds = new HashSet<string>(StringComparer.Ordinal);
        var taskCopies = new List<PublicTask>();

        foreach (var task in tasks)
        {
            if (task is null)
                throw new ArgumentException(
                    "Zadatak ne sme biti null.", nameof(tasks));

            if (!taskIds.Add(task.TaskId))
                throw new ArgumentException(
                    "ID zadatka mora biti jedinstven.", nameof(tasks));

            taskCopies.Add(task);
        }

        // Ako je Order isti, TaskId obezbeđuje deterministički redosled.
        var orderedTasks = taskCopies
            .OrderBy(task => task.Order)
            .ThenBy(task => task.TaskId, StringComparer.Ordinal)
            .ToArray();

        return new LabPublicContent(
            labId,
            courseId,
            authorId,
            state,
            title.Trim(),
            goal.Trim(),
            Array.AsReadOnly(orderedTasks));
    }
}

// Javni tekst zadatka i ponuđene opcije, bez tačnog odgovora.
public sealed record PublicTask
{
    public string TaskId { get; }
    public int Order { get; }
    public string Prompt { get; }
    public IReadOnlyList<string> Options { get; }

    public PublicTask(
        string taskId,
        int order,
        string prompt,
        IReadOnlyList<string> options)
    {
        if (string.IsNullOrWhiteSpace(taskId))
            throw new ArgumentException(
                "ID zadatka je obavezan.", nameof(taskId));

        if (order < 0)
            throw new ArgumentException(
                "Redosled ne sme biti negativan.", nameof(order));

        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException(
                "Tekst zadatka je obavezan.", nameof(prompt));

        ArgumentNullException.ThrowIfNull(options);

        var optionCopies = new List<string>();

        foreach (var option in options)
        {
            if (string.IsNullOrWhiteSpace(option))
                throw new ArgumentException(
                    "Ponuđena opcija ne sme biti prazna.", nameof(options));

            optionCopies.Add(option);
        }

        TaskId = taskId.Trim();
        Order = order;
        Prompt = prompt.Trim();
        Options = Array.AsReadOnly(optionCopies.ToArray());
    }
}