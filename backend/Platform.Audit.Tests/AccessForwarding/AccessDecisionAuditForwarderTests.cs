using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Platform.Access.Contracts;
using Platform.Audit.Contracts;
using Platform.Audit.Infrastructure.AccessForwarding;
using static Platform.Audit.Tests.AccessForwarding.AccessForwardingExamples;

namespace Platform.Audit.Tests.AccessForwarding;

/// <summary>
/// S-13 forwarder against fake dependencies (T10, T09 at unit level). The real PostgreSQL
/// path and the HTTP correlation id are covered later with the integration tests.
/// </summary>
[TestFixture]
public sealed class AccessDecisionAuditForwarderTests
{
    [Test]
    public async Task WriteAsync_WhenDeny_WritesExactlyOneAccessDeniedEventWithSameCorrelationId()
    {
        var audit = new FakeTrustedAuditWriter();
        var inner = new RecordingAccessAuditWriter();
        var forwarder = CreateForwarder(audit, inner);

        await forwarder.WriteAsync(Request(UserA), Deny());

        var write = audit.Writes.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(write.Draft.EventType, Is.EqualTo(AuditEventTypes.AccessDenied));
            Assert.That(write.Context.CorrelationId, Is.EqualTo(CorrelationA));
            Assert.That(write.Context.Actor, Is.EqualTo(AuditActor.ForUser(UserA)));
            Assert.That(inner.CallCount, Is.EqualTo(1), "the existing Access chain still receives the decision");
        }
    }

    [Test]
    public async Task WriteAsync_WhenAllow_DoesNotWriteToAudit()
    {
        var audit = new FakeTrustedAuditWriter();
        var inner = new RecordingAccessAuditWriter();
        var forwarder = CreateForwarder(audit, inner);

        await forwarder.WriteAsync(Request(UserA), Allow());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(audit.CallCount, Is.Zero);
            Assert.That(inner.CallCount, Is.EqualTo(1));
        }
    }

    [TestCase(FakeWriterMode.Unavailable)]
    [TestCase(FakeWriterMode.Reject)]
    [TestCase(FakeWriterMode.Throw)]
    public void WriteAsync_WhenAuditWriteFails_DoesNotThrow(FakeWriterMode mode)
    {
        var audit = new FakeTrustedAuditWriter(mode);
        var forwarder = CreateForwarder(audit, new RecordingAccessAuditWriter());

        Assert.DoesNotThrowAsync(async () => await forwarder.WriteAsync(Request(UserA), Deny()));
        Assert.That(audit.CallCount, Is.EqualTo(1));
    }

    [Test]
    public void WriteAsync_WhenCanceled_DoesNotThrow()
    {
        var forwarder = CreateForwarder(new CancelingTrustedAuditWriter(), new RecordingAccessAuditWriter());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.DoesNotThrowAsync(async () => await forwarder.WriteAsync(Request(UserA), Deny(), cts.Token));
    }

    [Test]
    public async Task WriteAsync_WhenAccessChainThrows_StillWritesToAudit()
    {
        var audit = new FakeTrustedAuditWriter();
        var forwarder = CreateForwarder(audit, new RecordingAccessAuditWriter(throwOnWrite: true));

        await forwarder.WriteAsync(Request(UserA), Deny());

        Assert.That(audit.Writes, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task WriteAsync_WhenSubjectIsNotUserGuid_SkipsAuditWithoutThrowing()
    {
        var audit = new FakeTrustedAuditWriter();
        var forwarder = CreateForwarder(audit, new RecordingAccessAuditWriter());

        await forwarder.WriteAsync(Request(subjectId: "demo-subject"), Deny());

        Assert.That(audit.CallCount, Is.Zero);
    }

    [Test]
    public async Task WriteAsync_OpensNewScopePerWrite()
    {
        var created = 0;
        var services = new ServiceCollection();
        services.AddScoped<ITrustedAuditWriter>(_ =>
        {
            Interlocked.Increment(ref created);
            return new FakeTrustedAuditWriter();
        });
        await using var provider = services.BuildServiceProvider(ValidatingOptions);
        var forwarder = new AccessDecisionAuditForwarder(
            new RecordingAccessAuditWriter(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AccessDecisionAuditForwarder>.Instance,
            UnusedMetrics.Metrics);

        await forwarder.WriteAsync(Request(UserA), Deny());
        await forwarder.WriteAsync(Request(UserB, correlationId: CorrelationB), Deny(correlationId: CorrelationB));

        Assert.That(created, Is.EqualTo(2));
    }

    [Test]
    public async Task WriteAsync_ParallelIdentities_KeepActorAndCorrelationTogether()
    {
        var audit = new FakeTrustedAuditWriter();
        var forwarder = CreateForwarder(audit, new RecordingAccessAuditWriter());

        var writes = Enumerable.Range(0, 50).Select(i => i % 2 == 0
            ? forwarder.WriteAsync(Request(UserA, correlationId: CorrelationA), Deny(correlationId: CorrelationA)).AsTask()
            : forwarder.WriteAsync(Request(UserB, correlationId: CorrelationB), Deny(correlationId: CorrelationB)).AsTask());
        await Task.WhenAll(writes);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(audit.Writes, Has.Count.EqualTo(50));
            Assert.That(
                audit.Writes.All(write =>
                    (write.Context.Actor.Id == UserA && write.Context.CorrelationId == CorrelationA) ||
                    (write.Context.Actor.Id == UserB && write.Context.CorrelationId == CorrelationB)),
                Is.True);
        }
    }

    [Test]
    public async Task WriteAsync_DoesNotAskAccessDecisionAgain()
    {
        var audit = new FakeTrustedAuditWriter();
        var decision = new CountingAccessDecision();
        var services = new ServiceCollection();
        services.AddSingleton<ITrustedAuditWriter>(audit);
        services.AddSingleton<IAccessDecision>(decision);
        await using var provider = services.BuildServiceProvider(ValidatingOptions);
        var forwarder = new AccessDecisionAuditForwarder(
            new RecordingAccessAuditWriter(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AccessDecisionAuditForwarder>.Instance,
            UnusedMetrics.Metrics);

        await forwarder.WriteAsync(Request(UserA), Deny());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(audit.Writes, Has.Count.EqualTo(1));
            Assert.That(decision.CallCount, Is.Zero);
        }
    }

    // ---- D6 / AUD-10: a lost record is measured, not only logged ----

    [Test]
    public async Task WriteAsync_WhenRecorded_CountsRecordedAndNoLoss()
    {
        using var metrics = new ForwardingMetricsRecorder();
        var forwarder = CreateForwarder(new FakeTrustedAuditWriter(), new RecordingAccessAuditWriter(), metrics: metrics.Metrics);

        await forwarder.WriteAsync(Request(UserA), Deny());
        await forwarder.WriteAsync(Request(UserA), Allow());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(metrics.Sum(AccessForwardingMetrics.RecordedInstrument), Is.EqualTo(1), "only the Deny is stored");
            Assert.That(metrics.Sum(AccessForwardingMetrics.FailuresInstrument), Is.Zero);
        }
    }

    [TestCase(FakeWriterMode.Unavailable, "unavailable")]
    [TestCase(FakeWriterMode.Reject, "rejected")]
    [TestCase(FakeWriterMode.Throw, "failed")]
    public async Task WriteAsync_WhenAuditWriteFails_CountsOneLossWithReason(FakeWriterMode mode, string reason)
    {
        using var metrics = new ForwardingMetricsRecorder();
        var forwarder = CreateForwarder(new FakeTrustedAuditWriter(mode), new RecordingAccessAuditWriter(), metrics: metrics.Metrics);

        await forwarder.WriteAsync(Request(UserA), Deny());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(metrics.Sum(AccessForwardingMetrics.FailuresInstrument, reason), Is.EqualTo(1));
            Assert.That(metrics.Sum(AccessForwardingMetrics.FailuresInstrument), Is.EqualTo(1));
            Assert.That(metrics.Sum(AccessForwardingMetrics.RecordedInstrument), Is.Zero);
        }
    }

    [Test]
    public async Task WriteAsync_WhenDenyCannotBeMapped_CountsLoss()
    {
        using var metrics = new ForwardingMetricsRecorder();
        var forwarder = CreateForwarder(new FakeTrustedAuditWriter(), new RecordingAccessAuditWriter(), metrics: metrics.Metrics);

        await forwarder.WriteAsync(Request(subjectId: "demo-subject"), Deny());

        Assert.That(metrics.Sum(AccessForwardingMetrics.FailuresInstrument, "unmapped"), Is.EqualTo(1));
    }

    [Test]
    public async Task WriteAsync_WhenOnlyAccessChainFails_IsNotCountedAsLostRecord()
    {
        using var metrics = new ForwardingMetricsRecorder();
        var forwarder = CreateForwarder(
            new FakeTrustedAuditWriter(),
            new RecordingAccessAuditWriter(throwOnWrite: true),
            metrics: metrics.Metrics);

        await forwarder.WriteAsync(Request(UserA), Deny());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(metrics.Sum(AccessForwardingMetrics.FailuresInstrument), Is.Zero);
            Assert.That(metrics.Sum(AccessForwardingMetrics.RecordedInstrument), Is.EqualTo(1));
        }
    }

    // ---- AUD-03 / AUD-16 / AUD-17: the alarm carries no secret, token or event content ----

    [Test]
    public async Task Alarm_WhenWriterThrowsWithSensitiveMessage_LogsOnlyReasonTypeAndCorrelation()
    {
        const string marker = "synthetic-secret-marker";
        var logger = new CapturingLogger<AccessDecisionAuditForwarder>();
        var forwarder = CreateForwarder(
            new ThrowingTrustedAuditWriter($"Host=db;Password={marker};Authorization: Bearer {marker}"),
            new RecordingAccessAuditWriter(),
            logger);

        await forwarder.WriteAsync(Request(UserA, resource: Course(CourseK)), Deny());

        var alarm = logger.Entries.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(alarm.Level, Is.EqualTo(LogLevel.Error));
            Assert.That(alarm.EventId.Id, Is.EqualTo(6201));
            Assert.That(alarm.Text, Is.EqualTo(
                $"audit.access_forwarding.failure reason=failed code=InvalidOperationException correlationId={CorrelationA}"));
            AssertNoEventContent(alarm.Text, marker);
        }
    }

    [Test]
    public async Task Alarm_WhenWriterRejects_DoesNotCopyDraftDetailsOrIdentities()
    {
        var logger = new CapturingLogger<AccessDecisionAuditForwarder>();
        var forwarder = CreateForwarder(new FakeTrustedAuditWriter(FakeWriterMode.Reject), new RecordingAccessAuditWriter(), logger);

        await forwarder.WriteAsync(Request(UserA, resource: Course(CourseK)), Deny());

        var alarm = logger.Entries.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(alarm.Text, Does.Contain("reason=rejected code=InvalidDetailValue"));
            AssertNoEventContent(alarm.Text);
        }
    }

    [Test]
    public async Task Alarm_WhenCorrelationIdIsHostile_LogsPlaceholderInsteadOfValue()
    {
        const string hostile = "Bearer synthetic-token\r\nforged-log-line";
        var logger = new CapturingLogger<AccessDecisionAuditForwarder>();
        var forwarder = CreateForwarder(new FakeTrustedAuditWriter(), new RecordingAccessAuditWriter(), logger);

        await forwarder.WriteAsync(Request(UserA, correlationId: hostile), Deny(correlationId: hostile));

        var alarm = logger.Entries.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(alarm.Text, Does.EndWith("reason=unmapped code=invalid_correlation_id correlationId=invalid"));
            Assert.That(alarm.Text, Does.Not.Contain("synthetic-token"));
            Assert.That(alarm.Text, Does.Not.Contain("\n"));
        }
    }

    private static void AssertNoEventContent(string text, string? marker = null)
    {
        if (marker is not null)
            Assert.That(text, Does.Not.Contain(marker));

        Assert.That(text, Does.Not.Contain(UserA.ToString()), "no actor id");
        Assert.That(text, Does.Not.Contain(CourseK.ToString()), "no course id");
        Assert.That(text, Does.Not.Contain("course-role"), "no policy detail");
        Assert.That(text, Does.Not.Contain("audit.events.read"), "no action detail");
        Assert.That(text, Does.Not.Contain("Bearer"));
        Assert.That(text, Does.Not.Contain("Password"));
    }

    // Shared for tests that do not inspect metrics; its own factory keeps it isolated from recorders.
    private static readonly ForwardingMetricsRecorder UnusedMetrics = new();

    private static readonly ServiceProviderOptions ValidatingOptions = new()
    {
        ValidateScopes = true,
        ValidateOnBuild = true
    };

    private static AccessDecisionAuditForwarder CreateForwarder(
        ITrustedAuditWriter audit,
        IAccessAuditWriter inner,
        ILogger<AccessDecisionAuditForwarder>? logger = null,
        AccessForwardingMetrics? metrics = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(audit);
        var provider = services.BuildServiceProvider(ValidatingOptions);

        return new AccessDecisionAuditForwarder(
            inner,
            provider.GetRequiredService<IServiceScopeFactory>(),
            logger ?? NullLogger<AccessDecisionAuditForwarder>.Instance,
            metrics ?? UnusedMetrics.Metrics);
    }

    private sealed class CancelingTrustedAuditWriter : ITrustedAuditWriter
    {
        public ValueTask<AuditWriteResult> WriteAsync(
            AuditEventDraft draft,
            AuditContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(AuditWriteResult.Recorded(Guid.NewGuid()));
        }
    }
}
