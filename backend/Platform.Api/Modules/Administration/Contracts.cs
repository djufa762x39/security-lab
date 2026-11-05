using Platform.Api.Modules.Access;

namespace Platform.Api.Modules.Administration;

[Flags]
public enum AdminCapability { None = 0, ViewAdministration = 1, ManageSettings = 2, ManageDelegations = 4 }
public enum GrantState { Active, Revoked }
public sealed record CourseSnapshot(Guid Id, string DisplayName, int Version);
public sealed record AdminGrant(Guid Id, Guid CourseId, Guid GranteeId, Guid GrantedBy,
    AdminCapability Capabilities, GrantState State, int Version, bool IsRoot = false);
public sealed record AdministrationView(Guid CourseId, string DisplayName, int Version, string[] Capabilities);
public sealed record GrantView(Guid Id, Guid CourseId, Guid GranteeId, string[] Capabilities, string State, int Version);
public sealed record RenameCourseRequest(string? DisplayName, int ExpectedVersion);
public sealed record DelegateRequest(Guid GranteeId, string[]? Capabilities);
public sealed record RevokeRequest(int ExpectedVersion);
public sealed record AdminWriteResult(WriteStatus Status, object? Value = null, int? Before = null, int? After = null);

// Administrative data lives here. Membership, teaching assignments, answers and grades do not.
public interface ICourseAdministrationStore
{
    PortResult<CourseSnapshot> FindCourse(Guid course);
    PortResult<AdminGrant> FindAuthority(Guid actor, Guid course);
    PortResult<AdministrationView> ReadAdministration(AdminGrant authority);
    AdminWriteResult Rename(AdminGrant authority, RenameCourseRequest request);
    AdminWriteResult Delegate(AdminGrant authority, Guid grantee, AdminCapability capabilities);
    AdminWriteResult Revoke(AdminGrant authority, Guid grantId, int expectedVersion);
}
