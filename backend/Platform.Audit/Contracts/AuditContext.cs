namespace Platform.Audit.Contracts;

/// <param name="Actor">Who performed the action, as confirmed by the server.</param>
/// <param name="CorrelationId">1 to 64 characters: ASCII letters, digits, '-', '_' and '.'.</param>
public sealed record AuditContext(AuditActor Actor, string CorrelationId);
