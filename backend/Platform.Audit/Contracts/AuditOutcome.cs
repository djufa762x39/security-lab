namespace Platform.Audit.Contracts;

public enum AuditOutcome
{
    /// <summary>The action was carried out and persisted.</summary>
    Success,

    /// <summary>The action was refused by an authorization or policy check.</summary>
    Denied,

    /// <summary>The action was allowed but failed.</summary>
    Failure
}
