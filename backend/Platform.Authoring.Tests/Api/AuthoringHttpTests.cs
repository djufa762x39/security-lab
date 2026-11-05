using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Platform.Authoring.Application;

using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Authoring.Tests.Fakes;

using Microsoft.Extensions.Configuration;

namespace Platform.Authoring.Tests.Api;

[TestFixture]
public sealed class AuthoringHttpRequests
{
    [Test]
    public async Task Assistant_CreatesDraftFromExistingTemplate()
    {
        var templateId = Guid.Parse("a1000000-0000-4000-8000-000000000001");
        var assistantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        
        using var baseFactory = new AuthoringHostFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            //supply the caller ID thorugh the configuration read by DevelopmentSubjectResolver
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Identity:DevSubjectId"] = assistantId.ToString("D")
                });
            });
        });
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/authoring/templates/{templateId}/drafts");

        using var response = await client.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var draft = body.RootElement;
        var draftId = draft.GetProperty("id").GetGuid();

        //check the same repository that DraftService writes to
        var repository = factory.Services.GetRequiredService<IDraftRepository>();
        var storedDraft = await repository.GetAsync(draftId);

        Assert.Multiple(() =>
        {
            Assert.That(draftId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(draftId, Is.Not.EqualTo(templateId));
            Assert.That(draft.GetProperty("sourceTemplateId").GetGuid(), Is.EqualTo(templateId));
            Assert.That(draft.GetProperty("ownerId").GetGuid(), Is.EqualTo(assistantId));
            Assert.That(draft.GetProperty("title").GetString(), Is.EqualTo("Access control and ownership"));
            Assert.That(storedDraft, Is.Not.Null);
        });
    }

    [Test]
    public async Task Assistant_RequestingUnknownTemplate_404WithoutCreatingDraft()
    {
        var repository = new InMemoryDraftRepository();
        var unknownTemplateId = Guid.Parse("99999999-9999-4999-8999-999999999999");

        using var baseFactory = new AuthoringHostFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            //use the same authorized caller as in the successful creation test
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Identity:DevSubjectId"] = "11111111-1111-1111-1111-111111111111"
                });
            });

            //Replace API"s draft repository with the existing Authoring test fake
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDraftRepository>();
                services.AddSingleton<IDraftRepository>(repository);
            });
        });

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/authoring/templates/{unknownTemplateId}/drafts");

        using var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(repository.Count, Is.Zero);
    }

    [Test]
    public async Task Student_RequestingExistingTemplate_403WithoutCreatingDraft()
    {
        var repository = new InMemoryDraftRepository();
        var templateId = Guid.Parse("a1000000-0000-4000-8000-000000000001");

        using var baseFactory = new AuthoringHostFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            //resolve the student as a known caller so the access policy can deny the action
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Identity:DevSubjectId"] = "22222222-2222-2222-2222-222222222222"
                });
            });

            //use the test repository to check whether a draft is stored
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDraftRepository>();
                services.AddSingleton<IDraftRepository>(repository);
            });
        });

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/authoring/templates/{templateId}/drafts");

        using var response = await client.SendAsync(request);
        
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(repository.Count, Is.Zero);
    }

    private sealed class AuthoringHostFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            //program registers the AUT-06 route and its adapters in Development
            builder.UseEnvironment("Development");   
        }
    }
}