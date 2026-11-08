using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NUnit.Framework;
using Platform.Access.Contracts;
using Platform.Audit.Contracts;
using Platform.Audit.Domain;
using Platform.Audit.Infrastructure.AccessForwarding;
using Platform.Audit.Infrastructure.Persistence;

namespace Platform.Audit.Tests.AccessForwarding;

/// <summary>
/// S-13 through the real Platform.Api host and the real Access middleware (T09/T10 at HTTP level).
/// The PostgreSQL fixture needs <c>AUDIT_TEST_RUNTIME_CONNECTION_STRING</c>
/// (set by scripts/audit/test-postgres.ps1, as in CI); the failure fixture needs no database.
/// </summary>
[TestFixture]
[Category("AuditRuntime")]
public sealed class AccessForwardingPostgresTests
{
    private string connectionString = null!;
    private AccessForwardingHostFactory? factory;
    private HttpClient client = null!;

    [OneTimeSetUp]
    public void StartHost()
    {
        var configured = Environment.GetEnvironmentVariable("AUDIT_TEST_RUNTIME_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(configured))
        {
            Assert.Ignore("Set AUDIT_TEST_RUNTIME_CONNECTION_STRING to run the S-13 forwarder against PostgreSQL.");
            return;
        }

        connectionString = new NpgsqlConnectionStringBuilder(configured) { Pooling = false }.ConnectionString;
        factory = new AccessForwardingHostFactory(connectionString);
        client = factory.CreateClient();
    }

    [OneTimeTearDown]
    public void StopHost()
    {
        client?.Dispose();
        factory?.Dispose();
    }

    [Test]
    public async Task AnonymousDeny_Returns401_AndStoresExactlyOneAccessDeniedRowWithDecisionCorrelationId()
    {
        var courseId = Guid.NewGuid();

        using var response = await client.GetAsync(AccessForwardingHostFactory.ProbePath(courseId));
        var correlationId = await ReadCorrelationIdAsync(response);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        var stored = await WaitForRowsAsync(correlationId, expected: 1);

        Assert.That(stored, Has.Count.EqualTo(1), "one Deny decision gives exactly one access.denied record");
        var record = stored[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(record.EventType, Is.EqualTo(AuditEventTypes.AccessDenied));
            Assert.That(record.Outcome, Is.EqualTo(AuditOutcome.Denied));
            Assert.That(record.ActorKind, Is.EqualTo(AuditActorKind.Anonymous));
            Assert.That(record.ActorId, Is.Null);
            Assert.That(record.CourseId, Is.EqualTo(courseId), "course comes from the Access resource, not the client");
            Assert.That(record.ResourceType, Is.EqualTo(AuditResourceTypes.Course));
            Assert.That(record.ResourceId, Is.EqualTo(courseId.ToString("D")));
            Assert.That(record.CorrelationId, Is.EqualTo(correlationId));
            Assert.That(record.Details["action"], Is.EqualTo("audit.events.read"));
            Assert.That(record.Details["denialReason"], Is.EqualTo("unauthenticated"));
            Assert.That(record.Details.Keys, Is.SubsetOf(new[] { "action", "denialReason", "policyId", "policyVersion" }));
        }
    }

    [Test]
    public async Task TwoDenies_StoreOneRowEach_WithoutMixingCourseOrCorrelation()
    {
        var courseK = Guid.NewGuid();
        var courseL = Guid.NewGuid();

        var responses = await Task.WhenAll(
            client.GetAsync(AccessForwardingHostFactory.ProbePath(courseK)),
            client.GetAsync(AccessForwardingHostFactory.ProbePath(courseL)));
        var correlationK = await ReadCorrelationIdAsync(responses[0]);
        var correlationL = await ReadCorrelationIdAsync(responses[1]);
        foreach (var response in responses)
            response.Dispose();

        var rowsK = await WaitForRowsAsync(correlationK, expected: 1);
        var rowsL = await WaitForRowsAsync(correlationL, expected: 1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(correlationK, Is.Not.EqualTo(correlationL));
            Assert.That(rowsK.Select(row => row.CourseId), Is.EqualTo(new Guid?[] { courseK }));
            Assert.That(rowsL.Select(row => row.CourseId), Is.EqualTo(new Guid?[] { courseL }));
        }
    }

    private static async Task<string> ReadCorrelationIdAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("correlationId").GetString()!;
    }

