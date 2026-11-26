using System.Data.Common;
using Platform.Audit.Contracts;

namespace Platform.Identity.Tests.Helpers;

/// <summary>A transactional Audit writer that never records, for the fail-closed cases (no record, no commit).</summary>
internal sealed class UnavailableTransactionalAuditWriter : ITransactionalAuditWriter
{
    public ValueTask<AuditWriteResult> WriteAsync(AuditEventDraft draft, AuditContext context,
        DbTransaction transaction, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(AuditWriteResult.Unavailable());
}
