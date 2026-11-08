// TEST ADAPTERS ONLY (S-13). These fakes replace the Audit storage and the Access chain in unit
// tests. They are not registered by any production composition and never run outside tests.
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Audit.Contracts;
using Platform.Audit.Infrastructure.AccessForwarding;

namespace Platform.Audit.Tests.AccessForwarding;

/// <summary>Writer whose failure message carries a marker, to prove the alarm never copies it.</summary>
internal sealed class ThrowingTrustedAuditWriter(string message) : ITrustedAuditWriter
{
    public ValueTask<AuditWriteResult> WriteAsync(
        AuditEventDraft draft,
        AuditContext context,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(message);
}

/// <summary>Captures formatted log lines (and any exception text) instead of discarding them.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public ConcurrentQueue<(LogLevel Level, EventId EventId, string Text)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Enqueue((logLevel, eventId, formatter(state, exception) + (exception is null ? "" : " " + exception)));
}

/// <summary>Logger provider for test hosts: keeps every line written by any category.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<(string Category, EventId EventId, string Text)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CategoryLogger(categoryName, Entries);

    public void Dispose()
    {
    }

    private sealed class CategoryLogger(
        string category,
        ConcurrentQueue<(string Category, EventId EventId, string Text)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            entries.Enqueue((category, eventId, formatter(state, exception) + (exception is null ? "" : " " + exception)));
    }
}

/// <summary>
/// Own <see cref="IMeterFactory"/> per test and a listener that only sees meters from that factory,
/// so parallel or earlier tests can never change the counted values.
/// </summary>
internal sealed class ForwardingMetricsRecorder : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly MeterListener _listener = new();

    public ForwardingMetricsRecorder()
    {
        _provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = _provider.GetRequiredService<IMeterFactory>();

        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter.Scope, factory) &&
                instrument.Meter.Name == AccessForwardingMetrics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            string? reason = null;
            foreach (var tag in tags)
            {
                if (tag.Key == AccessForwardingMetrics.ReasonTag)
                    reason = tag.Value as string;
            }

            Measurements.Enqueue((instrument.Name, value, reason));
        });
        _listener.Start();

        Metrics = new AccessForwardingMetrics(factory);
    }

    public AccessForwardingMetrics Metrics { get; }

    public ConcurrentQueue<(string Instrument, long Value, string? Reason)> Measurements { get; } = new();

    public long Sum(string instrument, string? reason = null) =>
        Measurements
            .Where(measurement => measurement.Instrument == instrument && (reason is null || measurement.Reason == reason))
            .Sum(measurement => measurement.Value);

    public void Dispose()
    {
        _listener.Dispose();
        _provider.Dispose();
    }
}

public enum FakeWriterMode
{
    Record,
    Unavailable,
    Reject,
    Throw
}

/// <summary>Fake <see cref="ITrustedAuditWriter"/>: remembers every draft with its context.</summary>
internal sealed class FakeTrustedAuditWriter(FakeWriterMode mode = FakeWriterMode.Record) : ITrustedAuditWriter
{
    public ConcurrentQueue<(AuditEventDraft Draft, AuditContext Context)> Writes { get; } = new();

    public int CallCount => _callCount;

    private int _callCount;

    public ValueTask<AuditWriteResult> WriteAsync(
        AuditEventDraft draft,
        AuditContext context,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);

        return mode switch
        {
            FakeWriterMode.Record => Record(draft, context),
            FakeWriterMode.Unavailable => ValueTask.FromResult(AuditWriteResult.Unavailable()),
            FakeWriterMode.Reject => ValueTask.FromResult(AuditWriteResult.Rejected(AuditRejectionCode.InvalidDetailValue)),
            _ => throw new InvalidOperationException("Simulated audit storage failure.")
        };
    }

    private ValueTask<AuditWriteResult> Record(AuditEventDraft draft, AuditContext context)
    {
        Writes.Enqueue((draft, context));
        return ValueTask.FromResult(AuditWriteResult.Recorded(Guid.NewGuid()));
    }
}

/// <summary>Stands in for the existing Platform.Access audit chain (log sink).</summary>
internal sealed class RecordingAccessAuditWriter(bool throwOnWrite = false) : IAccessAuditWriter
{
    public int CallCount => _callCount;

    private int _callCount;

    public ValueTask WriteAsync(AccessRequest request, AccessDecision decision, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        return throwOnWrite
            ? ValueTask.FromException(new InvalidOperationException("Simulated Access chain failure."))
            : ValueTask.CompletedTask;
    }
}

/// <summary>Counts access decisions, to prove that an audit write never asks Access again (T10).</summary>
internal sealed class CountingAccessDecision : IAccessDecision
{
    public int CallCount => _callCount;

    private int _callCount;

    public ValueTask<AccessDecision> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        return ValueTask.FromResult(AccessDecision.Deny(AccessDenialReason.PolicyDenied, request.CorrelationId));
    }
}

/// <summary>Synthetic identities and requests. No real accounts or secrets.</summary>
internal static class AccessForwardingExamples
{
    public static readonly Guid UserA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid UserB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid CourseK = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public const string CorrelationA = "0f8fad5bd9cb469fa16570867728950e";
    public const string CorrelationB = "7c9e6679742540de944be07fc1f90ae7";

    public static AccessRequest Request(
        Guid? userId = null,
        string action = "audit.events.read",
        ResourceRef? resource = null,
        string correlationId = CorrelationA,
        string? subjectId = null) =>
        new(
            subjectId is not null ? new SubjectRef(subjectId, AccountState.Active)
            : userId is Guid id ? new SubjectRef(id.ToString("D"), AccountState.Active)
            : null,
            action,
            resource,
            correlationId);

    public static ResourceRef Course(Guid courseId) => new("course", courseId.ToString("D"));

    public static AccessDecision Deny(
        AccessDenialReason reason = AccessDenialReason.PolicyDenied,
        string correlationId = CorrelationA,
        string? policyId = "course-role",
        string policyVersion = "v1") =>
        new(AccessOutcome.Deny, reason, policyId, policyVersion, correlationId);

    public static AccessDecision Allow(string correlationId = CorrelationA) =>
        AccessDecision.Allow("course-role", correlationId, "v1");
}
