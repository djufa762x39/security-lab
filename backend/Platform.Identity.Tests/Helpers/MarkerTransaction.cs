using System.Data.Common;

namespace Platform.Identity.Tests.Helpers;

/// <summary>A transaction without a connection, to check which transaction a write was given.</summary>
public sealed class MarkerTransaction : DbTransaction
{
    public override System.Data.IsolationLevel IsolationLevel => System.Data.IsolationLevel.ReadCommitted;
    protected override DbConnection? DbConnection => null;
    public override void Commit() { }
    public override void Rollback() { }
}
