namespace Platform.Audit.Contracts;

/// <summary>
/// Records a security-relevant event (AUD-01). The caller describes what happened;
/// the actor, the time and the correlation ID come from the trusted server context.
/// See docs/components/audit/CONTRACTS.md.
/// </summary>
public interface IAuditWriter
{
    ValueTask<AuditWriteResult> WriteAsync(
        AuditEventDraft draft,
        CancellationToken cancellationToken = default);
}
