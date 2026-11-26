using System.Collections.Concurrent;
using Platform.SessionSecurity.Contracts;

// IDN-11 TIM 09
namespace Platform.Identity.Tests.SessionSecurity.Helpers;

/// <summary>
/// IDN-11: audit writer for the end-to-end rotation tests over PostgreSQL. Keeps every entry and answers
/// with a fixed result, so a test can prove both an audited rotation and the fail-closed path (AUD-10).
/// The unit test double <c>RecordingAuditWriter</c> lives in Platform.Tests, which this project does not
/// reference, so it cannot be reused here.
/// </summary>
public sealed class RecordingSessionSecurityAuditWriter(bool writeSucceeds = true) : ISessionSecurityAuditWriter
{
    private readonly ConcurrentQueue<SessionSecurityAuditEntry> entries = new();

    public IReadOnlyList<SessionSecurityAuditEntry> Entries => entries.ToArray();

    public ValueTask<bool> TryWriteAsync(
        SessionSecurityAuditEntry entry,
        CancellationToken cancellationToken = default)
    {
        entries.Enqueue(entry);
        return ValueTask.FromResult(writeSucceeds);
    }
}