    // Access waits for its audit writer only up to AccessDecisionOptions.AuditTimeout (1 s); the
    // write itself continues after the response. Poll briefly instead of assuming it finished.
    private async Task<IReadOnlyList<AuditEvent>> WaitForRowsAsync(string correlationId, int expected)
    {
        IReadOnlyList<AuditEvent> rows = [];
        for (var attempt = 0; attempt < 50; attempt++)
        {
            await using var context = new AuditDbContext(
                new DbContextOptionsBuilder<AuditDbContext>().UseNpgsql(connectionString).Options);
            rows = (await context.Events.AsNoTracking()
                    .Where(row => row.CorrelationId == correlationId)
                    .ToListAsync())
                .Select(row => row.ToDomain())
                .ToList();

            if (rows.Count >= expected)
                break;

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        // A short extra wait proves no second row follows (no recursion, no duplicate).
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        await using (var context = new AuditDbContext(
                         new DbContextOptionsBuilder<AuditDbContext>().UseNpgsql(connectionString).Options))
        {
            var count = await context.Events.CountAsync(row => row.CorrelationId == correlationId);
            Assert.That(count, Is.EqualTo(rows.Count), "no late duplicate for the same decision");
        }

        return rows;
    }
}

/// <summary>
/// Audit database is down: the HTTP decision stays 401 and the forwarder raises its alarm.
/// Points the Audit DbContext at a closed local port, so no database is needed.
/// </summary>
[TestFixture]
public sealed class AccessForwardingHostFailureTests
{
    private const string UnreachableAudit =
        "Host=127.0.0.1;Port=1;Database=audit_unreachable;Username=audit_unreachable;Timeout=1;Pooling=false";

    private AccessForwardingHostFactory factory = null!;
    private HttpClient client = null!;

    [OneTimeSetUp]
    public void StartHost()
    {
        factory = new AccessForwardingHostFactory(UnreachableAudit);
        client = factory.CreateClient();
    }

    [OneTimeTearDown]
    public void StopHost()
    {
        client.Dispose();
        factory.Dispose();
    }

    [Test]
    public void Host_StartsWithScopeValidation_AndForwarderIsTheActiveAccessAuditWriter()
    {
        // Program.cs sets ValidateScopes and ValidateOnBuild; the host was built and started
        // (including AccessAuditForwardingStartupCheck) when the client was created.
        Assert.That(factory.Services.GetRequiredService<IAccessAuditWriter>(), Is.InstanceOf<AccessDecisionAuditForwarder>());
    }

    [Test]
    public async Task Deny_WhenAuditDatabaseIsDown_StillReturns401_AndRaisesAlarmWithoutSecrets()
    {
        using var response = await client.GetAsync(AccessForwardingHostFactory.ProbePath(Guid.NewGuid()));
        var body = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "audit failure never changes the decision");

        string correlationId;
        using (var json = JsonDocument.Parse(body))
            correlationId = json.RootElement.GetProperty("correlationId").GetString()!;

        var alarm = await WaitForAlarmAsync(correlationId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(alarm, Is.Not.Null, "a lost access.denied record is alarmed");
            Assert.That(alarm!, Does.Match("reason=(unavailable|failed) "));
            Assert.That(alarm, Does.Not.Contain("audit_unreachable"), "no connection details in the alarm");
            Assert.That(alarm, Does.Not.Contain("Host="));
            Assert.That(body, Does.Not.Contain("audit"), "the client sees only the access problem details");
        }
    }

    private async Task<string?> WaitForAlarmAsync(string correlationId)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var alarm = factory.Logs.Entries
                .Where(entry => entry.EventId.Id == 6201 && entry.Text.EndsWith($"correlationId={correlationId}", StringComparison.Ordinal))
                .Select(entry => entry.Text)
                .FirstOrDefault();
            if (alarm is not null)
                return alarm;

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        return null;
    }
}
