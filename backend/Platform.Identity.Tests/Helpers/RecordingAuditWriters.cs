using System.Data.Common;
using Platform.Audit.Contracts;

namespace Platform.Identity.Tests.Helpers;

public sealed record AuditWrite(AuditEventDraft Draft, AuditContext Context, DbTransaction? Transaction);

/// <summary>Stands in for both Audit writers behind IdentityAuditProducer and records every write.</summary>
public sealed class RecordingAuditWriters : ITrustedAuditWriter, ITransactionalAuditWriter
{
    public List<AuditWrite> Writes { get; } = [];
    public AuditWriteResult TrustedResult { get; set; } = AuditWriteResult.Recorded(Guid.NewGuid());
    public AuditWriteResult TransactionalResult { get; set; } = AuditWriteResult.Recorded(Guid.NewGuid());

    public ValueTask<AuditWriteResult> WriteAsync(
        AuditEventDraft draft, AuditContext context, CancellationToken cancellationToken = default)
    {
        Writes.Add(new AuditWrite(draft, context, null));
        return ValueTask.FromResult(TrustedResult);
    }

    public ValueTask<AuditWriteResult> WriteAsync(
        AuditEventDraft draft, AuditContext context, DbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        Writes.Add(new AuditWrite(draft, context, transaction));
        return ValueTask.FromResult(TransactionalResult);
    }
}
