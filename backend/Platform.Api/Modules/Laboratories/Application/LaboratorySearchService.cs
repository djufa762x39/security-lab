using Platform.Api.Modules.Laboratories.Domain;
using Platform.LabMetadata;
using Platform.LaboratoryPersistence.Application;

namespace Platform.Api.Modules.Laboratories.Application;

public enum LabSearchStatus
{
    Ok,
    InvalidQuery,
    Unauthenticated,
    FiltersUnavailable
}

public sealed record LabSearchResult(LabSearchStatus Status, IReadOnlyList<Laboratory> Items, bool Truncated)
{
    public static LabSearchResult Ok(IReadOnlyList<Laboratory> items, bool truncated) =>
        new(LabSearchStatus.Ok, items, truncated);

    public static readonly LabSearchResult InvalidQuery = new(LabSearchStatus.InvalidQuery, [], false);
    public static readonly LabSearchResult Unauthenticated = new(LabSearchStatus.Unauthenticated, [], false);
    public static readonly LabSearchResult FiltersUnavailable = new(LabSearchStatus.FiltersUnavailable, [], false);
}

public sealed class LaboratorySearchService(
    ILaboratoryRepository repository,
    IAccessDecision accessDecision,
    ILogger<LaboratorySearchService> logger,
    ILaboratoryMetadataStore? metadataStore = null)
{
    public const int MaxResults = 50;

    public async Task<LabSearchResult> SearchAsync(
        string? query, IIdentityContext caller, CancellationToken cancellationToken,
        LabSearchFilters? filters = null)
    {
        Guid? callerId = CallerId.From(caller);
        if (callerId is null)
        {
            return LabSearchResult.Unauthenticated;
        }

        // An empty title is a catalog search, needed for filters-only searches and reset.
        if ((query?.Any(char.IsControl) ?? false)
            || !(filters ?? new LabSearchFilters()).TryNormalize(out LabSearchFilters normalizedFilters))
        {
            return LabSearchResult.InvalidQuery;
        }

        string key = string.Empty;
        if (!string.IsNullOrWhiteSpace(query))
        {
            if (!LabTitleSearch.TryCreateKey(query, out string? titleKey))
                return LabSearchResult.InvalidQuery;
            key = titleKey;
        }
        if (normalizedFilters.HasValues && metadataStore is null)
        {
            return LabSearchResult.FiltersUnavailable;
        }

        IReadOnlyList<Laboratory> laboratories = await repository.ListAsync(cancellationToken);

        var candidates = new List<(Laboratory Lab, string Folded)>();
        foreach (Laboratory laboratory in laboratories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Authorize before reading metadata, matching filters or counting results.
            if (!CanRead(caller, callerId.Value, laboratory)) continue;
            string folded = LabTitleSearch.Fold(laboratory.Title);
            if (!folded.Contains(key, StringComparison.Ordinal)) continue;

            if (normalizedFilters.HasValues)
            {
                LaboratoryMetadata? metadata = await metadataStore!.GetAsync(laboratory.Id, cancellationToken);
                if (metadata is null || !normalizedFilters.Matches(metadata)) continue;
            }

            candidates.Add((laboratory, folded));
        }

        List<Laboratory> matches = candidates
            .OrderBy(x => x.Folded.StartsWith(key, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(x => x.Folded, StringComparer.Ordinal)
            .ThenBy(x => x.Lab.CreatedAt)
            .ThenBy(x => x.Lab.Id)
            .Take(MaxResults + 1)
            .Select(x => x.Lab)
            .ToList();

        bool truncated = matches.Count > MaxResults;
        if (truncated)
        {
            matches.RemoveAt(MaxResults);
        }

        logger.LogInformation(
            "Laboratory search: by={CallerId} queryLength={QueryLength} results={ResultCount} truncated={Truncated}",
            callerId.Value, key.Length, matches.Count, truncated);

        return LabSearchResult.Ok(matches, truncated);
    }

    public LabSearchFilterOptions GetFilterOptions(IIdentityContext caller)
    {
        if (CallerId.From(caller) is null)
            return new(LabSearchStatus.Unauthenticated, [], [], false);

        // Ovo su javne enum vrednosti, bez metapodataka konkretnih laboratorija.
        return new(LabSearchStatus.Ok, Enum.GetNames<LaboratoryDifficulty>(),
            Enum.GetNames<LaboratoryType>(), metadataStore is not null);
    }

    private bool CanRead(IIdentityContext caller, Guid callerId, Laboratory laboratory) =>
        LaboratoryVisibility.IsVisibleTo(laboratory, callerId)
        && accessDecision.Decide(caller, LabActions.Read, laboratory.Id.ToString()).Allowed;
}
