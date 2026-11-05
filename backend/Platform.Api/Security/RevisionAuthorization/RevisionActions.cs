namespace Platform.Api.Security.RevisionAuthorization;

public static class RevisionActions
{
    public const string Create = "revision.create";
    public const string Read = "revision.read";
    public const string Mine = "revision.mine";
    public const string PendingReviews = "revision.pending.reviews";

    public const string Submit = "revision.submit";
    public const string Approve = "revision.approve";
    public const string Return = "revision.return";

    public const string UnlockAccount = "account.unlock";
}