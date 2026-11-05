namespace Platform.Api.Interfaces.Identity;

public interface ILogoutService
{
    public Task ExecuteAsync(string? rawToken, string correlationId, CancellationToken ct = default);
}