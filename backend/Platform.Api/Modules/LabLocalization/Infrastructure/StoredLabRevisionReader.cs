using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Modules.RevisionReviews;
using Platform.LabLocalization.Application;
using Platform.Laboratories.Domain.Enums;
using Platform.Laboratories.Infrastructure.Persistence;

namespace Platform.Api.Modules.LabLocalization.Infrastructure;

// Cita revizije iz REV modula: objavljena revizija ima broj i nepromenljiv snapshot definicije sa evaluatorom.
public sealed class StoredLabRevisionReader(IDbContextFactory<LaboratoriesDbContext> contexts) : ILabRevisionReader
{
    public async Task<RevisionInfo?> FindCurrentForLabAsync(Guid labId, CancellationToken ct = default)
    {
        if (labId == Guid.Empty)
        {
            return null;
        }

        await using var db = await contexts.CreateDbContextAsync(ct);
        var current = await (
                from publication in db.ReviewOperations.AsNoTracking()
                join revision in db.LabRevisions.AsNoTracking() on publication.RevisionId equals revision.Id
                where publication.LaboratoryId == labId
                      && publication.Number != null
                      && publication.Snapshot != null
                      && revision.Status == LabRevisionStatus.Published
                orderby publication.Number descending
                select new { publication.RevisionId, publication.Snapshot })
            .FirstOrDefaultAsync(ct);

        if (current is null || EvaluatorVersionOf(current.Snapshot!) is not { } evaluator)
        {
            return null;
        }

        return new RevisionInfo(current.RevisionId, evaluator);
    }

    public async Task<RevisionInfo?> FindForLabAsync(Guid labId, Guid revisionId, CancellationToken ct = default)
    {
        if (labId == Guid.Empty || revisionId == Guid.Empty)
        {
            return null;
        }

        await using var db = await contexts.CreateDbContextAsync(ct);
        byte[]? definition = await db.ReviewDefinitions.AsNoTracking()
            .Where(value => value.RevisionId == revisionId && value.LaboratoryId == labId)
            .Select(value => value.Content)
            .SingleOrDefaultAsync(ct);

        // Nacrt jos ne mora imati ispravan evaluator; za dodavanje prevoda dovoljan je ID revizije.
        return definition is null
            ? null
            : new RevisionInfo(revisionId, EvaluatorVersionOf(definition) ?? string.Empty);
    }

    private static string? EvaluatorVersionOf(byte[] definition)
    {
        try
        {
            return PublicationDefinition.Read(definition)?.Evaluator is { Kind: { Length: > 0 } kind, Version: > 0 } evaluator
                ? $"{kind}-v{evaluator.Version}"
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
