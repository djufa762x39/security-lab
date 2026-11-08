using System.Diagnostics.Metrics;

namespace Platform.Audit.Infrastructure.AccessForwarding;

/// <summary>
/// S-13 counters (AUD-10, decision D6): a lost <c>access.denied</c> record is measured, not only
/// logged. <c>audit.access_forwarding.recorded</c> counts stored Deny records and
/// <c>audit.access_forwarding.failures</c> counts every Deny that did not reach storage, tagged
/// with the same fixed <c>reason</c> word as the alarm. No correlation id, actor or detail is used
/// as a tag, so cardinality stays fixed and nothing sensitive leaves through metrics.
/// </summary>
internal sealed class AccessForwardingMetrics
{
    internal const string MeterName = "Platform.Audit.AccessForwarding";
    internal const string RecordedInstrument = "audit.access_forwarding.recorded";
    internal const string FailuresInstrument = "audit.access_forwarding.failures";
    internal const string ReasonTag = "reason";

    private readonly Counter<long> _recorded;
    private readonly Counter<long> _failures;

    public AccessForwardingMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(MeterName);
        _recorded = meter.CreateCounter<long>(RecordedInstrument, unit: "{record}",
            description: "access.denied records stored by the S-13 forwarder.");
        _failures = meter.CreateCounter<long>(FailuresInstrument, unit: "{record}",
            description: "Access decisions that the S-13 forwarder could not store, by reason.");
    }

    public void Recorded() => _recorded.Add(1);

    public void Failed(string reason) => _failures.Add(1, new KeyValuePair<string, object?>(ReasonTag, reason));
}
