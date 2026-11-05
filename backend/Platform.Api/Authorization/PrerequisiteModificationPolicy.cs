using Platform.Access.Contracts;
using Platform.Access.Domain.Models;
using Platform.Authoring.Application;

namespace Platform.Api.Authorization;

public sealed class PrerequisiteModificationPolicy(
    IAuthorDirectory authors) : IAccessPolicy
{
    public string Id => "prerequisites.author-only";
    public string Version => "v1";

    public ValueTask<bool> EvaluateAsync(
        AccessRequest request,
        CancellationToken cancellationToken = default)
    {
        var allowed =
            request.Subject is not null &&
            Guid.TryParse(request.Subject.Id, out var userId) &&
            userId != Guid.Empty &&
            authors.IsAuthor(userId);

        return ValueTask.FromResult(allowed);
    }
}