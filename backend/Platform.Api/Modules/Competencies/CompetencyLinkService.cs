using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Competencies.Contracts;
using Platform.Competencies.Domain;
using Platform.LabVisibility.Contracts;
using AccessDecision = Platform.Access.Contracts.IAccessDecision;

namespace Platform.Api.Modules.Competencies;

// Call only with the user ID produced by Prg04SessionIdentity (not an HTTP field).
// All reads/writes of competency links happen after the concrete laboratory guard.
public sealed class CompetencyLinkService(
    ILaboratoryRepository laboratories,
    ICourseMembership memberships,
    AccessDecision access,
    IServiceProvider services)
{
    public async Task<CompetencyOperationResult> CatalogAsync(CancellationToken ct)
    {
        var catalog = services.GetService<ICompetencyCatalog>();
        return catalog is null
            ? new(CompetencyOperationStatus.StorageUnavailable)
            : new(CompetencyOperationStatus.Ok, await catalog.ListAsync(ct));
    }

    public async Task<CompetencyOperationResult> ReadAsync(Guid userId, Guid laboratoryId, CancellationToken ct)
    {
        var denied = await GuardAsync(userId, laboratoryId, edit: false, ct);
        if (denied is not null) return denied;
        var store = services.GetService<ILaboratoryCompetencyStore>();
        return store is null
            ? new(CompetencyOperationStatus.StorageUnavailable)
            : new(CompetencyOperationStatus.Ok, await store.GetForLaboratoryAsync(laboratoryId, ct));
    }

    public async Task<CompetencyOperationResult> ChangeAsync(
        Guid userId, Guid laboratoryId, Guid competencyId, bool add, CancellationToken ct)
    {
        var denied = await GuardAsync(userId, laboratoryId, edit: true, ct);
        if (denied is not null) return denied;
        if (competencyId == Guid.Empty)
            return new(CompetencyOperationStatus.InvalidCompetencyId);

        var catalog = services.GetService<ICompetencyCatalog>();
        var store = services.GetService<ILaboratoryCompetencyStore>();
        if (catalog is null || store is null)
            return new(CompetencyOperationStatus.StorageUnavailable);

        // The catalog is deliberately tiny and immutable in this PRG-04 increment.
        // Reuse its existing contract; no change to its interface or other consumers.
        if (!(await catalog.ListAsync(ct)).Any(x => x.Id == competencyId))
            return new(CompetencyOperationStatus.CompetencyNotFound);

        if (!add)
        {
            await store.RemoveAsync(laboratoryId, competencyId, ct);
            return new(CompetencyOperationStatus.NoContent);
        }

        try
        {
            bool created = await store.AddAsync(laboratoryId, competencyId, ct);
            return new(created ? CompetencyOperationStatus.Created : CompetencyOperationStatus.NoContent);
        }
        catch (KeyNotFoundException)
        {
            return new(CompetencyOperationStatus.CompetencyNotFound);
        }
    }

    private async Task<CompetencyOperationResult?> GuardAsync(
        Guid userId, Guid laboratoryId, bool edit, CancellationToken ct)
    {
        // Never grant anonymous/internal invalid identities access, even if this service
        // is accidentally called without its HTTP identity guard.
        if (userId == Guid.Empty)
            return new(CompetencyOperationStatus.LaboratoryNotFound);
        if (laboratoryId == Guid.Empty)
            return new(CompetencyOperationStatus.InvalidLaboratoryId);

        var laboratory = await laboratories.GetByIdAsync(laboratoryId, ct);
        if (laboratory is null || laboratory.Id != laboratoryId || laboratory.CourseId == Guid.Empty)
            return new(CompetencyOperationStatus.LaboratoryNotFound);

        if (!edit)
        {
            var role = await memberships.GetRoleAsync(laboratory.CourseId, userId, ct);
            return LabOpenPolicy.DecideOpen(laboratory.CourseId, role,
                laboratory.CreatedBy == userId).Allowed
                ? null : new(CompetencyOperationStatus.LaboratoryNotFound);
        }

        // Read access is deliberately NOT reused as permission to edit.
        var decision = await access.DecideAsync(new AccessRequest(
            new SubjectRef(userId.ToString("D"), AccountState.Active),
            CompetencyLinkActions.Manage,
            new ResourceRef("laboratory", laboratoryId.ToString("D")),
            Guid.NewGuid().ToString("N")), ct);
        return decision.Outcome == AccessOutcome.Allow
            ? null : new(CompetencyOperationStatus.LaboratoryNotFound);
    }
}

