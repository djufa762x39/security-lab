using Platform.Access.AspNet;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.Identity.Services;

// Oba modula imaju IAccessDecision; ovaj endpoint koristi Access modul.
using IAccessDecision = Platform.Access.Contracts.IAccessDecision;

namespace Platform.Api.Endpoints.Identity;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        // CreateAccountAsync calls the platform access decision before registration.
        app.MapPost("/api/admin/accounts", CreateAccountAsync)
            .DelegatesToLegacyAuthorization();
        // UnlockAccountAsync calls the platform access decision before looking up the account.
        app.MapPost("/api/admin/accounts/{id:guid}/unlock", UnlockAccountAsync)
            .DelegatesToLegacyAuthorization();
        return app;
    }

    private static async Task<IResult> CreateAccountAsync(
        CreateAccountRequest? body,
        HttpContext context,
        IRequestSubjectResolver subjectResolver,
        IAccessDecision accessDecision,
        IAccountRegistration registration,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString("N");

        var decision = await accessDecision.DecideAsync(
            new AccessRequest(
                subjectResolver.Resolve(context),
                CreateAccountPolicy.ActionId,
                null,
                correlationId),
            cancellationToken);

        if (decision.Outcome != AccessOutcome.Allow)
        {
            return decision.DenialReason == AccessDenialReason.Unauthenticated
                ? Results.Json(new { error = "unauthenticated", correlationId },
                    statusCode: StatusCodes.Status401Unauthorized)
                : Results.Json(new { error = "forbidden", correlationId },
                    statusCode: StatusCodes.Status403Forbidden);
        }

        if (body is null)
        {
            return Results.BadRequest(new
            {
                error = "invalid_input",
                errors = new[] { "Telo zahteva je obavezno." },
                correlationId
            });
        }

        var result = await registration.RegisterAsync(
            new RegisterAccountCommand(body.Email ?? string.Empty, body.Password ?? string.Empty),
            cancellationToken);

        return result.Outcome switch
        {
            RegistrationOutcome.Created => Results.Created(
                $"/api/admin/accounts/{result.AccountId}",
                new { id = result.AccountId, correlationId }),
            RegistrationOutcome.IdentifierTaken => Results.Conflict(
                new { error = "identifier_taken", correlationId }),
            _ => Results.BadRequest(
                new { error = "invalid_input", errors = result.Errors, correlationId })
        };
    }

    // IDN-05: administratorsko otključavanje. Pravo se proverava pre traženja naloga,
    // pa neovlašćeni pozivalac ne saznaje da li nalog postoji.
    private static async Task<IResult> UnlockAccountAsync(
        Guid id,
        HttpContext context,
        IRequestSubjectResolver subjectResolver,
        IAccessDecision accessDecision,
        IAccountLockoutAdministration lockouts,
        IAuditWriter audit,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        const string auditAction = "account.lockout.cleared";
        var correlationId = Guid.NewGuid().ToString("N");
        var subject = subjectResolver.Resolve(context);
        Guid? actorId = Guid.TryParse(subject?.Id, out var parsed) ? parsed : null;

        void Write(string outcome, string? reasonCode) =>
            audit.Write(new AuditEvent(
                clock.GetUtcNow(), actorId, auditAction, id.ToString(), outcome, correlationId, reasonCode));

        var decision = await accessDecision.DecideAsync(
            new AccessRequest(subject, UnlockAccountPolicy.ActionId, null, correlationId),
            cancellationToken);

        if (decision.Outcome != AccessOutcome.Allow)
        {
            var unauthenticated = decision.DenialReason == AccessDenialReason.Unauthenticated;
            Write("denied", unauthenticated ? "unauthenticated" : "forbidden");

            return unauthenticated
                ? Results.Json(new { error = "unauthenticated", correlationId },
                    statusCode: StatusCodes.Status401Unauthorized)
                : Results.Json(new { error = "forbidden", correlationId },
                    statusCode: StatusCodes.Status403Forbidden);
        }

        var outcome = await lockouts.UnlockAsync(id, cancellationToken);
        if (outcome == UnlockOutcome.NotFound)
        {
            Write("failure", "not_found");
            return Results.NotFound(new { error = "account_not_found", correlationId });
        }

        Write("success", null);
        return Results.Ok(new { id, unlocked = outcome == UnlockOutcome.Unlocked, correlationId });
    }
}