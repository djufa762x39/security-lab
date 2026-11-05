namespace Platform.Audit.Contracts;

/// <summary>
/// Records an event with an already confirmed context. For server-side adapters only
/// (successful login, the Access adapter, background jobs). The context must never be
/// built from an HTTP body, query string or header.
/// </summary>
public interface ITrustedAuditWriter
{
    ValueTask<AuditWriteResult> WriteAsync(
        AuditEventDraft draft,
        AuditContext context,
        CancellationToken cancellationToken = default);
}
