using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Audit.Contracts;

namespace Platform.Access.Infrastructure.Auditing;

internal sealed class CentralAccessAuditWriter(
    IAccessDeniedAuditMapper mapper,
    IServiceScopeFactory scopes,
    ILogger<CentralAccessAuditWriter> logger) : IAccessAuditWriter
{
    public async ValueTask WriteAsync(
        AccessRequest request,
        AccessDecision decision,
        CancellationToken cancellationToken = default)
    {
        if (decision.Outcome == AccessOutcome.Allow)
        {
            return;
        }

        var draft = mapper.CreateDraft(request, decision);
        var context = mapper.CreateContext(request);

        await using var scope = scopes.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<ITrustedAuditWriter>();
        var result = await writer.WriteAsync(draft, context, cancellationToken);

        if (!result.IsRecorded)
        {
            logger.LogWarning(
                "Access denial was not recorded: {Status} {RejectionCode}. CorrelationId: {CorrelationId}",
                result.Status, result.RejectionCode, decision.CorrelationId);
        }
    }
}
