using Platform.Competencies.Domain;

namespace Platform.Competencies.Contracts;

// Storage only. The PRG-04 application boundary must check laboratory existence and
// authorize the caller before invoking these methods (story 2); this port is not an API.
public interface ILaboratoryCompetencyStore
{
    // Returns false if the relationship already exists; unknown competencies are rejected.
    Task<bool> AddAsync(Guid laboratoryId, Guid competencyId, CancellationToken cancellationToken = default);
    // Removes only this relationship, not the competency or any other laboratory's links.
    Task<bool> RemoveAsync(Guid laboratoryId, Guid competencyId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Competency>> GetForLaboratoryAsync(Guid laboratoryId, CancellationToken cancellationToken = default);
}
