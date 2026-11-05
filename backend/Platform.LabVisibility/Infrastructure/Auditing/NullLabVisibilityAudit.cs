using Platform.LabVisibility.Contracts;

namespace Platform.LabVisibility.Infrastructure.Auditing;

// Privremeno. Zamenjuje ga LoggerLabVisibilityAudit (kartica 5).
public sealed class NullLabVisibilityAudit : ILabVisibilityAudit
{
    public ValueTask WriteAsync(LabVisibilityAuditEvent auditEvent, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}
