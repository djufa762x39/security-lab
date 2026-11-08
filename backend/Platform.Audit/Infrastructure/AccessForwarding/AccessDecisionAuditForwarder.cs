using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.Access.Contracts;
using Platform.Access.Domain.Models;
using Platform.Audit.Contracts;

namespace Platform.Audit.Infrastructure.AccessForwarding;

/// <summary>
/// S-13 Access forwarder (AUD-01/AUD-04). Decorates the existing Platform.Access audit chain:
/// the decision is first handed to that chain unchanged (its log and filtering stay), then a
/// Deny decision is stored as <c>access.denied</c> through <see cref="ITrustedAuditWriter"/>.
/// <para>
/// Lifetime: Access resolves <see cref="IAccessAuditWriter"/> as a singleton, while the Audit
/// writer is scoped (DbContext). The forwarder therefore opens its own scope per write instead
/// of capturing a scoped service. Actor and correlation id are taken from the
/// <see cref="AccessRequest"/> itself, not from an ambient request scope.
/// </para>
/// <para>
/// The forwarder never throws because of audit storage: a rejected, unavailable, failing or
/// canceled write raises an alarm without event content, and the access decision is returned
/// unchanged by Platform.Access. Status of the DI decision (D2) is in
/// docs/components/audit/S13-ACCESS-FORWARDER.md.
/// </para>
/// </summary>
internal sealed partial class AccessDecisionAuditForwarder : IAccessAuditWriter
{
    private const string NoCorrelation = "none";
    private const string InvalidCorrelation = "invalid";

    private readonly IAccessAuditWriter _inner;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AccessDecisionAuditForwarder> _logger;
    private readonly AccessForwardingMetrics _metrics;

    public AccessDecisionAuditForwarder(
        IAccessAuditWriter inner,
        IServiceScopeFactory scopeFactory,
        ILogger<AccessDecisionAuditForwarder> logger,
        AccessForwardingMetrics metrics)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    public async ValueTask WriteAsync(
        AccessRequest request,
        AccessDecision decision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(decision);

        await WriteToAccessChainAsync(request, decision, cancellationToken).ConfigureAwait(false);

        var mapping = AccessDecisionAuditMapper.Map(request, decision);
        switch (mapping.Status)
        {
            case AccessAuditMappingStatus.NotAudited:
                return;
            case AccessAuditMappingStatus.InvalidSubject:
                RaiseAlarm("unmapped", "invalid_subject", request.CorrelationId);
                return;
            case AccessAuditMappingStatus.InvalidCorrelationId:
                RaiseAlarm("unmapped", "invalid_correlation_id", request.CorrelationId);
                return;
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var writer = scope.ServiceProvider.GetRequiredService<ITrustedAuditWriter>();
            var result = await writer
                .WriteAsync(mapping.Draft!, mapping.Context!, cancellationToken)
                .ConfigureAwait(false);

            if (result.IsRecorded)
            {
                Count(static metrics => metrics.Recorded());
            }
            else
            {
                RaiseAlarm(
                    result.Status == AuditWriteStatus.Rejected ? "rejected" : "unavailable",
                    result.RejectionCode.ToString(),
                    request.CorrelationId);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RaiseAlarm("canceled", "none", request.CorrelationId);
        }
        catch (Exception exception)
        {
            RaiseAlarm("failed", exception.GetType().Name, request.CorrelationId);
        }
    }

    private async ValueTask WriteToAccessChainAsync(
        AccessRequest request,
        AccessDecision decision,
        CancellationToken cancellationToken)
    {
        try
        {
            await _inner.WriteAsync(request, decision, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // The Access chain is resilient by itself; a failure there must not stop the Audit write.
            // It is alarmed but not counted as a lost record: the Audit write still follows.
            RaiseAlarm("access_chain_failed", exception.GetType().Name, request.CorrelationId, lostRecord: false);
        }
    }

    // Only fixed reason words, enum names and exception type names are logged. Never the
    // draft, the details, the subject or a raw correlation value that fails the format.
    // Every alarm for a Deny that did not reach storage is also counted (D6: loss is measured).
    private void RaiseAlarm(string reason, string code, string? correlationId, bool lostRecord = true)
    {
        if (lostRecord)
        {
            Count(metrics => metrics.Failed(reason));
        }

        try
        {
            var safeCorrelationId =
                string.IsNullOrEmpty(correlationId) ? NoCorrelation
                : AccessDecisionAuditMapper.IsValidCorrelationId(correlationId) ? correlationId
                : InvalidCorrelation;

            LogForwardingFailure(_logger, reason, code, safeCorrelationId);
        }
        catch (Exception)
        {
            // An alarm must never turn into an exception for the access decision.
        }
    }

    private void Count(Action<AccessForwardingMetrics> record)
    {
        try
        {
            record(_metrics);
        }
        catch (Exception)
        {
            // A metrics listener must never turn into an exception for the access decision.
        }
    }

    [LoggerMessage(
        EventId = 6201,
        EventName = "audit.access_forwarding.failure",
        Level = LogLevel.Error,
        Message = "audit.access_forwarding.failure reason={Reason} code={Code} correlationId={CorrelationId}")]
    private static partial void LogForwardingFailure(
        ILogger logger, string reason, string code, string correlationId);
}
