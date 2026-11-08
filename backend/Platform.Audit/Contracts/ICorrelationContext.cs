namespace Platform.Audit.Contracts;

public interface ICorrelationContext
{
    string? CorrelationId { get; }
}
