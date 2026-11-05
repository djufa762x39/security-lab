using System.Collections.Concurrent;
using System.Text.Json;
using Platform.Api.Modules.Access;
using Platform.Api.Modules.Administration;

namespace Platform.Api.Modules.Kt1;

/// <summary>Repeatable process-local simulator. Restart the AccessKt1 profile to reset all state.</summary>
public sealed class DemoStore(ILogger<DemoStore> logger) : IAccountReader, IMembershipReader,
    IAttemptStore, ICourseAdministrationStore, ISecurityAudit
{
    public static Guid Id(int group, int number) => Guid.Parse($"{group}0000000-0000-0000-0000-{number:000000000000}");
    public static readonly Guid Ana = Id(1, 1), Marko = Id(1, 2), Teacher = Id(1, 3),
        ForeignTeacher = Id(1, 4), Blocked = Id(1, 5), Assistant = Id(1, 6),
        Admin = Id(1, 7), OtherAdmin = Id(1, 8), DelegateUser = Id(1, 9);
    public static readonly Guid Course = Id(2, 1), OtherCourse = Id(2, 2), MissingCourse = Id(2, 99);
    public static readonly Guid AnaAttempt = Id(3, 1), MarkoAttempt = Id(3, 2),
        DraftAttempt = Id(3, 4), CompletedAttempt = Id(3, 5), MissingAttempt = Id(3, 99);
    public static readonly Guid RootGrant = Id(4, 1), OtherRootGrant = Id(4, 2);
    private readonly object gate = new();
    private readonly Dictionary<Guid, AccountSnapshot> accounts = new[]
        { Ana, Marko, Teacher, ForeignTeacher, Blocked, Assistant, Admin, OtherAdmin, DelegateUser }
        .ToDictionary(id => id, id => new AccountSnapshot(id, id != Blocked));
    private readonly Dictionary<(Guid, Guid), MembershipSnapshot> memberships = new()
    {
        [(Ana, Course)] = new(Ana, Course, CourseRole.Student, true),
        [(Marko, Course)] = new(Marko, Course, CourseRole.Student, true),
        [(Teacher, Course)] = new(Teacher, Course, CourseRole.Teacher, true),
        [(ForeignTeacher, OtherCourse)] = new(ForeignTeacher, OtherCourse, CourseRole.Teacher, true),
        [(Blocked, Course)] = new(Blocked, Course, CourseRole.Student, true),
        [(Assistant, Course)] = new(Assistant, Course, CourseRole.Assistant, true)
    };
    private readonly Dictionary<Guid, (AttemptSnapshot Snapshot, string Answer)> attempts = new()
    {
        [AnaAttempt] = (new(AnaAttempt, Ana, Course, AttemptState.Submitted, 1), "Synthetic Ana answer."),
        [MarkoAttempt] = (new(MarkoAttempt, Marko, Course, AttemptState.Submitted, 1), "Synthetic Marko answer."),
        [DraftAttempt] = (new(DraftAttempt, Ana, Course, AttemptState.InProgress, 1), "Synthetic draft."),
        [CompletedAttempt] = (new(CompletedAttempt, Ana, Course, AttemptState.Completed, 1), "Synthetic completed answer.")
    };
    private readonly Dictionary<Guid, CourseSnapshot> courses = new()
    { [Course] = new(Course, "Security Lab A", 1), [OtherCourse] = new(OtherCourse, "Security Lab B", 1) };
    private readonly Dictionary<Guid, AdminGrant> grants = new()
    {
        [RootGrant] = new(RootGrant, Course, Admin, Admin, All, GrantState.Active, 1, true),
        [OtherRootGrant] = new(OtherRootGrant, OtherCourse, OtherAdmin, OtherAdmin, All, GrantState.Active, 1, true)
    };
    private const AdminCapability All = AdminCapability.ViewAdministration | AdminCapability.ManageSettings | AdminCapability.ManageDelegations;
    private readonly ConcurrentQueue<SecurityEvent> events = new();
    public IReadOnlyList<SecurityEvent> Events => events.ToArray();
    // Test-double failures are configured in tests only; no HTTP switch can enable them.
    public bool AccountsUnavailable { get; set; }
    public bool MembershipsUnavailable { get; set; }
    public bool AttemptsUnavailable { get; set; }
    public bool AdministrationUnavailable { get; set; }
    public PortResult<AccountSnapshot> FindAccount(Guid id)
    {
        lock (gate) return AccountsUnavailable ? PortResult<AccountSnapshot>.Offline() :
            accounts.TryGetValue(id, out var a) ? PortResult<AccountSnapshot>.Found(a) : PortResult<AccountSnapshot>.Missing();
    }
    public void SetAccountActive(Guid id, bool active)
    { lock (gate) accounts[id] = new(id, active); }
    public PortResult<MembershipSnapshot> FindMembership(Guid actor, Guid course)
    {
        lock (gate) return MembershipsUnavailable ? PortResult<MembershipSnapshot>.Offline() :
            memberships.TryGetValue((actor, course), out var m) ? PortResult<MembershipSnapshot>.Found(m) : PortResult<MembershipSnapshot>.Missing();
    }
    public PortResult<AttemptSnapshot> FindAttempt(Guid id)
    {
        lock (gate) return AttemptsUnavailable ? PortResult<AttemptSnapshot>.Offline() : attempts.TryGetValue(id, out var a)
            ? PortResult<AttemptSnapshot>.Found(a.Snapshot) : PortResult<AttemptSnapshot>.Missing();
    }
    public PortResult<AttemptView> ReadAnswer(AttemptSnapshot snapshot)
    {
        lock (gate) return !AttemptsUnavailable && attempts.TryGetValue(snapshot.Id, out var a) && a.Snapshot == snapshot
            ? PortResult<AttemptView>.Found(new(snapshot.Id, a.Answer, snapshot.Version)) : PortResult<AttemptView>.Missing();
    }
    public WriteResult UpdateDraft(AttemptSnapshot snapshot, int expectedVersion, string answer)
    {
        lock (gate)
        {
            if (AttemptsUnavailable) return new(WriteStatus.Unavailable);
            if (string.IsNullOrWhiteSpace(answer) || answer.Length > AttemptService.MaxAnswerLength) return new(WriteStatus.Denied);
            if (!attempts.TryGetValue(snapshot.Id, out var a) || a.Snapshot != snapshot ||
                snapshot.Version != expectedVersion || snapshot.State != AttemptState.InProgress) return new(WriteStatus.Conflict);
            var next = snapshot with { Version = checked(snapshot.Version + 1) };
            attempts[next.Id] = (next, answer);
            return new(WriteStatus.Updated, new(next.Id, answer, next.Version));
        }
    }
    public PortResult<CourseSnapshot> FindCourse(Guid course)
    {
        lock (gate) return AdministrationUnavailable ? PortResult<CourseSnapshot>.Offline() : courses.TryGetValue(course, out var c)
            ? PortResult<CourseSnapshot>.Found(c) : PortResult<CourseSnapshot>.Missing();
    }
    public PortResult<AdminGrant> FindAuthority(Guid actor, Guid course)
    {
        lock (gate)
        {
            if (AdministrationUnavailable) return PortResult<AdminGrant>.Offline();
            var a = grants.Values.SingleOrDefault(g => g.CourseId == course && g.GranteeId == actor && g.State == GrantState.Active);
            return a is null ? PortResult<AdminGrant>.Missing() : PortResult<AdminGrant>.Found(a);
        }
    }
    private bool HasAuthority(AdminGrant a, AdminCapability capability) => !AdministrationUnavailable && !AccountsUnavailable &&
        accounts.TryGetValue(a.GranteeId, out var account) && account.IsActive &&
        grants.TryGetValue(a.Id, out var current) && current == a && courses.ContainsKey(a.CourseId) &&
        AdministrationPolicy.Valid(current, a.GranteeId, a.CourseId, capability);
    public static string[] Names(AdminCapability caps) => Enum.GetValues<AdminCapability>()
        .Where(c => c != AdminCapability.None && caps.HasFlag(c)).Select(c => c.ToString()).ToArray();
    public PortResult<AdministrationView> ReadAdministration(AdminGrant a)
    {
        lock (gate)
        {
            if (!HasAuthority(a, AdminCapability.ViewAdministration)) return PortResult<AdministrationView>.Missing();
            var c = courses[a.CourseId];
            return PortResult<AdministrationView>.Found(new(c.Id, c.DisplayName, c.Version, Names(a.Capabilities)));
        }
    }
    public AdminWriteResult Rename(AdminGrant a, RenameCourseRequest input)
    {
        lock (gate)
        {
            if (!HasAuthority(a, AdminCapability.ManageSettings) || string.IsNullOrWhiteSpace(input.DisplayName) ||
                input.DisplayName.Length > 100 || input.DisplayName.Any(char.IsControl)) return new(WriteStatus.Denied);
            var before = courses[a.CourseId];
            if (before.Version != input.ExpectedVersion) return new(WriteStatus.Conflict);
            var next = before with { DisplayName = input.DisplayName.Trim(), Version = checked(before.Version + 1) };
            courses[next.Id] = next;
            return new(WriteStatus.Updated, new AdministrationView(next.Id, next.DisplayName, next.Version, Names(a.Capabilities)), before.Version, next.Version);
        }
    }
    public AdminWriteResult Delegate(AdminGrant a, Guid grantee, AdminCapability caps)
    {
        lock (gate)
        {
            var allowed = AdminCapability.ViewAdministration | AdminCapability.ManageSettings;
            if (!HasAuthority(a, AdminCapability.ManageDelegations) || grantee == a.GranteeId ||
                !accounts.TryGetValue(grantee, out var account) || !account.IsActive ||
                !caps.HasFlag(AdminCapability.ViewAdministration) || (caps & ~allowed) != 0) return new(WriteStatus.Denied);
            if (grants.Values.Any(g => g.CourseId == a.CourseId && g.GranteeId == grantee && g.State == GrantState.Active))
                return new(WriteStatus.Conflict);
            var next = new AdminGrant(Guid.NewGuid(), a.CourseId, grantee, a.GranteeId, caps, GrantState.Active, 1);
            grants.Add(next.Id, next);
            return new(WriteStatus.Updated, View(next), 0, 1);
        }
    }
    public AdminWriteResult Revoke(AdminGrant a, Guid id, int expectedVersion)
    {
        lock (gate)
        {
            if (!HasAuthority(a, AdminCapability.ManageDelegations) || !grants.TryGetValue(id, out var before) ||
                before.CourseId != a.CourseId || before.IsRoot || before.State != GrantState.Active) return new(WriteStatus.Denied);
            if (before.Version != expectedVersion) return new(WriteStatus.Conflict);
            var next = before with { State = GrantState.Revoked, Version = checked(before.Version + 1) };
            grants[id] = next;
            return new(WriteStatus.Updated, View(next), before.Version, next.Version);
        }
    }
    private static GrantView View(AdminGrant g) => new(g.Id, g.CourseId, g.GranteeId, Names(g.Capabilities), g.State.ToString(), g.Version);
    public void Write(SecurityEvent entry)
    {
        events.Enqueue(entry);
        while (events.Count > 256) events.TryDequeue(out _);
        logger.LogInformation("KT1_AUDIT_JSON:{Entry}", JsonSerializer.Serialize(entry));
    }
}
