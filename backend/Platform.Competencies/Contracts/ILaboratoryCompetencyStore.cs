using Platform.Competencies.Domain;

namespace Platform.Competencies.Contracts;

// Storage only. It neither resolves a laboratory nor authenticates or authorizes a caller.
// The current PRG-04 HTTP boundary checks identity only; existence and per-laboratory
// authorization are deliberately deferred to a later story.
public interface ILaboratoryCompetencyStore
{
    // Returns false if the relationship already exists; unknown competencies are rejected.
    Task<bool> AddAsync(Guid laboratoryId, Guid competencyId, CancellationToken cancellationToken = default);
    // Removes only this relationship, not the competency or any other laboratory's links.
    Task<bool> RemoveAsync(Guid laboratoryId, Guid competencyId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Competency>> GetForLaboratoryAsync(Guid laboratoryId, CancellationToken cancellationToken = default);
}
