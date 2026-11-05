using Platform.Access.AspNet;
using Microsoft.AspNetCore.Mvc;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;

namespace Platform.Api.Controllers.Identity;

public sealed record LoginRequest(string? Email, string? Password)
{
    public override string ToString() => "LoginRequest { *** }";
}

[ApiController]
[Route("api/auth")]
public sealed class LoginController(LoginAccount login) : ControllerBase
{
    [DelegatedAuthorization]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await login.ExecuteAsync(
            request.Email, request.Password, HttpContext.TraceIdentifier, cancellationToken);

        return result.Outcome switch
        {
            LoginOutcome.Success => Ok(new
            {
                tokenType = "Bearer",
                accessToken = result.AccessToken
            }),
            // IDN-05: isti odgovor za nepostojeći nalog, pogrešnu lozinku i zaključan nalog.
            LoginOutcome.InvalidCredentials => Unauthorized(new
            {
                code = "invalid_credentials",
                message = "Погрешан имејл или лозинка."
            }),
            LoginOutcome.AccountBlocked => StatusCode(StatusCodes.Status403Forbidden, new
            {
                code = "account_blocked",
                message = "Налог је блокиран."
            }),
            LoginOutcome.AccountArchived => StatusCode(StatusCodes.Status403Forbidden, new
            {
                code = "account_archived",
                message = "Налог је архивиран."
            }),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                code = "login_unavailable",
                message = "Пријава тренутно није доступна."
            })
        };
    }
}
