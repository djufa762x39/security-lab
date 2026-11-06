using Microsoft.AspNetCore.Mvc;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Services.Identity;

namespace Platform.Api.Controllers.Identity;

[ApiController]
[Route("api/auth/sessions")]
public class SessionController(IIdentityContext identity, ISessionManager sessionManager) : ControllerBase
{
    private readonly IIdentityContext _identity = identity;
    private readonly ISessionManager _sessionManager = sessionManager;

    [HttpGet]
    public async Task<IActionResult> GetMySessions(CancellationToken ct)
    {
        var caller = _identity.Current;

        if (caller == null || !caller.IsAuthenticated || caller.UserId == null)
            return Unauthorized(new { code = "unauthenticated" });

        var sessions = await _sessionManager.ListForUser(caller.UserId.Value, ct);

        Response.Headers.CacheControl = "no-store";

        return Ok(sessions);
    }
}
