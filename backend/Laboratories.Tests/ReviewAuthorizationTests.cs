
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Access.AspNet;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;
using Platform.Api.Enums.Identity;
using Platform.Api.Security.RevisionAuthorization;

namespace Laboratories.Tests;

public sealed class ReviewAuthorizationTests
{
    [TestCase(false, HttpStatusCode.Unauthorized)]
    [TestCase(true, HttpStatusCode.Forbidden)]
    public async Task Approve_WithoutRegisteredPolicy_DeniesBeforeDatabase(
        bool authenticated,
        HttpStatusCode expected)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Production");

            host.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRequestSubjectResolver>();
                services.RemoveAll<IAccessSubjectProvider>();
                services.RemoveAll<ISessionManager>();

                services.RemoveAll<IRevisionAuthorReader>();
                services.AddSingleton<IRevisionAuthorReader, TestRevisionAuthorReader>();
                var resolver = new TestResolver(authenticated);

                services.AddSingleton<IRequestSubjectResolver>(resolver);
                services.AddSingleton<IAccessSubjectProvider>(resolver);
                services.AddSingleton<ISessionManager, TestSessionManager>();
            });
        });

        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/revisions/{Guid.NewGuid()}/approve")
        {
            Content = JsonContent.Create(new
            {
                rowVersion = "2",
                contentHash = new string('a', 64)
            })
        };

        request.Headers.Add("Idempotency-Key", "test-request");

        if (authenticated)
        {
            request.Headers.Add(
                "Cookie",
                $"{SessionCookie.Name}=test-session");
        }

        using var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(expected));
    }

    private sealed class TestResolver(bool authenticated)
        : IRequestSubjectResolver, IAccessSubjectProvider
    {
        public SubjectRef? Resolve(
            Microsoft.AspNetCore.Http.HttpContext context) =>
            authenticated
                ? new SubjectRef(
                    "11111111-1111-1111-1111-111111111111",
                    AccountState.Active)
                : null;

        public ValueTask<SubjectRef?> GetSubjectAsync(
            Microsoft.AspNetCore.Http.HttpContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Resolve(context));
    }

    private sealed class TestSessionManager : ISessionManager
    {
        public Task<ValidateSession?> ValidateAsync(
            string token,
            CancellationToken ct = default)
        {
            ValidateSession? session = token == "test-session"
                ? new ValidateSession(
                    Guid.NewGuid(),
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    AuthenticationLevel.Active)
                : null;

            return Task.FromResult(session);
        }

        public Task<int> RevokeAllForUser(
            Guid userId,
            string reason,
            CancellationToken ct = default) =>
            Task.FromResult(0);

        public Task<bool> RevokeSession(
            Guid ownerUserId,
            Guid sessionHandle,
            CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<SessionInfo>> ListForUser(
            Guid userId,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SessionInfo>>(
                Array.Empty<SessionInfo>());
    }

    private sealed class TestRevisionAuthorReader : IRevisionAuthorReader
    {
    public Task<Guid?> FindAuthorIdAsync(
        Guid revisionId,
        CancellationToken cancellationToken = default)
    {
        Guid? authorId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        return Task.FromResult(authorId);
    }
    }
}
