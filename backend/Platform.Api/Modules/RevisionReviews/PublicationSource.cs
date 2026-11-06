using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Platform.Laboratories.Infrastructure.Persistence;

namespace Platform.Api.Modules.RevisionReviews;

// The provider must return the exact, complete bytes validated by REV-02 for this revision.
// Only the server-side adapter may supply content, evaluator validation and laboratory identity.
public interface IPublicationSource
{
    Task<PublicationCandidate?> LoadAndValidateAsync(Guid revisionId, CancellationToken cancellationToken);
}

public sealed record PublicationCandidate(Guid LaboratoryId, byte[] Snapshot, IReadOnlyList<PublicationError> Errors);
public sealed record PublicationError(string Field, string Message);

public sealed class StoredPublicationSource(IDbContextFactory<LaboratoriesDbContext> contexts) : IPublicationSource
{
    public async Task<PublicationCandidate?> LoadAndValidateAsync(Guid revisionId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var draft = await db.ReviewDefinitions.AsNoTracking()
            .SingleOrDefaultAsync(value => value.RevisionId == revisionId, cancellationToken);
        if (draft is null)
            return null;

        IReadOnlyList<PublicationError> errors;
        try
        {
            errors = PublicationDefinition.Read(draft.Content)?.Validate() ?? InvalidDefinition();
        }
        catch (JsonException)
        {
            errors = InvalidDefinition();
        }
        return new PublicationCandidate(draft.LaboratoryId, draft.Content, errors);
    }

    private static PublicationError[] InvalidDefinition() =>
        [new("definition", "The stored definition is invalid.")];
}
