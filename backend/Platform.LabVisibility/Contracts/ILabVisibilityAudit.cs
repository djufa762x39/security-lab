namespace Platform.LabVisibility.Contracts;

// Namerno nema polje za sadržaj: odgovor ne može da završi u auditu (AUD-03).
public sealed record LabVisibilityAuditEvent(
    string Action,
    Guid LabId,
    string? ActorId,
    string Outcome,
    string CorrelationId);

public interface ILabVisibilityAudit
{
    ValueTask WriteAsync(LabVisibilityAuditEvent auditEvent, CancellationToken cancellationToken = default);
}
