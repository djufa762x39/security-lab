using Platform.Api.Models.Identity;

namespace Platform.Api.Interfaces.Identity;

public interface ILoginService
{
    public Task<LoginResult> ExecuteAsync(string? email, string? password, string correlationId,
        CancellationToken ct = default);
}