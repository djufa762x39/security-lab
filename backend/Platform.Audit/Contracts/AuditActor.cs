namespace Platform.Audit.Contracts;

/// <summary>
/// <see cref="AuditActorKind.User"/> and <see cref="AuditActorKind.Service"/> require an Id;
/// <see cref="AuditActorKind.Anonymous"/> must not have one.
/// </summary>
public sealed record AuditActor(AuditActorKind Kind, Guid? Id)
{
    public static AuditActor Anonymous { get; } = new(AuditActorKind.Anonymous, null);

    public static AuditActor ForUser(Guid userId) => new(AuditActorKind.User, userId);

    public static AuditActor ForService(Guid serviceId) => new(AuditActorKind.Service, serviceId);
}

public enum AuditActorKind
{
    User,
    Service,
    Anonymous
}
