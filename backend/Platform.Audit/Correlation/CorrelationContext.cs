using Platform.Audit.Contracts;

namespace Platform.Audit.Correlation;

internal sealed class CorrelationContext : ICorrelationContext
{
    public string? CorrelationId { get; private set; }

    public void Assign(string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        if (CorrelationId is not null)
            throw new InvalidOperationException("The correlation ID is already assigned for this scope.");

        CorrelationId = correlationId;
    }

    public static string NewId() => Guid.NewGuid().ToString("N");
}
